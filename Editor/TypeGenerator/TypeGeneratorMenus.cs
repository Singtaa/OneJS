using UnityEditor;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Menu items for quick type generation operations.
    /// </summary>
    public static class TypeGeneratorMenus {
        private const string OutputDir = "Assets/Gen/Typings/csharp";

        #region Generate Presets

        [MenuItem("OneJS/Generate Typings/Unity Core", false, 100)]
        public static void GenerateUnityCore() {
            var path = $"{OutputDir}/unity-core.d.ts";
            TypeGenerator.Presets.UnityCore.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated Unity Core types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/UI Toolkit", false, 101)]
        public static void GenerateUIToolkit() {
            var path = $"{OutputDir}/uitoolkit.d.ts";
            TypeGenerator.Presets.UIToolkit.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated UI Toolkit types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/Physics", false, 102)]
        public static void GeneratePhysics() {
            var path = $"{OutputDir}/physics.d.ts";
            TypeGenerator.Presets.Physics.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated Physics types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/Animation", false, 103)]
        public static void GenerateAnimation() {
            var path = $"{OutputDir}/animation.d.ts";
            TypeGenerator.Presets.Animation.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated Animation types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/Audio", false, 104)]
        public static void GenerateAudio() {
            var path = $"{OutputDir}/audio.d.ts";
            TypeGenerator.Presets.Audio.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated Audio types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/Input System", false, 105)]
        public static void GenerateInputSystem() {
            var path = $"{OutputDir}/input-system.d.ts";
            TypeGenerator.Presets.InputSystem.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated Input System types to:\n{path}", "OK");
        }

        #endregion

        #region Generate Combined

        [MenuItem("OneJS/Generate Typings/All Unity Types", false, 200)]
        public static void GenerateAllUnity() {
            var path = $"{OutputDir}/unity-all.d.ts";
            TypeGenerator.Presets.All.WriteTo(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated all Unity types to:\n{path}", "OK");
        }

        [MenuItem("OneJS/Generate Typings/Project Types (Assembly-CSharp)", false, 201)]
        public static void GenerateProjectTypes() {
            var path = $"{OutputDir}/project.d.ts";
            TypeGenerator.GenerateProjectTypes(path);
            EditorUtility.DisplayDialog("Generation Complete",
                $"Generated project types to:\n{path}", "OK");
        }

        #endregion

        #region Utilities

        [MenuItem("OneJS/Generate Typings/Open Output Folder", false, 300)]
        public static void OpenOutputFolder() {
            // Ensure directory exists
            if (!System.IO.Directory.Exists(OutputDir)) {
                System.IO.Directory.CreateDirectory(OutputDir);
                AssetDatabase.Refresh();
            }

            // Reveal in file browser
            EditorUtility.RevealInFinder(OutputDir);
        }

        [MenuItem("OneJS/Generate Typings/Clean Generated Files", false, 301)]
        public static void CleanGeneratedFiles() {
            if (!System.IO.Directory.Exists(OutputDir)) {
                EditorUtility.DisplayDialog("Nothing to Clean",
                    "The output directory does not exist.", "OK");
                return;
            }

            var files = System.IO.Directory.GetFiles(OutputDir, "*.d.ts");
            if (files.Length == 0) {
                EditorUtility.DisplayDialog("Nothing to Clean",
                    "No .d.ts files found in the output directory.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm Clean",
                $"Delete {files.Length} generated .d.ts file(s) from:\n{OutputDir}?",
                "Delete", "Cancel")) {
                return;
            }

            foreach (var file in files) {
                System.IO.File.Delete(file);
                var metaFile = file + ".meta";
                if (System.IO.File.Exists(metaFile)) {
                    System.IO.File.Delete(metaFile);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[TypeGenerator] Cleaned {files.Length} generated files.");
        }

        #endregion
    }
}
