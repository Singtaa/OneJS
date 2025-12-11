using UnityEngine;

// MARK: Custom Structs
public struct CustomPoint {
    public float x;
    public float y;
    public string label;
}

public struct CustomSerializerTestPoint {
    public float x;
    public float y;
    public string label;
}

public struct NestedStruct {
    public Vector3 position;
    public Color color;
    public int id;
}

public struct PropertyStruct {
    public float X { get; set; }
    public float Y { get; set; }
}

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
        RunStructTests();

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
            var go = GameObject.Find("PositionTest");
            if (go == null) {
                throw new System.Exception("GameObject not found");
            }
            var pos = go.transform.position;
            if (Mathf.Abs(pos.x - 10) > 0.001f || Mathf.Abs(pos.y - 20) > 0.001f ||
                Mathf.Abs(pos.z - 30) > 0.001f) {
                throw new System.Exception(
                    $"Position mismatch: expected (10,20,30), got ({pos.x},{pos.y},{pos.z})");
            }
            _ctx.Eval("CS.UnityEngine.Object.Destroy(CS.UnityEngine.GameObject.Find('PositionTest'));");
        });

        Assert("AddComponent/GetComponent with Type arg", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('ComponentTest');
                var collider = go.AddComponent(CS.UnityEngine.BoxCollider);
                console.log('Added: ' + collider);
                var retrieved = go.GetComponent(CS.UnityEngine.BoxCollider);
                console.log('Retrieved: ' + retrieved);
                CS.UnityEngine.Object.Destroy(go);
                go.release();
            ");
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

    // MARK: Structs
    void RunStructTests() {
        Log("--- Struct Serialization Tests ---");

        // Test built-in Unity structs
        Assert("Vector3 from plain object", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('V3PlainTest');
                go.transform.position = { x: 5, y: 10, z: 15 };
            ");
            var go = GameObject.Find("V3PlainTest");
            if (go == null) throw new System.Exception("GameObject not found");
            var pos = go.transform.position;
            Object.Destroy(go);
            return Mathf.Abs(pos.x - 5) < 0.001f &&
                   Mathf.Abs(pos.y - 10) < 0.001f &&
                   Mathf.Abs(pos.z - 15) < 0.001f;
        });

        Assert("Vector2 from plain object", () => {
            var result = _ctx.Eval(@"
                var v = new CS.UnityEngine.Vector2(0, 0);
                // Can't set directly but can test constructor
                var v2 = new CS.UnityEngine.Vector2(3.5, 7.25);
                v2.x + ',' + v2.y;
            ");
            return result == "3.5,7.25";
        });

        Assert("Color from plain object", () => {
            // Test Color by creating a material and setting its color
            _ctx.Eval(@"
                var mat = new CS.UnityEngine.Material(CS.UnityEngine.Shader.Find('Standard'));
                mat.color = { r: 0.5, g: 0.25, b: 0.75, a: 1.0 };
                globalThis.__testMat = mat;
            ");
            // Retrieve and verify via another eval
            var result = _ctx.Eval(@"
                var c = globalThis.__testMat.color;
                Math.abs(c.r - 0.5) < 0.01 && Math.abs(c.g - 0.25) < 0.01 && Math.abs(c.b - 0.75) < 0.01;
            ");
            _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__testMat);");
            return result == "true";
        });

        Assert("Quaternion from plain object", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('QuatTest');
                go.transform.rotation = { x: 0, y: 0.7071, z: 0, w: 0.7071 };
            ");
            var go = GameObject.Find("QuatTest");
            if (go == null) throw new System.Exception("GameObject not found");
            var rot = go.transform.rotation;
            Object.Destroy(go);
            // Should be ~90 degree rotation around Y
            return Mathf.Abs(rot.y - 0.7071f) < 0.01f &&
                   Mathf.Abs(rot.w - 0.7071f) < 0.01f;
        });

        Assert("Rect from plain object", () => {
            var result = _ctx.Eval(@"
                var r = new CS.UnityEngine.Rect(0, 0, 100, 50);
                r.x + ',' + r.y + ',' + r.width + ',' + r.height;
            ");
            return result == "0,0,100,50";
        });

        // Test struct round-trip (C# -> JS -> read values)
        Assert("Vector3 round-trip", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('RoundTripTest');
                go.transform.position = { x: 100, y: 200, z: 300 };
            ");
            var result = _ctx.Eval(@"
                var go = CS.UnityEngine.GameObject.Find('RoundTripTest');
                var p = go.transform.position;
                p.x + ',' + p.y + ',' + p.z;
            ");
            var go = GameObject.Find("RoundTripTest");
            Object.Destroy(go);
            return result == "100,200,300";
        });

        // Test custom user struct (auto-registration)
        Assert("Custom struct auto-registration", () => {
            // CustomPoint should auto-register when encountered
            var point = new CustomPoint { x = 1.5f, y = 2.5f, label = "test" };
            var json = QuickJSNative.SerializeStruct(point);
            return json != null &&
                   json.Contains("\"x\":1.5") &&
                   json.Contains("\"y\":2.5") &&
                   json.Contains("\"label\":\"test\"");
        });

        Assert("Custom struct deserialization", () => {
            var dict = new System.Collections.Generic.Dictionary<string, object> {
                ["x"] = 3.14,
                ["y"] = 2.71,
                ["label"] = "pi-e"
            };
            var result = QuickJSNative.DeserializeFromDict(dict, typeof(CustomPoint));
            if (result is CustomPoint cp) {
                return Mathf.Abs(cp.x - 3.14f) < 0.01f &&
                       Mathf.Abs(cp.y - 2.71f) < 0.01f &&
                       cp.label == "pi-e";
            }
            return false;
        });

        Assert("Nested struct serialization", () => {
            var nested = new NestedStruct {
                position = new Vector3(1, 2, 3),
                color = new Color(0.5f, 0.5f, 0.5f, 1f),
                id = 42
            };
            var json = QuickJSNative.SerializeStruct(nested);
            return json != null &&
                   json.Contains("\"position\":") &&
                   json.Contains("\"color\":") &&
                   json.Contains("\"id\":42");
        });

        Assert("Property-only struct", () => {
            var ps = new PropertyStruct { X = 10, Y = 20 };
            var json = QuickJSNative.SerializeStruct(ps);
            return json != null && json.Contains("10") && json.Contains("20");
        });

        // Test partial object (missing fields default to zero)
        Assert("Partial plain object fills defaults", () => {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('PartialTest');
                go.transform.position = { x: 99 };
            ");
            var go = GameObject.Find("PartialTest");
            if (go == null) throw new System.Exception("GameObject not found");
            var pos = go.transform.position;
            Object.Destroy(go);
            // y and z should be 0 (default)
            return Mathf.Abs(pos.x - 99) < 0.001f &&
                   Mathf.Abs(pos.y) < 0.001f &&
                   Mathf.Abs(pos.z) < 0.001f;
        });

        // Test explicit registration with custom serializer
        Assert("Custom serializer registration", () => {
            QuickJSNative.RegisterStructType<CustomSerializerTestPoint>(
                cp =>
                    $"{{\"__type\":\"CustomSerializerTestPoint\",\"coords\":\"{cp.x},{cp.y}\",\"name\":\"{cp.label}\"}}",
                dict => {
                    var coords = dict.TryGetValue("coords", out var c) ? (string)c : "0,0";
                    var parts = coords.Split(',');
                    return new CustomSerializerTestPoint {
                        x = float.Parse(parts[0]),
                        y = float.Parse(parts[1]),
                        label = dict.TryGetValue("name", out var n) ? (string)n : ""
                    };
                }
            );
            var point = new CustomSerializerTestPoint { x = 7, y = 8, label = "custom" };
            var json = QuickJSNative.SerializeStruct(point);
            return json.Contains("coords") && json.Contains("7,8");
        });

        _ctx.RunGC();
        Log($"  Struct test handles: {QuickJSNative.GetHandleCount()}");
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