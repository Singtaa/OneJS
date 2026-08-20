using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    // MARK: Fixtures for JS -> C# delegate assignment
    public static class CallbackBindingFixture {
        public static Action OnPing;
        public static Action<int> OnScore;
        public static Func<int, int> Doubler;
        public static Func<string> GetLabel;
        public static Func<float, float> Halver;
        public static Func<int, int, bool> IsGreater;

        public static void Reset() {
            OnPing = null;
            OnScore = null;
            Doubler = null;
            GetLabel = null;
            Halver = null;
            IsGreater = null;
        }
    }

    /// <summary>
    /// Playmode tests for the C# -> JS calling surface: Func delegate wrappers
    /// (JS functions returning values to C#), array return marshaling, typed
    /// GetJSFunction bindings, and stale-handle safety across context reloads.
    /// </summary>
    [TestFixture]
    public class QuickJSCallbackBindingTests {
        QuickJSContext _ctx;

        [UnitySetUp]
        public IEnumerator SetUp() {
            CallbackBindingFixture.Reset();
            _ctx = new QuickJSContext();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            CallbackBindingFixture.Reset();
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        // MARK: Func delegate wrappers (JS returns a value to C#)

        [UnityTest]
        public IEnumerator FuncDelegate_IntReturn_MarshalsBack() {
            _ctx.Eval("CS.OneJS.Tests.CallbackBindingFixture.Doubler = function(x) { return x * 2; };");
            Assert.IsNotNull(CallbackBindingFixture.Doubler, "Func<int,int> assignment produced null");
            Assert.AreEqual(42, CallbackBindingFixture.Doubler(21));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FuncDelegate_StringReturn_MarshalsBack() {
            _ctx.Eval("CS.OneJS.Tests.CallbackBindingFixture.GetLabel = function() { return 'from-js'; };");
            Assert.IsNotNull(CallbackBindingFixture.GetLabel, "Func<string> assignment produced null");
            Assert.AreEqual("from-js", CallbackBindingFixture.GetLabel());
            yield return null;
        }

        [UnityTest]
        public IEnumerator FuncDelegate_FloatReturn_MarshalsBack() {
            _ctx.Eval("CS.OneJS.Tests.CallbackBindingFixture.Halver = function(x) { return x / 2; };");
            Assert.IsNotNull(CallbackBindingFixture.Halver, "Func<float,float> assignment produced null");
            Assert.AreEqual(3.5f, CallbackBindingFixture.Halver(7f), 0.0001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FuncDelegate_BoolReturn_MarshalsBack() {
            _ctx.Eval("CS.OneJS.Tests.CallbackBindingFixture.IsGreater = function(a, b) { return a > b; };");
            Assert.IsNotNull(CallbackBindingFixture.IsGreater, "Func<int,int,bool> assignment produced null");
            Assert.IsTrue(CallbackBindingFixture.IsGreater(5, 3));
            Assert.IsFalse(CallbackBindingFixture.IsGreater(3, 5));
            yield return null;
        }

        // MARK: InvokeCallback return values

        [UnityTest]
        public IEnumerator InvokeCallback_ArrayReturn_MarshalsAsArray() {
            var handle = int.Parse(_ctx.Eval("__registerCallback(function() { return [1, 2, 3]; });"));

            var result = _ctx.InvokeCallback(handle);
            var list = result as IList;
            Assert.IsNotNull(list, $"Expected an array-like result, got {result?.GetType().Name ?? "null"}");
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, Convert.ToInt32(list[0]));
            Assert.AreEqual(3, Convert.ToInt32(list[2]));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvokeCallback_PlainObjectReturn_MarshalsAsDictionary() {
            var handle = int.Parse(_ctx.Eval("__registerCallback(function() { return { score: 99, name: 'ok' }; });"));

            var result = _ctx.InvokeCallback(handle);
            var dict = result as Dictionary<string, object>;
            Assert.IsNotNull(dict, $"Expected a dictionary result, got {result?.GetType().Name ?? "null"}");
            Assert.AreEqual(99, Convert.ToInt32(dict["score"]));
            Assert.AreEqual("ok", dict["name"]);
            yield return null;
        }

        // MARK: Stale handle safety

        [UnityTest]
        public IEnumerator StaleHandle_FromDisposedContext_FailsLoudlyOnNewContext() {
            var staleHandle = int.Parse(_ctx.Eval("__registerCallback(function() { return 'old context'; });"));

            _ctx.Dispose();
            _ctx = new QuickJSContext();

            // Occupy slot 0 of the new context: without generation-tagged handles
            // the stale handle would silently invoke this function instead.
            _ctx.Eval("__registerCallback(function() { return 'WRONG function'; });");

            var ex = Assert.Throws<Exception>(() => _ctx.InvokeCallback(staleHandle));
            StringAssert.Contains("stale", ex.Message);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StaleDelegate_InvokedAfterContextDisposed_WarnsOnceAndNoOps() {
            _ctx.Eval("CS.OneJS.Tests.CallbackBindingFixture.OnPing = function() { CS.UnityEngine.Debug.Log('ping'); };");
            var stale = CallbackBindingFixture.OnPing;
            Assert.IsNotNull(stale);

            _ctx.Dispose();
            _ctx = null;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no longer exists"));
            Assert.DoesNotThrow(() => stale());
            // Second invocation must stay silent (warn-once) and must not crash.
            Assert.DoesNotThrow(() => stale());
            yield return null;
        }

        // MARK: GetJSFunction

        [UnityTest]
        public IEnumerator GetJSFunction_FuncWithArgs_CallsAndReturns() {
            _ctx.Eval("globalThis.addNumbers = function(a, b) { return a + b; };");

            var add = _ctx.GetJSFunction<Func<int, int, int>>("addNumbers");
            Assert.AreEqual(42, add(40, 2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_Action_CallsIntoJs() {
            _ctx.Eval("globalThis.lastToast = ''; globalThis.showToast = function(msg) { globalThis.lastToast = msg; };");

            var showToast = _ctx.GetJSFunction<Action<string>>("showToast");
            showToast("level complete");

            Assert.AreEqual("level complete", _ctx.Eval("globalThis.lastToast"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_DottedPath_Resolves() {
            _ctx.Eval("globalThis.game = { ui: { ping: function() { return 'pong'; } } };");

            var ping = _ctx.GetJSFunction<Func<string>>("game.ui.ping");
            Assert.AreEqual("pong", ping());
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_IsLazy_MissingGlobalThrowsOnInvoke() {
            // Creating the delegate must not throw: the global may be defined later.
            var missing = _ctx.GetJSFunction<Action>("notDefinedYet");

            Assert.Throws<MissingMemberException>(() => missing());

            // Define it and a fresh binding resolves. (The first failed resolution
            // doesn't poison the binding: resolution retries per invocation until
            // it succeeds.)
            _ctx.Eval("globalThis.notDefinedYet = function() { globalThis.itRan = true; };");
            Assert.DoesNotThrow(() => missing());
            Assert.AreEqual("true", _ctx.Eval("globalThis.itRan === true"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_SameNameAndType_ReturnsCachedDelegate() {
            _ctx.Eval("globalThis.noop = function() {};");

            var a = _ctx.GetJSFunction<Action>("noop");
            var b = _ctx.GetJSFunction<Action>("noop");
            Assert.AreSame(a, b);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_VectorReturn_Converts() {
            _ctx.Eval("globalThis.getSpawn = function() { return { x: 1, y: 2, z: 3 }; };");

            var getSpawn = _ctx.GetJSFunction<Func<Vector3>>("getSpawn");
            Assert.AreEqual(new Vector3(1, 2, 3), getSpawn());
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetJSFunction_AfterDispose_ThrowsInvalidOperation() {
            _ctx.Eval("globalThis.noop2 = function() {};");
            var noop = _ctx.GetJSFunction<Action>("noop2");
            noop(); // resolves against the live context

            _ctx.Dispose();
            var dead = _ctx;
            _ctx = null;

            Assert.Throws<InvalidOperationException>(() => noop());
            GC.KeepAlive(dead);
            yield return null;
        }
    }
}
