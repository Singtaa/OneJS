using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// How a CS path becomes a type, and what happens when it does not.
    ///
    /// ScrollView's TouchScrollBehavior and NestedInteractionKind are nested
    /// types. JS reaches them by the dotted path it uses for everything else,
    /// which the runtime once tried verbatim and nothing else, so the path
    /// resolved to nothing and every member of it read as 0. The static call
    /// case is UQueryExtensions.Q through the class, where a generic twin with
    /// the same parameters was picked and refused at Invoke.
    /// </summary>
    [TestFixture]
    public class QuickJSTypeResolutionTests {
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

            _go = new GameObject("TypeResolutionTestHost");
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
        public IEnumerator NestedEnum_ReadsByDottedPathAndByClrName() {
            var result = _bridge.Eval(@"
(() => {
    const UIE = CS.UnityEngine.UIElements
    return Number(UIE.ScrollView.TouchScrollBehavior.Elastic) + ':' +
           Number(UIE.ScrollView.NestedInteractionKind.ForwardScrolling) + ':' +
           Number(UIE['ScrollView+TouchScrollBehavior'].Clamped)
})()");
            Assert.AreEqual("1:2:2", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NestedEnum_AssignsThroughProperty() {
            var result = _bridge.Eval(@"
(() => {
    const sv = new CS.UnityEngine.UIElements.ScrollView()
    sv.touchScrollBehavior = CS.UnityEngine.UIElements.ScrollView.TouchScrollBehavior.Elastic
    return String(sv.touchScrollBehavior)
})()");
            Assert.AreEqual(((int)ScrollView.TouchScrollBehavior.Elastic).ToString(), result);
            yield return null;
        }

        /// <summary>
        /// The namespace path names no type. It used to set the enum's zero
        /// with nothing in the console; now the assignment says what went
        /// wrong, and the value still lands as the zero it always did.
        /// </summary>
        [UnityTest]
        public IEnumerator WrongPathAssignedToEnum_Warns() {
            LogAssert.Expect(LogType.Warning, new Regex("CS\\.UnityEngine\\.UIElements\\.TouchScrollBehavior\\.Elastic is not a C# type"));
            _bridge.Eval(@"
(() => {
    const sv = new CS.UnityEngine.UIElements.ScrollView()
    sv.touchScrollBehavior = CS.UnityEngine.UIElements.TouchScrollBehavior.Elastic
})()");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WrongPathReadAsNumber_Warns() {
            LogAssert.Expect(LogType.Warning, new Regex("CS\\.UnityEngine\\.UIElements\\.NestedInteractionKind\\.Default is not a C# enum member"));
            var result = _bridge.Eval("Number(CS.UnityEngine.UIElements.NestedInteractionKind.Default)");
            Assert.AreEqual("0", result);
            yield return null;
        }

        /// <summary>
        /// UQueryExtensions declares Q&lt;T&gt;(e, name, className) beside
        /// Q(e, name, className). Called through the static class, the matcher
        /// must skip the generic one: MethodInfo.Invoke refuses an open generic
        /// method, and on Unity 6000.5 it was the first one enumerated.
        /// </summary>
        [UnityTest]
        public IEnumerator StaticClassQ_SkipsGenericTwin() {
            var result = _bridge.Eval(@"
(() => {
    const root = new CS.UnityEngine.UIElements.VisualElement()
    const child = new CS.UnityEngine.UIElements.Label()
    child.name = 'probe'
    child.AddToClassList('probe-class')
    root.Add(child)
    const found = CS.UnityEngine.UIElements.UQueryExtensions.Q(root, null, 'probe-class')
    return found ? found.name : 'null'
})()");
            Assert.AreEqual("probe", result);
            yield return null;
        }
    }
}
