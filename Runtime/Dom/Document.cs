using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        Dictionary<VisualElement, Dom> _elementToDomLookup = new();

        Dictionary<string, Type> _tagCache = new();
        Dictionary<string, Type> _allUIElementEventTypes = new();
        Type[] _tagTypes;

        public Document(VisualElement root, ScriptEngine scriptEngine) {
            _root = root;
            _body = new Dom(_root, this);
            _scriptEngine = scriptEngine;
            _tagTypes = GetAllVisualElementTypes();
            InitAllUIElementEvents();
        }

        void InitAllUIElementEvents() {
            var eventTypes = typeof(VisualElement).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(EventBase)));
            foreach (var type in eventTypes) {
                var typeNameLower = type.Name.ToLower();
                _allUIElementEventTypes.Add(typeNameLower, type);

                if (type.Name.EndsWith("Event")) {
                    // Strips 5 characters from the end of type name, which is "Event"
                    _allUIElementEventTypes.Add(typeNameLower[..^5], type);
                }
            }
        }

        public Type FindUIElementEventType(string name) {
            if (_allUIElementEventTypes.TryGetValue(name, out var type)) {
                return type;
            }
            return null;
        }

        public void addRuntimeUSS(string uss) {
            var ss = ScriptableObject.CreateInstance<StyleSheet>();
            var builder = new OneJS.CustomStyleSheets.CustomStyleSheetImporterImpl(_scriptEngine);
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
                type = GetVisualElementType(tagName);
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

        public Dom createElementNS(string ns, string tagName, ElementCreationOptions options) { // namespace currently not used
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
            var doms = elems
                .Select(elem => _elementToDomLookup.TryGetValue(elem, out var dom) ? dom : null)
                .Where(dom => dom != null)
                .ToArray();
            return doms;
        }

        public Texture2D loadImage(string path, FilterMode filterMode = FilterMode.Bilinear) {
            // TODO cache
            try {
                path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
                var rawData = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2); // Create an empty Texture; size doesn't matter
                tex.LoadImage(rawData);
                tex.filterMode = filterMode;
                return tex;
            } catch (Exception e) {
                Debug.LogError($"Failed to load image: {path}");
                // Debug.LogError(e);
                return null;
            }
        }

        public Font loadFont(string path) {
            // TODO cache
            try {
            path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
            var font = new Font(path);
            return font;
            } catch (Exception e) {
                Debug.LogError($"Failed to load font: {path}");
                // Debug.LogError(e);
                return null;
            }
        }

        public FontDefinition loadFontDefinition(string path) {
            // TODO cache
            path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
            var font = new Font(path);
            return FontDefinition.FromFont(font);
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

        // public void addEventListener(string name, JsValue jsval, bool useCapture = false) {
        //     _body.addEventListener(name, jsval, useCapture);
        // }
        //
        // public void removeEventListener(string name, JsValue jsval, bool useCapture = false) {
        //     _body.removeEventListener(name, jsval, useCapture);
        // }

        internal void AddCachingDom(Dom dom) {
            _elementToDomLookup[dom.ve] = dom;
        }

        internal void RemoveCachingDom(Dom dom) {
            _elementToDomLookup.Remove(dom.ve);
        }

        Type[] GetAllVisualElementTypes() {
            List<Type> visualElementTypes = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies) {
                Type[] types = null;

                try {
                    types = assembly.GetTypes();
                } catch (ReflectionTypeLoadException ex) {
                    types = ex.Types.Where(t => t != null).ToArray(); // Handle partially loaded types
                }

                foreach (Type type in types) {
                    if (type.IsSubclassOf(typeof(VisualElement))) {
                        visualElementTypes.Add(type);
                    }
                }
            }

            return visualElementTypes.ToArray();
        }

        Type GetVisualElementType(string tagName) {
            Type foundType = null;
            var typeNameL = tagName.ToLower();
            foreach (var tagType in _tagTypes) {
                if (tagType.Name.ToLower() == typeNameL) {
                    foundType = tagType;
                    break;
                }
            }
            return foundType;
        }
    }
}