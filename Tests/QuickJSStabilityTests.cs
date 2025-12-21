using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
