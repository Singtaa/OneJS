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
            // via explicit interface implementation: width/height/etc. are not
            // exposed as public properties on the runtime type, only through
            // the interface. Reflect on IStyle so PropertyInfo.SetValue routes
            // through the interface dispatch.
            var style = element.style;

            foreach (var kvp in styles) {
                if (string.IsNullOrEmpty(kvp.Key)) continue;

                object value = ResolveValue(kvp.Value);

                try {
                    // Fast path: direct typed assignment for the common style
                    // properties. Anything not covered falls back to reflection.
                    if (!TryApplyFast(style, kvp.Key, value)) {
                        ApplyReflective(style, kvp.Key, value);
                    }
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
        // once per class: Tailwind classNames like "justify-center items-center
        // absolute h-full" cost 4 __cs.invoke crossings. WebGL builds spend
        // ~3ms per crossing, so heavy className usage was a measurable share of
        // mount latency. One crossing per element here regardless of class
        // count. Update path keeps the per-class add/remove flow since changes
        // are usually small deltas.
        //
        // JS arrays of strings come through the {__csArray, __csArrayType:"string"}
        // marshalling path and arrive as string[]. Untyped arrays would arrive
        // as List<object> - handle both for safety.
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

        // Direct typed setters for the common style properties. Reuses the same
        // ConvertToTargetType the reflection path uses (so values convert
        // identically), but assigns through the typed IStyle setter instead of
        // PropertyInfo.SetValue: no reflection invoke, no per-call object[].
        // Returns false for properties not covered here (long-tail props like
        // transforms, transitions, fonts, slices), which take the reflection path.
        static bool TryApplyFast(IStyle style, string key, object value) {
            switch (key) {
                // Lengths
                case "width": style.width = AsLength(value); return true;
                case "height": style.height = AsLength(value); return true;
                case "minWidth": style.minWidth = AsLength(value); return true;
                case "minHeight": style.minHeight = AsLength(value); return true;
                case "maxWidth": style.maxWidth = AsLength(value); return true;
                case "maxHeight": style.maxHeight = AsLength(value); return true;
                case "top": style.top = AsLength(value); return true;
                case "right": style.right = AsLength(value); return true;
                case "bottom": style.bottom = AsLength(value); return true;
                case "left": style.left = AsLength(value); return true;
                case "marginTop": style.marginTop = AsLength(value); return true;
                case "marginRight": style.marginRight = AsLength(value); return true;
                case "marginBottom": style.marginBottom = AsLength(value); return true;
                case "marginLeft": style.marginLeft = AsLength(value); return true;
                case "paddingTop": style.paddingTop = AsLength(value); return true;
                case "paddingRight": style.paddingRight = AsLength(value); return true;
                case "paddingBottom": style.paddingBottom = AsLength(value); return true;
                case "paddingLeft": style.paddingLeft = AsLength(value); return true;
                case "borderTopLeftRadius": style.borderTopLeftRadius = AsLength(value); return true;
                case "borderTopRightRadius": style.borderTopRightRadius = AsLength(value); return true;
                case "borderBottomLeftRadius": style.borderBottomLeftRadius = AsLength(value); return true;
                case "borderBottomRightRadius": style.borderBottomRightRadius = AsLength(value); return true;
                case "flexBasis": style.flexBasis = AsLength(value); return true;
                case "fontSize": style.fontSize = AsLength(value); return true;
                case "letterSpacing": style.letterSpacing = AsLength(value); return true;
                case "wordSpacing": style.wordSpacing = AsLength(value); return true;

                // Floats
                case "flexGrow": style.flexGrow = AsFloat(value); return true;
                case "flexShrink": style.flexShrink = AsFloat(value); return true;
                case "opacity": style.opacity = AsFloat(value); return true;
                case "borderTopWidth": style.borderTopWidth = AsFloat(value); return true;
                case "borderRightWidth": style.borderRightWidth = AsFloat(value); return true;
                case "borderBottomWidth": style.borderBottomWidth = AsFloat(value); return true;
                case "borderLeftWidth": style.borderLeftWidth = AsFloat(value); return true;
                case "unityTextOutlineWidth": style.unityTextOutlineWidth = AsFloat(value); return true;

                // Colors
                case "color": style.color = AsColor(value); return true;
                case "backgroundColor": style.backgroundColor = AsColor(value); return true;
                case "borderTopColor": style.borderTopColor = AsColor(value); return true;
                case "borderRightColor": style.borderRightColor = AsColor(value); return true;
                case "borderBottomColor": style.borderBottomColor = AsColor(value); return true;
                case "borderLeftColor": style.borderLeftColor = AsColor(value); return true;
                case "unityBackgroundImageTintColor": style.unityBackgroundImageTintColor = AsColor(value); return true;
                case "unityTextOutlineColor": style.unityTextOutlineColor = AsColor(value); return true;

                // Enums
                case "display": style.display = AsEnum<DisplayStyle>(value); return true;
                case "position": style.position = AsEnum<Position>(value); return true;
                case "flexDirection": style.flexDirection = AsEnum<FlexDirection>(value); return true;
                case "flexWrap": style.flexWrap = AsEnum<Wrap>(value); return true;
                case "alignItems": style.alignItems = AsEnum<Align>(value); return true;
                case "alignContent": style.alignContent = AsEnum<Align>(value); return true;
                case "alignSelf": style.alignSelf = AsEnum<Align>(value); return true;
                case "justifyContent": style.justifyContent = AsEnum<Justify>(value); return true;
                case "overflow": style.overflow = AsEnum<Overflow>(value); return true;
                case "visibility": style.visibility = AsEnum<Visibility>(value); return true;
                case "whiteSpace": style.whiteSpace = AsEnum<WhiteSpace>(value); return true;
                case "textOverflow": style.textOverflow = AsEnum<TextOverflow>(value); return true;
                case "unityTextAlign": style.unityTextAlign = AsEnum<TextAnchor>(value); return true;
                case "unityFontStyleAndWeight": style.unityFontStyleAndWeight = AsEnum<FontStyle>(value); return true;

                default: return false;
            }
        }

        static StyleLength AsLength(object v) =>
            (StyleLength)QuickJSNative.ConvertToTargetType(v, typeof(StyleLength));
        static StyleFloat AsFloat(object v) =>
            (StyleFloat)QuickJSNative.ConvertToTargetType(v, typeof(StyleFloat));
        static StyleColor AsColor(object v) =>
            (StyleColor)QuickJSNative.ConvertToTargetType(v, typeof(StyleColor));
        static StyleEnum<T> AsEnum<T>(object v) where T : struct, IConvertible =>
            (StyleEnum<T>)QuickJSNative.ConvertToTargetType(v, typeof(StyleEnum<T>));

        // Styles reapply on every React commit, so an always-on warning for a
        // bad key would repeat 30 times a second; once per key is enough to
        // surface the typo without drowning the console.
        static readonly ConcurrentDictionary<string, bool> _warnedUnknownKeys = new();

        static void ApplyReflective(IStyle style, string key, object value) {
            var prop = FindStyleProperty(key);
            if (prop == null) {
                if (_warnedUnknownKeys.TryAdd(key, true)) {
                    Debug.LogWarning(
                        $"[StyleBridge] Unknown style property '{key}'. IStyle has no property by that name, so the value was dropped. Warning once per key.");
                }
                return;
            }
            var converted = QuickJSNative.ConvertToTargetType(value, prop.PropertyType);
            prop.SetValue(style, converted);
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
