using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace OneJS.Dom {
    public class DomStyle {
        #region Statics
        static Dictionary<string, Action<Dom, string, object>> styleProcessors = new();

        static DomStyle() {
            AddProcessorForEnum("alignContent", typeof(Align));
            AddProcessorForEnum("alignItems", typeof(Align));
            AddProcessorForEnum("alignSelf", typeof(Align));
            AddProcessorForColor("backgroundColor");
            AddProcessorForBackground("backgroundImage");
            AddProcessorForBackgroundSize("backgroundSize");
            AddProcessorForBackgroundRepeat("backgroundRepeat");
            AddProcessorForBackgroundPosition("backgroundPosition"); // Composite
            AddProcessorForBackgroundPositionSingle("backgroundPositionX");
            AddProcessorForBackgroundPositionSingle("backgroundPositionY");

            AddProcessorForBorderColor("borderColor"); // Composite
            AddProcessorForBorderWidth("borderWidth"); // Composite
            AddProcessorForBorderRadius("borderRadius"); // Composite
            AddProcessorForColor("borderBottomColor");
            AddProcessorForColor("borderLeftColor");
            AddProcessorForColor("borderRightColor");
            AddProcessorForColor("borderTopColor");
            AddProcessorForFloat("borderBottomWidth");
            AddProcessorForFloat("borderLeftWidth");
            AddProcessorForFloat("borderRightWidth");
            AddProcessorForFloat("borderTopWidth");
            AddProcessorForLength("borderBottomLeftRadius");
            AddProcessorForLength("borderBottomRightRadius");
            AddProcessorForLength("borderTopLeftRadius");
            AddProcessorForLength("borderTopRightRadius");

            AddProcessorForLength("bottom");
            AddProcessorForColor("color");
            AddProcessorForCursor("cursor");
            AddProcessorForEnum("display", typeof(DisplayStyle));
            AddProcessorForLength("flexBasis");
            AddProcessorForEnum("flexDirection", typeof(FlexDirection));
            AddProcessorForFloat("flexGrow");
            AddProcessorForFloat("flexShrink");
            AddProcessorForEnum("flexWrap", typeof(Wrap));
            AddProcessorForLength("fontSize");
            AddProcessorForLength("height");

            AddProcessorForEnum("visibility", typeof(Visibility));
            AddProcessorForEnum("whiteSpace", typeof(WhiteSpace));
            AddProcessorForLength("width");
            AddProcessorForLength("wordSpacing");
            AddProcessorForLength("top");
            // AddProcessorForTransformOrigin("transformOrigin");
            // AddProcessorForListTimeValue("transitionDelay", typeof(TimeValue));
            // AddProcessorForListTimeValue("transitionDuration", typeof(TimeValue));
            // AddProcessorForListPropertyName("transitionProperty", typeof(StylePropertyName));
            // AddProcessorForListEasingFunction("transitionTimingFunction", typeof(EasingFunction));
            // AddProcessorForTranslate("translate");

            AddProcessorForColor("unityBackgroundImageTintColor");
            AddProcessorForEnum("unityBackgroundScaleMode", typeof(ScaleMode));
            AddProcessorForFont("unityFont");
            AddProcessorForFontDefinition("unityFontDefinition");
            AddProcessorForEnum("unityFontStyleAndWeight", typeof(FontStyle));
            AddProcessorForEnum("unityOverflowClipBox", typeof(OverflowClipBox));
            AddProcessorForLength("unityParagraphSpacing");
            AddProcessorForInt("unitySliceBottom");
            AddProcessorForInt("unitySliceLeft");
            AddProcessorForInt("unitySliceRight");
            AddProcessorForInt("unitySliceTop");
            AddProcessorForFloat("unitySliceScale");
            AddProcessorForEnum("unityTextAlign", typeof(TextAnchor));
            AddProcessorForColor("unityTextOutlineColor");
            AddProcessorForFloat("unityTextOutlineWidth");
            AddProcessorForEnum("unityTextOverflowPosition", typeof(TextOverflowPosition));

            // TODO Custom AddProcessor for borderColor (not an existing property)
        }

        static void AddProcessorForColor(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s) {
                    var c = ColorUtility.TryParseHtmlString(s, out var color) ? color : Color.white;
                    pi.SetValue(dom.ve.style, new StyleColor(c));
                    return;
                } else if (v is Color c) {
                    pi.SetValue(dom.ve.style, new StyleColor(c));
                    return;
                }
            });
        }

        static void AddProcessorForLength(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (GetLength(v, out var l)) {
                    pi.SetValue(dom.ve.style, new StyleLength(l));
                }
            });
        }

        static void AddProcessorForEnum(string key, Type enumType) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                    pi.SetValue(dom.ve.style, createStyleEnumWithKeyword(keyword, enumType));
                    return;
                }
                if (v is int i) {
                    if (Enum.IsDefined(enumType, i)) {
                        pi.SetValue(dom.ve.style, createStyleEnum(i, enumType));
                    }
                } else if (v is string s) {
                    if (Enum.TryParse(enumType, s, true, out var e)) {
                        pi.SetValue(dom.ve.style, createStyleEnum(e, enumType));
                    }
                }
            });
        }

        static void AddProcessorForBackground(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v == null) {
                    pi.SetValue(dom.ve.style, new StyleBackground(StyleKeyword.Initial));
                } else if (v is string s) {
                    pi.SetValue(dom.ve.style, new StyleBackground(Background.FromTexture2D(dom.document.loadImage(s))));
                } else if (v is Texture2D t) {
                    pi.SetValue(dom.ve.style, new StyleBackground(Background.FromTexture2D(t)));
                } else if (v is Sprite sp) {
                    pi.SetValue(dom.ve.style, new StyleBackground(Background.FromSprite(sp)));
                } else if (v is RenderTexture rt) {
                    pi.SetValue(dom.ve.style, new StyleBackground(Background.FromRenderTexture(rt)));
                } else if (v is VectorImage vi) {
                    pi.SetValue(dom.ve.style, new StyleBackground(Background.FromVectorImage(vi)));
                }
            });
        }

        static void AddProcessorForBackgroundSize(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s && StyleKeyword.TryParse(s, true, out StyleKeyword keyword)) {
                    dom.ve.style.backgroundSize = new StyleBackgroundSize(keyword);
                    return;
                }
                if (v is string str) {
                    var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (GetLength(parts[0], out var x)) {
                        if (parts.Length > 1 && GetLength(parts[1], out var y)) {
                            dom.ve.style.backgroundSize = new BackgroundSize(x, y);
                            return;
                        }
                        dom.ve.style.backgroundSize = new BackgroundSize(x, x); // If only one value is provided, use it for both x and y
                    }
                }
            });
        }

        static void AddProcessorForBackgroundRepeat(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s && StyleKeyword.TryParse(s, true, out StyleKeyword keyword)) {
                    pi.SetValue(dom.ve.style, new StyleBackgroundRepeat(keyword));
                    return;
                }
                if (v is string str) {
                    var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (Enum.TryParse(parts[0], true, out Repeat repeat)) {
                            dom.ve.style.backgroundRepeat = new BackgroundRepeat(repeat, repeat);
                        }
                    } else if (parts.Length == 2) {
                        if (Enum.TryParse(parts[0], true, out Repeat x) && Enum.TryParse(parts[1], true, out Repeat y)) {
                            dom.ve.style.backgroundRepeat = new BackgroundRepeat(x, y);
                        }
                    }
                }
            });
        }

        static void AddProcessorForBackgroundPosition(string key) {
            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s && StyleKeyword.TryParse(s, true, out StyleKeyword keyword)) {
                    dom.ve.style.backgroundPositionX = new StyleBackgroundPosition(keyword);
                    dom.ve.style.backgroundPositionY = new StyleBackgroundPosition(keyword);
                    return;
                }
                if (v is string str) {
                    var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x)) {
                            dom.ve.style.backgroundPositionX = new BackgroundPosition(x);
                            dom.ve.style.backgroundPositionY = new BackgroundPosition(x);
                        }
                    } else if (parts.Length == 2) {
                        if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x) && GetLength(parts[1], out var l)) {
                            dom.ve.style.backgroundPositionX = new BackgroundPosition(x, l);
                        } else if (Enum.TryParse(parts[0], true, out x) && Enum.TryParse(parts[1], true, out BackgroundPositionKeyword y)) {
                            dom.ve.style.backgroundPositionX = new BackgroundPosition(x, 0);
                            dom.ve.style.backgroundPositionY = new BackgroundPosition(y, 0);
                        }
                    } else if (parts.Length == 4) {
                        if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x) && GetLength(parts[1], out var lx) &&
                            Enum.TryParse(parts[2], true, out BackgroundPositionKeyword y) && GetLength(parts[3], out var ly)) {
                            dom.ve.style.backgroundPositionX = new BackgroundPosition(x, lx);
                            dom.ve.style.backgroundPositionY = new BackgroundPosition(y, ly);
                        }
                    }
                }
            });
        }

        static void AddProcessorForBackgroundPositionSingle(string key) {
            var pi = GetPropertyInfo(key);
            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s && StyleKeyword.TryParse(s, true, out StyleKeyword keyword)) {
                    pi.SetValue(dom.ve.style, new StyleBackgroundPosition(keyword));
                    return;
                }
                if (v is string str) {
                    var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword posKeyword)) {
                            pi.SetValue(dom.ve.style, new BackgroundPosition(posKeyword));
                        }
                    } else if (parts.Length == 2) {
                        if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword posKeyword) && GetLength(parts[1], out var l)) {
                            pi.SetValue(dom.ve.style, new BackgroundPosition(posKeyword, l));
                        }
                    }
                }
            });
        }

        static void AddProcessorForBorderColor(string key) {
            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                    dom.ve.style.borderBottomColor = new StyleColor(keyword);
                    dom.ve.style.borderLeftColor = new StyleColor(keyword);
                    dom.ve.style.borderRightColor = new StyleColor(keyword);
                    dom.ve.style.borderTopColor = new StyleColor(keyword);
                    return;
                }
                if (v is string s) {
                    var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (TryParseColorString(parts[0], out var c)) {
                            __setBorderColors(dom, c, c, c, c);
                        }
                    } else if (parts.Length == 2) {
                        if (TryParseColorString(parts[0], out var tb) && TryParseColorString(parts[1], out var lr)) {
                            __setBorderColors(dom, tb, lr, tb, lr);
                        }
                    } else if (parts.Length == 3) {
                        if (TryParseColorString(parts[0], out var t) && TryParseColorString(parts[1], out var lr) &&
                            TryParseColorString(parts[2], out var b)) {
                            __setBorderColors(dom, t, lr, b, lr);
                        }
                    } else if (parts.Length == 4) {
                        if (TryParseColorString(parts[0], out var t) && TryParseColorString(parts[1], out var r) &&
                            TryParseColorString(parts[2], out var b) && TryParseColorString(parts[3], out var l)) {
                            __setBorderColors(dom, t, r, b, l);
                        }
                    }
                } else if (v is Color c) {
                    __setBorderColors(dom, c, c, c, c);
                } else if (v is Puerts.JSObject jsObj) {
                    if (jsObj.Get<int>("length") == 1) {
                        var cc = jsObj.Get<Color>("0");
                        __setBorderColors(dom, cc, cc, cc, cc);
                    } else if (jsObj.Get<int>("length") == 2) {
                        var tb = jsObj.Get<Color>("0");
                        var lr = jsObj.Get<Color>("1");
                        __setBorderColors(dom, tb, lr, tb, lr);
                    } else if (jsObj.Get<int>("length") == 3) {
                        var t = jsObj.Get<Color>("0");
                        var lr = jsObj.Get<Color>("1");
                        var b = jsObj.Get<Color>("2");
                        __setBorderColors(dom, t, lr, b, lr);
                    } else if (jsObj.Get<int>("length") == 4) {
                        var t = jsObj.Get<Color>("0");
                        var r = jsObj.Get<Color>("1");
                        var b = jsObj.Get<Color>("2");
                        var l = jsObj.Get<Color>("3");
                        __setBorderColors(dom, t, r, b, l);
                    }
                }
            });
        }


        static void AddProcessorForBorderWidth(string key) {
            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                    dom.ve.style.borderBottomWidth = new StyleFloat(keyword);
                    dom.ve.style.borderLeftWidth = new StyleFloat(keyword);
                    dom.ve.style.borderRightWidth = new StyleFloat(keyword);
                    dom.ve.style.borderTopWidth = new StyleFloat(keyword);
                    return;
                }
                if (v is string s) {
                    var parts = s.Replace("px", "").Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (float.TryParse(parts[0], out var l)) {
                            __setBorderWidths(dom, l, l, l, l);
                        }
                    } else if (parts.Length == 2) {
                        if (float.TryParse(parts[0], out var tb) && float.TryParse(parts[1], out var lr)) {
                            __setBorderWidths(dom, tb, lr, tb, lr);
                        }
                    } else if (parts.Length == 3) {
                        if (float.TryParse(parts[0], out var t) && float.TryParse(parts[1], out var lr) &&
                            float.TryParse(parts[2], out var b)) {
                            __setBorderWidths(dom, t, lr, b, lr);
                        }
                    } else if (parts.Length == 4) {
                        if (float.TryParse(parts[0], out var t) && float.TryParse(parts[1], out var r) &&
                            float.TryParse(parts[2], out var b) && float.TryParse(parts[3], out var l)) {
                            __setBorderWidths(dom, t, r, b, l);
                        }
                    }
                } else if (v is double d) {
                    __setBorderWidths(dom, (float)d, (float)d, (float)d, (float)d);
                } else if (v is Puerts.JSObject jsObj) {
                    if (jsObj.Get<int>("length") == 1) {
                        var l = jsObj.Get<float>("0");
                        __setBorderWidths(dom, l, l, l, l);
                    } else if (jsObj.Get<int>("length") == 2) {
                        var tb = jsObj.Get<float>("0");
                        var lr = jsObj.Get<float>("1");
                        __setBorderWidths(dom, tb, lr, tb, lr);
                    } else if (jsObj.Get<int>("length") == 3) {
                        var t = jsObj.Get<float>("0");
                        var lr = jsObj.Get<float>("1");
                        var b = jsObj.Get<float>("2");
                        __setBorderWidths(dom, t, lr, b, lr);
                    } else if (jsObj.Get<int>("length") == 4) {
                        var t = jsObj.Get<float>("0");
                        var r = jsObj.Get<float>("1");
                        var b = jsObj.Get<float>("2");
                        var l = jsObj.Get<float>("3");
                        __setBorderWidths(dom, t, r, b, l);
                    }
                }
            });
        }


        static void AddProcessorForBorderRadius(string key) {
            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                    dom.ve.style.borderBottomLeftRadius = new StyleLength(keyword);
                    dom.ve.style.borderBottomRightRadius = new StyleLength(keyword);
                    dom.ve.style.borderTopLeftRadius = new StyleLength(keyword);
                    dom.ve.style.borderTopRightRadius = new StyleLength(keyword);
                    return;
                }
                if (v is string s) {
                    var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) {
                        if (GetLength(parts[0], out var l)) {
                            __setBorderRadii(dom, l, l, l, l);
                        }
                    } else if (parts.Length == 2) {
                        if (GetLength(parts[0], out var tlbr) && GetLength(parts[1], out var trbl)) {
                            __setBorderRadii(dom, tlbr, trbl, tlbr, trbl);
                        }
                    } else if (parts.Length == 3) {
                        if (GetLength(parts[0], out var tl) && GetLength(parts[1], out var trbl) &&
                            GetLength(parts[2], out var br)) {
                            __setBorderRadii(dom, tl, trbl, br, trbl);
                        }
                    } else if (parts.Length == 4) {
                        if (GetLength(parts[0], out var tl) && GetLength(parts[1], out var tr) &&
                            GetLength(parts[2], out var br) && GetLength(parts[3], out var bl)) {
                            __setBorderRadii(dom, tl, tr, br, bl);
                        }
                    }
                } else if (v is double d) {
                    var l = new Length((float)d);
                    __setBorderRadii(dom, l, l, l, l);
                } else if (v is Puerts.JSObject jsObj) {
                    if (jsObj.Get<int>("length") == 1) {
                        var l = new Length(jsObj.Get<float>("0"));
                        __setBorderRadii(dom, l, l, l, l);
                    } else if (jsObj.Get<int>("length") == 2) {
                        var tlbr = new Length(jsObj.Get<float>("0"));
                        var trbl = new Length(jsObj.Get<float>("1"));
                        __setBorderRadii(dom, tlbr, trbl, trbl, tlbr);
                    } else if (jsObj.Get<int>("length") == 3) {
                        var tl = new Length(jsObj.Get<float>("0"));
                        var trbl = new Length(jsObj.Get<float>("1"));
                        var br = new Length(jsObj.Get<float>("2"));
                        __setBorderRadii(dom, tl, trbl, br, trbl);
                    } else if (jsObj.Get<int>("length") == 4) {
                        var tl = new Length(jsObj.Get<float>("0"));
                        var tr = new Length(jsObj.Get<float>("1"));
                        var br = new Length(jsObj.Get<float>("2"));
                        var bl = new Length(jsObj.Get<float>("3"));
                        __setBorderRadii(dom, tl, tr, br, bl);
                    }
                }
            });
        }


        static void AddProcessorForFont(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s) {
                    dom.ve.style.unityFont = dom.document.loadFont(s);
                } else if (v is Font f) {
                    dom.ve.style.unityFont = f;
                }
            });
        }

        static void AddProcessorForFontDefinition(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is string s) {
                    dom.ve.style.unityFontDefinition = dom.document.loadFontDefinition(s);
                } else if (v is FontDefinition fd) {
                    dom.ve.style.unityFontDefinition = fd;
                }
            });
        }

        public static void AddProcessorForFloat(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                pi.SetValue(dom.ve.style, v is double d ? new StyleFloat((float)d) : new StyleFloat(StyleKeyword.Initial));
            });
        }

        public static void AddProcessorForInt(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                pi.SetValue(dom.ve.style, v is double d ? new StyleInt((int)d) : new StyleInt(StyleKeyword.Initial));
            });
        }

        public static void AddProcessorForCursor(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is Cursor cursor) {
                    dom.ve.style.cursor = cursor;
                }
            });
        }

        public static void AddProcessorForRotate(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v == null) {
                    pi.SetValue(dom.ve.style, new StyleRotate(StyleKeyword.Initial));
                    return;
                }

                if (v is string s) {
                    var rotateRegex = new Regex(@"(-?\d+\.?\d*|\.\d+)(deg|grad|rad|turn)", RegexOptions.IgnoreCase);
                    var match = rotateRegex.Match(s);
                    if (match.Success) {
                        float value = float.Parse(match.Groups[1].Value);
                        var unit = match.Groups[2].Value.ToLower();
                        AngleUnit angleUnit = AngleUnit.Degree; // Default to Degree

                        switch (unit) {
                            case "deg":
                                angleUnit = AngleUnit.Degree;
                                break;
                            case "grad":
                                angleUnit = AngleUnit.Gradian;
                                break;
                            case "rad":
                                angleUnit = AngleUnit.Radian;
                                break;
                            case "turn":
                                angleUnit = AngleUnit.Turn;
                                break;
                        }

                        pi.SetValue(dom.ve.style, new Rotate(new Angle(value, angleUnit)));
                    }
                } else if (v is double d) {
                    pi.SetValue(dom.ve.style, new StyleRotate(new Rotate(new Angle((float)d))));
                } else {
                    throw new Exception($"Unsupported value type for rotate: {v.GetType()}");
                }
            });
        }

        public static void AddProcessorForScale(string key) {
            var pi = GetPropertyInfo(key);

            styleProcessors.Add(key, (dom, k, v) => {
                if (v is double d) {
                    pi.SetValue(dom.ve.style, new StyleScale(new Scale(new Vector2((float)d, (float)d))));
                } else if (v is Puerts.JSObject jsObj && jsObj.Get<int>("length") == 2) {
                    var x = jsObj.Get<float>("0");
                    var y = jsObj.Get<float>("1");
                    pi.SetValue(dom.ve.style, new StyleScale(new Scale(new Vector2(x, y))));
                } else {
                    throw new Exception($"Unsupported value type for scale: {v.GetType()}");
                }
            });
        }

        public static PropertyInfo GetPropertyInfo(string key) {
            var pi = typeof(IStyle).GetProperty(key, BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public);
            if (pi == null)
                throw new Exception($"Property not found: {key}");
            return pi;
        }


        public static object createStyleEnum(object v, Type type) {
            Type myParameterizedSomeClass = typeof(StyleEnum<>).MakeGenericType(type);
            ConstructorInfo constr = myParameterizedSomeClass.GetConstructor(new[] { type });
            object instance = constr.Invoke(new object[] { v });
            return instance;
        }

        public static object createStyleEnumWithKeyword(StyleKeyword keyword, Type type) {
            Type myParameterizedSomeClass = typeof(StyleEnum<>).MakeGenericType(type);
            ConstructorInfo constr = myParameterizedSomeClass.GetConstructor(new[] { typeof(StyleKeyword) });
            object instance = constr.Invoke(new object[] { keyword });
            return instance;
        }

        public static bool TryParseColorString(string s, out Color color) {
            return ColorUtility.TryParseHtmlString(s, out color);
        }
        #endregion

        Dom _dom;

        public DomStyle(Dom dom) {
            this._dom = dom;
        }

        public IStyle veStyle => _dom.ve.style;

        public void setProperty(string key, object value) {
            // var pi = this.GetType().GetProperty(key, BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public);
            // if (pi != null) {
            //     pi.SetValue(this, value);
            // }
            if (styleProcessors.TryGetValue(key, out var action)) {
                action(_dom, key, value);
            }
        }

        #region Style Properties
        public object alignContent {
            get => veStyle.alignContent;
            set {
                if (TryParseStyleEnum<Align>(value, out var styleEnum))
                    veStyle.alignContent = styleEnum;
            }
        }


        public object alignItems {
            get => veStyle.alignItems;
            set {
                if (TryParseStyleEnum<Align>(value, out var styleEnum))
                    veStyle.alignItems = styleEnum;
            }
        }

        public object alignSelf {
            get => veStyle.alignSelf;
            set {
                if (TryParseStyleEnum<Align>(value, out var styleEnum))
                    veStyle.alignSelf = styleEnum;
            }
        }

        public object backgroundColor {
            get => veStyle.backgroundColor;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.backgroundColor = styleColor;
            }
        }

        public object backgroundImage {
            get => veStyle.backgroundImage;
            set {
                if (TryParseStyleBackground(value, out var styleBackground))
                    veStyle.backgroundImage = styleBackground;
            }
        }

        public object backgroundSize {
            get => veStyle.backgroundSize;
            set {
                if (TryParseStyleBackgroundSize(value, out var styleBackgroundSize))
                    veStyle.backgroundSize = styleBackgroundSize;
            }
        }

        public object backgroundRepeat {
            get => veStyle.backgroundRepeat;
            set {
                if (TryParseStyleBackgroundRepeat(value, out var styleBackgroundRepeat))
                    veStyle.backgroundRepeat = styleBackgroundRepeat;
            }
        }

        // Composite
        public object backgroundPosition {
            get => (veStyle.backgroundPositionX, veStyle.backgroundPositionY);
            set => SetBackgroundPosition(value);
        }

        public object backgroundPositionX {
            get => veStyle.backgroundPositionX;
            set {
                if (TryParseStyleBackgroundPositionSingle(value, out var styleBackgroundPosition))
                    veStyle.backgroundPositionX = styleBackgroundPosition;
            }
        }

        public object backgroundPositionY {
            get => veStyle.backgroundPositionY;
            set {
                if (TryParseStyleBackgroundPositionSingle(value, out var styleBackgroundPosition))
                    veStyle.backgroundPositionY = styleBackgroundPosition;
            }
        }

        // Composite
        public object borderColor {
            get => (veStyle.borderTopColor, veStyle.borderRightColor, veStyle.borderBottomColor, veStyle.borderLeftColor);
            set => SetBorderColor(value);
        }

        // Composite
        public object borderWidth {
            get => (veStyle.borderTopWidth, veStyle.borderRightWidth, veStyle.borderBottomWidth, veStyle.borderLeftWidth);
            set => SetBorderWidth(value);
        }

        // Composite
        public object borderRadius {
            get => (veStyle.borderTopLeftRadius, veStyle.borderTopRightRadius, veStyle.borderBottomRightRadius, veStyle.borderBottomLeftRadius);
            set => SetBorderRadius(value);
        }

        public object borderTopColor {
            get => veStyle.borderTopColor;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.borderTopColor = styleColor;
            }
        }

        public object borderRightColor {
            get => veStyle.borderRightColor;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.borderRightColor = styleColor;
            }
        }

        public object borderBottomColor {
            get => veStyle.borderBottomColor;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.borderBottomColor = styleColor;
            }
        }

        public object borderLeftColor {
            get => veStyle.borderLeftColor;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.borderLeftColor = styleColor;
            }
        }

        public object borderTopWidth {
            get => veStyle.borderTopWidth;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.borderTopWidth = styleFloat;
            }
        }

        public object borderRightWidth {
            get => veStyle.borderRightWidth;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.borderRightWidth = styleFloat;
            }
        }

        public object borderBottomWidth {
            get => veStyle.borderBottomWidth;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.borderBottomWidth = styleFloat;
            }
        }

        public object borderLeftWidth {
            get => veStyle.borderLeftWidth;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.borderLeftWidth = styleFloat;
            }
        }

        public object borderTopLeftRadius {
            get => veStyle.borderTopLeftRadius;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.borderTopLeftRadius = styleLength;
            }
        }

        public object borderTopRightRadius {
            get => veStyle.borderTopRightRadius;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.borderTopRightRadius = styleLength;
            }
        }

        public object borderBottomRightRadius {
            get => veStyle.borderBottomRightRadius;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.borderBottomRightRadius = styleLength;
            }
        }

        public object borderBottomLeftRadius {
            get => veStyle.borderBottomLeftRadius;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.borderBottomLeftRadius = styleLength;
            }
        }
        #endregion

        #region ParseStyles
        public bool TryParseStyleEnum<T>(object value, out StyleEnum<T> styleEnum) where T : struct, IConvertible {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleEnum = new StyleEnum<T>(keyword);
                return true;
            }
            if (value is double d) {
                if (Enum.IsDefined(typeof(T), (int)d)) {
                    styleEnum = new StyleEnum<T>((T)((object)(int)d));
                    return true;
                }
            } else if (value is string s) {
                if (Enum.TryParse(typeof(T), s, true, out var e)) {
                    styleEnum = new StyleEnum<T>((T)e);
                    return true;
                }
            }
            styleEnum = default;
            return false;
        }

        public bool TryParseStyleColor(object value, out StyleColor styleColor) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleColor = new StyleColor(keyword);
                return true;
            }
            if (value == null) {
                styleColor = new StyleColor(StyleKeyword.Null);
                return true;
            }

            if (value is string s) {
                var c = ColorUtility.TryParseHtmlString(s, out var color) ? color : Color.white;
                styleColor = new StyleColor(c);
                return true;
            } else if (value is Color c) {
                styleColor = new StyleColor(c);
                return true;
            }
            styleColor = default;
            return false;
        }

        public bool TryParseStyleBackground(object value, out StyleBackground styleBackground) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleBackground = new StyleBackground(keyword);
                return true;
            }
            if (value == null) {
                styleBackground = new StyleBackground(StyleKeyword.Null);
                return true;
            }

            if (value is string s) {
                var texture = _dom.document.loadImage(s);
                if (texture != null) {
                    styleBackground = new StyleBackground(Background.FromTexture2D(texture));
                    return true;
                }
            } else if (value is Texture2D t) {
                styleBackground = new StyleBackground(Background.FromTexture2D(t));
                return true;
            } else if (value is Sprite sp) {
                styleBackground = new StyleBackground(Background.FromSprite(sp));
                return true;
            } else if (value is RenderTexture rt) {
                styleBackground = new StyleBackground(Background.FromRenderTexture(rt));
                return true;
            } else if (value is VectorImage vi) {
                styleBackground = new StyleBackground(Background.FromVectorImage(vi));
                return true;
            }
            styleBackground = default;
            return false;
        }

        public bool TryParseStyleBackgroundSize(object value, out StyleBackgroundSize styleBackgroundSize) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleBackgroundSize = new StyleBackgroundSize(keyword);
                return true;
            }
            if (value == null) {
                styleBackgroundSize = new StyleBackgroundSize(StyleKeyword.Null);
                return true;
            }

            if (value is string str) {
                var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (GetLength(parts[0], out var x)) {
                    if (parts.Length > 1 && GetLength(parts[1], out var y)) {
                        styleBackgroundSize = new BackgroundSize(x, y);
                        return true;
                    }
                    styleBackgroundSize = new BackgroundSize(x, x); // If only one value is provided, use it for both x and y
                    return true;
                }
            }
            styleBackgroundSize = default;
            return false;
        }

        public bool TryParseStyleBackgroundRepeat(object value, out StyleBackgroundRepeat styleBackgroundRepeat) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleBackgroundRepeat = new StyleBackgroundRepeat(keyword);
                return true;
            }
            if (value == null) {
                styleBackgroundRepeat = new StyleBackgroundRepeat(StyleKeyword.Null);
                return true;
            }

            if (value is string str) {
                var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (Enum.TryParse(parts[0], true, out Repeat repeat)) {
                        styleBackgroundRepeat = new BackgroundRepeat(repeat, repeat);
                        return true;
                    }
                } else if (parts.Length == 2) {
                    if (Enum.TryParse(parts[0], true, out Repeat x) && Enum.TryParse(parts[1], true, out Repeat y)) {
                        styleBackgroundRepeat = new BackgroundRepeat(x, y);
                        return true;
                    }
                }
            }
            styleBackgroundRepeat = default;
            return false;
        }

        public void SetBackgroundPosition(object value) {
            if (value is string s && StyleKeyword.TryParse(s, true, out StyleKeyword keyword)) {
                _dom.ve.style.backgroundPositionX = new StyleBackgroundPosition(keyword);
                _dom.ve.style.backgroundPositionY = new StyleBackgroundPosition(keyword);
                return;
            }
            if (value is string str) {
                var parts = str.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x)) {
                        _dom.ve.style.backgroundPositionX = new BackgroundPosition(x);
                        _dom.ve.style.backgroundPositionY = new BackgroundPosition(x);
                    }
                } else if (parts.Length == 2) {
                    if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x) && GetLength(parts[1], out var l)) {
                        _dom.ve.style.backgroundPositionX = new BackgroundPosition(x, l);
                    } else if (Enum.TryParse(parts[0], true, out x) && Enum.TryParse(parts[1], true, out BackgroundPositionKeyword y)) {
                        _dom.ve.style.backgroundPositionX = new BackgroundPosition(x, 0);
                        _dom.ve.style.backgroundPositionY = new BackgroundPosition(y, 0);
                    }
                } else if (parts.Length == 4) {
                    if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword x) && GetLength(parts[1], out var lx) &&
                        Enum.TryParse(parts[2], true, out BackgroundPositionKeyword y) && GetLength(parts[3], out var ly)) {
                        _dom.ve.style.backgroundPositionX = new BackgroundPosition(x, lx);
                        _dom.ve.style.backgroundPositionY = new BackgroundPosition(y, ly);
                    }
                }
            }
        }

        public bool TryParseStyleBackgroundPositionSingle(object value, out StyleBackgroundPosition styleBackgroundPosition) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleBackgroundPosition = new StyleBackgroundPosition(keyword);
                return true;
            }
            if (value == null) {
                styleBackgroundPosition = new StyleBackgroundPosition(StyleKeyword.Null);
                return true;
            }

            if (value is string s) {
                var parts = s.ToLower().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword posKeyword)) {
                        styleBackgroundPosition = new BackgroundPosition(posKeyword);
                        return true;
                    }
                } else if (parts.Length == 2) {
                    if (Enum.TryParse(parts[0], true, out BackgroundPositionKeyword posKeyword) && GetLength(parts[1], out var l)) {
                        styleBackgroundPosition = new BackgroundPosition(posKeyword, l);
                        return true;
                    }
                }
            }
            styleBackgroundPosition = default;
            return false;
        }

        public void SetBorderColor(object value) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                _dom.ve.style.borderBottomColor = new StyleColor(keyword);
                _dom.ve.style.borderLeftColor = new StyleColor(keyword);
                _dom.ve.style.borderRightColor = new StyleColor(keyword);
                _dom.ve.style.borderTopColor = new StyleColor(keyword);
                return;
            }
            if (value is string s) {
                var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (TryParseColorString(parts[0], out var c)) {
                        __setBorderColors(_dom, c, c, c, c);
                    }
                } else if (parts.Length == 2) {
                    if (TryParseColorString(parts[0], out var tb) && TryParseColorString(parts[1], out var lr)) {
                        __setBorderColors(_dom, tb, lr, tb, lr);
                    }
                } else if (parts.Length == 3) {
                    if (TryParseColorString(parts[0], out var t) && TryParseColorString(parts[1], out var lr) &&
                        TryParseColorString(parts[2], out var b)) {
                        __setBorderColors(_dom, t, lr, b, lr);
                    }
                } else if (parts.Length == 4) {
                    if (TryParseColorString(parts[0], out var t) && TryParseColorString(parts[1], out var r) &&
                        TryParseColorString(parts[2], out var b) && TryParseColorString(parts[3], out var l)) {
                        __setBorderColors(_dom, t, r, b, l);
                    }
                }
            } else if (value is Color c) {
                __setBorderColors(_dom, c, c, c, c);
            } else if (value is Puerts.JSObject jsObj) {
                if (jsObj.Get<int>("length") == 1) {
                    var cc = jsObj.Get<Color>("0");
                    __setBorderColors(_dom, cc, cc, cc, cc);
                } else if (jsObj.Get<int>("length") == 2) {
                    var tb = jsObj.Get<Color>("0");
                    var lr = jsObj.Get<Color>("1");
                    __setBorderColors(_dom, tb, lr, tb, lr);
                } else if (jsObj.Get<int>("length") == 3) {
                    var t = jsObj.Get<Color>("0");
                    var lr = jsObj.Get<Color>("1");
                    var b = jsObj.Get<Color>("2");
                    __setBorderColors(_dom, t, lr, b, lr);
                } else if (jsObj.Get<int>("length") == 4) {
                    var t = jsObj.Get<Color>("0");
                    var r = jsObj.Get<Color>("1");
                    var b = jsObj.Get<Color>("2");
                    var l = jsObj.Get<Color>("3");
                    __setBorderColors(_dom, t, r, b, l);
                }
            }
        }

        public void SetBorderWidth(object value) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                _dom.ve.style.borderBottomWidth = new StyleFloat(keyword);
                _dom.ve.style.borderLeftWidth = new StyleFloat(keyword);
                _dom.ve.style.borderRightWidth = new StyleFloat(keyword);
                _dom.ve.style.borderTopWidth = new StyleFloat(keyword);
                return;
            }
            if (value is string s) {
                var parts = s.Replace("px", "").Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (float.TryParse(parts[0], out var l)) {
                        __setBorderWidths(_dom, l, l, l, l);
                    }
                } else if (parts.Length == 2) {
                    if (float.TryParse(parts[0], out var tb) && float.TryParse(parts[1], out var lr)) {
                        __setBorderWidths(_dom, tb, lr, tb, lr);
                    }
                } else if (parts.Length == 3) {
                    if (float.TryParse(parts[0], out var t) && float.TryParse(parts[1], out var lr) &&
                        float.TryParse(parts[2], out var b)) {
                        __setBorderWidths(_dom, t, lr, b, lr);
                    }
                } else if (parts.Length == 4) {
                    if (float.TryParse(parts[0], out var t) && float.TryParse(parts[1], out var r) &&
                        float.TryParse(parts[2], out var b) && float.TryParse(parts[3], out var l)) {
                        __setBorderWidths(_dom, t, r, b, l);
                    }
                }
            } else if (value is double d) {
                __setBorderWidths(_dom, (float)d, (float)d, (float)d, (float)d);
            } else if (value is Puerts.JSObject jsObj) {
                if (jsObj.Get<int>("length") == 1) {
                    var l = jsObj.Get<float>("0");
                    __setBorderWidths(_dom, l, l, l, l);
                } else if (jsObj.Get<int>("length") == 2) {
                    var tb = jsObj.Get<float>("0");
                    var lr = jsObj.Get<float>("1");
                    __setBorderWidths(_dom, tb, lr, tb, lr);
                } else if (jsObj.Get<int>("length") == 3) {
                    var t = jsObj.Get<float>("0");
                    var lr = jsObj.Get<float>("1");
                    var b = jsObj.Get<float>("2");
                    __setBorderWidths(_dom, t, lr, b, lr);
                } else if (jsObj.Get<int>("length") == 4) {
                    var t = jsObj.Get<float>("0");
                    var r = jsObj.Get<float>("1");
                    var b = jsObj.Get<float>("2");
                    var l = jsObj.Get<float>("3");
                    __setBorderWidths(_dom, t, r, b, l);
                }
            }
        }

        public void SetBorderRadius(object value) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                _dom.ve.style.borderBottomLeftRadius = new StyleLength(keyword);
                _dom.ve.style.borderBottomRightRadius = new StyleLength(keyword);
                _dom.ve.style.borderTopLeftRadius = new StyleLength(keyword);
                _dom.ve.style.borderTopRightRadius = new StyleLength(keyword);
                return;
            }
            if (value is string s) {
                var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (GetLength(parts[0], out var l)) {
                        __setBorderRadii(_dom, l, l, l, l);
                    }
                } else if (parts.Length == 2) {
                    if (GetLength(parts[0], out var tlbr) && GetLength(parts[1], out var trbl)) {
                        __setBorderRadii(_dom, tlbr, trbl, tlbr, trbl);
                    }
                } else if (parts.Length == 3) {
                    if (GetLength(parts[0], out var tl) && GetLength(parts[1], out var trbl) &&
                        GetLength(parts[2], out var br)) {
                        __setBorderRadii(_dom, tl, trbl, br, trbl);
                    }
                } else if (parts.Length == 4) {
                    if (GetLength(parts[0], out var tl) && GetLength(parts[1], out var tr) &&
                        GetLength(parts[2], out var br) && GetLength(parts[3], out var bl)) {
                        __setBorderRadii(_dom, tl, tr, br, bl);
                    }
                }
            } else if (value is double d) {
                var l = new Length((float)d);
                __setBorderRadii(_dom, l, l, l, l);
            } else if (value is Puerts.JSObject jsObj) {
                if (jsObj.Get<int>("length") == 1) {
                    var l = new Length(jsObj.Get<float>("0"));
                    __setBorderRadii(_dom, l, l, l, l);
                } else if (jsObj.Get<int>("length") == 2) {
                    var tlbr = new Length(jsObj.Get<float>("0"));
                    var trbl = new Length(jsObj.Get<float>("1"));
                    __setBorderRadii(_dom, tlbr, trbl, trbl, tlbr);
                } else if (jsObj.Get<int>("length") == 3) {
                    var tl = new Length(jsObj.Get<float>("0"));
                    var trbl = new Length(jsObj.Get<float>("1"));
                    var br = new Length(jsObj.Get<float>("2"));
                    __setBorderRadii(_dom, tl, trbl, br, trbl);
                } else if (jsObj.Get<int>("length") == 4) {
                    var tl = new Length(jsObj.Get<float>("0"));
                    var tr = new Length(jsObj.Get<float>("1"));
                    var br = new Length(jsObj.Get<float>("2"));
                    var bl = new Length(jsObj.Get<float>("3"));
                    __setBorderRadii(_dom, tl, tr, br, bl);
                }
            }
        }

        public bool TryParseStyleLength(object value, out StyleLength styleLength) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleLength = new StyleLength(keyword);
                return true;
            }
            if (value == null) {
                styleLength = new StyleLength(StyleKeyword.Null);
                return true;
            }

            if (value is string s) {
                if (s.EndsWith("px")) {
                    if (float.TryParse(s.Substring(0, s.Length - 2), out var pixelValue)) {
                        styleLength = new StyleLength(new Length(pixelValue));
                        return true;
                    }
                } else if (s.EndsWith("%")) {
                    if (float.TryParse(s.Substring(0, s.Length - 1), out var percentValue)) {
                        styleLength = new StyleLength(new Length(percentValue, LengthUnit.Percent));
                        return true;
                    }
                }
            } else if (value is double doubleValue) {
                styleLength = new StyleLength((float)doubleValue);
                return true;
            }
            styleLength = default;
            return false;
        }

        public bool TryParseStyleFloat(object value, out StyleFloat styleFloat) {
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                styleFloat = new StyleFloat(keyword);
                return true;
            }
            if (value == null) {
                styleFloat = new StyleFloat(StyleKeyword.Null);
                return true;
            }

            if (value is double d) {
                styleFloat = new StyleFloat((float)d);
                return true;
            }
            styleFloat = default;
            return false;
        }
        #endregion

        #region Static Utils
        public static bool GetLength(object value, out Length lengthValue) {
            if (value is string s) {
                // Attempt to parse the string for length values (e.g., "100px", "50%")
                if (s.EndsWith("px")) {
                    if (float.TryParse(s.Substring(0, s.Length - 2), out var pixelValue)) {
                        lengthValue = new Length(pixelValue);
                        return true;
                    }
                } else if (s.EndsWith("%")) {
                    if (float.TryParse(s.Substring(0, s.Length - 1), out var percentValue)) {
                        lengthValue = new Length(percentValue, LengthUnit.Percent);
                        return true;
                    }
                }
            } else if (value is double doubleValue) {
                lengthValue = new Length((float)doubleValue);
                return true;
            }
            lengthValue = default;
            return false;
        }

        static void __setBorderColors(Dom dom, Color t, Color r, Color b, Color l) {
            dom.ve.style.borderBottomColor = new StyleColor(b);
            dom.ve.style.borderLeftColor = new StyleColor(l);
            dom.ve.style.borderRightColor = new StyleColor(r);
            dom.ve.style.borderTopColor = new StyleColor(t);
        }

        static void __setBorderWidths(Dom dom, float t, float r, float b, float l) {
            dom.ve.style.borderBottomWidth = new StyleFloat(b);
            dom.ve.style.borderLeftWidth = new StyleFloat(l);
            dom.ve.style.borderRightWidth = new StyleFloat(r);
            dom.ve.style.borderTopWidth = new StyleFloat(t);
        }

        static void __setBorderRadii(Dom dom, Length tl, Length tr, Length br, Length bl) {
            dom.ve.style.borderBottomLeftRadius = new StyleLength(bl);
            dom.ve.style.borderBottomRightRadius = new StyleLength(br);
            dom.ve.style.borderTopLeftRadius = new StyleLength(tl);
            dom.ve.style.borderTopRightRadius = new StyleLength(tr);
        }
        #endregion
    }
}