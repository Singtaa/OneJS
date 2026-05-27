using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Batched style application: applies a dictionary of style values to a
    /// VisualElement's IStyle in a single JS->C# crossing.
    ///
    /// The React reconciler used to set styles one property at a time, which
    /// became one __cs.invoke per property. On WebGL each crossing is multi-
    /// millisecond (JSON marshal + reflection), so a 30-element subtree with
    /// 10 styles each cost ~300 crossings. ApplyStyles cuts that to one
    /// crossing per element by iterating in C#.
    ///
    /// Called from JS via:
    ///   CS.OneJS.StyleBridge.ApplyStyles(element, { width: ..., height: ... })
    /// </summary>
    public static class StyleBridge {
        static readonly ConcurrentDictionary<(Type, string), PropertyInfo> _styleProps = new();

        public static void ApplyStyles(VisualElement element, object stylesObj) {
            if (element == null || stylesObj == null) return;
            if (stylesObj is not Dictionary<string, object> styles) return;

            var style = element.style;
            var styleType = style.GetType();

            foreach (var kvp in styles) {
                if (string.IsNullOrEmpty(kvp.Key)) continue;

                var prop = FindStyleProperty(styleType, kvp.Key);
                if (prop == null) continue;

                object value = ResolveValue(kvp.Value);

                try {
                    var converted = QuickJSNative.ConvertToTargetType(value, prop.PropertyType);
                    prop.SetValue(style, converted);
                } catch (Exception ex) {
                    Debug.LogWarning(
                        $"[StyleBridge] ApplyStyles failed for '{kvp.Key}': {ex.Message}");
                }
            }
        }

        // Nested objects with __csHandle came through JSON marshalling as
        // Dictionary<string, object>; resolve them back to live C# objects via
        // the handle table so ConvertToTargetType can use implicit operators
        // (e.g. Length -> StyleLength) on the unwrapped object.
        static object ResolveValue(object value) {
            if (value is Dictionary<string, object> dict
                && dict.TryGetValue("__csHandle", out var h) && h != null) {
                int handle = Convert.ToInt32(h);
                if (handle > 0) {
                    var resolved = QuickJSNative.GetObjectByHandle(handle);
                    if (resolved != null) return resolved;
                }
            }
            return value;
        }

        static PropertyInfo FindStyleProperty(Type type, string name) {
            var key = (type, name);
            if (_styleProps.TryGetValue(key, out var cached)) return cached;
            var prop = type.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public);
            if (prop != null) _styleProps[key] = prop;
            return prop;
        }
    }
}
