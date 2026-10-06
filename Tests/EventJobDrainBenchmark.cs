using System.Diagnostics;
using NUnit.Framework;

namespace OneJS.Tests {
    /// <summary>
    /// #106 finding A: every event dispatch drains the job queue
    /// (ExecutePendingJobs) before it returns. This measures what that drain
    /// costs against the dispatch it follows, on the same zero-alloc call the
    /// bridge makes for a pointer event, so the most that skipping or
    /// coalescing it could save is a number rather than a guess.
    ///
    /// Explicit: a benchmark, not a check, and the suites skip it. Run it with
    /// <c>unity test . --mode PlayMode --filter OneJS.Tests.EventJobDrainBenchmark</c>
    /// and read the "[Bench106A]" lines.
    /// </summary>
    [TestFixture, Explicit("benchmark"), Category("Benchmark")]
    public class EventJobDrainBenchmark {
        const int Events = 200000;
        const int Warmup = 20000;

        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() => _ctx = new QuickJSContext();

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        int Handler(string body) =>
            int.Parse(_ctx.Eval($"__registerCallback(function(t, h, x, y, b, p) {{ {body} }})"));

        /// <summary>Nanoseconds per event over <see cref="Events"/> dispatches, each followed by a drain every <paramref name="drainEvery"/> events (0: never).</summary>
        double NsPerEvent(int handle, int drainEvery) {
            for (int i = 0; i < Warmup; i++) {
                _ctx.InvokeCallbackReturnInt(handle, 1, 2, 0.5f, 0.5f, 0, 0);
                if (drainEvery > 0 && i % drainEvery == 0) _ctx.ExecutePendingJobs();
            }
            _ctx.ExecutePendingJobs();
            var sw = Stopwatch.StartNew();
            for (int i = 1; i <= Events; i++) {
                _ctx.InvokeCallbackReturnInt(handle, 1, 2, 0.5f, 0.5f, 0, 0);
                if (drainEvery > 0 && i % drainEvery == 0) _ctx.ExecutePendingJobs();
            }
            sw.Stop();
            _ctx.ExecutePendingJobs();
            return sw.Elapsed.TotalMilliseconds * 1e6 / Events;
        }

        [Test]
        public void DrainAfterEveryDispatch() {
            // A handler that does nothing, as most pointermoves reach none.
            int idle = Handler("return 0;");
            // One that queues a microtask, as a React state update does.
            _ctx.Eval("globalThis.__benchJobs = 0;");
            int react = Handler("Promise.resolve().then(function() { __benchJobs++; }); return 0;");

            double dispatch = NsPerEvent(idle, 0);
            double dispatchDrainEmpty = NsPerEvent(idle, 1);
            double reactDrainEach = NsPerEvent(react, 1);
            double reactDrainPer10 = NsPerEvent(react, 10);

            var line = $"[Bench106A] ns/event: dispatch {dispatch:F0}, + empty drain {dispatchDrainEmpty:F0} " +
                       $"(drain {dispatchDrainEmpty - dispatch:F0}), microtask drained each {reactDrainEach:F0}, " +
                       $"microtask drained every 10th {reactDrainPer10:F0}";
            UnityEngine.Debug.Log(line);
            TestContext.WriteLine(line);
            Assert.Greater(int.Parse(_ctx.Eval("__benchJobs")), 0, "The microtask handler never ran its jobs.");
        }
    }
}
