using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    public class DomStyle {
        #region Statics
        static Dictionary<string, Action<IStyle, string, object>> styleProcessors = new();

        static DomStyle() {
            AddProcessorForColor("color");
            AddProcessorForColor("backgroundColor");
            AddProcessorForColor("borderBottomColor");
            AddProcessorForColor("borderLeftColor");
            AddProcessorForColor("borderRightColor");
            AddProcessorForColor("borderTopColor");
            AddProcessorForColor("unityBackgroundImageTintColor");
            AddProcessorForColor("unityTextOutlineColor");
            // TODO Custom AddProcessor for borderColor (not an existing property)
        }

        static void AddProcessorForColor(string key) {
            var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
            var pi = typeof(IStyle).GetProperty(key, flags);
            if (pi == null)
                throw new Exception("Property not found: " + key);
            Action<IStyle, string, object> action = (style, k, v) => {
                if (v is string s) {
                    var c = ColorUtility.TryParseHtmlString(s, out var color) ? color : Color.white;
                    pi.SetValue(style, new StyleColor(c));
                    return;
                } else if (v is Color c) {
                    pi.SetValue(style, new StyleColor(c));
                    return;
                }
            };

            styleProcessors.Add(key, action);
        }
        #endregion

        Dom _dom;

        public DomStyle(Dom dom) {
            this._dom = dom;
        }

        public IStyle veStyle => _dom.ve.style;

        public void setProperty(string key, object value) {
            if (styleProcessors.TryGetValue(key, out var action)) {
                action(veStyle, key, value);
            } else {
                var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
                var pi = typeof(IStyle).GetProperty(key, flags);
                if (pi == null)
                    throw new Exception("Property not found: " + key);
                pi.SetValue(veStyle, value);
            }
        }
    }
}