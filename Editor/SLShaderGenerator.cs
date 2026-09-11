using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using OneJS.SL;
using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Turns the shader programs an app declares into compiled shader assets.
    ///
    /// This is what makes ejecting worth anything. On play.onejs.com a program is
    /// interpreted, because a prebuilt container cannot compile a shader. In a
    /// project with an editor the same program becomes real HLSL, and Unity
    /// compiles it like any other asset. The author changes nothing; the runtime
    /// picks the compiled shader by program hash and falls back to the VM when
    /// there is none.
    ///
    /// A manifest, not a scan. The programs are recorded when JavaScript runs, so
    /// nothing can find them by reading source. A `*.sl.json` manifest holds a
    /// hash and the generated HLSL per program, and this turns each entry into a
    /// `.shader`. That keeps the generator ignorant of JavaScript and keeps the
    /// emitter, which is the part with the interesting logic, in TypeScript
    /// where it is unit tested.
    ///
    /// Who writes the manifest: the running app, through this class. When the
    /// runtime interprets a program in the editor it hands the HLSL to
    /// <see cref="Record"/>, which appends it to <see cref="RecordedManifest"/>,
    /// generates the shader and moves the live material onto it. So the first
    /// run of an ejected game is interpreted and every run after is compiled,
    /// with nobody writing a file by hand. An app can still ship its own
    /// manifest (`manifest()` in onejs-unity/sl) beside its bundle; both are
    /// read.
    /// </summary>
    public static class SLShaderGenerator {
        /// <summary>Where generated shaders go. Deliberately NOT a Resources folder.</summary>
        public const string OutputDir = "Assets/OneJS.Generated/Shaders";
        /// <summary>The manifest the running app writes, one entry per program it interpreted.</summary>
        public const string RecordedManifest = OutputDir + "/Recorded.sl.json";
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
        static bool s_FlushScheduled;

        [InitializeOnLoadMethod]
        static void AttachRecorder() {
            SLProgramBridge.SourceRecorder = Record;
        }

        /// <summary>
        /// Takes a program the runtime just interpreted. Batched and flushed on
        /// the next editor tick rather than acted on here: this is called from
        /// inside a React commit, and an asset import from there would stall
        /// the frame that is still being built.
        /// </summary>
        public static void Record(string hash, string hlsl) {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(hlsl)) return;
            s_Pending[hash] = hlsl;
            if (s_FlushScheduled) return;
            s_FlushScheduled = true;
            EditorApplication.delayCall += () => FlushRecorded();
        }

        /// <summary>
        /// Writes pending programs into the recorded manifest, generates their
        /// shaders and moves live materials onto them. Returns how many programs
        /// were new to the manifest. Public so a test can drive it synchronously.
        /// </summary>
        public static int FlushRecorded() {
            s_FlushScheduled = false;
            if (s_Pending.Count == 0) return 0;
            var pending = new Dictionary<string, string>(s_Pending);
            s_Pending.Clear();

            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var file = Path.Combine(root, RecordedManifest.Replace('/', Path.DirectorySeparatorChar));
            var entries = new Dictionary<string, string>();
            if (File.Exists(file)) {
                try {
                    var existing = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                    if (existing?.programs != null) {
                        foreach (var e in existing.programs) {
                            if (!string.IsNullOrEmpty(e.hash) && !string.IsNullOrEmpty(e.hlsl)) entries[e.hash] = e.hlsl;
                        }
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[OneJS sl] rewriting an unreadable {RecordedManifest}: {e.Message}");
                }
            }
            int added = 0;
            foreach (var kv in pending) {
                if (entries.ContainsKey(kv.Key)) continue;
                entries[kv.Key] = kv.Value;
                added++;
            }
            if (added > 0) {
                // Sorted by hash so the file does not churn with run order.
                var list = new List<Entry>();
                foreach (var kv in entries) list.Add(new Entry { hash = kv.Key, hlsl = kv.Value });
                list.Sort((a, b) => string.CompareOrdinal(a.hash, b.hash));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                CopyTextIfDifferent(JsonUtility.ToJson(new Manifest { programs = list.ToArray() }, true), file);
            }
            Generate(FindManifests());
            int adopted = SLProgramBridge.AdoptGenerated();
            if (added > 0) {
                Debug.Log($"[OneJS sl] recorded {added} shader program{(added == 1 ? "" : "s")} into " +
                          $"{RecordedManifest}; {adopted} now running compiled.");
            }
            return added;
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
        public static int Generate(string[] manifestPaths) {
            if (manifestPaths.Length == 0) return 0;
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
                    return 0;
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
            return seen.Count;
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
                return;
            }
        }
    }
}
