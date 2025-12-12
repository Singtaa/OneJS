using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Tests for QuickJS zero-allocation fast path interop.
/// Verifies correctness and measures allocation behavior.
/// </summary>
public class QuickJSFastPathTest : MonoBehaviour {
    QuickJSContext _ctx;
    int _passed;
    int _failed;

    void Awake() {
        _ctx = new QuickJSContext();
    }

    void Start() {
        Log("=== QuickJS Fast Path Tests ===");

        RunCorrectnessTests();
        RunAllocationTests();
        RunPerformanceComparison();

        LogSummary();
    }

    void OnDestroy() {
        Log($"Final handle count: {QuickJSNative.GetHandleCount()}");
        _ctx?.Dispose();
        _ctx = null;
        QuickJSNative.ClearAllHandles();
    }

    // MARK: Correctness
    void RunCorrectnessTests() {
        Log("--- Correctness Tests ---");

        Assert("Fast path count > 0", () => {
            // Trigger initialization
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            return QuickJSNative.FastPath.Count > 0;
        });

        Assert("Time.deltaTime via fast path", () => {
            var result = _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            return float.TryParse(result, out var dt) && dt >= 0;
        });

        Assert("Time.frameCount via fast path", () => {
            var result = _ctx.Eval("CS.UnityEngine.Time.frameCount");
            return int.TryParse(result, out var fc) && fc >= 0;
        });

        Assert("Time.timeScale get/set via fast path", () => {
            float original = Time.timeScale;
            _ctx.Eval("CS.UnityEngine.Time.timeScale = 0.5");
            bool setWorked = Mathf.Abs(Time.timeScale - 0.5f) < 0.001f;
            Time.timeScale = original;
            return setWorked;
        });

        Assert("Transform.position via fast path", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('FastPathPosTest');
                go.transform.position = { x: 11, y: 22, z: 33 };
            ");
            var go = GameObject.Find("FastPathPosTest");
            if (go == null) return false;
            var pos = go.transform.position;
            UnityEngine.Object.Destroy(go);
            return Mathf.Abs(pos.x - 11) < 0.01f &&
                   Mathf.Abs(pos.y - 22) < 0.01f &&
                   Mathf.Abs(pos.z - 33) < 0.01f;
        });

        Assert("Transform.localScale via fast path", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('FastPathScaleTest');
                go.transform.localScale = { x: 2, y: 3, z: 4 };
            ");
            var go = GameObject.Find("FastPathScaleTest");
            if (go == null) return false;
            var scale = go.transform.localScale;
            UnityEngine.Object.Destroy(go);
            return Mathf.Abs(scale.x - 2) < 0.01f &&
                   Mathf.Abs(scale.y - 3) < 0.01f &&
                   Mathf.Abs(scale.z - 4) < 0.01f;
        });

        Assert("GameObject.name via fast path", () => {
            var result = _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('NameTest');
                var name = go.name;
                CS.UnityEngine.Object.Destroy(go);
                name;
            ");
            return result == "NameTest";
        });

        Assert("GameObject.SetActive via fast path", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('ActiveTest');
                go.SetActive(false);
                globalThis.__activeGo = go;
            ");
            var go = GameObject.Find("ActiveTest");
            // Find returns null for inactive objects, so check a different way
            var result = _ctx.Eval("globalThis.__activeGo.activeSelf");
            _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__activeGo)");
            return result == "false";
        });

        Assert("Screen.width via fast path", () => {
            var result = _ctx.Eval("CS.UnityEngine.Screen.width");
            return int.TryParse(result, out var w) && w > 0;
        });

        // Skip Input test - depends on legacy input being enabled
        // If you need Input System support, register those APIs separately

        _ctx.RunGC();
    }

    // MARK: Allocation
    void RunAllocationTests() {
        Log("--- Allocation Tests ---");

        // Warm up - ensure JIT and caches are primed
        for (int i = 0; i < 100; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert("Fast path property get - low allocation", () => {
            return MeasureAllocation("CS.UnityEngine.Time.deltaTime", 1000, out long bytes) && bytes < 50000;
            // Allow some allocation for string interning, etc. but should be much less than reflection path
        });

        Assert("Fast path property set - low allocation", () => {
            float original = Time.timeScale;
            bool result = MeasureAllocation("CS.UnityEngine.Time.timeScale = 1.0", 1000, out long bytes) &&
                          bytes < 50000;
            Time.timeScale = original;
            return result;
        });

        // Compare against an unregistered path (will use reflection)
        // Note: GC.GetTotalMemory is imprecise for small allocations
        // Instead, we verify fast path is used by checking registration count
        Assert("Fast path registry contains expected entries", () => {
            int count = QuickJSNative.FastPath.Count;
            Log($"    Registered fast paths: {count}");
            // Should have Time, Transform, GameObject, Screen, Mathf entries
            return count >= 30;
        });

        Assert("Repeated calls show consistent low overhead", () => {
            // Run many iterations and check timing is consistent
            // (allocation would cause GC pauses and timing variance)
            var times = new long[5];
            for (int batch = 0; batch < 5; batch++) {
                var sw = System.Diagnostics.Stopwatch.StartNew();
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

            Log($"    5 batches of 1000 calls: min={min}ms, max={max}ms, variance={max - min}ms");
            // Variance should be low if no GC pressure
            return (max - min) < 50; // Allow 50ms variance
        });
    }

    bool MeasureAllocation(string code, int iterations, out long bytesAllocated) {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetTotalMemory(false);

        for (int i = 0; i < iterations; i++) {
            _ctx.Eval(code);
        }

        GC.Collect();
        bytesAllocated = GC.GetTotalMemory(false) - before;

        Log(
            $"    {code}: ~{bytesAllocated} bytes for {iterations} calls (~{bytesAllocated / iterations} per call)");
        return true;
    }

    // MARK: Performance
    void RunPerformanceComparison() {
        Log("--- Performance Comparison ---");

        const int iterations = 10000;

        // Warm up
        for (int i = 0; i < 100; i++) {
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            _ctx.Eval("CS.UnityEngine.Application.platform");
        }

        // Note: Fast path speed advantage over cached reflection is marginal
        // The real benefit is zero allocation, not raw speed
        // This test logs the comparison but doesn't fail on small differences
        {
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

            Log($"    FastPath: {fastPathMs}ms, Reflection: {reflectionMs}ms for {iterations} calls");

            if (fastPathMs < reflectionMs) {
                Log($"    FastPath is {(double)reflectionMs / Math.Max(1, fastPathMs):F1}x faster");
            } else {
                Log($"    Difference within noise margin (both paths are fast when cached)");
            }
            _passed++; // Always pass - this is informational
            Log("  [PASS] Performance comparison logged");
        }

        Assert("Transform property access performance", () => {
            _ctx.Eval(@"
                globalThis.__perfGo = new CS.UnityEngine.GameObject('PerfTest');
                globalThis.__perfTr = globalThis.__perfGo.transform;
            ");

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                _ctx.Eval("globalThis.__perfTr.position");
            }
            sw.Stop();

            Log($"    Transform.position: {sw.ElapsedMilliseconds}ms for {iterations} calls");
            Log($"    {iterations * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):F0} calls/sec");

            _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__perfGo)");

            return sw.ElapsedMilliseconds < 10000; // Should complete in reasonable time
        });

        Assert("Per-frame simulation (deltaTime + position)", () => {
            _ctx.Eval(@"
                globalThis.__simGo = new CS.UnityEngine.GameObject('SimTest');
                globalThis.__simTr = globalThis.__simGo.transform;
                globalThis.__simTr.position = { x: 0, y: 0, z: 0 };
            ");

            // Simulate 1000 frames of: read deltaTime, update position
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

            Log($"    1000 frame simulation: {sw.ElapsedMilliseconds}ms");
            Log($"    Final X position: {finalX:F4}");

            return sw.ElapsedMilliseconds < 5000 && finalX > 0;
        });

        _ctx.RunGC();
    }

    // MARK: Helpers
    void Assert(string name, Func<bool> predicate) {
        try {
            if (predicate()) {
                _passed++;
                Log($"  [PASS] {name}");
            } else {
                _failed++;
                Debug.LogError($"  [FAIL] {name}: assertion failed");
            }
        } catch (Exception ex) {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: {ex.Message}");
        }
    }

    void Log(string msg) => Debug.Log($"[FastPath] {msg}");

    void LogSummary() {
        var total = _passed + _failed;
        var status = _failed == 0 ? "ALL PASSED" : $"{_failed} FAILED";
        Log($"=== Results: {_passed}/{total} passed ({status}) ===");
        Log($"Fast path registrations: {QuickJSNative.FastPath.Count}");
    }
}