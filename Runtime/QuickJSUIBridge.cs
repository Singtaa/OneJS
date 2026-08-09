using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OneJS.CustomStyleSheets;
using OneJS.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Bridges QuickJS context to UI Toolkit with event delegation and scheduling.
    /// Attach to a GameObject with UIDocument, or construct manually with a root element.
    /// </summary>
    public class QuickJSUIBridge : IDisposable {
        readonly QuickJSContext _ctx;
        readonly VisualElement _root;
        readonly StringBuilder _sb = new(256);
        readonly string _workingDir;
        readonly UssCompiler _ussCompiler;
        readonly Dictionary<string, StyleSheet> _jsStyleSheets = new(); // Track JS-loaded stylesheets by name
        bool _disposed;
        bool _countedLive; // Whether this bridge incremented _liveBridgeCount (guards a ctor that throws before counting).
        float _startTime;

        // Number of live bridges (== live QuickJS contexts driving UI Toolkit). The
        // handle table and pending-task queue in QuickJSNative are process-global and
        // SHARED across every context, so they may only be wiped when the LAST bridge
        // is disposed. Clearing them on a per-context Dispose() blows away sibling
        // contexts' handles (causing wrong-element dispatch and dropped events) and
        // their in-flight async work. Play-mode entry independently hard-resets these
        // via QuickJSNative.ResetStaticState, so this counter only governs edit-mode /
        // hot-reload teardown. A fully isolated future design would partition the
        // handle table by context id and remove this counter entirely.
        static int _liveBridgeCount;
        bool _inEval; // Recursion guard to prevent re-entrant JS execution (all platforms)
        int _tickCallbackHandle = -1; // Cached handle for zero-alloc tick
        int _eventDispatchHandle = -1; // Cached handle for zero-alloc event dispatch
        readonly int _wsContextId; // WebSocketBridge context ID for per-context event routing


        // Event type IDs for zero-alloc dispatch. Must match QuickJSBootstrap.js.txt __EVT_* constants.
        const int EVT_CHANGE_FLOAT = 1;
        const int EVT_CHANGE_INT = 2;
        const int EVT_CHANGE_BOOL = 3;
        const int EVT_CLICK = 10;
        const int EVT_POINTER_DOWN = 11;
        const int EVT_POINTER_UP = 12;
        const int EVT_POINTER_MOVE = 13;
        const int EVT_POINTER_ENTER = 14;
        const int EVT_POINTER_LEAVE = 15;
        const int EVT_WHEEL = 16; // shares the pointer id block; JS handles it via an explicit branch before the pointer range
        const int EVT_FOCUS = 20;
        const int EVT_BLUR = 21;
        const int EVT_FOCUSCHANGE = 22;
        const int EVT_VIEWPORT_CHANGE = 30;
        const int EVT_NAVIGATION_MOVE = 40;
        const int EVT_NAVIGATION_SUBMIT = 41;
        const int EVT_NAVIGATION_CANCEL = 42;

        // Viewport tracking for responsive design
        float _lastViewportWidth;
        float _lastViewportHeight;

        // Focus tracking: the panel's focusController.focusedElement at the previous
        // tick. Diffed each Tick to emit a reliable "focuschange" to JS (the event
        // path drops programmatic focus during eval; this runs outside _inEval).
        VisualElement _lastFocusedElement;

        // Per-element C# handler registry for events that don't reach _root's
        // TrickleDown hook: captured pointer events (Unity 6 delivers them directly
        // to the capturing element) and non-bubbling events like GeometryChangedEvent.
        readonly Dictionary<(int handle, string eventType), VisualElement> _perElementHandlers = new();

        // Dedup: prevent double-dispatch when both _root TrickleDown and per-element
        // fire for the same event. UI Toolkit's event pool reuses instances across
        // dispatches, so a reference-equality check would treat consecutive pooled
        // events as duplicates and silently drop them (this was the WebGL drag
        // regression). EventBase.timestamp is refreshed in Init() each time an event
        // is acquired from the pool, so it's the same within one dispatch (root +
        // per-element phases) and different across dispatches.
        long _lastDispatchedPointerDownTs = -1;
        long _lastDispatchedPointerUpTs = -1;
        long _lastDispatchedPointerMoveTs = -1;
        long _lastDispatchedPointerCancelTs = -1;
        long _lastDispatchedPointerCaptureTs = -1;
        long _lastDispatchedPointerCaptureOutTs = -1;

        public QuickJSContext Context => _ctx;
        public VisualElement Root => _root;
        public string WorkingDir => _workingDir;
        public int WebSocketContextId => _wsContextId;

        // MARK: Lifecycle
        public QuickJSUIBridge(VisualElement root, string workingDir = null, int bufferSize = 16 * 1024) {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _workingDir = workingDir ?? "";
            _ctx = new QuickJSContext(bufferSize);
            _ussCompiler = new UssCompiler(_workingDir);
            _startTime = Time.realtimeSinceStartup;
            _wsContextId = WebSocketBridge.RegisterContext();

            // Inject context ID so the bootstrap WebSocket class can pass it to C# Connect()
            _ctx.Eval($"globalThis.__wsContextId = {_wsContextId}");

            PerElementEventSupport.RegisterBridge(_wsContextId, this);
            RegisterEventDelegation();

            // Count this bridge as live only after construction has fully succeeded, so
            // a throw above never leaves the counter (and thus the global-table clears)
            // out of balance.
            _countedLive = true;
            System.Threading.Interlocked.Increment(ref _liveBridgeCount);
        }

        // MARK: StyleSheet API

        /// <summary>
        /// Load a USS file from the working directory and apply it to the root element.
        /// </summary>
        /// <param name="path">Path relative to working directory</param>
        /// <returns>True if successful</returns>
        public bool LoadStyleSheet(string path) {
            try {
                string fullPath = Path.Combine(_workingDir, path);
                if (!File.Exists(fullPath)) {
#if UNITY_EDITOR
                    Debug.LogWarning($"[QuickJSUIBridge] StyleSheet not found: {fullPath}");
#else
                    Debug.LogWarning($"[QuickJSUIBridge] StyleSheet not found: {fullPath}. " +
                        "loadStyleSheet() reads from the filesystem at runtime, and working-dir files are not shipped in builds. " +
                        "Embed styles in the JS bundle instead: import the .uss file as text and call compileStyleSheet(), " +
                        "or use CSS Modules (.module.uss) / Tailwind.");
#endif
                    return false;
                }

                string content = File.ReadAllText(fullPath);
                return CompileStyleSheet(content, path);
            } catch (Exception ex) {
                Debug.LogError($"[QuickJSUIBridge] LoadStyleSheet error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Compile a USS string and apply it to the root element.
        /// If a stylesheet with the same name already exists, it will be replaced (deduplication).
        /// </summary>
        /// <param name="ussContent">USS content</param>
        /// <param name="name">Name for the stylesheet (used for deduplication and debugging)</param>
        /// <returns>True if successful</returns>
        public bool CompileStyleSheet(string ussContent, string name = "inline") {
            try {
                // Remove existing stylesheet with same name (deduplication for hot reload)
                if (_jsStyleSheets.TryGetValue(name, out var existing)) {
                    _root.styleSheets.Remove(existing);
                    UnityEngine.Object.DestroyImmediate(existing);
                    _jsStyleSheets.Remove(name);
                }

                var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
                styleSheet.name = name;
                _ussCompiler.Compile(styleSheet, ussContent);
                _root.styleSheets.Add(styleSheet);
                _jsStyleSheets[name] = styleSheet;
                return true;
            } catch (Exception ex) {
                Debug.LogError($"[QuickJSUIBridge] CompileStyleSheet error ({name}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove a stylesheet by name.
        /// </summary>
        /// <param name="name">Name of the stylesheet to remove</param>
        /// <returns>True if the stylesheet was found and removed</returns>
        public bool RemoveStyleSheet(string name) {
            if (!_jsStyleSheets.TryGetValue(name, out var styleSheet)) {
                return false;
            }

            _root.styleSheets.Remove(styleSheet);
            UnityEngine.Object.DestroyImmediate(styleSheet);
            _jsStyleSheets.Remove(name);
            return true;
        }

        /// <summary>
        /// Remove all JS-loaded stylesheets.
        /// Does not affect stylesheets loaded via Unity assets (e.g., from JSRunner._stylesheets).
        /// </summary>
        /// <returns>Number of stylesheets removed</returns>
        public int ClearStyleSheets() {
            int count = _jsStyleSheets.Count;
            foreach (var kvp in _jsStyleSheets) {
                _root.styleSheets.Remove(kvp.Value);
                UnityEngine.Object.DestroyImmediate(kvp.Value);
            }
            _jsStyleSheets.Clear();
            return count;
        }

        /// <summary>
        /// Get the names of all JS-loaded stylesheets.
        /// </summary>
        public IEnumerable<string> GetStyleSheetNames() => _jsStyleSheets.Keys;

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing) {
            if (_disposed) return;
            _disposed = true;

            // Run JS-registered teardown hooks (e.g. React unmounts its roots, firing
            // useEffect/useLayoutEffect cleanups) while the context is still alive. This
            // is the single chokepoint for every teardown path (hot reload, play/edit
            // stop, destroy), so cleanups fire consistently before the context is torn
            // down. Skipped on the finalizer path: it runs on a GC thread where calling
            // back into QuickJS would be unsafe.
            if (disposing) {
                RunTeardownHooks();
                // Safety net: dispose particle systems the JS side leaked. Normal
                // disposal already happened via effect cleanups inside the teardown
                // hooks above. Not on the finalizer path (touches VisualElements).
                ParticleBridge.DisposeAll();
                OneJS.ShaderFX.ShaderEffectBridge.DisposeAll();
            }

            _tickCallbackHandle = -1;
            _eventDispatchHandle = -1;

            UnregisterEventDelegation();
            UnregisterAllPerElementHandlers();
            PerElementEventSupport.UnregisterBridge(_wsContextId);
            ClearStyleSheets(); // Clean up JS-loaded stylesheets
            WebSocketBridge.CloseAll(_wsContextId);
            WebSocketBridge.UnregisterContext(_wsContextId);

            // The pending-task queue and handle table are shared by every live context
            // (see QuickJSNative), so only wipe them when the FINAL bridge is going away.
            // Decrement exactly once per bridge, and only if construction counted it.
            bool lastBridge = _countedLive
                && System.Threading.Interlocked.Decrement(ref _liveBridgeCount) <= 0;

            if (lastBridge) QuickJSNative.ClearPendingTasks();
            _ctx?.Dispose();
            if (lastBridge) QuickJSNative.ClearAllHandles();
        }

        ~QuickJSUIBridge() {
            Dispose(false);
        }

        /// <summary>
        /// Run JS teardown hooks registered via globalThis.__onTeardown (e.g. the React
        /// reconciler's root unmount, which fires component cleanup functions). Runs on
        /// the main thread with the context still alive. Idempotent: __runTeardown drains
        /// its callback list, so repeat calls are no-ops.
        /// </summary>
        void RunTeardownHooks() {
            if (_ctx == null) return;
            try {
                _ctx.Eval("typeof globalThis.__runTeardown === 'function' && globalThis.__runTeardown()");
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Teardown hook error: {ex.Message}");
            }
        }

        // MARK: Public API
        public string Eval(string code, string filename = "<input>") {
            return _ctx.Eval(code, filename);
        }

        /// <summary>
        /// Cache the __tick callback handle for zero-allocation per-frame invocation.
        /// Call this once after the bootstrap and user code have been evaluated.
        /// </summary>
        public void CacheTickCallback() {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL runs __tick directly via the browser RAF loop started by
            // __startWebGLTick. There's no __registerCallback on the WebGL side
            // (it's provided by the native QuickJS runtime, not the bootstrap),
            // and JSRunner.TickIfReady doesn't call _bridge.Tick() on WebGL.
            _tickCallbackHandle = -1;
            return;
#else
            try {
                var handleStr = _ctx.Eval("typeof __tick === 'function' ? __registerCallback(__tick) : -1");
                _tickCallbackHandle = int.Parse(handleStr);
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Failed to cache __tick callback: {ex.Message}");
                _tickCallbackHandle = -1;
            }
#endif
        }

        /// <summary>
        /// Cache the __dispatchEventFast callback handle for zero-allocation event dispatch.
        /// Call this once after the bootstrap has been evaluated.
        /// </summary>
        public void CacheEventDispatchCallback() {
#if UNITY_WEBGL && !UNITY_EDITOR
            return; // WebGL uses its own fast dispatch via qjs_dispatch_event
#else
            try {
                var handleStr = _ctx.Eval("typeof __dispatchEventFast === 'function' ? __registerCallback(__dispatchEventFast) : -1");
                _eventDispatchHandle = int.Parse(handleStr);
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Failed to cache event dispatch callback: {ex.Message}");
                _eventDispatchHandle = -1;
            }
#endif
        }

        /// <summary>
        /// Safe eval that prevents recursive calls (important for WebGL).
        /// Returns null if already in an eval call.
        /// </summary>
        string SafeEval(string code) {
            if (_inEval) {
                Debug.LogWarning("[QuickJSUIBridge] Prevented recursive eval");
                return null;
            }
            _inEval = true;
            try {
                return _ctx.Eval(code);
            } finally {
                _inEval = false;
            }
        }

        /// <summary>
        /// Call every frame from Update() to drive RAF, timers, and Promise microtasks.
        /// Uses zero-allocation path when tick callback is cached.
        /// </summary>
        public void Tick() {
            if (_disposed || _inEval) return;

            // Advance C#-side particle simulations (self-guarded against multiple
            // bridges ticking in the same frame). Lives here so play mode, edit-mode
            // preview and JSPad all drive particles through one integration point.
            ParticleBridge.TickAll();
            OneJS.ShaderFX.ShaderEffectBridge.TickAll();

            // Detect focus changes before entering the eval block (CheckFocusChange
            // dispatches, which sets _inEval itself). Runs outside _inEval so it
            // captures programmatic focus that the event path drops.
            CheckFocusChange();

            _inEval = true;

            // No per-frame dedup reset needed: dedup uses EventBase.timestamp,
            // which is unique per dispatch (refreshed in EventBase.Init() each time
            // the pool reuses an instance).

            try {
                // Process completed C# Tasks and resolve/reject their JS Promises
                QuickJSNative.ProcessCompletedTasks(_ctx);
                WebSocketBridge.ProcessEvents(_ctx, _wsContextId);

                // Reads engine realtime unless an offline renderer has taken the clock
                // over, in which case it advances by an exact frame interval instead.
                // This one value feeds every rAF callback, JS timer and transition.
                float timestamp = (float)((VirtualClock.RealtimeSeconds - _startTime) * 1000.0);

                if (_tickCallbackHandle >= 0) {
                    // Zero-allocation path: invoke cached callback directly
                    _ctx.InvokeCallbackNoAlloc(_tickCallbackHandle, timestamp);
                } else {
                    // Fallback: use Eval (allocates strings)
                    _ctx.Eval($"globalThis.__tick && __tick({timestamp.ToString("F2", CultureInfo.InvariantCulture)})");
                }

                // Execute pending Promise jobs (microtasks): critical for React scheduler
                _ctx.ExecutePendingJobs();

                // Drain FinalizationRegistry callbacks (which free C# handles). The
                // zero-allocation tick path (InvokeCallbackNoAlloc) bypasses Eval(), which is
                // the only other place GC runs. Without this, C# handles leak unboundedly
                // during normal operation. MaybeRunGC only runs a full GC once handles have
                // grown past a delta since the last GC, so idle UIs don't pay per-frame.
                _ctx.MaybeRunGC();
            } catch (System.Exception ex) {
                UnityEngine.Debug.LogError($"[QuickJSUIBridge] Tick error: {ex.Message}");
            } finally {
                _inEval = false;
            }
        }

        // MARK: Event Registration
        void RegisterEventDelegation() {
            _root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCaptureEvent>(OnPointerCapture, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            _root.RegisterCallback<ChangeEvent<string>>(OnChangeString, TrickleDown.TrickleDown);
            _root.RegisterCallback<ChangeEvent<bool>>(OnChangeBool, TrickleDown.TrickleDown);
            _root.RegisterCallback<ChangeEvent<float>>(OnChangeFloat, TrickleDown.TrickleDown);
            _root.RegisterCallback<ChangeEvent<int>>(OnChangeInt, TrickleDown.TrickleDown);
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _root.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        void UnregisterEventDelegation() {
            _root.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCaptureEvent>(OnPointerCapture, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            _root.UnregisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            _root.UnregisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
            _root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            _root.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            _root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel, TrickleDown.TrickleDown);
            _root.UnregisterCallback<ChangeEvent<string>>(OnChangeString, TrickleDown.TrickleDown);
            _root.UnregisterCallback<ChangeEvent<bool>>(OnChangeBool, TrickleDown.TrickleDown);
            _root.UnregisterCallback<ChangeEvent<float>>(OnChangeFloat, TrickleDown.TrickleDown);
            _root.UnregisterCallback<ChangeEvent<int>>(OnChangeInt, TrickleDown.TrickleDown);
            _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _root.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        // MARK: Event Handlers

        // JS preventDefault() (defaultPrevented, bit1) is mirrored onto the native event as
        // StopImmediatePropagation, so a JS gesture can suppress nested native controls (e.g. a
        // ScrollView's pan/scroll). stopPropagation() (bit0) stays JS-bubble-only and is NOT
        // mirrored, preserving behavior for handlers that only stop the JS-side bubble.
        const int FLAG_DEFAULT_PREVENTED = 2;
        static void ApplyNativeSuppression(EventBase e, int flags) {
            if ((flags & FLAG_DEFAULT_PREVENTED) != 0) e.StopImmediatePropagation();
        }

        void OnClick(ClickEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_CLICK, FindElementHandle(e.target), e.position.x, e.position.y, e.button, 0)
                : DispatchPointerEvent("click", e.target, e.position, e.button);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerDown(PointerDownEvent e) {
            if (e.timestamp == _lastDispatchedPointerDownTs) return;
            _lastDispatchedPointerDownTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_DOWN, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerUp(PointerUpEvent e) {
            if (e.timestamp == _lastDispatchedPointerUpTs) return;
            _lastDispatchedPointerUpTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_UP, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerMove(PointerMoveEvent e) {
            if (!InputBridge.PointerMoveEventsEnabled) return;
            if (e.timestamp == _lastDispatchedPointerMoveTs) return;
            _lastDispatchedPointerMoveTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_MOVE, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerEnter(PointerEnterEvent e) {
            if (_eventDispatchHandle >= 0) {
                int handle = FindElementHandle(e.target);
                DispatchEventFast(EVT_POINTER_ENTER, handle, e.position.x, e.position.y, 0, e.pointerId);
            } else {
                DispatchPointerEvent("pointerenter", e.target, e.position, 0, e.pointerId);
            }
        }

        void OnPointerLeave(PointerLeaveEvent e) {
            if (_eventDispatchHandle >= 0) {
                int handle = FindElementHandle(e.target);
                DispatchEventFast(EVT_POINTER_LEAVE, handle, e.position.x, e.position.y, 0, e.pointerId);
            } else {
                DispatchPointerEvent("pointerleave", e.target, e.position, 0, e.pointerId);
            }
        }

        // Cancel / capture transitions are infrequent (not per-frame like pointermove),
        // so they stay on the string dispatch path rather than adding parallel fast-path
        // EVT_* constants. Capture events carry only a pointerId.
        void OnPointerCancel(PointerCancelEvent e) {
            if (e.timestamp == _lastDispatchedPointerCancelTs) return;
            _lastDispatchedPointerCancelTs = e.timestamp;
            DispatchPointerEvent("pointercancel", e.target, e.position, e.button, e.pointerId);
        }

        void OnPointerCapture(PointerCaptureEvent e) {
            if (e.timestamp == _lastDispatchedPointerCaptureTs) return;
            _lastDispatchedPointerCaptureTs = e.timestamp;
            DispatchPointerCaptureEvent("pointercapture", e.target, e.pointerId);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent e) {
            if (e.timestamp == _lastDispatchedPointerCaptureOutTs) return;
            _lastDispatchedPointerCaptureOutTs = e.timestamp;
            DispatchPointerCaptureEvent("pointercaptureout", e.target, e.pointerId);
        }

        // Mouse wheel / trackpad scroll. Takes the zero-alloc fast path when available
        // (active-scroll bursts can approach frame rate on trackpads), falling back to the
        // string path otherwise. The fast path passes the delta as (x=deltaX, y=deltaY); the
        // JS side rebuilds { deltaX, deltaY } for EVT_WHEEL. Only the root TrickleDown handler
        // fires (wheel has no per-element/capture handler), so no timestamp dedup is needed.
        // WheelEvent.delta is a Vector3; the z component is unused.
        void OnWheel(WheelEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_WHEEL, FindElementHandle(e.target), e.delta.x, e.delta.y, 0, 0)
                : DispatchWheelEvent("wheel", e.target, e.delta);
            ApplyNativeSuppression(e, flags);
        }

        void OnFocusIn(FocusInEvent e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_FOCUS, FindElementHandle(e.target));
            } else {
                DispatchEvent("focus", e.target, "{}");
            }
        }

        void OnFocusOut(FocusOutEvent e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_BLUR, FindElementHandle(e.target));
            } else {
                DispatchEvent("blur", e.target, "{}");
            }
        }

        // Key events stay on eval path (need string args)
        void OnKeyDown(KeyDownEvent e) => DispatchKeyEvent("keydown", e.target, e.keyCode, e.character, e.modifiers);
        void OnKeyUp(KeyUpEvent e) => DispatchKeyEvent("keyup", e.target, e.keyCode, '\0', e.modifiers);

        // Navigation events (controller / keyboard focus navigation)
        void OnNavigationMove(NavigationMoveEvent e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_NAVIGATION_MOVE, FindElementHandle(e.target), (int)e.direction);
            } else {
                DispatchEvent("navigationmove", e.target,
                    $"{{\"direction\":\"{NavigationDirectionName(e.direction)}\"}}");
            }
        }

        void OnNavigationSubmit(NavigationSubmitEvent e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_NAVIGATION_SUBMIT, FindElementHandle(e.target));
            } else {
                DispatchEvent("navigationsubmit", e.target, "{}");
            }
        }

        void OnNavigationCancel(NavigationCancelEvent e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_NAVIGATION_CANCEL, FindElementHandle(e.target));
            } else {
                DispatchEvent("navigationcancel", e.target, "{}");
            }
        }

        static string NavigationDirectionName(NavigationMoveEvent.Direction d) => d switch {
            NavigationMoveEvent.Direction.Left => "left",
            NavigationMoveEvent.Direction.Up => "up",
            NavigationMoveEvent.Direction.Right => "right",
            NavigationMoveEvent.Direction.Down => "down",
            NavigationMoveEvent.Direction.Next => "next",
            NavigationMoveEvent.Direction.Previous => "previous",
            _ => "none",
        };

        // String change events stay on eval path (need string value)
        void OnChangeString(ChangeEvent<string> e) {
            // Skip ChangeEvent<string> from controls that already fire typed change events
            // (ChangeEvent<float/int/bool>). Their internal text fields generate redundant
            // string change events that are expensive to dispatch via eval.
            if (e.target is BaseSlider<float> or BaseSlider<int> or Toggle) return;
            DispatchEvent("change", e.target, BuildChangeData($"\"{EscapeForJson(e.newValue)}\""));
        }

        void OnChangeBool(ChangeEvent<bool> e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_CHANGE_BOOL, FindElementHandle(e.target), e.newValue ? 1 : 0);
            } else {
                DispatchEvent("change", e.target, BuildChangeData(e.newValue ? "true" : "false"));
            }
        }

        void OnChangeFloat(ChangeEvent<float> e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_CHANGE_FLOAT, FindElementHandle(e.target), e.newValue);
            } else {
                DispatchEvent("change", e.target, BuildChangeData(e.newValue.ToString("G", CultureInfo.InvariantCulture)));
            }
        }

        void OnChangeInt(ChangeEvent<int> e) {
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_CHANGE_INT, FindElementHandle(e.target), e.newValue);
            } else {
                DispatchEvent("change", e.target, BuildChangeData(e.newValue.ToString()));
            }
        }

        void OnGeometryChanged(GeometryChangedEvent e) {
            float newWidth = e.newRect.width;
            float newHeight = e.newRect.height;

            // Only dispatch if size actually changed (avoid spurious events)
            if (Mathf.Approximately(newWidth, _lastViewportWidth) &&
                Mathf.Approximately(newHeight, _lastViewportHeight)) {
                return;
            }

            _lastViewportWidth = newWidth;
            _lastViewportHeight = newHeight;

            int handle = QuickJSNative.GetHandleForObject(_root);
            if (_eventDispatchHandle >= 0) {
                DispatchEventFastViewport(handle, newWidth, newHeight);
            } else {
                int w = (int)newWidth;
                int h = (int)newHeight;
                string data = $"{{\"width\":{w},\"height\":{h}}}";
                DispatchEventInternal(handle, "viewportchange", data);
            }
        }

        // MARK: Core Event Dispatch
        int FindElementHandle(IEventHandler target) {
            // Single-lock parent-chain walk (was one lock + dict lookup per hop).
            return QuickJSNative.GetHandleForElementOrAncestor(target as VisualElement);
        }

        /// <summary>
        /// Core dispatch method: all event dispatching goes through here.
        /// </summary>
        // Returns the suppression-flags bitmask from __dispatchEvent (bit0=propagationStopped,
        // bit1=defaultPrevented), or 0 if nothing was dispatched.
        int DispatchEventInternal(int handle, string eventType, string dataJson) {
            if (handle == 0 || _inEval) return 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            // qjs_dispatch_event returns the suppression-flags bitmask (bit0=propagationStopped,
            // bit1=defaultPrevented), so preventDefault() suppresses native behavior on WebGL too.
            return QuickJSNative.qjs_dispatch_event(handle, eventType, dataJson);
#else
            _sb.Clear();
            _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
            _sb.Append(handle);
            _sb.Append(",\"");
            _sb.Append(eventType);
            _sb.Append("\",");
            _sb.Append(dataJson);
            _sb.Append(")");

            // Hold _inEval through ExecutePendingJobs to prevent cascading events
            // during React reconciliation (matches DispatchEventFast semantics).
            _inEval = true;
            try {
                // __dispatchEvent returns the suppression-flags bitmask; the eval result carries it.
                string result = _ctx.Eval(_sb.ToString());
                _ctx.ExecutePendingJobs();
                return (result != null && int.TryParse(result, out int flags)) ? flags : 0;
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}\nEval: {_sb}");
                return 0;
            } finally {
                _inEval = false;
            }
#endif
        }

        // MARK: Zero-Alloc Event Dispatch

        void DispatchEventFast(int eventTypeId, int elemHandle) {
            if (elemHandle == 0 || _inEval) return;
            _inEval = true;
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, eventTypeId, elemHandle, 0);
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error ({eventTypeId}): {ex.Message}");
            } finally { _inEval = false; }
        }

        void DispatchEventFast(int eventTypeId, int elemHandle, float a0) {
            if (elemHandle == 0 || _inEval) return;
            _inEval = true;
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, eventTypeId, elemHandle, a0);
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error ({eventTypeId}): {ex.Message}");
            } finally { _inEval = false; }
        }

        void DispatchEventFast(int eventTypeId, int elemHandle, int a0) {
            if (elemHandle == 0 || _inEval) return;
            _inEval = true;
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, eventTypeId, elemHandle, a0);
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error ({eventTypeId}): {ex.Message}");
            } finally { _inEval = false; }
        }

        // Pointer/click fast path. Returns the suppression-flags bitmask from the JS dispatch
        // (bit0=propagationStopped, bit1=defaultPrevented), or 0.
        int DispatchEventFast(int eventTypeId, int elemHandle, float x, float y, int button, int pointerId) {
            if (elemHandle == 0 || _inEval) return 0;
            _inEval = true;
            try {
                int flags = _ctx.InvokeCallbackReturnInt(_eventDispatchHandle, eventTypeId, elemHandle, x, y, button, pointerId);
                _ctx.ExecutePendingJobs();
                return flags;
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error ({eventTypeId}): {ex.Message}");
                return 0;
            } finally { _inEval = false; }
        }

        void DispatchEventFastViewport(int elemHandle, float width, float height) {
            if (elemHandle == 0 || _inEval) return;
            _inEval = true;
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, EVT_VIEWPORT_CHANGE, elemHandle, width, height);
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error (viewport): {ex.Message}");
            } finally { _inEval = false; }
        }

        /// <summary>
        /// Emits a "focuschange" event to JS (targeted at the panel root) whenever the
        /// panel's focused element changes. Called once per Tick, outside _inEval, so it
        /// observes the settled focus, including programmatic focus that the FocusIn/Out
        /// event path drops. The JS focus-visible manager subscribes to this to keep the
        /// focus ring in sync with navigation. Diffs by element reference (cheap); only
        /// resolves handles + dispatches on an actual change.
        /// </summary>
        void CheckFocusChange() {
            if (_eventDispatchHandle < 0) return;
            var fe = _root?.focusController?.focusedElement as VisualElement;
            if (fe == _lastFocusedElement) return;
            _lastFocusedElement = fe;

            int rootHandle = QuickJSNative.GetHandleForObject(_root);
            int focusedHandle = fe != null ? QuickJSNative.GetHandleForElementOrAncestor(fe) : 0;
            DispatchEventFastFocusChange(rootHandle, focusedHandle);
        }

        void DispatchEventFastFocusChange(int rootHandle, int focusedHandle) {
            if (rootHandle == 0 || _inEval) return;
            _inEval = true;
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, EVT_FOCUSCHANGE, rootHandle, focusedHandle);
                _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error (focuschange): {ex.Message}");
            } finally { _inEval = false; }
        }

        /// <summary>
        /// Dispatch an event with pre-built JSON data.
        /// </summary>
        int DispatchEvent(string eventType, IEventHandler target, string dataJson) {
            int handle = FindElementHandle(target);
            return DispatchEventInternal(handle, eventType, dataJson);
        }

        /// <summary>
        /// Dispatch a pointer event with position and button data.
        /// </summary>
        int DispatchPointerEvent(string eventType, IEventHandler target, Vector2 position, int button, int pointerId = 0) {
            int handle = FindElementHandle(target);
            if (handle == 0) return 0;

            string data = string.Format(CultureInfo.InvariantCulture,
                "{{\"x\":{0:F2},\"y\":{1:F2},\"button\":{2},\"pointerId\":{3}}}",
                position.x, position.y, button, pointerId);

            return DispatchEventInternal(handle, eventType, data);
        }

        /// <summary>
        /// Dispatch a wheel event carrying the scroll delta. WheelEvent.delta is a Vector3
        /// (z is unused); exposed to JS as flat { deltaX, deltaY } to mirror the DOM WheelEvent
        /// (e.deltaX / e.deltaY) and to keep the event-data object flat like the other events.
        /// </summary>
        int DispatchWheelEvent(string eventType, IEventHandler target, Vector3 delta) {
            int handle = FindElementHandle(target);
            if (handle == 0) return 0;

            // Avoid `string.Format` here: a trailing `{1:F4}}}` (format-spec placeholder
            // followed by `}}`) is parsed inconsistently on Mono and corrupts the final
            // field, same hazard documented in RectToJson. Plain `.ToString` sidesteps it.
            var inv = CultureInfo.InvariantCulture;
            string data = "{\"deltaX\":" + delta.x.ToString("F4", inv)
                        + ",\"deltaY\":" + delta.y.ToString("F4", inv)
                        + "}";

            return DispatchEventInternal(handle, eventType, data);
        }

        /// <summary>
        /// Dispatch a pointer capture transition event (pointercapture / pointercaptureout).
        /// Unlike other pointer events these carry no position or button, only the
        /// pointerId involved in the capture change.
        /// </summary>
        void DispatchPointerCaptureEvent(string eventType, IEventHandler target, int pointerId) {
            int handle = FindElementHandle(target);
            if (handle == 0) return;

            string data = string.Format(CultureInfo.InvariantCulture,
                "{{\"pointerId\":{0}}}", pointerId);

            DispatchEventInternal(handle, eventType, data);
        }

        /// <summary>
        /// Dispatch a keyboard event with key and modifier data.
        /// </summary>
        void DispatchKeyEvent(string eventType, IEventHandler target, KeyCode keyCode, char character, EventModifiers modifiers) {
            int handle = FindElementHandle(target);
            if (handle == 0) return;

            string charEscaped = character != '\0' ? EscapeForJson(character.ToString()) : "";
            string data = string.Format(CultureInfo.InvariantCulture,
                "{{\"keyCode\":{0},\"key\":\"{1}\",\"char\":\"{2}\",\"shift\":{3},\"ctrl\":{4},\"alt\":{5},\"meta\":{6}}}",
                (int)keyCode,
                keyCode.ToString(),
                charEscaped,
                (modifiers & EventModifiers.Shift) != 0 ? "true" : "false",
                (modifiers & EventModifiers.Control) != 0 ? "true" : "false",
                (modifiers & EventModifiers.Alt) != 0 ? "true" : "false",
                (modifiers & EventModifiers.Command) != 0 ? "true" : "false");

            DispatchEventInternal(handle, eventType, data);
        }

        // MARK: Per-Element Pointer Handlers (capture support)
        // Unity 6 dispatches captured pointer events directly to the capturing element,
        // bypassing TrickleDown/BubbleUp on ancestors. These per-element handlers ensure
        // JS event handlers fire during pointer capture. Dedup via reference equality
        // prevents double-dispatch when both _root TrickleDown and per-element fire.

        internal void RegisterPerElementHandler(VisualElement element, string eventType) {
            int handle = QuickJSNative.GetHandleForObject(element);
            if (handle <= 0) return;
            var key = (handle, eventType);
            if (_perElementHandlers.TryGetValue(key, out var existing)) {
                if (ReferenceEquals(existing, element)) return; // Same element, already registered
                // Stale entry from recycled handle: unregister old before re-registering
                UnregisterCallbackForEventType(existing, eventType);
                _perElementHandlers.Remove(key);
            }
            _perElementHandlers[key] = element;

            switch (eventType) {
                case "pointerdown":
                    element.RegisterCallback<PointerDownEvent>(OnPerElementPointerDown);
                    break;
                case "pointerup":
                    element.RegisterCallback<PointerUpEvent>(OnPerElementPointerUp);
                    break;
                case "pointermove":
                    element.RegisterCallback<PointerMoveEvent>(OnPerElementPointerMove);
                    break;
                case "pointercancel":
                    element.RegisterCallback<PointerCancelEvent>(OnPerElementPointerCancel);
                    break;
                case "pointercapture":
                    element.RegisterCallback<PointerCaptureEvent>(OnPerElementPointerCapture);
                    break;
                case "pointercaptureout":
                    element.RegisterCallback<PointerCaptureOutEvent>(OnPerElementPointerCaptureOut);
                    break;
                case "geometrychanged":
                    element.RegisterCallback<GeometryChangedEvent>(OnPerElementGeometryChanged);
                    break;
            }
        }

        internal void UnregisterPerElementHandler(VisualElement element, string eventType) {
            int handle = QuickJSNative.GetHandleForObject(element);
            if (handle <= 0) return;
            var key = (handle, eventType);
            // Only remove if the registered element matches (handles can be recycled)
            if (!_perElementHandlers.TryGetValue(key, out var existing) || !ReferenceEquals(existing, element))
                return;
            _perElementHandlers.Remove(key);
            UnregisterCallbackForEventType(element, eventType);
        }

        void UnregisterCallbackForEventType(VisualElement element, string eventType) {
            switch (eventType) {
                case "pointerdown":
                    element.UnregisterCallback<PointerDownEvent>(OnPerElementPointerDown);
                    break;
                case "pointerup":
                    element.UnregisterCallback<PointerUpEvent>(OnPerElementPointerUp);
                    break;
                case "pointermove":
                    element.UnregisterCallback<PointerMoveEvent>(OnPerElementPointerMove);
                    break;
                case "pointercancel":
                    element.UnregisterCallback<PointerCancelEvent>(OnPerElementPointerCancel);
                    break;
                case "pointercapture":
                    element.UnregisterCallback<PointerCaptureEvent>(OnPerElementPointerCapture);
                    break;
                case "pointercaptureout":
                    element.UnregisterCallback<PointerCaptureOutEvent>(OnPerElementPointerCaptureOut);
                    break;
                case "geometrychanged":
                    element.UnregisterCallback<GeometryChangedEvent>(OnPerElementGeometryChanged);
                    break;
            }
        }

        void UnregisterAllPerElementHandlers() {
            _perElementHandlers.Clear();
            // Element callbacks hold method references but elements are being destroyed
            // during bridge disposal, so explicit unregistration is not needed here.
        }

        // Per-element handlers fire during pointer capture, when the captured element
        // receives events directly and the _root TrickleDown handler may not run (see the
        // dedup note above). They mirror the root handlers' fast-path branch and suppression
        // wiring: captured pointermove (the hottest drag path) takes the same zero-alloc
        // DispatchEventFast route as the root, and preventDefault() keeps suppressing native
        // controls mid-drag, not just on the initial press. When both _root and per-element
        // fire, the timestamp dedup makes this a no-op (the root handler already ran).
        void OnPerElementPointerDown(PointerDownEvent e) {
            if (e.timestamp == _lastDispatchedPointerDownTs) return;
            _lastDispatchedPointerDownTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_DOWN, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerUp(PointerUpEvent e) {
            if (e.timestamp == _lastDispatchedPointerUpTs) return;
            _lastDispatchedPointerUpTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_UP, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerMove(PointerMoveEvent e) {
            if (!InputBridge.PointerMoveEventsEnabled) return;
            if (e.timestamp == _lastDispatchedPointerMoveTs) return;
            _lastDispatchedPointerMoveTs = e.timestamp;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_MOVE, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerCancel(PointerCancelEvent e) {
            if (e.timestamp == _lastDispatchedPointerCancelTs) return;
            _lastDispatchedPointerCancelTs = e.timestamp;
            int flags = DispatchPointerEvent("pointercancel", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerCapture(PointerCaptureEvent e) {
            if (e.timestamp == _lastDispatchedPointerCaptureTs) return;
            _lastDispatchedPointerCaptureTs = e.timestamp;
            DispatchPointerCaptureEvent("pointercapture", e.target, e.pointerId);
        }

        void OnPerElementPointerCaptureOut(PointerCaptureOutEvent e) {
            if (e.timestamp == _lastDispatchedPointerCaptureOutTs) return;
            _lastDispatchedPointerCaptureOutTs = e.timestamp;
            DispatchPointerCaptureEvent("pointercaptureout", e.target, e.pointerId);
        }

        void OnPerElementGeometryChanged(GeometryChangedEvent e) {
            int handle = FindElementHandle(e.target);
            if (handle == 0) return;
            DispatchGeometryEvent("geometrychanged", handle, e.oldRect, e.newRect);
        }

        void DispatchGeometryEvent(string eventType, int handle, Rect oldRect, Rect newRect) {
            if (_inEval) return;
            string data = "{\"oldRect\":" + RectToJson(oldRect)
                        + ",\"newRect\":" + RectToJson(newRect) + "}";
            DispatchEventInternal(handle, eventType, data);
        }

        static string RectToJson(Rect r) {
            // Avoid `string.Format` here: `{3:F2}}}` at the end of a format
            // string is parsed inconsistently on Mono (the trailing `}}` gets
            // partially absorbed into the format spec), corrupting the final
            // field. Plain `.ToString` with the invariant culture sidesteps it.
            var inv = CultureInfo.InvariantCulture;
            return "{\"x\":" + r.x.ToString("F2", inv)
                 + ",\"y\":" + r.y.ToString("F2", inv)
                 + ",\"width\":" + r.width.ToString("F2", inv)
                 + ",\"height\":" + r.height.ToString("F2", inv)
                 + "}";
        }

        // MARK: Data Builders
        static string BuildChangeData(string valueJson) => $"{{\"value\":{valueJson}}}";

        // MARK: String Escaping
        /// <summary>
        /// Escape a string for safe inclusion in JSON.
        /// </summary>
        static string EscapeForJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";

            // Fast path: check if escaping is needed
            bool needsEscape = false;
            foreach (char c in s) {
                if (c == '\\' || c == '"' || c == '\n' || c == '\r' || c == '\t') {
                    needsEscape = true;
                    break;
                }
            }
            if (!needsEscape) return s;

            // Slow path: build escaped string
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s) {
                switch (c) {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
