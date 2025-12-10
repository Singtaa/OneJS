using UnityEngine;

// MARK: Main
public class QuickJSTest : MonoBehaviour {
    QuickJSContext _ctx;
    int _passed;
    int _failed;

    void Awake() {
        _ctx = new QuickJSContext();
    }

    void Start() {
        Log("=== QuickJS Test Suite ===");

        RunBasicEvalTests();
        RunStaticCallTests();
        RunGameObjectInteropTests();
        RunCallbackTests();

        LogSummary();
    }

    void OnDestroy() {
        Log($"Final handle count: {QuickJSNative.GetHandleCount()}");
        _ctx?.Dispose();
        _ctx = null;
        QuickJSNative.ClearAllHandles();
    }

    // MARK: Eval
    void RunBasicEvalTests() {
        Log("--- Basic Eval Tests ---");

        Assert("Simple arithmetic",
            _ctx.Eval("1 + 2"), "3");

        Assert("Function definition and call",
            _ctx.Eval("function add(a, b) { return a + b; } add(10, 32);"), "42");

        Assert("Console.log works",
            () => _ctx.Eval("console.log('hello from JS');"));
    }

    // MARK: Static
    void RunStaticCallTests() {
        Log("--- Static Call Tests ---");

        Assert("Debug.Log via callStatic",
            () => { _ctx.Eval("__csHelpers.callStatic('UnityEngine.Debug', 'Log', 'via callStatic');"); });

        Assert("Debug.Log via CS proxy", () => { _ctx.Eval("CS.UnityEngine.Debug.Log('via CS proxy');"); });

        Assert("Time.deltaTime access", () => {
            var result = _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            return float.TryParse(result, out _);
        });

        Assert("Time.frameCount access", () => {
            var result = _ctx.Eval("CS.UnityEngine.Time.frameCount");
            return int.TryParse(result, out var fc) && fc >= 0;
        });
    }

    // MARK: Interop
    void RunGameObjectInteropTests() {
        Log("--- GameObject Interop Tests ---");

        var handlesBefore = QuickJSNative.GetHandleCount();

        Assert("Create GameObject from JS", () => {
            _ctx.Eval(@"
                var testGo = new CS.UnityEngine.GameObject('QuickJSTestObject');
                testGo.SetActive(false);
                CS.UnityEngine.Object.Destroy(testGo);
                testGo.release();
            ");
        });

        Assert("Create and manipulate Vector3", () => {
            _ctx.Eval(@"
                var v = new CS.UnityEngine.Vector3(1, 2, 3);
                console.log('Vector3:', v);
            ");
        });

        Assert("Set transform.position", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('PositionTest');
                var pos = new CS.UnityEngine.Vector3(10, 20, 30);
                go.transform.position = pos;
            ");
            // Verify the position was actually set
            var go = GameObject.Find("PositionTest");
            if (go == null) {
                throw new System.Exception("GameObject not found");
            }
            var pos = go.transform.position;
            if (Mathf.Abs(pos.x - 10) > 0.001f || Mathf.Abs(pos.y - 20) > 0.001f || Mathf.Abs(pos.z - 30) > 0.001f) {
                throw new System.Exception($"Position mismatch: expected (10,20,30), got ({pos.x},{pos.y},{pos.z})");
            }
            // Cleanup
            _ctx.Eval("CS.UnityEngine.Object.Destroy(CS.UnityEngine.GameObject.Find('PositionTest'));");
        });

        _ctx.RunGC();
        var handlesAfter = QuickJSNative.GetHandleCount();
        Log($"  Handles: {handlesBefore} -> {handlesAfter}");
    }

    // MARK: Callbacks
    void RunCallbackTests() {
        Log("--- Callback Tests ---");

        Assert("Numeric callback", () => {
            var handle = int.Parse(_ctx.Eval(@"
                __registerCallback(function(x, y) { return x + y; });
            "));
            var result = _ctx.InvokeCallback(handle, 10, 20);
            return result is int i && i == 30;
        });

        Assert("String callback", () => {
            var handle = int.Parse(_ctx.Eval(@"
                __registerCallback(function(name) { return 'Hello, ' + name + '!'; });
            "));
            var result = _ctx.InvokeCallback(handle, "World");
            return result is string s && s == "Hello, World!";
        });

        Assert("Callback with object return (JSON)", () => {
            var handle = int.Parse(_ctx.Eval(@"
                __registerCallback(function(multiplier) {
                    return JSON.stringify({ value: 42 * multiplier, label: 'computed' });
                });
            "));
            var result = _ctx.InvokeCallback(handle, 2);
            return result is string s && s.Contains("84");
        });
    }

    // MARK: Helpers
    void Assert(string name, string actual, string expected) {
        if (actual == expected) {
            _passed++;
            Log($"  [PASS] {name}");
        } else {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: expected '{expected}', got '{actual}'");
        }
    }

    void Assert(string name, System.Action action) {
        try {
            action();
            _passed++;
            Log($"  [PASS] {name}");
        } catch (System.Exception ex) {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: {ex.Message}");
        }
    }

    void Assert(string name, System.Func<bool> predicate) {
        try {
            if (predicate()) {
                _passed++;
                Log($"  [PASS] {name}");
            } else {
                _failed++;
                Debug.LogError($"  [FAIL] {name}: assertion failed");
            }
        } catch (System.Exception ex) {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: {ex.Message}");
        }
    }

    void Log(string msg) => Debug.Log($"[QuickJS] {msg}");

    void LogSummary() {
        var total = _passed + _failed;
        var status = _failed == 0 ? "ALL PASSED" : $"{_failed} FAILED";
        Log($"=== Results: {_passed}/{total} passed ({status}) ===");
    }
}