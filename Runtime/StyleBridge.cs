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
        static readonly ConcurrentDictionary<string, PropertyInfo> _styleProps = new();
        static readonly Type _iStyleType = typeof(IStyle);

        public static void ApplyStyles(VisualElement element, object stylesObj) {
            if (element == null || stylesObj == null) return;
            if (stylesObj is not Dictionary<string, object> styles) return;

            // IStyle is implemented by an internal class (InlineStyleAccess)
            // via explicit interface implementation — width/height/etc. are not
            // exposed as public properties on the runtime type, only through
            // the interface. Reflect on IStyle so PropertyInfo.SetValue routes
            // through the interface dispatch.
            var style = element.style;

            foreach (var kvp in styles) {
                if (string.IsNullOrEmpty(kvp.Key)) continue;

                var prop = FindStyleProperty(kvp.Key);
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

        // Values that arrived as top-level args would be detected by the
        // WebGL jslib's marshalValue (Color/Vector hints, handle types). Here
        // they come in nested inside a JSON-stringified dict, so the special
        // shapes survive only as plain Dictionary<string, object>. Reconstruct
        // the live C# value before handing off to ConvertToTargetType, which
        // then handles e.g. Color -> StyleColor and Length -> StyleLength via
        // implicit operators.
        static object ResolveValue(object value) {
            if (value is not Dictionary<string, object> dict) return value;

            if (dict.TryGetValue("__csHandle", out var h) && h != null) {
                int handle = Convert.ToInt32(h);
                if (handle > 0) {
                    var resolved = QuickJSNative.GetObjectByHandle(handle);
                    if (resolved != null) return resolved;
                }
                return value;
            }

            // {r,g,b,a} -> Color (parseStyleValue returns this for colors).
            if (dict.ContainsKey("r") && dict.ContainsKey("g") && dict.ContainsKey("b")) {
                float r = ToFloat(dict, "r");
                float g = ToFloat(dict, "g");
                float b = ToFloat(dict, "b");
                float a = dict.ContainsKey("a") ? ToFloat(dict, "a") : 1f;
                return new Color(r, g, b, a);
            }

            // {x,y,z,w} -> Vector4; {x,y,z} -> Vector3.
            if (dict.ContainsKey("x") && dict.ContainsKey("y")) {
                float x = ToFloat(dict, "x");
                float y = ToFloat(dict, "y");
                if (dict.ContainsKey("z")) {
                    float z = ToFloat(dict, "z");
                    if (dict.ContainsKey("w")) {
                        return new Vector4(x, y, z, ToFloat(dict, "w"));
                    }
                    return new Vector3(x, y, z);
                }
                return new Vector2(x, y);
            }

            return value;
        }

        static float ToFloat(Dictionary<string, object> dict, string key) {
            return dict.TryGetValue(key, out var v) && v != null
                ? Convert.ToSingle(v)
                : 0f;
        }

        // Batched class-list add. The reconciler used to call AddToClassList
        // once per class — Tailwind classNames like "justify-center items-center
        // absolute h-full" cost 4 __cs.invoke crossings. WebGL builds spend
        // ~3ms per crossing, so heavy className usage was a measurable share of
        // mount latency. One crossing per element here regardless of class
        // count. Update path keeps the per-class add/remove flow since changes
        // are usually small deltas.
        //
        // JS arrays of strings come through the {__csArray, __csArrayType:"string"}
        // marshalling path and arrive as string[]. Untyped arrays would arrive
        // as List<object> — handle both for safety.
        public static void AddClassesBatch(VisualElement element, object classesObj) {
            if (element == null || classesObj == null) return;
            switch (classesObj) {
                case string[] arr:
                    for (int i = 0; i < arr.Length; i++) {
                        if (!string.IsNullOrEmpty(arr[i])) element.AddToClassList(arr[i]);
                    }
                    break;
                case System.Collections.IList list:
                    for (int i = 0; i < list.Count; i++) {
                        if (list[i] is string s && !string.IsNullOrEmpty(s)) {
                            element.AddToClassList(s);
                        }
                    }
                    break;
            }
        }

        static PropertyInfo FindStyleProperty(string name) {
            if (_styleProps.TryGetValue(name, out var cached)) return cached;
            var prop = _iStyleType.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public);
            if (prop != null) _styleProps[name] = prop;
            return prop;
        }
    }
}
