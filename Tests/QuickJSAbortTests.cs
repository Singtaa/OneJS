using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for the AbortController/AbortSignal polyfill. The install is
    /// if-missing so WebGL keeps the browser natives; these run on the QuickJS
    /// path where the bootstrap's implementation is the one in play.
    /// </summary>
    [TestFixture]
    public class QuickJSAbortTests {
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

            _go = new GameObject("AbortTestHost");
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
        public IEnumerator Globals_Exist() {
            Assert.AreEqual("function", _bridge.Eval("typeof AbortController"));
            Assert.AreEqual("function", _bridge.Eval("typeof AbortSignal"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FreshSignal_IsNotAborted() {
            var result = _bridge.Eval(
                "(() => { const c = new AbortController(); " +
                "return c.signal.aborted + ':' + typeof c.signal.reason })()");
            Assert.AreEqual("false:undefined", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Abort_SetsAbortedAndAbortErrorReason() {
            var result = _bridge.Eval(
                "(() => { const c = new AbortController(); c.abort(); " +
                "return c.signal.aborted + ':' + c.signal.reason.name })()");
            Assert.AreEqual("true:AbortError", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Abort_CarriesCustomReason() {
            var result = _bridge.Eval(
                "(() => { const c = new AbortController(); c.abort('because'); " +
                "return c.signal.reason })()");
            Assert.AreEqual("because", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Listeners_FireOnceAndRespectRemoval() {
            var result = _bridge.Eval(@"
(() => {
    const c = new AbortController()
    let fired = 0, onabortFired = 0, removedFired = 0
    const removed = () => { removedFired++ }
    c.signal.addEventListener('abort', (e) => { fired += (e.type === 'abort' ? 1 : 100) })
    c.signal.addEventListener('abort', removed)
    c.signal.removeEventListener('abort', removed)
    c.signal.onabort = () => { onabortFired++ }
    c.abort()
    c.abort()   // second abort must not re-fire anything
    return fired + ':' + onabortFired + ':' + removedFired
})()");
            Assert.AreEqual("1:1:0", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowIfAborted_ThrowsTheReason() {
            var result = _bridge.Eval(@"
(() => {
    const c = new AbortController()
    c.abort('stop')
    try { c.signal.throwIfAborted(); return 'no-throw' } catch (e) { return 'threw:' + e }
})()");
            Assert.AreEqual("threw:stop", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StaticAbort_ReturnsPreAbortedSignal() {
            var result = _bridge.Eval(
                "(() => { const s = AbortSignal.abort(); " +
                "return s.aborted + ':' + s.reason.name })()");
            Assert.AreEqual("true:AbortError", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StaticTimeout_AbortsThroughTheTimerQueue() {
            _bridge.Eval("globalThis.__abortTimeoutSig = AbortSignal.timeout(50)");
            Assert.AreEqual("false", _bridge.Eval("String(__abortTimeoutSig.aborted)"));

            for (int i = 0; i < 200 && _bridge.Eval("String(__abortTimeoutSig.aborted)") != "true"; i++) {
                _bridge.Tick();
                yield return null;
            }

            Assert.AreEqual("true", _bridge.Eval("String(__abortTimeoutSig.aborted)"));
            Assert.AreEqual("TimeoutError", _bridge.Eval("__abortTimeoutSig.reason.name"));
        }
    }
}
