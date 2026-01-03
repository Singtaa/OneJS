using System;
using System.Collections.Generic;
using System.IO;
using OneJS;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Build processor that automatically copies JS bundles and assets to StreamingAssets before build.
/// Scans all enabled scenes in Build Settings for JSRunner components.
/// Assets are only copied during builds to keep StreamingAssets clean during development.
/// </summary>
public class JSRunnerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
    public int callbackOrder => 0;

    static List<string> _copiedFiles = new List<string>();

    // Track processed items to avoid duplicates
    static HashSet<string> _processedBundles = new HashSet<string>();
    static HashSet<string> _processedWorkingDirs = new HashSet<string>();

    public void OnPreprocessBuild(BuildReport report) {
        _copiedFiles.Clear();
        _processedBundles.Clear();
        _processedWorkingDirs.Clear();

        Debug.Log("[JSRunner] Processing JSRunner components in build scenes...");

        var originalScenePath = SceneManager.GetActiveScene().path;
        var buildScenes = EditorBuildSettings.scenes;

        // If no enabled scenes in build settings, process current scene
        if (buildScenes.Length == 0 || !Array.Exists(buildScenes, s => s.enabled)) {
            ProcessScene(SceneManager.GetActiveScene());
        } else {
            // Process each enabled scene
            foreach (var buildScene in buildScenes) {
                if (!buildScene.enabled) continue;

                EditorSceneManager.OpenScene(buildScene.path);
                ProcessScene(SceneManager.GetActiveScene());
            }
        }

        // Restore original scene
        if (!string.IsNullOrWhiteSpace(originalScenePath)) {
            EditorSceneManager.OpenScene(originalScenePath);
        }

        if (_copiedFiles.Count > 0) {
            AssetDatabase.Refresh();
        }

        Debug.Log($"[JSRunner] Build preprocessing complete. Processed {_processedBundles.Count} bundle(s), {_processedWorkingDirs.Count} working dir(s).");
    }

    void ProcessScene(Scene scene) {
        foreach (var rootObj in scene.GetRootGameObjects()) {
            var runners = rootObj.GetComponentsInChildren<JSRunner>(true);
            foreach (var runner in runners) {
                // Only process enabled runners on active GameObjects
                if (!runner.enabled || !runner.gameObject.activeInHierarchy) continue;

                ProcessJSRunner(runner);
                ProcessAssets(runner);
                ExtractCartridges(runner);
            }
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

        // Skip if already processed this bundle path (multiple runners might share same bundle)
        var destPath = Path.Combine(Application.streamingAssetsPath, streamingAssetsPath);
        if (_processedBundles.Contains(destPath)) {
            Debug.Log($"[JSRunner] Bundle already processed: {streamingAssetsPath}");
            return;
        }

        // Get entry file path
        var entryFilePath = runner.EntryFileFullPath;
        if (!File.Exists(entryFilePath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found, skipping: {entryFilePath}");
            return;
        }

        // Ensure StreamingAssets directory exists
        var destDir = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(destDir)) {
            Directory.CreateDirectory(destDir);
        }

        // Copy the bundle
        File.Copy(entryFilePath, destPath, overwrite: true);
        _copiedFiles.Add(destPath);
        _processedBundles.Add(destPath);

        Debug.Log($"[JSRunner] Copied bundle: {streamingAssetsPath}");
    }

    void ProcessAssets(JSRunner runner) {
        var workingDir = runner.WorkingDirFullPath;
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) {
            return;
        }

        // Skip if already processed this working directory
        var normalizedPath = Path.GetFullPath(workingDir);
        if (_processedWorkingDirs.Contains(normalizedPath)) {
            return;
        }
        _processedWorkingDirs.Add(normalizedPath);

        var destBasePath = Path.Combine(Application.streamingAssetsPath, "onejs", "assets");
        int assetsCopied = 0;

        // 1. Copy user assets from {workingDir}/assets/
        var userAssetsPath = Path.Combine(workingDir, "assets");
        if (Directory.Exists(userAssetsPath)) {
            assetsCopied += CopyDirectory(userAssetsPath, destBasePath);
        }

        // 2. Scan node_modules for packages with assets/@namespace/ folders
        var nodeModulesPath = Path.Combine(workingDir, "node_modules");
        if (Directory.Exists(nodeModulesPath)) {
            assetsCopied += CopyPackageAssets(nodeModulesPath, destBasePath);
        }

        if (assetsCopied > 0) {
            Debug.Log($"[JSRunner] Copied {assetsCopied} asset file(s) from: {runner.WorkingDir}");
        }
    }

    void ExtractCartridges(JSRunner runner) {
        var cartridges = runner.Cartridges;
        if (cartridges == null || cartridges.Count == 0) return;

        int extracted = 0;

        foreach (var cartridge in cartridges) {
            if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

            var destPath = runner.GetCartridgePath(cartridge);
            if (string.IsNullOrEmpty(destPath)) continue;

            // Clear existing cartridge folder
            if (Directory.Exists(destPath)) {
                Directory.Delete(destPath, true);
            }
            Directory.CreateDirectory(destPath);

            // Extract files from cartridge
            foreach (var file in cartridge.Files) {
                if (string.IsNullOrEmpty(file.path) || file.content == null) continue;

                var filePath = Path.Combine(destPath, file.path);
                var fileDir = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                    Directory.CreateDirectory(fileDir);
                }

                File.WriteAllText(filePath, file.content.text);
                _copiedFiles.Add(filePath);
            }

            // Generate TypeScript definitions
            var dts = CartridgeTypeGenerator.Generate(cartridge);
            var dtsPath = Path.Combine(destPath, $"{cartridge.Slug}.d.ts");
            File.WriteAllText(dtsPath, dts);
            _copiedFiles.Add(dtsPath);

            extracted++;
            Debug.Log($"[JSRunner] Extracted cartridge '{cartridge.DisplayName}' to: @cartridges/{cartridge.Slug}/");
        }

        if (extracted > 0) {
            Debug.Log($"[JSRunner] Extracted {extracted} cartridge(s) for: {runner.gameObject.name}");
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
        // Look for assets/ folder with @namespace/ subfolders
        var assetsPath = Path.Combine(pkgDir, "assets");
        if (!Directory.Exists(assetsPath)) return 0;

        int copied = 0;

        // Only copy @namespace/ folders (convention-based detection)
        foreach (var subDir in Directory.GetDirectories(assetsPath)) {
            var subDirName = Path.GetFileName(subDir);
            if (subDirName.StartsWith("@")) {
                var destPath = Path.Combine(destBasePath, subDirName);
                copied += CopyDirectory(subDir, destPath);
            }
        }

        return copied;
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
        if (_copiedFiles.Count > 0) {
            Debug.Log($"[JSRunner] Build complete. {_copiedFiles.Count} file(s) copied to StreamingAssets.");
            Debug.Log("[JSRunner] These files persist for faster subsequent builds. Delete StreamingAssets/onejs to remove them.");
        }
    }

    T GetSerializedField<T>(UnityEngine.Object obj, string fieldName) {
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
