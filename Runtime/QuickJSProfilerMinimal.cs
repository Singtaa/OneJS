using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Minimal per-frame test. Attach to a cube and watch it orbit.
/// Check Profiler > CPU > "JS Fast Path" and "JS Reflection" samples.
/// </summary>
public class QuickJSProfilerMinimal : MonoBehaviour {
    QuickJSContext _ctx;
    int _transformHandle;

    CustomSampler _fastPathSampler;
    CustomSampler _reflectionSampler;

    void Start() {
        _ctx = new QuickJSContext();
        _fastPathSampler = CustomSampler.Create("JS Fast Path");
        _reflectionSampler = CustomSampler.Create("JS Reflection");

        // Register this transform for JS access
        var method = typeof(QuickJSNative).GetMethod("RegisterObject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        _transformHandle = (int)method.Invoke(null, new object[] { transform });

        // Store handle in JS
        _ctx.Eval($"globalThis.tr = __csHelpers.wrapObject('UnityEngine.Transform', {_transformHandle});");

        Debug.Log("[Profiler] Use Deep Profile mode for allocation tracking");
    }

    void Update() {
        // FAST PATH - should show 0 B allocation
        _fastPathSampler.Begin();
        _ctx.Eval(@"
            var t = CS.UnityEngine.Time.time;
            tr.position = { x: Math.cos(t) * 3, y: 0, z: Math.sin(t) * 3 };
        ");
        _fastPathSampler.End();

        // REFLECTION PATH - will show allocations
        _reflectionSampler.Begin();
        _ctx.Eval("CS.UnityEngine.Application.productName");
        _reflectionSampler.End();
    }

    void OnDestroy() {
        _ctx?.Dispose();
        QuickJSNative.ClearAllHandles();
    }
}