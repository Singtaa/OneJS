using System.Linq;
using System.Reflection;
using Jint;
using Jint.Native;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    public class DomStyle {

        Dom _dom;

        public DomStyle(Dom dom) {
            this._dom = dom;
        }

        public JsValue this[string key] {
            get {
                var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
                key = key.Replace("_", "").Replace("-", "");
                var pi = typeof(IStyle).GetProperty(key, flags);
                if (pi != null) {
                    var engine = _dom.document.scriptEngine.JintEngine;
                    return JsValue.FromObject(engine, pi.GetValue(_dom.ve.style));
                }
                return null;
            }
            set { setProperty(key, value); }
        }

        public void setProperty(string key, JsValue val) {
            var flags = BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public;
            key = key.Replace("_", "").Replace("-", "");
            var pi = typeof(IStyle).GetProperty(key, flags);
            if (pi != null) {
                key = pi.Name;
                var engine = _dom.document.scriptEngine.JintEngine;
                var globalThis = engine.GetValue("globalThis");
                var func = engine.GetValue("globalThis").Get("__setStyleProperty");
                func.Call(globalThis, JsValue.FromObject(engine, _dom.ve.style), key, val);
            }
        }

        public IStyle GetVEStyle() {
            return _dom.ve.style;
        }
    }
}