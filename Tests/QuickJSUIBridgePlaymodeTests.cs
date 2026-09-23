using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Playmode tests for QuickJSUIBridge event delegation and scheduling.
    /// Creates UIDocument and PanelSettings programmatically: no external assets required.
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

            // First dispatch: should fire
            _bridge.Eval($"__dispatchEvent({handle}, 'click', {{}})");
            var count1 = _bridge.Eval("globalThis.__removeResult.length");

            // Remove handler
            _bridge.Eval(@"
                var el = { __csHandle: globalThis.__removeElHandle };
                __eventAPI.removeEventListener(el, 'click', globalThis.__removeHandler);
            ");

            // Second dispatch: should not fire
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
        public IEnumerator Event_LocalXY_AreRelativeToCurrentTarget() {
            // localX and localY come off the synthetic event's prototype and
            // read worldBound on first use, so they need a laid-out element
            // with a real position, which is why this attaches to the panel
            // and waits a frame.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);
            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                globalThis.__localResult = null;
                var outer = new CS.UnityEngine.UIElements.VisualElement();
                outer.style.position = CS.UnityEngine.UIElements.Position.Absolute;
                outer.style.left = 100; outer.style.top = 50;
                outer.style.width = 300; outer.style.height = 200;
                var inner = new CS.UnityEngine.UIElements.VisualElement();
                inner.style.position = CS.UnityEngine.UIElements.Position.Absolute;
                inner.style.left = 20; inner.style.top = 10;
                inner.style.width = 100; inner.style.height = 100;
                outer.Add(inner);
                root.Add(outer);
                __eventAPI.setParent(inner.__csHandle, outer.__csHandle);

                __eventAPI.addEventListener(inner, 'pointerdown', (e) => {{
                    globalThis.__localResult = {{ innerX: e.localX, innerY: e.localY }};
                }});
                // The same event, bubbling: local coordinates follow currentTarget.
                __eventAPI.addEventListener(outer, 'pointerdown', (e) => {{
                    globalThis.__localResult.outerX = e.localX;
                    globalThis.__localResult.outerY = e.localY;
                    globalThis.__localResult.plainX = e.x;
                }});
                globalThis.__localHandle = inner.__csHandle;
            ");
            yield return null;
            yield return null;

            var handle = _bridge.Eval("globalThis.__localHandle");
            _bridge.Eval($"__dispatchEvent({handle}, 'pointerdown', {{ x: 150, y: 90, button: 0 }})");

            // inner sits at panel (120, 60) and outer at (100, 50). The test
            // panel's scale is not exactly 1, so worldBound lands a fraction
            // off the styled position; a tolerance keeps that out of the
            // assertion without hiding a wrong origin.
            float Read(string key) => float.Parse(_bridge.Eval($"String(globalThis.__localResult.{key})"),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(30f, Read("innerX"), 0.5f);
            Assert.AreEqual(30f, Read("innerY"), 0.5f);
            Assert.AreEqual(50f, Read("outerX"), 0.5f);
            Assert.AreEqual(40f, Read("outerY"), 0.5f);
            Assert.AreEqual(150f, Read("plainX"), 0.001f);
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

        // MARK: Native Event Suppression + Wheel (PR #104)
        // End-to-end tests for the wired feature: wheel is bridged to JS, and a JS
        // preventDefault() maps to native StopImmediatePropagation() to suppress nested
        // controls. Covers the root fast path, the per-element captured-pointer path, and
        // the wheel -> ScrollView path.

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
        public IEnumerator Suppression_FastPath_PreventDefaultInOnPointerDown_Suppresses() {
            // Validates the PRODUCTION pointer path. JSRunner caches the fast dispatch callback,
            // so pointer events go through DispatchEventFast -> InvokeCallbackReturnInt (not the
            // string path the other tests use). A JS onPointerDown calling preventDefault() must
            // suppress the native event (a descendant's PointerDown callback does not fire);
            // without it, the descendant fires (backward-compat).
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            // Enable the fast path (mirrors JSRunner) and confirm it actually engaged.
            _bridge.CacheEventDispatchCallback();
            var dispatchHandleField = typeof(QuickJSUIBridge).GetField(
                "_eventDispatchHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            int dispatchHandle = (int)dispatchHandleField.GetValue(_bridge);
            Assert.GreaterOrEqual(dispatchHandle, 0,
                "Fast dispatch path should be enabled so this test exercises DispatchEventFast / InvokeCallbackReturnInt.");

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var child = new CS.UnityEngine.UIElements.VisualElement();
                child.style.flexGrow = 1;
                root.Add(child);
                globalThis.__child = child;
                globalThis.__pdFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(child, 'pointerdown', (e) => {{
                    globalThis.__pdFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            int childHandle = int.Parse(_bridge.Eval("globalThis.__child.__csHandle"));
            var childCs = QuickJSNative.GetObjectByHandle(childHandle) as VisualElement;
            Assert.IsNotNull(childCs, "Child should resolve back to C#.");

            bool childNativeGotDown = false;
            childCs.RegisterCallback<PointerDownEvent>(_ => childNativeGotDown = true);

            // Backward-compat: without preventDefault the native PointerDown reaches the child.
            _bridge.Eval("globalThis.__pdFired = 0; globalThis.__doPreventDefault = false;");
            childNativeGotDown = false;
            using (var evt = PointerDownEvent.GetPooled()) {
                evt.target = childCs;
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pdFired"),
                "JS onPointerDown should fire via the fast path.");
            Assert.IsTrue(childNativeGotDown,
                "Without preventDefault, the child's native PointerDown callback should fire.");

            // Suppression: with preventDefault the native PointerDown is suppressed.
            _bridge.Eval("globalThis.__pdFired = 0; globalThis.__doPreventDefault = true;");
            childNativeGotDown = false;
            using (var evt = PointerDownEvent.GetPooled()) {
                evt.target = childCs;
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pdFired"),
                "JS onPointerDown should still fire via the fast path.");
            Assert.IsFalse(childNativeGotDown,
                "preventDefault() in onPointerDown must suppress the child's native PointerDown (fast path).");
        }

        [UnityTest]
        public IEnumerator Suppression_PerElement_PreventDefaultDuringCapture_Suppresses() {
            // Phase 3a: once a pointer is captured, Unity 6 delivers pointer events directly to the
            // capturing element, bypassing the _root TrickleDown handler: only the per-element
            // handler (OnPerElementPointerMove) fires (see PerElementEventSupport). A JS
            // onPointerMove calling preventDefault() must still suppress the native event mid-drag
            // (e.g. a ScrollView pan); without it the native callback fires (backward-compat).
            // Guards against the per-element handlers discarding the dispatch flags.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            // String dispatch path (no CacheEventDispatchCallback): the path per-element handlers use.
            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                useExtensions(CS.UnityEngine.UIElements.PointerCaptureHelper);
                var child = new CS.UnityEngine.UIElements.VisualElement();
                child.style.width = 200; child.style.height = 200;
                root.Add(child);
                globalThis.__child = child;
                globalThis.__pmFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(child, 'pointermove', (e) => {{
                    globalThis.__pmFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            int childHandle = int.Parse(_bridge.Eval("globalThis.__child.__csHandle"));
            var childCs = QuickJSNative.GetObjectByHandle(childHandle) as VisualElement;
            Assert.IsNotNull(childCs, "Child should resolve back to C#.");

            // Native PointerMove probe on the same element, registered AFTER the per-element handler
            // (added via addEventListener above) so a StopImmediatePropagation from it suppresses this.
            bool nativeGotMove = false;
            childCs.RegisterCallback<PointerMoveEvent>(_ => nativeGotMove = true);

            // Capture so events reach only the per-element handler, not _root TrickleDown.
            childCs.CapturePointer(PointerId.mousePointerId);
            Assert.IsTrue(childCs.HasPointerCapture(PointerId.mousePointerId),
                "Child should have pointer capture.");

            // Backward-compat: without preventDefault, the native PointerMove probe fires.
            _bridge.Eval("globalThis.__pmFired = 0; globalThis.__doPreventDefault = false;");
            nativeGotMove = false;
            using (var evt = PointerMoveEvent.GetPooled()) {
                SetPointerEventPointerId(evt, PointerId.mousePointerId);
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pmFired"),
                "JS onPointerMove should fire via the per-element handler during capture.");
            Assert.IsTrue(nativeGotMove,
                "Without preventDefault, the native PointerMove probe should fire during capture.");

            // Suppression: with preventDefault, the per-element handler must suppress the native probe.
            _bridge.Eval("globalThis.__pmFired = 0; globalThis.__doPreventDefault = true;");
            nativeGotMove = false;
            using (var evt = PointerMoveEvent.GetPooled()) {
                SetPointerEventPointerId(evt, PointerId.mousePointerId);
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pmFired"),
                "JS onPointerMove should still fire via the per-element handler during capture.");
            Assert.IsFalse(nativeGotMove,
                "preventDefault() in onPointerMove must suppress the native event during pointer capture (per-element path).");

            childCs.ReleasePointer(PointerId.mousePointerId);
        }

        [UnityTest]
        [Category("RequiresGraphics")]
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

            // A wheel can only scroll once layout has resolved and the scroller has
            // range. Two frames suffice locally, but the headless CI panel can lag,
            // so wait for scrollability instead of assuming it.
            for (int i = 0; i < 120 && sv.verticalScroller.highValue <= 0f; i++)
                yield return null;
            if (sv.verticalScroller.highValue <= 0f) {
                // A headless editor with no graphics device never lays out runtime
                // panels, and wheel scrolling is meaningless without resolved
                // geometry. Skip there; any environment that renders runs the
                // full assertion.
                Assert.Ignore("Panel never resolved layout (headless, no graphics device); wheel scrolling is untestable here.");
            }

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

        [UnityTest]
        public IEnumerator Suppression_PreventDefaultInOnNavigationMove_KeepsFocus([Values] bool fastPath) {
            // NavigationMoveEvent's native default is to move focus. A JS onNavigationMove calling
            // preventDefault() must stop that, on both dispatch paths; without it focus still
            // moves (backward-compat). Guards against the navigation handlers dropping the flags.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);
            if (fastPath) _bridge.CacheEventDispatchCallback();

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var first = new CS.UnityEngine.UIElements.Button();
                var second = new CS.UnityEngine.UIElements.Button();
                root.Add(first);
                root.Add(second);
                globalThis.__first = first;
                globalThis.__navFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(first, 'navigationmove', (e) => {{
                    globalThis.__navFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            var first = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__first.__csHandle"))) as VisualElement;
            Assert.IsNotNull(first, "First button should resolve back to C#.");
            var focus = root.panel.focusController;

            // Backward-compat: without preventDefault, focus moves off the first button.
            first.Focus();
            _bridge.Eval("globalThis.__navFired = 0; globalThis.__doPreventDefault = false;");
            SendNavigationMove(root, first);
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__navFired"), "JS onNavigationMove should fire.");
            Assert.AreNotSame(first, focus.focusedElement,
                "Without preventDefault, NavigationMove should move focus off the first button.");

            // Suppression: with preventDefault, focus stays where it was.
            first.Focus();
            _bridge.Eval("globalThis.__navFired = 0; globalThis.__doPreventDefault = true;");
            SendNavigationMove(root, first);
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__navFired"), "JS onNavigationMove should still fire.");
            Assert.AreSame(first, focus.focusedElement,
                "preventDefault() in onNavigationMove must keep focus on the first button.");
        }

        [UnityTest]
        public IEnumerator Suppression_PreventDefaultInOnNavigationSubmitOrCancel_Suppresses(
            [Values("navigationsubmit", "navigationcancel")] string eventType, [Values] bool fastPath) {
            // Submit and cancel carry no payload, so they take the arity-0 dispatch. A JS handler
            // calling preventDefault() must stop the native event reaching the target's own
            // callbacks, as it does for pointer events; without it the native callback fires.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);
            if (fastPath) _bridge.CacheEventDispatchCallback();

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var child = new CS.UnityEngine.UIElements.VisualElement();
                root.Add(child);
                globalThis.__child = child;
                globalThis.__navFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(child, '{eventType}', (e) => {{
                    globalThis.__navFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            var child = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__child.__csHandle"))) as VisualElement;
            Assert.IsNotNull(child, "Child should resolve back to C#.");
            bool nativeFired = false;
            if (eventType == "navigationsubmit") child.RegisterCallback<NavigationSubmitEvent>(_ => nativeFired = true);
            else child.RegisterCallback<NavigationCancelEvent>(_ => nativeFired = true);

            void Send() {
                using (EventBase evt = eventType == "navigationsubmit"
                           ? NavigationSubmitEvent.GetPooled()
                           : NavigationCancelEvent.GetPooled()) {
                    evt.target = child;
                    root.SendEvent(evt);
                }
            }

            // Backward-compat: without preventDefault the native callback fires.
            _bridge.Eval("globalThis.__navFired = 0; globalThis.__doPreventDefault = false;");
            Send();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__navFired"), $"JS {eventType} handler should fire.");
            Assert.IsTrue(nativeFired, $"Without preventDefault, the native {eventType} callback should fire.");

            // Suppression: with preventDefault the native callback does not.
            nativeFired = false;
            _bridge.Eval("globalThis.__navFired = 0; globalThis.__doPreventDefault = true;");
            Send();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__navFired"), $"JS {eventType} handler should still fire.");
            Assert.IsFalse(nativeFired, $"preventDefault() in {eventType} must suppress the native callback.");
        }

        [UnityTest]
        public IEnumerator Suppression_PreventDefaultInOnPointerDown_KeepsFocus([Values] bool fastPath) {
            // Part of a PointerDownEvent's native default is moving focus to the pressed element,
            // which UI Toolkit does in PostDispatch regardless of propagation. A JS onPointerDown
            // calling preventDefault() must stop that, as it does in the DOM; without it the press
            // still takes focus (backward-compat).
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);
            if (fastPath) _bridge.CacheEventDispatchCallback();

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var first = new CS.UnityEngine.UIElements.Button();
                var second = new CS.UnityEngine.UIElements.Button();
                root.Add(first);
                root.Add(second);
                globalThis.__first = first;
                globalThis.__second = second;
                globalThis.__pdFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(second, 'pointerdown', (e) => {{
                    globalThis.__pdFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            var first = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__first.__csHandle"))) as VisualElement;
            var second = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__second.__csHandle"))) as VisualElement;
            Assert.IsNotNull(first, "First button should resolve back to C#.");
            Assert.IsNotNull(second, "Second button should resolve back to C#.");
            var focus = root.panel.focusController;

            void Press() {
                using (var evt = PointerDownEvent.GetPooled()) {
                    evt.target = second;
                    root.SendEvent(evt);
                }
            }

            // Backward-compat: without preventDefault, the press moves focus to the second button.
            first.Focus();
            Assert.AreSame(first, focus.focusedElement, "Setup: the first button should hold focus before the press.");
            _bridge.Eval("globalThis.__pdFired = 0; globalThis.__doPreventDefault = false;");
            Press();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pdFired"), "JS onPointerDown should fire.");
            Assert.AreSame(second, focus.focusedElement,
                "Without preventDefault, a press should move focus to the pressed button.");

            // Suppression: with preventDefault, focus stays where it was.
            first.Focus();
            Assert.AreSame(first, focus.focusedElement, "Setup: the first button should hold focus before the press.");
            _bridge.Eval("globalThis.__pdFired = 0; globalThis.__doPreventDefault = true;");
            Press();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pdFired"), "JS onPointerDown should still fire.");
            Assert.AreSame(first, focus.focusedElement,
                "preventDefault() in onPointerDown must keep focus on the first button.");
        }

        [UnityTest]
        public IEnumerator Suppression_PreventDefaultInOnKeyDownOrUp_Suppresses([Values("keydown", "keyup")] string eventType) {
            // Key events take the string dispatch path on both bridge configurations. A JS handler
            // calling preventDefault() must stop the native event reaching the target's own
            // callbacks, as it does for pointer and navigation events; without it the native
            // callback fires.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var child = new CS.UnityEngine.UIElements.VisualElement();
                root.Add(child);
                globalThis.__child = child;
                globalThis.__keyFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(child, '{eventType}', (e) => {{
                    globalThis.__keyFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            var child = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__child.__csHandle"))) as VisualElement;
            Assert.IsNotNull(child, "Child should resolve back to C#.");
            bool nativeFired = false;
            if (eventType == "keydown") child.RegisterCallback<KeyDownEvent>(_ => nativeFired = true);
            else child.RegisterCallback<KeyUpEvent>(_ => nativeFired = true);

            void Send() {
                using (EventBase evt = eventType == "keydown"
                           ? KeyDownEvent.GetPooled('a', KeyCode.A, EventModifiers.None)
                           : KeyUpEvent.GetPooled('a', KeyCode.A, EventModifiers.None)) {
                    evt.target = child;
                    root.SendEvent(evt);
                }
            }

            // Backward-compat: without preventDefault the native callback fires.
            _bridge.Eval("globalThis.__keyFired = 0; globalThis.__doPreventDefault = false;");
            Send();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__keyFired"), $"JS {eventType} handler should fire.");
            Assert.IsTrue(nativeFired, $"Without preventDefault, the native {eventType} callback should fire.");

            // Suppression: with preventDefault the native callback does not.
            nativeFired = false;
            _bridge.Eval("globalThis.__keyFired = 0; globalThis.__doPreventDefault = true;");
            Send();
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__keyFired"), $"JS {eventType} handler should still fire.");
            Assert.IsFalse(nativeFired, $"preventDefault() in {eventType} must suppress the native callback.");
        }

        [UnityTest]
        public IEnumerator Suppression_PreventDefaultInOnKeyDown_StopsTextFieldInput() {
            // The behaviour change the ChangeLog names: a TextField whose JS onKeyDown calls
            // preventDefault() no longer receives that key, as in the DOM. Without it the
            // character is typed (backward-compat).
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var field = new CS.UnityEngine.UIElements.TextField();
                root.Add(field);
                globalThis.__field = field;
                globalThis.__keyFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(field, 'keydown', (e) => {{
                    globalThis.__keyFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            var field = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__field.__csHandle"))) as TextField;
            Assert.IsNotNull(field, "TextField should resolve back to C#.");
            // The key handler lives on the field's inner text element, which is what holds focus
            // when the field does (focusedElement reports the field itself). Its USS class is the
            // only public way to find it: TextInputBase.innerTextElementUssClassName is protected.
            var inner = field.Q<TextElement>(className: "unity-text-element--inner-input-field-component");
            Assert.IsNotNull(inner, "Setup: the TextField's inner text element should be found by its USS class.");
            var focus = root.panel.focusController;

            void Type(char c) {
                using (var evt = KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None)) {
                    evt.target = inner;
                    root.SendEvent(evt);
                }
            }

            field.Focus();
            yield return null;
            Assert.AreSame(field, focus.focusedElement, "Setup: the TextField should hold focus.");

            // Backward-compat: without preventDefault the character reaches the field.
            _bridge.Eval("globalThis.__keyFired = 0; globalThis.__doPreventDefault = false;");
            Type('a');
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__keyFired"), "JS onKeyDown should fire.");
            Assert.AreEqual("a", field.value, "Without preventDefault, the typed character should reach the TextField.");

            // Suppression: with preventDefault the field does not receive it.
            _bridge.Eval("globalThis.__keyFired = 0; globalThis.__doPreventDefault = true;");
            Type('b');
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__keyFired"), "JS onKeyDown should still fire.");
            Assert.AreEqual("a", field.value, "preventDefault() in onKeyDown must keep the character out of the TextField.");
        }

        [UnityTest]
        public IEnumerator Focus_FocusOutBubblesToAncestor_SoAFocusScopeTrapHoldsFocus([Values] bool fastPath) {
            // onejs-ui's FocusScope traps focus by listening for focusout on its root, which only
            // sees a descendant losing focus if focusout bubbles in JS. This mounts that trap as
            // FocusScope writes it (focusout on the scope, then a requestAnimationFrame that pulls
            // focus back inside if it landed outside), moves focus out by navigation, and asserts
            // the trap returned it. blur on the same elements is the control: it must still fire
            // on the element that lost focus and must still not bubble.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);
            if (fastPath) _bridge.CacheEventDispatchCallback();

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var scope = new CS.UnityEngine.UIElements.VisualElement();
                var inside = new CS.UnityEngine.UIElements.Button();
                var outside = new CS.UnityEngine.UIElements.Button();
                scope.Add(inside);
                root.Add(scope);
                root.Add(outside);
                __eventAPI.setParent(inside.__csHandle, scope.__csHandle);
                __eventAPI.setParent(scope.__csHandle, root.__csHandle);
                __eventAPI.setParent(outside.__csHandle, root.__csHandle);
                globalThis.__inside = inside;
                globalThis.__outside = outside;
                globalThis.__scopeFocusOut = 0;
                globalThis.__scopeFocusIn = 0;
                globalThis.__scopeBlur = 0;
                globalThis.__insideBlur = 0;
                __eventAPI.addEventListener(scope, 'focusout', () => {{
                    globalThis.__scopeFocusOut++;
                    requestAnimationFrame(() => {{
                        var focused = root.focusController.focusedElement;
                        if (focused && !scope.Contains(focused)) inside.Focus();
                    }});
                }});
                __eventAPI.addEventListener(scope, 'focusin', () => {{ globalThis.__scopeFocusIn++; }});
                __eventAPI.addEventListener(scope, 'blur', () => {{ globalThis.__scopeBlur++; }});
                __eventAPI.addEventListener(inside, 'blur', () => {{ globalThis.__insideBlur++; }});
            ");
            yield return null;

            var inside = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__inside.__csHandle"))) as VisualElement;
            var outside = QuickJSNative.GetObjectByHandle(int.Parse(_bridge.Eval("globalThis.__outside.__csHandle"))) as VisualElement;
            Assert.IsNotNull(inside, "Inside button should resolve back to C#.");
            Assert.IsNotNull(outside, "Outside button should resolve back to C#.");
            var focus = root.panel.focusController;

            inside.Focus();
            yield return null;
            Assert.AreSame(inside, focus.focusedElement, "Setup: focus should start inside the scope.");
            Assert.AreEqual("1", _bridge.Eval("globalThis.__scopeFocusIn"),
                "focusin on a descendant should bubble to the scope.");

            // Navigate out of the scope. Nothing in JS prevents it, so focus really does leave.
            SendNavigationMove(root, inside);
            yield return null;
            Assert.AreSame(outside, focus.focusedElement,
                "Setup: the navigation move should have taken focus outside the scope.");
            Assert.AreEqual("1", _bridge.Eval("globalThis.__insideBlur"), "blur should still fire on the element that lost focus.");
            Assert.AreEqual("0", _bridge.Eval("globalThis.__scopeBlur"), "blur must not bubble.");
            Assert.AreEqual("1", _bridge.Eval("globalThis.__scopeFocusOut"),
                "focusout on a descendant should bubble to the scope.");

            // The trap's deferred check runs on the next tick and pulls focus back inside.
            _bridge.Tick();
            yield return null;
            Assert.AreSame(inside, focus.focusedElement, "The focus trap should have returned focus inside the scope.");
        }

        // Helper: dispatch a NavigationMove toward the next focusable element, targeting `target`.
        static void SendNavigationMove(VisualElement root, VisualElement target) {
            using (var evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next)) {
                evt.target = target;
                root.SendEvent(evt);
            }
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

        [UnityTest]
        public IEnumerator Wheel_FastPath_DispatchedToJsHandlerWithDelta() {
            // Mirrors Wheel_DispatchedToJsHandlerWithDelta but with the zero-alloc fast path
            // enabled (CacheEventDispatchCallback, as JSRunner does), so wheel goes through
            // DispatchEventFast(EVT_WHEEL) -> InvokeCallbackReturnInt -> __dispatchEventFast,
            // which must rebuild { deltaX, deltaY } for the JS handler.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            _bridge.CacheEventDispatchCallback();
            var dispatchHandleField = typeof(QuickJSUIBridge).GetField(
                "_eventDispatchHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.GreaterOrEqual((int)dispatchHandleField.GetValue(_bridge), 0,
                "Fast dispatch path should be enabled so this test exercises the wheel fast path.");

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                var el = new CS.UnityEngine.UIElements.VisualElement();
                el.style.flexGrow = 1;
                root.Add(el);
                globalThis.__wheelEl = el;
                globalThis.__wheelCount = 0;
                globalThis.__wheelDeltaX = -1;
                globalThis.__wheelDeltaY = 0;
                __eventAPI.addEventListener(el, 'wheel', (e) => {{
                    globalThis.__wheelCount++;
                    globalThis.__wheelDeltaX = e.deltaX;
                    globalThis.__wheelDeltaY = e.deltaY;
                }});
            ");
            yield return null;
            yield return null;

            int elHandle = int.Parse(_bridge.Eval("globalThis.__wheelEl.__csHandle"));
            var elCs = QuickJSNative.GetObjectByHandle(elHandle) as VisualElement;
            Assert.IsNotNull(elCs, "Should resolve the JS-created element back to C#.");

            SendWheel(root, elCs, 10f);
            yield return null;

            Assert.AreEqual("1", _bridge.Eval("globalThis.__wheelCount"),
                "Wheel should reach the JS handler once via the fast path.");
            Assert.AreEqual("true", _bridge.Eval("globalThis.__wheelDeltaY > 0"),
                "deltaY should be forwarded as a positive number through the fast path.");
            Assert.AreEqual("number", _bridge.Eval("typeof globalThis.__wheelDeltaX"),
                "deltaX should be present and numeric (fast path must rebuild { deltaX, deltaY }, not { x, y, ... }).");
        }

        [UnityTest]
        public IEnumerator Suppression_PerElementFastPath_PreventDefaultDuringCapture_Suppresses() {
            // Covers the per-element pointermove FAST path during capture (the OnPerElementPointerMove
            // fast-path branch). With the fast dispatch callback cached (as JSRunner does), a captured
            // pointermove must still fire the JS handler AND honor preventDefault() to suppress the
            // native event mid-drag. The slow-path variant is covered by
            // Suppression_PerElement_PreventDefaultDuringCapture_Suppresses.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            _bridge.CacheEventDispatchCallback();
            var dispatchHandleField = typeof(QuickJSUIBridge).GetField(
                "_eventDispatchHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.GreaterOrEqual((int)dispatchHandleField.GetValue(_bridge), 0,
                "Fast dispatch path should be enabled so this test exercises the per-element fast path.");

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                useExtensions(CS.UnityEngine.UIElements.PointerCaptureHelper);
                var child = new CS.UnityEngine.UIElements.VisualElement();
                child.style.width = 200; child.style.height = 200;
                root.Add(child);
                globalThis.__child = child;
                globalThis.__pmFired = 0;
                globalThis.__doPreventDefault = false;
                __eventAPI.addEventListener(child, 'pointermove', (e) => {{
                    globalThis.__pmFired++;
                    if (globalThis.__doPreventDefault) e.preventDefault();
                }});
            ");
            yield return null;

            int childHandle = int.Parse(_bridge.Eval("globalThis.__child.__csHandle"));
            var childCs = QuickJSNative.GetObjectByHandle(childHandle) as VisualElement;
            Assert.IsNotNull(childCs, "Child should resolve back to C#.");

            // Native PointerMove probe registered AFTER the per-element handler, so a
            // StopImmediatePropagation from suppression keeps it from firing.
            bool nativeGotMove = false;
            childCs.RegisterCallback<PointerMoveEvent>(_ => nativeGotMove = true);

            childCs.CapturePointer(PointerId.mousePointerId);
            Assert.IsTrue(childCs.HasPointerCapture(PointerId.mousePointerId),
                "Child should have pointer capture.");

            // Backward-compat: without preventDefault, the native PointerMove probe fires.
            _bridge.Eval("globalThis.__pmFired = 0; globalThis.__doPreventDefault = false;");
            nativeGotMove = false;
            using (var evt = PointerMoveEvent.GetPooled()) {
                SetPointerEventPointerId(evt, PointerId.mousePointerId);
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pmFired"),
                "JS onPointerMove should fire via the per-element fast path during capture.");
            Assert.IsTrue(nativeGotMove,
                "Without preventDefault, the native PointerMove probe should fire during capture.");

            // Suppression: with preventDefault, the per-element fast path must suppress the native probe.
            _bridge.Eval("globalThis.__pmFired = 0; globalThis.__doPreventDefault = true;");
            nativeGotMove = false;
            using (var evt = PointerMoveEvent.GetPooled()) {
                SetPointerEventPointerId(evt, PointerId.mousePointerId);
                root.SendEvent(evt);
            }
            yield return null;
            Assert.AreEqual("1", _bridge.Eval("globalThis.__pmFired"),
                "JS onPointerMove should still fire via the per-element fast path during capture.");
            Assert.IsFalse(nativeGotMove,
                "preventDefault() in onPointerMove must suppress the native event during capture (per-element fast path).");

            childCs.ReleasePointer(PointerId.mousePointerId);
        }

        [UnityTest]
        public IEnumerator PointerMove_TwoInOneFrame_EachReachesJsOnce() {
            // Two distinct moves sent back to back land inside the same millisecond, so
            // they share EventBase.timestamp. The bridge dedups the root and per-element
            // handlers seeing one event; keying that on the timestamp dropped the second
            // move (#126). Each move must reach JS exactly once: once, not twice, when the
            // root TrickleDown and the per-element handler both see it; once, not zero
            // times, when it follows another move inside one millisecond.
            var root = _uiDocument.rootVisualElement;
            int rootHandle = QuickJSNative.RegisterObject(root);

            _bridge.Eval($@"
                var root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle});
                useExtensions(CS.UnityEngine.UIElements.PointerCaptureHelper);
                var child = new CS.UnityEngine.UIElements.VisualElement();
                child.style.width = 200; child.style.height = 200;
                root.Add(child);
                globalThis.__child = child;
                globalThis.__pmFired = 0;
                __eventAPI.addEventListener(child, 'pointermove', () => {{ globalThis.__pmFired++; }});
            ");
            yield return null;

            int childHandle = int.Parse(_bridge.Eval("globalThis.__child.__csHandle"));
            var childCs = QuickJSNative.GetObjectByHandle(childHandle) as VisualElement;
            Assert.IsNotNull(childCs, "Child should resolve back to C#.");

            // Not captured: the root TrickleDown and the per-element handler both run.
            for (int i = 0; i < 2; i++) {
                using (var evt = PointerMoveEvent.GetPooled()) {
                    SetPointerEventPointerId(evt, PointerId.mousePointerId);
                    evt.target = childCs;
                    root.SendEvent(evt);
                }
            }
            yield return null;
            Assert.AreEqual("2", _bridge.Eval("globalThis.__pmFired"),
                "Two moves without capture should reach JS once each.");

            // Captured: only the per-element handler runs.
            childCs.CapturePointer(PointerId.mousePointerId);
            _bridge.Eval("globalThis.__pmFired = 0;");
            for (int i = 0; i < 2; i++) {
                using (var evt = PointerMoveEvent.GetPooled()) {
                    SetPointerEventPointerId(evt, PointerId.mousePointerId);
                    root.SendEvent(evt);
                }
            }
            yield return null;
            Assert.AreEqual("2", _bridge.Eval("globalThis.__pmFired"),
                "Two moves during capture should reach JS once each.");

            childCs.ReleasePointer(PointerId.mousePointerId);
        }
    }
}
