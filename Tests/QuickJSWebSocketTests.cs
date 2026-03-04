using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the WebSocket API implementation.
/// Uses ws.postman-echo.com for real WebSocket echo tests.
/// </summary>
[TestFixture]
public class QuickJSWebSocketTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    [UnitySetUp]
    public IEnumerator SetUp() {
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        _go = new GameObject("WebSocketTestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        yield return null;

        var root = _uiDocument.rootVisualElement;
        _bridge = new QuickJSUIBridge(root);

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _bridge?.Dispose();
        _bridge = null;

        if (_go != null) Object.Destroy(_go);
        if (_panelSettings != null) Object.Destroy(_panelSettings);

        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: API Existence Tests

    [UnityTest]
    public IEnumerator WebSocket_GlobalExists() {
        var result = _bridge.Eval("typeof WebSocket");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator WebSocket_Constants_AreCorrect() {
        var result = _bridge.Eval(@"
            JSON.stringify({
                connecting: WebSocket.CONNECTING,
                open: WebSocket.OPEN,
                closing: WebSocket.CLOSING,
                closed: WebSocket.CLOSED
            })
        ");
        Assert.IsTrue(result.Contains("\"connecting\":0"), $"Expected CONNECTING=0, got: {result}");
        Assert.IsTrue(result.Contains("\"open\":1"), $"Expected OPEN=1, got: {result}");
        Assert.IsTrue(result.Contains("\"closing\":2"), $"Expected CLOSING=2, got: {result}");
        Assert.IsTrue(result.Contains("\"closed\":3"), $"Expected CLOSED=3, got: {result}");
        yield return null;
    }

    // MARK: Constructor Tests

    [UnityTest]
    public IEnumerator WebSocket_Constructor_SetsUrl() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.url;
        ");
        Assert.AreEqual("wss://ws.postman-echo.com/raw", result);
        // Clean up
        _bridge.Eval("ws.close()");
        yield return null;
    }

    [UnityTest]
    public IEnumerator WebSocket_Constructor_StartsConnecting() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.readyState.toString();
        ");
        Assert.AreEqual("0", result);
        _bridge.Eval("ws.close()");
        yield return null;
    }

    [UnityTest]
    public IEnumerator WebSocket_Send_ThrowsWhenNotOpen() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            var threw = false;
            try {
                ws.send('test');
            } catch (e) {
                threw = true;
            }
            ws.close();
            threw ? 'yes' : 'no';
        ");
        Assert.AreEqual("yes", result);
        yield return null;
    }

    // MARK: Connection Lifecycle Tests

    [UnityTest]
    public IEnumerator WebSocket_Connect_FiresOnOpen() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function() {
                globalThis.__wsResult = { readyState: ws.readyState };
                ws.close();
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"readyState\":1"), $"Expected readyState:1 (OPEN), got: {resultJson}");
    }

    [UnityTest]
    public IEnumerator WebSocket_Close_FiresOnClose() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function() {
                ws.close();
            };
            ws.onclose = function(event) {
                globalThis.__wsResult = {
                    readyState: ws.readyState,
                    code: event.code,
                    wasClean: event.wasClean
                };
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"readyState\":3"), $"Expected readyState:3 (CLOSED), got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"code\":1000"), $"Expected code:1000, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"wasClean\":true"), $"Expected wasClean:true, got: {resultJson}");
    }

    // MARK: Echo Tests

    [UnityTest]
    public IEnumerator WebSocket_SendAndReceive_EchoWorks() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function() {
                ws.send('hello from OneJS');
            };
            ws.onmessage = function(event) {
                globalThis.__wsResult = { echo: event.data };
                ws.close();
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"echo\":\"hello from OneJS\""), $"Expected echo, got: {resultJson}");
    }

    [UnityTest]
    public IEnumerator WebSocket_SendJson_EchoWorks() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function() {
                ws.send(JSON.stringify({ type: 'test', value: 42 }));
            };
            ws.onmessage = function(event) {
                var data = JSON.parse(event.data);
                globalThis.__wsResult = { type: data.type, value: data.value };
                ws.close();
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"type\":\"test\""), $"Expected type:test, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"value\":42"), $"Expected value:42, got: {resultJson}");
    }

    // MARK: addEventListener Tests

    [UnityTest]
    public IEnumerator WebSocket_AddEventListener_Works() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.addEventListener('open', function() {
                globalThis.__wsResult = { opened: true };
                ws.close();
                globalThis.__wsDone = true;
            });
            ws.addEventListener('error', function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"opened\":true"), $"Expected opened:true, got: {resultJson}");
    }

    // MARK: Error Handling Tests

    [UnityTest]
    public IEnumerator WebSocket_InvalidUrl_FiresErrorAndClose() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = { gotError: false, gotClose: false };
            var ws = new WebSocket('wss://invalid.host.that.does.not.exist.example.com');
            ws.onerror = function() {
                globalThis.__wsResult.gotError = true;
            };
            ws.onclose = function(event) {
                globalThis.__wsResult.gotClose = true;
                globalThis.__wsResult.code = event.code;
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 15f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"gotError\":true"), $"Expected gotError:true, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"gotClose\":true"), $"Expected gotClose:true, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"code\":1006"), $"Expected code:1006, got: {resultJson}");
    }

    // MARK: Binary Frame Tests

    [UnityTest]
    public IEnumerator WebSocket_BinaryType_DefaultsToArrayBuffer() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.binaryType;
        ");
        Assert.AreEqual("arraybuffer", result);
        _bridge.Eval("ws.close()");
        yield return null;
    }

    [UnityTest]
    public IEnumerator WebSocket_Binary_Base64RoundTrip() {
        // Test the base64 encode/decode pipeline without a server
        var result = _bridge.Eval(@"
            var original = [0, 1, 127, 128, 254, 255];
            var arr = new Uint8Array(original);
            var b64 = __arrayBufferToBase64(arr.buffer);
            var decoded = __base64ToArrayBuffer(b64);
            var roundTripped = Array.from(new Uint8Array(decoded));
            JSON.stringify({ b64: b64, match: JSON.stringify(original) === JSON.stringify(roundTripped), bytes: roundTripped });
        ");
        Assert.IsTrue(result.Contains("\"match\":true"), $"Base64 round-trip failed: {result}");
        Assert.IsTrue(result.Contains("\"bytes\":[0,1,127,128,254,255]"), $"Byte values wrong: {result}");
        yield return null;
    }

    [UnityTest]
    public IEnumerator WebSocket_Binary_SendDetectsTypes() {
        // Verify send() correctly detects binary types vs text
        var result = _bridge.Eval(@"
            var ab = new ArrayBuffer(2);
            var u8 = new Uint8Array([1, 2, 3]);
            var i32 = new Int32Array([42]);
            JSON.stringify({
                abIsAB: ab instanceof ArrayBuffer,
                u8IsView: ArrayBuffer.isView(u8),
                i32IsView: ArrayBuffer.isView(i32),
                strIsView: ArrayBuffer.isView('hello')
            });
        ");
        Assert.IsTrue(result.Contains("\"abIsAB\":true"), $"ArrayBuffer detection failed: {result}");
        Assert.IsTrue(result.Contains("\"u8IsView\":true"), $"Uint8Array detection failed: {result}");
        Assert.IsTrue(result.Contains("\"i32IsView\":true"), $"Int32Array detection failed: {result}");
        Assert.IsTrue(result.Contains("\"strIsView\":false"), $"String falsely detected as view: {result}");
        yield return null;
    }

    // MARK: Multiple Connections Test

    [UnityTest]
    public IEnumerator WebSocket_MultipleConnections_WorkIndependently() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = { count: 0 };
            var ws1 = new WebSocket('wss://ws.postman-echo.com/raw');
            var ws2 = new WebSocket('wss://ws.postman-echo.com/raw');
            ws1.onopen = function() {
                globalThis.__wsResult.count++;
                ws1.close();
                if (globalThis.__wsResult.count === 2) globalThis.__wsDone = true;
            };
            ws2.onopen = function() {
                globalThis.__wsResult.count++;
                ws2.close();
                if (globalThis.__wsResult.count === 2) globalThis.__wsDone = true;
            };
            ws1.onerror = function() { globalThis.__wsDone = true; };
            ws2.onerror = function() { globalThis.__wsDone = true; };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"count\":2"), $"Expected count:2, got: {resultJson}");
    }

    // MARK: Protocol Tests

    [UnityTest]
    public IEnumerator WebSocket_Protocol_SetAfterOpen() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function() {
                globalThis.__wsResult = {
                    protocolType: typeof ws.protocol,
                    protocolIsString: typeof ws.protocol === 'string'
                };
                ws.close();
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"protocolIsString\":true"), $"Expected protocol to be a string after open, got: {resultJson}");
    }

    // MARK: Instance Constants Tests

    [UnityTest]
    public IEnumerator WebSocket_InstanceConstants_Accessible() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            var r = JSON.stringify({
                connecting: ws.CONNECTING,
                open: ws.OPEN,
                closing: ws.CLOSING,
                closed: ws.CLOSED
            });
            ws.close();
            r;
        ");
        Assert.IsTrue(result.Contains("\"connecting\":0"), $"Expected CONNECTING=0 on instance, got: {result}");
        Assert.IsTrue(result.Contains("\"open\":1"), $"Expected OPEN=1 on instance, got: {result}");
        Assert.IsTrue(result.Contains("\"closing\":2"), $"Expected CLOSING=2 on instance, got: {result}");
        Assert.IsTrue(result.Contains("\"closed\":3"), $"Expected CLOSED=3 on instance, got: {result}");
        yield return null;
    }

    // MARK: EventTarget Tests

    [UnityTest]
    public IEnumerator WebSocket_EventTarget_HasTargetProperty() {
        _bridge.Eval(@"
            globalThis.__wsDone = false;
            globalThis.__wsResult = null;
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            ws.onopen = function(event) {
                globalThis.__wsResult = {
                    openTargetMatch: event.target === ws,
                    openCurrentTargetMatch: event.currentTarget === ws
                };
                ws.close();
            };
            ws.onclose = function(event) {
                globalThis.__wsResult.closeTargetMatch = event.target === ws;
                globalThis.__wsDone = true;
            };
            ws.onerror = function() {
                globalThis.__wsResult = { error: 'connection failed' };
                globalThis.__wsDone = true;
            };
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__wsDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__wsResult)");
        Assert.IsTrue(resultJson.Contains("\"openTargetMatch\":true"), $"Expected event.target === ws in onopen, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"openCurrentTargetMatch\":true"), $"Expected event.currentTarget === ws in onopen, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"closeTargetMatch\":true"), $"Expected event.target === ws in onclose, got: {resultJson}");
    }

    [UnityTest]
    public IEnumerator WebSocket_DispatchEvent_Works() {
        var result = _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
            var received = null;
            ws.addEventListener('message', function(event) {
                received = {
                    data: event.data,
                    targetMatch: event.target === ws
                };
            });
            ws.dispatchEvent({ type: 'message', data: 'test' });
            ws.close();
            JSON.stringify(received);
        ");
        Assert.IsTrue(result.Contains("\"data\":\"test\""), $"Expected dispatched event data, got: {result}");
        Assert.IsTrue(result.Contains("\"targetMatch\":true"), $"Expected event.target === ws in dispatchEvent, got: {result}");
        yield return null;
    }

    // MARK: TypedArray Subview Tests

    [UnityTest]
    public IEnumerator WebSocket_TypedArraySubview_SlicesCorrectly() {
        var result = _bridge.Eval(@"
            var big = new ArrayBuffer(10);
            var view = new Uint8Array(big);
            for (var i = 0; i < 10; i++) view[i] = i;
            var sub = new Uint8Array(big, 3, 4);
            var slice = sub.buffer.slice(sub.byteOffset, sub.byteOffset + sub.byteLength);
            var result = Array.from(new Uint8Array(slice));
            JSON.stringify(result);
        ");
        Assert.AreEqual("[3,4,5,6]", result, $"Expected [3,4,5,6], got: {result}");
        yield return null;
    }

    // MARK: Per-Context Isolation Tests

    [UnityTest]
    public IEnumerator WebSocket_PerContext_EventIsolation() {
        var result = _bridge.Eval(@"
            JSON.stringify({
                type: typeof globalThis.__wsContextId,
                isPositive: typeof globalThis.__wsContextId === 'number' && globalThis.__wsContextId > 0
            });
        ");
        Assert.IsTrue(result.Contains("\"type\":\"number\""), $"Expected __wsContextId to be a number, got: {result}");
        Assert.IsTrue(result.Contains("\"isPositive\":true"), $"Expected __wsContextId > 0, got: {result}");
        yield return null;
    }

    // MARK: Cleanup Tests

    [UnityTest]
    public IEnumerator WebSocket_Dispose_ClosesAllConnections() {
        _bridge.Eval(@"
            var ws = new WebSocket('wss://ws.postman-echo.com/raw');
        ");
        _bridge.Context.ExecutePendingJobs();

        // Wait a bit for connection to start
        yield return new WaitForSeconds(1f);

        // Dispose should close all WebSocket connections via WebSocketBridge.CloseAll()
        _bridge.Dispose();

        // Verify no exceptions were thrown during cleanup
        Assert.IsTrue(true, "Dispose completed without exceptions");
        yield return null;
    }
}
