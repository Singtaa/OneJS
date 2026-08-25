using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// End to end cover for console severity: real JS through the real
    /// bootstrap and the real native callback, asserted at the Unity log level
    /// it lands on. The EditMode tests cover the splitting in isolation; these
    /// are what prove the bootstrap and the C# side agree on the encoding, which
    /// is the part that would silently rot if either changed alone.
    /// </summary>
    [TestFixture]
    public class JsLogSeverityPlaymodeTests {
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

            _go = new GameObject("JsLogSeverityTestHost");
            _uiDocument = _go.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;
            yield return null;

            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement);
            yield return null;

            JsLog.ResetErrorCount();
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
        public IEnumerator ConsoleError_ArrivesAsAUnityError() {
            LogAssert.Expect(LogType.Error, "[QuickJS] boom");
            _bridge.Eval("console.error('boom')");
            yield return null;

            Assert.AreEqual(1, JsLog.ErrorCount);
            Assert.AreEqual("boom", JsLog.LastError);
        }

        [UnityTest]
        public IEnumerator ConsoleWarn_ArrivesAsAUnityWarning_AndIsNotAnError() {
            LogAssert.Expect(LogType.Warning, "[QuickJS] careful");
            _bridge.Eval("console.warn('careful')");
            yield return null;

            Assert.AreEqual(0, JsLog.ErrorCount);
        }

        [UnityTest]
        public IEnumerator ConsoleLog_StaysAtLogLevel() {
            LogAssert.Expect(LogType.Log, "[QuickJS] hello");
            _bridge.Eval("console.log('hello')");
            yield return null;

            Assert.AreEqual(0, JsLog.ErrorCount);
        }

        // The native console called C# once per argument, so this used to arrive
        // as two entries and only the first could have carried a level.
        [UnityTest]
        public IEnumerator MultipleArguments_ArriveAsOneLine() {
            LogAssert.Expect(LogType.Error, "[QuickJS] failed: 42");
            _bridge.Eval("console.error('failed:', 42)");
            yield return null;

            Assert.AreEqual(1, JsLog.ErrorCount);
        }

        // The reason this change exists: a handler that throws is the framework's
        // most common runtime failure, and it used to be logged as information.
        [UnityTest]
        public IEnumerator AThrowingCallbackIsReportedAsAnError() {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("kaboom"));
            _bridge.Eval("setTimeout(function () { throw new Error('kaboom') }, 0)");
            yield return null;
            _bridge.Tick();
            yield return null;
            _bridge.Tick();
            yield return null;

            Assert.GreaterOrEqual(JsLog.ErrorCount, 1, "a throwing timer callback should count as a JS error");
        }
    }
}
