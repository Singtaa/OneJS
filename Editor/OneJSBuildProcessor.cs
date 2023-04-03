using System.Collections.Generic;
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
            foreach (var bScene in EditorBuildSettings.scenes) {
                if (!bScene.enabled)
                    continue;
                EditorSceneManager.OpenScene(bScene.path);
                var scene = EditorSceneManager.GetActiveScene();
                foreach (var obj in scene.GetRootGameObjects()) {
                    var bundlers = obj.GetComponentsInChildren<Bundler>();
                    foreach (var bundler in bundlers) {
                        bundler.PackageScriptsForBuild();
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(originalScenePath)) {
                EditorSceneManager.OpenScene(originalScenePath);
            }
        }
    }
}