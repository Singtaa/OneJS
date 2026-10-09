using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.CustomStyleSheets {
    /// <summary>
    /// A USS filter value ("blur(4px) grayscale(50%)") as the StyleList an inline
    /// style takes, for IStyle.filter (Unity 6.3+) and IStyle.backdropFilter (6.6+).
    /// A sheet reaches the same properties through UssCompiler, which hands the
    /// value to Unity's own reader; an inline style has no such reader, so this
    /// builds the functions itself, with UssCompiler's tokenizer and value parsers.
    ///
    /// Which functions exist is Unity's FilterFunctionType for the running editor,
    /// matched by name (hue-rotate is HueRotate), so drop-shadow works where
    /// Unity has it and is refused where it does not. FilterStylePlaymodeTests
    /// holds every input to the result a compiled sheet gives.
    /// </summary>
    public static class UssFilter {
        /// <summary>
        /// Parses <paramref name="value"/>. Returns false, with the reason in
        /// <paramref name="error"/>, for anything Unity could not draw.
        /// </summary>
        public static bool TryParse(string value, out StyleList<FilterFunction> result, out string error) {
            result = default;
            error = null;
            value = value?.Trim() ?? "";
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                result = new StyleList<FilterFunction>(UnityEngine.UIElements.StyleKeyword.None);
                return true;
            }
            if (value.Equals("initial", StringComparison.OrdinalIgnoreCase)) {
                result = new StyleList<FilterFunction>(UnityEngine.UIElements.StyleKeyword.Initial);
                return true;
            }

            var functions = new List<FilterFunction>();
            foreach (var token in UssCompiler.SplitSpaceSeparated(value)) {
                if (!UssCompiler.IsFunction(token, out var name, out var inner)) {
                    error = $"'{token}' is not a filter function";
                    return false;
                }
                if (!TryFunctionType(name, out var type)) {
                    error = $"'{name}()' is not a filter function in this Unity version";
                    return false;
                }
                var function = new FilterFunction(type);
                foreach (var group in UssCompiler.SplitCssValue(inner)) {
                    foreach (var arg in UssCompiler.SplitSpaceSeparated(group)) {
                        if (!TryParameter(arg, out var parameter)) {
                            error = $"'{arg}' in {name}() is not a number, length, angle or colour";
                            return false;
                        }
                        if (function.parameterCount == 4) {
                            error = $"{name}() takes at most 4 values";
                            return false;
                        }
                        function.AddParameter(parameter);
                    }
                }
                functions.Add(function);
            }
            result = new StyleList<FilterFunction>(functions);
            return true;
        }

        // "hue-rotate" is FilterFunctionType.HueRotate. None, Custom and Count are not functions.
        static bool TryFunctionType(string name, out FilterFunctionType type) {
            var pascal = string.Concat(Array.ConvertAll(name.Split('-'),
                part => part.Length == 0 ? "" : char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant()));
            // By name, not by member: FilterFunctionType.Count is an error to name on 6.6
            type = default;
            return pascal is not ("None" or "Custom" or "Count") && Enum.TryParse(pascal, out type) && Enum.IsDefined(typeof(FilterFunctionType), type);
        }

        static bool TryParameter(string arg, out FilterParameter parameter) {
            parameter = default;
            if (UssCompiler.TryParseNumber(arg, out var number, out var unit)) {
                parameter = new FilterParameter(ToFilterFloat(number, unit));
                return true;
            }
            if (UssCompiler.TryParseColorFunctionOrHex(arg, out var color) || UssCompiler.TryParseNamedColor(arg, out color)) {
                parameter = new FilterParameter(color);
                return true;
            }
            return false;
        }

        // Unity's StyleProperty.ConvertDimensionToFilterFloat, which takes an internal
        // Dimension and so cannot be called: a sheet's "90deg" reaches a filter in radians.
        static float ToFilterFloat(float value, DimensionUnit? unit) => unit switch {
            DimensionUnit.Percent => value * 0.01f,
            DimensionUnit.Degree => value * (MathF.PI / 180f),
            DimensionUnit.Turn => value * MathF.PI * 2f,
            DimensionUnit.Gradian => value * MathF.PI / 200f,
            DimensionUnit.Millisecond => value * 0.001f,
            _ => value,
        };
    }
}
