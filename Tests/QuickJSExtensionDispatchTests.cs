using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for extension-method dispatch with omitted optional arguments.
    /// The canonical case is UQuery's Q: its usable overload declares
    /// (this, string name = null, string className = null), so a JS call like
    /// element.Q("name") only binds if the matcher accepts trailing defaults
    /// and skips the generic Q&lt;T&gt; twins.
    /// </summary>
    [TestFixture]
    public class QuickJSExtensionDispatchTests {
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

            _go = new GameObject("ExtensionDispatchTestHost");
            _uiDocument = _go.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;
            yield return null;

            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement);
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

        [UnityTest]
        public IEnumerator Q_DispatchesWithOmittedOptionals() {
            var result = _bridge.Eval(@"
(() => {
    useExtensions(CS.UnityEngine.UIElements.UQueryExtensions)
    const root = new CS.UnityEngine.UIElements.VisualElement()
    const child = new CS.UnityEngine.UIElements.Label()
    child.name = 'probe'
    child.AddToClassList('probe-class')
    const grand = new CS.UnityEngine.UIElements.Label()
    grand.name = 'deep'
    root.Add(child)
    child.Add(grand)
    const byName = root.Q('probe')          // one arg: className omitted
    const nested = root.Q('deep')           // descends past the first level
    const byClass = root.Q(null, 'probe-class')  // two args: exact arity
    return (byName ? byName.name : 'null') + ':' +
           (nested ? nested.name : 'null') + ':' +
           (byClass ? byClass.name : 'null')
})()");
            Assert.AreEqual("probe:deep:probe", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Q_ReturnsNullForNoMatchInsteadOfThrowing() {
            var result = _bridge.Eval(@"
(() => {
    useExtensions(CS.UnityEngine.UIElements.UQueryExtensions)
    const root = new CS.UnityEngine.UIElements.VisualElement()
    const miss = root.Q('missing')
    return miss === null || miss === undefined ? 'null' : 'found'
})()");
            Assert.AreEqual("null", result);
            yield return null;
        }
    }
}
