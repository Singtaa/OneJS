using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Automatically starts/stops file watchers for JSRunner instances when entering/exiting Play mode.
    /// Handles npm install if node_modules is missing.
    /// </summary>
    [InitializeOnLoad]
    public static class JSRunnerAutoWatch {
        static readonly HashSet<string> _pendingInstalls = new();
        static readonly HashSet<string> _watchersStartedThisSession = new();

        static JSRunnerAutoWatch() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state) {
            switch (state) {
                case PlayModeStateChange.ExitingEditMode:
                    // Create PanelSettings assets for JSRunners that don't have one
                    EnsurePanelSettingsAssets();
                    // Scaffold files and run initial build if needed
                    EnsureProjectsReady();
                    // Prepare watchers before entering play mode
                    PrepareWatchers();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // Start watchers after entering play mode
                    StartWatchersAsync();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // Stop watchers that we started this session
                    StopSessionWatchers();
                    break;
            }
        }

        static void PrepareWatchers() {
            _watchersStartedThisSession.Clear();
        }

        /// <summary>
        /// Ensures all JSRunner projects have their files scaffolded before entering Play mode.
        /// npm install and build run asynchronously after Play mode starts.
        /// </summary>
        static void EnsureProjectsReady() {
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsSortMode.None);

            foreach (var runner in runners) {
                if (runner == null || !runner.enabled || !runner.gameObject.activeInHierarchy) continue;
                if (!runner.IsSceneSaved) continue;

                // Scaffold files if needed (this is fast, do it synchronously)
                runner.EnsureProjectSetup();
            }

            // Refresh AssetDatabase to pick up new files
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Creates PanelSettings assets for JSRunners that don't have one assigned.
        /// Called before entering Play mode so the assignment persists.
        /// </summary>
        static void EnsurePanelSettingsAssets() {
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsSortMode.None);
            bool anyCreated = false;

            foreach (var runner in runners) {
                if (runner == null || !runner.enabled || !runner.gameObject.activeInHierarchy) continue;
                if (!runner.IsSceneSaved) continue;

                // Check if PanelSettings already assigned
                var panelSettingsProp = new SerializedObject(runner).FindProperty("_panelSettings");
                if (panelSettingsProp.objectReferenceValue != null) continue;

                // Try to load existing asset
                var psPath = runner.PanelSettingsAssetPath;
                if (string.IsNullOrEmpty(psPath)) continue;

                var existingPS = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
                if (existingPS != null) {
                    // Assign existing asset
                    panelSettingsProp.objectReferenceValue = existingPS;
                    panelSettingsProp.serializedObject.ApplyModifiedProperties();
                    continue;
                }

                // Create new PanelSettings asset
                runner.CreateDefaultPanelSettingsAsset();
                anyCreated = true;
            }

            if (anyCreated) {
                AssetDatabase.SaveAssets();
            }
        }

        static void StartWatchersAsync() {
            var runners = UnityEngine.Object.FindObjectsByType<JSRunner>(FindObjectsSortMode.None);

            foreach (var runner in runners) {
                if (runner == null || !runner.enabled || !runner.gameObject.activeInHierarchy) continue;

                var workingDir = runner.WorkingDirFullPath;
                if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir)) continue;

                // Skip if watcher is already running
                if (NodeWatcherManager.IsRunning(workingDir)) continue;

                // Check for package.json
                if (!File.Exists(Path.Combine(workingDir, "package.json"))) {
                    Debug.Log($"[JSRunner] Skipping auto-watch for {runner.name}: no package.json");
                    continue;
                }

                // Check if we need to do first-time setup (install + build)
                bool needsInstall = !runner.HasNodeModules;
                bool needsBuild = !runner.HasBundle;

                if (needsInstall) {
                    // Need to install dependencies first, then build, then start watcher
                    Debug.Log($"[JSRunner] Installing dependencies for {runner.name}...");
                    RunNpmInstallBuildAndWatch(workingDir, runner, needsBuild);
                } else if (needsBuild) {
                    // Have node_modules but no bundle - just build then start watcher
                    Debug.Log($"[JSRunner] Building {runner.name}...");
                    RunNpmBuildAndWatch(workingDir, runner);
                } else {
                    // Everything ready, just start watcher
                    StartWatcher(workingDir, runner.name);
                }
            }
        }

        static void StartWatcher(string workingDir, string runnerName) {
            if (NodeWatcherManager.StartWatcher(workingDir)) {
                _watchersStartedThisSession.Add(workingDir);
            }
        }

        static void StopSessionWatchers() {
            foreach (var workingDir in _watchersStartedThisSession) {
                if (NodeWatcherManager.IsRunning(workingDir)) {
                    NodeWatcherManager.StopWatcher(workingDir);
                }
            }
            _watchersStartedThisSession.Clear();
        }

        static void RunNpmInstallBuildAndWatch(string workingDir, JSRunner runner, bool needsBuild) {
            if (_pendingInstalls.Contains(workingDir)) return;
            _pendingInstalls.Add(workingDir);

            RunNpmCommand(workingDir, "install", () => {
                Debug.Log($"[JSRunner] Dependencies installed for {runner.name}");
                _pendingInstalls.Remove(workingDir);

                if (!EditorApplication.isPlaying) return;

                if (needsBuild) {
                    // Now run build
                    RunNpmBuildAndWatch(workingDir, runner);
                } else {
                    StartWatcher(workingDir, runner.name);
                }
            }, code => {
                _pendingInstalls.Remove(workingDir);
                Debug.LogError($"[JSRunner] npm install failed for {runner.name} (exit code {code})");
            });
        }

        static void RunNpmBuildAndWatch(string workingDir, JSRunner runner) {
            RunNpmCommand(workingDir, "run build", () => {
                Debug.Log($"[JSRunner] Build complete for {runner.name}");

                if (!EditorApplication.isPlaying) return;

                // Trigger reload to pick up the new bundle
                if (runner != null) {
                    runner.ForceReload();
                }

                // Start watcher for subsequent changes
                StartWatcher(workingDir, runner.name);
            }, code => {
                Debug.LogError($"[JSRunner] npm build failed for {runner.name} (exit code {code})");
            });
        }

        static void RunNpmCommand(string workingDir, string arguments, Action onSuccess, Action<int> onFailure) {
            try {
                ProcessStartInfo startInfo;
#if UNITY_EDITOR_WIN
                if (OneJSWslHelper.UseWsl) {
                    startInfo = new ProcessStartInfo {
                        FileName = "wsl.exe",
                        Arguments = OneJSWslHelper.GetWslNpmArguments(workingDir, arguments),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                } else
#endif
                {
                    var npmPath = GetNpmExecutable();
                    var nodeBinDir = Path.GetDirectoryName(npmPath);
                    startInfo = new ProcessStartInfo {
                        FileName = npmPath,
                        Arguments = arguments,
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!string.IsNullOrEmpty(nodeBinDir)) {
                        startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;
                    }
                }

                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                process.OutputDataReceived += (_, args) => {
                    if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
                };
                process.ErrorDataReceived += (_, args) => {
                    if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
                };
                process.Exited += (_, _) => {
                    EditorApplication.delayCall += () => {
                        if (process.ExitCode == 0) {
                            onSuccess?.Invoke();
                        } else {
                            onFailure?.Invoke(process.ExitCode);
                        }
                    };
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to run npm {arguments}: {ex.Message}");
                onFailure?.Invoke(-1);
            }
        }

        // MARK: npm Resolution (same as NodeWatcherManager)

        static string _cachedNpmPath;

        static string GetNpmExecutable() {
            if (!string.IsNullOrEmpty(_cachedNpmPath)) return _cachedNpmPath;

#if UNITY_EDITOR_WIN
            return _cachedNpmPath = OneJSWslHelper.GetWindowsNpmPath();
#else
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] searchPaths = {
                "/usr/local/bin/npm",
                "/opt/homebrew/bin/npm",
                "/usr/bin/npm",
                Path.Combine(home, "n/bin/npm"),
            };

            foreach (var path in searchPaths) {
                if (File.Exists(path)) return _cachedNpmPath = path;
            }

            // Check nvm
            var nvmDir = Path.Combine(home, ".nvm/versions/node");
            if (Directory.Exists(nvmDir)) {
                try {
                    foreach (var nodeDir in Directory.GetDirectories(nvmDir)) {
                        var npmPath = Path.Combine(nodeDir, "bin", "npm");
                        if (File.Exists(npmPath)) return _cachedNpmPath = npmPath;
                    }
                } catch { }
            }

            // Fallback: login shell
            try {
                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = "/bin/bash",
                        Arguments = "-l -c \"which npm\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (!string.IsNullOrEmpty(result) && File.Exists(result)) return _cachedNpmPath = result;
            } catch { }

            return "npm";
#endif
        }
    }
}
