using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Provides file system access for JavaScript.
    /// Enables runtime loading of text files from absolute paths,
    /// useful for modding, theming, and runtime configuration.
    /// </summary>
    public static class FileSystem {
        /// <summary>
        /// Read a text file asynchronously from an absolute path.
        /// Works in Editor and standalone builds.
        /// </summary>
        /// <param name="path">Absolute path to the file</param>
        /// <returns>File contents as string</returns>
        /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
        /// <exception cref="IOException">If file cannot be read</exception>
        /// <example>
        /// // JavaScript usage:
        /// const content = await CS.OneJS.FileSystem.ReadTextFileAsync(__persistentDataPath + "/config.json");
        /// // Or via the global helper:
        /// const content = await readTextFile(__persistentDataPath + "/config.json");
        /// </example>
        public static async Task<string> ReadTextFileAsync(string path) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }

            if (!File.Exists(path)) {
                throw new FileNotFoundException($"File not found: {path}", path);
            }

            // Use async file reading to avoid blocking the main thread
            // For very large files, this prevents frame drops
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL doesn't support async file I/O, use sync read
            return File.ReadAllText(path);
#else
            return await Task.Run(() => File.ReadAllText(path));
#endif
        }

        /// <summary>
        /// Check if a file exists at the given path.
        /// </summary>
        /// <param name="path">Absolute path to check</param>
        /// <returns>True if file exists</returns>
        public static bool FileExists(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            return File.Exists(path);
        }

        /// <summary>
        /// Check if a directory exists at the given path.
        /// </summary>
        /// <param name="path">Absolute path to check</param>
        /// <returns>True if directory exists</returns>
        public static bool DirectoryExists(string path) {
            if (string.IsNullOrEmpty(path)) return false;
            return Directory.Exists(path);
        }

        /// <summary>
        /// Write text to a file asynchronously.
        /// Creates the file if it doesn't exist, overwrites if it does.
        /// </summary>
        /// <param name="path">Absolute path to the file</param>
        /// <param name="content">Content to write</param>
        /// <example>
        /// // JavaScript usage:
        /// await CS.OneJS.FileSystem.WriteTextFileAsync(__persistentDataPath + "/save.json", JSON.stringify(data));
        /// // Or via the global helper:
        /// await writeTextFile(__persistentDataPath + "/save.json", JSON.stringify(data));
        /// </example>
        public static async Task WriteTextFileAsync(string path, string content) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            File.WriteAllText(path, content ?? "");
#else
            await Task.Run(() => File.WriteAllText(path, content ?? ""));
#endif
        }

        /// <summary>
        /// Delete a file at the given path.
        /// </summary>
        /// <param name="path">Absolute path to the file</param>
        /// <returns>True if file was deleted, false if it didn't exist</returns>
        public static bool DeleteFile(string path) {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                return false;
            }
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// List files in a directory matching an optional pattern.
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <param name="pattern">Search pattern (e.g., "*.uss", "*.json"). Default is "*"</param>
        /// <param name="recursive">Search subdirectories</param>
        /// <returns>List of file paths</returns>
        public static List<string> ListFiles(string path, string pattern = "*", bool recursive = false) {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) {
                return new List<string>();
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.GetFiles(path, pattern ?? "*", option).ToList();
        }
    }
}
