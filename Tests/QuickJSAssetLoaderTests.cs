using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// PlayMode tests for AssetLoader (loadResourceAsync).
    /// Uses Resources/OneJS/QuickJSBootstrap.js.txt (a TextAsset guaranteed to exist) as the test resource.
    /// </summary>
    [TestFixture]
    public class QuickJSAssetLoaderTests {
        QuickJSContext _ctx;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _ctx = new QuickJSContext();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadResourceAsync_GlobalExists() {
            var result = _ctx.Eval("typeof loadResourceAsync");
            Assert.AreEqual("function", result, "loadResourceAsync should be a global function");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadResourceAsync_ReturnsNull_ForNonexistent() {
            _ctx.Eval(@"
                var __asyncResult = 'not_set';
                var __asyncDone = false;
                var promise = loadResourceAsync('NonExistent/FakePath');
                promise.then(function(result) {
                    __asyncResult = result;
                    __asyncDone = true;
                });
            ");
            _ctx.ExecutePendingJobs();

            yield return new WaitForSeconds(0.1f);

            QuickJSNative.ProcessCompletedTasks(_ctx);
            _ctx.ExecutePendingJobs();

            var done = _ctx.Eval("__asyncDone");
            Assert.AreEqual("true", done, "Promise should have resolved");

            var result = _ctx.Eval("__asyncResult");
            Assert.AreEqual("null", result, "Non-existent resource should resolve to null");

            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadResourceAsync_LoadsBuiltinResource() {
            _ctx.Eval(@"
                var __asyncResult = null;
                var __asyncDone = false;
                var promise = loadResourceAsync('OneJS/QuickJSBootstrap.js');
                promise.then(function(result) {
                    __asyncResult = result;
                    __asyncDone = true;
                });
            ");
            _ctx.ExecutePendingJobs();

            yield return new WaitForSeconds(0.1f);

            QuickJSNative.ProcessCompletedTasks(_ctx);
            _ctx.ExecutePendingJobs();

            var done = _ctx.Eval("__asyncDone");
            Assert.AreEqual("true", done, "Promise should have resolved");

            var result = _ctx.Eval("__asyncResult !== null");
            Assert.AreEqual("true", result, "Should have loaded a non-null asset");

            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadResourceAsync_WithType_ReturnsTypedAsset() {
            _ctx.Eval(@"
                var __asyncResult = null;
                var __asyncDone = false;
                var __hasText = false;
                var promise = loadResourceAsync('OneJS/QuickJSBootstrap.js', CS.UnityEngine.TextAsset);
                promise.then(function(result) {
                    __asyncResult = result;
                    __asyncDone = true;
                    __hasText = (typeof result.text === 'string' && result.text.length > 0);
                });
            ");
            _ctx.ExecutePendingJobs();

            yield return new WaitForSeconds(0.1f);

            QuickJSNative.ProcessCompletedTasks(_ctx);
            _ctx.ExecutePendingJobs();

            var done = _ctx.Eval("__asyncDone");
            Assert.AreEqual("true", done, "Promise should have resolved");

            var result = _ctx.Eval("__asyncResult !== null");
            Assert.AreEqual("true", result, "Should have loaded a non-null TextAsset");

            var hasText = _ctx.Eval("__hasText");
            Assert.AreEqual("true", hasText, "TextAsset should have accessible .text property");

            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadResourceAsync_EmptyPath_Throws() {
            _ctx.Eval(@"
                var __asyncError = null;
                var __asyncDone = false;
                var promise = loadResourceAsync('');
                promise.then(function(result) {
                    __asyncDone = true;
                }).catch(function(error) {
                    __asyncError = error.message;
                    __asyncDone = true;
                });
            ");
            _ctx.ExecutePendingJobs();

            yield return new WaitForSeconds(0.1f);

            QuickJSNative.ProcessCompletedTasks(_ctx);
            _ctx.ExecutePendingJobs();

            var done = _ctx.Eval("__asyncDone");
            Assert.AreEqual("true", done, "Promise should have settled");

            var error = _ctx.Eval("__asyncError");
            Assert.IsNotNull(error, "Should have caught an error");
            Assert.IsTrue(error.Contains("Path") || error.Contains("path") || error.Contains("empty"),
                $"Error should mention the path issue, got: {error}");

            yield return null;
        }
    }
}
