#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Ultra-minimal test to isolate zero-alloc issues.
/// Tests ONLY Time.deltaTime which should hit the fast path.
/// Editor-only: excluded from builds.
/// </summary>
public class QuickJSProfilerMinimal : MonoBehaviour {
    QuickJSContext _ctx;
    int _tickHandle;

    static readonly CustomSampler _sampler = CustomSampler.Create("JS MinimalTick");

    void Start() {
        _ctx = new QuickJSContext();

        // Enable debug logging to see fast path hits/misses
        QuickJSNative.DebugFastPath = true;

        // Test 1: Just read Time.deltaTime - should be zero-alloc
        _tickHandle = int.Parse(_ctx.Eval(@"
            var t = 0;
            __registerCallback(function() {
                var dt = CS.UnityEngine.Time.deltaTime;
                t += dt;
                cube.transform.position = { 
                    x: Math.sin(t * 2) * 3, 
                    y: Math.cos(t * 3) * 2, 
                    z: Math.sin(t * 1.5) * 2.5 
                };
            });
        "));

        Debug.Log($"[MinimalTest] FastPath count: {QuickJSNative.FastPath.Count}");

        // Verify Time is registered
        var timeType = typeof(Time);
        Debug.Log($"[MinimalTest] Time type hash: {timeType.GetHashCode()}");
        Debug.Log($"[MinimalTest] Time fullname: {timeType.FullName}");

        // Disable after first few frames to reduce noise
        Invoke("DisableDebug", 0.5f);
    }

    void DisableDebug() {
        QuickJSNative.DebugFastPath = false;
        Debug.Log("[MinimalTest] Debug logging disabled");
    }

    void Update() {
        _sampler.Begin();
        _ctx.InvokeCallbackNoAlloc(_tickHandle);
        _sampler.End();
    }

    void OnDestroy() {
        _ctx?.Dispose();
        QuickJSNative.ClearAllHandles();
    }

    void OnGUI() {
        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label("Minimal Fast Path Test", GUI.skin.box);
        GUILayout.Label($"Handles: {QuickJSNative.GetHandleCount()}");
        GUILayout.Label($"FastPaths: {QuickJSNative.FastPath.Count}");
        GUILayout.Label("Check 'JS MinimalTick' in Profiler");
        GUILayout.EndArea();
    }
}
#endif