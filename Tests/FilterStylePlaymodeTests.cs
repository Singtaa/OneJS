using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OneJS.CustomStyleSheets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// USS filter functions (filter from Unity 6.3, backdrop-filter from 6.6) on a
    /// real panel, through both of OneJS's style paths: a sheet the runtime
    /// UssCompiler built (.uss files, CSS modules and Tailwind all compile through
    /// it), and an inline style set by StyleBridge (a JSX style prop). Each must
    /// arrive as the filter functions Unity resolves, not be dropped or throw.
    /// </summary>
    public class FilterStylePlaymodeTests {
        GameObject _go;
        PanelSettings _panelSettings;
        StyleSheet _sheet;
        VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _go = new GameObject("FilterStyleHost");
            var doc = _go.AddComponent<UIDocument>();
            doc.panelSettings = _panelSettings;
            yield return null;
            _root = doc.rootVisualElement;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            Object.Destroy(_go);
            Object.Destroy(_panelSettings);
            if (_sheet != null) Object.Destroy(_sheet);
            yield return null;
        }

        static string Describe(IEnumerable<FilterFunction> filters) =>
            string.Join(" ", filters.Select(f =>
                f.type + "(" + string.Join(",", Enumerable.Range(0, f.parameterCount).Select(i => {
                    var p = f.GetParameter(i);
                    return p.type == FilterParameterType.Color ? "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue) : p.floatValue.ToString("0.###");
                })) + ")"));

        IEnumerator Resolve(VisualElement el) {
            el.style.width = 50;
            el.style.height = 50;
            _root.Add(el);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Sheet_FilterFunctions_Resolve() {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var compiler = new UssCompiler();
            compiler.Compile(_sheet, ".f { filter: blur(4px) grayscale(0.5) hue-rotate(90deg); }");
            Assert.IsEmpty(compiler.Diagnostics, string.Join("\n", compiler.Diagnostics));
            _root.styleSheets.Add(_sheet);

            var el = new VisualElement();
            el.AddToClassList("f");
            yield return Resolve(el);
            Assert.AreEqual("Blur(4) Grayscale(0.5) HueRotate(90)", Describe(el.resolvedStyle.filter));
        }

        [UnityTest]
        public IEnumerator Inline_FilterString_Resolves() {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "blur(4px) grayscale(50%)" } });
            yield return Resolve(el);
            Assert.AreEqual("Blur(4) Grayscale(0.5)", Describe(el.resolvedStyle.filter));
        }

        [UnityTest]
        public IEnumerator Inline_FilterNone_ClearsIt() {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "blur(4px)" } });
            yield return Resolve(el);
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "none" } });
            yield return null;
            yield return null;
            Assert.AreEqual("", Describe(el.resolvedStyle.filter));
        }
    }
}
