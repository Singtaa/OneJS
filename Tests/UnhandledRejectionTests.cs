using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// A rejected promise nobody handles is reported at error level once the
    /// job queue drains, as a browser reports it, so a throwing async effect
    /// leaves a trace. One handled by then, even a moment late, is not.
    /// </summary>
    [TestFixture]
    public class UnhandledRejectionTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() => _ctx = new QuickJSContext();

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        [Test]
        public void AnUnhandledRejection_IsLoggedAsAnError() {
            // An exception carrying the JS frames, so the Console links to the script
            LogAssert.Expect(LogType.Exception, new Regex(@"JSException: .*\[OneJS\] Unhandled promise rejection: Error: lost"));
            _ctx.Eval("(async function () { throw new Error('lost') })()");
            _ctx.ExecutePendingJobs();
        }

        [Test]
        public void ARejectionHandledBeforeTheQueueDrains_IsNotLogged() {
            _ctx.Eval(@"
                Promise.reject(new Error('caught')).catch(function () {});
                var late = Promise.reject(new Error('caught later'));
                Promise.resolve().then(function () { late.catch(function () {}) });");
            _ctx.ExecutePendingJobs();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
