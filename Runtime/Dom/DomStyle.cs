using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    public class DomStyle {
        Dom _dom;

        public DomStyle(Dom dom) {
            this._dom = dom;
        }

        public object this[string key] {
            get {
                var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
                key = key.Replace("_", "").Replace("-", "");
                var pi = typeof(IStyle).GetProperty(key, flags);
                if (pi != null) {
                    // var engine = _dom.document.scriptEngine.JintEngine;
                    return pi.GetValue(_dom.ve.style);
                }
                return null;
            }
            set { setProperty(key, value); }
        }

        public void setProperty(string key, object val) {
            var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
            key = key.Replace("_", "").Replace("-", "");
            var pi = typeof(IStyle).GetProperty(key, flags);
            if (pi != null) {
                key = pi.Name;
                var engine = _dom.document.scriptEngine.CoreEngine;
                // var globalThis = engine.GetValue("globalThis");
                // var func = globalThis.Get("__setStyleProperty");
                var func = engine.Evaluate("globalThis.__setStyleProperty");
                engine.Call(func, _dom.ve.style, key, val);
                // func.Call(_dom.ve.style, key, val);
            }
        }

        public IStyle GetVEStyle() {
            return _dom.ve.style;
        }
    }
}
