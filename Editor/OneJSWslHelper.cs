using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;

namespace OneJS.Editor {
    /// <summary>
    /// Helper for using WSL (Windows Subsystem for Linux) instead of the Windows console
    /// for terminal and npm commands. When enabled, "Open Terminal" launches WSL and
    /// npm is run via wsl.exe so Node/npm from your WSL distro are used.
    /// </summary>
    public static class OneJSWslHelper {
        const string UseWslKey = "OneJS.UseWsl";

        public static bool UseWsl {
            get => EditorPrefs.GetBool(UseWslKey, false);
            set => EditorPrefs.SetBool(UseWslKey, value);
        }

#if UNITY_EDITOR_WIN
        static bool? _wslInstalledCached;

        /// <summary>
        /// Returns true if wsl.exe is present in System32 (WSL is installed).
        /// Result is cached for the session.
        /// </summary>
        public static bool IsWslInstalled {
            get {
                if (_wslInstalledCached == null) {
                    var wslPath = Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.Windows), "System32", "wsl.exe");
                    _wslInstalledCached = File.Exists(wslPath);
                }
                return _wslInstalledCached.Value;
            }
        }
#else
        public static bool IsWslInstalled => false;
#endif

        /// <summary>
        /// Converts a Windows path to a WSL path (e.g. D:\foo\bar -> /mnt/d/foo/bar).
        /// No-op on non-Windows or if path is null/empty.
        /// </summary>
        public static string ToWslPath(string windowsPath) {
            if (string.IsNullOrEmpty(windowsPath)) return windowsPath;
            var path = Path.GetFullPath(windowsPath).Replace('\\', '/');
            if (path.Length >= 2 && path[1] == ':') {
                var drive = char.ToLowerInvariant(path[0]);
                return "/mnt/" + drive + path.Substring(2);
            }
            return path;
        }

        /// <summary>
        /// Builds wsl.exe arguments to run npm in a shell with nvm/fnm and profile sourced
        /// so Node is on PATH (WSL non-interactive often doesn't load these).
        /// </summary>
        public static string GetWslNpmArguments(string workingDir, string npmArguments) {
            var wslPath = ToWslPath(workingDir);
            var escapedPath = wslPath.Replace("'", "'\\''");
            // Source nvm/fnm and profile so npm is on PATH; then cd and run npm
            const string preamble = "source ~/.nvm/nvm.sh 2>/dev/null; source ~/.bashrc 2>/dev/null; source ~/.profile 2>/dev/null; ";
            return "bash -c \"" + preamble + "cd '" + escapedPath + "' && npm " + npmArguments + "\"";
        }

#if UNITY_EDITOR_WIN
        static string _cachedWindowsNpmPath;

        /// <summary>
        /// Resolves npm to a full path on Windows (Unity often doesn't inherit PATH).
        /// Tries "where npm.cmd" then common Node.js install locations.
        /// </summary>
        public static string GetWindowsNpmPath() {
            if (!string.IsNullOrEmpty(_cachedWindowsNpmPath)) return _cachedWindowsNpmPath;
            try {
                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = "cmd.exe",
                        Arguments = "/c where npm.cmd",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                var firstLine = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                    return _cachedWindowsNpmPath = firstLine;
            } catch { }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] candidates = {
                Path.Combine(programFiles, "nodejs", "npm.cmd"),
                Path.Combine(programFilesX86, "nodejs", "npm.cmd"),
                Path.Combine(localAppData, "Programs", "node", "npm.cmd"),
                Path.Combine(appData, "npm", "npm.cmd"),
            };
            foreach (var p in candidates) {
                if (File.Exists(p))
                    return _cachedWindowsNpmPath = p;
            }
            return _cachedWindowsNpmPath = "npm.cmd";
        }
#endif
    }
}
