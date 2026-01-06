using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
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

                // Check for node_modules
                var nodeModulesPath = Path.Combine(workingDir, "node_modules");
                if (!Directory.Exists(nodeModulesPath)) {
                    // Need to install dependencies first
                    Debug.Log($"[JSRunner] Installing dependencies for {runner.name}...");
                    RunNpmInstallThenWatch(workingDir, runner.name);
                } else {
                    // Start watcher directly
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

        static void RunNpmInstallThenWatch(string workingDir, string runnerName) {
            if (_pendingInstalls.Contains(workingDir)) return;
            _pendingInstalls.Add(workingDir);

            RunNpmCommand(workingDir, "install", () => {
                Debug.Log($"[JSRunner] Dependencies installed for {runnerName}");
                _pendingInstalls.Remove(workingDir);
                if (EditorApplication.isPlaying) {
                    StartWatcher(workingDir, runnerName);
                }
            }, code => {
                _pendingInstalls.Remove(workingDir);
                Debug.LogError($"[JSRunner] npm install failed for {runnerName} (exit code {code})");
            });
        }

        static void RunNpmCommand(string workingDir, string arguments, Action onSuccess, Action<int> onFailure) {
            try {
                var npmPath = GetNpmExecutable();
                var nodeBinDir = Path.GetDirectoryName(npmPath);

                var startInfo = new ProcessStartInfo {
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
            return _cachedNpmPath = "npm.cmd";
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
