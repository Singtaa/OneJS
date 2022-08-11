using Jint;
using Jint.Native;
using Jint.Native.Object;
using OneJS.Dom;
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    /// <summary>
    /// Example VisualElement for interoperating with OneJS list models.
    /// </summary>
    public class SimpleListView : ListView {
        /// <summary>
        /// Recover the original Component that was rendered by the associated VisualElement.
        /// </summary>
        private Dictionary<VisualElement, JsValue> _inverse = new();

        public JsValue make {
            set => this.makeItem = () => {
                    var elem = value.As<Jint.Native.Function.FunctionInstance>().Call();
                    if (!elem.IsObject()) {
                        throw new Exception("make must return an object.");
                    }
                    ObjectInstance veo = elem.AsObject();
                    var _domProp = veo.Get("_dom");
                    if (_domProp == null) {
                        throw new Exception("must return rendered element");
                    }
                    object domo = _domProp.ToObject();
                    if (!(domo is Dom)) {
                        throw new Exception("element must return a rendered Dom");
                    }
                    Dom dom = domo as Dom;
                    VisualElement ve = dom.ve;
                    _inverse.Add(ve, elem);
                    return ve;
                };
        }
        public JsValue bind {
            set => this.bindItem = (VisualElement ve, int i) => {
                    if (_inverse.TryGetValue(ve, out JsValue dom)) {
                        var func = value.As<Jint.Native.Function.FunctionInstance>();
                        var engine = func.Engine;
                        engine.Invoke(func, dom, i);
                    }
                };
        }
    }
}
