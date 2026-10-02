using System.Reflection;
using NUnit.Framework;
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
