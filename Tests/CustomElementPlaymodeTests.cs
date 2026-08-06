using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Custom VisualElement for testing registerElement/createComponent.
    /// Properties use lowercase to match JS convention (the CS proxy
    /// resolves properties case-sensitively via reflection).
    /// </summary>
    public class TestCustomProgressBar : VisualElement {
        public float progress { get; set; }
        public string trackColor { get; set; }
    }

    /// <summary>
    /// End-to-end PlayMode tests for custom element registration in the React reconciler.
    /// Tests the full flow: C# VisualElement → registerElement() → createComponent() → render() → verify.
    ///
    /// Requires pre-built test fixture at Resources/TestCustomElement.
    /// To rebuild: cd Tests/Fixtures/CustomElement~ && npm install && npm run build
    /// </summary>
    [TestFixture]
    public class CustomElementPlaymodeTests {
        GameObject _go;
        UIDocument _uiDocument;
        PanelSettings _panelSettings;
        QuickJSUIBridge _bridge;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet =
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

            _go = new GameObject("CustomElementTestHost");
            _uiDocument = _go.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;

            yield return null;

            var root = _uiDocument.rootVisualElement;
            _bridge = new QuickJSUIBridge(root);

            // Expose __root (same pattern as JSRunner/JSPad)
            var rootHandle = QuickJSNative.RegisterObject(root);
            _bridge.Eval(
                $"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");

            // Load and eval the pre-built React test fixture
            var fixture = Resources.Load<TextAsset>("TestCustomElement");
            Assert.IsNotNull(fixture,
                "TestCustomElement fixture not found. " +
                "Rebuild: cd Tests/Fixtures/CustomElement~ && npm install && npm run build");
            _bridge.Eval(fixture.text);
            FlushReact();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;

            if (_go != null) Object.Destroy(_go);
            if (_panelSettings != null) Object.Destroy(_panelSettings);

            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        /// <summary>
        /// Flush React's microtask queue and tick the bridge to process
        /// RAF callbacks, timers, and any resulting microtasks.
        /// </summary>
        void FlushReact() {
            _bridge.Context.ExecutePendingJobs();
            _bridge.Tick();
            _bridge.Context.ExecutePendingJobs();
        }

        // MARK: Registration Tests

        [UnityTest]
        public IEnumerator Registration_CustomElement_CanBeCreated() {
            _bridge.Eval("__test_basicRendering()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;

            // React renders: root > View > TestCustomProgressBar
            Assert.AreEqual(1, root.childCount, "Root should have 1 child (View)");

            var view = root[0];
            Assert.AreEqual(1, view.childCount, "View should have 1 child (TestCustomProgressBar)");

            var customEl = view[0] as TestCustomProgressBar;
            Assert.IsNotNull(customEl, "Child should be a TestCustomProgressBar instance");
        }

        [UnityTest]
        public IEnumerator Registration_CustomProps_AreForwarded() {
            _bridge.Eval("__test_basicRendering()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var customEl = root[0][0] as TestCustomProgressBar;
            Assert.IsNotNull(customEl);

            Assert.AreEqual(0.75f, customEl.progress, 0.001f,
                "progress prop should be forwarded to C# element");
            Assert.AreEqual("#ff0000", customEl.trackColor,
                "trackColor prop should be forwarded to C# element");
        }

        [UnityTest]
        public IEnumerator Registration_ClassName_AppliedToCustomElement() {
            _bridge.Eval("__test_basicRendering()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var customEl = root[0][0] as TestCustomProgressBar;
            Assert.IsNotNull(customEl);

            Assert.IsTrue(customEl.ClassListContains("custom-test"),
                "className should be applied to custom element");
        }

        // MARK: Prop Update Tests

        [UnityTest]
        public IEnumerator PropUpdate_CustomProps_UpdateOnStateChange() {
            _bridge.Eval("__test_propUpdates()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var customEl = root[0][0] as TestCustomProgressBar;
            Assert.IsNotNull(customEl);
            Assert.AreEqual(0.25f, customEl.progress, 0.001f,
                "Initial progress should be 0.25");

            // Trigger prop update via React state change
            _bridge.Eval("__setProgress(0.9)");
            FlushReact();
            yield return null;

            Assert.AreEqual(0.9f, customEl.progress, 0.001f,
                "progress should update to 0.9 after React state change");
        }

        // MARK: Event Tests

        [UnityTest]
        public IEnumerator Events_ClickHandler_WorksOnCustomElement() {
            _bridge.Eval("__test_events()");
            FlushReact();
            yield return null;

            // Dispatch click event via the JS event system
            var handle = _bridge.Eval("__root.ElementAt(0).ElementAt(0).__csHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{ x: 10, y: 10 }})");

            var clickCount = _bridge.Eval("globalThis.__clickCount");
            Assert.AreEqual("1", clickCount,
                "Click handler should fire on custom element");
        }

        // MARK: Ref Tests

        [UnityTest]
        public IEnumerator Ref_ForwardsToCustomElement() {
            _bridge.Eval("__test_refForwarding()");
            FlushReact();
            yield return null;

            var typeName = _bridge.Eval("globalThis.__refTypeName");
            Assert.AreEqual("TestCustomProgressBar", typeName,
                "Ref should point to TestCustomProgressBar C# type");

            var handle = _bridge.Eval("globalThis.__refHandle");
            Assert.AreNotEqual("-1", handle, "Ref handle should be valid");
            Assert.AreNotEqual("0", handle, "Ref handle should be non-zero");
        }

        // MARK: Hierarchy Tests

        [UnityTest]
        public IEnumerator Hierarchy_MultipleCustomElements_AllCreated() {
            _bridge.Eval("__test_multipleElements()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var view = root[0];
            Assert.AreEqual(3, view.childCount, "View should have 3 custom element children");

            for (int i = 0; i < 3; i++) {
                var el = view[i] as TestCustomProgressBar;
                Assert.IsNotNull(el, $"Child {i} should be a TestCustomProgressBar");
            }

            // Verify individual props forwarded correctly
            var el0 = view[0] as TestCustomProgressBar;
            var el1 = view[1] as TestCustomProgressBar;
            var el2 = view[2] as TestCustomProgressBar;

            Assert.AreEqual(0.1f, el0.progress, 0.001f);
            Assert.AreEqual(0.5f, el1.progress, 0.001f);
            Assert.AreEqual(0.9f, el2.progress, 0.001f);
            Assert.AreEqual("#aaa", el0.trackColor);
            Assert.AreEqual("#bbb", el1.trackColor);
            Assert.AreEqual("#ccc", el2.trackColor);
        }

        [UnityTest]
        public IEnumerator Hierarchy_MixedBuiltInAndCustom_CorrectOrder() {
            _bridge.Eval("__test_mixedElements()");
            FlushReact();
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var view = root[0];
            Assert.AreEqual(3, view.childCount, "View should have 3 children");

            // Label creates TextElement for inline text children
            Assert.IsInstanceOf<TextElement>(view[0],
                "First child should be a TextElement (from Label's text)");
            Assert.IsInstanceOf<TestCustomProgressBar>(view[1],
                "Second child should be TestCustomProgressBar");
            Assert.IsInstanceOf<TextElement>(view[2],
                "Third child should be a TextElement (from Label's text)");

            var customEl = view[1] as TestCustomProgressBar;
            Assert.AreEqual(0.5f, customEl.progress, 0.001f);
            Assert.AreEqual("#123", customEl.trackColor);
        }
    }
}
