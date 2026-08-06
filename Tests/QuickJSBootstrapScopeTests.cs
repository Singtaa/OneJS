using NUnit.Framework;
using UnityEngine;

namespace OneJS.Tests {
    /// <summary>
    /// Guards the bootstrap's global-scope contract. On WebGL the bootstrap is
    /// evaluated by indirect eval in the embedding page's global scope, so two
    /// rules must hold (see Plugins/WebGL/README.md, gotcha 5):
    /// 1. Polyfills are install-if-missing - a pre-existing global (a browser
    ///    native on WebGL) must never be replaced.
    /// 2. Top-level declarations stay inside the bootstrap IIFE - only explicit
    ///    globalThis.* exports may appear on the global object (a leaked
    ///    top-level addEventListener would shadow the host page's EventTarget
    ///    method).
    /// QuickJS mirrors both mechanisms closely enough to pin them here without a
    /// browser: sentinels planted before a re-eval play the role of natives.
    /// </summary>
    [TestFixture]
    public class QuickJSBootstrapScopeTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
        }

        static string LoadBootstrapText() {
            var asset = Resources.Load<TextAsset>("OneJS/QuickJSBootstrap.js");
            Assert.IsNotNull(asset, "Bootstrap TextAsset not found in Resources");
            return asset.text;
        }

        [Test]
        public void Bootstrap_CanBeReEvaluated_WithoutError() {
            // Pre-IIFE this threw on top-level const redeclaration. Re-eval being
            // legal is what lets the sentinel tests below simulate a host page.
            Assert.DoesNotThrow(() => _ctx.Eval(LoadBootstrapText(), "bootstrap_reeval.js"));
        }

        [Test]
        public void Polyfills_DoNotReplace_PreexistingGlobals() {
            // Plant sentinels in the polyfill slots (standing in for browser
            // natives), then re-evaluate the bootstrap. Every guard must leave
            // the sentinel in place.
            _ctx.Eval(@"
                globalThis.URL = 'sURL';
                globalThis.URLSearchParams = 'sUSP';
                globalThis.btoa = 'sBtoa';
                globalThis.atob = 'sAtob';
                globalThis.queueMicrotask = 'sQM';
                globalThis.localStorage = 'sLS';
                globalThis.sessionStorage = 'sSS';
                globalThis.Headers = 'sH';
                globalThis.Response = 'sR';
            ");
            _ctx.Eval(LoadBootstrapText(), "bootstrap_reeval.js");
            var survivors = _ctx.Eval(
                "[globalThis.URL, globalThis.URLSearchParams, globalThis.btoa, globalThis.atob, " +
                "globalThis.queueMicrotask, globalThis.localStorage, globalThis.sessionStorage, " +
                "globalThis.Headers, globalThis.Response].join(',')");
            Assert.AreEqual("sURL,sUSP,sBtoa,sAtob,sQM,sLS,sSS,sH,sR", survivors,
                "A bootstrap polyfill replaced a pre-existing global (would clobber a browser native on WebGL)");
        }

        [Test]
        public void TopLevelDeclarations_DoNotLeak_ToGlobalObject() {
            var leaked = _ctx.Eval(
                "['newObject','callMethod','callStatic','addEventListener','removeEventListener'," +
                "'removeAllEventListeners','__base64Chars','__cleanupHandle','__flushMicrotasks'," +
                "'__resolveValue','__wrapObject']" +
                ".filter(function(n) { return n in globalThis; }).join(',')");
            Assert.AreEqual("", leaked,
                "Bootstrap internals leaked onto the global object (would land on the host page's window on WebGL)");
        }

        [Test]
        public void PublicSurface_IsExported() {
            var missing = _ctx.Eval(
                "['__eventAPI','__csHelpers','$typeof','useExtensions','releaseObject','__dispatchEvent'," +
                "'__tick','__onTeardown','__runTeardown','setImmediate','clearImmediate','loadStyleSheet'," +
                "'Headers','Response','URL','URLSearchParams','btoa','atob','localStorage','sessionStorage'," +
                "'queueMicrotask','fetch','WebSocket','__arrayBufferToBase64','__base64ToArrayBuffer']" +
                ".filter(function(n) { return !(n in globalThis); }).join(',')");
            Assert.AreEqual("", missing,
                "Expected globals missing after bootstrap eval - a needed export was lost");
        }

        [Test]
        public void CsHelpers_ExposeInteropFunctions() {
            // The interop helpers are intentionally closure-scoped; __csHelpers is
            // their supported access path (releaseObject additionally exported as
            // documented public API).
            Assert.AreEqual("function,function,function,function,function", _ctx.Eval(
                "[typeof __csHelpers.newObject, typeof __csHelpers.callMethod, typeof __csHelpers.callStatic, " +
                "typeof __csHelpers.wrapObject, typeof __csHelpers.releaseObject].join(',')"));
            Assert.AreEqual("true", _ctx.Eval("String(globalThis.releaseObject === __csHelpers.releaseObject)"));
        }
    }
}
