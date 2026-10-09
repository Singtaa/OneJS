using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
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
    /// Which functions exist is Unity's own function table for the running editor,
    /// the one UssCompiler compiles a sheet through, so drop-shadow works where
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
            // An empty list, not StyleKeyword.None: Unity 6.3's renderer throws
            // ("Filter IEnumerable is not a List<FilterFunction>") when an element's
            // filter goes from functions to a keyword. Both mean no filter.
            // "initial" too: a filter's initial value is none.
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("initial", StringComparison.OrdinalIgnoreCase)) {
                result = new StyleList<FilterFunction>(new List<FilterFunction>());
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

        // A function name as Unity's sheet reader resolves it: USS name to
        // StyleValueFunction (UssCompiler's lookup), then StyleProperty.ToFilterFunctionType,
        // both internal, hence reflected. One table for sheets and inline styles, so the
        // two never disagree on which functions exist. Cached per name, so a value
        // animated every frame reflects nothing after its first.
        static readonly ConcurrentDictionary<string, FilterFunctionType?> _functionTypes = new(StringComparer.Ordinal);
        static MethodInfo _toFilterFunctionType;
        static bool _toFilterFunctionTypeProbed;

        internal static bool TryFunctionType(string name, out FilterFunctionType type) {
            var resolved = _functionTypes.GetOrAdd(name, ResolveFunctionType);
            type = resolved.GetValueOrDefault();
            return resolved.HasValue;
        }

        static FilterFunctionType? ResolveFunctionType(string name) {
            if (!UssCompiler.TryUnityFunctionValue(name, out var function)) return null;
            if (!_toFilterFunctionTypeProbed) {
                _toFilterFunctionTypeProbed = true;
                _toFilterFunctionType = typeof(StyleSheet).Assembly
                    .GetType("UnityEngine.UIElements.StyleProperty")
                    ?.GetMethod("ToFilterFunctionType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_toFilterFunctionType == null) {
                    Debug.LogWarning("[OneJS] Filters in inline styles cannot be read: Unity's filter function table was not found by reflection.");
                }
            }
            if (_toFilterFunctionType == null || _withoutUnityTable) return null;
            try {
                // None and Custom are what a USS name such as none() maps to, not functions to draw
                var type = (FilterFunctionType)_toFilterFunctionType.Invoke(null, new[] { function });
                return type is FilterFunctionType.None or FilterFunctionType.Custom ? null : type;
            } catch (TargetInvocationException) {
                return null;
            }
        }

        static bool _withoutUnityTable;

        /// <summary>
        /// For tests: resolve names as if reflection had not found Unity's table,
        /// which is what a stripped player without OneJS's link.xml would see,
        /// until the result is disposed.
        /// </summary>
        internal static IDisposable WithoutUnityTableForTests() {
            _withoutUnityTable = true;
            _functionTypes.Clear();
            return new Restore();
        }

        sealed class Restore : IDisposable {
            public void Dispose() {
                _withoutUnityTable = false;
                _functionTypes.Clear();
            }
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
