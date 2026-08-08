using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for the fetch API implementation.
    /// Hermetic: a loopback HttpListener serves canned httpbin-style responses, so the
    /// suite needs no internet and cannot flake on third-party outages. Requests still
    /// go through the full UnityWebRequest fetch path.
    /// Plain HTTP to the loopback server requires the insecure-HTTP player setting;
    /// IPrebuildSetup flips it before the run and IPostBuildCleanup restores it.
    /// </summary>
    [TestFixture]
    public class QuickJSNetworkTests : IPrebuildSetup, IPostBuildCleanup {
        const string SavedHttpOptionKey = "OneJS.NetworkTests.SavedInsecureHttpOption";

        GameObject _go;
        UIDocument _uiDocument;
        PanelSettings _panelSettings;
        QuickJSUIBridge _bridge;
        static LocalHttpServer _server;

        public void Setup() {
            // Editor-side, before entering Play mode for the test run.
            SessionState.SetInt(SavedHttpOptionKey, (int)PlayerSettings.insecureHttpOption);
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        }

        public void Cleanup() {
            PlayerSettings.insecureHttpOption =
                (InsecureHttpOption)SessionState.GetInt(SavedHttpOptionKey, (int)InsecureHttpOption.NotAllowed);
            SessionState.EraseInt(SavedHttpOptionKey);
        }

        [OneTimeSetUp]
        public void FixtureSetUp() {
            _server = new LocalHttpServer();
        }

        [OneTimeTearDown]
        public void FixtureTearDown() {
            _server?.Dispose();
            _server = null;
        }

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
            _bridge.Eval($"globalThis.__testBase = \"{_server.BaseUrl}\";");

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;

            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_panelSettings != null) UnityEngine.Object.Destroy(_panelSettings);

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

        [UnityTest]
        public IEnumerator Headers_PairArrayInit_Works() {
            // Spec: sequence-of-pairs init, duplicate names combine via append
            var result = _bridge.Eval(@"
                var h = new Headers([['A', '1'], ['a', '2']]);
                h.get('a');
            ");
            Assert.AreEqual("1, 2", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Headers_HeadersInit_CopiesEntries() {
            var result = _bridge.Eval(@"
                var src = new Headers({ 'X-One': '1' });
                var h = new Headers(src);
                h.get('x-one');
            ");
            Assert.AreEqual("1", result);
            yield return null;
        }

        // MARK: Fetch GET Tests

        [UnityTest]
        public IEnumerator Fetch_SimpleGet_ReturnsResponse() {
            _bridge.Eval(@"
                globalThis.__fetchTestDone = false;
                globalThis.__fetchTestResult = null;
                fetch(__testBase + 'get')
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
                fetch(__testBase + 'json')
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
                fetch(__testBase + 'post', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ test: 'hello' })
                })
                .then(function(response) {
                    return response.json();
                })
                .then(function(data) {
                    // the test server echoes the posted data back in the 'json' field
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

        // MARK: Request Header Tests

        [UnityTest]
        public IEnumerator Fetch_HeadersInstance_SendsHeaders() {
            // Headers instances must be flattened before the C# crossing.
            // Regression: JSON.stringify(new Headers(...)) serialized internal
            // fields and silently dropped every header (broke supabase-js).
            _bridge.Eval(@"
                globalThis.__fetchTestDone = false;
                globalThis.__fetchTestResult = null;
                var h = new Headers({ 'apikey': 'secret123', 'X-Custom': 'abc' });
                fetch(__testBase + 'echo-headers', { headers: h })
                    .then(function(response) {
                        return response.json();
                    })
                    .then(function(data) {
                        globalThis.__fetchTestResult = {
                            apikey: data.headers['apikey'] || null,
                            custom: data.headers['x-custom'] || null
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
            Assert.IsTrue(resultJson.Contains("\"apikey\":\"secret123\""), $"Expected apikey to arrive, got: {resultJson}");
            Assert.IsTrue(resultJson.Contains("\"custom\":\"abc\""), $"Expected x-custom to arrive, got: {resultJson}");
        }

        [UnityTest]
        public IEnumerator Fetch_HeaderPairsArray_SendsHeaders() {
            _bridge.Eval(@"
                globalThis.__fetchTestDone = false;
                globalThis.__fetchTestResult = null;
                fetch(__testBase + 'echo-headers', { headers: [['X-Pairs', 'pairs123']] })
                    .then(function(response) {
                        return response.json();
                    })
                    .then(function(data) {
                        globalThis.__fetchTestResult = { pairs: data.headers['x-pairs'] || null };
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
            Assert.IsTrue(resultJson.Contains("\"pairs\":\"pairs123\""), $"Expected x-pairs to arrive, got: {resultJson}");
        }

        [UnityTest]
        public IEnumerator Fetch_ObjectBody_KeepsExistingContentType() {
            // Auto-JSON Content-Type must not clobber a caller-set header,
            // regardless of the caller's casing
            _bridge.Eval(@"
                globalThis.__fetchTestDone = false;
                globalThis.__fetchTestResult = null;
                fetch(__testBase + 'echo-headers', {
                    method: 'POST',
                    headers: { 'content-type': 'text/plain' },
                    body: { ignored: true }
                })
                    .then(function(response) {
                        return response.json();
                    })
                    .then(function(data) {
                        globalThis.__fetchTestResult = { contentType: data.headers['content-type'] || null };
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
            Assert.IsTrue(resultJson.Contains("text/plain"), $"Expected caller content-type to survive, got: {resultJson}");
        }

        // MARK: Response Object Tests

        [UnityTest]
        public IEnumerator Response_TextMethod_Works() {
            _bridge.Eval(@"
                globalThis.__fetchTestDone = false;
                globalThis.__fetchTestResult = null;
                fetch(__testBase + 'robots.txt')
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
                fetch(__testBase + 'get')
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
                fetch(__testBase + 'status/404')
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

        // MARK: Local Test Server

        /// <summary>
        /// Minimal loopback HTTP server with httpbin-style canned responses.
        /// Listens on a free port on localhost; requests are handled on a background
        /// thread, and Dispose unblocks it by stopping the listener.
        /// </summary>
        sealed class LocalHttpServer : IDisposable {
            readonly HttpListener _listener;
            public string BaseUrl { get; }

            public LocalHttpServer() {
                var port = GetFreePort();
                // "localhost" (not 127.0.0.1): exempt from URL ACL registration on Windows.
                BaseUrl = $"http://localhost:{port}/";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl);
                _listener.Start();
                new Thread(Loop) { IsBackground = true, Name = "OneJS.NetworkTestServer" }.Start();
            }

            static int GetFreePort() {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }

            void Loop() {
                while (_listener.IsListening) {
                    HttpListenerContext ctx;
                    try { ctx = _listener.GetContext(); } catch { return; }
                    try { Handle(ctx); } catch { try { ctx.Response.Abort(); } catch { } }
                }
            }

            void Handle(HttpListenerContext ctx) {
                switch (ctx.Request.Url.AbsolutePath) {
                    case "/get":
                        WriteJson(ctx, 200, "{\"url\": \"" + BaseUrl + "get\", \"args\": {}}");
                        break;
                    case "/json":
                        WriteJson(ctx, 200, "{\"slideshow\": {\"title\": \"Sample Slide Show\"}}");
                        break;
                    case "/post": {
                        string body;
                        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding)) {
                            body = reader.ReadToEnd();
                        }
                        WriteJson(ctx, 200, "{\"json\": " + (string.IsNullOrEmpty(body) ? "null" : body) + "}");
                        break;
                    }
                    case "/echo-headers": {
                        var sb = new StringBuilder();
                        sb.Append("{\"headers\":{");
                        bool first = true;
                        foreach (string key in ctx.Request.Headers.AllKeys) {
                            if (!first) sb.Append(",");
                            first = false;
                            var value = ctx.Request.Headers[key] ?? "";
                            sb.Append("\"").Append(key.ToLowerInvariant()).Append("\":\"")
                                .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"");
                        }
                        sb.Append("}}");
                        WriteJson(ctx, 200, sb.ToString());
                        break;
                    }
                    case "/robots.txt":
                        Write(ctx, 200, "text/plain", "User-agent: *\nDisallow: /deny\n");
                        break;
                    case "/status/404":
                    default:
                        WriteJson(ctx, 404, "{\"error\": \"not found\"}");
                        break;
                }
            }

            static void WriteJson(HttpListenerContext ctx, int status, string json) {
                Write(ctx, status, "application/json", json);
            }

            static void Write(HttpListenerContext ctx, int status, string contentType, string content) {
                var bytes = Encoding.UTF8.GetBytes(content);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }

            public void Dispose() {
                try {
                    _listener.Stop();
                    _listener.Close();
                } catch { }
            }
        }
    }
}
