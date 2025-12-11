using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Tests for QuickJSUIBridge event delegation and scheduling.
/// Attach to a GameObject with UIDocument.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class QuickJSUIBridgeTest : MonoBehaviour {
    UIDocument _uiDocument;
    QuickJSUIBridge _bridge;
    int _passed;
    int _failed;

    void Awake() {
        _uiDocument = GetComponent<UIDocument>();
    }

    void Start() {
        var root = _uiDocument.rootVisualElement;
        _bridge = new QuickJSUIBridge(root);

        Log("=== QuickJSUIBridge Tests ===");

        RunSchedulingTests();
        RunEventTests();

        LogSummary();
    }

    void Update() {
        _bridge?.Tick();
    }

    void OnDestroy() {
        Log($"Final handle count: {QuickJSNative.GetHandleCount()}");
        _bridge?.Dispose();
        _bridge = null;
        QuickJSNative.ClearAllHandles();
    }

    // MARK: Scheduling
    void RunSchedulingTests() {
        Log("--- Scheduling Tests ---");

        Assert("requestAnimationFrame exists", () => {
            var result = _bridge.Eval("typeof requestAnimationFrame");
            return result == "function";
        });

        Assert("setTimeout exists", () => {
            var result = _bridge.Eval("typeof setTimeout");
            return result == "function";
        });

        Assert("setInterval exists", () => {
            var result = _bridge.Eval("typeof setInterval");
            return result == "function";
        });

        Assert("performance.now exists", () => {
            var result = _bridge.Eval("typeof performance.now");
            return result == "function";
        });

        Assert("RAF callback invoked on tick", () => {
            _bridge.Eval(@"
                globalThis.__rafTestResult = 0;
                requestAnimationFrame((ts) => {
                    globalThis.__rafTestResult = ts > 0 ? 1 : -1;
                });
            ");
            // Tick to execute
            _bridge.Tick();
            var result = _bridge.Eval("globalThis.__rafTestResult");
            return result == "1";
        });

        Assert("setTimeout fires after tick", () => {
            _bridge.Eval(@"
                globalThis.__timeoutResult = 0;
                setTimeout(() => {
                    globalThis.__timeoutResult = 42;
                }, 0);
            ");
            _bridge.Tick();
            var result = _bridge.Eval("globalThis.__timeoutResult");
            return result == "42";
        });

        Assert("clearTimeout prevents execution", () => {
            _bridge.Eval(@"
                globalThis.__clearResult = 'initial';
                var id = setTimeout(() => {
                    globalThis.__clearResult = 'should not see this';
                }, 0);
                clearTimeout(id);
            ");
            _bridge.Tick();
            var result = _bridge.Eval("globalThis.__clearResult");
            return result == "initial";
        });

        Assert("cancelAnimationFrame prevents execution", () => {
            _bridge.Eval(@"
                globalThis.__cancelRafResult = 'initial';
                var id = requestAnimationFrame(() => {
                    globalThis.__cancelRafResult = 'should not see this';
                });
                cancelAnimationFrame(id);
            ");
            _bridge.Tick();
            var result = _bridge.Eval("globalThis.__cancelRafResult");
            return result == "initial";
        });

        Assert("queueMicrotask exists", () => {
            var result = _bridge.Eval("typeof queueMicrotask");
            return result == "function";
        });
    }

    // MARK: Events
    void RunEventTests() {
        Log("--- Event Tests ---");

        Assert("__eventAPI exists", () => {
            var result = _bridge.Eval("typeof globalThis.__eventAPI");
            return result == "object";
        });

        Assert("addEventListener available", () => {
            var result = _bridge.Eval("typeof __eventAPI.addEventListener");
            return result == "function";
        });

        Assert("__dispatchEvent available", () => {
            var result = _bridge.Eval("typeof globalThis.__dispatchEvent");
            return result == "function";
        });

        Assert("Register and dispatch click event", () => {
            _bridge.Eval(@"
                globalThis.__clickTestResult = null;
                var btn = new CS.UnityEngine.UIElements.Button();
                btn.name = 'EventTestBtn';
                btn.text = 'Click Test';
                
                __eventAPI.addEventListener(btn, 'click', (e) => {
                    globalThis.__clickTestResult = { x: e.x, y: e.y, type: e.type };
                });
                
                globalThis.__testBtnHandle = btn.__csHandle;
            ");

            // Simulate dispatch from C# (normally this comes from actual UI events)
            var handle = _bridge.Eval("globalThis.__testBtnHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{ x: 100, y: 200 }})");

            var result = _bridge.Eval("JSON.stringify(globalThis.__clickTestResult)");
            return result.Contains("100") && result.Contains("200") && result.Contains("click");
        });

        Assert("Multiple handlers for same event", () => {
            _bridge.Eval(@"
                globalThis.__multiResult = [];
                var el = new CS.UnityEngine.UIElements.VisualElement();
                
                __eventAPI.addEventListener(el, 'click', () => {
                    globalThis.__multiResult.push('handler1');
                });
                __eventAPI.addEventListener(el, 'click', () => {
                    globalThis.__multiResult.push('handler2');
                });
                
                globalThis.__multiElHandle = el.__csHandle;
            ");

            var handle = _bridge.Eval("globalThis.__multiElHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{}})");

            var result = _bridge.Eval("globalThis.__multiResult.join(',')");
            return result == "handler1,handler2";
        });

        Assert("removeEventListener works", () => {
            _bridge.Eval(@"
                globalThis.__removeResult = [];
                var el = new CS.UnityEngine.UIElements.VisualElement();
                
                function handler() {
                    globalThis.__removeResult.push('called');
                }
                
                __eventAPI.addEventListener(el, 'click', handler);
                globalThis.__removeElHandle = el.__csHandle;
                globalThis.__removeHandler = handler;
            ");

            var handle = _bridge.Eval("globalThis.__removeElHandle");

            // First dispatch - should fire
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{}})");
            var count1 = _bridge.Eval("globalThis.__removeResult.length");

            // Remove handler
            _bridge.Eval(@"
                var el = { __csHandle: globalThis.__removeElHandle };
                __eventAPI.removeEventListener(el, 'click', globalThis.__removeHandler);
            ");

            // Second dispatch - should not fire
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{}})");
            var count2 = _bridge.Eval("globalThis.__removeResult.length");

            return count1 == "1" && count2 == "1";
        });

        Assert("removeAllEventListeners clears all handlers", () => {
            _bridge.Eval(@"
                globalThis.__clearAllResult = 0;
                var el = new CS.UnityEngine.UIElements.VisualElement();
                
                __eventAPI.addEventListener(el, 'click', () => globalThis.__clearAllResult++);
                __eventAPI.addEventListener(el, 'pointerdown', () => globalThis.__clearAllResult++);
                
                globalThis.__clearAllHandle = el.__csHandle;
                __eventAPI.removeAllEventListeners(el);
            ");

            var handle = _bridge.Eval("globalThis.__clearAllHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{}})");
            _bridge.Eval($"__dispatchEvent({handle}, 'pointerdown', {{}})");

            var result = _bridge.Eval("globalThis.__clearAllResult");
            return result == "0";
        });

        Assert("Event data passed correctly", () => {
            _bridge.Eval(@"
                globalThis.__eventDataResult = null;
                var el = new CS.UnityEngine.UIElements.VisualElement();
                
                __eventAPI.addEventListener(el, 'pointerdown', (e) => {
                    globalThis.__eventDataResult = {
                        type: e.type,
                        x: e.x,
                        y: e.y,
                        button: e.button,
                        hasPreventDefault: typeof e.preventDefault === 'function'
                    };
                });
                
                globalThis.__dataTestHandle = el.__csHandle;
            ");

            var handle = _bridge.Eval("globalThis.__dataTestHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'pointerdown', {{ x: 50, y: 75, button: 1 }})");

            var result = _bridge.Eval("JSON.stringify(globalThis.__eventDataResult)");
            return result.Contains("\"type\":\"pointerdown\"") &&
                   result.Contains("\"x\":50") &&
                   result.Contains("\"y\":75") &&
                   result.Contains("\"button\":1") &&
                   result.Contains("\"hasPreventDefault\":true");
        });
    }

    // MARK: Helpers
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

    void Log(string msg) => Debug.Log($"[UIBridgeTest] {msg}");

    void LogSummary() {
        var total = _passed + _failed;
        var status = _failed == 0 ? "ALL PASSED" : $"{_failed} FAILED";
        Log($"=== Results: {_passed}/{total} passed ({status}) ===");
    }
}