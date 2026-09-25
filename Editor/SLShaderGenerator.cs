using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using OneJS.SL;
using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Turns the shader programs an app declares into compiled shader assets.
    ///
    /// In a project with an editor a program becomes real HLSL, and Unity
    /// compiles it like any other asset. The author changes nothing; the runtime
    /// picks the compiled shader by program hash, and draws nothing until there
    /// is one.
    ///
    /// A manifest, not a scan. A `*.sl.json` manifest holds a hash and the
    /// generated HLSL per program, and this turns each entry into a `.shader`.
    /// That keeps the generator ignorant of JavaScript and keeps the emitter,
    /// which is the part with the interesting logic, in TypeScript where it is
    /// unit tested.
    ///
    /// Who writes the manifests. The build of a `.sl` file writes app.sl.json
    /// beside the bundle, and this generates from it before every load
    /// (<see cref="GenerateBeside"/>), so such a program is compiled from its
    /// first frame. A program built in code is known only once it runs: the
    /// editor sees it has no shader, the host hands its HLSL to
    /// <see cref="Record"/>, and the next editor update appends it to
    /// <see cref="RecordedManifest"/>, generates the shader and gives the live
    /// element a material, so it is blank for a frame and compiled after.
    /// JSPad's manifest is under Temp, so its build records it here as well
    /// (<see cref="RecordManifest"/>).
    ///
    /// A player gets the same shaders from <see cref="SLShaderBuildStep"/>,
    /// which generates from every manifest again and writes the registry that
    /// makes the build pack them (<see cref="SLShaderRegistry"/>).
    /// </summary>
    public static class SLShaderGenerator {
        /// <summary>
        /// Where generated shaders go. Deliberately NOT a Resources folder: a
        /// player ships only the ones the registry names, not every shader an
        /// editor ever generated. Derived from the manifests, so a project need
        /// not commit it.
        /// </summary>
        public const string OutputDir = "Assets/OneJS.Generated/Shaders";
        /// <summary>
        /// The manifest the running app writes, one entry per program it
        /// interpreted. Outside <see cref="OutputDir"/> because it is a source,
        /// not a product: a program built in code is known only from this file,
        /// so it has to be committed for a teammate's build or CI to ship it.
        /// </summary>
        public const string RecordedManifest = "Assets/OneJS/Recorded.sl.json";
        /// <summary>Where <see cref="RecordedManifest"/> lived before, inside the folder projects ignore.</summary>
        public const string LegacyRecordedManifest = OutputDir + "/Recorded.sl.json";
        /// <summary>The registry a player loads its generated shaders from (<see cref="SLShaderRegistry"/>).</summary>
        public const string RegistryAsset = "Assets/OneJS.Generated/Resources/" + SLShaderRegistry.ResourcePath + ".asset";
        /// <summary>The include every generated shader starts from.</summary>
        public const string RootInclude = "SLCommon.cginc";
        const string PackageDir = "Packages/com.singtaa.onejs/Resources/OneJS";
        const string AssetsDir = "Assets/Singtaa/OneJS/Resources/OneJS";

        /// <summary>
        /// Every include a generated shader needs, copied beside it: the root
        /// and everything it includes, transitively, read from the files.
        ///
        /// Read rather than listed. A list held SLCommon and SDF2D, and when
        /// Noise2D was added beside them the list was not, so every shader an
        /// eject generated compiled to nothing and rendered magenta, which is
        /// the failure the eject path can least afford: it looks like a broken
        /// game rather than a broken build step. The test that should have
        /// caught it kept a list of its own.
        /// </summary>
        public static string[] Includes() {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var ordered = new List<string>();
            var pending = new Queue<string>();
            pending.Enqueue(RootInclude);
            while (pending.Count > 0) {
                var name = pending.Dequeue();
                if (ordered.Contains(name)) continue;
                ordered.Add(name);
                var from = IncludeSource(root, name);
                if (from == null) continue;
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(from), @"#include\s+""([^""]+)""")) {
                    pending.Enqueue(m.Groups[1].Value);
                }
            }
            return ordered.ToArray();
        }

        static string IncludeSource(string root, string name) {
            var pkg = Path.Combine(root, Path.Combine(PackageDir, name).Replace('/', Path.DirectorySeparatorChar));
            var loc = Path.Combine(root, Path.Combine(AssetsDir, name).Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(pkg) ? pkg : File.Exists(loc) ? loc : null;
        }

        [Serializable]
        class Entry {
            public string hash;
            public string hlsl;
        }

        [Serializable]
        class Manifest {
            public Entry[] programs;
        }

        [MenuItem("Tools/OneJS/Generate Shader Programs")]
        public static void GenerateAll() {
            int n = Generate(FindManifests());
            EditorUtility.DisplayDialog("OneJS",
                n == 0
                    ? "No shader programs found.\n\nPrograms are recorded when the app runs: run it once " +
                      "in the editor, or write a *.sl.json manifest beside its bundle."
                    : $"Generated {n} shader program{(n == 1 ? "" : "s")} into {OutputDir}.",
                "OK");
        }

        // MARK: recording

        static readonly Dictionary<string, string> s_Pending = new Dictionary<string, string>();

        [InitializeOnLoadMethod]
        static void AttachRecorder() {
            SLProgramBridge.SourceRecorder = Record;
            EditorApplication.update -= FlushOnUpdate;
            EditorApplication.update += FlushOnUpdate;
            JSRunner.EditorLoadingBundle -= GenerateBeside;
            JSRunner.EditorLoadingBundle += GenerateBeside;
        }

        /// <summary>
        /// Takes a program the editor has just seen with no compiled shader.
        /// Flushed on the next editor update rather than acted on here: this is
        /// called from inside a React commit, and an asset import from there
        /// would stall the frame that is still being built.
        ///
        /// The update rather than `delayCall`, which is what this used: an
        /// unfocused editor left a delayCall pending for as long as nobody
        /// clicked on it, and the program drew nothing all that time.
        /// </summary>
        public static void Record(string hash, string hlsl) {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(hlsl)) return;
            s_Pending[hash] = hlsl;
        }

        /// <summary>True while a program handed to <see cref="Record"/> has not been generated.</summary>
        public static bool HasPendingRecordings => s_Pending.Count > 0;

        static void FlushOnUpdate() {
            if (s_Pending.Count > 0) FlushRecorded();
        }

        /// <summary>
        /// Generates the shaders of the manifest a build wrote beside a bundle,
        /// before the bundle runs. Attached to <see cref="JSRunner.EditorLoadingBundle"/>.
        ///
        /// Generated here rather than left to the manifest's import. The watcher
        /// rewrites app.sl.json with every build, and the reload that follows
        /// reads the bundle straight from disk, so nothing imported the manifest
        /// until the editor next refreshed, and every program new in that build
        /// drew nothing until then. The files are compared first, so a reload
        /// that changed no program costs a read and no import.
        /// </summary>
        public static void GenerateBeside(string bundlePath) {
            var dir = Path.GetDirectoryName(bundlePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            var manifests = Directory.GetFiles(dir, "*.sl.json", SearchOption.TopDirectoryOnly);
            if (manifests.Length == 0) return;
            Generate(manifests);
            SLProgramBridge.AdoptGenerated();
        }

        /// <summary>
        /// Records every program in a manifest the project cannot see, and
        /// returns how many were new. For JSPad, whose build writes its manifest
        /// under Temp: recorded, its programs are generated now and ship in a
        /// player like any program the editor has drawn.
        /// </summary>
        public static int RecordManifest(string manifestPath) {
            foreach (var kv in ReadEntriesAt(manifestPath)) {
                if (!s_Pending.ContainsKey(kv.Key)) s_Pending[kv.Key] = kv.Value;
            }
            return FlushRecorded();
        }

        /// <summary>
        /// Writes pending programs into the recorded manifest, generates their
        /// shaders and moves live materials onto them. Returns how many programs
        /// were new to the manifest. Public so a test can drive it synchronously.
        /// </summary>
        public static int FlushRecorded() {
            if (s_Pending.Count == 0) return 0;
            var pending = new Dictionary<string, string>(s_Pending);
            s_Pending.Clear();

            MigrateRecorded();
            var entries = ReadEntries(RecordedManifest);
            int added = 0;
            foreach (var kv in pending) {
                if (entries.ContainsKey(kv.Key)) continue;
                entries[kv.Key] = kv.Value;
                added++;
            }
            if (added > 0) WriteEntries(RecordedManifest, entries);
            Generate(FindManifests());
            int adopted = SLProgramBridge.AdoptGenerated();
            if (added > 0) {
                Debug.Log($"[OneJS sl] recorded {added} shader program{(added == 1 ? "" : "s")} into " +
                          $"{RecordedManifest}; {adopted} now running compiled.");
            }
            return added;
        }

        /// <summary>
        /// Moves a recorded manifest from where older versions wrote it, inside
        /// the ignored folder, to <see cref="RecordedManifest"/>, merging when
        /// both exist. Returns true when there was one to move.
        /// </summary>
        public static bool MigrateRecorded() {
            if (!File.Exists(Abs(LegacyRecordedManifest))) return false;
            var entries = ReadEntries(RecordedManifest);
            foreach (var kv in ReadEntries(LegacyRecordedManifest)) {
                if (!entries.ContainsKey(kv.Key)) entries[kv.Key] = kv.Value;
            }
            WriteEntries(RecordedManifest, entries);
            if (!AssetDatabase.DeleteAsset(LegacyRecordedManifest)) File.Delete(Abs(LegacyRecordedManifest));
            AssetDatabase.ImportAsset(RecordedManifest, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[OneJS sl] moved {LegacyRecordedManifest} to {RecordedManifest}, where it can be committed " +
                      "so every build of this project ships the programs it lists.");
            return true;
        }

        static string Abs(string assetPath) =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                assetPath.Replace('/', Path.DirectorySeparatorChar));

        static Dictionary<string, string> ReadEntries(string assetPath) => ReadEntriesAt(Abs(assetPath));

        static Dictionary<string, string> ReadEntriesAt(string file) {
            var entries = new Dictionary<string, string>();
            if (!File.Exists(file)) return entries;
            try {
                var existing = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                if (existing?.programs != null) {
                    foreach (var e in existing.programs) {
                        if (!string.IsNullOrEmpty(e.hash) && !string.IsNullOrEmpty(e.hlsl)) entries[e.hash] = e.hlsl;
                    }
                }
            } catch (Exception e) {
                Debug.LogWarning($"[OneJS sl] could not read {file}, so its programs are left out: {e.Message}");
            }
            return entries;
        }

        static void WriteEntries(string assetPath, Dictionary<string, string> entries) {
            // Sorted by hash so the file does not churn with run order.
            var list = new List<Entry>();
            foreach (var kv in entries) list.Add(new Entry { hash = kv.Key, hlsl = kv.Value });
            list.Sort((a, b) => string.CompareOrdinal(a.hash, b.hash));
            var file = Abs(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            CopyTextIfDifferent(JsonUtility.ToJson(new Manifest { programs = list.ToArray() }, true), file);
        }

        public static string[] FindManifests() {
            var found = new List<string>();
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var dir in new[] { "Assets", "Packages" }) {
                var full = Path.Combine(root, dir);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.GetFiles(full, "*.sl.json", SearchOption.AllDirectories)) {
                    // A `~` folder is invisible to Unity, and a shader generated
                    // from one would reference an asset the editor cannot see.
                    if (f.Contains("~" + Path.DirectorySeparatorChar)) continue;
                    found.Add(f);
                }
            }
            return found.ToArray();
        }

        /// <summary>
        /// Writes a shader per program and returns how many.
        ///
        /// Existing files are compared before writing. Rewriting an identical
        /// shader would dirty the asset and force Unity to recompile it, which
        /// on a project with many programs turns every import into a stall for
        /// no change at all.
        /// </summary>
        public static int Generate(string[] manifestPaths) => GenerateHashes(manifestPaths).Length;

        /// <summary>The same, returning the hash of every program, sorted.</summary>
        public static string[] GenerateHashes(string[] manifestPaths) {
            if (manifestPaths.Length == 0) return new string[0];
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outAbs = Path.Combine(root, OutputDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(outAbs);

            // The generated shaders include the shared helpers, and both
            // backends must include the SAME files or they can drift on what
            // noise means. Copying them beside the output keeps every include a
            // plain relative path, which resolves the same way on every Unity
            // version.
            foreach (var inc in Includes()) {
                var from = IncludeSource(root, inc);
                if (from == null) {
                    Debug.LogError(
                        $"[OneJS sl] {inc} is missing, so generated shaders cannot compile and would " +
                        "render magenta. A program would run on the site and break after an eject.");
                    return new string[0];
                }
                CopyIfDifferent(from, Path.Combine(outAbs, inc));
            }

            int count = 0;
            var seen = new HashSet<string>();
            foreach (var path in manifestPaths) {
                Manifest m;
                try {
                    m = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                } catch (Exception e) {
                    Debug.LogError($"[OneJS sl] could not read the program manifest at {path}: {e.Message}");
                    continue;
                }
                if (m?.programs == null) continue;
                foreach (var p in m.programs) {
                    if (string.IsNullOrEmpty(p.hash) || string.IsNullOrEmpty(p.hlsl)) continue;
                    // Two programs with the same hash ARE the same program, so
                    // the second is not a conflict, it is a duplicate.
                    if (!seen.Add(p.hash)) continue;
                    if (CopyTextIfDifferent(p.hlsl, Path.Combine(outAbs, p.hash + ".shader"))) count++;
                }
            }

            if (count > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var hashes = new List<string>(seen);
            hashes.Sort(string.CompareOrdinal);
            return hashes.ToArray();
        }

        // MARK: the registry a player loads

        /// <summary>
        /// Writes <see cref="RegistryAsset"/> naming the generated shader of each
        /// program, and returns how many it names.
        ///
        /// A shader that did not import, or imported with errors, is left out and
        /// said so: a player then draws that program on the VM, which is slower
        /// and still right, where a registered broken shader would draw magenta.
        /// </summary>
        public static int WriteRegistry(IEnumerable<string> hashes) {
            var entries = new List<SLShaderRegistry.Entry>();
            foreach (var hash in hashes) {
                var path = OutputDir + "/" + hash + ".shader";
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null && File.Exists(Abs(path))) {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                }
                if (shader == null) {
                    Debug.LogError($"[OneJS sl] {path} did not import, so program {hash} draws on the VM in this player.");
                    continue;
                }
                if (ShaderUtil.ShaderHasError(shader)) {
                    Debug.LogError($"[OneJS sl] {path} has compile errors, so program {hash} draws on the VM in this " +
                                   "player. Select the shader to see them.");
                    continue;
                }
                entries.Add(new SLShaderRegistry.Entry { hash = hash, shader = shader });
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.hash, b.hash));

            var registry = AssetDatabase.LoadAssetAtPath<SLShaderRegistry>(RegistryAsset);
            if (registry == null) {
                Directory.CreateDirectory(Path.GetDirectoryName(Abs(RegistryAsset)));
                registry = ScriptableObject.CreateInstance<SLShaderRegistry>();
                registry.SetEntries(entries.ToArray());
                AssetDatabase.CreateAsset(registry, RegistryAsset);
            } else {
                registry.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(registry);
            }
            AssetDatabase.SaveAssetIfDirty(registry);
            SLProgramBridge.ReloadRegistry();
            return entries.Count;
        }

        /// <summary>Removes the registry, so a build packs no generated shader.</summary>
        public static void DeleteRegistry() {
            if (File.Exists(Abs(RegistryAsset))) AssetDatabase.DeleteAsset(RegistryAsset);
            SLProgramBridge.ReloadRegistry();
        }

        static void CopyIfDifferent(string from, string to) {
            CopyTextIfDifferent(File.ReadAllText(from), to);
        }

        static bool CopyTextIfDifferent(string text, string to) {
            if (File.Exists(to) && File.ReadAllText(to) == text) return false;
            File.WriteAllText(to, text);
            return true;
        }
    }

    /// <summary>
    /// Ships a compiled shader for every program the project knows in each
    /// native player.
    ///
    /// Generates from every `*.sl.json` again rather than trusting what the
    /// editor last left in <see cref="SLShaderGenerator.OutputDir"/>, because a
    /// clean checkout or a CI machine has none of it: the manifests are the
    /// source, and the shaders and the registry are rebuilt from them here.
    ///
    /// Not for WebGL, which draws programs compiled by the page
    /// (<see cref="SLProgramBridge.CompiledOnly"/>): there the registry is
    /// removed, so the build packs no generated shader and that path is
    /// unchanged.
    /// </summary>
    public class SLShaderBuildStep : IPreprocessBuildWithReport {
        // After JSRunnerBuildProcessor's 0, which copies app bundles, although
        // nothing here reads what it writes.
        public int callbackOrder => 1;

        public void OnPreprocessBuild(BuildReport report) => Prepare(report.summary.platform);

        /// <summary>
        /// What a build for `target` does, callable without one. Returns how many
        /// programs the registry names: 0 for WebGL, which gets none.
        /// </summary>
        public static int Prepare(BuildTarget target) {
            if (target == BuildTarget.WebGL) {
                SLShaderGenerator.DeleteRegistry();
                return 0;
            }
            SLShaderGenerator.MigrateRecorded();
            var manifests = SLShaderGenerator.FindManifests();
            var hashes = SLShaderGenerator.GenerateHashes(manifests);
            int shipped = SLShaderGenerator.WriteRegistry(hashes);
            Debug.Log($"[OneJS sl] {shipped} of {hashes.Length} shader program{(hashes.Length == 1 ? "" : "s")} " +
                      $"from {manifests.Length} manifest{(manifests.Length == 1 ? "" : "s")} ship compiled in this player.");
            return shipped;
        }
    }

    /// <summary>
    /// Regenerates when a manifest is imported, so an ejected project gets its
    /// compiled shaders without anybody knowing to ask for them.
    ///
    /// That silence is deliberate and is also the one hazard: a project where
    /// generation quietly failed looks exactly like one where it worked, only
    /// slower. SLProgramBridge.IsNative exists so a test can tell the difference.
    /// </summary>
    public class SLShaderPostprocessor : AssetPostprocessor {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            foreach (var path in imported) {
                if (!path.EndsWith(".sl.json", StringComparison.OrdinalIgnoreCase)) continue;
                SLShaderGenerator.Generate(SLShaderGenerator.FindManifests());
                // A program already on screen and waiting for its shader draws
                // from this frame on, rather than from the next reload.
                SLProgramBridge.AdoptGenerated();
                return;
            }
        }
    }
}
