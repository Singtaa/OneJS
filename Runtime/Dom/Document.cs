using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public Dom body => _body;

        Dom _body;
        VisualElement _root;
        ScriptEngine _scriptEngine;
        List<StyleSheet> _runtimeStyleSheets = new List<StyleSheet>();

        public Document(VisualElement root, ScriptEngine scriptEngine) {
            _root = root;
            _body = new Dom(_root, this);
            _scriptEngine = scriptEngine;
        }

        public void addRuntimeCSS(string css) {
            var ss = ScriptableObject.CreateInstance<StyleSheet>();
            var builder = new OneJS.CustomStyleSheets.CustomStyleSheetImporterImpl();
            builder.BuildStyleSheet(ss, css);
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
        }

        public Dom createElement(string tagName) {
            var typesToSearch = typeof(VisualElement).Assembly.GetTypes().Concat(typeof(Document).Assembly.GetTypes());
            var type = typesToSearch.Where(t => t.Name.ToLower() == tagName.ToLower())
                .FirstOrDefault();
            if (type != null && type.IsSubclassOf(typeof(VisualElement))) {
                return new Dom(Activator.CreateInstance(type) as VisualElement, this);
            }
            return new Dom(new VisualElement(), this);
        }

        public Dom createElement(string tagName, ElementCreationOptions options) {
            return createElement(tagName);
        }

        public Dom createTextNode(string text) {
            var tn = new TextElement();
            tn.text = text;
            return new Dom(tn, this);
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
    }
}