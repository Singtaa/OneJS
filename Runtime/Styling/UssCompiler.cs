using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExCSS;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.CustomStyleSheets {
    /// <summary>
    /// Compiles USS (Unity Style Sheets) strings into StyleSheet assets at runtime.
    /// Uses ExCSS for parsing and reflection to access Unity's internal StyleSheetBuilder.
    /// </summary>
    public class UssCompiler {
        readonly StyleSheetBuilderWrapper _builder;
        readonly StylesheetParser _parser;
        readonly string _workingDir;

        int _currentLine;

        // Unit name to DimensionUnit mapping
        static readonly Dictionary<string, DimensionUnit> UnitMap = new Dictionary<string, DimensionUnit>(StringComparer.OrdinalIgnoreCase) {
            { "px", DimensionUnit.Pixel },
            { "%", DimensionUnit.Percent },
            { "s", DimensionUnit.Second },
            { "ms", DimensionUnit.Millisecond },
            { "deg", DimensionUnit.Degree },
            { "grad", DimensionUnit.Gradian },
            { "rad", DimensionUnit.Radian },
            { "turn", DimensionUnit.Turn },
        };

        // Keyword string to StyleKeyword mapping
        static readonly Dictionary<string, StyleKeyword> KeywordMap = new Dictionary<string, StyleKeyword>(StringComparer.OrdinalIgnoreCase) {
            { "auto", StyleKeyword.Auto },
            { "none", StyleKeyword.None },
            { "initial", StyleKeyword.Initial },
        };

        public UssCompiler(string workingDir = null) {
            _builder = new StyleSheetBuilderWrapper();
            _parser = new StylesheetParser(
                includeUnknownDeclarations: true,
                tolerateInvalidValues: true
            );
            _workingDir = workingDir ?? "";
        }

        /// <summary>
        /// Compiles a USS string into a StyleSheet asset.
        /// </summary>
        public void Compile(StyleSheet asset, string ussContent) {
            var stylesheet = _parser.Parse(ussContent);

            foreach (var rule in stylesheet.StyleRules) {
                _currentLine = GetRuleLine(rule);
                _builder.BeginRule(_currentLine);

                // Compile selector
                CompileSelector(rule.Selector);

                // Compile properties
                foreach (var declaration in rule.Style) {
                    if (declaration is Property property) {
                        CompileProperty(property);
                    }
                }

                _builder.EndRule();
            }

            _builder.BuildTo(asset);

            // Compute content hash
            var hash = new Hash128();
            var bytes = Encoding.UTF8.GetBytes(ussContent);
            if (bytes.Length > 0) {
                HashUtilities.ComputeHash128(bytes, ref hash);
            }
            asset.contentHash = hash.GetHashCode();
        }

        int GetRuleLine(IStyleRule rule) {
            try {
                var text = rule.StylesheetText;
                if (text != null) {
                    return text.Range.Start.Line;
                }
            } catch { }
            return 1;
        }

        #region Selector Compilation

        void CompileSelector(ISelector selector) {
            switch (selector) {
                case ListSelector listSelector:
                    foreach (var sub in listSelector) {
                        CompileSelector(sub);
                    }
                    break;

                case ComplexSelector complexSelector:
                    CompileComplexSelector(complexSelector);
                    break;

                case CompoundSelector compoundSelector:
                    CompileSimpleSelector(compoundSelector);
                    break;

                default:
                    CompileSingleSelector(selector);
                    break;
            }
        }

        void CompileComplexSelector(ComplexSelector complexSelector) {
            int specificity = CalculateSpecificity(complexSelector);

            using (_builder.BeginComplexSelector(specificity)) {
                var relationship = SelectorRelationship.None;
                int index = 0;
                int count = complexSelector.Length;

                foreach (var combinator in complexSelector) {
                    var parts = ExtractSelectorParts(combinator.Selector);
                    if (parts.Length > 0) {
                        _builder.AddSimpleSelector(parts, relationship);
                    }

                    index++;
                    if (index < count) {
                        if (combinator.Delimiter == Combinators.Child) {
                            relationship = SelectorRelationship.Child;
                        } else if (combinator.Delimiter == Combinators.Descendent) {
                            relationship = SelectorRelationship.Descendent;
                        } else {
                            relationship = SelectorRelationship.None;
                        }
                    }
                }
            }
        }

        void CompileSimpleSelector(ISelector selector) {
            var parts = ExtractSelectorParts(selector);
            if (parts.Length == 0) return;

            int specificity = CalculateSpecificity(parts);
            using (_builder.BeginComplexSelector(specificity)) {
                _builder.AddSimpleSelector(parts, SelectorRelationship.None);
            }
        }

        void CompileSingleSelector(ISelector selector) {
            var parts = ExtractSelectorParts(selector);
            if (parts.Length == 0) return;

            int specificity = CalculateSpecificity(parts);
            using (_builder.BeginComplexSelector(specificity)) {
                _builder.AddSimpleSelector(parts, SelectorRelationship.None);
            }
        }

        SelectorPart[] ExtractSelectorParts(ISelector selector) {
            var parts = new List<SelectorPart>();

            switch (selector) {
                case AllSelector:
                    parts.Add(SelectorPart.Wildcard());
                    break;

                case ClassSelector classSelector:
                    parts.Add(SelectorPart.Class(classSelector.Class));
                    break;

                case IdSelector idSelector:
                    parts.Add(SelectorPart.Id(idSelector.Id));
                    break;

                case TypeSelector typeSelector:
                    parts.Add(SelectorPart.TypeName(typeSelector.Name));
                    break;

                case PseudoClassSelector pseudoClassSelector:
                    parts.Add(SelectorPart.PseudoClass(pseudoClassSelector.Class));
                    break;

                case CompoundSelector compoundSelector:
                    foreach (var sub in compoundSelector) {
                        parts.AddRange(ExtractSelectorParts(sub));
                    }
                    break;

                default:
                    var textParts = ParseSelectorText(selector.Text);
                    parts.AddRange(textParts);
                    break;
            }

            return parts.ToArray();
        }

        static readonly Regex SelectorRegex = new Regex(
            @"(?<id>#[\w-]+)|(?<class>\.[\w-]+)|(?<pseudo>:[\w-]+)|(?<type>\w+)|(?<wildcard>\*)",
            RegexOptions.Compiled
        );

        SelectorPart[] ParseSelectorText(string text) {
            if (string.IsNullOrEmpty(text)) return Array.Empty<SelectorPart>();

            var parts = new List<SelectorPart>();
            var matches = SelectorRegex.Matches(text);

            foreach (Match match in matches) {
                if (match.Groups["id"].Success) {
                    parts.Add(SelectorPart.Id(match.Groups["id"].Value.Substring(1)));
                } else if (match.Groups["class"].Success) {
                    parts.Add(SelectorPart.Class(match.Groups["class"].Value.Substring(1)));
                } else if (match.Groups["pseudo"].Success) {
                    parts.Add(SelectorPart.PseudoClass(match.Groups["pseudo"].Value.Substring(1)));
                } else if (match.Groups["type"].Success) {
                    parts.Add(SelectorPart.TypeName(match.Groups["type"].Value));
                } else if (match.Groups["wildcard"].Success) {
                    parts.Add(SelectorPart.Wildcard());
                }
            }

            return parts.ToArray();
        }

        int CalculateSpecificity(ComplexSelector selector) {
            int specificity = 1;
            foreach (var combinator in selector) {
                var parts = ExtractSelectorParts(combinator.Selector);
                specificity += CalculateSpecificityForParts(parts);
            }
            return specificity;
        }

        int CalculateSpecificity(SelectorPart[] parts) {
            return 1 + CalculateSpecificityForParts(parts);
        }

        int CalculateSpecificityForParts(SelectorPart[] parts) {
            int specificity = 0;
            foreach (var part in parts) {
                switch (part.Type) {
                    case SelectorType.ID:
                        specificity += 100;
                        break;
                    case SelectorType.Class:
                    case SelectorType.PseudoClass:
                        specificity += 10;
                        break;
                    case SelectorType.Type:
                        specificity += 1;
                        break;
                }
            }
            return specificity;
        }

        #endregion

        #region Property Compilation

        void CompileProperty(Property property) {
            string name = property.Name;
            string value = property.Value;

            _builder.BeginProperty(name, _currentLine);
            ParseAndAddValue(value);
            _builder.EndProperty();
        }

        // Regex patterns for parsing CSS values
        static readonly Regex ColorHexRegex = new Regex(@"^#([0-9a-fA-F]{3,8})$", RegexOptions.Compiled);
        static readonly Regex RgbRegex = new Regex(@"^rgba?\s*\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+))?\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex NumberWithUnitRegex = new Regex(@"^(-?[\d.]+)(px|%|s|ms|deg|grad|rad|turn)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex UrlRegex = new Regex(@"^url\s*\(\s*['""]?(.+?)['""]?\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ResourceRegex = new Regex(@"^resource\s*\(\s*['""]?(.+?)['""]?\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex VarRegex = new Regex(@"^var\s*\(\s*(.+)\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        void ParseAndAddValue(string value) {
            if (string.IsNullOrWhiteSpace(value)) return;

            value = value.Trim();

            // Check for comma-separated values (e.g., font-family fallbacks, transitions)
            if (value.Contains(",") && !value.StartsWith("rgb") && !value.StartsWith("rgba")) {
                var parts = SplitCssValue(value);
                for (int i = 0; i < parts.Count; i++) {
                    if (i > 0) _builder.AddCommaSeparator();
                    ParseSingleValue(parts[i].Trim());
                }
            } else {
                // Check for space-separated values (e.g., margin: 10px 20px)
                var parts = SplitSpaceSeparated(value);
                foreach (var part in parts) {
                    ParseSingleValue(part);
                }
            }
        }

        List<string> SplitCssValue(string value) {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0) {
                    result.Add(value.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(value.Substring(start));
            return result;
        }

        List<string> SplitSpaceSeparated(string value) {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int i = 0; i < value.Length; i++) {
                char c = value[i];

                if ((c == '"' || c == '\'') && (i == 0 || value[i - 1] != '\\')) {
                    if (!inQuotes) {
                        inQuotes = true;
                        quoteChar = c;
                    } else if (c == quoteChar) {
                        inQuotes = false;
                    }
                } else if (!inQuotes) {
                    if (c == '(' || c == '[') depth++;
                    else if (c == ')' || c == ']') depth--;
                    else if (char.IsWhiteSpace(c) && depth == 0) {
                        if (i > start) {
                            result.Add(value.Substring(start, i - start));
                        }
                        start = i + 1;
                    }
                }
            }

            if (start < value.Length) {
                result.Add(value.Substring(start));
            }

            return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        void ParseSingleValue(string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            value = value.Trim();

            // Try var() function
            var varMatch = VarRegex.Match(value);
            if (varMatch.Success) {
                CompileVarFunction(varMatch.Groups[1].Value.Trim());
                return;
            }

            // Try hex color
            var hexMatch = ColorHexRegex.Match(value);
            if (hexMatch.Success) {
                if (ColorUtility.TryParseHtmlString(value, out var color)) {
                    _builder.AddValue(color);
                    return;
                }
            }

            // Try rgb/rgba
            var rgbMatch = RgbRegex.Match(value);
            if (rgbMatch.Success) {
                float r = float.Parse(rgbMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                float g = float.Parse(rgbMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                float b = float.Parse(rgbMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                float a = rgbMatch.Groups[4].Success
                    ? float.Parse(rgbMatch.Groups[4].Value, CultureInfo.InvariantCulture)
                    : 1f;

                // If values are > 1, assume 0-255 range
                if (r > 1 || g > 1 || b > 1) {
                    r /= 255f;
                    g /= 255f;
                    b /= 255f;
                }

                _builder.AddValue(new UnityEngine.Color(r, g, b, a));
                return;
            }

            // Try url()
            var urlMatch = UrlRegex.Match(value);
            if (urlMatch.Success) {
                LoadAndAddAsset(urlMatch.Groups[1].Value);
                return;
            }

            // Try resource()
            var resourceMatch = ResourceRegex.Match(value);
            if (resourceMatch.Success) {
                _builder.AddValue(resourceMatch.Groups[1].Value, StyleValueType.ResourcePath);
                return;
            }

            // Try number with unit
            var numMatch = NumberWithUnitRegex.Match(value);
            if (numMatch.Success) {
                float num = float.Parse(numMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                string unit = numMatch.Groups[2].Value;

                if (string.IsNullOrEmpty(unit)) {
                    _builder.AddValue(num);
                } else if (UnitMap.TryGetValue(unit, out var dimensionUnit)) {
                    _builder.AddValue(num, dimensionUnit);
                } else {
                    _builder.AddValue(num, DimensionUnit.Pixel);
                }
                return;
            }

            // Try keyword
            if (KeywordMap.TryGetValue(value, out var keyword)) {
                _builder.AddValue(keyword);
                return;
            }

            // Try named color
            if (TryParseNamedColor(value, out var namedColor)) {
                _builder.AddValue(namedColor);
                return;
            }

            // Default: treat as enum or string
            _builder.AddValue(value, StyleValueType.Enum);
        }

        void CompileVarFunction(string inner) {
            // Split into variable name and optional fallback at the first top-level comma
            string varName;
            string fallback = null;

            int depth = 0;
            int commaIndex = -1;
            for (int i = 0; i < inner.Length; i++) {
                char c = inner[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0) {
                    commaIndex = i;
                    break;
                }
            }

            if (commaIndex >= 0) {
                varName = inner.Substring(0, commaIndex).Trim();
                fallback = inner.Substring(commaIndex + 1).Trim();
            } else {
                varName = inner;
            }

            // Calculate arg count to match Unity's token counting:
            // variable name (1) + comma (1) + fallback value count
            int argCount = 1;
            List<string> fallbackParts = null;
            if (fallback != null) {
                argCount++; // comma token
                fallbackParts = SplitSpaceSeparated(fallback);
                argCount += fallbackParts.Count;
            }

            _builder.AddValue(StyleFunction.Var);
            _builder.AddValue((float)argCount);
            _builder.AddValue(varName, StyleValueType.Variable);

            if (fallbackParts != null) {
                _builder.AddCommaSeparator();
                foreach (var part in fallbackParts) {
                    ParseSingleValue(part);
                }
            }
        }

        bool TryParseNamedColor(string name, out UnityEngine.Color color) {
            switch (name.ToLowerInvariant()) {
                case "black": color = UnityEngine.Color.black; return true;
                case "white": color = UnityEngine.Color.white; return true;
                case "red": color = UnityEngine.Color.red; return true;
                case "green": color = UnityEngine.Color.green; return true;
                case "blue": color = UnityEngine.Color.blue; return true;
                case "yellow": color = UnityEngine.Color.yellow; return true;
                case "cyan": color = UnityEngine.Color.cyan; return true;
                case "magenta": color = UnityEngine.Color.magenta; return true;
                case "gray": case "grey": color = UnityEngine.Color.gray; return true;
                case "clear": color = UnityEngine.Color.clear; return true;
                case "transparent": color = new UnityEngine.Color(0, 0, 0, 0); return true;
                default:
                    color = default;
                    return false;
            }
        }

        void LoadAndAddAsset(string path) {
            string fullPath = string.IsNullOrEmpty(_workingDir)
                ? path
                : Path.Combine(_workingDir, path);

            if (!File.Exists(fullPath)) return;

            string ext = Path.GetExtension(path).ToLowerInvariant();

            switch (ext) {
                case ".png":
                case ".jpg":
                case ".jpeg":
                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(File.ReadAllBytes(fullPath));
                    texture.filterMode = FilterMode.Bilinear;
                    _builder.AddValue(texture);
                    break;

                case ".ttf":
                case ".otf":
                    var font = new Font(fullPath);
                    _builder.AddValue(font);
                    break;
            }
        }

        #endregion
    }
}
