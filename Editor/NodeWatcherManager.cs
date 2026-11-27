using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    ///
    /// InitializeOnLoad so that after every domain reload it reattaches to each watcher this
    /// session started, and registers its quit handler, whether or not a runner asks for its
    /// watcher again. A watcher whose scene was closed when a reload happened used to be
    /// forgotten, and outlived the editor (#143).
    /// </summary>
    [InitializeOnLoad]
    public static class NodeWatcherManager {
        const string PidPrefKeyPrefix = "OneJS.Watcher.Pid.";
        const string OutputPrefKeyPrefix = "OneJS.Watcher.Output.";
        /// <summary>The npm arguments each watcher was started with, so one restarted after a reload runs what it ran: a root cart's is its own ojplay's build, not run watch.</summary>
        const string ArgsPrefKeyPrefix = "OneJS.Watcher.Args.";
        const string DefaultArguments = "run watch";
        /// <summary>The working directories this session's watchers run in, one per line. SessionState, so a domain reload keeps it and a new session starts empty.</summary>
        const string WatchedDirsKey = "OneJS.Watcher.Dirs";

        static readonly Dictionary<string, Process> _watchers = new();
        /// <summary>Written by the watchers' reader threads, so every access holds _outputLock. EditorPrefs is main thread only: the buffers are saved there before a reload (OnBeforeReload).</summary>
        static readonly Dictionary<string, StringBuilder> _outputBuffers = new();
        static readonly object _outputLock = new();
        static readonly HashSet<string> _starting = new();
        static readonly Dictionary<string, int> _outputPendingCount = new();  // Track pending output streams

        public static event Action<string> OnWatcherStarted;  // workingDir
        public static event Action<string> OnWatcherStopped;  // workingDir
        public static event Action<string, string> OnWatcherOutput;  // workingDir, message

        static NodeWatcherManager() {
            // Register for domain reload and editor quit
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnEditorQuitting;
            ReattachAll();
        }

        /// <summary>
        /// Reattaches to every watcher this session started that is still running, so StopAll
        /// knows about it when the editor quits.
        /// </summary>
        internal static void ReattachAll() {
            foreach (var dir in WatchedDirs()) TryReattach(dir);
        }

        static List<string> WatchedDirs() =>
            SessionState.GetString(WatchedDirsKey, "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        /// <summary>Whether this session has a watcher on record in this working directory, running or not yet reattached.</summary>
        internal static bool IsTracked(string workingDir) => WatchedDirs().Contains(workingDir);

        /// <summary>Takes a started watcher under management: the one place it is recorded for reattaching.</summary>
        internal static void Track(string workingDir, Process process, string npmArguments = DefaultArguments) {
            var key = GetKey(workingDir);
            _watchers[key] = process;
            // Save PID for reattachment after domain reload
            EditorPrefs.SetInt(PidPrefKeyPrefix + key, process.Id);
            EditorPrefs.SetString(ArgsPrefKeyPrefix + key, npmArguments);
            var dirs = WatchedDirs();
            if (!dirs.Contains(workingDir)) {
                dirs.Add(workingDir);
                SessionState.SetString(WatchedDirsKey, string.Join("\n", dirs));
            }
        }

        // Public API

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
            lock (_outputLock) {
                if (_outputBuffers.TryGetValue(key, out var buffer)) return buffer.ToString();
            }
            // Try to restore from EditorPrefs (after domain reload)
            return EditorPrefs.GetString(OutputPrefKeyPrefix + key, "");
        }

        /// <summary>
        /// Takes back a watcher that was running before a domain reload, by starting it again.
        /// The reload dropped the editor's end of its stdout and stderr pipes, and nothing can
        /// pick them up again: a process adopted by its pid alone writes into pipes nobody reads,
        /// so its next line, a build error, never reached the Console and ended the watcher. A
        /// fresh one has pipes the editor reads, and its first build writes nothing new.
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

                // Sanity guard against PID reuse. The tracked top-level process is node
                // on Unix, but on Windows it's cmd.exe: Process.Start wraps npm.cmd (a
                // batch file) in cmd.exe when UseShellExecute=false, so ProcessName is
                // "cmd" even though the real watcher is node running underneath.
                var processName = process.ProcessName.ToLowerInvariant();
                bool validName = processName.Contains("node") || processName.Contains("cmd");
                if (!validName) {
                    ClearSavedState(key);
                    return false;
                }

                // Started again, also for a watcher an earlier session left running.
                var npmArguments = EditorPrefs.GetString(ArgsPrefKeyPrefix + key, DefaultArguments);
                var savedOutput = EditorPrefs.GetString(OutputPrefKeyPrefix + key, "");
                try {
                    OneJSProcessUtils.KillProcessTree(process);
                    process.WaitForExit(2000);
                } finally {
                    process.Dispose();
                }
                ClearSavedState(key);
                return Start(workingDir, npmArguments, savedOutput);
            } catch (ArgumentException) {
                ClearSavedState(key);
                return false;
            } catch (Exception e) {
                // Expected failure: the saved process is gone or no longer ours.
                Debug.LogWarning($"[OneJS] Failed to reattach to watcher: {e.Message}");
                ClearSavedState(key);
                return false;
            }
        }

        /// <summary>
        /// Start a watcher for the given working directory.
        /// </summary>
        public static bool StartWatcher(string workingDir) => StartWatcher(workingDir, "run watch");

        /// <summary>
        /// Start a watcher for the given working directory that runs these npm arguments, for a
        /// working directory whose watch is not its package.json's watch script: a root cart's
        /// is its own ojplay's Unity build.
        /// </summary>
        public static bool StartWatcher(string workingDir, string npmArguments) {
            // A watcher from before a reload is started again, with the arguments it had
            if (TryReattach(workingDir)) {
                return true;
            }
            return Start(workingDir, npmArguments, "");
        }

        static bool Start(string workingDir, string npmArguments, string earlierOutput) {
            var key = GetKey(workingDir);

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
                var startInfo = OneJSWslHelper.CreateNpmProcessStartInfo(workingDir, npmArguments, GetNpmExecutable());

                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                // Initialize output buffer and pending stream count (stdout + stderr)
                lock (_outputLock) _outputBuffers[key] = new StringBuilder(earlierOutput);
                _outputPendingCount[key] = 2;

                process.OutputDataReceived += (s, e) => {
                    if (e.Data != null) {
                        if (!string.IsNullOrEmpty(e.Data)) {
                            AppendOutput(key, e.Data);
                            OnWatcherOutput?.Invoke(workingDir, e.Data);
                            Debug.Log($"[Watch] {e.Data}");
                        }
                    } else {
                        // null means end of stream: check if all streams are done
                        HandleStreamEnd(key, workingDir);
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (e.Data != null) {
                        if (!string.IsNullOrEmpty(e.Data)) {
                            AppendOutput(key, e.Data);
                            OnWatcherOutput?.Invoke(workingDir, e.Data);
                            // esbuild outputs to stderr even for non-errors
                            Debug.Log($"[Watch] {e.Data}");
                        }
                    } else {
                        // null means end of stream: check if all streams are done
                        HandleStreamEnd(key, workingDir);
                    }
                };

                process.Exited += (s, e) => {
                    // Don't remove watcher here: wait for streams to finish via HandleStreamEnd
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Track(workingDir, process, npmArguments);

                OnWatcherStarted?.Invoke(workingDir);

                return true;
            } catch (Exception e) {
                // Expected failure: npm could not be started (not installed, not on PATH).
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
                        OneJSProcessUtils.KillProcessTree(process);
                        process.WaitForExit(2000);
                    } catch (Exception e) {
                        // Expected failure: the process exited while being stopped.
                        Debug.LogWarning($"[OneJS] Error stopping watcher: {e.Message}");
                    }
                }

                try { process?.Dispose(); } catch { }
                _watchers.Remove(key);
            }

            ClearSavedState(key);
            OnWatcherStopped?.Invoke(workingDir);
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
                            OneJSProcessUtils.KillProcessTree(process);
                            process.WaitForExit(1000);
                        } catch { }
                    }
                    try { process?.Dispose(); } catch { }
                }
                ClearSavedState(key);
            }
            _watchers.Clear();
            lock (_outputLock) _outputBuffers.Clear();
            _outputPendingCount.Clear();
        }

        // Internal

        static string GetKey(string workingDir) {
            // Use a hash of the path to create a valid EditorPrefs key
            return workingDir.GetHashCode().ToString("X8");
        }

        static void ClearSavedState(string key) {
            EditorPrefs.DeleteKey(PidPrefKeyPrefix + key);
            EditorPrefs.DeleteKey(OutputPrefKeyPrefix + key);
            EditorPrefs.DeleteKey(ArgsPrefKeyPrefix + key);
            var dirs = WatchedDirs();
            if (dirs.RemoveAll(dir => GetKey(dir) == key) > 0) SessionState.SetString(WatchedDirsKey, string.Join("\n", dirs));
            lock (_outputLock) _outputBuffers.Remove(key);
            _outputPendingCount.Remove(key);
        }

        static void HandleStreamEnd(string key, string workingDir) {
            if (!_outputPendingCount.TryGetValue(key, out var count)) return;

            count--;
            _outputPendingCount[key] = count;

            if (count <= 0) {
                // All streams have finished: now safe to clean up
                _watchers.Remove(key);
                ClearSavedState(key);
                OnWatcherStopped?.Invoke(workingDir);
            }
        }

        /// <summary>
        /// Called on a reader thread. It touches no Unity API but the thread-safe Debug.Log: an
        /// EditorPrefs write here threw on the first line, which ended the reading, so nothing a
        /// watcher said after it ever reached the Console.
        /// </summary>
        static void AppendOutput(string key, string line) {
            lock (_outputLock) {
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
            }
        }

        static void OnBeforeReload() {
            // Watchers keep running through the reload; TryReattach starts each again after it.
            // Their output is kept for the inspector, from the main thread EditorPrefs needs.
            lock (_outputLock) {
                foreach (var pair in _outputBuffers) EditorPrefs.SetString(OutputPrefKeyPrefix + pair.Key, pair.Value.ToString());
            }
        }

        static void OnEditorQuitting() {
            StopAll();
        }

        // npm Resolution

        static string _cachedNpmPath;

        internal static string GetNpmExecutable() {
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
