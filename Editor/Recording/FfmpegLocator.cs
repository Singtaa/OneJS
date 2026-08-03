using System;
using System.Diagnostics;
using System.IO;

namespace OneJS.Editor {
    /// <summary>
    /// Finds the ffmpeg executable used to encode recordings.
    ///
    /// A Unity launched from Finder or the Hub inherits a minimal PATH that has
    /// none of the usual package-manager bin directories on it, so a bare
    /// "ffmpeg" would resolve for a terminal build but not for the editor. This
    /// mirrors the well-known-paths-then-login-shell strategy that
    /// <c>NodeWatcherManager</c> already uses to locate npm.
    /// </summary>
    public static class FfmpegLocator {
        static string s_Cached;

        /// <summary>
        /// Returns an absolute path to ffmpeg. Pass <paramref name="explicitPath"/>
        /// to bypass discovery. Throws <see cref="FileNotFoundException"/> with an
        /// install hint when ffmpeg is not present.
        /// </summary>
        public static string Resolve(string explicitPath = null) {
            if (!string.IsNullOrEmpty(explicitPath)) {
                if (!File.Exists(explicitPath))
                    throw new FileNotFoundException(
                        $"[OneJS] ffmpeg not found at '{explicitPath}'.", explicitPath);
                return explicitPath;
            }

            if (!string.IsNullOrEmpty(s_Cached) && File.Exists(s_Cached)) return s_Cached;

            var found = Search();
            if (string.IsNullOrEmpty(found)) {
                throw new FileNotFoundException(
                    "[OneJS] ffmpeg was not found. Install it (macOS: 'brew install ffmpeg', " +
                    "Windows: 'winget install Gyan.FFmpeg', Linux: your package manager) or set " +
                    "PanelRecordingOptions.FfmpegPath to an absolute path.");
            }
            return s_Cached = found;
        }

        static string Search() {
#if UNITY_EDITOR_WIN
            foreach (var candidate in new[] {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            }) {
                if (File.Exists(candidate)) return candidate;
            }
            return QueryShell("where", "ffmpeg");
#else
            foreach (var candidate in new[] {
                "/opt/homebrew/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/usr/bin/ffmpeg",
            }) {
                if (File.Exists(candidate)) return candidate;
            }
            // Login shell so user profile PATH entries (nix, macports, asdf) apply.
            return QueryShell("/bin/bash", "-l -c \"which ffmpeg\"");
#endif
        }

        static string QueryShell(string fileName, string arguments) {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // `where` can return several matches; take the first usable line.
                foreach (var line in output.Split('\n')) {
                    var path = line.Trim();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
            } catch (Exception) {
                // Discovery is best-effort; the caller reports the actionable error.
            }
            return null;
        }
    }
}
