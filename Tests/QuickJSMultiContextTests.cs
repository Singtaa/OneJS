using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// Two live QuickJS contexts at once, which is what two JSRunners are.
    ///
    /// Every other fixture in this repository creates exactly one context, so
    /// nothing here has ever exercised the state that is per process rather than
    /// per context. That blind spot has now produced three defects in a
    /// fortnight, all of them from the same reporter and all of them only
    /// reachable with two runners live: the StreamingAssets collision fixed in
    /// 3.4.3, the Addressables follow-on in 3.4.4, and issue #120, the one this
    /// fixture is about.
    ///
    /// Multiple runners is not an exotic setup. The documentation recommends it
    /// for independent UI layers, a HUD under a menu being the stock example.
    ///
    /// The shared state worth knowing about, all of it static on QuickJSNative:
    /// the completion queue, the task id counter, the queue warning latch and
    /// the peak size counter. ClearAllHandles is process wide too.
    /// </summary>
    public class QuickJSMultiContextTests {
        QuickJSContext _a;
        QuickJSContext _b;

        /// <summary>
        /// How long to wait for a C# task to finish and reach the queue. Generous
        /// on purpose: this fixture is about where a completion goes, never about
        /// how fast it gets there, and a tight bound here would turn an unrelated
        /// slow frame into a failure that reads like the defect.
        /// </summary>
        const float CompletionTimeout = 5f;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _a = new QuickJSContext();
            _b = new QuickJSContext();

            // The completion queue is static, so a task left behind by any other
            // test in this run is still sitting in it. Drain it before measuring
            // anything, or a stray completion counts as ours and the numbers in
            // the failure message below describe the wrong task.
            for (var i = 0; i < 10 && QuickJSNative.GetPendingTaskCount() > 0; i++) {
                QuickJSNative.ProcessCompletedTasks(_a);
                yield return null;
            }
            Assert.AreEqual(0, QuickJSNative.GetPendingTaskCount(),
                "The shared completion queue did not drain before the test started, so anything measured here would be "
                + "about somebody else's task. This is a fixture problem, not the defect under test.");
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _a?.Dispose();
            _a = null;
            _b?.Dispose();
            _b = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        /// <summary>
        /// The fixture proving itself before it is allowed to accuse anything.
        ///
        /// Two contexts that are not really both live is the trap here: the
        /// defect below would "reproduce" beautifully against a second context
        /// that was dead or was secretly the first one, and the red would mean
        /// nothing. So this asserts they both evaluate, and that they do not
        /// share a global scope.
        /// </summary>
        [UnityTest]
        public IEnumerator BothContextsAreLiveAndIndependent() {
            Assert.AreEqual("42", _a.Eval("42"), "Context A does not evaluate, so there is no fixture here.");
            Assert.AreEqual("42", _b.Eval("42"), "Context B does not evaluate, so there is only one context here.");

            _a.Eval("var __who = 'A'");
            _b.Eval("var __who = 'B'");

            Assert.AreEqual("A", _a.Eval("__who"));
            Assert.AreEqual("B", _b.Eval("__who"),
                "Both contexts report the same global, so they are one context and every other test in this fixture "
                + "would be measuring nothing.");

            yield return null;
        }

        /// <summary>
        /// That the queue really is shared, which is the mechanism the defect
        /// rides on.
        ///
        /// Asserted rather than assumed, because if a completion registered from
        /// B were somehow invisible to A, the test below would pass for a reason
        /// that has nothing to do with the bug being fixed.
        ///
        /// The pushed version of this test asserted that A *drained* B's
        /// completion, and said in its own comment that it should keep passing
        /// after #120 is fixed. Those two cannot both hold: A consuming B's
        /// completion IS the defect, so that assertion could only stay green by
        /// leaving the bug in. What the comment was reaching for is kept here and
        /// the measurement is moved off consumption: A reaches into the same
        /// queue and SEES the entry, then declines it by ownership and leaves it
        /// for B.
        ///
        /// Seeing-and-declining is what separates the fix from a fixture where A
        /// simply never had access. The foreign-completion counter is what tells
        /// them apart: it only moves when a context dequeued an entry, found
        /// somebody else's id on it, and put it back.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCompletionQueueIsSharedBetweenContexts() {
            _b.Eval("CS.OneJS.Tests.AsyncTestHelper.DelayedMessageAsync('from B', 10)");
            _b.ExecutePendingJobs();

            yield return WaitForQueuedCompletion();

            Assert.GreaterOrEqual(QuickJSNative.GetPendingTaskCount(), 1,
                "A task registered from context B never reached the queue at all.");

            QuickJSNative.ResetTaskQueueMonitoring();

            // Context A reaches into the same queue, which is the whole point.
            var drainedByA = QuickJSNative.ProcessCompletedTasks(_a);
            var examinedByA = QuickJSNative.GetForeignCompletionCount();

            Assert.AreEqual(0, drainedByA,
                "Context A consumed a completion that context B registered. That is issue #120 itself: the task id "
                + "means nothing in A, so the resolve is dropped in silence and B's promise hangs.");
            Assert.GreaterOrEqual(examinedByA, 1,
                "Context A did not examine B's completion at all, so this fixture cannot tell 'A declined it because "
                + "it belongs to B' from 'A never had access to the queue'. Without that distinction the "
                + "cross-context test below is not measuring what it claims to.");
            Assert.GreaterOrEqual(QuickJSNative.GetPendingTaskCount(), 1,
                "B's completion is gone from the queue even though nobody processed it. Foreign completions must be "
                + "left for their owner, not dropped.");

            // And it is still the live entry B is waiting on, not a husk.
            Assert.AreEqual(1, QuickJSNative.ProcessCompletedTasks(_b),
                "Context B could not drain its own completion after A had examined and requeued it.");
        }

        /// <summary>
        /// Issue #120. A completion belongs to the context that registered it.
        ///
        /// Context B starts a C# task and holds the only JS promise for it.
        /// Context A ticks first, the way a second JSRunner's Update would. A
        /// takes the completion off the shared queue and resolves it against its
        /// own context, where the task id means nothing, and __resolveTask
        /// returns silently because a completion with no promise behind it is a
        /// legitimate case for C#-only tasks. By the time B ticks there is
        /// nothing left to take, and B's promise stays pending forever.
        ///
        /// EXPECTED TO FAIL until #120 is fixed. It fails on an assertion that
        /// says which context drained what, not on a timeout, because a hang
        /// proves only that something did not happen.
        /// </summary>
        [UnityTest]
        public IEnumerator ACompletionIsNotConsumedByAForeignContext() {
            _b.Eval(@"
                var __settled = false;
                var __result = null;
                CS.OneJS.Tests.AsyncTestHelper.DelayedMessageAsync('for B', 10)
                    .then(function (r) { __result = r; __settled = true; })
                    .catch(function (e) { __result = 'rejected: ' + e.message; __settled = true; });
            ");
            _b.ExecutePendingJobs();
            Assert.AreEqual("false", _b.Eval("__settled"),
                "B's promise settled before the task could have completed, so this test never reached its subject.");

            yield return WaitForQueuedCompletion();

            var queuedBeforeTick = QuickJSNative.GetPendingTaskCount();
            Assert.GreaterOrEqual(queuedBeforeTick, 1,
                $"B's task never reached the shared queue within {CompletionTimeout}s, so nothing was dequeued by "
                + "anyone and this run says nothing about where a completion goes. Fixture or environment, not #120.");

            // Both contexts are checked for life HERE, at the moment of the
            // dequeue, rather than in SetUp. A context that died in between would
            // produce the same red for an entirely different reason.
            Assert.AreEqual("1", _a.Eval("1"), "Context A is not alive at the point of the dequeue.");
            Assert.AreEqual("1", _b.Eval("1"), "Context B is not alive at the point of the dequeue.");

            // A ticks first, as the other runner's Update would.
            var drainedByA = QuickJSNative.ProcessCompletedTasks(_a);
            _a.ExecutePendingJobs();

            // Then B ticks, and finds whatever A left.
            var queuedAfterA = QuickJSNative.GetPendingTaskCount();
            var drainedByB = QuickJSNative.ProcessCompletedTasks(_b);
            _b.ExecutePendingJobs();

            // Both ticked, which is the other half of "both are live". A fixture
            // where only one context ever ticks cannot show this defect.
            Assert.AreEqual(1, drainedByA + drainedByB,
                $"Expected exactly one completion to be drained between the two contexts, saw {drainedByA} by A and "
                + $"{drainedByB} by B. The queue held {queuedBeforeTick} before the ticks. Something other than B's "
                + "task is in play, so treat this as a fixture problem rather than as #120.");

            var settled = _b.Eval("__settled");
            Assert.AreEqual("true", settled,
                "B registered the task and holds the only promise for it, but B's promise never settled.\n"
                + $"  context A drained {drainedByA}, context B drained {drainedByB}\n"
                + $"  queue held {queuedBeforeTick} before A ticked and {queuedAfterA} after\n"
                + "A took a completion it did not register and resolved it into its own context, where the task id "
                + "does not exist, and __resolveTask dropped it in silence. The completion is gone from the queue and "
                + "B's promise is pending forever. This is issue #120: a completion does not remember which context "
                + "registered it. Two JSRunners is a documented setup, so this reaches users.");

            Assert.AreEqual("for B", _b.Eval("__result"),
                "B's promise settled with the wrong value, which is a different defect from #120 and worth its own look.");
        }

        /// <summary>
        /// The control for the test above, and the reason its red can be read as
        /// #120 rather than as a broken fixture.
        ///
        /// Identical in every respect except one: context B ticks first instead
        /// of context A. Same two contexts, same C# task, same promise, same
        /// waits, same assertions. If this passes while the one above fails, the
        /// only variable between them is which context drained the completion,
        /// which is the claim.
        ///
        /// Without this, a red above would be consistent with the promise never
        /// settling for some entirely unrelated reason, and a fixture that can
        /// only ever produce red proves nothing.
        ///
        /// Expected to pass both before and after #120 is fixed.
        /// </summary>
        [UnityTest]
        public IEnumerator TheOwningContextSettlesItWhenItTicksFirst() {
            _b.Eval(@"
                var __settled = false;
                var __result = null;
                CS.OneJS.Tests.AsyncTestHelper.DelayedMessageAsync('for B', 10)
                    .then(function (r) { __result = r; __settled = true; })
                    .catch(function (e) { __result = 'rejected: ' + e.message; __settled = true; });
            ");
            _b.ExecutePendingJobs();

            yield return WaitForQueuedCompletion();

            var queuedBeforeTick = QuickJSNative.GetPendingTaskCount();
            Assert.GreaterOrEqual(queuedBeforeTick, 1,
                $"B's task never reached the shared queue within {CompletionTimeout}s, so this control says nothing.");

            Assert.AreEqual("1", _a.Eval("1"), "Context A is not alive at the point of the dequeue.");
            Assert.AreEqual("1", _b.Eval("1"), "Context B is not alive at the point of the dequeue.");

            // The one difference from the test above: B goes first.
            var drainedByB = QuickJSNative.ProcessCompletedTasks(_b);
            _b.ExecutePendingJobs();
            var drainedByA = QuickJSNative.ProcessCompletedTasks(_a);
            _a.ExecutePendingJobs();

            Assert.AreEqual(1, drainedByA + drainedByB,
                $"Expected exactly one completion between the two contexts, saw {drainedByA} by A and {drainedByB} by B.");

            Assert.AreEqual("true", _b.Eval("__settled"),
                $"B registered the task AND drained it first ({drainedByB} by B, {drainedByA} by A) and its promise "
                + "still did not settle. That breaks the reading of the failing test beside this one: the problem "
                + "would not be which context drains the completion, and #120 would be the wrong diagnosis.");
            Assert.AreEqual("for B", _b.Eval("__result"));
        }

        /// <summary>
        /// The other half of requeueing, and the reason the drain is bounded by
        /// entries EXAMINED rather than entries processed.
        ///
        /// Once a foreign completion goes back in the queue instead of being
        /// consumed, a budget counted in processed entries never terminates: a
        /// queue holding nothing but another context's completions dequeues and
        /// re-enqueues the same entries forever with the count stuck at zero.
        ///
        /// Read a hang here as that defect. This test cannot assert its way out
        /// of an infinite loop inside ProcessCompletedTasks, so the bound has to
        /// be observed from outside: the pass returns, nothing is lost, and the
        /// number of entries it looked at is smaller than the queue.
        /// </summary>
        [UnityTest]
        public IEnumerator AForeignOnlyQueueIsExaminedUnderABoundAndLosesNothing() {
            const int foreignCount = 120; // deliberately more than one tick's budget

            for (var i = 0; i < foreignCount; i++) {
                QuickJSNative.RegisterTask(_a, Task.CompletedTask);
            }
            yield return null;

            Assert.AreEqual(foreignCount, QuickJSNative.GetPendingTaskCount(),
                "The completions registered against context A did not all reach the queue, so the numbers below "
                + "describe a different queue from the one this test set up.");

            QuickJSNative.ResetTaskQueueMonitoring();

            // One pass by the context that owns none of it.
            var drainedByB = QuickJSNative.ProcessCompletedTasks(_b);
            var examinedByB = QuickJSNative.GetForeignCompletionCount();

            Assert.AreEqual(0, drainedByB, "Context B consumed completions registered by context A.");
            Assert.GreaterOrEqual(examinedByB, 1,
                "Context B examined nothing at all, so this test never reached its subject.");
            Assert.Less(examinedByB, foreignCount,
                $"Context B examined {examinedByB} of {foreignCount} entries in a single pass. The pass is supposed "
                + "to stop at a per-tick budget; walking the whole queue means a large backlog blocks the frame.");
            Assert.AreEqual(foreignCount, QuickJSNative.GetPendingTaskCount(),
                $"The queue holds {QuickJSNative.GetPendingTaskCount()} entries after B's pass, down from "
                + $"{foreignCount}. Foreign completions must be put back, not dropped: every one of these is a "
                + "promise context A is still waiting on.");

            // And A still gets all of them, across as many ticks as its budget needs.
            var drainedByA = 0;
            for (var tick = 0; tick < 10 && drainedByA < foreignCount; tick++) {
                drainedByA += QuickJSNative.ProcessCompletedTasks(_a);
                yield return null;
            }
            Assert.AreEqual(foreignCount, drainedByA,
                $"Context A recovered only {drainedByA} of its {foreignCount} completions after B had rotated them "
                + "through the queue.");
        }

        /// <summary>
        /// Issue #120's second half, raised by the reporter and sharpened in the
        /// issue thread: a busy runner must not starve a quiet one.
        ///
        /// Context A has a backlog several ticks deep. Context B has a single
        /// completion sitting behind all of it. B has to reach its own entry in a
        /// bounded number of ticks, which is what requeueing to the BACK buys:
        /// every pass moves B's entry closer to the front, so the wait is
        /// proportional to the backlog and not indefinite.
        ///
        /// The failure this guards against is an implementation that bounds the
        /// pass correctly but puts foreign entries back at the FRONT. That
        /// terminates, so it never hangs, and B never advances.
        /// </summary>
        [UnityTest]
        public IEnumerator AQuietContextReachesItsCompletionBehindABusyOnesBacklog() {
            const int foreignCount = 120;
            const int tickBudget = 8; // 120 entries at 50 examined per tick needs 3

            for (var i = 0; i < foreignCount; i++) {
                QuickJSNative.RegisterTask(_a, Task.CompletedTask);
            }

            // B's own task goes in behind the backlog, which is the arrangement
            // under test: B cannot see it without first getting past A's entries.
            _b.Eval(@"
                var __settled = false;
                var __result = null;
                CS.OneJS.Tests.AsyncTestHelper.DelayedMessageAsync('for B', 10)
                    .then(function (r) { __result = r; __settled = true; })
                    .catch(function (e) { __result = 'rejected: ' + e.message; __settled = true; });
            ");
            _b.ExecutePendingJobs();

            var deadline = Time.realtimeSinceStartup + CompletionTimeout;
            while (QuickJSNative.GetPendingTaskCount() <= foreignCount && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
            Assert.AreEqual(foreignCount + 1, QuickJSNative.GetPendingTaskCount(),
                $"Expected A's {foreignCount} completions plus B's one, saw {QuickJSNative.GetPendingTaskCount()}. "
                + "Fixture or environment, not the property under test.");

            var ticks = 0;
            while (_b.Eval("__settled") == "false" && ticks < tickBudget) {
                QuickJSNative.ProcessCompletedTasks(_b);
                ticks++;
                yield return null;
            }

            Assert.AreEqual("true", _b.Eval("__settled"),
                $"Context B ticked {ticks} times against a backlog of {foreignCount} completions belonging to context "
                + "A and never reached its own. A busy runner is starving a quiet one: foreign completions have to go "
                + "back at the END of the queue so each pass brings this context's own entries forward.");
            Assert.AreEqual("for B", _b.Eval("__result"),
                "B settled with the wrong value, so it picked up somebody else's completion on the way.");
            Assert.AreEqual(foreignCount, QuickJSNative.GetPendingTaskCount(),
                "Context A's backlog did not survive B's passes intact.");
        }

        /// <summary>
        /// Waits for a completion to reach the shared queue, or gives up.
        ///
        /// Bounded and frame based so a test can never hang: an unbounded wait
        /// here would turn every failure into an anonymous timeout, which is the
        /// shape that tells you least. Whether anything arrived is left to the
        /// caller to assert, because the right complaint differs per test.
        /// </summary>
        IEnumerator WaitForQueuedCompletion() {
            var deadline = Time.realtimeSinceStartup + CompletionTimeout;
            while (QuickJSNative.GetPendingTaskCount() == 0 && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }
        }
    }
}
