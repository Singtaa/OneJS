using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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
    /// nothing can find them by reading source. Instead the app's build writes
    /// `*.sl.json` beside its bundle, holding a hash and the generated HLSL per
    /// program, and this turns each entry into a `.shader`. That keeps the
    /// generator ignorant of JavaScript and keeps the emitter, which is the part
    /// with the interesting logic, in TypeScript where it is unit tested.
    /// </summary>
    public static class SLShaderGenerator {
        /// <summary>Where generated shaders go. Deliberately NOT a Resources folder.</summary>
        public const string OutputDir = "Assets/OneJS.Generated/Shaders";
        const string CommonSource = "Packages/com.singtaa.onejs/Resources/OneJS/SLCommon.cginc";
        const string CommonFallback = "Assets/Singtaa/OneJS/Resources/OneJS/SLCommon.cginc";

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
                    ? "No shader programs found.\n\nAn app declares them by writing a *.sl.json manifest " +
                      "beside its bundle during its build."
                    : $"Generated {n} shader program{(n == 1 ? "" : "s")} into {OutputDir}.",
                "OK");
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

            // The generated shaders include the shared helpers, and both backends
            // must include the SAME file or they can drift on what noise means.
            // Copying it beside them keeps the include a plain relative path,
            // which resolves the same way on every Unity version.
            var common = File.Exists(Path.Combine(root, CommonSource.Replace('/', Path.DirectorySeparatorChar)))
                ? CommonSource : CommonFallback;
            var commonAbs = Path.Combine(root, common.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(commonAbs)) {
                Debug.LogError(
                    "[OneJS sl] SLCommon.cginc is missing, so generated shaders cannot compile. " +
                    "Both backends include it, and without it a program would run on the site and " +
                    "fail to build after an eject.");
                return 0;
            }
            CopyIfDifferent(commonAbs, Path.Combine(outAbs, "SLCommon.cginc"));

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
