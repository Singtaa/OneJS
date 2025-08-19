using System;
using System.Diagnostics;
using System.IO;

namespace OneJS.Editor {
    public class OneJSEditorUtil {
        public static void OpenDir(string path) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var processName = "explorer.exe";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var processName = "open";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            var processName = "xdg-open";
#else
            var processName = "unknown";
            UnityEngine.Debug.LogWarning("Unknown platform. Cannot open folder");
#endif
            var argStr = $"\"{Path.GetFullPath(path)}\"";
            var proc = new Process() {
                StartInfo = new ProcessStartInfo() {
                    FileName = processName,
                    Arguments = argStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
                },
            };
            proc.Start();
        }

        public static void VSCodeOpenDir(string path) {
            var full = Path.GetFullPath(path);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var exe = GetCodeExecutablePathOnWindows();
            if (string.IsNullOrEmpty(exe)) {
                UnityEngine.Debug.LogWarning("VSCode not found. Is it installed?");
                return;
            }
            StartProcess(new ProcessStartInfo {
                FileName = exe,
                Arguments = $"\"{full}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
#elif UNITY_EDITOR_OSX
            // Use the user's login shell to get the real PATH, then run `code -n "<dir>"`
            LaunchViaLoginShell($"code -n -- {ShQ(full)}");
            // Fallback if `code` CLI isn’t installed
            if (!_lastProcessStarted) {
                StartProcess(new ProcessStartInfo {
                    FileName = "/usr/bin/open",
                    Arguments = $"-n -b com.microsoft.VSCode --args {ShQ(full)}",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                });
            }
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        LaunchViaLoginShell($"code -n \"{EscapeQuotes(full)}\"");
#else
        UnityEngine.Debug.LogWarning("Unsupported platform for VSCode launch.");
#endif
        }

        static bool _lastProcessStarted;

        static void LaunchViaLoginShell(string command) {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell)) shell = "/bin/zsh"; // macOS default; fine on most dev boxes

            // -l: login shell (loads ~/.zprofile or ~/.profile), -i: interactive (loads ~/.zshrc/bashrc)
            var psi = new ProcessStartInfo {
                FileName = shell,
                Arguments = $"-l -i -c \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            StartProcess(psi);
        }

        static void StartProcess(ProcessStartInfo psi) {
            try {
                _lastProcessStarted = Process.Start(psi) != null;
            } catch (Exception e) {
                _lastProcessStarted = false;
                UnityEngine.Debug.LogWarning($"Failed to start process: {e.Message}");
            }
        }

        static string EscapeQuotes(string s) => s.Replace("\"", "\\\"");
        static string ShQ(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";

        static string GetCodeExecutablePathOnWindows() {
            string[] possiblePaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Microsoft VS Code\Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Microsoft VS Code\Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Microsoft VS Code\Code.exe")
            };

            foreach (var p in possiblePaths)
                if (File.Exists(p))
                    return p;

            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath)) {
                foreach (var dir in envPath.Split(Path.PathSeparator)) {
                    var full = Path.Combine(dir, "Code.exe");
                    if (File.Exists(full)) return full;
                    var fullLower = Path.Combine(dir, "code.exe");
                    if (File.Exists(fullLower)) return fullLower;
                }
            }
            return null;
        }
    }
}
