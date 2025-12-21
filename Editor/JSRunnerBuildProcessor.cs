using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Build processor that automatically copies JS bundles to StreamingAssets before build.
/// This ensures the JS code is included in standalone builds without manual steps.
/// </summary>
public class JSRunnerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
    public int callbackOrder => 0;

    static List<string> _copiedFiles = new List<string>();

    public void OnPreprocessBuild(BuildReport report) {
        _copiedFiles.Clear();

        var jsRunners = FindJSRunnersInBuildScenes();
        if (jsRunners.Count == 0) {
            return;
        }

        Debug.Log($"[JSRunner] Found {jsRunners.Count} JSRunner(s) in build scenes");

        foreach (var runner in jsRunners) {
            ProcessJSRunner(runner);
        }

        if (_copiedFiles.Count > 0) {
            AssetDatabase.Refresh();
        }
    }

    void ProcessJSRunner(JSRunner runner) {
        // Skip if using embedded TextAsset
        var embeddedScript = GetSerializedField<TextAsset>(runner, "_embeddedScript");
        if (embeddedScript != null) {
            Debug.Log($"[JSRunner] Using embedded TextAsset for {runner.gameObject.name}");
            return;
        }

        var streamingAssetsPath = GetSerializedField<string>(runner, "_streamingAssetsPath");
        if (string.IsNullOrEmpty(streamingAssetsPath)) {
            Debug.LogWarning($"[JSRunner] No streaming assets path configured for {runner.gameObject.name}");
            return;
        }

        // Get entry file path
        var entryFilePath = runner.EntryFileFullPath;
        if (!File.Exists(entryFilePath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found, skipping: {entryFilePath}");
            return;
        }

        // Ensure StreamingAssets directory exists
        var destPath = Path.Combine(Application.streamingAssetsPath, streamingAssetsPath);
        var destDir = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(destDir)) {
            Directory.CreateDirectory(destDir);
        }

        // Copy the bundle
        File.Copy(entryFilePath, destPath, overwrite: true);
        _copiedFiles.Add(destPath);

        // Also create .meta file if it doesn't exist (Unity needs this)
        var metaPath = destPath + ".meta";
        if (!File.Exists(metaPath)) {
            _copiedFiles.Add(metaPath);
        }

        Debug.Log($"[JSRunner] Copied bundle to StreamingAssets: {streamingAssetsPath}");
    }

    public void OnPostprocessBuild(BuildReport report) {
        // Optionally clean up copied files after build
        // For now, we leave them for faster subsequent builds
        // Users can manually delete StreamingAssets/onejs if they want

        if (_copiedFiles.Count > 0) {
            Debug.Log($"[JSRunner] Build complete. {_copiedFiles.Count} file(s) copied to StreamingAssets.");
            Debug.Log("[JSRunner] These files will be included in subsequent builds. Delete StreamingAssets/onejs to remove them.");
        }
    }

    List<JSRunner> FindJSRunnersInBuildScenes() {
        var result = new List<JSRunner>();

        // Get all scenes in build settings
        var buildScenes = EditorBuildSettings.scenes;

        foreach (var buildScene in buildScenes) {
            if (!buildScene.enabled) continue;

            // Load scene additively to inspect it
            var scene = SceneManager.GetSceneByPath(buildScene.path);
            bool wasLoaded = scene.isLoaded;

            if (!wasLoaded) {
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    buildScene.path,
                    UnityEditor.SceneManagement.OpenSceneMode.Additive
                );
            }

            // Find all JSRunners in the scene
            var rootObjects = scene.GetRootGameObjects();
            foreach (var root in rootObjects) {
                var runners = root.GetComponentsInChildren<JSRunner>(true);
                result.AddRange(runners);
            }

            // Unload if we loaded it
            if (!wasLoaded) {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        return result;
    }

    T GetSerializedField<T>(Object obj, string fieldName) {
        var so = new SerializedObject(obj);
        var prop = so.FindProperty(fieldName);
        if (prop == null) return default;

        if (typeof(T) == typeof(string)) {
            return (T)(object)prop.stringValue;
        } else if (typeof(T) == typeof(TextAsset)) {
            return (T)(object)prop.objectReferenceValue;
        }

        return default;
    }
}
