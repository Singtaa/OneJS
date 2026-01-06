using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Manages Node.js watcher processes for JSRunner instances.
    /// Each JSRunner can have its own watcher process that survives domain reloads.
    ///
    /// Uses EditorPrefs to persist PIDs across domain reloads, allowing reattachment
    /// to running watcher processes.
    /// </summary>
    public static class NodeWatcherManager {
        const string PidPrefKeyPrefix = "OneJS.Watcher.Pid.";
        const string OutputPrefKeyPrefix = "OneJS.Watcher.Output.";

        static readonly Dictionary<string, Process> _watchers = new();
        static readonly Dictionary<string, StringBuilder> _outputBuffers = new();
        static readonly HashSet<string> _starting = new();

        public static event Action<string> OnWatcherStarted;  // workingDir
        public static event Action<string> OnWatcherStopped;  // workingDir
        public static event Action<string, string> OnWatcherOutput;  // workingDir, message

        static NodeWatcherManager() {
            // Register for domain reload and editor quit
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        // MARK: Public API

        /// <summary>
        /// Check if a watcher is running for the given working directory.
        /// </summary>
        public static bool IsRunning(string workingDir) {
            var key = GetKey(workingDir);
            return _watchers.TryGetValue(key, out var process) && process != null && !process.HasExited;
        }

        /// <summary>
        /// Check if a watcher is currently starting.
        /// </summary>
        public static bool IsStarting(string workingDir) {
            return _starting.Contains(GetKey(workingDir));
        }

        /// <summary>
        /// Get the recent output from the watcher.
        /// </summary>
        public static string GetOutput(string workingDir) {
            var key = GetKey(workingDir);
            if (_outputBuffers.TryGetValue(key, out var buffer)) {
                return buffer.ToString();
            }
            // Try to restore from EditorPrefs (after domain reload)
            return EditorPrefs.GetString(OutputPrefKeyPrefix + key, "");
        }

        /// <summary>
        /// Try to reattach to a watcher process that was running before domain reload.
        /// </summary>
        public static bool TryReattach(string workingDir) {
            var key = GetKey(workingDir);

            if (_watchers.ContainsKey(key)) return true;

            var savedPid = EditorPrefs.GetInt(PidPrefKeyPrefix + key, -1);
            if (savedPid <= 0) return false;

            try {
                var process = Process.GetProcessById(savedPid);

                if (process.HasExited) {
                    ClearSavedState(key);
                    return false;
                }

                // Verify it's a Node process
                var processName = process.ProcessName.ToLowerInvariant();
                if (!processName.Contains("node")) {
                    ClearSavedState(key);
                    return false;
                }

                _watchers[key] = process;

                // Re-register exit handler
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) => {
                    _watchers.Remove(key);
                    ClearSavedState(key);
                    OnWatcherStopped?.Invoke(workingDir);
                };

                // Restore output buffer
                var savedOutput = EditorPrefs.GetString(OutputPrefKeyPrefix + key, "");
                _outputBuffers[key] = new StringBuilder(savedOutput);

                Debug.Log($"[OneJS] Reattached to watcher process (PID {savedPid}) for {Path.GetFileName(workingDir)}");
                OnWatcherStarted?.Invoke(workingDir);
                return true;
            } catch (ArgumentException) {
                ClearSavedState(key);
                return false;
            } catch (Exception e) {
                Debug.LogWarning($"[OneJS] Failed to reattach to watcher: {e.Message}");
                ClearSavedState(key);
                return false;
            }
        }

        /// <summary>
        /// Start a watcher for the given working directory.
        /// </summary>
        public static bool StartWatcher(string workingDir) {
            var key = GetKey(workingDir);

            // Try reattaching first
            if (TryReattach(workingDir)) {
                return true;
            }

            if (IsRunning(workingDir) || _starting.Contains(key)) {
                return false;
            }

            // Validate directory
            if (!Directory.Exists(workingDir)) {
                Debug.LogError($"[OneJS] Working directory not found: {workingDir}");
                return false;
            }

            if (!File.Exists(Path.Combine(workingDir, "package.json"))) {
                Debug.LogError($"[OneJS] package.json not found in {workingDir}");
                return false;
            }

            if (!Directory.Exists(Path.Combine(workingDir, "node_modules"))) {
                Debug.LogError($"[OneJS] node_modules not found in {workingDir}. Click 'Install' first.");
                return false;
            }

            _starting.Add(key);

            try {
                var npmPath = GetNpmExecutable();
                var nodeBinDir = Path.GetDirectoryName(npmPath);

                var startInfo = new ProcessStartInfo {
                    FileName = npmPath,
                    Arguments = "run watch",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Ensure PATH includes node directory
                var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!string.IsNullOrEmpty(nodeBinDir)) {
                    startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;
                }

                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                // Initialize output buffer
                _outputBuffers[key] = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        AppendOutput(key, e.Data);
                        OnWatcherOutput?.Invoke(workingDir, e.Data);
                        Debug.Log($"[Watch] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        AppendOutput(key, e.Data);
                        OnWatcherOutput?.Invoke(workingDir, e.Data);
                        // esbuild outputs to stderr even for non-errors
                        Debug.Log($"[Watch] {e.Data}");
                    }
                };

                process.Exited += (s, e) => {
                    _watchers.Remove(key);
                    ClearSavedState(key);
                    OnWatcherStopped?.Invoke(workingDir);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _watchers[key] = process;

                // Save PID for reattachment after domain reload
                EditorPrefs.SetInt(PidPrefKeyPrefix + key, process.Id);

                Debug.Log($"[OneJS] Started watcher for {Path.GetFileName(workingDir)}");
                OnWatcherStarted?.Invoke(workingDir);

                return true;
            } catch (Exception e) {
                Debug.LogError($"[OneJS] Failed to start watcher: {e.Message}");
                return false;
            } finally {
                _starting.Remove(key);
            }
        }

        /// <summary>
        /// Stop the watcher for the given working directory.
        /// </summary>
        public static void StopWatcher(string workingDir) {
            var key = GetKey(workingDir);

            if (_watchers.TryGetValue(key, out var process)) {
                if (process != null && !process.HasExited) {
                    try {
                        process.Kill();
                        process.WaitForExit(2000);
                    } catch (Exception e) {
                        Debug.LogWarning($"[OneJS] Error stopping watcher: {e.Message}");
                    }
                }

                try { process?.Dispose(); } catch { }
                _watchers.Remove(key);
            }

            ClearSavedState(key);
            OnWatcherStopped?.Invoke(workingDir);
            Debug.Log($"[OneJS] Stopped watcher for {Path.GetFileName(workingDir)}");
        }

        /// <summary>
        /// Stop all watchers.
        /// </summary>
        public static void StopAll() {
            var keys = new List<string>(_watchers.Keys);
            foreach (var key in keys) {
                if (_watchers.TryGetValue(key, out var process)) {
                    if (process != null && !process.HasExited) {
                        try {
                            process.Kill();
                            process.WaitForExit(1000);
                        } catch { }
                    }
                    try { process?.Dispose(); } catch { }
                }
                ClearSavedState(key);
            }
            _watchers.Clear();
            _outputBuffers.Clear();
        }

        // MARK: Internal

        static string GetKey(string workingDir) {
            // Use a hash of the path to create a valid EditorPrefs key
            return workingDir.GetHashCode().ToString("X8");
        }

        static void ClearSavedState(string key) {
            EditorPrefs.DeleteKey(PidPrefKeyPrefix + key);
            EditorPrefs.DeleteKey(OutputPrefKeyPrefix + key);
            _outputBuffers.Remove(key);
        }

        static void AppendOutput(string key, string line) {
            if (!_outputBuffers.TryGetValue(key, out var buffer)) {
                buffer = new StringBuilder();
                _outputBuffers[key] = buffer;
            }

            buffer.AppendLine(line);

            // Keep only last 50 lines
            var lines = buffer.ToString().Split('\n');
            if (lines.Length > 50) {
                buffer.Clear();
                for (int i = lines.Length - 50; i < lines.Length; i++) {
                    buffer.AppendLine(lines[i]);
                }
            }

            // Persist to EditorPrefs for domain reload survival
            EditorPrefs.SetString(OutputPrefKeyPrefix + key, buffer.ToString());
        }

        static void OnBeforeReload() {
            // Don't stop watchers on domain reload - they keep running
            // We'll reattach via TryReattach after reload
        }

        static void OnEditorQuitting() {
            StopAll();
        }

        // MARK: npm Resolution

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
