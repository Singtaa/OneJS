#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Zero-allocation profiler test for development.
/// Exercises: Time.deltaTime (static) + transform.position get/set (instance + Vector3)
/// Expected: 0 B allocation per frame in Update()
/// Editor-only: excluded from builds.
/// </summary>
public class QuickJSZeroAllocProfilerTest : MonoBehaviour {
    QuickJSContext _ctx;
    int _tickHandle;

    static readonly CustomSampler _sampler = CustomSampler.Create("JS Tick");

    void Start() {
        _ctx = new QuickJSContext();

        // Create test cube if needed
        var cube = GameObject.Find("Cube");
        if (cube == null) {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube";
        }

        // Register tick callback - exercises full fast path:
        // - CS.UnityEngine.Time.deltaTime (static property, returns float)
        // - transform.position (instance property, returns Vector3)
        // - transform.position = {...} (instance property set, accepts Vector3 from plain object)
        _tickHandle = int.Parse(_ctx.Eval(@"
            var cube = CS.UnityEngine.GameObject.Find('Cube');
            var transform = cube.transform;
            var t = 0;
            
            __registerCallback(function() {
                var dt = CS.UnityEngine.Time.deltaTime;
                t += dt;
                
                transform.position = { 
                    x: Math.sin(t * 2) * 3, 
                    y: Math.cos(t * 3) * 2, 
                    z: Math.sin(t * 1.5) * 2.5 
                };
            });
        "));

        Debug.Log($"[ZeroAlloc] Setup complete. FastPaths: {QuickJSNative.FastPath.Count}");
    }

    void Update() {
        _sampler.Begin();
        _ctx.InvokeCallbackNoAlloc(_tickHandle);
        _sampler.End();
    }

    void OnDestroy() {
        Debug.Log($"[ZeroAlloc] Final handles: {QuickJSNative.GetHandleCount()}");
        _ctx?.Dispose();
        QuickJSNative.ClearAllHandles();
    }

    void OnGUI() {
        GUILayout.BeginArea(new Rect(10, 10, 300, 120));
        GUILayout.Label("Zero-Alloc Interop Test", GUI.skin.box);
        GUILayout.Label($"Handles: {QuickJSNative.GetHandleCount()}");
        GUILayout.Label($"FastPaths: {QuickJSNative.FastPath.Count}");
        GUILayout.Label("");
        GUILayout.Label("Profiler > 'JS Tick' should show 0 B");
        GUILayout.EndArea();
    }
}
#endif