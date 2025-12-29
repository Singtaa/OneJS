using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Build processor that automatically copies JS bundles and assets to StreamingAssets before build.
/// This ensures the JS code and assets are included in standalone builds without manual steps.
/// Assets are only copied during builds to keep StreamingAssets clean during development.
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
            CopyAssets(runner);
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

    void CopyAssets(JSRunner runner) {
        // Get working directory from JSRunner
        var workingDir = runner.WorkingDir;
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) {
            return;
        }

        var destBasePath = Path.Combine(Application.streamingAssetsPath, "onejs", "assets");
        int assetsCopied = 0;

        // 1. Copy user assets from {workingDir}/assets/
        var userAssetsPath = Path.Combine(workingDir, "assets");
        if (Directory.Exists(userAssetsPath)) {
            assetsCopied += CopyDirectory(userAssetsPath, destBasePath);
        }

        // 2. Scan node_modules for packages with onejs.assets
        var nodeModulesPath = Path.Combine(workingDir, "node_modules");
        if (Directory.Exists(nodeModulesPath)) {
            assetsCopied += CopyPackageAssets(nodeModulesPath, destBasePath);
        }

        if (assetsCopied > 0) {
            Debug.Log($"[JSRunner] Copied {assetsCopied} asset files to StreamingAssets/onejs/assets");
        }
    }

    int CopyPackageAssets(string nodeModulesPath, string destBasePath) {
        int copied = 0;

        foreach (var pkgDir in Directory.GetDirectories(nodeModulesPath)) {
            var dirName = Path.GetFileName(pkgDir);

            // Handle scoped packages (@scope/name)
            if (dirName.StartsWith("@")) {
                foreach (var scopedPkgDir in Directory.GetDirectories(pkgDir)) {
                    copied += TryCopyPackageAssets(scopedPkgDir, destBasePath);
                }
            } else {
                copied += TryCopyPackageAssets(pkgDir, destBasePath);
            }
        }

        return copied;
    }

    int TryCopyPackageAssets(string pkgDir, string destBasePath) {
        var pkgJsonPath = Path.Combine(pkgDir, "package.json");
        if (!File.Exists(pkgJsonPath)) return 0;

        try {
            var json = File.ReadAllText(pkgJsonPath);

            // Simple regex to extract onejs.assets value
            var match = System.Text.RegularExpressions.Regex.Match(
                json,
                @"""onejs""\s*:\s*\{\s*""assets""\s*:\s*""([^""]+)"""
            );

            if (!match.Success) return 0;

            var assetsDir = match.Groups[1].Value;
            var assetsPath = Path.Combine(pkgDir, assetsDir);

            if (!Directory.Exists(assetsPath)) return 0;

            // Copy all contents (including @namespace folders) flat to dest
            return CopyDirectory(assetsPath, destBasePath);
        } catch {
            return 0;
        }
    }

    int CopyDirectory(string srcDir, string destDir) {
        if (!Directory.Exists(srcDir)) return 0;

        Directory.CreateDirectory(destDir);
        int copied = 0;

        // Copy files
        foreach (var file in Directory.GetFiles(srcDir)) {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
            _copiedFiles.Add(destFile);
            copied++;
        }

        // Copy subdirectories recursively
        foreach (var subDir in Directory.GetDirectories(srcDir)) {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            copied += CopyDirectory(subDir, destSubDir);
        }

        return copied;
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
