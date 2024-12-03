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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var processName = GetCodeExecutablePathOnWindows();
#elif UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            var processName = GetCodeExecutablePathOnUnix();
#else
            var processName = "unknown";
            UnityEngine.Debug.LogWarning("Unknown platform. Cannot open VSCode folder");
            return;
#endif
            var argStr = $"\"{Path.GetFullPath(path)}\"";
            var proc = new Process() {
                StartInfo = new ProcessStartInfo() {
                    FileName = processName,
                    Arguments = argStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
        }

        static string GetCodeExecutablePathOnWindows() {
            string[] possiblePaths = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft VS Code\code.exe")
            };

            foreach (var path in possiblePaths) {
                if (File.Exists(path)) {
                    return path;
                }
            }

            // Additional search in PATH environment variable
            string pathEnvironmentVariable = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvironmentVariable != null) {
                foreach (var path in pathEnvironmentVariable.Split(Path.PathSeparator)) {
                    string fullPath = Path.Combine(path, "code.exe");
                    if (File.Exists(fullPath)) {
                        return fullPath;
                    }
                }
            }

            return null;
        }

        static string GetCodeExecutablePathOnUnix() {
            string[] possiblePaths = new string[] {
                "/usr/local/bin/code",
                "/usr/bin/code",
                "/snap/bin/code"
            };

            foreach (var path in possiblePaths) {
                if (File.Exists(path)) {
                    return path;
                }
            }

            // Additional search in PATH environment variable
            string pathEnvironmentVariable = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvironmentVariable != null) {
                foreach (var path in pathEnvironmentVariable.Split(Path.PathSeparator)) {
                    string fullPath = Path.Combine(path, "code");
                    if (File.Exists(fullPath)) {
                        return fullPath;
                    }
                }
            }

            return null;
        }
    }
}