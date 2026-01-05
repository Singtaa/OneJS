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
///   {SceneDir}/{SceneName}_JSRunner/{GameObjectName}_{InstanceId}/app.js.txt
///   {SceneDir}/{SceneName}_JSRunner/{GameObjectName}_{InstanceId}/app.js.txt.map (optional)
/// </summary>
public class JSRunnerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
    public int callbackOrder => 0;

    static List<string> _createdAssets = new List<string>();
    static HashSet<string> _processedRunners = new HashSet<string>();
    static List<Scene> _modifiedScenes = new List<Scene>();

    public void OnPreprocessBuild(BuildReport report) {
        _createdAssets.Clear();
        _processedRunners.Clear();
        _modifiedScenes.Clear();

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
            }
        }

        // Save all modified scenes
        foreach (var scene in _modifiedScenes) {
            EditorSceneManager.SaveScene(scene);
        }

        // Refresh asset database to pick up new TextAssets
        if (_createdAssets.Count > 0) {
            AssetDatabase.Refresh();
        }

        // Restore original scene
        if (!string.IsNullOrWhiteSpace(originalScenePath)) {
            EditorSceneManager.OpenScene(originalScenePath);
        }

        Debug.Log($"[JSRunner] Build preprocessing complete. Processed {_processedRunners.Count} runner(s), created {_createdAssets.Count} asset(s).");
    }

    void ProcessScene(Scene scene) {
        bool sceneModified = false;

        foreach (var rootObj in scene.GetRootGameObjects()) {
            var runners = rootObj.GetComponentsInChildren<JSRunner>(true);
            foreach (var runner in runners) {
                // Only process enabled runners on active GameObjects
                if (!runner.enabled || !runner.gameObject.activeInHierarchy) continue;

                if (ProcessJSRunner(runner)) {
                    sceneModified = true;
                }
                ExtractCartridges(runner);
            }
        }

        if (sceneModified && !_modifiedScenes.Contains(scene)) {
            _modifiedScenes.Add(scene);
        }
    }

    bool ProcessJSRunner(JSRunner runner) {
        // Skip if already has a bundle asset assigned
        if (runner.BundleAsset != null) {
            Debug.Log($"[JSRunner] Bundle already assigned for {runner.gameObject.name}");
            return false;
        }

        // Get paths
        var entryFilePath = runner.EntryFileFullPath;
        var bundleAssetPath = runner.BundleAssetPath;
        var sourceMapAssetPath = runner.SourceMapAssetPath;

        if (string.IsNullOrEmpty(entryFilePath) || string.IsNullOrEmpty(bundleAssetPath)) {
            Debug.LogWarning($"[JSRunner] Invalid paths for {runner.gameObject.name}. Is scene saved?");
            return false;
        }

        // Check entry file exists
        if (!File.Exists(entryFilePath)) {
            Debug.LogWarning($"[JSRunner] Entry file not found for {runner.gameObject.name}: {entryFilePath}");
            return false;
        }

        // Skip if already processed (same bundle path)
        if (_processedRunners.Contains(bundleAssetPath)) {
            Debug.Log($"[JSRunner] Bundle already processed: {bundleAssetPath}");
            return false;
        }
        _processedRunners.Add(bundleAssetPath);

        // Ensure directory exists
        var bundleDir = Path.GetDirectoryName(bundleAssetPath);
        if (!string.IsNullOrEmpty(bundleDir) && !Directory.Exists(bundleDir)) {
            Directory.CreateDirectory(bundleDir);
        }

        // Read and create bundle TextAsset
        var bundleContent = File.ReadAllText(entryFilePath);
        File.WriteAllText(bundleAssetPath, bundleContent);
        _createdAssets.Add(bundleAssetPath);
        Debug.Log($"[JSRunner] Created bundle: {bundleAssetPath}");

        // Handle source map
        if (runner.IncludeSourceMap) {
            var sourceMapFilePath = runner.SourceMapFilePath;
            if (!string.IsNullOrEmpty(sourceMapFilePath) && File.Exists(sourceMapFilePath)) {
                var sourceMapContent = File.ReadAllText(sourceMapFilePath);
                File.WriteAllText(sourceMapAssetPath, sourceMapContent);
                _createdAssets.Add(sourceMapAssetPath);
                Debug.Log($"[JSRunner] Created source map: {sourceMapAssetPath}");
            }
        }

        // Refresh to import the new assets
        AssetDatabase.Refresh();

        // Load and assign the TextAssets
        var bundleAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(bundleAssetPath);
        if (bundleAsset != null) {
            runner.SetBundleAsset(bundleAsset);
        } else {
            Debug.LogError($"[JSRunner] Failed to load created bundle asset: {bundleAssetPath}");
        }

        if (runner.IncludeSourceMap && File.Exists(sourceMapAssetPath)) {
            var sourceMapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(sourceMapAssetPath);
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

    public void OnPostprocessBuild(BuildReport report) {
        if (_createdAssets.Count > 0) {
            Debug.Log($"[JSRunner] Build complete. {_createdAssets.Count} asset(s) created/updated.");
        }
    }
}
