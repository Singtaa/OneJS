using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Runs validation tests in standalone builds.
/// Outputs parseable test results via Debug.Log with [BUILD_TEST] prefix.
/// Exits with code 0 on success, 1 on failure.
///
/// Usage:
/// 1. Add this component to a test scene
/// 2. Configure the JSRunner reference if needed
/// 3. Build the player
/// 4. Run with -logFile to capture output
/// 5. Parse [BUILD_TEST] lines for results
/// </summary>
public class BuildValidationRunner : MonoBehaviour {
    [Tooltip("Optional: JSRunner to test. If null, will search scene.")]
    [SerializeField] JSRunner _jsRunner;

    [Tooltip("Simple test script to execute (no React, just validation)")]
    [SerializeField, TextArea(5, 10)]
    string _testScript = @"
globalThis.__buildTestResult = {
    rootExists: typeof __root !== 'undefined' && __root.__csHandle > 0,
    bridgeExists: typeof __bridge !== 'undefined' && __bridge.__csHandle > 0,
    platformDefines: typeof UNITY_EDITOR !== 'undefined',
    csProxyExists: typeof CS !== 'undefined'
};
";

    List<string> _results = new List<string>();
    bool _completed = false;
    float _startTime;


    void Awake() {
        Debug.Log("[BUILD_TEST] BuildValidationRunner.Awake() called");
    }

    void Start() {
        _startTime = Time.realtimeSinceStartup;
        Debug.Log("[BUILD_TEST] BuildValidationRunner.Start() called");
        Debug.Log($"[BUILD_TEST] Time.realtimeSinceStartup: {_startTime}");
        StartCoroutine(RunValidation());
    }

    void Update() {
        // Global timeout - force quit if running too long (30 seconds)
        if (!_completed && Time.realtimeSinceStartup - _startTime > 30f) {
            Debug.LogError("[BUILD_TEST] GLOBAL TIMEOUT - forcing exit");
            _results.Add("FAIL: Global timeout exceeded");
            ForceExit(1);
        }
    }

    void ForceExit(int exitCode) {
        _completed = true;
#if !UNITY_EDITOR
        Debug.Log($"[BUILD_TEST] Force exiting with code: {exitCode}");
        Application.Quit(exitCode);
#endif
    }

    IEnumerator RunValidation() {
        Debug.Log("[BUILD_TEST] Starting build validation...");
        Debug.Log($"[BUILD_TEST] Platform: {Application.platform}");
        Debug.Log($"[BUILD_TEST] StreamingAssets: {Application.streamingAssetsPath}");

        // Wait a few frames for Unity to stabilize
        yield return null;
        yield return null;

        // Test 1: StreamingAssets path accessible
        TestStreamingAssetsPath();
        yield return null;

        // Test 2: JS bundle exists
        TestJSBundleExists();
        yield return null;

        // Test 3: Package assets (@namespace folders)
        TestPackageAssets();
        yield return null;

        // Test 4: JSRunner execution
        yield return TestJSRunnerExecution();

        // Output all results
        Debug.Log("[BUILD_TEST] ===== RESULTS =====");
        foreach (var result in _results) {
            Debug.Log($"[BUILD_TEST] {result}");
        }

        var passed = _results.Count(r => r.StartsWith("PASS"));
        var failed = _results.Count(r => r.StartsWith("FAIL"));
        Debug.Log($"[BUILD_TEST] Total: {passed} passed, {failed} failed");

        _completed = true;

        // Exit with appropriate code (only in builds, not editor)
#if !UNITY_EDITOR
        var exitCode = failed > 0 ? 1 : 0;
        Debug.Log($"[BUILD_TEST] Exiting with code: {exitCode}");
        Application.Quit(exitCode);
#else
        Debug.Log("[BUILD_TEST] Running in Editor - not exiting");
#endif
    }

    void TestStreamingAssetsPath() {
        var path = Application.streamingAssetsPath;
        var exists = Directory.Exists(path);

        if (exists) {
            _results.Add($"PASS: StreamingAssets path exists: {path}");
        } else {
            _results.Add($"FAIL: StreamingAssets path not found: {path}");
        }
    }

    void TestJSBundleExists() {
        // Look for any .js files in StreamingAssets/onejs/
        var onejsPath = Path.Combine(Application.streamingAssetsPath, "onejs");

        if (!Directory.Exists(onejsPath)) {
            _results.Add($"FAIL: StreamingAssets/onejs/ directory not found");
            return;
        }

        var jsFiles = Directory.GetFiles(onejsPath, "*.js", SearchOption.AllDirectories);

        if (jsFiles.Length > 0) {
            _results.Add($"PASS: Found {jsFiles.Length} JS bundle(s) in StreamingAssets/onejs/");
        } else {
            _results.Add($"FAIL: No JS bundles found in StreamingAssets/onejs/");
        }
    }

    void TestPackageAssets() {
        var assetsPath = Path.Combine(Application.streamingAssetsPath, "onejs", "assets");

        if (!Directory.Exists(assetsPath)) {
            _results.Add("SKIP: StreamingAssets/onejs/assets/ directory not found (may be expected if no package assets)");
            return;
        }

        // Look for @namespace folders
        var namespaces = Directory.GetDirectories(assetsPath)
            .Select(Path.GetFileName)
            .Where(name => name.StartsWith("@"))
            .ToList();

        if (namespaces.Count > 0) {
            _results.Add($"PASS: Found {namespaces.Count} package asset namespace(s): {string.Join(", ", namespaces)}");
        } else {
            _results.Add("SKIP: No @namespace asset folders found (may be expected)");
        }
    }

    IEnumerator TestJSRunnerExecution() {
        // Find JSRunner if not assigned
        if (_jsRunner == null) {
            _jsRunner = FindFirstObjectByType<JSRunner>();
        }

        if (_jsRunner == null) {
            _results.Add("SKIP: No JSRunner found in scene");
            yield break;
        }

        // Wait for JSRunner to initialize
        float timeout = 5f;
        float elapsed = 0f;

        while (!_jsRunner.IsRunning && elapsed < timeout) {
            yield return null;
            elapsed += Time.deltaTime;
        }

        if (!_jsRunner.IsRunning) {
            _results.Add("FAIL: JSRunner did not start within timeout");
            yield break;
        }

        _results.Add("PASS: JSRunner is running");

        // Execute test script
        try {
            _jsRunner.Bridge.Eval(_testScript);
            _jsRunner.Bridge.Context.ExecutePendingJobs();

            // Check results
            var rootExists = _jsRunner.Bridge.Eval("globalThis.__buildTestResult?.rootExists");
            var bridgeExists = _jsRunner.Bridge.Eval("globalThis.__buildTestResult?.bridgeExists");
            var csExists = _jsRunner.Bridge.Eval("globalThis.__buildTestResult?.csProxyExists");

            if (rootExists == "true") {
                _results.Add("PASS: __root global is accessible");
            } else {
                _results.Add($"FAIL: __root global not accessible (got: {rootExists})");
            }

            if (bridgeExists == "true") {
                _results.Add("PASS: __bridge global is accessible");
            } else {
                _results.Add($"FAIL: __bridge global not accessible (got: {bridgeExists})");
            }

            if (csExists == "true") {
                _results.Add("PASS: CS proxy is accessible");
            } else {
                _results.Add($"FAIL: CS proxy not accessible (got: {csExists})");
            }
        } catch (Exception ex) {
            _results.Add($"FAIL: JS execution error: {ex.Message}");
        }
    }

    void OnGUI() {
        // Display status on screen for visual debugging
        GUILayout.BeginArea(new Rect(10, 10, 500, 400));
        GUILayout.Label("Build Validation Status", GUI.skin.box);

        if (!_completed) {
            GUILayout.Label("Running tests...");
        } else {
            foreach (var result in _results) {
                var color = result.StartsWith("PASS") ? Color.green :
                           result.StartsWith("FAIL") ? Color.red :
                           Color.yellow;
                GUI.contentColor = color;
                GUILayout.Label(result);
            }
            GUI.contentColor = Color.white;
        }

        GUILayout.EndArea();
    }
}
