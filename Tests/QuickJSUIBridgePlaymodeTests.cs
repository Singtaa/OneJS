using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");


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

    // MARK: Pointer Capture Tests

    [UnityTest]
    public IEnumerator PointerCapture_UseExtensions_CaptureAndRelease_Works() {
        // Add element to the panel (required for pointer capture)
        var root = _uiDocument.rootVisualElement;
        int rootHandle = QuickJSNative.RegisterObject(root);

        _bridge.Eval($@"
            var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
            useExtensions(CS.UnityEngine.UIElements.PointerCaptureHelper);

            var el = new CS.UnityEngine.UIElements.VisualElement();
            el.name = 'CaptureTestElement';
            el.style.width = 100;
            el.style.height = 100;
            root.Add(el);

            globalThis.__captureTestEl = el;
        ");

        yield return null; // Wait for layout

        // Now test capture
        var captureResult = _bridge.Eval(@"
            try {
                var el = globalThis.__captureTestEl;
                el.CapturePointer(0);
                var hasCap = el.HasPointerCapture(0);
                el.ReleasePointer(0);
                var hasCapAfter = el.HasPointerCapture(0);
                JSON.stringify({ captured: hasCap, released: !hasCapAfter });
            } catch(e) {
                JSON.stringify({ error: e.message });
            }
        ");

        StringAssert.Contains("\"captured\":true", captureResult);
        StringAssert.Contains("\"released\":true", captureResult);
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

    // MARK: Pointer Capture + PointerMove Tests

    /// <summary>
    /// Integration test: Does the JS onPointerMove handler fire when a captured
    /// PointerMoveEvent propagates through QuickJSUIBridge's TrickleDown handler?
    /// </summary>
    [UnityTest]
    public IEnumerator PointerCapture_PointerMove_JSHandlerFiresDuringCapture() {
        var root = _uiDocument.rootVisualElement;
        int rootHandle = QuickJSNative.RegisterObject(root);

        _bridge.Eval($@"
            var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
            useExtensions(CS.UnityEngine.UIElements.PointerCaptureHelper);

            var el = new CS.UnityEngine.UIElements.VisualElement();
            el.name = 'PointerMoveCaptureTest';
            el.style.width = 200;
            el.style.height = 200;
            root.Add(el);

            globalThis.__pmCaptureMoveCount = 0;
            globalThis.__pmCaptureLastEvent = null;

            __eventAPI.addEventListener(el, 'pointermove', (e) => {{
                globalThis.__pmCaptureMoveCount++;
                globalThis.__pmCaptureLastEvent = {{ type: e.type, x: e.x, y: e.y }};
            }});

            globalThis.__pmCaptureTestEl = el;
            globalThis.__pmCaptureTestHandle = el.__csHandle;
        ");

        yield return null; // layout

        // Capture pointer
        _bridge.Eval("globalThis.__pmCaptureTestEl.CapturePointer(0)");
        var hasCap = _bridge.Eval("globalThis.__pmCaptureTestEl.HasPointerCapture(0)");
        Assert.AreEqual("true", hasCap, "Element should have pointer capture");

        // Send synthetic PointerMoveEvent through the panel
        using (var evt = PointerMoveEvent.GetPooled()) {
            SetPointerEventPointerId(evt, PointerId.mousePointerId);
            root.SendEvent(evt);
        }

        var count = _bridge.Eval("globalThis.__pmCaptureMoveCount");
        Debug.Log($"[PointerCapture Integration] JS pointermove handler called {count} time(s)");

        Assert.AreEqual("1", count,
            "JS onPointerMove handler should fire during pointer capture. " +
            "If 0, the C# TrickleDown handler may not fire for captured events.");

        // Cleanup
        _bridge.Eval("globalThis.__pmCaptureTestEl.ReleasePointer(0)");
    }

    // MARK: Native Event Suppression + Wheel (confirmation for PR #104)
    // These validate the planned design BEFORE it is wired into JS:
    //  - A TrickleDown handler on the bridge root that calls StopImmediatePropagation()
    //    prevents descendant elements (e.g. a nested ScrollView) from acting. This is the
    //    mechanism the planned `preventDefault -> StopImmediatePropagation` will rely on.
    //  - The bridge currently does NOT dispatch WheelEvent to JS (wheel is unbridged).

    [UnityTest]
    public IEnumerator Suppression_RootTrickleDownStop_PreventsDescendantPointerDown() {
        var root = _uiDocument.rootVisualElement;

        var child = new VisualElement { name = "SuppressChild" };
        child.style.width = 100;
        child.style.height = 100;
        root.Add(child);
        yield return null; // layout

        bool childGotDown = false;
        child.RegisterCallback<PointerDownEvent>(_ => childGotDown = true);

        // Baseline: with nothing intercepting, the descendant receives PointerDown.
        using (var evt = PointerDownEvent.GetPooled()) {
            evt.target = child;
            root.SendEvent(evt);
        }
        Assert.IsTrue(childGotDown,
            "Baseline: descendant should receive PointerDown when nothing intercepts it.");

        // Fix mechanism: a TrickleDown handler on root that stops immediate propagation
        // must prevent the descendant from receiving the event at all.
        childGotDown = false;
        EventCallback<PointerDownEvent> stopper = e => e.StopImmediatePropagation();
        root.RegisterCallback(stopper, TrickleDown.TrickleDown);
        using (var evt = PointerDownEvent.GetPooled()) {
            evt.target = child;
            root.SendEvent(evt);
        }
        root.UnregisterCallback(stopper, TrickleDown.TrickleDown);

        Assert.IsFalse(childGotDown,
            "Fix: StopImmediatePropagation() in a root TrickleDown handler must prevent the " +
            "descendant from receiving PointerDown. This is the path that lets a JS gesture " +
            "suppress a nested ScrollView's native pan.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Suppression_RootTrickleDownStop_StopsScrollViewWheelScroll() {
        var root = _uiDocument.rootVisualElement;

        var sv = new ScrollView(ScrollViewMode.Vertical);
        sv.style.width = 200;
        sv.style.height = 200;
        var content = new VisualElement { name = "TallContent" };
        content.style.height = 2000; // taller than the viewport, so it can scroll
        sv.Add(content);
        root.Add(sv);
        yield return null;
        yield return null; // a couple frames for layout

        // Baseline: a positive wheel delta scrolls the ScrollView down.
        sv.scrollOffset = Vector2.zero;
        SendWheel(root, content, 50f);
        yield return null;
        float scrolled = sv.scrollOffset.y;
        Assert.Greater(scrolled, 0f,
            $"Baseline: ScrollView should scroll on a wheel delta. (scrollOffset.y = {scrolled})");

        // Fix mechanism: root TrickleDown StopImmediatePropagation must keep scrollOffset put.
        sv.scrollOffset = Vector2.zero;
        EventCallback<WheelEvent> stopper = e => e.StopImmediatePropagation();
        root.RegisterCallback(stopper, TrickleDown.TrickleDown);
        SendWheel(root, content, 50f);
        yield return null;
        float suppressed = sv.scrollOffset.y;
        root.UnregisterCallback(stopper, TrickleDown.TrickleDown);

        Assert.AreEqual(0f, suppressed, 0.0001f,
            "Fix: with StopImmediatePropagation() in a root TrickleDown WheelEvent handler, the " +
            $"ScrollView must not scroll. (scrollOffset.y = {suppressed})");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Wheel_DispatchedToJsHandlerWithDelta() {
        var root = _uiDocument.rootVisualElement;
        int rootHandle = QuickJSNative.RegisterObject(root);

        // Create a JS element that fills the panel and register onWheel on it (mirrors the
        // pointer-capture test's JS-created-element pattern, which has correct handle
        // bookkeeping). A WheelEvent at a point inside it picks it as the target, driving
        // the real OnWheel -> FindElementHandle -> __dispatchEvent path end to end.
        _bridge.Eval($@"
            var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
            var el = new CS.UnityEngine.UIElements.VisualElement();
            el.style.flexGrow = 1;
            el.style.width = new CS.UnityEngine.UIElements.Length(100, CS.UnityEngine.UIElements.LengthUnit.Percent);
            el.style.height = new CS.UnityEngine.UIElements.Length(100, CS.UnityEngine.UIElements.LengthUnit.Percent);
            root.Add(el);
            globalThis.__wheelEl = el;
            globalThis.__wheelCount = 0;
            globalThis.__wheelDeltaY = 0;
            __eventAPI.addEventListener(el, 'wheel', (e) => {{
                globalThis.__wheelCount++;
                globalThis.__wheelDeltaY = e.deltaY;
            }});
        ");
        yield return null;
        yield return null; // layout so the element fills the panel and is pickable

        int elHandle = int.Parse(_bridge.Eval("globalThis.__wheelEl.__csHandle"));
        var elCs = QuickJSNative.GetObjectByHandle(elHandle) as VisualElement;
        Assert.IsNotNull(elCs, "Should resolve the JS-created element back to C#.");

        var systemEvent = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 10f), mousePosition = new Vector2(10f, 10f) };
        using (var evt = WheelEvent.GetPooled(systemEvent)) {
            evt.target = elCs;
            root.SendEvent(evt);
        }
        yield return null;

        // Phase 1: WheelEvent is now bridged, so the JS onWheel handler fires with delta.
        Assert.AreEqual("1", _bridge.Eval("globalThis.__wheelCount"),
            $"Real WheelEvent should reach JS via the bridge (elHandle={elHandle}).");
        Assert.AreEqual("true", _bridge.Eval("globalThis.__wheelDeltaY > 0"),
            "The wheel deltaY should be forwarded to JS (positive for a downward scroll).");
    }

    [UnityTest]
    public IEnumerator Suppression_PreventDefaultInOnWheel_StopsScrollViewScroll() {
        // End-to-end Phase 2: a JS onWheel handler calling preventDefault() must stop the
        // enclosing ScrollView from scrolling (native suppression). Without preventDefault
        // the ScrollView still scrolls (backward-compatible).
        var root = _uiDocument.rootVisualElement;
        int rootHandle = QuickJSNative.RegisterObject(root);

        _bridge.Eval($@"
            var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
            var sv = new CS.UnityEngine.UIElements.ScrollView();
            sv.style.width = 200; sv.style.height = 200;
            root.Add(sv);
            var content = new CS.UnityEngine.UIElements.VisualElement();
            content.style.height = 2000;
            sv.Add(content);
            globalThis.__sv = sv;
            globalThis.__content = content;
            globalThis.__doPreventDefault = false;
            __eventAPI.addEventListener(content, 'wheel', (e) => {{
                if (globalThis.__doPreventDefault) e.preventDefault();
            }});
        ");
        yield return null;
        yield return null;

        int contentHandle = int.Parse(_bridge.Eval("globalThis.__content.__csHandle"));
        var contentCs = QuickJSNative.GetObjectByHandle(contentHandle) as VisualElement;
        int svHandle = int.Parse(_bridge.Eval("globalThis.__sv.__csHandle"));
        var sv = QuickJSNative.GetObjectByHandle(svHandle) as ScrollView;
        Assert.IsNotNull(sv, "ScrollView should resolve back to C#.");

        // Backward-compat: without preventDefault, the wheel scrolls the ScrollView.
        sv.scrollOffset = Vector2.zero;
        _bridge.Eval("globalThis.__doPreventDefault = false;");
        SendWheel(root, contentCs, 50f);
        yield return null;
        Assert.Greater(sv.scrollOffset.y, 0f,
            $"Without preventDefault the ScrollView should scroll. (scrollOffset.y={sv.scrollOffset.y})");

        // Suppression: with preventDefault, the wheel must not scroll the ScrollView.
        sv.scrollOffset = Vector2.zero;
        _bridge.Eval("globalThis.__doPreventDefault = true;");
        SendWheel(root, contentCs, 50f);
        yield return null;
        Assert.AreEqual(0f, sv.scrollOffset.y, 0.0001f,
            $"preventDefault() in onWheel must stop the ScrollView from scrolling. (scrollOffset.y={sv.scrollOffset.y})");
    }

    // Helper: dispatch a synthetic vertical wheel scroll targeting `target`.
    static void SendWheel(VisualElement root, VisualElement target, float deltaY) {
        var systemEvent = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, deltaY) };
        using (var evt = WheelEvent.GetPooled(systemEvent)) {
            evt.target = target;
            root.SendEvent(evt);
        }
    }

    /// <summary>
    /// Helper: set pointerId on a PointerEventBase via reflection.
    /// PointerEventBase.pointerId has a protected setter, so we use reflection for tests.
    /// </summary>
    static void SetPointerEventPointerId(EventBase evt, int pointerId) {
        // Walk the type hierarchy to find the pointerId property
        var type = evt.GetType();
        while (type != null) {
            var prop = type.GetProperty("pointerId",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (prop != null) {
                var setter = prop.GetSetMethod(true); // true = include non-public
                if (setter != null) {
                    setter.Invoke(evt, new object[] { pointerId });
                    return;
                }
            }
            type = type.BaseType;
        }

        // Fallback: try the backing field directly
        type = evt.GetType();
        while (type != null) {
            var field = type.GetField("<pointerId>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) {
                field.SetValue(evt, pointerId);
                return;
            }
            // Also try common Unity field naming conventions
            field = type.GetField("m_PointerId",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) {
                field.SetValue(evt, pointerId);
                return;
            }
            type = type.BaseType;
        }

        Debug.LogWarning("[Test] Could not set pointerId via reflection - " +
                         "test may not properly simulate captured pointer events");
    }
}