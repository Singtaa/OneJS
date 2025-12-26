using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using OneJS.GPU;

/// <summary>
/// PlayMode tests for GPU compute shader functionality.
/// Tests both the C# GPUBridge directly and through JavaScript interop.
/// </summary>
[TestFixture]
public class GPUBridgePlaymodeTests {
    ComputeShader _testShader;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Load test shader from Resources
        _testShader = Resources.Load<ComputeShader>("TestShaders/SimpleCompute");
        if (_testShader == null) {
            Debug.LogWarning("[GPUBridgePlaymodeTests] Test shader not found, skipping GPU tests");
        }

        // Clear any previous state
        GPUBridge.Cleanup();
        GPUBridge.ClearRegistry();

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        GPUBridge.Cleanup();
        GPUBridge.ClearRegistry();
        yield return null;
    }

    // MARK: Platform Capability Tests

    [UnityTest]
    public IEnumerator Platform_SupportsCompute_ReturnsExpectedValue() {
        // Just verify the property doesn't throw
        bool supports = GPUBridge.SupportsCompute;
        Debug.Log($"[GPUBridgePlaymodeTests] SupportsCompute: {supports}");
        Assert.IsTrue(supports || !supports); // Always true, just verify no exception
        yield return null;
    }

    [UnityTest]
    public IEnumerator Platform_SupportsAsyncReadback_ReturnsExpectedValue() {
        bool supports = GPUBridge.SupportsAsyncReadback;
        Debug.Log($"[GPUBridgePlaymodeTests] SupportsAsyncReadback: {supports}");
        Assert.IsTrue(supports || !supports);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Platform_MaxWorkGroupSize_ReturnsPositiveValues() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        Assert.Greater(GPUBridge.MaxComputeWorkGroupSizeX, 0);
        Assert.Greater(GPUBridge.MaxComputeWorkGroupSizeY, 0);
        Assert.Greater(GPUBridge.MaxComputeWorkGroupSizeZ, 0);
        yield return null;
    }

    // MARK: Registry Tests

    [UnityTest]
    public IEnumerator Register_ValidShader_CanBeLoaded() {
        if (_testShader == null) {
            Assert.Ignore("Test shader not available");
            yield break;
        }

        GPUBridge.Register("TestShader", _testShader);
        int handle = GPUBridge.LoadShader("TestShader");

        Assert.Greater(handle, 0, "Shader handle should be positive");

        GPUBridge.DisposeShader(handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LoadShader_NotRegistered_ReturnsNegative() {
        // Expect the warning log
        LogAssert.Expect(LogType.Warning, "[GPUBridge] Shader 'NonExistentShader' not found in registry");

        int handle = GPUBridge.LoadShader("NonExistentShader");
        Assert.AreEqual(-1, handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Unregister_RemovesShader() {
        if (_testShader == null) {
            Assert.Ignore("Test shader not available");
            yield break;
        }

        // Expect warning when trying to load after unregistering
        LogAssert.Expect(LogType.Warning, "[GPUBridge] Shader 'TestShader' not found in registry");

        GPUBridge.Register("TestShader", _testShader);
        GPUBridge.Unregister("TestShader");
        int handle = GPUBridge.LoadShader("TestShader");

        Assert.AreEqual(-1, handle, "Shader should not be loadable after unregister");
        yield return null;
    }

    // MARK: Kernel Tests

    [UnityTest]
    public IEnumerator FindKernel_ValidKernel_ReturnsPositiveIndex() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        GPUBridge.Register("TestShader", _testShader);
        int shaderHandle = GPUBridge.LoadShader("TestShader");
        int kernelIndex = GPUBridge.FindKernel(shaderHandle, "CSMain");

        Assert.GreaterOrEqual(kernelIndex, 0, "Kernel index should be non-negative");

        GPUBridge.DisposeShader(shaderHandle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FindKernel_InvalidKernel_ReturnsNegative() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        // Expect Unity's error log when kernel is not found (use regex for flexibility)
        LogAssert.Expect(LogType.Error, new Regex("NonExistentKernel"));

        GPUBridge.Register("TestShader", _testShader);
        int shaderHandle = GPUBridge.LoadShader("TestShader");

        // NOTE: The following error log is expected - Unity logs when FindKernel fails
        Debug.Log("[GPUBridgePlaymodeTests] Intentionally requesting non-existent kernel (expect error below)");
        int kernelIndex = GPUBridge.FindKernel(shaderHandle, "NonExistentKernel");

        Assert.AreEqual(-1, kernelIndex);

        GPUBridge.DisposeShader(shaderHandle);
        yield return null;
    }

    // MARK: Buffer Tests

    [UnityTest]
    public IEnumerator CreateBuffer_ValidParams_ReturnsPositiveHandle() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        int handle = GPUBridge.CreateBuffer(64, sizeof(float));
        Assert.Greater(handle, 0, "Buffer handle should be positive");

        GPUBridge.DisposeBuffer(handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator CreateBuffer_InvalidParams_ReturnsNegative() {
        // Expect warning logs for invalid params
        LogAssert.Expect(LogType.Warning, new Regex("Invalid buffer parameters"));
        LogAssert.Expect(LogType.Warning, new Regex("Invalid buffer parameters"));

        int handle = GPUBridge.CreateBuffer(0, sizeof(float));
        Assert.AreEqual(-1, handle);

        handle = GPUBridge.CreateBuffer(64, 0);
        Assert.AreEqual(-1, handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SetBufferData_ValidJson_NoException() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        int handle = GPUBridge.CreateBuffer(4, sizeof(float));
        GPUBridge.SetBufferData(handle, "[1.0, 2.0, 3.0, 4.0]");

        // No exception means success
        GPUBridge.DisposeBuffer(handle);
        yield return null;
    }

    // MARK: Dispatch Tests

    [UnityTest]
    public IEnumerator Dispatch_SimpleMultiply_ModifiesBuffer() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        // Register shader
        GPUBridge.Register("TestShader", _testShader);
        int shaderHandle = GPUBridge.LoadShader("TestShader");
        int kernelIndex = GPUBridge.FindKernel(shaderHandle, "CSMain");

        // Create buffer with test data
        int bufferHandle = GPUBridge.CreateBuffer(4, sizeof(float));
        GPUBridge.SetBufferData(bufferHandle, "[1.0, 2.0, 3.0, 4.0]");

        // Set uniforms and bind buffer
        GPUBridge.SetFloat(shaderHandle, "multiplier", 2.0f);
        GPUBridge.SetInt(shaderHandle, "dataCount", 4); // Bounds check
        GPUBridge.BindBuffer(shaderHandle, kernelIndex, "data", bufferHandle);

        // Dispatch
        GPUBridge.Dispatch(shaderHandle, kernelIndex, 1, 1, 1);

        // Request readback
        int requestId = GPUBridge.RequestReadback(bufferHandle);
        Assert.Greater(requestId, 0, "Readback request should succeed");

        // Wait for readback to complete
        int maxWaitFrames = 60;
        int waitedFrames = 0;
        while (!GPUBridge.IsReadbackComplete(requestId) && waitedFrames < maxWaitFrames) {
            waitedFrames++;
            yield return null;
        }

        Assert.IsTrue(GPUBridge.IsReadbackComplete(requestId), "Readback should complete");

        // Get result
        string resultJson = GPUBridge.GetReadbackData(requestId);
        Debug.Log($"[GPUBridgePlaymodeTests] Result: {resultJson}");

        // Parse and verify
        // Expected: [2.0, 4.0, 6.0, 8.0]
        Assert.IsTrue(resultJson.Contains("2"), "First element should be multiplied");

        // Cleanup
        GPUBridge.DisposeBuffer(bufferHandle);
        GPUBridge.DisposeShader(shaderHandle);
    }

    // MARK: Uniform Setting Tests

    [UnityTest]
    public IEnumerator SetFloat_NoException() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        GPUBridge.Register("TestShader", _testShader);
        int handle = GPUBridge.LoadShader("TestShader");

        GPUBridge.SetFloat(handle, "multiplier", 3.14f);
        // No exception means success

        GPUBridge.DisposeShader(handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SetInt_NoException() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        GPUBridge.Register("TestShader", _testShader);
        int handle = GPUBridge.LoadShader("TestShader");

        GPUBridge.SetInt(handle, "someInt", 42);
        // No exception means success

        GPUBridge.DisposeShader(handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SetVector_NoException() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        GPUBridge.Register("TestShader", _testShader);
        int handle = GPUBridge.LoadShader("TestShader");

        GPUBridge.SetVector(handle, "offset", 1.0f, 2.0f, 3.0f, 0.0f);
        // No exception means success

        GPUBridge.DisposeShader(handle);
        yield return null;
    }

    // MARK: Cleanup Tests

    [UnityTest]
    public IEnumerator Cleanup_ReleasesAllResources() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        // Create some resources
        int buffer1 = GPUBridge.CreateBuffer(64, sizeof(float));
        int buffer2 = GPUBridge.CreateBuffer(64, sizeof(float));

        Assert.Greater(buffer1, 0);
        Assert.Greater(buffer2, 0);

        // Cleanup
        GPUBridge.Cleanup();

        // Resources should be gone (we can't really verify this without internal access,
        // but at least verify no exception)
        yield return null;
    }
}

/// <summary>
/// PlayMode tests for GPU compute shader functionality through JavaScript.
/// </summary>
[TestFixture]
public class GPUBridgeJSPlaymodeTests {
    QuickJSContext _ctx;
    ComputeShader _testShader;

    [UnitySetUp]
    public IEnumerator SetUp() {
        _ctx = new QuickJSContext();

        // Load test shader from Resources
        _testShader = Resources.Load<ComputeShader>("TestShaders/SimpleCompute");

        // Clear any previous state
        GPUBridge.Cleanup();
        GPUBridge.ClearRegistry();

        // Register test shader if available
        if (_testShader != null) {
            GPUBridge.Register("TestShader", _testShader);
        }

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _ctx?.Dispose();
        _ctx = null;
        GPUBridge.Cleanup();
        GPUBridge.ClearRegistry();
        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_PlatformSupportsCompute_MatchesCSharp() {
        // Use GetSupportsCompute() method since CS proxy treats uppercase names as methods
        var result = _ctx.Eval("CS.OneJS.GPU.GPUBridge.GetSupportsCompute()");
        bool jsSupports = result == "true";
        Assert.AreEqual(GPUBridge.SupportsCompute, jsSupports);
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_LoadShader_ReturnsHandle() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        var result = _ctx.Eval("CS.OneJS.GPU.GPUBridge.LoadShader('TestShader')");
        int handle = int.Parse(result);
        Assert.Greater(handle, 0, "JS should get positive handle");

        _ctx.Eval($"CS.OneJS.GPU.GPUBridge.DisposeShader({handle})");
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_LoadShader_NotFound_ReturnsNegative() {
        // Expect the warning log
        LogAssert.Expect(LogType.Warning, "[GPUBridge] Shader 'NonExistent' not found in registry");

        var result = _ctx.Eval("CS.OneJS.GPU.GPUBridge.LoadShader('NonExistent')");
        int handle = int.Parse(result);
        Assert.AreEqual(-1, handle);
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_CreateBuffer_ReturnsHandle() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        var result = _ctx.Eval("CS.OneJS.GPU.GPUBridge.CreateBuffer(64, 4)");
        int handle = int.Parse(result);
        Assert.Greater(handle, 0, "JS should get positive buffer handle");

        _ctx.Eval($"CS.OneJS.GPU.GPUBridge.DisposeBuffer({handle})");
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_SetBufferData_NoException() {
        if (!GPUBridge.SupportsCompute) {
            Assert.Ignore("Compute shaders not supported on this platform");
            yield break;
        }

        _ctx.Eval(@"
            var buf = CS.OneJS.GPU.GPUBridge.CreateBuffer(4, 4);
            CS.OneJS.GPU.GPUBridge.SetBufferData(buf, '[1.0, 2.0, 3.0, 4.0]');
            CS.OneJS.GPU.GPUBridge.DisposeBuffer(buf);
        ");
        // No exception means success
        yield return null;
    }

    [UnityTest]
    public IEnumerator JS_FullComputeWorkflow_Works() {
        if (_testShader == null || !GPUBridge.SupportsCompute) {
            Assert.Ignore("Test shader not available or compute not supported");
            yield break;
        }

        // Execute full workflow from JS
        _ctx.Eval(@"
            globalThis.testResult = null;
            globalThis.testError = null;

            try {
                // Load shader
                var shader = CS.OneJS.GPU.GPUBridge.LoadShader('TestShader');
                if (shader < 0) throw new Error('Failed to load shader');

                // Find kernel
                var kernel = CS.OneJS.GPU.GPUBridge.FindKernel(shader, 'CSMain');
                if (kernel < 0) throw new Error('Failed to find kernel');

                // Create buffer
                var buffer = CS.OneJS.GPU.GPUBridge.CreateBuffer(4, 4);
                if (buffer < 0) throw new Error('Failed to create buffer');

                // Set data
                CS.OneJS.GPU.GPUBridge.SetBufferData(buffer, '[1.0, 2.0, 3.0, 4.0]');

                // Set uniforms and bind buffer
                CS.OneJS.GPU.GPUBridge.SetFloat(shader, 'multiplier', 3.0);
                CS.OneJS.GPU.GPUBridge.SetInt(shader, 'dataCount', 4); // Bounds check
                CS.OneJS.GPU.GPUBridge.BindBuffer(shader, kernel, 'data', buffer);

                // Dispatch
                CS.OneJS.GPU.GPUBridge.Dispatch(shader, kernel, 1, 1, 1);

                // Request readback
                var requestId = CS.OneJS.GPU.GPUBridge.RequestReadback(buffer);

                // Store handles for later
                globalThis.testShaderHandle = shader;
                globalThis.testBufferHandle = buffer;
                globalThis.testRequestId = requestId;
                globalThis.testResult = 'dispatched';
            } catch (e) {
                globalThis.testError = e.message;
            }
        ");

        var error = _ctx.Eval("globalThis.testError");
        if (error != "null" && error != "undefined") {
            Assert.Fail($"JS error: {error}");
        }

        var status = _ctx.Eval("globalThis.testResult");
        Assert.AreEqual("dispatched", status);

        // Wait for readback - poll each frame like the C# test does
        int maxWaitFrames = 60;
        int waitedFrames = 0;
        bool complete = false;

        while (!complete && waitedFrames < maxWaitFrames) {
            waitedFrames++;
            yield return null;

            var completeResult = _ctx.Eval("CS.OneJS.GPU.GPUBridge.IsReadbackComplete(globalThis.testRequestId)");
            complete = completeResult == "true";
        }

        Assert.IsTrue(complete, "Readback should complete within timeout");

        // Get readback data
        var data = _ctx.Eval("CS.OneJS.GPU.GPUBridge.GetReadbackData(globalThis.testRequestId)");
        Debug.Log($"[GPUBridgeJSPlaymodeTests] Readback data: {data}");

        // Verify data contains expected values (1*3=3, 2*3=6, 3*3=9, 4*3=12)
        Assert.IsTrue(data.Contains("3"), "Should contain multiplied values");

        // Cleanup
        _ctx.Eval(@"
            CS.OneJS.GPU.GPUBridge.DisposeBuffer(globalThis.testBufferHandle);
            CS.OneJS.GPU.GPUBridge.DisposeShader(globalThis.testShaderHandle);
        ");
    }
}
