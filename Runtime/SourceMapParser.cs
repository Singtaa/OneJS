using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Minimal source map parser for translating generated positions to original source positions.
    /// Supports Source Map v3 format.
    /// </summary>
    public class SourceMapParser {
        readonly string[] _sources;
        readonly List<MappingEntry>[] _mappingsByLine;

        struct MappingEntry {
            public int GeneratedColumn;
            public int SourceIndex;
            public int OriginalLine;
            public int OriginalColumn;
        }

        public struct OriginalPosition {
            public string Source;
            public int Line;
            public int Column;
            public bool Found;
        }

        SourceMapParser(string[] sources, List<MappingEntry>[] mappingsByLine) {
            _sources = sources;
            _mappingsByLine = mappingsByLine;
        }

        /// <summary>
        /// Load and parse a source map from file path.
        /// </summary>
        public static SourceMapParser Load(string mapFilePath) {
            if (!File.Exists(mapFilePath)) return null;

            try {
                var json = File.ReadAllText(mapFilePath);
                return Parse(json);
            } catch (Exception ex) {
                Debug.LogWarning($"[SourceMapParser] Failed to load source map: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse source map from JSON string.
        /// </summary>
        public static SourceMapParser Parse(string json) {
            // Simple JSON parsing for source map fields
            var sources = ParseStringArray(json, "sources");
            var mappings = ParseString(json, "mappings");

            if (sources == null || mappings == null) return null;

            var mappingsByLine = DecodeMappings(mappings);
            return new SourceMapParser(sources, mappingsByLine);
        }

        /// <summary>
        /// Get the original source position for a generated position.
        /// </summary>
        public OriginalPosition GetOriginalPosition(int generatedLine, int generatedColumn) {
            var result = new OriginalPosition { Found = false };

            // Lines are 1-indexed in stack traces, but our array is 0-indexed
            int lineIndex = generatedLine - 1;
            if (lineIndex < 0 || lineIndex >= _mappingsByLine.Length) return result;

            var lineMappings = _mappingsByLine[lineIndex];
            if (lineMappings == null || lineMappings.Count == 0) return result;

            // Find the mapping segment that contains this column (binary search for efficiency)
            MappingEntry? found = null;
            foreach (var entry in lineMappings) {
                if (entry.GeneratedColumn <= generatedColumn) {
                    found = entry;
                } else {
                    break;
                }
            }

            if (found.HasValue) {
                var entry = found.Value;
                result.Found = true;
                result.Source = entry.SourceIndex >= 0 && entry.SourceIndex < _sources.Length
                    ? _sources[entry.SourceIndex]
                    : "unknown";
                result.Line = entry.OriginalLine + 1; // Convert to 1-indexed
                result.Column = entry.OriginalColumn + 1;
            }

            return result;
        }

        /// <summary>
        /// Translate a JavaScript stack trace to show original source locations.
        /// </summary>
        public string TranslateStackTrace(string stackTrace) {
            if (string.IsNullOrEmpty(stackTrace)) return stackTrace;

            // Match patterns like "at <eval> (app.js:123:45)" or "at functionName (app.js:123:45)"
            var pattern = @"\(([^:]+):(\d+):(\d+)\)";
            return Regex.Replace(stackTrace, pattern, match => {
                var file = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int line) &&
                    int.TryParse(match.Groups[3].Value, out int column)) {
                    var original = GetOriginalPosition(line, column);
                    if (original.Found) {
                        return $"({original.Source}:{original.Line}:{original.Column})";
                    }
                }
                return match.Value;
            });
        }

        #region VLQ Decoding

        static readonly string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        static List<MappingEntry>[] DecodeMappings(string mappings) {
            var lines = new List<List<MappingEntry>>();
            var currentLine = new List<MappingEntry>();

            int generatedColumn = 0;
            int sourceIndex = 0;
            int originalLine = 0;
            int originalColumn = 0;

            int i = 0;
            while (i < mappings.Length) {
                char c = mappings[i];

                if (c == ';') {
                    // New line
                    lines.Add(currentLine);
                    currentLine = new List<MappingEntry>();
                    generatedColumn = 0;
                    i++;
                } else if (c == ',') {
                    // Next segment
                    i++;
                } else {
                    // Decode VLQ segment
                    var values = new List<int>();
                    while (i < mappings.Length && mappings[i] != ',' && mappings[i] != ';') {
                        int value = DecodeVLQ(mappings, ref i);
                        values.Add(value);
                    }

                    if (values.Count >= 1) {
                        generatedColumn += values[0];

                        if (values.Count >= 4) {
                            sourceIndex += values[1];
                            originalLine += values[2];
                            originalColumn += values[3];

                            currentLine.Add(new MappingEntry {
                                GeneratedColumn = generatedColumn,
                                SourceIndex = sourceIndex,
                                OriginalLine = originalLine,
                                OriginalColumn = originalColumn
                            });
                        }
                    }
                }
            }

            // Add final line
            lines.Add(currentLine);

            return lines.ToArray();
        }

        static int DecodeVLQ(string str, ref int index) {
            int result = 0;
            int shift = 0;
            bool continuation;

            do {
                if (index >= str.Length) break;
                char c = str[index++];
                int digit = Base64Chars.IndexOf(c);
                if (digit < 0) break;

                continuation = (digit & 32) != 0;
                digit &= 31;
                result |= digit << shift;
                shift += 5;
            } while (continuation);

            // Convert from VLQ signed representation
            bool negative = (result & 1) != 0;
            result >>= 1;
            return negative ? -result : result;
        }

        #endregion

        #region Simple JSON Parsing

        static string ParseString(string json, string key) {
            var pattern = $"\"{key}\"\\s*:\\s*\"([^\"]*)\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        static string[] ParseStringArray(string json, string key) {
            var pattern = $"\"{key}\"\\s*:\\s*\\[([^\\]]*)\\]";
            var match = Regex.Match(json, pattern);
            if (!match.Success) return null;

            var arrayContent = match.Groups[1].Value;
            var items = new List<string>();
            var itemPattern = "\"([^\"]*)\"";
            foreach (Match itemMatch in Regex.Matches(arrayContent, itemPattern)) {
                items.Add(itemMatch.Groups[1].Value);
            }
            return items.ToArray();
        }

        #endregion
    }
}
