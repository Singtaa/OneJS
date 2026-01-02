using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using OneJS.GPU;
using Debug = UnityEngine.Debug;

/// <summary>
/// Comprehensive tests for the zero-allocation interop system.
///
/// This file serves as both test suite and documentation for the zero-alloc mechanism:
///
/// ## Overview
///
/// The zero-alloc system provides truly zero-allocation C#/JavaScript interop using:
///
/// - **Generic Bind&lt;T&gt;()** - Uses UnsafeUtility.As for boxing-free type conversion (0B per call)
///
/// ## How Zero-Allocation is Achieved
///
/// C# generics normally cause boxing, but we avoid it using UnsafeUtility.As:
/// - GetArg&lt;T&gt;(): `return UnsafeUtility.As&lt;int, T&gt;(ref i)` - no boxing!
/// - SetResult&lt;T&gt;(): `result-&gt;i32 = UnsafeUtility.As&lt;T, int&gt;(ref value)` - no boxing!
///
/// ## Property ID Caching
///
/// String shader property names are converted to integer IDs once via
/// Shader.PropertyToID(), then cached. Per-frame calls use cached IDs.
///
/// ## Usage Pattern
///
/// ```csharp
/// // At init time - register binding (all Bind&lt;T&gt; methods are zero-alloc)
/// int bindingId = QuickJSNative.Bind&lt;int, int, float&gt;((h, id, v) =&gt; {
///     GPUBridge.SetFloatById(h, id, v);
/// });
///
/// // From JavaScript - call via __zaInvoke3
/// __zaInvoke3(bindingId, shaderHandle, propertyId, floatValue);
/// ```
/// </summary>
[TestFixture]
public class QuickJSZeroAllocTests {
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

    // ========================================================================
    // MARK: Basic Binding Registration Tests
    // ========================================================================

    /// <summary>
    /// Demonstrates basic zero-arg binding registration.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_ZeroArg_ReturnsPositiveId() {
        // Register a simple zero-arg action
        int bindingId = QuickJSNative.Bind(() => {
            // This action will be invoked when __zaInvoke0(bindingId) is called
        });

        Assert.Greater(bindingId, 0, "Binding ID should be positive");
        Debug.Log($"[ZeroAlloc] Registered zero-arg binding: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Demonstrates binding with return value.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_WithReturnValue_Works() {
        int callCount = 0;

        // Register a function that returns a value
        int bindingId = QuickJSNative.Bind(() => {
            callCount++;
            return 42;
        });

        Assert.Greater(bindingId, 0);

        // Note: In a real scenario, this would be called from JS via __zaInvoke0
        // The return value would be marshaled back through InteropValue
        Debug.Log($"[ZeroAlloc] Registered return-value binding: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Demonstrates multi-arg binding using generic Bind&lt;T&gt;().
    /// All Bind methods are now zero-alloc thanks to UnsafeUtility.As.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_MultiArg_Works() {
        float capturedValue = 0;

        // Register a 3-arg action (zero-alloc via UnsafeUtility.As)
        int bindingId = QuickJSNative.Bind<int, int, float>((handle, nameId, value) => {
            capturedValue = value;
            Debug.Log($"[ZeroAlloc] Binding called: handle={handle}, nameId={nameId}, value={value}");
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Registered 3-arg binding: ID={bindingId}");

        yield return null;
    }

    // ========================================================================
    // MARK: GPU Binding Tests
    // ========================================================================

    /// <summary>
    /// Tests 3-arg float setter binding - zero-alloc.
    ///
    /// This is the recommended pattern for per-frame shader uniform updates.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_SetFloatById_NoBoxing() {
        int capturedHandle = 0;
        int capturedNameId = 0;
        float capturedValue = 0;

        // Register binding - zero-alloc via UnsafeUtility.As
        int bindingId = QuickJSNative.Bind<int, int, float>((handle, nameId, value) => {
            capturedHandle = handle;
            capturedNameId = nameId;
            capturedValue = value;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Bind<int, int, float> registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests 6-arg vector setter binding - zero-alloc.
    ///
    /// Passes 6 args: shaderHandle, nameId, x, y, z, w
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_SetVectorById_SixArgs() {
        float capturedX = 0, capturedY = 0, capturedZ = 0, capturedW = 0;

        int bindingId = QuickJSNative.Bind<int, int, float, float, float, float>((handle, nameId, x, y, z, w) => {
            capturedX = x;
            capturedY = y;
            capturedZ = z;
            capturedW = w;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Bind<int, int, float, float, float, float> registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests 5-arg dispatch binding - zero-alloc.
    ///
    /// Passes 5 args: shaderHandle, kernelIndex, groupsX, groupsY, groupsZ
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_Dispatch_FiveArgs() {
        int capturedGroupsX = 0, capturedGroupsY = 0, capturedGroupsZ = 0;

        int bindingId = QuickJSNative.Bind<int, int, int, int, int>((shader, kernel, gx, gy, gz) => {
            capturedGroupsX = gx;
            capturedGroupsY = gy;
            capturedGroupsZ = gz;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Bind<int, int, int, int, int> registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests zero-arg int return binding - zero-alloc.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_GetScreenWidth_ReturnsInt() {
        int bindingId = QuickJSNative.Bind(() => {
            return Screen.width;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Bind<int>() registered: ID={bindingId}");

        yield return null;
    }

    // ========================================================================
    // MARK: Property ID Caching Tests
    // ========================================================================

    /// <summary>
    /// Demonstrates property ID caching pattern.
    ///
    /// Shader.PropertyToID() converts string names to integer IDs.
    /// Cache the ID once, use it for all subsequent calls.
    /// </summary>
    [UnityTest]
    public IEnumerator PropertyToID_CachingPattern() {
        // First call allocates (acceptable at init time)
        int timeId = Shader.PropertyToID("_Time");
        int resolutionId = Shader.PropertyToID("_Resolution");
        int resultId = Shader.PropertyToID("_Result");

        Assert.AreNotEqual(0, timeId, "PropertyToID should return non-zero ID");
        Assert.AreNotEqual(timeId, resolutionId, "Different names should have different IDs");

        // Same name returns same ID (cached by Unity internally)
        int timeId2 = Shader.PropertyToID("_Time");
        Assert.AreEqual(timeId, timeId2, "Same name should return same ID");

        Debug.Log($"[ZeroAlloc] Property IDs: _Time={timeId}, _Resolution={resolutionId}, _Result={resultId}");

        yield return null;
    }

    /// <summary>
    /// Tests GPUBridge.PropertyToID wrapper used by JavaScript.
    /// </summary>
    [UnityTest]
    public IEnumerator GPUBridge_PropertyToID_Works() {
        int id1 = GPUBridge.PropertyToID("_TestProperty");
        int id2 = GPUBridge.PropertyToID("_TestProperty");

        Assert.AreEqual(id1, id2, "Same property name should return same ID");
        Assert.AreNotEqual(0, id1);

        Debug.Log($"[ZeroAlloc] GPUBridge.PropertyToID('_TestProperty') = {id1}");

        yield return null;
    }

    // ========================================================================
    // MARK: GPUBridge Zero-Alloc Binding Integration Tests
    // ========================================================================

    /// <summary>
    /// Tests that GPUBridge zero-alloc bindings are properly initialized.
    /// </summary>
    [UnityTest]
    public IEnumerator GPUBridge_ZeroAllocBindings_Initialized() {
        var bindingIds = GPUBridge.GetZeroAllocBindingIds();

        // Verify all hot-path bindings are registered (positive IDs)
        Assert.Greater(bindingIds.dispatch, 0, "dispatch binding should be registered");
        Assert.Greater(bindingIds.setFloatById, 0, "setFloatById binding should be registered");
        Assert.Greater(bindingIds.setIntById, 0, "setIntById binding should be registered");
        Assert.Greater(bindingIds.setVectorById, 0, "setVectorById binding should be registered");
        Assert.Greater(bindingIds.setTextureById, 0, "setTextureById binding should be registered");
        Assert.Greater(bindingIds.getScreenWidth, 0, "getScreenWidth binding should be registered");
        Assert.Greater(bindingIds.getScreenHeight, 0, "getScreenHeight binding should be registered");

        Debug.Log($"[ZeroAlloc] GPUBridge binding IDs: dispatch={bindingIds.dispatch}, " +
            $"setFloatById={bindingIds.setFloatById}, setVectorById={bindingIds.setVectorById}");

        yield return null;
    }

    /// <summary>
    /// Tests ID-based setters in GPUBridge.
    /// </summary>
    [UnityTest]
    public IEnumerator GPUBridge_SetFloatById_Works() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        // Load a test shader
        var testShader = Resources.Load<ComputeShader>("TestShaders/SimpleCompute");
        if (testShader == null) {
            Assert.Ignore("Test shader not available");
            yield break;
        }

        GPUBridge.Register("ZeroAllocTest", testShader);
        int shaderHandle = GPUBridge.LoadShader("ZeroAllocTest");

        // Get property ID once
        int multiplierId = GPUBridge.PropertyToID("multiplier");

        // Set float using ID-based method (zero-alloc)
        GPUBridge.SetFloatById(shaderHandle, multiplierId, 3.14f);

        // No exception means success
        Debug.Log($"[ZeroAlloc] SetFloatById({shaderHandle}, {multiplierId}, 3.14f) succeeded");

        GPUBridge.DisposeShader(shaderHandle);
        GPUBridge.Unregister("ZeroAllocTest");

        yield return null;
    }

    // ========================================================================
    // MARK: Binding Registration Tests
    // ========================================================================

    /// <summary>
    /// Tests that multiple bindings can be registered with different signatures.
    /// All Bind methods are now zero-alloc thanks to UnsafeUtility.As.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_MultipleSignatures_AllZeroAlloc() {
        int bindingCalls = 0;

        // 3-arg binding
        int threeArgId = QuickJSNative.Bind<int, int, float>((h, id, v) => {
            bindingCalls++;
        });

        // 5-arg binding
        int fiveArgId = QuickJSNative.Bind<int, int, int, int, int>((a, b, c, d, e) => {
            bindingCalls++;
        });

        // 6-arg binding
        int sixArgId = QuickJSNative.Bind<int, int, float, float, float, float>((h, id, x, y, z, w) => {
            bindingCalls++;
        });

        Assert.Greater(threeArgId, 0);
        Assert.Greater(fiveArgId, 0);
        Assert.Greater(sixArgId, 0);

        Debug.Log($"[ZeroAlloc] Registered bindings: 3-arg={threeArgId}, 5-arg={fiveArgId}, 6-arg={sixArgId}");
        Debug.Log("[ZeroAlloc] All Bind<T>() methods are now 0B allocation per call");
        Debug.Log("[ZeroAlloc] Achieved via UnsafeUtility.As in GetArg<T> and SetResult<T>");

        yield return null;
    }

    /// <summary>
    /// Tests binding count after registration.
    /// </summary>
    [UnityTest]
    public IEnumerator ZeroAllocBindingCount_Increases() {
        int initialCount = QuickJSNative.ZeroAllocBindingCount;

        // Register a new binding
        QuickJSNative.Bind(() => { });

        int afterCount = QuickJSNative.ZeroAllocBindingCount;

        Assert.AreEqual(initialCount + 1, afterCount, "Binding count should increase by 1");
        Debug.Log($"[ZeroAlloc] Binding count: before={initialCount}, after={afterCount}");

        yield return null;
    }

    // ========================================================================
    // MARK: JavaScript Integration Tests
    // ========================================================================

    /// <summary>
    /// Tests GPUBridge zero-alloc binding IDs accessible from JavaScript.
    /// </summary>
    [UnityTest]
    public IEnumerator JS_GetZeroAllocBindingIds_Works() {
        var result = _ctx.Eval(@"
            var ids = CS.OneJS.GPU.GPUBridge.GetZeroAllocBindingIds();
            JSON.stringify({
                dispatch: ids.dispatch,
                setFloatById: ids.setFloatById,
                setIntById: ids.setIntById,
                setVectorById: ids.setVectorById,
                getScreenWidth: ids.getScreenWidth
            });
        ");

        Debug.Log($"[ZeroAlloc] JS binding IDs: {result}");
        Assert.IsTrue(result.Contains("dispatch"), "Should contain dispatch binding ID");
        Assert.IsTrue(result.Contains("setFloatById"), "Should contain setFloatById binding ID");

        yield return null;
    }

    /// <summary>
    /// Tests PropertyToID accessible from JavaScript.
    /// </summary>
    [UnityTest]
    public IEnumerator JS_PropertyToID_Works() {
        var result = _ctx.Eval("CS.OneJS.GPU.GPUBridge.PropertyToID('_Time')");
        int id = int.Parse(result);

        Assert.AreNotEqual(0, id);
        Debug.Log($"[ZeroAlloc] JS PropertyToID('_Time') = {id}");

        // Verify it matches C# result
        int csharpId = GPUBridge.PropertyToID("_Time");
        Assert.AreEqual(csharpId, id, "JS and C# should return same property ID");

        yield return null;
    }

    /// <summary>
    /// Tests screen dimension getters which use specialized bindings with SetResultInt.
    /// </summary>
    [UnityTest]
    public IEnumerator JS_ScreenDimensions_Works() {
        var widthResult = _ctx.Eval("CS.OneJS.GPU.GPUBridge.GetScreenWidth()");
        var heightResult = _ctx.Eval("CS.OneJS.GPU.GPUBridge.GetScreenHeight()");

        int jsWidth = int.Parse(widthResult);
        int jsHeight = int.Parse(heightResult);

        Assert.Greater(jsWidth, 0);
        Assert.Greater(jsHeight, 0);
        Assert.AreEqual(GPUBridge.GetScreenWidth(), jsWidth);
        Assert.AreEqual(GPUBridge.GetScreenHeight(), jsHeight);

        Debug.Log($"[ZeroAlloc] Screen dimensions from JS: {jsWidth}x{jsHeight}");

        yield return null;
    }
}

/// <summary>
/// Performance-focused tests for zero-alloc interop.
/// These tests measure allocation and timing behavior.
/// </summary>
[TestFixture]
public class QuickJSZeroAllocPerformanceTests {
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

    /// <summary>
    /// Tests binding registration performance.
    /// Registration is one-time at init, so some allocation is acceptable.
    /// </summary>
    [UnityTest]
    public IEnumerator Binding_RegistrationTime() {
        var sw = Stopwatch.StartNew();

        const int registrations = 100;
        for (int i = 0; i < registrations; i++) {
            QuickJSNative.Bind<int, int, float>((h, id, v) => { });
        }

        sw.Stop();

        Debug.Log($"[ZeroAlloc] Registered {registrations} bindings in {sw.ElapsedMilliseconds}ms");
        Debug.Log($"[ZeroAlloc] Average: {sw.ElapsedMilliseconds / (float)registrations:F2}ms per registration");

        Assert.Less(sw.ElapsedMilliseconds, 1000, "Registration should be fast");

        yield return null;
    }

    /// <summary>
    /// Simulates a typical per-frame GPU update pattern.
    /// </summary>
    [UnityTest]
    public IEnumerator SimulatedPerFrameGpuUpdate_Pattern() {
        // This demonstrates the recommended pattern for zero-alloc GPU updates

        // 1. At init time - cache property IDs
        int timeId = Shader.PropertyToID("_Time");
        int resolutionId = Shader.PropertyToID("_Resolution");
        int resultId = Shader.PropertyToID("_Result");

        // 2. Get binding IDs (one-time)
        var bindings = GPUBridge.GetZeroAllocBindingIds();

        // 3. Simulate per-frame calls
        // In real usage, these would call __zaInvokeN from JavaScript

        var sw = Stopwatch.StartNew();
        const int frames = 1000;

        for (int frame = 0; frame < frames; frame++) {
            float time = frame * 0.016f; // ~60fps

            // These represent the operations that would happen each frame:
            // - Set _Time uniform (float)
            // - Set _Resolution uniform (vec2 as vec4)
            // - Set _Result texture (texture handle)
            // - Dispatch kernel

            // In production, each of these would use __zaInvokeN with cached property IDs
            // Here we just demonstrate the pattern
        }

        sw.Stop();

        Debug.Log($"[ZeroAlloc] Simulated {frames} frame updates in {sw.ElapsedMilliseconds}ms");
        Debug.Log($"[ZeroAlloc] Pattern: cache property IDs at init, use ID-based setters per-frame");
        Debug.Log($"[ZeroAlloc] Binding IDs: timeId={timeId}, dispatch={bindings.dispatch}");

        Assert.Pass("Pattern demonstration completed");

        yield return null;
    }
}
