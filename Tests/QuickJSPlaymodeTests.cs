using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// MARK: Async Test Helpers
public static class AsyncTestHelper {
    /// <summary>
    /// Simple async method that completes immediately with an int result.
    /// </summary>
    public static Task<int> GetValueAsync(int value) {
        return Task.FromResult(value);
    }

    /// <summary>
    /// Async method that delays for specified milliseconds then returns a string.
    /// </summary>
    public static async Task<string> DelayedMessageAsync(string message, int delayMs) {
        await Task.Delay(delayMs);
        return message;
    }

    /// <summary>
    /// Async method with no return value (Task, not Task<T>).
    /// </summary>
    public static async Task DoWorkAsync(int delayMs) {
        await Task.Delay(delayMs);
    }

    /// <summary>
    /// Async method that throws an exception.
    /// </summary>
    public static async Task<int> FailingAsync(string errorMessage) {
        await Task.Delay(10);
        throw new System.Exception(errorMessage);
    }

    /// <summary>
    /// Async method that returns a complex object.
    /// </summary>
    public static Task<GameObject> CreateGameObjectAsync(string name) {
        var go = new GameObject(name);
        return Task.FromResult(go);
    }
}

// MARK: Custom Structs for Tests
public struct TestCustomPoint {
    public float x;
    public float y;
    public string label;
}

public struct TestCustomSerializerPoint {
    public float x;
    public float y;
    public string label;
}

public struct TestNestedStruct {
    public Vector3 position;
    public Color color;
    public int id;
}

public struct TestPropertyStruct {
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>
/// Playmode tests for QuickJS core functionality.
/// Tests basic eval, static calls, GameObject interop, callbacks, and struct serialization.
/// </summary>
[TestFixture]
public class QuickJSPlaymodeTests {
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

    // MARK: Basic Eval Tests

    [UnityTest]
    public IEnumerator Eval_SimpleArithmetic_ReturnsCorrectResult() {
        var result = _ctx.Eval("1 + 2");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Eval_FunctionDefinitionAndCall_Works() {
        var result = _ctx.Eval("function add(a, b) { return a + b; } add(10, 32);");
        Assert.AreEqual("42", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Eval_ConsoleLog_DoesNotThrow() {
        Assert.DoesNotThrow(() => _ctx.Eval("console.log('hello from JS');"));
        yield return null;
    }

    // MARK: Static Call Tests

    [UnityTest]
    public IEnumerator Static_DebugLogViaCallStatic_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval("__csHelpers.callStatic('UnityEngine.Debug', 'Log', 'via callStatic');");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Static_DebugLogViaCSProxy_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval("CS.UnityEngine.Debug.Log('via CS proxy');");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Static_TimeDeltaTime_ReturnsFloat() {
        var result = _ctx.Eval("CS.UnityEngine.Time.deltaTime");
        Assert.IsTrue(float.TryParse(result, out _), $"Expected float, got: {result}");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Static_TimeFrameCount_ReturnsNonNegativeInt() {
        var result = _ctx.Eval("CS.UnityEngine.Time.frameCount");
        Assert.IsTrue(int.TryParse(result, out var fc) && fc >= 0, $"Expected non-negative int, got: {result}");
        yield return null;
    }

    // MARK: GameObject Interop Tests

    [UnityTest]
    public IEnumerator Interop_CreateGameObjectFromJS_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var testGo = new CS.UnityEngine.GameObject('QuickJSTestObject');
                testGo.SetActive(false);
                CS.UnityEngine.Object.Destroy(testGo);
                testGo.release();
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Interop_CreateAndManipulateVector3_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var v = new CS.UnityEngine.Vector3(1, 2, 3);
                console.log('Vector3:', v);
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Interop_SetTransformPosition_UpdatesPosition() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('PositionTest');
            var pos = new CS.UnityEngine.Vector3(10, 20, 30);
            go.transform.position = pos;
        ");

        var go = GameObject.Find("PositionTest");
        Assert.IsNotNull(go, "GameObject not found");

        var pos = go.transform.position;
        Assert.AreEqual(10f, pos.x, 0.001f);
        Assert.AreEqual(20f, pos.y, 0.001f);
        Assert.AreEqual(30f, pos.z, 0.001f);

        _ctx.Eval("CS.UnityEngine.Object.Destroy(CS.UnityEngine.GameObject.Find('PositionTest'));");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Interop_AddComponentGetComponent_Works() {
        Assert.DoesNotThrow(() => {
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
        yield return null;
    }

    // MARK: Callback Tests

    [UnityTest]
    public IEnumerator Callback_NumericCallback_ReturnsSum() {
        var handle = int.Parse(_ctx.Eval(@"
            __registerCallback(function(x, y) { return x + y; });
        "));

        var result = _ctx.InvokeCallback(handle, 10, 20);
        Assert.IsInstanceOf<int>(result);
        Assert.AreEqual(30, result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Callback_StringCallback_ReturnsConcatenation() {
        var handle = int.Parse(_ctx.Eval(@"
            __registerCallback(function(name) { return 'Hello, ' + name + '!'; });
        "));

        var result = _ctx.InvokeCallback(handle, "World");
        Assert.IsInstanceOf<string>(result);
        Assert.AreEqual("Hello, World!", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Callback_ObjectReturn_ReturnsJSON() {
        var handle = int.Parse(_ctx.Eval(@"
            __registerCallback(function(multiplier) {
                return JSON.stringify({ value: 42 * multiplier, label: 'computed' });
            });
        "));

        var result = _ctx.InvokeCallback(handle, 2);
        Assert.IsInstanceOf<string>(result);
        StringAssert.Contains("84", (string)result);
        yield return null;
    }

    // MARK: Struct Serialization Tests

    [UnityTest]
    public IEnumerator Struct_Vector3FromPlainObject_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('V3PlainTest');
            go.transform.position = { x: 5, y: 10, z: 15 };
        ");

        var go = GameObject.Find("V3PlainTest");
        Assert.IsNotNull(go, "GameObject not found");

        var pos = go.transform.position;
        Assert.AreEqual(5f, pos.x, 0.001f);
        Assert.AreEqual(10f, pos.y, 0.001f);
        Assert.AreEqual(15f, pos.z, 0.001f);

        Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_Vector2Constructor_Works() {
        var result = _ctx.Eval(@"
            var v2 = new CS.UnityEngine.Vector2(3.5, 7.25);
            v2.x + ',' + v2.y;
        ");
        Assert.AreEqual("3.5,7.25", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_ColorFromPlainObject_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('ColorTest');
            var light = go.AddComponent(CS.UnityEngine.Light);
            light.color = { r: 0.5, g: 0.25, b: 0.75, a: 1.0 };
        ");

        var go = GameObject.Find("ColorTest");
        Assert.IsNotNull(go, "GameObject not found");

        var light = go.GetComponent<Light>();
        Assert.IsNotNull(light, "Light not found");

        var c = light.color;
        Assert.AreEqual(0.5f, c.r, 0.01f);
        Assert.AreEqual(0.25f, c.g, 0.01f);
        Assert.AreEqual(0.75f, c.b, 0.01f);

        Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_QuaternionFromPlainObject_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('QuatTest');
            go.transform.rotation = { x: 0, y: 0.7071, z: 0, w: 0.7071 };
        ");

        var go = GameObject.Find("QuatTest");
        Assert.IsNotNull(go, "GameObject not found");

        var rot = go.transform.rotation;
        Assert.AreEqual(0.7071f, rot.y, 0.01f);
        Assert.AreEqual(0.7071f, rot.w, 0.01f);

        Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_RectConstructor_Works() {
        var result = _ctx.Eval(@"
            var r = new CS.UnityEngine.Rect(0, 0, 100, 50);
            r.x + ',' + r.y + ',' + r.width + ',' + r.height;
        ");
        Assert.AreEqual("0,0,100,50", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_Vector3RoundTrip_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('RoundTripTest');
            go.transform.position = { x: 100, y: 200, z: 300 };
        ");

        var result = _ctx.Eval(@"
            var go = CS.UnityEngine.GameObject.Find('RoundTripTest');
            var p = go.transform.position;
            p.x + ',' + p.y + ',' + p.z;
        ");

        Assert.AreEqual("100,200,300", result);

        var go = GameObject.Find("RoundTripTest");
        Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_CustomStructAutoRegistration_Works() {
        var point = new TestCustomPoint { x = 1.5f, y = 2.5f, label = "test" };
        var json = QuickJSNative.SerializeStruct(point);

        Assert.IsNotNull(json);
        StringAssert.Contains("\"x\":1.5", json);
        StringAssert.Contains("\"y\":2.5", json);
        StringAssert.Contains("\"label\":\"test\"", json);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_CustomStructDeserialization_Works() {
        var dict = new System.Collections.Generic.Dictionary<string, object> {
            ["x"] = 3.14,
            ["y"] = 2.71,
            ["label"] = "pi-e"
        };

        var result = QuickJSNative.DeserializeFromDict(dict, typeof(TestCustomPoint));
        Assert.IsInstanceOf<TestCustomPoint>(result);

        var cp = (TestCustomPoint)result;
        Assert.AreEqual(3.14f, cp.x, 0.01f);
        Assert.AreEqual(2.71f, cp.y, 0.01f);
        Assert.AreEqual("pi-e", cp.label);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_NestedStructSerialization_Works() {
        var nested = new TestNestedStruct {
            position = new Vector3(1, 2, 3),
            color = new Color(0.5f, 0.5f, 0.5f, 1f),
            id = 42
        };

        var json = QuickJSNative.SerializeStruct(nested);
        Assert.IsNotNull(json);
        StringAssert.Contains("\"position\":", json);
        StringAssert.Contains("\"color\":", json);
        StringAssert.Contains("\"id\":42", json);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_PropertyOnlyStruct_Works() {
        var ps = new TestPropertyStruct { X = 10, Y = 20 };
        var json = QuickJSNative.SerializeStruct(ps);

        Assert.IsNotNull(json);
        StringAssert.Contains("10", json);
        StringAssert.Contains("20", json);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_PartialPlainObjectFillsDefaults_Works() {
        _ctx.Eval(@"
            var go = new CS.UnityEngine.GameObject('PartialTest');
            go.transform.position = { x: 99 };
        ");

        var go = GameObject.Find("PartialTest");
        Assert.IsNotNull(go, "GameObject not found");

        var pos = go.transform.position;
        Assert.AreEqual(99f, pos.x, 0.001f);
        Assert.AreEqual(0f, pos.y, 0.001f);
        Assert.AreEqual(0f, pos.z, 0.001f);

        Object.Destroy(go);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Struct_CustomSerializerRegistration_Works() {
        QuickJSNative.RegisterStructType<TestCustomSerializerPoint>(
            cp => $"{{\"__type\":\"TestCustomSerializerPoint\",\"coords\":\"{cp.x},{cp.y}\",\"name\":\"{cp.label}\"}}",
            dict => {
                var coords = dict.TryGetValue("coords", out var c) ? (string)c : "0,0";
                var parts = coords.Split(',');
                return new TestCustomSerializerPoint {
                    x = float.Parse(parts[0]),
                    y = float.Parse(parts[1]),
                    label = dict.TryGetValue("name", out var n) ? (string)n : ""
                };
            }
        );

        var point = new TestCustomSerializerPoint { x = 7, y = 8, label = "custom" };
        var json = QuickJSNative.SerializeStruct(point);

        StringAssert.Contains("coords", json);
        StringAssert.Contains("7,8", json);
        yield return null;
    }

    // MARK: Generic Type Tests

    [UnityTest]
    public IEnumerator Generics_ListInt_CreateAndAdd_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(10);
            list.Add(20);
            list.Add(30);
            list.Count;
        ");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Generics_ListString_CreateAndAdd_Works() {
        var result = _ctx.Eval(@"
            var StringList = CS.System.Collections.Generic.List(CS.System.String);
            var list = new StringList();
            list.Add('hello');
            list.Add('world');
            list.get_Item(0) + ' ' + list.get_Item(1);
        ");
        Assert.AreEqual("hello world", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Generics_Dictionary_CreateAndAdd_Works() {
        var result = _ctx.Eval(@"
            var StringIntDict = CS.System.Collections.Generic.Dictionary(CS.System.String, CS.System.Int32);
            var dict = new StringIntDict();
            dict.Add('one', 1);
            dict.Add('two', 2);
            dict.get_Item('one') + dict.get_Item('two');
        ");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Generics_ListVector3_Works() {
        _ctx.Eval(@"
            var Vector3List = CS.System.Collections.Generic.List(CS.UnityEngine.Vector3);
            var list = new Vector3List();
            list.Add(new CS.UnityEngine.Vector3(1, 2, 3));
            list.Add(new CS.UnityEngine.Vector3(4, 5, 6));
        ");

        var result = _ctx.Eval("list.Count");
        Assert.AreEqual("2", result);

        var first = _ctx.Eval("list.get_Item(0).x");
        Assert.AreEqual("1", first);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Generics_BoundTypeIsCallable_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(42);
            list.get_Item(0);
        ");
        Assert.AreEqual("42", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Generics_HashSet_Works() {
        var result = _ctx.Eval(@"
            var IntSet = CS.System.Collections.Generic.HashSet(CS.System.Int32);
            var set = new IntSet();
            set.Add(1);
            set.Add(2);
            set.Add(1);  // duplicate
            set.Count;
        ");
        Assert.AreEqual("2", result);
        yield return null;
    }

    // MARK: Indexer Tests

    [UnityTest]
    public IEnumerator Indexer_ListInt_GetByIndex_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(100);
            list.Add(200);
            list.Add(300);
            list[0] + ',' + list[1] + ',' + list[2];
        ");
        Assert.AreEqual("100,200,300", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_ListInt_SetByIndex_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(0);
            list.Add(0);
            list.Add(0);
            list[0] = 111;
            list[1] = 222;
            list[2] = 333;
            list[0] + ',' + list[1] + ',' + list[2];
        ");
        Assert.AreEqual("111,222,333", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_ListString_GetSetByIndex_Works() {
        var result = _ctx.Eval(@"
            var StringList = CS.System.Collections.Generic.List(CS.System.String);
            var list = new StringList();
            list.Add('a');
            list.Add('b');
            list[0] = 'hello';
            list[1] = 'world';
            list[0] + ' ' + list[1];
        ");
        Assert.AreEqual("hello world", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_ListGameObject_Works() {
        _ctx.Eval(@"
            var GOList = CS.System.Collections.Generic.List(CS.UnityEngine.GameObject);
            var list = new GOList();
            var go1 = new CS.UnityEngine.GameObject('IndexerTestGO1');
            var go2 = new CS.UnityEngine.GameObject('IndexerTestGO2');
            list.Add(go1);
            list.Add(go2);
        ");

        var result = _ctx.Eval("list[0].name + ',' + list[1].name");
        Assert.AreEqual("IndexerTestGO1,IndexerTestGO2", result);

        // Cleanup
        _ctx.Eval(@"
            CS.UnityEngine.Object.Destroy(list[0]);
            CS.UnityEngine.Object.Destroy(list[1]);
        ");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_ListVector3_Works() {
        var result = _ctx.Eval(@"
            var V3List = CS.System.Collections.Generic.List(CS.UnityEngine.Vector3);
            var list = new V3List();
            list.Add(new CS.UnityEngine.Vector3(1, 2, 3));
            list.Add(new CS.UnityEngine.Vector3(4, 5, 6));
            var first = list[0];
            var second = list[1];
            first.x + ',' + first.y + ',' + second.z;
        ");
        Assert.AreEqual("1,2,6", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_WithLoop_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            for (var i = 0; i < 5; i++) {
                list.Add(i * 10);
            }
            var sum = 0;
            for (var i = 0; i < list.Count; i++) {
                sum += list[i];
            }
            sum;
        ");
        Assert.AreEqual("100", result); // 0 + 10 + 20 + 30 + 40 = 100
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_CountAndLengthStillWork_Works() {
        // Ensure that Count property still works alongside indexer
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(1);
            list.Add(2);
            list.Add(3);
            'count=' + list.Count + ',first=' + list[0] + ',last=' + list[list.Count - 1];
        ");
        Assert.AreEqual("count=3,first=1,last=3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Indexer_ModifyInPlace_Works() {
        var result = _ctx.Eval(@"
            var IntList = CS.System.Collections.Generic.List(CS.System.Int32);
            var list = new IntList();
            list.Add(10);
            list[0] = list[0] * 2;  // Double the value
            list[0];
        ");
        Assert.AreEqual("20", result);
        yield return null;
    }

    // MARK: Async/Await Tests (C# Task to JS Promise)

    [UnityTest]
    public IEnumerator Async_ImmediateTaskInt_ReturnsDirectly() {
        // Task.FromResult returns an already-completed task
        // For already-completed tasks, we return the result directly (not a Promise)
        var result = _ctx.Eval(@"
            CS.AsyncTestHelper.GetValueAsync(42);
        ");
        Assert.AreEqual("42", result, "Result should be 42");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_DelayedTaskString_ResolvesAfterDelay() {
        // Call a C# async method that completes after a delay
        _ctx.Eval(@"
            var __asyncResult = null;
            var __asyncDone = false;
            var promise = CS.AsyncTestHelper.DelayedMessageAsync('Hello Async', 50);
            promise.then(function(result) {
                __asyncResult = result;
                __asyncDone = true;
            });
        ");
        _ctx.ExecutePendingJobs();

        // Wait for the Task to complete
        yield return new WaitForSeconds(0.1f);

        // Process completed tasks
        QuickJSNative.ProcessCompletedTasks(_ctx);
        _ctx.ExecutePendingJobs();

        var done = _ctx.Eval("__asyncDone");
        Assert.AreEqual("true", done, "Promise should have resolved");

        var result = _ctx.Eval("__asyncResult");
        Assert.AreEqual("Hello Async", result, "Result should match the message");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_TaskVoid_ResolvesWithNull() {
        // Call a C# async method with no return value
        _ctx.Eval(@"
            var __asyncResult = 'not_set';
            var __asyncDone = false;
            var promise = CS.AsyncTestHelper.DoWorkAsync(50);
            promise.then(function(result) {
                __asyncResult = result;
                __asyncDone = true;
            });
        ");
        _ctx.ExecutePendingJobs();

        // Wait for the Task to complete
        yield return new WaitForSeconds(0.1f);

        // Process completed tasks
        QuickJSNative.ProcessCompletedTasks(_ctx);
        _ctx.ExecutePendingJobs();

        var done = _ctx.Eval("__asyncDone");
        Assert.AreEqual("true", done, "Promise should have resolved");

        var result = _ctx.Eval("__asyncResult");
        Assert.AreEqual("null", result, "Task without return value should resolve to null");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_FailingTask_Rejects() {
        // Call a C# async method that throws an exception
        _ctx.Eval(@"
            var __asyncError = null;
            var __asyncDone = false;
            var promise = CS.AsyncTestHelper.FailingAsync('Test error message');
            promise.then(function(result) {
                __asyncDone = true;
            }).catch(function(error) {
                __asyncError = error.message;
                __asyncDone = true;
            });
        ");
        _ctx.ExecutePendingJobs();

        // Wait for the Task to fail
        yield return new WaitForSeconds(0.1f);

        // Process completed tasks
        QuickJSNative.ProcessCompletedTasks(_ctx);
        _ctx.ExecutePendingJobs();

        var done = _ctx.Eval("__asyncDone");
        Assert.AreEqual("true", done, "Promise should have settled");

        var error = _ctx.Eval("__asyncError");
        Assert.IsTrue(error.Contains("Test error message"), $"Error should contain the message, got: {error}");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_TaskReturnsGameObject_WrapsAsHandle() {
        // Task.FromResult returns an already-completed task
        // For already-completed tasks, we return the result directly (not a Promise)
        // This is more efficient and matches synchronous behavior
        _ctx.Eval(@"
            var result = CS.AsyncTestHelper.CreateGameObjectAsync('AsyncTestGO');
        ");
        _ctx.ExecutePendingJobs();

        var name = _ctx.Eval("result.name");
        Assert.AreEqual("AsyncTestGO", name, "Should be able to access GameObject properties directly");

        // Cleanup
        _ctx.Eval("CS.UnityEngine.Object.Destroy(result)");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_AwaitSyntax_Works() {
        // Test using async/await syntax in JS (wrapped in async IIFE)
        _ctx.Eval(@"
            var __asyncResult = null;
            var __asyncDone = false;
            (async function() {
                try {
                    var result = await CS.AsyncTestHelper.GetValueAsync(100);
                    __asyncResult = result;
                    __asyncDone = true;
                } catch (e) {
                    __asyncResult = 'error: ' + e.message;
                    __asyncDone = true;
                }
            })();
        ");
        _ctx.ExecutePendingJobs();

        // Process completed tasks
        QuickJSNative.ProcessCompletedTasks(_ctx);
        _ctx.ExecutePendingJobs();

        var done = _ctx.Eval("__asyncDone");
        Assert.AreEqual("true", done, "Async function should have completed");

        var result = _ctx.Eval("__asyncResult");
        Assert.AreEqual("100", result, "Result should be 100");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Async_MultipleAwaits_Work() {
        // Test chaining multiple async calls
        _ctx.Eval(@"
            var __asyncResult = null;
            var __asyncDone = false;
            (async function() {
                var a = await CS.AsyncTestHelper.GetValueAsync(10);
                var b = await CS.AsyncTestHelper.GetValueAsync(20);
                var c = await CS.AsyncTestHelper.GetValueAsync(30);
                __asyncResult = a + b + c;
                __asyncDone = true;
            })();
        ");
        _ctx.ExecutePendingJobs();

        // Process tasks multiple times (one per await)
        for (int i = 0; i < 5; i++) {
            QuickJSNative.ProcessCompletedTasks(_ctx);
            _ctx.ExecutePendingJobs();
        }

        var done = _ctx.Eval("__asyncDone");
        Assert.AreEqual("true", done, "Async function should have completed");

        var result = _ctx.Eval("__asyncResult");
        Assert.AreEqual("60", result, "Sum should be 60");

        yield return null;
    }

    // MARK: Array Marshaling Tests

    [UnityTest]
    public IEnumerator Array_Float32Array_ToFloatArray_Works() {
        // Test passing Float32Array to a C# method expecting float[]
        var result = _ctx.Eval(@"
            var arr = new Float32Array([1.5, 2.5, 3.5, 4.5]);
            CS.ArrayTestHelper.SumFloatArray(arr);
        ");
        Assert.AreEqual("12", result); // 1.5 + 2.5 + 3.5 + 4.5 = 12
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_Int32Array_ToIntArray_Works() {
        // Test passing Int32Array to a C# method expecting int[]
        var result = _ctx.Eval(@"
            var arr = new Int32Array([10, 20, 30, 40]);
            CS.ArrayTestHelper.SumIntArray(arr);
        ");
        Assert.AreEqual("100", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_JsArray_ToIntArray_Works() {
        // Test passing JS array to a C# method expecting int[]
        var result = _ctx.Eval(@"
            var arr = [1, 2, 3, 4, 5];
            CS.ArrayTestHelper.SumIntArray(arr);
        ");
        Assert.AreEqual("15", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_JsArray_ToFloatArray_Works() {
        // Test passing JS array with floats to a C# method
        var result = _ctx.Eval(@"
            var arr = [0.5, 1.5, 2.5];
            CS.ArrayTestHelper.SumFloatArray(arr);
        ");
        Assert.AreEqual("4.5", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_JsObjectArray_ToVector3Array_Works() {
        // Test passing JS array of {x,y,z} objects to Vector3[]
        var result = _ctx.Eval(@"
            var arr = [
                { x: 1, y: 2, z: 3 },
                { x: 4, y: 5, z: 6 }
            ];
            var sum = CS.ArrayTestHelper.SumVector3Array(arr);
            sum.x + ',' + sum.y + ',' + sum.z;
        ");
        Assert.AreEqual("5,7,9", result); // (1+4, 2+5, 3+6)
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_JsTupleArray_ToVector3Array_Works() {
        // Test passing JS array of [x,y,z] tuples to Vector3[]
        var result = _ctx.Eval(@"
            var arr = [
                [1, 0, 0],
                [0, 1, 0],
                [0, 0, 1]
            ];
            var sum = CS.ArrayTestHelper.SumVector3Array(arr);
            sum.x + ',' + sum.y + ',' + sum.z;
        ");
        Assert.AreEqual("1,1,1", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_EmptyTypedArray_Works() {
        // Test passing empty typed array (TypedArrays have explicit type info)
        var result = _ctx.Eval(@"
            var arr = new Int32Array(0);
            CS.ArrayTestHelper.SumIntArray(arr);
        ");
        Assert.AreEqual("0", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_JsColorArray_ToColorArray_Works() {
        // Test passing JS array of {r,g,b,a} objects to Color[]
        // Note: Color is returned as Vector4, so we access x,y,z (which map to r,g,b)
        var result = _ctx.Eval(@"
            var colors = [
                { r: 1, g: 0, b: 0, a: 1 },
                { r: 0, g: 1, b: 0, a: 1 }
            ];
            var avg = CS.ArrayTestHelper.AverageColors(colors);
            avg.x + ',' + avg.y + ',' + avg.z;
        ");
        Assert.AreEqual("0.5,0.5,0", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_StringArray_Works() {
        // Test passing JS string array
        var result = _ctx.Eval(@"
            var arr = ['Hello', ' ', 'World'];
            CS.ArrayTestHelper.JoinStrings(arr);
        ");
        Assert.AreEqual("Hello World", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Array_LargeArray_Works() {
        // Test with a larger array to ensure no performance/memory issues
        var result = _ctx.Eval(@"
            var arr = new Float32Array(1000);
            for (var i = 0; i < 1000; i++) arr[i] = 1;
            CS.ArrayTestHelper.SumFloatArray(arr);
        ");
        Assert.AreEqual("1000", result);
        yield return null;
    }
}

// MARK: Array Test Helper
public static class ArrayTestHelper {
    public static float SumFloatArray(float[] arr) {
        if (arr == null || arr.Length == 0) return 0;
        float sum = 0;
        for (int i = 0; i < arr.Length; i++) sum += arr[i];
        return sum;
    }

    public static int SumIntArray(int[] arr) {
        if (arr == null || arr.Length == 0) return 0;
        int sum = 0;
        for (int i = 0; i < arr.Length; i++) sum += arr[i];
        return sum;
    }

    public static Vector3 SumVector3Array(Vector3[] arr) {
        if (arr == null || arr.Length == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < arr.Length; i++) sum += arr[i];
        return sum;
    }

    public static Color AverageColors(Color[] arr) {
        if (arr == null || arr.Length == 0) return Color.black;
        float r = 0, g = 0, b = 0, a = 0;
        for (int i = 0; i < arr.Length; i++) {
            r += arr[i].r;
            g += arr[i].g;
            b += arr[i].b;
            a += arr[i].a;
        }
        int n = arr.Length;
        return new Color(r / n, g / n, b / n, a / n);
    }

    public static string JoinStrings(string[] arr) {
        if (arr == null || arr.Length == 0) return "";
        return string.Join("", arr);
    }
}

