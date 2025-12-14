using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Playmode tests for QuickJSUIBridge event delegation and scheduling.
/// Creates UIDocument and PanelSettings programmatically - no external assets required.
/// </summary>
[TestFixture]
public class QuickJSUIBridgePlaymodeTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

        // Create GameObject with UIDocument
        _go = new GameObject("UIBridgeTestHost");
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

    // MARK: Scheduling Tests

    [UnityTest]
    public IEnumerator Scheduling_RequestAnimationFrame_Exists() {
        var result = _bridge.Eval("typeof requestAnimationFrame");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scheduling_SetTimeout_Exists() {
        var result = _bridge.Eval("typeof setTimeout");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scheduling_SetInterval_Exists() {
        var result = _bridge.Eval("typeof setInterval");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scheduling_PerformanceNow_Exists() {
        var result = _bridge.Eval("typeof performance.now");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scheduling_QueueMicrotask_Exists() {
        var result = _bridge.Eval("typeof queueMicrotask");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Scheduling_RAFCallback_InvokedOnTick() {
        _bridge.Eval(@"
            globalThis.__rafTestResult = 0;
            requestAnimationFrame((ts) => {
                globalThis.__rafTestResult = ts > 0 ? 1 : -1;
            });
        ");

        _bridge.Tick();
        yield return null;

        var result = _bridge.Eval("globalThis.__rafTestResult");
        Assert.AreEqual("1", result);
    }

    [UnityTest]
    public IEnumerator Scheduling_SetTimeout_FiresAfterTick() {
        _bridge.Eval(@"
            globalThis.__timeoutResult = 0;
            setTimeout(() => {
                globalThis.__timeoutResult = 42;
            }, 0);
        ");

        _bridge.Tick();
        yield return null;

        var result = _bridge.Eval("globalThis.__timeoutResult");
        Assert.AreEqual("42", result);
    }

    [UnityTest]
    public IEnumerator Scheduling_ClearTimeout_PreventsExecution() {
        _bridge.Eval(@"
            globalThis.__clearResult = 'initial';
            var id = setTimeout(() => {
                globalThis.__clearResult = 'should not see this';
            }, 0);
            clearTimeout(id);
        ");

        _bridge.Tick();
        yield return null;

        var result = _bridge.Eval("globalThis.__clearResult");
        Assert.AreEqual("initial", result);
    }

    [UnityTest]
    public IEnumerator Scheduling_CancelAnimationFrame_PreventsExecution() {
        _bridge.Eval(@"
            globalThis.__cancelRafResult = 'initial';
            var id = requestAnimationFrame(() => {
                globalThis.__cancelRafResult = 'should not see this';
            });
            cancelAnimationFrame(id);
        ");

        _bridge.Tick();
        yield return null;

        var result = _bridge.Eval("globalThis.__cancelRafResult");
        Assert.AreEqual("initial", result);
    }

    // MARK: Event Tests

    [UnityTest]
    public IEnumerator Event_EventAPI_Exists() {
        var result = _bridge.Eval("typeof globalThis.__eventAPI");
        Assert.AreEqual("object", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_AddEventListener_Available() {
        var result = _bridge.Eval("typeof __eventAPI.addEventListener");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_DispatchEvent_Available() {
        var result = _bridge.Eval("typeof globalThis.__dispatchEvent");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_RegisterAndDispatchClick_Works() {
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

        var handle = _bridge.Eval("globalThis.__testBtnHandle");
        _bridge.Eval($"__dispatchEvent({handle}, 'click', {{ x: 100, y: 200 }})");

        var result = _bridge.Eval("JSON.stringify(globalThis.__clickTestResult)");
        StringAssert.Contains("100", result);
        StringAssert.Contains("200", result);
        StringAssert.Contains("click", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_MultipleHandlers_BothCalled() {
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
        Assert.AreEqual("handler1,handler2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_RemoveEventListener_Works() {
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

        Assert.AreEqual("1", count1);
        Assert.AreEqual("1", count2);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_RemoveAllEventListeners_ClearsAllHandlers() {
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
        Assert.AreEqual("0", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Event_EventData_PassedCorrectly() {
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
        StringAssert.Contains("\"type\":\"pointerdown\"", result);
        StringAssert.Contains("\"x\":50", result);
        StringAssert.Contains("\"y\":75", result);
        StringAssert.Contains("\"button\":1", result);
        StringAssert.Contains("\"hasPreventDefault\":true", result);
        yield return null;
    }
}

