using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Service that automatically generates TypeScript typings for JSRunner configurations.
    /// Triggers on domain reload and provides manual generation via inspector button.
    /// </summary>
    [InitializeOnLoad]
    public static class TypeGeneratorService {
        private static bool _hasGeneratedThisSession;

        static TypeGeneratorService() {
            // Delay to ensure Unity is fully initialized
            EditorApplication.delayCall += OnDomainReload;
        }

        /// <summary>
        /// Called after domain reload. Generates typings for all JSRunners with auto-generation enabled.
        /// </summary>
        private static void OnDomainReload() {
            if (_hasGeneratedThisSession) return;
            _hasGeneratedThisSession = true;

            // Find all JSRunners in all loaded scenes
            var runners = FindAllJSRunners();

            foreach (var runner in runners) {
                if (runner.AutoGenerateTypings && runner.TypingAssemblies.Count > 0) {
                    try {
                        GenerateTypingsFor(runner, silent: true);
                    } catch (Exception ex) {
                        Debug.LogWarning($"[TypeGeneratorService] Auto-generation failed for '{runner.name}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Generates TypeScript typings for a specific JSRunner.
        /// </summary>
        /// <param name="runner">The JSRunner to generate typings for</param>
        /// <param name="silent">If true, only logs on error</param>
        /// <returns>True if generation was successful</returns>
        public static bool GenerateTypingsFor(JSRunner runner, bool silent = false) {
            if (runner == null) {
                Debug.LogError("[TypeGeneratorService] Runner is null");
                return false;
            }

            if (runner.TypingAssemblies == null || runner.TypingAssemblies.Count == 0) {
                if (!silent) {
                    Debug.LogWarning($"[TypeGeneratorService] No assemblies configured for '{runner.name}'");
                }
                return false;
            }

            try {
                var builder = new TypeGeneratorBuilder()
                    .IncludeDocumentation()
                    .ExcludeObsolete();

                // Add all configured assemblies
                foreach (var assemblyName in runner.TypingAssemblies) {
                    if (string.IsNullOrWhiteSpace(assemblyName)) continue;
                    builder.AddAssemblyByName(assemblyName.Trim());
                }

                // Build the result
                var result = builder.Build();

                if (result.TypeCount == 0) {
                    if (!silent) {
                        Debug.LogWarning($"[TypeGeneratorService] No types found for '{runner.name}'");
                    }
                    return false;
                }

                // Ensure output directory exists
                var outputPath = runner.TypingsFullPath;
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) {
                    Directory.CreateDirectory(outputDir);
                }

                // Write the file
                result.WriteTo(outputPath);

                if (!silent) {
                    Debug.Log($"[TypeGeneratorService] Generated {result.TypeCount} types for '{runner.name}' at {outputPath}");
                }

                return true;
            } catch (Exception ex) {
                Debug.LogError($"[TypeGeneratorService] Failed to generate typings for '{runner.name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds all JSRunner instances in all loaded scenes.
        /// </summary>
        private static JSRunner[] FindAllJSRunners() {
            // In editor, search loaded scenes
            var runners = new System.Collections.Generic.List<JSRunner>();

            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects()) {
                    runners.AddRange(root.GetComponentsInChildren<JSRunner>(true));
                }
            }

            // Also search prefab stage if open
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null) {
                runners.AddRange(prefabStage.prefabContentsRoot.GetComponentsInChildren<JSRunner>(true));
            }

            return runners.ToArray();
        }

        /// <summary>
        /// Force regeneration for all JSRunners in the project.
        /// </summary>
        [MenuItem("Tools/OneJS/Regenerate All Project Typings")]
        public static void RegenerateAllTypings() {
            var runners = FindAllJSRunners();
            var successCount = 0;

            foreach (var runner in runners) {
                if (runner.TypingAssemblies.Count > 0) {
                    if (GenerateTypingsFor(runner, silent: false)) {
                        successCount++;
                    }
                }
            }

            Debug.Log($"[TypeGeneratorService] Regenerated typings for {successCount}/{runners.Length} JSRunners");
        }
    }
}
