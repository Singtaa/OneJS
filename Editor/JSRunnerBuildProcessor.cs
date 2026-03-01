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
/// Build processor that automatically creates TextAsset bundles for JSRunner components.
/// Scans all enabled scenes in Build Settings and generates TextAssets for each JSRunner.
///
/// Directory structure:
///   {SceneDir}/{SceneName}/{GameObjectName}/app.js.txt
///   {SceneDir}/{SceneName}/{GameObjectName}/app.js.txt.map (optional)
/// </summary>
public class JSRunnerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
    public int callbackOrder => 0;

    static List<string> _createdAssets = new List<string>();
    static HashSet<string> _processedRunners = new HashSet<string>();
    static int _copiedAssetCount = 0;

    public void OnPreprocessBuild(BuildReport report) {
        _createdAssets.Clear();
        _processedRunners.Clear();
        _copiedAssetCount = 0;

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

                var scene = EditorSceneManager.OpenScene(buildScene.path);
                ProcessScene(scene);

                if (scene.isDirty) {
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }

        // Refresh asset database to pick up new TextAssets
        if (_createdAssets.Count > 0) {
            AssetDatabase.Refresh();
        }

        // Restore original scene
        if (!string.IsNullOrWhiteSpace(originalScenePath)) {
            EditorSceneManager.OpenScene(originalScenePath);
        }

        var assetMsg = _copiedAssetCount > 0 ? $", copied {_copiedAssetCount} asset file(s) to StreamingAssets" : "";
        Debug.Log($"[JSRunner] Build preprocessing complete. Processed {_processedRunners.Count} runner(s), created {_createdAssets.Count} asset(s){assetMsg}.");
    }

    void ProcessScene(Scene scene) {
        foreach (var rootObj in scene.GetRootGameObjects()) {
            var runners = rootObj.GetComponentsInChildren<JSRunner>(true);
            foreach (var runner in runners) {
                // Only process enabled runners on active GameObjects
                if (!runner.enabled || !runner.gameObject.activeInHierarchy) continue;

                ProcessJSRunner(runner);
                ExtractCartridges(runner);
                CopyAssets(runner);
            }
        }
    }

    bool ProcessJSRunner(JSRunner runner) {
        // Skip if already has a bundle asset assigned
        if (runner.BundleAsset != null) {
            Debug.Log($"[JSRunner] Bundle already assigned for {runner.gameObject.name}");
            return false;
        }

        var entryFilePath = runner.EntryFileFullPath;
        var instanceFolder = runner.InstanceFolder;
        var bundleDir = !string.IsNullOrEmpty(instanceFolder) ? instanceFolder : Path.GetDirectoryName(entryFilePath);
        var bundleAssetPathUnity = runner.InstanceFolderAssetPath != null ? runner.InstanceFolderAssetPath + "/app.js.txt" : null;
        var sourceMapAssetPathUnity = runner.InstanceFolderAssetPath != null ? runner.InstanceFolderAssetPath + "/app.js.map.txt" : null;

        if (string.IsNullOrEmpty(entryFilePath) || string.IsNullOrEmpty(bundleAssetPathUnity)) {
            Debug.LogWarning($"[JSRunner] Invalid paths for {runner.gameObject.name}. Is scene saved?");
            return false;
        }

        if (!File.Exists(entryFilePath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found for {runner.gameObject.name}: {entryFilePath}");
            return false;
        }

        if (_processedRunners.Contains(bundleAssetPathUnity)) {
            Debug.Log($"[JSRunner] Bundle already processed: {bundleAssetPathUnity}");
            return false;
        }
        _processedRunners.Add(bundleAssetPathUnity);

        if (!string.IsNullOrEmpty(bundleDir) && !Directory.Exists(bundleDir)) {
            Directory.CreateDirectory(bundleDir);
        }

        var bundleContent = File.ReadAllText(entryFilePath);
        var bundleFullPath = Path.Combine(bundleDir ?? "", "app.js.txt");
        File.WriteAllText(bundleFullPath, bundleContent);
        _createdAssets.Add(bundleFullPath);
        Debug.Log($"[JSRunner] Created bundle: {bundleAssetPathUnity}");

        if (runner.IncludeSourceMap) {
            var sourceMapFilePath = runner.SourceMapFilePath;
            if (!string.IsNullOrEmpty(sourceMapFilePath) && File.Exists(sourceMapFilePath)) {
                var sourceMapContent = File.ReadAllText(sourceMapFilePath);
                var sourceMapFullPath = Path.Combine(bundleDir ?? "", "app.js.map.txt");
                File.WriteAllText(sourceMapFullPath, sourceMapContent);
                _createdAssets.Add(sourceMapFullPath);
                Debug.Log($"[JSRunner] Created source map: {sourceMapAssetPathUnity}");
            }
        }

        AssetDatabase.Refresh();

        var bundleAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(bundleAssetPathUnity);
        if (bundleAsset != null) {
            runner.SetBundleAsset(bundleAsset);
        } else {
            Debug.LogError($"[JSRunner] Failed to load created bundle asset: {bundleAssetPathUnity}");
        }

        if (runner.IncludeSourceMap && !string.IsNullOrEmpty(sourceMapAssetPathUnity) && File.Exists(Path.Combine(bundleDir ?? "", "app.js.map.txt"))) {
            var sourceMapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(sourceMapAssetPathUnity);
            if (sourceMapAsset != null) {
                runner.SetSourceMapAsset(sourceMapAsset);
            }
        }

        return true;
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
                _createdAssets.Add(filePath);
            }

            // Generate TypeScript definitions
            var dts = CartridgeTypeGenerator.Generate(cartridge);
            var dtsPath = Path.Combine(destPath, $"{cartridge.Slug}.d.ts");
            File.WriteAllText(dtsPath, dts);
            _createdAssets.Add(dtsPath);

            extracted++;
            Debug.Log($"[JSRunner] Extracted cartridge '{cartridge.DisplayName}' to: @cartridges/{cartridge.Slug}/");
        }

        if (extracted > 0) {
            Debug.Log($"[JSRunner] Extracted {extracted} cartridge(s) for: {runner.gameObject.name}");
        }
    }

    void CopyAssets(JSRunner runner) {
        var workingDir = runner.WorkingDirFullPath;
        if (string.IsNullOrEmpty(workingDir)) return;

        var assetsDir = Path.Combine(workingDir, "assets");
        if (!Directory.Exists(assetsDir)) return;

        var destDir = Path.Combine(Application.dataPath, "StreamingAssets", "onejs", "assets");
        if (Directory.Exists(destDir)) {
            Directory.Delete(destDir, true);
        }
        int count = CopyDirectoryRecursive(assetsDir, destDir);
        _copiedAssetCount += count;

        if (count > 0) {
            Debug.Log($"[JSRunner] Copied {count} asset file(s) to StreamingAssets for: {runner.gameObject.name}");
        }
    }

    int CopyDirectoryRecursive(string src, string dest) {
        int count = 0;

        if (!Directory.Exists(dest)) {
            Directory.CreateDirectory(dest);
        }

        foreach (var file in Directory.GetFiles(src)) {
            if (file.EndsWith(".meta")) continue;
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, true);
            count++;
        }

        foreach (var dir in Directory.GetDirectories(src)) {
            var destSubDir = Path.Combine(dest, Path.GetFileName(dir));
            count += CopyDirectoryRecursive(dir, destSubDir);
        }

        return count;
    }

    public void OnPostprocessBuild(BuildReport report) {
        if (_createdAssets.Count > 0) {
            Debug.Log($"[JSRunner] Build complete. {_createdAssets.Count} asset(s) created/updated.");
        }
    }
}
