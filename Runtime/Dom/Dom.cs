using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    public class RegisteredCallbackHolder {
        public EventCallback<EventBase> callback;
        public JsValue jsValue;
    }

    public class Dom {
        public Document document => _document;

        public VisualElement ve => _ve;

        public Dom[] childNodes => _childNodes.ToArray();

        public Dom firstChild => _childNodes.Count > 0 ? childNodes[0] : null;

        public Dom parentNode { get { return _parentNode; } }

        public Dom nextSibling { get { return _nextSibling; } }

        /// <summary>
        /// ECMA Compliant id property, stored in the VE.name
        /// </summary>
        public string Id { get { return _ve.name; } set { _ve.name = value; } }

        public string key { get { return _key; } set { _key = value; } }

        public DomStyle style => new DomStyle(this);

        public object value { get { return _value; } }

        public bool @checked { get { return _checked; } }

        public object data {
            get { return _data; }
            set {
                _data = value;
                if (_ve is TextElement) {
                    (_ve as TextElement).text = value.ToString();
                }
            }
        }

        public string innerHTML { get { return _innerHTML; } }

        public Vector2 layoutSize => _ve.layout.size;

        public object _children { get { return __children; } set { __children = value; } }

        // NOTE: Using `JsValue` here because `EventCallback<EventBase>` will lead to massive slowdown on Linux.
        // [props.ts] `dom._listeners[name + useCapture] = value;`
        public Dictionary<string, JsValue> _listeners => __listeners;

        Document _document;
        VisualElement _ve;
        string _key;
        Dom _parentNode;
        Dom _nextSibling;
        object _value;
        bool _checked;
        object _data;
        string _innerHTML;
        List<Dom> _childNodes = new List<Dom>();
        object __children;
        Dictionary<string, JsValue> __listeners = new Dictionary<string, JsValue>();

        Dictionary<string, List<RegisteredCallbackHolder>> _registeredCallbacks =
            new Dictionary<string, List<RegisteredCallbackHolder>>();

        static Dictionary<string, RegisterCallbackDelegate> _eventCache =
            new Dictionary<string, RegisterCallbackDelegate>();

        TickBasedCallTracker _tickBasedCallTracker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            _eventCache.Clear();
        }

        public static void RegisterCallback<T>(VisualElement ve, EventCallback<T> callback,
            TrickleDown trickleDown = TrickleDown.NoTrickleDown)
            where T : EventBase<T>, new() {
            ve.RegisterCallback(callback, trickleDown);
        }

        public delegate void RegisterCallbackDelegate(VisualElement ve, EventCallback<EventBase> callback,
            TrickleDown trickleDown = TrickleDown.NoTrickleDown);

        // Not Used
        //public Dom(string tagName) {
        //    _ve = new VisualElement();
        //}

        public Dom(VisualElement ve, Document document) {
            _ve = ve;
            _document = document;
        }

        public void CallListener(string name, EventBase evt) {
            // var func = __listeners[name].As<FunctionInstance>();
            var tick = _document.scriptEngine.Tick;
            _tickBasedCallTracker.count = _tickBasedCallTracker.tick == tick ? _tickBasedCallTracker.count + 1 : 0;
            _tickBasedCallTracker.tick = tick;
            if (_tickBasedCallTracker.count > 1000) {
                Debug.LogError(
                    $"Possible infinite loop detected. Event Listener(s) on {_ve.GetType().Name} called more than 1000 times in one frame.");
                return;
            }
            var jintEngine = _document.scriptEngine.JintEngine;
            var thisDom = JsValue.FromObject(jintEngine, this);
            jintEngine.Call(__listeners[name], thisDom, new[] { JsValue.FromObject(jintEngine, evt) });
        }

        public void clearChildren() {
            _ve.Clear();
        }

        public void addEventListener(string name, JsValue jsval, bool useCapture = false) {
            // var t = DateTime.Now;
            // var func = jsval.As<FunctionInstance>();
            var engine = _document.scriptEngine.JintEngine;
            var thisDom = JsValue.FromObject(engine, this);
            var callback = (EventCallback<EventBase>)((e) => {
                engine.Call(jsval, thisDom, new[] { JsValue.FromObject(engine, e) });
            });
            var isValueChanged = name == "ValueChanged";
            // Debug.Log("addEventListener " + name + " on " + _ve.name + " " + isValueChanged);

            if (!isValueChanged && _eventCache.ContainsKey(name)) {
                _eventCache[name](_ve, callback, TrickleDown.NoTrickleDown);
                // Debug.Log("Registered " + name + " on " + _ve.name);
            } else {
                var eventType = _document.FindUIElementEventType(name);
                if (isValueChanged) {
                    var notifyInterface = _ve.GetType().GetInterfaces().Where(i => i.Name == "INotifyValueChanged`1")
                        .FirstOrDefault();
                    if (notifyInterface != null) {
                        var valType = notifyInterface.GenericTypeArguments[0];
                        eventType = typeof(VisualElement).Assembly.GetType($"UnityEngine.UIElements.ChangeEvent`1");
                        eventType = eventType.MakeGenericType(valType);
                    }
                }
                if (eventType != null) {
                    var mi = this.GetType().GetMethod("RegisterCallback");
                    mi = mi.MakeGenericMethod(eventType);
                    // mi.Invoke(null, new object[] { _ve, callback });
                    var del = (RegisterCallbackDelegate)Delegate.CreateDelegate(typeof(RegisterCallbackDelegate), mi);
                    if (!isValueChanged && !_eventCache.ContainsKey(name))
                        _eventCache.Add(name, del);
                    del(_ve, callback, TrickleDown.NoTrickleDown);
                    // Debug.Log("Registered " + name + " on " + _ve.name);
                }
            }

            var callbackHolder = new RegisteredCallbackHolder() { callback = callback, jsValue = jsval };
            if (_registeredCallbacks.ContainsKey(name))
                _registeredCallbacks[name].Add(callbackHolder);
            else
                _registeredCallbacks.Add(name, new List<RegisteredCallbackHolder> { callbackHolder });

            // Debug.Log($"{name} {(DateTime.Now - t).TotalMilliseconds}ms");
        }

        /// <summary>
        /// Note that this method will remove ALL callbacks registered for the given event name.
        /// </summary>
        public void removeEventListener(string name, JsValue jsval, bool useCapture = false) {
            if (!_registeredCallbacks.ContainsKey(name))
                return;
            var callbackHolders = _registeredCallbacks[name];
            var eventType = _document.FindUIElementEventType(name);
            if (eventType != null) {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                var mi = _ve.GetType().GetMethods(flags)
                    .Where(m => m.Name == "UnregisterCallback" && m.GetGenericArguments().Length == 1).First();
                mi = mi.MakeGenericMethod(eventType);
                for (var i = 0; i < callbackHolders.Count; i++) {
                    if (callbackHolders[i].jsValue == jsval) {
                        mi.Invoke(_ve, new object[] { callbackHolders[i].callback, null });
                        callbackHolders.RemoveAt(i);
                        i--;
                    }
                }
                if (callbackHolders.Count == 0) {
                    _registeredCallbacks.Remove(name);
                }
            }
        }

        public void appendChild(Dom node) {
            // Debug.Log(
            //     $"{this._ve.GetType().Name} [{this._ve.childCount}] ({this._ve.GetHashCode()}) Adding {node.ve.GetType().Name} ({node.ve.GetHashCode()})");
            // if (node.ve.GetType().Name == "TextElement") {
            //     Debug.Log((node.ve as TextElement).text);
            // }
            try {
                this._ve.Add(node.ve);
            } catch (Exception e) {
                Debug.LogError(e.Message);
                throw new Exception("Invalid Dom appendChild");
            }
            node._parentNode = this;
            if (_childNodes.Count > 0) {
                _childNodes[_childNodes.Count - 1]._nextSibling = node;
            }
            _childNodes.Add(node);
        }

        public void removeChild(Dom child) {
            if (child == null || !this._ve.Contains(child.ve))
                return;
            using (var evt = TransitionCancelEvent.GetPooled()) {
                evt.target = child.ve;
                child.ve.SendEvent(evt);
            }
            this._ve.Remove(child.ve);
            var index = _childNodes.IndexOf(child);
            if (index > 0) {
                var prev = _childNodes[index - 1];
                prev._nextSibling = child._nextSibling;
            }
            _childNodes.Remove(child);
            child._parentNode = null;
        }

        public void insertBefore(Dom a, Dom b) {
            if (b == null || b.ve == null || _ve.IndexOf(b.ve) == -1) {
                appendChild(a);
                return;
            }
            var index = _ve.IndexOf(b.ve);
            _ve.Insert(index, a.ve);
            _childNodes.Insert(index, a);
            a._nextSibling = b;
            a._parentNode = this;
            if (index > 0) {
                _childNodes[index - 1]._nextSibling = a;
            }
        }

        public void setAttribute(string name, object val) {
            if (name == "class" || name == "className") {
                var unityClassnames = _ve.GetClasses().Where(c => c.StartsWith("unity-")).ToArray();
                _ve.ClearClassList();
                var unprocessedClassStr = _document.scriptEngine.ProcessClassStr(val.ToString(), this);
                var parts = (unprocessedClassStr).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var unityClassname in unityClassnames) {
                    _ve.AddToClassList(unityClassname);
                }
                foreach (var part in parts) {
                    _ve.AddToClassList(part);
                }
            } else if (name == "id" || name == "name") {
                _ve.name = val.ToString();
            } else if (name == "disabled") {
                _ve.SetEnabled(!Convert.ToBoolean(val));
            } else {
                name = name.Replace("-", "");
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
                var ei = _ve.GetType().GetEvent(name, flags);
                if (ei != null) {
                    ei.AddMethod.Invoke(_ve, new object[] { val });
                    return;
                }
                var pi = _ve.GetType().GetProperty(name, flags);
                if (pi != null) {
                    if (pi.PropertyType.IsEnum) {
                        val = Convert.ToInt32(val);
                    } else if (val.GetType() == typeof(object[])) {
                        var objAry = (object[])val;
                        var length = ((object[])val).Length;
                        if (pi.PropertyType.IsArray) {
                            Array destinationArray = Array.CreateInstance(pi.PropertyType.GetElementType(), length);
                            Array.Copy(objAry, destinationArray, length);
                            val = destinationArray;
                        } else {
                            var genericArgs = pi.PropertyType.GetGenericArguments();
                            var listType = typeof(List<>).MakeGenericType(genericArgs);
                            if (pi.PropertyType == listType) {
                                Array destinationArray = Array.CreateInstance(genericArgs[0], length);
                                Array.Copy(objAry, destinationArray, length);
                                var list = (IList)Activator.CreateInstance(listType, destinationArray);
                                val = list;
                            }
                        }
                    } else if (pi.PropertyType == typeof(Single) && val.GetType() == typeof(double)) {
                        val = Convert.ToSingle(val);
                    } else if (pi.PropertyType == typeof(Int32) && val.GetType() == typeof(double)) {
                        val = Convert.ToInt32(val);
                    } else if (pi.PropertyType == typeof(char) && val.GetType() == typeof(string)) {
                        val = val.ToString()[0];
                    }
                    pi.SetValue(_ve, val);
                }
            }
        }

        public void removeAttribute(string name) {
            if (name == "class" || name == "className") {
                _ve.ClearClassList();
            } else if (name == "id") {
                _ve.name = null;
            } else {
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
                var pi = _ve.GetType().GetProperty(name, flags);
                if (pi != null) {
                    pi.SetValue(_ve, null);
                }
            }
        }

        public void focus() {
            _ve.Focus();
        }

        public override string ToString() {
            return $"dom: {this._ve.GetType().Name} ({this._ve.GetHashCode()})";
        }

        /// <summary>
        /// BFS for first predicate matching Dom, including this one.
        /// </summary>
        /// <param name="predicate">Search criteria</param>
        /// <returns>Matching Dom or null</returns>
        public Dom First(Func<Dom, bool> predicate) {
            Queue<Dom> q = new();
            q.Enqueue(this);
            while (q.Count > 0) {
                var cnt = q.Count;
                for (int i = 0; i < cnt; i++) {
                    var cur = q.Dequeue();
                    if (predicate(cur)) {
                        return cur;
                    }
                    if (cur._childNodes != null) {
                        for (int ci = 0; ci < cur._childNodes.Count; ci++) {
                            q.Enqueue(cur._childNodes[ci]);
                        }
                    }
                }
            }
            return null;
        }

        struct TickBasedCallTracker {
            public int tick;
            public int count;
        }
    }
}
