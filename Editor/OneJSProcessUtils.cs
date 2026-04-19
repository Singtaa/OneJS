using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// Helpers for managing editor-spawned processes.
    /// </summary>
    public static class OneJSProcessUtils {
        /// <summary>
        /// Kills a process and all of its descendants.
        ///
        /// Necessary because Process.Kill() on Windows only terminates the direct
        /// process, leaving npm → node → cmd → node → esbuild children orphaned.
        /// Those orphans keep running after Unity exits, hold file handles in the
        /// working directory, and accumulate across Play/Stop cycles.
        /// </summary>
        public static void KillProcessTree(Process process) {
            if (process == null) return;

            int pid;
            try {
                if (process.HasExited) return;
                pid = process.Id;
            } catch {
                return;
            }

#if UNITY_EDITOR_WIN
            KillProcessTreeWindows(pid, process);
#else
            KillProcessTreeUnix(pid);
            try { if (!process.HasExited) process.Kill(); } catch { }
#endif
        }

#if UNITY_EDITOR_WIN
        static void KillProcessTreeWindows(int pid, Process fallback) {
            try {
                using var taskkill = Process.Start(new ProcessStartInfo {
                    FileName = "taskkill",
                    Arguments = $"/T /F /PID {pid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                taskkill?.WaitForExit(2000);
            } catch (Exception ex) {
                Debug.LogWarning($"[OneJS] taskkill /T /F /PID {pid} failed: {ex.Message}. Falling back to Process.Kill().");
                try { if (!fallback.HasExited) fallback.Kill(); } catch { }
            }
        }
#else
        static void KillProcessTreeUnix(int pid) {
            var descendants = new List<int>();
            CollectDescendants(pid, descendants);
            // Deepest first so parents can't re-spawn children between kills.
            for (int i = descendants.Count - 1; i >= 0; i--) {
                KillUnixPid(descendants[i]);
            }
            KillUnixPid(pid);
        }

        static void CollectDescendants(int pid, List<int> sink) {
            try {
                using var pgrep = Process.Start(new ProcessStartInfo {
                    FileName = "/usr/bin/pgrep",
                    Arguments = $"-P {pid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                });
                if (pgrep == null) return;
                string line;
                while ((line = pgrep.StandardOutput.ReadLine()) != null) {
                    if (int.TryParse(line.Trim(), out var child)) {
                        sink.Add(child);
                        CollectDescendants(child, sink);
                    }
                }
                pgrep.WaitForExit(1000);
            } catch { }
        }

        static void KillUnixPid(int pid) {
            try {
                using var kill = Process.Start(new ProcessStartInfo {
                    FileName = "/bin/kill",
                    Arguments = $"-9 {pid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                kill?.WaitForExit(500);
            } catch { }
        }
#endif
    }
}
