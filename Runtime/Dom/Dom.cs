using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Dom {
    public class Dom {
        public Document document => _document;

        public VisualElement ve => _ve;

        public Dom parentNode {
            get { return _parentNode; }
        }

        public Dom nextSibling {
            get { return _nextSibling; }
        }

        public DomStyle style => new DomStyle(this);

        public object value {
            get { return _value; }
        }

        public bool @checked {
            get { return _checked; }
        }

        public object data {
            get { return _data; }
            set {
                _data = value;
                if (_ve is TextElement) {
                    (_ve as TextElement).text = value.ToString();
                }
            }
        }

        public string innerHTML {
            get { return _innerHTML; }
        }

        public Vector2 layoutSize => _ve.layout.size;

        public object _children {
            get { return __children; }
            set { __children = value; }
        }

        public Dictionary<string, EventCallback<EventBase>> _listeners {
            get { return __listeners; }
            set { __listeners = value; }
        }

        Document _document;
        VisualElement _ve;
        Dom _parentNode;
        Dom _nextSibling;
        object _value;
        bool _checked;
        object _data;
        string _innerHTML;
        List<Dom> _childNodes = new List<Dom>();
        object __children;
        Dictionary<string, EventCallback<EventBase>> __listeners;

        Dictionary<string, EventCallback<EventBase>> _registeredCallbacks =
            new Dictionary<string, EventCallback<EventBase>>();

        public Dom(string tagName) {
            _ve = new VisualElement();
            // This constructor is called by preact?
            // Debug.Log("dom(string tagName) called");
        }

        public Dom(VisualElement ve, Document document) {
            _ve = ve;
            _document = document;
        }

        public void clearChildren() {
            _ve.Clear();
        }

        public void addEventListener(string name, JsValue jsval, bool useCapture = false) {
            var func = jsval.As<FunctionInstance>();
            var engine = _document.scriptEngine.JintEngine;
            var thisDom = JsValue.FromObject(engine, this);
            var callback = (EventCallback<EventBase>)((e) => { func.Call(thisDom, JsValue.FromObject(engine, e)); });
            var eventType = typeof(VisualElement).Assembly.GetType($"UnityEngine.UIElements.{name}Event");
            if (eventType != null) {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                var mi = _ve.GetType().GetMethods(flags)
                    .Where(m => m.Name == "RegisterCallback" && m.GetGenericArguments().Length == 1).First();
                mi = mi.MakeGenericMethod(eventType);
                mi.Invoke(_ve, new object[] { callback, null });
            }
            _registeredCallbacks.Add(name, callback);
        }

        public void removeEventListener(string name, JsValue jsval, bool useCapture = false) {
            var callback = _registeredCallbacks[name];
            var eventType = typeof(VisualElement).Assembly.GetType($"UnityEngine.UIElements.{name}Event");
            if (eventType != null) {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                var mi = _ve.GetType().GetMethods(flags)
                    .Where(m => m.Name == "UnregisterCallback" && m.GetGenericArguments().Length == 1).First();
                mi = mi.MakeGenericMethod(eventType);
                mi.Invoke(_ve, new object[] { callback, null });
            }
            _registeredCallbacks.Remove(name);
        }

        public void appendChild(Dom node) {
            // Debug.Log(
            //     $"{this._ve.GetType().Name} [{this._ve.childCount}] ({this._ve.GetHashCode()}) Adding {node.ve.GetType().Name} ({node.ve.GetHashCode()})");
            // if (node.ve.GetType().Name == "TextElement") {
            //     Debug.Log((node.ve as TextElement).text);
            // }
            this._ve.Add(node.ve);
            node._parentNode = this;
            if (_childNodes.Count > 0) {
                _childNodes[_childNodes.Count - 1]._nextSibling = node;
            }
            _childNodes.Add(node);
        }

        public void removeChild(Dom child) {
            if (!this._ve.Contains(child.ve))
                return;
            this._ve.Remove(child.ve);
            var index = _childNodes.IndexOf(child);
            if (index > 0) {
                var prev = _childNodes[index - 1];
                prev._nextSibling = child._nextSibling;
            }
            _childNodes.Remove(child);
        }

        public void insertBefore(Dom a, Dom b) {
            var index = _ve.IndexOf(b.ve);
            _ve.Insert(index, a.ve);
            _childNodes.Insert(index, a);
            a._nextSibling = b;
            if (index > 0) {
                _childNodes[index - 1]._nextSibling = a;
            }
        }

        public void setAttribute(string name, object val) {
            if (name == "class" || name == "className") {
                _ve.ClearClassList();
                var unprocessedClassStr = _document.scriptEngine.ProcessClassStr(val.ToString(), this);
                var parts = (unprocessedClassStr).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts) {
                    _ve.AddToClassList(part);
                }
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
                    } else if (pi.PropertyType.IsArray && val.GetType() == typeof(object[])) {
                        var objAry = (object[])val;
                        var length = ((object[])val).Length;
                        Array destinationArray = Array.CreateInstance(pi.PropertyType.GetElementType(), length);
                        Array.Copy(objAry, destinationArray, length);
                        val = destinationArray;
                    } else if (pi.PropertyType == typeof(Single) && val.GetType() == typeof(double)) {
                        val = Convert.ToSingle(val);
                    } else if (pi.PropertyType == typeof(Int32) && val.GetType() == typeof(double)) {
                        val = Convert.ToInt32(val);
                    }
                    pi.SetValue(_ve, val);
                }
            }
        }

        public void removeAttribute(string name) {
            if (name == "class" || name == "className") {
                _ve.ClearClassList();
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
    }
}