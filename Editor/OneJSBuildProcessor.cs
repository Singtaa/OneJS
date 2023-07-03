using System.Collections.Generic;
using OneJS.Engine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS.Editor {
    public class OneJSBuildProcessor : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            Debug.Log("Processing Bundler(s)...");

            var originalScenePath = EditorSceneManager.GetActiveScene().path;
            var buildScenes = EditorBuildSettings.scenes;

            // If there are no enabled scenes in the build settings, use the current scene
            if (buildScenes.Length == 0 || !System.Array.Exists(buildScenes, bScene => bScene.enabled)) {
                ProcessScene(EditorSceneManager.GetActiveScene());
            } else {
                // Process each enabled scene in the build settings
                foreach (var bScene in buildScenes) {
                    if (!bScene.enabled)
                        continue;
                    EditorSceneManager.OpenScene(bScene.path);
                    ProcessScene(EditorSceneManager.GetActiveScene());
                }
            }

            if (!string.IsNullOrWhiteSpace(originalScenePath)) {
                EditorSceneManager.OpenScene(originalScenePath);
            }
        }

        private void ProcessScene(Scene scene) {
            foreach (var obj in scene.GetRootGameObjects()) {
                var bundlers = obj.GetComponentsInChildren<Bundler>();
                foreach (var bundler in bundlers) {
                    if (bundler.enabled && bundler.gameObject.activeInHierarchy)
                        bundler.PackageScriptsForBuild();
                }
            }
        }
    }
}