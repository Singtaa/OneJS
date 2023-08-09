using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint.Native;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Dom {
    public class ElementCreationOptions {
        public string @is;
    }

    public class Document {
        public ScriptEngine scriptEngine => _scriptEngine;
        public VisualElement Root { get { return _root; } }
        public Dom body => _body;
        public Dictionary<string, Type> UIElementEventTypesDict => _allUIElementEventTypes;

        Dom _body;
        VisualElement _root;
        ScriptEngine _scriptEngine;
        List<StyleSheet> _runtimeStyleSheets = new List<StyleSheet>();

        Dictionary<string, Type> _tagCache = new Dictionary<string, System.Type>();
        Dictionary<string, Type> _allUIElementEventTypes = new Dictionary<string, Type>();

        public Document(VisualElement root, ScriptEngine scriptEngine) {
            _root = root;
            _body = new Dom(_root, this);
            _scriptEngine = scriptEngine;
            _scriptEngine.OnPostInit += InitAllUIElementEvents;
        }

        void InitAllUIElementEvents() {
            List<Type> typesInheritingFromEventBase = new List<Type>();

            foreach (Assembly assembly in _scriptEngine.LoadedAssemblies) {
                foreach (Type type in assembly.GetTypes()) {
                    if (type.IsSubclassOf(typeof(EventBase))) {
                        _allUIElementEventTypes.Add(type.Name, type);
                    }
                }
            }
            _scriptEngine.OnPostInit -= InitAllUIElementEvents;
        }

        public Type FindUIElementEventType(string name) {
            Type type;
            if (_allUIElementEventTypes.TryGetValue(name, out type)) {
                return type;
            } else if (_allUIElementEventTypes.TryGetValue(name + "Event", out type)) {
                return type;
            }
            return type;
        }

        public void addRuntimeUSS(string uss) {
            var ss = ScriptableObject.CreateInstance<StyleSheet>();
            var builder = new OneJS.CustomStyleSheets.CustomStyleSheetImporterImpl();
            builder.BuildStyleSheet(ss, uss);
            if (builder.importErrors.hasErrors) {
                Debug.LogError($"Runtime USS Error(s)");
                foreach (var error in builder.importErrors) {
                    Debug.LogError(error);
                }
                return;
            }
            _runtimeStyleSheets.Add(ss);
            _root.styleSheets.Add(ss);
        }

        public void removeRuntimeStyleSheet(StyleSheet sheet) {
            _root.styleSheets.Remove(sheet);
            Object.Destroy(sheet);
        }

        public void clearRuntimeStyleSheets() {
            foreach (var sheet in _runtimeStyleSheets) {
                _root.styleSheets.Remove(sheet);
                Object.Destroy(sheet);
            }
            _runtimeStyleSheets.Clear();
        }

        public Dom createElement(string tagName) {
            Type type;
            // Try to lookup from tagCache, may still be null if not a VE type.
            if (!_tagCache.TryGetValue(tagName, out type)) {
                type = _scriptEngine.FindVisualElementType(tagName);
                _tagCache[tagName] = type;
            }

            if (type == null) {
                return new Dom(new VisualElement(), this);
            }
            return new Dom(Activator.CreateInstance(type) as VisualElement, this);
        }

        public Dom createElement(string tagName, ElementCreationOptions options) {
            return createElement(tagName);
        }

        public Dom createTextNode(string text) {
            var tn = new TextElement();
            tn.text = text;
            return new Dom(tn, this);
        }

        /// <summary>
        /// finds and returns an Element object representing the element whose id property matches the specified string.
        /// </summary>
        /// <param name="id">Element ID</param>
        /// <returns>Dom element or null if not found</returns>
        public Dom getElementById(string id) {
            //var firstElement = _root.Q<VisualElement>(id);
            var elem = body.First((d) => d.ve.name == id);
            return elem;
        }

        public Dom[] querySelectorAll(string selector) {
            var elems = _root.Query<VisualElement>(selector).Build();
            // TODO new Dom shouldn't be used here, we need to be able to get existing Dom of the VisualElement
            return elems.Select((e) => new Dom(e, this)).ToArray();
        }

        public static object createStyleEnum(int v, Type type) {
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

        public static object createStyleList(object v, Type type) {
            Type listType = typeof(List<>).MakeGenericType(type);
            Type myParameterizedSomeClass = typeof(StyleList<>).MakeGenericType(type);
            ConstructorInfo constr = myParameterizedSomeClass.GetConstructor(new[] { listType });
            object instance = constr.Invoke(new object[] { v });
            return instance;
        }

        public static object createStyleListWithKeyword(StyleKeyword keyword, Type type) {
            Type listType = typeof(List<>).MakeGenericType(type);
            Type myParameterizedSomeClass = typeof(StyleList<>).MakeGenericType(listType);
            ConstructorInfo constr = myParameterizedSomeClass.GetConstructor(new[] { typeof(StyleKeyword) });
            object instance = constr.Invoke(new object[] { keyword });
            return instance;
        }

        public void addEventListener(string name, JsValue jsval, bool useCapture = false) {
            _body.addEventListener(name, jsval, useCapture);
        }

        public void removeEventListener(string name, JsValue jsval, bool useCapture = false) {
            _body.removeEventListener(name, jsval, useCapture);
        }
    }
}