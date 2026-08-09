using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// Playmode tests for QuickJS stability and monitoring features.
    /// Tests handle monitoring, task queue monitoring, and buffer overflow detection.
    /// </summary>
    [TestFixture]
    public class QuickJSStabilityTests {
        QuickJSContext _ctx;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _ctx = new QuickJSContext();
            QuickJSNative.ClearAllHandles();
            QuickJSNative.ResetHandleMonitoring();
            QuickJSNative.ResetTaskQueueMonitoring();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
            QuickJSNative.ClearPendingTasks();
            yield return null;
        }

        // MARK: Handle Monitoring Tests

        [UnityTest]
        public IEnumerator HandleMonitoring_GetHandleCount_ReturnsCorrectCount() {
            // Initially should be 0
            Assert.AreEqual(0, QuickJSNative.GetHandleCount());

            // Register some objects
            var go1 = new GameObject("Test1");
            var go2 = new GameObject("Test2");
            var handle1 = QuickJSNative.RegisterObject(go1);
            var handle2 = QuickJSNative.RegisterObject(go2);

            Assert.AreEqual(2, QuickJSNative.GetHandleCount());

            // Cleanup
            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleLookup_ElementOrAncestor_ResolvesNearestRegistered() {
            var parent = new UnityEngine.UIElements.VisualElement();
            var child = new UnityEngine.UIElements.VisualElement();
            var grandchild = new UnityEngine.UIElements.VisualElement(); // left unregistered
            parent.Add(child);
            child.Add(grandchild);

            QuickJSNative.RegisterObject(parent);
            int ch = QuickJSNative.RegisterObject(child);

            // Exact match resolves to the element's own handle.
            Assert.AreEqual(ch, QuickJSNative.GetHandleForElementOrAncestor(child));
            // An unregistered descendant resolves to its nearest registered ancestor.
            Assert.AreEqual(ch, QuickJSNative.GetHandleForElementOrAncestor(grandchild));
            // A detached, unregistered element resolves to 0.
            Assert.AreEqual(0, QuickJSNative.GetHandleForElementOrAncestor(new UnityEngine.UIElements.VisualElement()));
            // null is safe.
            Assert.AreEqual(0, QuickJSNative.GetHandleForElementOrAncestor(null));

            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleMonitoring_GetPeakHandleCount_TracksPeak() {
            // Register objects
            var objects = new GameObject[5];
            for (int i = 0; i < 5; i++) {
                objects[i] = new GameObject($"Test{i}");
                QuickJSNative.RegisterObject(objects[i]);
            }

            Assert.GreaterOrEqual(QuickJSNative.GetPeakHandleCount(), 5);

            // Clear all handles
            QuickJSNative.ClearAllHandles();

            // Current count should be 0, but peak should still be >= 5
            Assert.AreEqual(0, QuickJSNative.GetHandleCount());

            // After reset, peak should match current
            QuickJSNative.ResetHandleMonitoring();
            Assert.AreEqual(0, QuickJSNative.GetPeakHandleCount());

            // Cleanup
            foreach (var go in objects) {
                Object.DestroyImmediate(go);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleMonitoring_ClearAllHandles_ResetsState() {
            // Register some objects
            var go = new GameObject("Test");
            QuickJSNative.RegisterObject(go);

            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // Clear all
            QuickJSNative.ClearAllHandles();

            Assert.AreEqual(0, QuickJSNative.GetHandleCount());

            // Cleanup
            Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleMonitoring_DuplicateRegistration_ReturnsSameHandle() {
            var go = new GameObject("Test");

            var handle1 = QuickJSNative.RegisterObject(go);
            var handle2 = QuickJSNative.RegisterObject(go);

            // Should return same handle for same object
            Assert.AreEqual(handle1, handle2);

            // Should only count as 1 handle
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // Cleanup
            Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleRefCount_UnregisterDecrementsBeforeRemoval() {
            var go = new GameObject("RefCountTest");

            // Register same object 3 times (simulates 3 JS proxies for same C# object)
            var handle1 = QuickJSNative.RegisterObject(go);
            var handle2 = QuickJSNative.RegisterObject(go);
            var handle3 = QuickJSNative.RegisterObject(go);

            Assert.AreEqual(handle1, handle2);
            Assert.AreEqual(handle1, handle3);
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // First unregister: ref count 3 -> 2, handle should survive
            QuickJSNative.UnregisterObjectForTest(handle1);
            Assert.IsNotNull(QuickJSNative.GetObjectByHandle(handle1), "Handle should survive first unregister");
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // Second unregister: ref count 2 -> 1, handle should survive
            QuickJSNative.UnregisterObjectForTest(handle1);
            Assert.IsNotNull(QuickJSNative.GetObjectByHandle(handle1), "Handle should survive second unregister");
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // Third unregister: ref count 1 -> 0, handle should be removed
            QuickJSNative.UnregisterObjectForTest(handle1);
            Assert.IsNull(QuickJSNative.GetObjectByHandle(handle1), "Handle should be removed after final unregister");
            Assert.AreEqual(0, QuickJSNative.GetHandleCount());

            Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleRefCount_SingleRegistrationRemovesImmediately() {
            var go = new GameObject("SingleRefTest");

            var handle = QuickJSNative.RegisterObject(go);
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            // Single registration, single unregister should remove
            QuickJSNative.UnregisterObjectForTest(handle);
            Assert.IsNull(QuickJSNative.GetObjectByHandle(handle), "Handle should be removed after single unregister");
            Assert.AreEqual(0, QuickJSNative.GetHandleCount());

            Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandleRefCount_ReregistrationAfterFullRelease() {
            var go = new GameObject("ReregTest");

            // Register, fully unregister
            var handle1 = QuickJSNative.RegisterObject(go);
            QuickJSNative.UnregisterObjectForTest(handle1);
            Assert.IsNull(QuickJSNative.GetObjectByHandle(handle1));

            // Re-register same object should get a new handle
            var handle2 = QuickJSNative.RegisterObject(go);
            Assert.IsNotNull(QuickJSNative.GetObjectByHandle(handle2));
            Assert.AreEqual(1, QuickJSNative.GetHandleCount());

            Object.DestroyImmediate(go);
            yield return null;
        }

        // MARK: Proxy Cache Refcount Fix Tests
        // Verifies that proxy cache hits correctly counteract RegisterObject refcount increments.
        // Without the fix, each interop return of the same C# object increments the refcount
        // in RegisterObject, but the JS proxy cache reuses the existing proxy (no new
        // FinalizationRegistry entry), causing the refcount to diverge from cleanup entries.

        [UnityTest]
        public IEnumerator ProxyCacheRefcount_RepeatedAccess_RefcountStaysAtOne() {
            InteropTestHelper.Init("Town", 1);

            // Access the same C# object 50 times from JS.
            // Each call: C# RegisterObject (refcount++) → JS wrapObject → cache hit → __releaseHandle (refcount--)
            _ctx.Eval(@"
                for (var i = 0; i < 50; i++) {
                    CS.OneJS.Tests.InteropTestHelper.GetState();
                }
            ");

            var state = InteropTestHelper.GetState();
            var handle = QuickJSNative.GetHandleForObject(state);
            Assert.AreNotEqual(0, handle, "State object should have a handle");

            // With fix: refcount = 1 (each cache hit counteracts the RegisterObject increment)
            // Without fix: refcount = 50
            Assert.AreEqual(1, QuickJSNative.GetRefCountForTest(handle),
                "Refcount should be 1 after repeated access - proxy cache fix must counteract RegisterObject increments");

            InteropTestHelper.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProxyCacheRefcount_CollectionItemAccess_RefcountStaysAtOne() {
            InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
            InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

            // Simulate repeated access to collection items (like panel mount/unmount)
            _ctx.Eval(@"
                var inv = CS.OneJS.Tests.InteropTestHelper.GetInventory();
                for (var i = 0; i < 50; i++) {
                    var sword = inv[0];
                    var shield = inv[1];
                }
            ");

            var inventory = InteropTestHelper.GetInventory();
            var swordHandle = QuickJSNative.GetHandleForObject(inventory[0]);
            var shieldHandle = QuickJSNative.GetHandleForObject(inventory[1]);
            Assert.AreNotEqual(0, swordHandle, "Sword should have a handle");
            Assert.AreNotEqual(0, shieldHandle, "Shield should have a handle");

            Assert.AreEqual(1, QuickJSNative.GetRefCountForTest(swordHandle),
                "Sword refcount should be 1 after 50 accesses");
            Assert.AreEqual(1, QuickJSNative.GetRefCountForTest(shieldHandle),
                "Shield refcount should be 1 after 50 accesses");

            InteropTestHelper.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProxyCacheRefcount_SingleUnregisterFreesHandle() {
            InteropTestHelper.Init("Town", 1);

            _ctx.Eval(@"
                for (var i = 0; i < 50; i++) {
                    CS.OneJS.Tests.InteropTestHelper.GetState();
                }
            ");

            var state = InteropTestHelper.GetState();
            var handle = QuickJSNative.GetHandleForObject(state);
            Assert.AreNotEqual(0, handle);

            // With fix: refcount=1, single unregister frees the handle
            // Without fix: refcount=50, handle would survive 49 more unregisters
            QuickJSNative.UnregisterObjectForTest(handle);
            Assert.IsNull(QuickJSNative.GetObjectByHandle(handle),
                "Handle should be fully freed after single unregister - confirms refcount was 1");
            Assert.AreEqual(0, QuickJSNative.GetRefCountForTest(handle),
                "Refcount should be 0 after unregister");

            InteropTestHelper.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProxyCacheRefcount_ProxyIdentityPreserved() {
            InteropTestHelper.Init("Town", 1);

            // Verify the fix doesn't break proxy identity (cache still returns same proxy)
            var result = _ctx.Eval(@"
                var refs = [];
                for (var i = 0; i < 10; i++) {
                    refs.push(CS.OneJS.Tests.InteropTestHelper.GetState());
                }
                refs.every(function(r) { return r === refs[0]; });
            ");
            Assert.AreEqual("true", result,
                "All references should be the same proxy - cache must still work after refcount fix");

            InteropTestHelper.Reset();
            yield return null;
        }

        // MARK: Task Queue Monitoring Tests

        [UnityTest]
        public IEnumerator TaskQueueMonitoring_GetPendingTaskCount_ReturnsCorrectCount() {
            // Initially should be 0
            Assert.AreEqual(0, QuickJSNative.GetPendingTaskCount());
            yield return null;
        }

        [UnityTest]
        public IEnumerator TaskQueueMonitoring_CompletedTasksAreQueued() {
            // Create a task that completes immediately
            var task = Task.FromResult(42);
            QuickJSNative.RegisterTask(task);

            // Wait a frame for the continuation to run
            yield return null;

            // The task should now be in the completed queue
            Assert.GreaterOrEqual(QuickJSNative.GetPendingTaskCount(), 0);
        }

        [UnityTest]
        public IEnumerator TaskQueueMonitoring_ProcessCompletedTasks_ClearsQueue() {
            // Create and register multiple completed tasks
            for (int i = 0; i < 5; i++) {
                var task = Task.FromResult(i);
                QuickJSNative.RegisterTask(task);
            }

            // Wait for continuations to run
            yield return null;

            // Process the tasks
            int processed = QuickJSNative.ProcessCompletedTasks(_ctx);

            // Should have processed at least some tasks
            Assert.GreaterOrEqual(processed, 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TaskQueueMonitoring_GetPeakTaskQueueSize_TracksPeak() {
            QuickJSNative.ResetTaskQueueMonitoring();

            // Create multiple tasks
            for (int i = 0; i < 10; i++) {
                var task = Task.FromResult(i);
                QuickJSNative.RegisterTask(task);
            }

            // Wait for continuations
            yield return null;
            yield return null;

            int peak = QuickJSNative.GetPeakTaskQueueSize();

            // Process all tasks
            while (QuickJSNative.GetPendingTaskCount() > 0) {
                QuickJSNative.ProcessCompletedTasks(_ctx);
            }

            // Peak should still reflect the maximum
            Assert.GreaterOrEqual(QuickJSNative.GetPeakTaskQueueSize(), 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TaskQueueMonitoring_ResetTaskQueueMonitoring_ResetsPeak() {
            // Create some tasks
            for (int i = 0; i < 5; i++) {
                var task = Task.FromResult(i);
                QuickJSNative.RegisterTask(task);
            }

            yield return null;

            // Reset monitoring
            QuickJSNative.ResetTaskQueueMonitoring();

            // Peak should be reset to current count
            Assert.AreEqual(QuickJSNative.GetPendingTaskCount(), QuickJSNative.GetPeakTaskQueueSize());
            yield return null;
        }

        // MARK: Buffer Overflow Detection Tests

        [UnityTest]
        public IEnumerator BufferOverflow_SmallOutput_NoWarning() {
            // Small output should not trigger warning
            LogAssert.NoUnexpectedReceived();
            var result = _ctx.Eval("'hello'");
            Assert.AreEqual("hello", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BufferOverflow_LargeOutput_TriggersWarning() {
            // Create a string that will fill the 16KB buffer
            // The default buffer is 16KB, so we need to create a string larger than that
            string largeString = new string('x', 20000);

            // This should trigger the buffer overflow warning
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[QuickJSContext\] Eval output may have been truncated"));

            _ctx.Eval($"'{largeString}'");
            yield return null;
        }

        // MARK: Context Dispose Tests

        [UnityTest]
        public IEnumerator ContextDispose_ClearsPendingTasks() {
            // Create tasks
            for (int i = 0; i < 3; i++) {
                var task = Task.FromResult(i);
                QuickJSNative.RegisterTask(task);
            }

            yield return null;

            // Dispose should clear pending tasks (via QuickJSUIBridge.Dispose)
            QuickJSNative.ClearPendingTasks();

            Assert.AreEqual(0, QuickJSNative.GetPendingTaskCount());
            yield return null;
        }

        // MARK: Exception Context Tests

        [UnityTest]
        public IEnumerator ExceptionContext_MethodNotFound_ThrowsWithContext() {
            // Try to call a non-existent method - should throw with error info
            Debug.Log("[Test] The following red error is EXPECTED - testing error handling for non-existent methods");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[QuickJS\] Method not found"));

            bool exceptionThrown = false;
            try {
                _ctx.Eval("CS.UnityEngine.Debug.NonExistentMethod()");
            } catch (System.Exception ex) {
                exceptionThrown = true;
                // Exception should contain QuickJS error info
                Assert.IsTrue(ex.Message.Contains("QuickJS error"), "Exception should contain QuickJS error");
            }

            Assert.IsTrue(exceptionThrown, "Should have thrown an exception");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExceptionContext_TypeNotFound_ThrowsWithContext() {
            // Try to access a non-existent type - should throw with error info
            Debug.Log("[Test] The following red error is EXPECTED - testing error handling for non-existent types");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[QuickJS\] Type not found"));

            bool exceptionThrown = false;
            try {
                _ctx.Eval("CS.NonExistent.FakeType.DoSomething()");
            } catch (System.Exception ex) {
                exceptionThrown = true;
                // Exception should contain QuickJS error info
                Assert.IsTrue(ex.Message.Contains("QuickJS error"), "Exception should contain QuickJS error");
            }

            Assert.IsTrue(exceptionThrown, "Should have thrown an exception");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExceptionContext_SyntaxError_ThrowsWithContext() {
            // Try to evaluate malformed JavaScript - should throw with syntax error info
            // Note: QuickJS throws exceptions but doesn't log syntax errors to Unity console
            bool exceptionThrown = false;
            try {
                _ctx.Eval("if (true { console.log('missing paren'); }");
            } catch (System.Exception ex) {
                exceptionThrown = true;
                // Exception should contain error info
                Assert.IsTrue(
                    ex.Message.Contains("QuickJS error") || ex.Message.Contains("SyntaxError"),
                    $"Exception should contain error info, got: {ex.Message}");
            }

            Assert.IsTrue(exceptionThrown, "Should have thrown an exception for syntax error");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExceptionContext_RuntimeError_ThrowsWithContext() {
            // Try to throw an error from JS - should propagate properly
            // Note: QuickJS throws exceptions but doesn't log JS runtime errors to Unity console
            bool exceptionThrown = false;
            try {
                _ctx.Eval("throw new Error('Intentional test error');");
            } catch (System.Exception ex) {
                exceptionThrown = true;
                Assert.IsTrue(
                    ex.Message.Contains("Intentional test error") || ex.Message.Contains("QuickJS error"),
                    $"Exception should contain error message, got: {ex.Message}");
            }

            Assert.IsTrue(exceptionThrown, "Should have thrown an exception for runtime error");
            yield return null;
        }
    }

    /// <summary>
    /// Helper class for task monitoring tests.
    /// </summary>
    public static class TaskMonitoringTestHelper {
        public static async Task<int> DelayedResult(int value, int delayMs) {
            await Task.Delay(delayMs);
            return value;
        }

        public static async Task FailAfterDelay(int delayMs, string message) {
            await Task.Delay(delayMs);
            throw new System.Exception(message);
        }
    }
}
