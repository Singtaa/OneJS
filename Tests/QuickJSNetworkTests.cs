using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the fetch API implementation.
/// Uses httpbin.org for real network requests.
/// </summary>
[TestFixture]
public class QuickJSNetworkTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        // Create GameObject with UIDocument
        _go = new GameObject("NetworkTestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        // Wait a frame for UIDocument to initialize
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

    // MARK: Fetch API Existence Tests

    [UnityTest]
    public IEnumerator Fetch_GlobalExists() {
        var result = _bridge.Eval("typeof fetch");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Fetch_ResponseClassExists() {
        var result = _bridge.Eval("typeof Response");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Fetch_HeadersClassExists() {
        var result = _bridge.Eval("typeof Headers");
        Assert.AreEqual("function", result);
        yield return null;
    }

    // MARK: Headers Class Tests

    [UnityTest]
    public IEnumerator Headers_GetSet_Works() {
        var result = _bridge.Eval(@"
            var h = new Headers({ 'Content-Type': 'application/json' });
            h.get('content-type');
        ");
        Assert.AreEqual("application/json", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Headers_Has_Works() {
        var result = _bridge.Eval(@"
            var h = new Headers({ 'X-Custom': 'value' });
            h.has('x-custom') ? 'yes' : 'no';
        ");
        Assert.AreEqual("yes", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Headers_Append_Works() {
        var result = _bridge.Eval(@"
            var h = new Headers({ 'Accept': 'text/html' });
            h.append('Accept', 'application/json');
            h.get('accept');
        ");
        Assert.AreEqual("text/html, application/json", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Headers_Delete_Works() {
        var result = _bridge.Eval(@"
            var h = new Headers({ 'X-Remove': 'value' });
            h.delete('X-Remove');
            h.has('x-remove') ? 'yes' : 'no';
        ");
        Assert.AreEqual("no", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Headers_Keys_Works() {
        var result = _bridge.Eval(@"
            var h = new Headers({ 'A': '1', 'B': '2' });
            h.keys().sort().join(',');
        ");
        Assert.AreEqual("a,b", result);
        yield return null;
    }

    // MARK: Fetch GET Tests

    [UnityTest]
    public IEnumerator Fetch_SimpleGet_ReturnsResponse() {
        // Use httpbin.org for testing
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/get')
                .then(function(response) {
                    globalThis.__fetchTestResult = {
                        ok: response.ok,
                        status: response.status
                    };
                    globalThis.__fetchTestDone = true;
                })
                .catch(function(err) {
                    globalThis.__fetchTestResult = { error: err.message };
                    globalThis.__fetchTestDone = true;
                });
        ");
        _bridge.Context.ExecutePendingJobs();

        // Wait for fetch to complete (max 10 seconds)
        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"ok\":true"), $"Expected ok:true, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"status\":200"), $"Expected status:200, got: {resultJson}");
    }

    [UnityTest]
    public IEnumerator Fetch_GetJson_ParsesCorrectly() {
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/json')
                .then(function(response) {
                    return response.json();
                })
                .then(function(data) {
                    globalThis.__fetchTestResult = { hasSlideshow: 'slideshow' in data };
                    globalThis.__fetchTestDone = true;
                })
                .catch(function(err) {
                    globalThis.__fetchTestResult = { error: err.message };
                    globalThis.__fetchTestDone = true;
                });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"hasSlideshow\":true"), $"Expected hasSlideshow:true, got: {resultJson}");
    }

    // MARK: Fetch POST Tests

    [UnityTest]
    public IEnumerator Fetch_PostJson_SendsBody() {
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/post', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ test: 'hello' })
            })
            .then(function(response) {
                return response.json();
            })
            .then(function(data) {
                // httpbin echoes back the posted data in 'json' field
                globalThis.__fetchTestResult = {
                    receivedTest: data.json ? data.json.test : null
                };
                globalThis.__fetchTestDone = true;
            })
            .catch(function(err) {
                globalThis.__fetchTestResult = { error: err.message };
                globalThis.__fetchTestDone = true;
            });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"receivedTest\":\"hello\""), $"Expected receivedTest:hello, got: {resultJson}");
    }

    // MARK: Response Object Tests

    [UnityTest]
    public IEnumerator Response_TextMethod_Works() {
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/robots.txt')
                .then(function(response) {
                    return response.text();
                })
                .then(function(text) {
                    globalThis.__fetchTestResult = { hasUserAgent: text.includes('User-agent') };
                    globalThis.__fetchTestDone = true;
                })
                .catch(function(err) {
                    globalThis.__fetchTestResult = { error: err.message };
                    globalThis.__fetchTestDone = true;
                });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"hasUserAgent\":true"), $"Expected hasUserAgent:true, got: {resultJson}");
    }

    [UnityTest]
    public IEnumerator Response_Headers_Accessible() {
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/get')
                .then(function(response) {
                    var contentType = response.headers.get('content-type');
                    globalThis.__fetchTestResult = { hasContentType: contentType !== null };
                    globalThis.__fetchTestDone = true;
                })
                .catch(function(err) {
                    globalThis.__fetchTestResult = { error: err.message };
                    globalThis.__fetchTestDone = true;
                });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"hasContentType\":true"), $"Expected hasContentType:true, got: {resultJson}");
    }

    // MARK: Error Handling Tests

    [UnityTest]
    public IEnumerator Fetch_404_SetsOkFalse() {
        _bridge.Eval(@"
            globalThis.__fetchTestDone = false;
            globalThis.__fetchTestResult = null;
            fetch('https://httpbin.org/status/404')
                .then(function(response) {
                    globalThis.__fetchTestResult = {
                        ok: response.ok,
                        status: response.status
                    };
                    globalThis.__fetchTestDone = true;
                })
                .catch(function(err) {
                    globalThis.__fetchTestResult = { error: err.message };
                    globalThis.__fetchTestDone = true;
                });
        ");
        _bridge.Context.ExecutePendingJobs();

        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout) {
            _bridge.Tick();
            var done = _bridge.Eval("globalThis.__fetchTestDone");
            if (done == "true") break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        var resultJson = _bridge.Eval("JSON.stringify(globalThis.__fetchTestResult)");
        Assert.IsTrue(resultJson.Contains("\"ok\":false"), $"Expected ok:false, got: {resultJson}");
        Assert.IsTrue(resultJson.Contains("\"status\":404"), $"Expected status:404, got: {resultJson}");
    }
}
