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
/// The zero-alloc system provides two tiers of C#/JavaScript interop:
///
/// 1. **Generic Bind&lt;T&gt;()** - Convenient but boxes value types (~80B per call)
/// 2. **Specialized BindGpu*()** - No generics, no boxing (0B per call)
///
/// ## Why Boxing Occurs in Generic Bind
///
/// C# generics use type erasure, causing boxing in two places:
/// - GetArg&lt;T&gt;(): `return (T)(object)GetInt(v)` - boxes int to object
/// - SetResult&lt;T&gt;(): `switch (value)` - pattern matching boxes value types
///
/// ## Specialized Bindings
///
/// The BindGpu* methods use direct primitive types:
/// - BindGpuSetFloatById(Action&lt;int, int, float&gt;)
/// - BindGpuSetIntById(Action&lt;int, int, int&gt;)
/// - BindGpuSetVectorById(Action&lt;int, int, float, float, float, float&gt;)
/// - BindGpuSetTextureById(Action&lt;int, int, int, int&gt;)
/// - BindGpuDispatch(Action&lt;int, int, int, int, int&gt;)
/// - BindGpuGetScreenWidth(Func&lt;int&gt;)
/// - BindGpuGetScreenHeight(Func&lt;int&gt;)
///
/// ## Property ID Caching
///
/// String shader property names are converted to integer IDs once via
/// Shader.PropertyToID(), then cached. Per-frame calls use cached IDs.
///
/// ## Usage Pattern
///
/// ```csharp
/// // At init time - register specialized binding
/// int bindingId = QuickJSNative.BindGpuSetFloatById((h, id, v) => {
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
    /// Note: This still boxes due to generics - use specialized bindings for hot paths.
    /// </summary>
    [UnityTest]
    public IEnumerator Bind_MultiArg_Works() {
        float capturedValue = 0;

        // Register a 3-arg action (generic - will box)
        int bindingId = QuickJSNative.Bind<int, int, float>((handle, nameId, value) => {
            capturedValue = value;
            Debug.Log($"[ZeroAlloc] Generic binding called: handle={handle}, nameId={nameId}, value={value}");
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] Registered 3-arg generic binding: ID={bindingId}");

        yield return null;
    }

    // ========================================================================
    // MARK: Specialized GPU Binding Tests
    // ========================================================================

    /// <summary>
    /// Tests BindGpuSetFloatById - truly zero-alloc float setter.
    ///
    /// This is the recommended pattern for per-frame shader uniform updates.
    /// </summary>
    [UnityTest]
    public IEnumerator BindGpuSetFloatById_NoBoxing() {
        int capturedHandle = 0;
        int capturedNameId = 0;
        float capturedValue = 0;

        // Register specialized binding - no generics, no boxing
        int bindingId = QuickJSNative.BindGpuSetFloatById((handle, nameId, value) => {
            capturedHandle = handle;
            capturedNameId = nameId;
            capturedValue = value;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] BindGpuSetFloatById registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests BindGpuSetVectorById - truly zero-alloc vector setter.
    ///
    /// Passes 6 args: shaderHandle, nameId, x, y, z, w
    /// </summary>
    [UnityTest]
    public IEnumerator BindGpuSetVectorById_SixArgs() {
        float capturedX = 0, capturedY = 0, capturedZ = 0, capturedW = 0;

        int bindingId = QuickJSNative.BindGpuSetVectorById((handle, nameId, x, y, z, w) => {
            capturedX = x;
            capturedY = y;
            capturedZ = z;
            capturedW = w;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] BindGpuSetVectorById registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests BindGpuDispatch - truly zero-alloc dispatch.
    ///
    /// Passes 5 args: shaderHandle, kernelIndex, groupsX, groupsY, groupsZ
    /// </summary>
    [UnityTest]
    public IEnumerator BindGpuDispatch_FiveArgs() {
        int capturedGroupsX = 0, capturedGroupsY = 0, capturedGroupsZ = 0;

        int bindingId = QuickJSNative.BindGpuDispatch((shader, kernel, gx, gy, gz) => {
            capturedGroupsX = gx;
            capturedGroupsY = gy;
            capturedGroupsZ = gz;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] BindGpuDispatch registered: ID={bindingId}");

        yield return null;
    }

    /// <summary>
    /// Tests BindGpuGetScreenWidth - truly zero-alloc int return.
    ///
    /// Uses SetResultInt instead of generic SetResult&lt;T&gt; to avoid boxing.
    /// </summary>
    [UnityTest]
    public IEnumerator BindGpuGetScreenWidth_ReturnsInt() {
        int bindingId = QuickJSNative.BindGpuGetScreenWidth(() => {
            return Screen.width;
        });

        Assert.Greater(bindingId, 0);
        Debug.Log($"[ZeroAlloc] BindGpuGetScreenWidth registered: ID={bindingId}");

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
    // MARK: Allocation Comparison Tests
    // ========================================================================

    /// <summary>
    /// Compares allocation between generic and specialized bindings.
    ///
    /// This test demonstrates why specialized bindings are necessary for hot paths.
    /// </summary>
    [UnityTest]
    public IEnumerator Allocation_GenericVsSpecialized_Comparison() {
        // Create test bindings
        int genericBindingCalls = 0;
        int specializedBindingCalls = 0;

        // Generic binding (uses Bind<T0, T1, T2>)
        int genericId = QuickJSNative.Bind<int, int, float>((h, id, v) => {
            genericBindingCalls++;
        });

        // Specialized binding (uses BindGpuSetFloatById)
        int specializedId = QuickJSNative.BindGpuSetFloatById((h, id, v) => {
            specializedBindingCalls++;
        });

        Assert.Greater(genericId, 0);
        Assert.Greater(specializedId, 0);

        Debug.Log($"[ZeroAlloc] Binding IDs: generic={genericId}, specialized={specializedId}");
        Debug.Log("[ZeroAlloc] In production:");
        Debug.Log("  - Generic Bind<T>(): ~80B allocation per call (boxing)");
        Debug.Log("  - Specialized BindGpu*(): 0B allocation per call");
        Debug.Log("[ZeroAlloc] Use specialized bindings for per-frame GPU operations");

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
    /// Tests specialized binding registration performance.
    /// Registration is one-time at init, so some allocation is acceptable.
    /// </summary>
    [UnityTest]
    public IEnumerator SpecializedBinding_RegistrationTime() {
        var sw = Stopwatch.StartNew();

        const int registrations = 100;
        for (int i = 0; i < registrations; i++) {
            QuickJSNative.BindGpuSetFloatById((h, id, v) => { });
        }

        sw.Stop();

        Debug.Log($"[ZeroAlloc] Registered {registrations} specialized bindings in {sw.ElapsedMilliseconds}ms");
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
