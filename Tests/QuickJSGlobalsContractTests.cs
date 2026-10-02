using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS.Tests {
    public class NullStaticFixtureItem {
        public int Value = 7;
    }

    /// <summary>
    /// Statics read through the CS path proxy, one of them null, the way an
    /// unset singleton is in edit-mode preview.
    /// </summary>
    public static class NullStaticFixture {
        public static NullStaticFixtureItem Missing { get; set; }
        public static NullStaticFixtureItem Present { get; set; } = new NullStaticFixtureItem();
        public static string NullField;
    }

    /// <summary>A UnityEvent-shaped listener list, for passing a JS function to a C# method.</summary>
    public class CallbackListenerFixture {
        readonly System.Collections.Generic.List<System.Action> _listeners = new System.Collections.Generic.List<System.Action>();
        public void AddListener(System.Action listener) => _listeners.Add(listener);
        public void RemoveListener(System.Action listener) => _listeners.Remove(listener);
        public int Count => _listeners.Count;
        public void Fire() {
            foreach (var l in _listeners.ToArray()) l();
        }
    }

    /// <summary>
    /// Globals a user calls behaving the way their names promise: the web APIs
    /// as on the web, and the CS proxy and releaseObject as documented.
    /// </summary>
    [TestFixture]
    public class QuickJSGlobalsContractTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            NullStaticFixture.Missing = null;
            NullStaticFixture.NullField = null;
            _ctx = new QuickJSContext();
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        // MARK: CS proxy

        [Test]
        public void NullStaticProperty_ReadsAsNull() {
            Assert.AreEqual("true", _ctx.Eval("String(CS.OneJS.Tests.NullStaticFixture.Missing === null)"),
                "a static property holding null must read as null, not as a truthy path proxy");
            Assert.AreEqual("true", _ctx.Eval("String(CS.OneJS.Tests.NullStaticFixture.NullField === null)"),
                "a static field holding null must read as null");
            Assert.AreEqual("7", _ctx.Eval("String(CS.OneJS.Tests.NullStaticFixture.Present.Value)"));
        }

        [Test]
        public void ReleaseObject_ZeroesTheProxyHandleWithoutThrowing() {
            Assert.AreEqual("0", _ctx.Eval(@"
                var o = CS.OneJS.Tests.NullStaticFixture.Present;
                releaseObject(o);
                String(o.__csHandle)"));
        }

        [Test]
        public void AFunctionPassedToACSharpMethod_IsTheSameDelegateEachTime() {
            // AddListener(fn) then RemoveListener(fn) has to find the listener it
            // added, as it does in C#: one function, one callback slot, one delegate.
            Assert.AreEqual("1,0", _ctx.Eval(@"
                var list = new CS.OneJS.Tests.CallbackListenerFixture();
                var calls = 0;
                function onFire() { calls++; }
                list.AddListener(onFire);
                list.Fire();
                list.RemoveListener(onFire);
                calls + ',' + list.Count"));
        }

        // MARK: onejs namespace

        [Test]
        public void TheOnejsNamespace_ReachesTheSameGlobalsAsTheOldNames() {
            _ctx.Eval("globalThis.__isPlaying = true; globalThis.__workingDir = '/app'");
            Assert.AreEqual("true,/app,true,true,true,true", _ctx.Eval(@"
                [onejs.isPlaying, onejs.paths.working,
                 onejs.fs.readText === readTextFile, onejs.styles.load === loadStyleSheet,
                 onejs.cs.release === releaseObject, onejs.cs.extensions === useExtensions].join(',')"));
            Assert.AreEqual("true,true,false", _ctx.Eval(@"
                [onejs.cs.typeExists(CS.UnityEngine.GameObject), onejs.cs.typeExists('UnityEngine.Vector3'),
                 onejs.cs.typeExists('UnityEngine.NoSuchThing')].join(',')"));
            Assert.AreEqual("true", _ctx.Eval("String(Object.isFrozen(onejs) && Object.isFrozen(onejs.cs))"));
        }

        // MARK: Crossings

        [Test]
        public void ARepeatedMethodCall_CrossesIntoCSharpOnce() {
            // The first call asks C# whether the member is a property or a
            // method; every later call on that type knows, and only invokes.
            _ctx.Eval(@"
                globalThis.__list = new CS.OneJS.Tests.CallbackListenerFixture();
                __list.Fire();
                CS.UnityEngine.Mathf.Abs(-1);");
            long before = QuickJSNative.InteropCallCount;
            _ctx.Eval("__list.Fire()");
            Assert.AreEqual(1, QuickJSNative.InteropCallCount - before, "instance method");
            before = QuickJSNative.InteropCallCount;
            _ctx.Eval("CS.UnityEngine.Mathf.Abs(-1)");
            Assert.AreEqual(1, QuickJSNative.InteropCallCount - before, "static method");
        }

        // MARK: Timers

        [Test]
        public void TimeoutsDueInOnePass_RunInDueOrder() {
            _ctx.Eval(@"
                globalThis.__order = [];
                setTimeout(function () { __order.push('ten'); }, 10);
                setTimeout(function () { __order.push('zero'); }, 0);
                setTimeout(function () { __order.push('five'); }, 5);
            ");
            _ctx.Eval("__tick(50)");
            Assert.AreEqual("zero,five,ten", _ctx.Eval("__order.join(',')"),
                "timeouts due in the same pass must run in due order, as on the web");
        }

        // MARK: setImmediate

        [Test]
        public void SetImmediate_RunsPromptly_ButARescheduleWaitsForTheNextTick() {
            // The first immediate runs before the turn ends (React's scheduler
            // relies on it); one scheduled from inside an immediate waits for
            // the next tick, so `function step() { work(); setImmediate(step) }`
            // yields instead of freezing Unity.
            _ctx.Eval(@"
                globalThis.__steps = 0;
                globalThis.__order = [];
                function step() { __steps++; if (__steps < 1000) setImmediate(step); }
                setImmediate(step);
                setImmediate(function () { __order.push('b'); });
                var cancelled = setImmediate(function () { __order.push('never'); });
                clearImmediate(cancelled);
            ");
            _ctx.ExecutePendingJobs();
            Assert.AreEqual("1", _ctx.Eval("String(__steps)"));
            Assert.AreEqual("b", _ctx.Eval("__order.join(',')"));
            _ctx.Eval("__tick(16)");
            _ctx.ExecutePendingJobs();
            Assert.AreEqual("2", _ctx.Eval("String(__steps)"));
        }

        // MARK: localStorage

        const string StorageKey = "onejs_test_storage_key";

        static void ClearStorageKeys() {
            PlayerPrefs.DeleteKey(StorageKey);
            PlayerPrefs.DeleteKey("onejs:" + StorageKey);
            PlayerPrefs.DeleteKey("onejs:__keys");
            PlayerPrefs.DeleteKey("onejs_test_other");
        }

        [Test]
        public void LocalStorage_IsScopedToOneJS_AndEnumerable() {
            ClearStorageKeys();
            try {
                PlayerPrefs.SetString("onejs_test_other", "the game's own");
                _ctx.Eval($"localStorage.setItem('{StorageKey}', 'dark')");
                Assert.AreEqual("dark", _ctx.Eval($"localStorage.getItem('{StorageKey}')"));
                Assert.AreEqual("1", _ctx.Eval("String(localStorage.length)"));
                Assert.AreEqual(StorageKey, _ctx.Eval("localStorage.key(0)"));

                _ctx.Eval("localStorage.clear()");
                Assert.AreEqual("true", _ctx.Eval($"String(localStorage.getItem('{StorageKey}') === null)"));
                Assert.AreEqual("the game's own", PlayerPrefs.GetString("onejs_test_other"),
                    "clear() must leave PlayerPrefs that OneJS did not write");
            } finally {
                ClearStorageKeys();
            }
        }

        [Test]
        public void LocalStorage_ReadsAValueSavedBeforeTheScope_Once() {
            ClearStorageKeys();
            try {
                PlayerPrefs.SetString(StorageKey, "saved by an older OneJS");
                Assert.AreEqual("saved by an older OneJS", _ctx.Eval($"localStorage.getItem('{StorageKey}')"));
                Assert.AreEqual("saved by an older OneJS", PlayerPrefs.GetString("onejs:" + StorageKey),
                    "the old value moves into the scope when first read");
                _ctx.Eval($"localStorage.removeItem('{StorageKey}')");
                Assert.AreEqual("true", _ctx.Eval($"String(localStorage.getItem('{StorageKey}') === null)"),
                    "a removed key does not come back from its old unscoped copy");
            } finally {
                ClearStorageKeys();
            }
        }

        // MARK: Fetch polyfills

        [Test]
        public void Headers_AreIterable() {
            Assert.AreEqual("a=1,b=2", _ctx.Eval(@"
                var out = [];
                for (const [k, v] of new Headers({ a: '1', b: '2' })) out.push(k + '=' + v);
                out.join(',')"));
            Assert.AreEqual("true", _ctx.Eval("String(new Headers({ x: '' }).get('x') === '')"),
                "an empty header value reads as an empty string");
        }

        [Test]
        public void URLSearchParams_AreIterable() {
            Assert.AreEqual("2", _ctx.Eval("Object.fromEntries(new URLSearchParams('a=1&b=2')).b"));
            Assert.AreEqual("2", _ctx.Eval("String([...new URLSearchParams('a=1&b=2')].length)"));
        }

        [Test]
        public void Fetch_SendsPatchAsPatchWithItsBody() {
            var create = typeof(Network).GetMethod("CreateRequest", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(create, "Network.CreateRequest");
            using (var request = (UnityWebRequest)create.Invoke(null, new object[] { "http://localhost/x", "PATCH", "{\"a\":1}" })) {
                Assert.AreEqual("PATCH", request.method);
                Assert.IsNotNull(request.uploadHandler, "the PATCH body was dropped");
                Assert.IsNotNull(request.downloadHandler);
            }
        }
    }
}
