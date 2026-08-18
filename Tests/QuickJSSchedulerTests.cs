using NUnit.Framework;
using UnityEngine;

namespace OneJS.Tests {
    /// <summary>
    /// Guards the bootstrap scheduler's bounded-pass semantics and the WebGL
    /// timer teardown contract (see Plugins/WebGL/README.md, gotcha 6):
    /// 1. A drain pass is bounded: zero-delay self-reschedulers run once per
    ///    pass and a stalled interval fires once instead of bursting its whole
    ///    backlog. Pre-fix, either could spin one pass forever, which is how
    ///    unityInstance.Quit() hung the embedding tab on WebGL.
    /// 2. __teardownTimers (defined only when a native RAF exists, i.e. WebGL)
    ///    stops the tick, migrates pending timers onto native timers, restores
    ///    the native functions, and maps migrated override ids in the thin
    ///    clear*/cancel* wrappers. QuickJS pins this without a browser via the
    ///    same sentinel trick as QuickJSBootstrapScopeTests: planted recording
    ///    fakes play the role of browser natives across a bootstrap re-eval.
    /// </summary>
    [TestFixture]
    public class QuickJSSchedulerTests {
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
        public void TimerIds_StartAboveNativeIdRange() {
            // After WebGL teardown the restored clear*/cancel* wrappers tell
            // override ids from native browser ids by range; browsers hand out
            // small sequential ids, overrides start at 1 << 30.
            Assert.AreEqual("true,true,true", _ctx.Eval(@"
                [setTimeout(function(){}, 1000) >= (1 << 30),
                 setInterval(function(){}, 1000) >= (1 << 30),
                 requestAnimationFrame(function(){}) >= (1 << 30)].join(',')"));
        }

        [Test]
        public void ZeroDelayReschedule_RunsOncePerPass() {
            // A setTimeout(fn, 0) issued from inside a timeout callback is due
            // immediately; it must run in the NEXT pass, not extend the current
            // one (pre-fix this spun a single __tick forever).
            _ctx.Eval(@"
                globalThis.__n = 0;
                setTimeout(function again() { __n++; setTimeout(again, 0); }, 0);
            ");
            _ctx.Eval("__tick(10)");
            Assert.AreEqual("1", _ctx.Eval("String(__n)"), "zero-delay reschedule ran more than once in one pass");
            _ctx.Eval("__tick(20)");
            Assert.AreEqual("2", _ctx.Eval("String(__n)"), "rescheduled timeout did not run on the following pass");
        }

        [Test]
        public void Timeout_ClearedDuringPass_DoesNotRun() {
            _ctx.Eval(@"
                globalThis.__ran = false;
                globalThis.__late = 0;
                setTimeout(function() { clearTimeout(__late); }, 1);
                __late = setTimeout(function() { __ran = true; }, 1);
                __tick(100);
            ");
            Assert.AreEqual("false", _ctx.Eval("String(__ran)"),
                "a timeout cleared by an earlier callback in the same pass still ran");
        }

        [Test]
        public void Interval_StalledPass_FiresOnceAndRebases() {
            _ctx.Eval(@"
                globalThis.__c = 0;
                setInterval(function() { __c++; }, 100);
            ");
            // 10 intervals behind after a stall: one fire, then re-base
            _ctx.Eval("__tick(1000)");
            Assert.AreEqual("1", _ctx.Eval("String(__c)"), "stalled interval burst-fired its backlog");
            _ctx.Eval("__tick(1050)");
            Assert.AreEqual("1", _ctx.Eval("String(__c)"), "re-based interval fired before a full period elapsed");
            _ctx.Eval("__tick(1100)");
            Assert.AreEqual("2", _ctx.Eval("String(__c)"));
            // Keeping up: phase is preserved (next stays on the 100ms grid)
            _ctx.Eval("__tick(1201)");
            Assert.AreEqual("3", _ctx.Eval("String(__c)"), "keeping-up interval lost its phase");
        }

        [Test]
        public void TeardownTimers_OnlyDefined_WhenNativeRafExists() {
            // Fresh QuickJS has no native requestAnimationFrame, so the WebGL
            // branch (including __teardownTimers) must stay inert.
            Assert.AreEqual("undefined", _ctx.Eval("typeof __teardownTimers"));
        }

        const string PlantSentinelNatives = @"
            globalThis.__fakeLog = [];
            globalThis.__fakeId = 0;
            globalThis.__fakeTimers = {};
            globalThis.setTimeout = function(fn, delay) { var id = ++__fakeId; __fakeLog.push('setTimeout:' + id + ':' + delay); __fakeTimers[id] = fn; return id; };
            globalThis.clearTimeout = function(id) { __fakeLog.push('clearTimeout:' + id); };
            globalThis.setInterval = function(fn, interval) { var id = ++__fakeId; __fakeLog.push('setInterval:' + id + ':' + interval); __fakeTimers[id] = fn; return id; };
            globalThis.clearInterval = function(id) { __fakeLog.push('clearInterval:' + id); };
            globalThis.requestAnimationFrame = function(fn) { var id = ++__fakeId; __fakeLog.push('raf:' + id); __fakeTimers[id] = fn; return id; };
            globalThis.cancelAnimationFrame = function(id) { __fakeLog.push('cancelRaf:' + id); };
            globalThis.performance = { now: function() { return 1000; } };
            globalThis.__sentinelSetTimeout = globalThis.setTimeout;
            globalThis.__sentinelClearTimeout = globalThis.clearTimeout;
            globalThis.__sentinelRaf = globalThis.requestAnimationFrame;
        ";

        void ReEvalWithSentinels() {
            _ctx.Eval(PlantSentinelNatives, "plant_sentinels.js");
            _ctx.Eval(LoadBootstrapText(), "bootstrap_reeval.js");
        }

        [Test]
        public void TeardownTimers_RestoresPlantedNatives() {
            ReEvalWithSentinels();
            Assert.AreEqual("function", _ctx.Eval("typeof __teardownTimers"),
                "WebGL branch did not activate with a (sentinel) native RAF present");
            // Before teardown the overrides are installed: override ids are huge
            _ctx.Eval("globalThis.__probeId = setTimeout(function(){}, 5); clearTimeout(__probeId);");
            Assert.AreEqual("true", _ctx.Eval("String(__probeId >= (1 << 30))"),
                "re-eval did not install the timer overrides");
            _ctx.Eval("__teardownTimers()");
            // set* and raf are the sentinels again; clear*/cancel* are thin
            // wrappers that fall through to the sentinels for unknown ids
            _ctx.Eval("__fakeLog.length = 0; clearTimeout(42); clearInterval(43); cancelAnimationFrame(44);");
            Assert.AreEqual("clearTimeout:42,clearInterval:43,cancelRaf:44", _ctx.Eval("__fakeLog.join(',')"),
                "restored clear wrappers did not fall through to the natives for native ids");
            Assert.AreEqual("true", _ctx.Eval("String(setTimeout(function(){}, 5) < 1000)"),
                "globalThis.setTimeout is not the planted native after teardown (native ids are small)");
        }

        [Test]
        public void TeardownTimers_MigratesPendingTimers_AndMapsIds() {
            ReEvalWithSentinels();
            // Time base: current time 1000 (matches performance.now sentinel)
            _ctx.Eval("__tick(1000)");
            _ctx.Eval(@"
                globalThis.__mig = { timeout: 0, interval: 0, raf: 0 };
                globalThis.__toId = setTimeout(function() { __mig.timeout++; }, 500);
                globalThis.__intId = setInterval(function() { __mig.interval++; }, 100);
                globalThis.__rafId2 = requestAnimationFrame(function() { __mig.raf++; });
                __fakeLog.length = 0;
                __teardownTimers();
            ");
            // Migration: raf -> native raf; timeout -> native setTimeout with
            // remaining delay; interval -> native setTimeout for the pending
            // fire (two-stage), upgraded to native setInterval when it fires
            Assert.AreEqual("raf:1,setTimeout:2:500,setTimeout:3:100", _ctx.Eval("__fakeLog.join(',')"),
                "pending timers were not migrated onto the natives as expected");
            // Firing the interval's stage-1 upgrades it to a native interval
            _ctx.Eval("__fakeLog.length = 0; __fakeTimers[3]();");
            Assert.AreEqual("setInterval:4:100", _ctx.Eval("__fakeLog.join(',')"));
            Assert.AreEqual("1", _ctx.Eval("String(__mig.interval)"));
            // clear* with the old override ids routes to the migrated native ids
            _ctx.Eval("__fakeLog.length = 0; clearInterval(__intId); clearTimeout(__toId); cancelAnimationFrame(__rafId2);");
            Assert.AreEqual("clearInterval:4,clearTimeout:2,cancelRaf:1", _ctx.Eval("__fakeLog.join(',')"),
                "clear wrappers did not map migrated override ids to their native counterparts");
            // Idempotent: a second call must not touch the natives again
            _ctx.Eval("__fakeLog.length = 0; __teardownTimers();");
            Assert.AreEqual("", _ctx.Eval("__fakeLog.join(',')"), "__teardownTimers is not idempotent");
        }

        [Test]
        public void WebGLClock_SeedsFromPerformanceNow() {
            // On WebGL, tick timestamps are performance.now()-based page
            // uptime. Module-level timers are created before the first tick;
            // without seeding, every delay shorter than the boot time would
            // fire immediately on tick 1.
            ReEvalWithSentinels(); // sentinel performance.now() = 1000
            _ctx.Eval("globalThis.__early = 0; setTimeout(function() { __early++; }, 500);");
            _ctx.Eval("__tick(1050)");
            Assert.AreEqual("0", _ctx.Eval("String(__early)"),
                "module-level timer misfired on the first tick: clock not seeded from performance.now()");
            _ctx.Eval("__tick(1600)");
            Assert.AreEqual("1", _ctx.Eval("String(__early)"), "seeded timer did not fire at its real due time");
        }

        [Test]
        public void Teardown_AfterRestart_StillStopsTick() {
            ReEvalWithSentinels();
            _ctx.Eval("__teardownTimers(); __fakeLog.length = 0; __startWebGLTick();");
            Assert.AreEqual("true", _ctx.Eval("String(__fakeLog.length === 1 && __fakeLog[0].indexOf('raf:') === 0)"),
                "restart after teardown did not schedule via the native RAF");
            _ctx.Eval(@"
                globalThis.__restartRafId = parseInt(__fakeLog[0].split(':')[1], 10);
                __teardownTimers();
                __fakeLog.length = 0;
                __fakeTimers[__restartRafId](12345);
            ");
            Assert.AreEqual("false", _ctx.Eval("String(__fakeLog.some(function(e) { return e.indexOf('raf:') === 0; }))"),
                "a second teardown after a restart left the tick loop rescheduling forever");
        }

        [Test]
        public void WebGLTick_MidPassTeardown_StopsAndMigrates() {
            // The actual quit path: Application.Quit runs inside a __webglTick
            // pass (Unity's frame rides the OneJS tick), so qjs_destroy's
            // teardown happens mid-drain. The pass must finish without running
            // torn-down timers, migrate them instead, and not reschedule.
            ReEvalWithSentinels();
            _ctx.Eval(@"
                __startWebGLTick();
                globalThis.__order = [];
                setTimeout(function() { __order.push('a'); __teardownTimers(); }, 1);
                setTimeout(function() { __order.push('b'); }, 1);
                globalThis.__tickRafId = parseInt(__fakeLog[0].split(':')[1], 10);
                __fakeLog.length = 0;
                __fakeTimers[__tickRafId](2000);
            ");
            Assert.AreEqual("a", _ctx.Eval("__order.join(',')"),
                "a timeout due in the torn-down pass still ran inside that pass");
            Assert.AreEqual("false", _ctx.Eval("String(__fakeLog.some(function(e) { return e.indexOf('raf:') === 0; }))"),
                "the tick rescheduled itself after a mid-pass teardown");
            _ctx.Eval(@"
                var mig = __fakeLog.filter(function(e) { return e.indexOf('setTimeout:') === 0; })[0];
                __fakeTimers[parseInt(mig.split(':')[1], 10)]();
            ");
            Assert.AreEqual("a,b", _ctx.Eval("__order.join(',')"), "the mid-pass-migrated timeout did not fire");
        }

        [Test]
        public void BootstrapReEval_RetiresPreviousGeneration() {
            // Context recreate without a page reload evaluates a new bootstrap
            // generation. It must retire the previous one (stop its tick,
            // migrate its pending timers) and capture the TRUE natives via the
            // page stash, never a predecessor's overrides or wrappers.
            ReEvalWithSentinels(); // generation A with sentinel natives
            _ctx.Eval(@"
                __startWebGLTick();
                globalThis.__genATickRafId = parseInt(__fakeLog[0].split(':')[1], 10);
                globalThis.__genAPending = setTimeout(function() {}, 50000);
                __fakeLog.length = 0;
            ");
            _ctx.Eval(LoadBootstrapText(), "bootstrap_reeval2.js"); // generation B boots
            Assert.AreEqual("true", _ctx.Eval("String(__fakeLog.some(function(e) { return e.indexOf('setTimeout:') === 0; }))"),
                "generation B's boot did not migrate generation A's pending timer");
            _ctx.Eval("__fakeLog.length = 0; __fakeTimers[__genATickRafId](9999);");
            Assert.AreEqual("false", _ctx.Eval("String(__fakeLog.some(function(e) { return e.indexOf('raf:') === 0; }))"),
                "generation A's tick loop kept rescheduling after generation B booted");
            // The page stash holds the true (sentinel) natives
            Assert.AreEqual("true", _ctx.Eval("String(__onejsNativeTimers.setTimeout === __sentinelSetTimeout)"),
                "the native stash does not hold the true natives");
            // Generation B's overrides are installed; its teardown restores the
            // true natives directly (no wrapper chaining across generations)
            _ctx.Eval("globalThis.__probeB = setTimeout(function(){}, 5); clearTimeout(__probeB);");
            Assert.AreEqual("true", _ctx.Eval("String(__probeB >= (1 << 30))"),
                "generation B's overrides are not installed");
            _ctx.Eval("__teardownTimers();");
            Assert.AreEqual("true", _ctx.Eval("String(globalThis.setTimeout === __sentinelSetTimeout)"),
                "generation B's teardown did not restore the true native setTimeout");
            Assert.AreEqual("true", _ctx.Eval("String(globalThis.requestAnimationFrame === __sentinelRaf)"),
                "generation B's teardown did not restore the true native requestAnimationFrame");
        }

        [Test]
        public void CleanTeardown_RestoresRawNatives() {
            // With nothing pending to migrate, teardown must leave the page
            // exactly as it was: raw natives, no wrappers at all.
            ReEvalWithSentinels();
            _ctx.Eval("__teardownTimers();");
            Assert.AreEqual("true,true", _ctx.Eval(
                "[globalThis.setTimeout === __sentinelSetTimeout, globalThis.clearTimeout === __sentinelClearTimeout].join(',')"),
                "a teardown with nothing migrated installed wrappers instead of the raw natives");
        }

        [Test]
        public void OverrideClears_FallThrough_ForForeignIds() {
            // A host page that captured a native timer id before OneJS booted
            // must still be able to cancel it through the overrides.
            ReEvalWithSentinels();
            _ctx.Eval("globalThis.__preBootId = __sentinelSetTimeout(function(){}, 60000);");
            _ctx.Eval("__fakeLog.length = 0; clearTimeout(__preBootId);");
            Assert.AreEqual("true", _ctx.Eval("String(__fakeLog.length === 1 && __fakeLog[0] === 'clearTimeout:' + __preBootId)"),
                "the clearTimeout override did not fall through to the native for a pre-boot id");
            // And clearTimeout(intervalId) clears the interval (browser parity)
            _ctx.Eval(@"
                globalThis.__intFired = 0;
                var iid = setInterval(function() { __intFired++; }, 100);
                clearTimeout(iid);
                __tick(99999);
            ");
            Assert.AreEqual("0", _ctx.Eval("String(__intFired)"), "clearTimeout did not clear an interval id");
        }
    }
}
