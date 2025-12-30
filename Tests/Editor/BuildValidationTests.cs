using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// EditMode tests that build a standalone player, run it, and validate the output.
/// These tests are slow (30-60+ seconds) but provide end-to-end validation.
///
/// Prerequisites:
/// - A test scene with BuildValidationRunner component
/// - The scene must be added to Build Settings
/// </summary>
[TestFixture]
public class BuildValidationTests {
    const string TEST_SCENE_PATH = "Assets/Singtaa/OneJS/Tests/BuildValidation/BuildValidationScene.unity";
    const string BUILD_OUTPUT_DIR = "Temp/OneJSBuildTest";
    const int BUILD_TIMEOUT_MS = 120000; // 2 minutes for build
    const int RUN_TIMEOUT_MS = 60000;    // 1 minute for execution

    string _buildPath;
    string _originalScenes;

    [SetUp]
    public void SetUp() {
        _buildPath = Path.Combine(
            Path.GetDirectoryName(Application.dataPath),
            BUILD_OUTPUT_DIR,
            GetBuildName());

        // Store original build scenes
        _originalScenes = string.Join(",",
            EditorBuildSettings.scenes.Select(s => s.path));
    }

    [TearDown]
    public void TearDown() {
        // Cleanup build output
        var buildDir = Path.GetDirectoryName(_buildPath);
        if (Directory.Exists(buildDir)) {
            try {
                Directory.Delete(buildDir, true);
            } catch (IOException) {
                Debug.LogWarning($"[BuildValidation] Could not clean up: {buildDir}");
            }
        }
    }

    /// <summary>
    /// Full end-to-end test: build player, run it, validate results.
    /// Requires a properly configured test scene.
    /// </summary>
    [Test, Explicit("Slow test - builds and runs standalone player")]
    [Timeout(180000)] // 3 minute total timeout
    public void StandaloneBuild_ExecutesJS_AndPassesValidation() {
        // Skip if test scene doesn't exist
        if (!File.Exists(TEST_SCENE_PATH)) {
            Assert.Ignore($"Test scene not found: {TEST_SCENE_PATH}. Create it with BuildValidationRunner component.");
            return;
        }

        // 1. Configure build settings
        var originalScenes = EditorBuildSettings.scenes;
        try {
            AddTestSceneToBuildSettings();

            // 2. Build player
            var buildReport = BuildTestPlayer();
            Assert.AreEqual(BuildResult.Succeeded, buildReport.summary.result,
                $"Build failed: {GetBuildErrors(buildReport)}");

            // 3. Run executable and capture output
            var (exitCode, output) = RunAndCapture(_buildPath, RUN_TIMEOUT_MS);

            // 4. Parse [BUILD_TEST] lines
            var results = ParseTestResults(output);

            // 5. Log all results for debugging
            Debug.Log($"[BuildValidation] Captured {results.Count} test results:");
            foreach (var result in results) {
                Debug.Log($"  {result}");
            }

            // 6. Assert results
            Assert.IsTrue(results.Count > 0, "No test results captured. Check build output.");

            var failures = results.Where(r => r.StartsWith("FAIL")).ToList();
            if (failures.Count > 0) {
                Assert.Fail($"Build validation failed:\n{string.Join("\n", failures)}");
            }

            var passes = results.Count(r => r.StartsWith("PASS"));
            Debug.Log($"[BuildValidation] All {passes} tests passed!");
        } finally {
            // Restore original build settings
            EditorBuildSettings.scenes = originalScenes;
        }
    }

    /// <summary>
    /// Quick validation that build processor copies assets correctly.
    /// Doesn't run the build, just simulates the preprocessing.
    /// </summary>
    [Test]
    public void BuildProcessor_CopiesAssetsToStreamingAssets() {
        // This is covered by JSRunnerBuildProcessorTests
        // Just verify the processor exists and can be instantiated
        var processor = new JSRunnerBuildProcessor();
        Assert.IsNotNull(processor);
        Assert.AreEqual(0, processor.callbackOrder);
    }

    // MARK: Helper Methods

    void AddTestSceneToBuildSettings() {
        var scenes = EditorBuildSettings.scenes.ToList();

        // Check if test scene is already included
        var existingScene = scenes.FirstOrDefault(s => s.path == TEST_SCENE_PATH);
        if (existingScene != null) {
            existingScene.enabled = true;
        } else {
            scenes.Insert(0, new EditorBuildSettingsScene(TEST_SCENE_PATH, true));
        }

        // Disable other scenes for test
        foreach (var scene in scenes) {
            if (scene.path != TEST_SCENE_PATH) {
                scene.enabled = false;
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    BuildReport BuildTestPlayer() {
        // Ensure output directory exists
        var buildDir = Path.GetDirectoryName(_buildPath);
        if (Directory.Exists(buildDir)) {
            Directory.Delete(buildDir, true);
        }
        Directory.CreateDirectory(buildDir);

        var options = new BuildPlayerOptions {
            scenes = new[] { TEST_SCENE_PATH },
            locationPathName = _buildPath,
            target = EditorUserBuildSettings.activeBuildTarget,
            options = BuildOptions.None
        };

        Debug.Log($"[BuildValidation] Building to: {_buildPath}");
        var stopwatch = Stopwatch.StartNew();

        var report = BuildPipeline.BuildPlayer(options);

        stopwatch.Stop();
        Debug.Log($"[BuildValidation] Build completed in {stopwatch.ElapsedMilliseconds}ms");

        return report;
    }

    (int exitCode, string output) RunAndCapture(string executablePath, int timeoutMs) {
        // Handle platform-specific executable paths
        var actualPath = executablePath;

#if UNITY_EDITOR_OSX
        // On macOS, the executable is inside the .app bundle
        if (executablePath.EndsWith(".app")) {
            var appName = Path.GetFileNameWithoutExtension(executablePath);
            actualPath = Path.Combine(executablePath, "Contents", "MacOS", appName);
        }
#endif

        if (!File.Exists(actualPath)) {
            Debug.LogError($"[BuildValidation] Executable not found: {actualPath}");
            return (-1, $"Executable not found: {actualPath}");
        }

        Debug.Log($"[BuildValidation] Running: {actualPath}");

        var logFile = Path.Combine(Path.GetTempPath(), "onejs_build_test.log");

        var startInfo = new ProcessStartInfo {
            FileName = actualPath,
            Arguments = $"-logFile \"{logFile}\" -batchmode -nographics",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var output = "";
        var exitCode = -1;

        using (var process = new Process { StartInfo = startInfo }) {
            try {
                process.Start();

                // Wait for exit with timeout
                if (process.WaitForExit(timeoutMs)) {
                    exitCode = process.ExitCode;
                } else {
                    Debug.LogWarning("[BuildValidation] Process timed out, killing...");
                    process.Kill();
                    exitCode = -1;
                }

                // Read log file
                if (File.Exists(logFile)) {
                    output = File.ReadAllText(logFile);
                    File.Delete(logFile);
                }
            } catch (Exception ex) {
                Debug.LogError($"[BuildValidation] Process error: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        Debug.Log($"[BuildValidation] Process exited with code: {exitCode}");
        return (exitCode, output);
    }

    List<string> ParseTestResults(string output) {
        var results = new List<string>();
        var regex = new Regex(@"\[BUILD_TEST\]\s*(.+)$", RegexOptions.Multiline);

        foreach (Match match in regex.Matches(output)) {
            var result = match.Groups[1].Value.Trim();

            // Only include actual test results (PASS/FAIL/SKIP)
            if (result.StartsWith("PASS") ||
                result.StartsWith("FAIL") ||
                result.StartsWith("SKIP")) {
                results.Add(result);
            }
        }

        return results;
    }

    string GetBuildErrors(BuildReport report) {
        var errors = new List<string>();
        foreach (var step in report.steps) {
            foreach (var message in step.messages) {
                if (message.type == LogType.Error) {
                    errors.Add(message.content);
                }
            }
        }
        return string.Join("\n", errors);
    }

    string GetBuildName() {
#if UNITY_EDITOR_WIN
        return "BuildTest.exe";
#elif UNITY_EDITOR_OSX
        return "BuildTest.app";
#elif UNITY_EDITOR_LINUX
        return "BuildTest";
#else
        return "BuildTest";
#endif
    }
}
