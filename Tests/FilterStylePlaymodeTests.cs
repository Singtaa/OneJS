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
            compiler.Compile(_sheet, ".f { filter: blur(4px) grayscale(0.5); }");
            Assert.IsEmpty(compiler.Diagnostics, string.Join("\n", compiler.Diagnostics));
            _root.styleSheets.Add(_sheet);

            var el = new VisualElement();
            el.AddToClassList("f");
            yield return Resolve(el);
            Assert.AreEqual("Blur(4) Grayscale(0.5)", Describe(el.resolvedStyle.filter));
        }

        // Every value a sheet and an inline style can both say, through both: the
        // inline parser (UssFilter) must give what Unity's own reader gives a sheet.
        static readonly string[] Shared = {
            "blur(4px)", "blur(2.5px) grayscale(50%)", "hue-rotate(90deg)", "hue-rotate(0.25turn)", "hue-rotate(1.5rad)",
            "opacity(0.3) invert(1)", "sepia(1) contrast(150%)", "tint(red)", "tint(#00ff0080)", "tint(rgba(255, 0, 0, 0.5))",
#if UNITY_6000_6_OR_NEWER
            "drop-shadow(2px 3px 4px rgba(0, 0, 0, 0.5))", "drop-shadow(1px 1px 0px #ff0000) blur(1px)",
#endif
        };

        [UnityTest]
        public IEnumerator Inline_GivesWhatASheetGives([ValueSource(nameof(Shared))] string value) {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var compiler = new UssCompiler();
            compiler.Compile(_sheet, ".f { filter: " + value + "; }");
            Assert.IsEmpty(compiler.Diagnostics, string.Join("\n", compiler.Diagnostics));
            _root.styleSheets.Add(_sheet);
            var fromSheet = new VisualElement();
            fromSheet.AddToClassList("f");
            var inline = new VisualElement();
            StyleBridge.ApplyStyles(inline, new Dictionary<string, object> { { "filter", value } });
            yield return Resolve(fromSheet);
            yield return Resolve(inline);
            var expected = Describe(fromSheet.resolvedStyle.filter);
            Assert.IsNotEmpty(expected, "the sheet resolved no filter");
            Assert.AreEqual(expected, Describe(inline.resolvedStyle.filter));
        }

#if !UNITY_6000_6_OR_NEWER
        [Test]
        public void Sheet_FunctionThisUnityLacks_IsDiagnosed() {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var compiler = new UssCompiler();
            compiler.Compile(_sheet, ".f { filter: drop-shadow(1px 1px 2px red); }");
            Assert.AreEqual(1, compiler.Diagnostics.Count);
            StringAssert.Contains("drop-shadow", compiler.Diagnostics[0].Message);
        }
#endif

#if UNITY_6000_6_OR_NEWER
        [UnityTest]
        public IEnumerator BackdropFilter_FromSheetAndInline_Resolve() {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var compiler = new UssCompiler();
            compiler.Compile(_sheet, ".f { backdrop-filter: blur(6px); }");
            Assert.IsEmpty(compiler.Diagnostics, string.Join("\n", compiler.Diagnostics));
            _root.styleSheets.Add(_sheet);
            var fromSheet = new VisualElement();
            fromSheet.AddToClassList("f");
            var inline = new VisualElement();
            StyleBridge.ApplyStyles(inline, new Dictionary<string, object> { { "backdropFilter", "blur(6px)" } });
            yield return Resolve(fromSheet);
            yield return Resolve(inline);
            Assert.AreEqual("Blur(6)", Describe(fromSheet.resolvedStyle.backdropFilter));
            Assert.AreEqual("Blur(6)", Describe(inline.resolvedStyle.backdropFilter));
        }
#endif

        [UnityTest]
        public IEnumerator Inline_FilterString_Resolves() {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "blur(4px) grayscale(50%)" } });
            yield return Resolve(el);
            Assert.AreEqual("Blur(4) Grayscale(0.5)", Describe(el.resolvedStyle.filter));
        }

        // React dropping filter from a style sends it as null (host-config's
        // clearRemovedStyles), which clears the inline filter: the sheet's shows.
        [UnityTest]
        public IEnumerator Inline_FilterRemoved_FallsBackToTheSheet() {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssCompiler().Compile(_sheet, ".f { filter: grayscale(0.5); }");
            _root.styleSheets.Add(_sheet);
            var el = new VisualElement();
            el.AddToClassList("f");
            var plain = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "blur(4px)" } });
            StyleBridge.ApplyStyles(plain, new Dictionary<string, object> { { "filter", "blur(4px)" } });
            yield return Resolve(el);
            yield return Resolve(plain);
            Assert.AreEqual("Blur(4)", Describe(el.resolvedStyle.filter), "precondition: the inline filter beats the sheet's");
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", null } });
            StyleBridge.ApplyStyles(plain, new Dictionary<string, object> { { "filter", null } });
            yield return null;
            yield return null;
            Assert.AreEqual("Grayscale(0.5)", Describe(el.resolvedStyle.filter));
            Assert.AreEqual("", Describe(plain.resolvedStyle.filter));
        }

        // The same move through a sheet: a class whose rule says none replaces one
        // whose rule has functions. UssCompiler writes none as Unity's importer does.
        [UnityTest]
        public IEnumerator Sheet_FunctionsToNone_ClearsIt() {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var compiler = new UssCompiler();
            compiler.Compile(_sheet, ".f { filter: blur(4px); } .f.n { filter: none; }");
            Assert.IsEmpty(compiler.Diagnostics, string.Join("\n", compiler.Diagnostics));
            _root.styleSheets.Add(_sheet);
            var el = new VisualElement();
            el.AddToClassList("f");
            yield return Resolve(el);
            Assert.AreEqual("Blur(4)", Describe(el.resolvedStyle.filter), "precondition");
            el.AddToClassList("n");
            yield return null;
            yield return null;
            Assert.AreEqual("", Describe(el.resolvedStyle.filter));
        }

        // From functions back to none: Unity 6.3 threw on the keyword form of this.
        [UnityTest]
        public IEnumerator Inline_FilterNone_ClearsIt([Values("none", "initial")] string clear) {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", "blur(4px)" } });
            yield return Resolve(el);
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", clear } });
            yield return null;
            yield return null;
            Assert.AreEqual("", Describe(el.resolvedStyle.filter));
        }
    }
}
