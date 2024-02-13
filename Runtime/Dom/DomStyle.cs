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
        Dom _dom;

        public DomStyle(Dom dom) {
            this._dom = dom;
        }

        public IStyle veStyle => _dom.ve.style;

        public void setProperty(string key, object value) {
            var pi = this.GetType().GetProperty(key, BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public);
            if (pi != null) {
                pi.SetValue(this, value);
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

        public object bottom {
            get => veStyle.bottom;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.bottom = styleLength;
            }
        }

        public object color {
            get => veStyle.color;
            set {
                if (TryParseStyleColor(value, out var styleColor))
                    veStyle.color = styleColor;
            }
        }

        public object cursor {
            get => veStyle.cursor;
            set {
                if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                    veStyle.cursor = new StyleCursor(keyword);
                } else if (value is Cursor c) {
                    veStyle.cursor = new StyleCursor(c);
                }
            }
        }

        public object display {
            get => veStyle.display;
            set {
                if (TryParseStyleEnum<DisplayStyle>(value, out var styleEnum))
                    veStyle.display = styleEnum;
            }
        }

        public object flexBasis {
            get => veStyle.flexBasis;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.flexBasis = styleLength;
            }
        }

        public object flexDirection {
            get => veStyle.flexDirection;
            set {
                if (TryParseStyleEnum<FlexDirection>(value, out var styleEnum))
                    veStyle.flexDirection = styleEnum;
            }
        }

        public object flexGrow {
            get => veStyle.flexGrow;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.flexGrow = styleFloat;
            }
        }

        public object flexShrink {
            get => veStyle.flexShrink;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.flexShrink = styleFloat;
            }
        }

        public object flexWrap {
            get => veStyle.flexWrap;
            set {
                if (TryParseStyleEnum<Wrap>(value, out var styleEnum))
                    veStyle.flexWrap = styleEnum;
            }
        }

        public object fontSize {
            get => veStyle.fontSize;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.fontSize = styleLength;
            }
        }

        public object height {
            get => veStyle.height;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.height = styleLength;
            }
        }

        public object justifyContent {
            get => veStyle.justifyContent;
            set {
                if (TryParseStyleEnum<Justify>(value, out var styleEnum))
                    veStyle.justifyContent = styleEnum;
            }
        }

        public object left {
            get => veStyle.left;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.left = styleLength;
            }
        }

        public object letterSpacing {
            get => veStyle.letterSpacing;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.letterSpacing = styleLength;
            }
        }

        // Composite
        public object margin {
            get => (veStyle.marginTop, veStyle.marginRight, veStyle.marginBottom, veStyle.marginLeft);
            set => SetMargin(value);
        }

        public object marginTop {
            get => veStyle.marginTop;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.marginTop = styleLength;
            }
        }

        public object marginRight {
            get => veStyle.marginRight;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.marginRight = styleLength;
            }
        }

        public object marginBottom {
            get => veStyle.marginBottom;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.marginBottom = styleLength;
            }
        }

        public object marginLeft {
            get => veStyle.marginLeft;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.marginLeft = styleLength;
            }
        }

        public object maxHeight {
            get => veStyle.maxHeight;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.maxHeight = styleLength;
            }
        }

        public object maxWidth {
            get => veStyle.maxWidth;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.maxWidth = styleLength;
            }
        }

        public object minHeight {
            get => veStyle.minHeight;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.minHeight = styleLength;
            }
        }

        public object minWidth {
            get => veStyle.minWidth;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.minWidth = styleLength;
            }
        }

        public object opacity {
            get => veStyle.opacity;
            set {
                if (TryParseStyleFloat(value, out var styleFloat))
                    veStyle.opacity = styleFloat;
            }
        }

        public object overflow {
            get => veStyle.overflow;
            set {
                if (TryParseStyleEnum<Overflow>(value, out var styleEnum))
                    veStyle.overflow = styleEnum;
            }
        }
        
        public object padding {
            get => (veStyle.paddingTop, veStyle.paddingRight, veStyle.paddingBottom, veStyle.paddingLeft);
            set => SetPadding(value);
        }
        
        public object paddingTop {
            get => veStyle.paddingTop;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.paddingTop = styleLength;
            }
        }
        
        public object paddingRight {
            get => veStyle.paddingRight;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.paddingRight = styleLength;
            }
        }
        
        public object paddingBottom {
            get => veStyle.paddingBottom;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.paddingBottom = styleLength;
            }
        }
        
        public object paddingLeft {
            get => veStyle.paddingLeft;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.paddingLeft = styleLength;
            }
        }
        
        public object position {
            get => veStyle.position;
            set {
                if (TryParseStyleEnum<Position>(value, out var styleEnum))
                    veStyle.position = styleEnum;
            }
        }
        
        public object right {
            get => veStyle.right;
            set {
                if (TryParseStyleLength(value, out var styleLength))
                    veStyle.right = styleLength;
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
            if (value == null) {
                __setBorderColorKeyword(_dom, StyleKeyword.Null);
                return;
            }
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                __setBorderColorKeyword(_dom, keyword);
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
            if (value == null) {
                __setBorderWidthKeyword(_dom, StyleKeyword.Null);
                return;
            }
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                __setBorderWidthKeyword(_dom, keyword);
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
            if (value == null) {
                __setBorderRadiusKeyword(_dom, StyleKeyword.Null);
                return;
            }
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                __setBorderRadiusKeyword(_dom, keyword);
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

        // Make use of GetLength() method
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
                if (GetLength(s, out var length)) {
                    styleLength = new StyleLength(length);
                    return true;
                }
            } else if (value is double d) {
                styleLength = new StyleLength((float)d);
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

        public void SetMargin(object value) {
            if (value == null) {
                __setMarginKeyword(_dom, StyleKeyword.Null);
                return;
            }
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                __setMarginKeyword(_dom, keyword);
                return;
            }
            if (value is string s) {
                var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (GetLength(parts[0], out var l)) {
                        __setMargins(_dom, l, l, l, l);
                    }
                } else if (parts.Length == 2) {
                    if (GetLength(parts[0], out var tb) && GetLength(parts[1], out var lr)) {
                        __setMargins(_dom, tb, lr, tb, lr);
                    }
                } else if (parts.Length == 3) {
                    if (GetLength(parts[0], out var t) && GetLength(parts[1], out var lr) &&
                        GetLength(parts[2], out var b)) {
                        __setMargins(_dom, t, lr, b, lr);
                    }
                } else if (parts.Length == 4) {
                    if (GetLength(parts[0], out var t) && GetLength(parts[1], out var r) &&
                        GetLength(parts[2], out var b) && GetLength(parts[3], out var l)) {
                        __setMargins(_dom, t, r, b, l);
                    }
                }
            }
        }

        public void SetPadding(object value) {
            if (value == null) {
                __setPaddingKeyword(_dom, StyleKeyword.Null);
                return;
            }
            if (value is string ss && StyleKeyword.TryParse(ss, true, out StyleKeyword keyword)) {
                __setPaddingKeyword(_dom, keyword);
                return;
            }
            if (value is string s) {
                var parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) {
                    if (GetLength(parts[0], out var l)) {
                        __setPaddings(_dom, l, l, l, l);
                    }
                } else if (parts.Length == 2) {
                    if (GetLength(parts[0], out var tb) && GetLength(parts[1], out var lr)) {
                        __setPaddings(_dom, tb, lr, tb, lr);
                    }
                } else if (parts.Length == 3) {
                    if (GetLength(parts[0], out var t) && GetLength(parts[1], out var lr) &&
                        GetLength(parts[2], out var b)) {
                        __setPaddings(_dom, t, lr, b, lr);
                    }
                } else if (parts.Length == 4) {
                    if (GetLength(parts[0], out var t) && GetLength(parts[1], out var r) &&
                        GetLength(parts[2], out var b) && GetLength(parts[3], out var l)) {
                        __setPaddings(_dom, t, r, b, l);
                    }
                }
            }
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

        public static bool TryParseColorString(string s, out Color color) {
            return ColorUtility.TryParseHtmlString(s, out color);
        }

        static void __setBorderColors(Dom dom, Color t, Color r, Color b, Color l) {
            dom.ve.style.borderTopColor = new StyleColor(t);
            dom.ve.style.borderRightColor = new StyleColor(r);
            dom.ve.style.borderBottomColor = new StyleColor(b);
            dom.ve.style.borderLeftColor = new StyleColor(l);
        }

        static void __setBorderColorKeyword(Dom dom, StyleKeyword keyword) {
            dom.ve.style.borderTopColor = new StyleColor(keyword);
            dom.ve.style.borderRightColor = new StyleColor(keyword);
            dom.ve.style.borderBottomColor = new StyleColor(keyword);
            dom.ve.style.borderLeftColor = new StyleColor(keyword);
        }

        static void __setBorderWidths(Dom dom, float t, float r, float b, float l) {
            dom.ve.style.borderTopWidth = new StyleFloat(t);
            dom.ve.style.borderRightWidth = new StyleFloat(r);
            dom.ve.style.borderBottomWidth = new StyleFloat(b);
            dom.ve.style.borderLeftWidth = new StyleFloat(l);
        }

        static void __setBorderWidthKeyword(Dom dom, StyleKeyword keyword) {
            dom.ve.style.borderTopWidth = new StyleFloat(keyword);
            dom.ve.style.borderRightWidth = new StyleFloat(keyword);
            dom.ve.style.borderBottomWidth = new StyleFloat(keyword);
            dom.ve.style.borderLeftWidth = new StyleFloat(keyword);
        }

        static void __setBorderRadii(Dom dom, Length tl, Length tr, Length br, Length bl) {
            dom.ve.style.borderTopLeftRadius = new StyleLength(tl);
            dom.ve.style.borderTopRightRadius = new StyleLength(tr);
            dom.ve.style.borderBottomRightRadius = new StyleLength(br);
            dom.ve.style.borderBottomLeftRadius = new StyleLength(bl);
        }

        static void __setBorderRadiusKeyword(Dom dom, StyleKeyword keyword) {
            dom.ve.style.borderTopLeftRadius = new StyleLength(keyword);
            dom.ve.style.borderTopRightRadius = new StyleLength(keyword);
            dom.ve.style.borderBottomRightRadius = new StyleLength(keyword);
            dom.ve.style.borderBottomLeftRadius = new StyleLength(keyword);
        }

        static void __setMargins(Dom dom, Length t, Length r, Length b, Length l) {
            dom.ve.style.marginTop = new StyleLength(t);
            dom.ve.style.marginRight = new StyleLength(r);
            dom.ve.style.marginBottom = new StyleLength(b);
            dom.ve.style.marginLeft = new StyleLength(l);
        }

        static void __setMarginKeyword(Dom dom, StyleKeyword keyword) {
            dom.ve.style.marginTop = new StyleLength(keyword);
            dom.ve.style.marginRight = new StyleLength(keyword);
            dom.ve.style.marginBottom = new StyleLength(keyword);
            dom.ve.style.marginLeft = new StyleLength(keyword);
        }

        static void __setPaddings(Dom dom, Length t, Length r, Length b, Length l) {
            dom.ve.style.paddingTop = new StyleLength(t);
            dom.ve.style.paddingRight = new StyleLength(r);
            dom.ve.style.paddingBottom = new StyleLength(b);
            dom.ve.style.paddingLeft = new StyleLength(l);
        }

        static void __setPaddingKeyword(Dom dom, StyleKeyword keyword) {
            dom.ve.style.paddingTop = new StyleLength(keyword);
            dom.ve.style.paddingRight = new StyleLength(keyword);
            dom.ve.style.paddingBottom = new StyleLength(keyword);
            dom.ve.style.paddingLeft = new StyleLength(keyword);
        }
        #endregion
    }
}