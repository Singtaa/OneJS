using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Result of type generation. Contains the generated TypeScript content
    /// and provides methods to write it to various outputs.
    /// </summary>
    public class TypeGeneratorResult {
        /// <summary>
        /// The generated TypeScript declaration content.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// The source types that were used to generate this result.
        /// </summary>
        public IReadOnlyList<Type> SourceTypes { get; }

        /// <summary>
        /// The analyzed type information.
        /// </summary>
        public IReadOnlyList<TsTypeInfo> TypeInfos { get; }

        /// <summary>
        /// Number of types in this result.
        /// </summary>
        public int TypeCount => TypeInfos?.Count ?? 0;

        /// <summary>
        /// Size of the generated content in bytes (UTF-8).
        /// </summary>
        public int ContentSize => Encoding.UTF8.GetByteCount(Content ?? "");

        /// <summary>
        /// Number of lines in the generated content.
        /// </summary>
        public int LineCount => string.IsNullOrEmpty(Content) ? 0 : Content.Split('\n').Length;

        internal TypeGeneratorResult(string content, IReadOnlyList<Type> sourceTypes, IReadOnlyList<TsTypeInfo> typeInfos) {
            Content = content ?? "";
            SourceTypes = sourceTypes ?? Array.Empty<Type>();
            TypeInfos = typeInfos ?? Array.Empty<TsTypeInfo>();
        }

        #region Output Methods

        /// <summary>
        /// Writes the generated content to a file.
        /// </summary>
        /// <param name="path">Output path (relative to project root or absolute)</param>
        /// <param name="refreshAssetDatabase">Whether to refresh the AssetDatabase after writing</param>
        public void WriteTo(string path, bool refreshAssetDatabase = true) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Output path cannot be null or empty", nameof(path));
            }

            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, Content, Encoding.UTF8);

            if (refreshAssetDatabase) {
                AssetDatabase.Refresh();
            }

            Debug.Log($"[TypeGenerator] Generated {TypeCount} types to: {path} ({FormatBytes(ContentSize)})");
        }

        /// <summary>
        /// Writes the generated content to a TextWriter.
        /// </summary>
        /// <param name="writer">TextWriter to write to</param>
        public void WriteTo(TextWriter writer) {
            writer.Write(Content);
        }

        /// <summary>
        /// Returns the generated content as a string.
        /// </summary>
        public override string ToString() => Content;

        /// <summary>
        /// Implicit conversion to string for convenience.
        /// </summary>
        public static implicit operator string(TypeGeneratorResult result) => result?.Content ?? "";

        #endregion

        #region Combining Results

        /// <summary>
        /// Combines multiple results into a single result.
        /// Deduplicates types and merges content.
        /// </summary>
        /// <param name="results">Results to combine</param>
        /// <returns>A combined result</returns>
        public static TypeGeneratorResult Combine(params TypeGeneratorResult[] results) {
            if (results == null || results.Length == 0) {
                return new TypeGeneratorResult("", Array.Empty<Type>(), Array.Empty<TsTypeInfo>());
            }

            if (results.Length == 1) {
                return results[0];
            }

            // Collect unique types
            var allTypes = new HashSet<Type>();
            foreach (var result in results) {
                if (result?.SourceTypes != null) {
                    foreach (var type in result.SourceTypes) {
                        allTypes.Add(type);
                    }
                }
            }

            // Regenerate with combined types
            return TypeGenerator.Create()
                .AddTypes(allTypes)
                .Build();
        }

        /// <summary>
        /// Combines this result with another.
        /// </summary>
        /// <param name="other">Other result to combine with</param>
        /// <returns>A combined result</returns>
        public TypeGeneratorResult CombineWith(TypeGeneratorResult other) {
            return Combine(this, other);
        }

        #endregion

        #region Filtering

        /// <summary>
        /// Creates a new result containing only types matching the predicate.
        /// </summary>
        /// <param name="predicate">Filter predicate</param>
        /// <returns>A filtered result</returns>
        public TypeGeneratorResult Filter(Func<Type, bool> predicate) {
            var filteredTypes = SourceTypes.Where(predicate).ToList();
            return TypeGenerator.Create()
                .AddTypes(filteredTypes)
                .Build();
        }

        /// <summary>
        /// Creates a new result excluding types matching the predicate.
        /// </summary>
        /// <param name="predicate">Exclusion predicate</param>
        /// <returns>A filtered result</returns>
        public TypeGeneratorResult Exclude(Func<Type, bool> predicate) {
            return Filter(t => !predicate(t));
        }

        #endregion

        #region Helpers

        private static string FormatBytes(int bytes) {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        #endregion
    }
}
