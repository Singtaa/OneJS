using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

/// <summary>
/// Playmode tests for QuickJS zero-allocation fast path interop.
/// Tests correctness, allocation behavior, and performance.
/// </summary>
[TestFixture]
public class QuickJSFastPathPlaymodeTests {
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

    // MARK: Correctness Tests

    [UnityTest]
    public IEnumerator FastPath_Count_GreaterThanZero() {
        // Trigger initialization
        _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        Assert.Greater(QuickJSNative.FastPath.Count, 0);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_TimeDeltaTime_ReturnsNonNegativeFloat() {
        var result = _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        Assert.IsTrue(float.TryParse(result, out var dt) && dt >= 0, $"Expected non-negative float, got: {result}");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_TimeFrameCount_ReturnsNonNegativeInt() {
        var result = _ctx.Eval("CS.UnityEngine.Time.frameCount");
        Assert.IsTrue(int.TryParse(result, out var fc) && fc >= 0, $"Expected non-negative int, got: {result}");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_TimeTimeScale_GetSetWorks() {
        float original = Time.timeScale;
        try {
            _ctx.Eval("CS.UnityEngine.Time.timeScale = 0.5");
            Assert.AreEqual(0.5f, Time.timeScale, 0.001f);
        } finally {
            Time.timeScale = original;
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_TransformPosition_SetWorks() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('FastPathPosTest');
            go.transform.position = { x: 11, y: 22, z: 33 };
        ");

        var go = GameObject.Find("FastPathPosTest");
        Assert.IsNotNull(go);

        var pos = go.transform.position;
        Assert.AreEqual(11f, pos.x, 0.01f);
        Assert.AreEqual(22f, pos.y, 0.01f);
        Assert.AreEqual(33f, pos.z, 0.01f);

        UnityEngine.Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_TransformLocalScale_SetWorks() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('FastPathScaleTest');
            go.transform.localScale = { x: 2, y: 3, z: 4 };
        ");

        var go = GameObject.Find("FastPathScaleTest");
        Assert.IsNotNull(go);

        var scale = go.transform.localScale;
        Assert.AreEqual(2f, scale.x, 0.01f);
        Assert.AreEqual(3f, scale.y, 0.01f);
        Assert.AreEqual(4f, scale.z, 0.01f);

        UnityEngine.Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_GameObjectName_GetWorks() {
        var result = _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('NameTest');
            var name = go.name;
            CS.UnityEngine.Object.Destroy(go);
            name;
        ");
        Assert.AreEqual("NameTest", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_GameObjectSetActive_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('ActiveTest');
            go.SetActive(false);
            globalThis.__activeGo = go;
        ");

        var result = _ctx.Eval("globalThis.__activeGo.activeSelf");
        Assert.AreEqual("false", result);

        _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__activeGo)");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_ScreenWidth_ReturnsPositiveInt() {
        var result = _ctx.Eval("CS.UnityEngine.Screen.width");
        Assert.IsTrue(int.TryParse(result, out var w) && w > 0, $"Expected positive int, got: {result}");
        yield return null;
    }

    // MARK: Allocation Tests

    [UnityTest]
    public IEnumerator FastPath_PropertyGet_LowAllocation() {
        // Warm up
        for (int i = 0; i < 100; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetTotalMemory(false);

        for (int i = 0; i < 1000; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        }

        GC.Collect();
        long bytes = GC.GetTotalMemory(false) - before;

        Debug.Log($"FastPath property get: ~{bytes} bytes for 1000 calls (~{bytes / 1000} per call)");
        Assert.Less(bytes, 50000, "Allocation should be low for fast path");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_PropertySet_LowAllocation() {
        float original = Time.timeScale;
        try {
            // Warm up
            for (int i = 0; i < 100; i++) {
                _ctx.Eval("CS.UnityEngine.Time.timeScale = 1.0");
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < 1000; i++) {
                _ctx.Eval("CS.UnityEngine.Time.timeScale = 1.0");
            }

            GC.Collect();
            long bytes = GC.GetTotalMemory(false) - before;

            Debug.Log($"FastPath property set: ~{bytes} bytes for 1000 calls (~{bytes / 1000} per call)");
            Assert.Less(bytes, 50000, "Allocation should be low for fast path");
        } finally {
            Time.timeScale = original;
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_Registry_ContainsExpectedEntries() {
        // Trigger some initialization
        _ctx.Eval("CS.UnityEngine.Time.deltaTime");

        int count = QuickJSNative.FastPath.Count;
        Debug.Log($"Registered fast paths: {count}");

        Assert.GreaterOrEqual(count, 30, "Should have Time, Transform, GameObject, Screen, Mathf entries");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FastPath_RepeatedCalls_ConsistentLowOverhead() {
        // Warm up
        for (int i = 0; i < 100; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        }

        var times = new long[5];
        for (int batch = 0; batch < 5; batch++) {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            }
            sw.Stop();
            times[batch] = sw.ElapsedMilliseconds;
        }

        long min = times[0], max = times[0];
        foreach (var t in times) {
            if (t < min) min = t;
            if (t > max) max = t;
        }

        Debug.Log($"5 batches of 1000 calls: min={min}ms, max={max}ms, variance={max - min}ms");
        Assert.Less(max - min, 50, "Variance should be low if no GC pressure");
        yield return null;
    }

    // MARK: Performance Tests

    [UnityTest]
    public IEnumerator Performance_TransformPropertyAccess_ReasonableTime() {
        _ctx.Eval(@"
            globalThis.__perfGo = new CS.UnityEngine.GameObject('PerfTest');
            globalThis.__perfTr = globalThis.__perfGo.transform;
        ");

        const int iterations = 10000;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) {
            _ctx.Eval("globalThis.__perfTr.position");
        }
        sw.Stop();

        Debug.Log($"Transform.position: {sw.ElapsedMilliseconds}ms for {iterations} calls");
        Debug.Log($"{iterations * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):F0} calls/sec");

        _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__perfGo)");

        Assert.Less(sw.ElapsedMilliseconds, 10000, "Should complete in reasonable time");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Performance_PerFrameSimulation_Works() {
        _ctx.Eval(@"
            globalThis.__simGo = new CS.UnityEngine.GameObject('SimTest');
            globalThis.__simTr = globalThis.__simGo.transform;
            globalThis.__simTr.position = { x: 0, y: 0, z: 0 };
        ");

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) {
            _ctx.Eval(@"
                var dt = CS.UnityEngine.Time.deltaTime;
                var pos = globalThis.__simTr.position;
                globalThis.__simTr.position = { x: pos.x + dt, y: pos.y, z: pos.z };
            ");
        }
        sw.Stop();

        var go = GameObject.Find("SimTest");
        float finalX = go != null ? go.transform.position.x : 0;
        UnityEngine.Object.Destroy(go);

        Debug.Log($"1000 frame simulation: {sw.ElapsedMilliseconds}ms");
        Debug.Log($"Final X position: {finalX:F4}");

        Assert.Less(sw.ElapsedMilliseconds, 5000);
        Assert.Greater(finalX, 0);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Performance_FastPathVsReflection_Comparison() {
        const int iterations = 10000;

        // Warm up
        for (int i = 0; i < 100; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            _ctx.Eval("CS.UnityEngine.Application.platform");
        }

        // Fast path (Time.deltaTime is registered)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        }
        sw.Stop();
        long fastPathMs = sw.ElapsedMilliseconds;

        // Reflection path (Application.platform is NOT registered)
        sw.Restart();
        for (int i = 0; i < iterations; i++) {
            _ctx.Eval("CS.UnityEngine.Application.platform");
        }
        sw.Stop();
        long reflectionMs = sw.ElapsedMilliseconds;

        Debug.Log($"FastPath: {fastPathMs}ms, Reflection: {reflectionMs}ms for {iterations} calls");

        if (fastPathMs < reflectionMs) {
            Debug.Log($"FastPath is {(double)reflectionMs / Math.Max(1, fastPathMs):F1}x faster");
        } else {
            Debug.Log("Difference within noise margin (both paths are fast when cached)");
        }

        // This is informational - always pass
        Assert.Pass("Performance comparison logged");
        yield return null;
    }
}

