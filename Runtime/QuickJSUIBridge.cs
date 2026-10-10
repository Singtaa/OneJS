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
        bool _inEval; // JS this bridge entered itself, the tick or an event, is running (all platforms)

        // This bridge's JS is on the stack: in the tick or an event this bridge
        // dispatched (_inEval), or in a call that JS made into C#, however the JS was
        // entered (Eval, GetJSFunction, a C# delegate holding a JS function). That
        // pointer is set while a call from JS is dispatched, which is where every
        // event JS causes is raised.
        bool JsRunning => _inEval || (_ctx != null && QuickJSNative.CurrentContextPtr == _ctx.NativePtr);

        // Enters JS for an event. Inside JS that is already running, the handler runs
        // there and then, as a browser runs one inside el.focus() or el.click(); the
        // microtask checkpoint is left to the outermost JS, as a browser leaves it
        // until the stack is empty. Returns whether this is the outermost.
        bool EnterEvent(out bool wasInEval) {
            bool outermost = !JsRunning;
            wasInEval = _inEval;
            _inEval = true;
            return outermost;
        }
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
        const int EVT_FOCUS_IN = 23;
        const int EVT_FOCUS_OUT = 24;
        const int EVT_VIEWPORT_CHANGE = 30;
        const int EVT_NAVIGATION_MOVE = 40;
        const int EVT_NAVIGATION_SUBMIT = 41;
        const int EVT_NAVIGATION_CANCEL = 42;
        // UI Toolkit's mouse events, which it raises from the primary pointer beside the
        // pointer events: { x, y, button }
        const int EVT_MOUSE_DOWN = 50;
        const int EVT_MOUSE_UP = 51;
        const int EVT_MOUSE_MOVE = 52;
        const int EVT_MOUSE_ENTER = 53;
        const int EVT_MOUSE_LEAVE = 54;
        const int EVT_MOUSE_OVER = 55;
        const int EVT_MOUSE_OUT = 56;

        // Viewport tracking for responsive design
        float _lastViewportWidth;
        float _lastViewportHeight;

        // Focus tracking: the panel's focusController.focusedElement at the previous
        // tick. Diffed each Tick to emit one "focuschange" to JS for the settled focus,
        // however it moved (this runs outside _inEval).
        VisualElement _lastFocusedElement;

        // Per-element C# handler registry for events that don't reach _root's
        // TrickleDown hook: captured pointer events (Unity 6 delivers them directly
        // to the capturing element) and non-bubbling events like GeometryChangedEvent.
        readonly Dictionary<(int handle, string eventType), VisualElement> _perElementHandlers = new();

        // Dedup: prevent double-dispatch when both _root TrickleDown and per-element
        // fire for the same event. UI Toolkit's event pool reuses instances across
        // dispatches, so a reference-equality check would treat consecutive pooled
        // events as duplicates and silently drop them (this was the WebGL drag
        // regression). EventBase.timestamp is no better: it is whole milliseconds,
        // so two distinct events inside one millisecond (fast frames, several
        // pointers moving in one frame) compare equal and the second never reaches
        // JS. EventBase.eventId is assigned from a counter each time an event is
        // acquired from the pool, so it is the same within one dispatch (root +
        // per-element phases) and different across dispatches. It is internal, so
        // it is bound once below; nothing public tells two dispatches apart.
        ulong _lastDispatchedPointerDownId = ulong.MaxValue;
        ulong _lastDispatchedPointerUpId = ulong.MaxValue;
        ulong _lastDispatchedPointerMoveId = ulong.MaxValue;
        ulong _lastDispatchedPointerCancelId = ulong.MaxValue;
        ulong _lastDispatchedPointerCaptureId = ulong.MaxValue;
        ulong _lastDispatchedPointerCaptureOutId = ulong.MaxValue;
        // The per-element events below reach C# once for each element along the path that
        // JS listens on, so each remembers the last event of its type it sent to JS. Keyed by
        // event type, because a handler can raise an event of another type mid-dispatch.
        readonly Dictionary<long, ulong> _lastPerElementEventIds = new();

        // Open delegate over the internal getter: no allocation per event. Looked up
        // by method name because stripping can drop property metadata while keeping
        // the getter, which UI Toolkit itself calls. If a Unity upgrade removes it,
        // the dedup falls back to the timestamp and warns, and
        // PointerMove_TwoInOneFrame_EachReachesJsOnce fails.
        static readonly Func<EventBase, ulong> s_eventId = BindEventId();

        static Func<EventBase, ulong> BindEventId() {
            var getter = typeof(EventBase).GetMethod("get_eventId",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (getter != null && getter.ReturnType == typeof(ulong)) {
                try {
                    return (Func<EventBase, ulong>)Delegate.CreateDelegate(typeof(Func<EventBase, ulong>), getter);
                } catch (Exception) { }
            }
            Debug.LogWarning("[OneJS] EventBase.eventId is unavailable; pointer events inside one millisecond may be deduplicated as one.");
            return null;
        }

        // True the first time a handler sees this dispatch, false when another
        // handler (root TrickleDown or per-element) already dispatched it to JS.
        static bool IsNewDispatch(ref ulong lastId, EventBase e) {
            ulong id = s_eventId != null ? s_eventId(e) : (ulong)e.timestamp;
            if (id == lastId) return false;
            lastId = id;
            return true;
        }

        public QuickJSContext Context => _ctx;
        public VisualElement Root => _root;
        public string WorkingDir => _workingDir;
        public int WebSocketContextId => _wsContextId;

        Func<string, string> _translateError;

        /// <summary>
        /// Maps JS positions in this bridge's errors to source lines (a runner's
        /// source map), both in the exceptions it logs and in console.error lines
        /// its scripts print. Null logs them as JS reported them.
        /// </summary>
        public Func<string, string> TranslateError {
            get => _translateError;
            set {
                _translateError = value;
                JsLog.SetTranslator(_wsContextId, value);
                QuickJSNative.SetContextTranslator(_ctx.NativePtr, value);
            }
        }

        /// <summary>Logs an exception caught running this bridge's JS, its frames source-mapped.</summary>
        void LogJsError(string message, Exception ex) => OneJSLog.Exception(message, ex, translate: _translateError);

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
                // Expected failure: the file could not be read (compiling has its
                // own catch below), so the message is all a reader needs.
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
                WarnAboutDiagnostics(name);
                _root.styleSheets.Add(styleSheet);
                _jsStyleSheets[name] = styleSheet;
                return true;
            } catch (Exception ex) {
                OneJSLog.Exception($"[QuickJSUIBridge] CompileStyleSheet error ({name})", ex);
                return false;
            }
        }

        /// <summary>
        /// Diagnostics from the most recent CompileStyleSheet call: the
        /// declarations the tolerant parse kept but UI Toolkit will ignore.
        /// Empty when the last sheet was clean. Primarily for tests.
        /// </summary>
        public IReadOnlyList<UssCompiler.UssDiagnostic> LastStyleDiagnostics => _ussCompiler.Diagnostics;

        void WarnAboutDiagnostics(string sheetName) {
            var diags = _ussCompiler.Diagnostics;
            if (diags.Count == 0) return;
            var sb = new StringBuilder(128);
            sb.Append("[OneJS] Stylesheet '").Append(sheetName).Append("': ")
              .Append(diags.Count).Append(" declaration(s) compile but will be ignored:");
            int shown = Math.Min(diags.Count, 10);
            for (int i = 0; i < shown; i++) {
                sb.Append("\n  ").Append(diags[i]);
            }
            if (diags.Count > shown) {
                sb.Append("\n  (and ").Append(diags.Count - shown).Append(" more)");
            }
            Debug.LogWarning(sb.ToString());
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

        /// <summary>
        /// Teardown for subsystems in assemblies this one cannot name (Input, Models):
        /// raised with the context's id right after the subsystems above dispose what
        /// it made. Each subscribes on load, unsubscribing first, since a domain reload
        /// clears the handler and play-mode entry without one does not.
        /// </summary>
        internal static event Action<int> ContextTornDown;

        /// <summary>Raised when the last context goes, after <see cref="ContextTornDown"/>.</summary>
        internal static event Action LastContextTornDown;

        void Dispose(bool disposing) {
            if (_disposed) return;
            _disposed = true;

            // The pending-task queue, the handle table and the C#-owned resource
            // registries are shared by every live context (see QuickJSNative), so
            // only wipe them wholesale when the FINAL bridge is going away.
            // Decrement exactly once per bridge, and only if construction counted it.
            bool lastBridge = _countedLive
                && System.Threading.Interlocked.Decrement(ref _liveBridgeCount) <= 0;

            // Run JS-registered teardown hooks (e.g. React unmounts its roots, firing
            // useEffect/useLayoutEffect cleanups) while the context is still alive. This
            // is the single chokepoint for every teardown path (hot reload, play/edit
            // stop, destroy), so cleanups fire consistently before the context is torn
            // down. Skipped on the finalizer path: it runs on a GC thread where calling
            // back into QuickJS would be unsafe.
            if (disposing) {
                RunTeardownHooks();
                // Safety net: dispose what this context made in every C#-owned
                // subsystem, leaving another JSRunner's running. Normal disposal
                // already happened via effect cleanups inside the teardown hooks
                // above. The last bridge also sweeps anything made outside a JS
                // call, plus the fx pool and materials. Not on the finalizer path
                // (touches VisualElements). Shader effects before SL: an effect
                // releases its own program.
                int owner = _ctx?.Id ?? 0;
                ParticleBridge.DisposeOwnedBy(owner);
                Physics2DBridge.DisposeOwnedBy(owner);
                OneJS.ShaderFX.ShaderEffectBridge.DisposeOwnedBy(owner);
                OneJS.Fx.FxBridge.DisposeOwnedBy(owner);
                OneJS.SL.SLProgramBridge.DisposeOwnedBy(owner);
                OneJS.GPU.GPUBridge.DisposeOwnedBy(owner);
                OneJS.Audio.AudioBridge.DisposeOwnedBy(owner);
                ContextTornDown?.Invoke(owner);
                if (lastBridge) {
                    ParticleBridge.DisposeAll();
                    Physics2DBridge.DisposeAll();
                    OneJS.ShaderFX.ShaderEffectBridge.DisposeAll();
                    OneJS.Fx.FxBridge.DisposeAll();
                    OneJS.SL.SLProgramBridge.DisposeAll();
                    OneJS.GPU.GPUBridge.Cleanup();
                    OneJS.Audio.AudioBridge.Dispose();
                    LastContextTornDown?.Invoke();
                }
            }

            _tickCallbackHandle = -1;
            _eventDispatchHandle = -1;

            UnregisterEventDelegation();
            UnregisterAllPerElementHandlers();
            PerElementEventSupport.UnregisterBridge(_wsContextId);
            JsLog.SetTranslator(_wsContextId, null);
            ClearStyleSheets(); // Clean up JS-loaded stylesheets
            WebSocketBridge.CloseAll(_wsContextId);
            WebSocketBridge.UnregisterContext(_wsContextId);

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
                LogJsError("[QuickJSUIBridge] Teardown hook error", ex);
            }
        }

        // MARK: Public API
        public string Eval(string code, string filename = "<input>") {
            return _ctx.Eval(code, filename);
        }

        /// <summary>
        /// Returns a typed delegate that calls a JS function by name ("showToast",
        /// or a dotted path from globalThis like "game.ui.showToast"). Bound to
        /// this bridge's context; for a delegate that survives hot reload, use
        /// JSRunner.GetJSFunction. See QuickJSContext.GetJSFunction for details.
        /// </summary>
        public TDelegate GetJSFunction<TDelegate>(string globalName) where TDelegate : Delegate {
            return _ctx.GetJSFunction<TDelegate>(globalName);
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
                LogJsError("[QuickJSUIBridge] Failed to cache __tick callback", ex);
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
                LogJsError("[QuickJSUIBridge] Failed to cache event dispatch callback", ex);
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
        /// Advances the C#-owned per-frame systems: particles, physics and
        /// shader effects. Each is self-guarded against being ticked twice in a
        /// frame by more than one bridge.
        ///
        /// Separate from Tick() because WebGL needs it on its own. There, the JS
        /// side is driven by the browser's requestAnimationFrame and Tick() is
        /// never called from Update, so anything living only inside Tick()
        /// simply stopped on the web: particles did not move and physics did not
        /// simulate, on the one platform with no way to notice from the editor.
        /// Update still runs in a WebGL build, so that is where this is driven
        /// from instead.
        /// </summary>
        public void TickSystems() {
            if (_disposed) return;
            ParticleBridge.TickAll();
            Physics2DBridge.TickAll();
            OneJS.ShaderFX.ShaderEffectBridge.TickAll();
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL is the only platform where this is not reached through
            // Tick(), and the only one where Update never calls Tick() at all:
            // the JS scheduler is driven by the browser's requestAnimationFrame
            // instead. Settling completed C# Tasks was left behind in Tick(),
            // so on WebGL every one of them stayed pending forever.
            //
            // What that meant in practice: Network.LoadTextureFromUrl and
            // AudioBridge.LoadClip both return a Task, so <Image src="...">
            // never showed an image and audio.load() never resolved, in every
            // web build, with no error anywhere. The promise simply never
            // settled. This is the same oversight that once left particles and
            // physics frozen here, one layer further in.
            //
            // Safe to do from Update on this platform specifically. Settling a
            // promise only schedules its continuations, and qjs_execute_pending
            // _jobs is a no-op on WebGL because the browser owns the microtask
            // queue: the .then handlers therefore run after this stack unwinds,
            // outside the player loop, which is exactly the recursion the WebGL
            // tick arrangement exists to avoid.
            if (!_inEval) {
                _inEval = true;
                try {
                    QuickJSNative.ProcessCompletedTasks(_ctx);
                    WebSocketBridge.ProcessEvents(_ctx, _wsContextId);
                } finally {
                    _inEval = false;
                }
            }

            // Tick() is where every other platform notices a focus change, and
            // Tick() never runs here, so focuschange never reached a web app and
            // onejs-ui's focus ring missed programmatic focus. The dispatch runs
            // handlers from Update, as every UI Toolkit input event already does
            // on this platform.
            CheckFocusChange();
#endif
        }

        /// <summary>
        /// Call every frame from Update() to drive RAF, timers, and Promise microtasks.
        /// Uses zero-allocation path when tick callback is cached.
        /// </summary>
        public void Tick() {
            if (_disposed || JsRunning) return;

            TickSystems();

            // Detect focus changes before entering the eval block (CheckFocusChange
            // dispatches, which sets _inEval itself), so the settled focus is what
            // focuschange reports.
            CheckFocusChange();

            _inEval = true;

            // No per-frame dedup reset needed: dedup uses EventBase.eventId,
            // which is unique per dispatch (reassigned each time the pool reuses
            // an instance).

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
                LogJsError("[QuickJSUIBridge] Tick error", ex);
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

        // A pointerdown's and a navigation move's native default includes moving focus, which UI
        // Toolkit does in PostDispatch regardless of propagation. FocusController.IgnoreEvent is the
        // public gate PostDispatch checks, so a prevented one is also handed to it and focus stays
        // where it was, as preventDefault() on a DOM mousedown does.
        void ApplyNativeSuppressionAndKeepFocus(EventBase e, int flags) {
            ApplyNativeSuppression(e, flags);
            if ((flags & FLAG_DEFAULT_PREVENTED) != 0) _root.focusController?.IgnoreEvent(e);
        }

        void OnClick(ClickEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_CLICK, FindElementHandle(e.target), e.position.x, e.position.y, e.button, 0)
                : DispatchPointerEvent("click", e.target, e.position, e.button);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerDown(PointerDownEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerDownId, e)) return;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_DOWN, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppressionAndKeepFocus(e, flags);
        }

        void OnPointerUp(PointerUpEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerUpId, e)) return;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_UP, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPointerMove(PointerMoveEvent e) {
            if (!PointerEvents.MoveEventsEnabled) return;
            if (!IsNewDispatch(ref _lastDispatchedPointerMoveId, e)) return;
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
            if (!IsNewDispatch(ref _lastDispatchedPointerCancelId, e)) return;
            DispatchPointerEvent("pointercancel", e.target, e.position, e.button, e.pointerId);
        }

        void OnPointerCapture(PointerCaptureEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerCaptureId, e)) return;
            DispatchPointerCaptureEvent("pointercapture", e.target, e.pointerId);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerCaptureOutId, e)) return;
            DispatchPointerCaptureEvent("pointercaptureout", e.target, e.pointerId);
        }

        // Mouse wheel / trackpad scroll. Takes the zero-alloc fast path when available
        // (active-scroll bursts can approach frame rate on trackpads), falling back to the
        // string path otherwise. The fast path passes the delta as (x=deltaX, y=deltaY); the
        // JS side rebuilds { deltaX, deltaY } for EVT_WHEEL. Only the root TrickleDown handler
        // fires (wheel has no per-element/capture handler), so no eventId dedup is needed.
        // WheelEvent.delta is a Vector3; the z component is unused.
        void OnWheel(WheelEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_WHEEL, FindElementHandle(e.target), e.delta.x, e.delta.y, 0, 0)
                : DispatchWheelEvent("wheel", e.target, e.delta);
            ApplyNativeSuppression(e, flags);
        }

        // Each native focus change reaches JS as both of the DOM's pairs, in the DOM's order:
        // focus/blur, which the bootstrap does not bubble, then focusin/focusout, which it does,
        // so an ancestor sees a descendant gain or lose focus (FocusScope's trap depends on it).
        // Focus changes are rare, so the second crossing costs nothing that matters.
        void OnFocusIn(FocusInEvent e) {
            int handle = FindElementHandle(e.target);
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_FOCUS, handle);
                DispatchEventFast(EVT_FOCUS_IN, handle);
            } else {
                DispatchEventInternal(handle, "focus", "{}");
                DispatchEventInternal(handle, "focusin", "{}");
            }
        }

        void OnFocusOut(FocusOutEvent e) {
            int handle = FindElementHandle(e.target);
            if (_eventDispatchHandle >= 0) {
                DispatchEventFast(EVT_BLUR, handle);
                DispatchEventFast(EVT_FOCUS_OUT, handle);
            } else {
                DispatchEventInternal(handle, "blur", "{}");
                DispatchEventInternal(handle, "focusout", "{}");
            }
        }

        // Key events stay on eval path (need string args). preventDefault() is mirrored like the
        // pointer handlers, so a prevented key never reaches the target's own callbacks: a
        // TextField does not receive it. It does not cancel the NavigationMove/Submit/Cancel the
        // same key press produces, which arrives as its own event with its own handlers.
        void OnKeyDown(KeyDownEvent e) =>
            ApplyNativeSuppression(e, DispatchKeyEvent("keydown", e.target, e.keyCode, e.character, e.modifiers));
        void OnKeyUp(KeyUpEvent e) =>
            ApplyNativeSuppression(e, DispatchKeyEvent("keyup", e.target, e.keyCode, '\0', e.modifiers));

        // Navigation events (controller / keyboard focus navigation)
        // preventDefault() is mirrored like the pointer handlers. A NavigationMove's native default
        // is moving focus, so a prevented one also keeps focus where it was.
        void OnNavigationMove(NavigationMoveEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_NAVIGATION_MOVE, FindElementHandle(e.target), (int)e.direction)
                : DispatchEvent("navigationmove", e.target,
                    $"{{\"direction\":\"{NavigationDirectionName(e.direction)}\"}}");
            ApplyNativeSuppressionAndKeepFocus(e, flags);
        }

        void OnNavigationSubmit(NavigationSubmitEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_NAVIGATION_SUBMIT, FindElementHandle(e.target))
                : DispatchEvent("navigationsubmit", e.target, "{}");
            ApplyNativeSuppression(e, flags);
        }

        void OnNavigationCancel(NavigationCancelEvent e) {
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_NAVIGATION_CANCEL, FindElementHandle(e.target))
                : DispatchEvent("navigationcancel", e.target, "{}");
            ApplyNativeSuppression(e, flags);
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
            // A text's content changing is not a change: TextElement, Label and Button
            // raise one for every text set, from JS or C#, and a browser reports none
            if (e.target is TextElement) return;
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
                DispatchEvent("change", e.target, BuildChangeData(JsonFloat(e.newValue)));
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
                string data = "{\"width\":" + JsonFloat(newWidth) + ",\"height\":" + JsonFloat(newHeight) + "}";
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
            if (handle == 0) return 0;

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

            bool outermost = EnterEvent(out bool wasInEval);
            try {
                // __dispatchEvent returns the suppression-flags bitmask; the eval result carries it.
                string result = _ctx.Eval(_sb.ToString());
                if (outermost) _ctx.ExecutePendingJobs();
                return (result != null && int.TryParse(result, out int flags)) ? flags : 0;
            } catch (Exception ex) {
                LogJsError($"[QuickJSUIBridge] Event dispatch error, evaluating: {_sb}", ex);
                return 0;
            } finally {
                _inEval = wasInEval;
            }
#endif
        }

        // MARK: Zero-Alloc Event Dispatch

        // The no-payload and int overloads return the suppression-flags bitmask like the pointer
        // overload below; callers without a native default to suppress ignore it.
        int DispatchEventFast(int eventTypeId, int elemHandle) => DispatchEventFast(eventTypeId, elemHandle, 0);

        void DispatchEventFast(int eventTypeId, int elemHandle, float a0) {
            if (elemHandle == 0) return;
            bool outermost = EnterEvent(out bool wasInEval);
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, eventTypeId, elemHandle, a0);
                if (outermost) _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                LogJsError($"[QuickJSUIBridge] Event dispatch error ({eventTypeId})", ex);
            } finally { _inEval = wasInEval; }
        }

        int DispatchEventFast(int eventTypeId, int elemHandle, int a0) {
            if (elemHandle == 0) return 0;
            bool outermost = EnterEvent(out bool wasInEval);
            try {
                int flags = _ctx.InvokeCallbackReturnInt(_eventDispatchHandle, eventTypeId, elemHandle, a0);
                if (outermost) _ctx.ExecutePendingJobs();
                return flags;
            } catch (Exception ex) {
                LogJsError($"[QuickJSUIBridge] Event dispatch error ({eventTypeId})", ex);
                return 0;
            } finally { _inEval = wasInEval; }
        }

        // Pointer/click fast path. Returns the suppression-flags bitmask from the JS dispatch
        // (bit0=propagationStopped, bit1=defaultPrevented), or 0.
        int DispatchEventFast(int eventTypeId, int elemHandle, float x, float y, int button, int pointerId) {
            if (elemHandle == 0) return 0;
            bool outermost = EnterEvent(out bool wasInEval);
            try {
                int flags = _ctx.InvokeCallbackReturnInt(_eventDispatchHandle, eventTypeId, elemHandle, x, y, button, pointerId);
                if (outermost) _ctx.ExecutePendingJobs();
                return flags;
            } catch (Exception ex) {
                LogJsError($"[QuickJSUIBridge] Event dispatch error ({eventTypeId})", ex);
                return 0;
            } finally { _inEval = wasInEval; }
        }

        void DispatchEventFastViewport(int elemHandle, float width, float height) {
            if (elemHandle == 0) return;
            bool outermost = EnterEvent(out bool wasInEval);
            try {
                _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, EVT_VIEWPORT_CHANGE, elemHandle, width, height);
                if (outermost) _ctx.ExecutePendingJobs();
            } catch (Exception ex) {
                LogJsError("[QuickJSUIBridge] Event dispatch error (viewport)", ex);
            } finally { _inEval = wasInEval; }
        }

        /// <summary>
        /// Emits a "focuschange" event to JS (targeted at the panel root) whenever the
        /// panel's focused element changes. Called once per Tick, outside _inEval, so it
        /// observes the settled focus, however it moved. The JS focus-visible manager subscribes to this to keep the
        /// focus ring in sync with navigation. Diffs by element reference (cheap); only
        /// resolves the root's handle + dispatches on an actual change. The event carries
        /// nothing: a handler asks the panel what is focused.
        /// </summary>
        void CheckFocusChange() {
            var fe = _root?.focusController?.focusedElement as VisualElement;
            if (fe == _lastFocusedElement) return;
            _lastFocusedElement = fe;

            int rootHandle = QuickJSNative.GetHandleForObject(_root);
            if (_eventDispatchHandle >= 0) DispatchEventFast(EVT_FOCUSCHANGE, rootHandle);
            else DispatchEventInternal(rootHandle, "focuschange", "{}");
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

            string data = "{\"x\":" + JsonFloat(position.x) + ",\"y\":" + JsonFloat(position.y)
                        + ",\"button\":" + button.ToString(CultureInfo.InvariantCulture)
                        + ",\"pointerId\":" + pointerId.ToString(CultureInfo.InvariantCulture) + "}";

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

            string data = "{\"deltaX\":" + JsonFloat(delta.x) + ",\"deltaY\":" + JsonFloat(delta.y) + "}";

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
        int DispatchKeyEvent(string eventType, IEventHandler target, KeyCode keyCode, char character, EventModifiers modifiers) {
            int handle = FindElementHandle(target);
            if (handle == 0) return 0;

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

            return DispatchEventInternal(handle, eventType, data);
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
                case "mousedown": element.RegisterCallback<MouseDownEvent>(OnPerElementMouseDown); break;
                case "mouseup": element.RegisterCallback<MouseUpEvent>(OnPerElementMouseUp); break;
                case "mousemove": element.RegisterCallback<MouseMoveEvent>(OnPerElementMouseMove); break;
                case "mouseenter": element.RegisterCallback<MouseEnterEvent>(OnPerElementMouseEnter); break;
                case "mouseleave": element.RegisterCallback<MouseLeaveEvent>(OnPerElementMouseLeave); break;
                case "mouseover": element.RegisterCallback<MouseOverEvent>(OnPerElementMouseOver); break;
                case "mouseout": element.RegisterCallback<MouseOutEvent>(OnPerElementMouseOut); break;
                case "input": element.RegisterCallback<InputEvent>(OnPerElementInput); break;
                case "transitionrun": element.RegisterCallback<TransitionRunEvent>(OnPerElementTransitionRun); break;
                case "transitionstart": element.RegisterCallback<TransitionStartEvent>(OnPerElementTransitionStart); break;
                case "transitionend": element.RegisterCallback<TransitionEndEvent>(OnPerElementTransitionEnd); break;
                case "transitioncancel": element.RegisterCallback<TransitionCancelEvent>(OnPerElementTransitionCancel); break;
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
                case "mousedown": element.UnregisterCallback<MouseDownEvent>(OnPerElementMouseDown); break;
                case "mouseup": element.UnregisterCallback<MouseUpEvent>(OnPerElementMouseUp); break;
                case "mousemove": element.UnregisterCallback<MouseMoveEvent>(OnPerElementMouseMove); break;
                case "mouseenter": element.UnregisterCallback<MouseEnterEvent>(OnPerElementMouseEnter); break;
                case "mouseleave": element.UnregisterCallback<MouseLeaveEvent>(OnPerElementMouseLeave); break;
                case "mouseover": element.UnregisterCallback<MouseOverEvent>(OnPerElementMouseOver); break;
                case "mouseout": element.UnregisterCallback<MouseOutEvent>(OnPerElementMouseOut); break;
                case "input": element.UnregisterCallback<InputEvent>(OnPerElementInput); break;
                case "transitionrun": element.UnregisterCallback<TransitionRunEvent>(OnPerElementTransitionRun); break;
                case "transitionstart": element.UnregisterCallback<TransitionStartEvent>(OnPerElementTransitionStart); break;
                case "transitionend": element.UnregisterCallback<TransitionEndEvent>(OnPerElementTransitionEnd); break;
                case "transitioncancel": element.UnregisterCallback<TransitionCancelEvent>(OnPerElementTransitionCancel); break;
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
        // fire, the eventId dedup makes this a no-op (the root handler already ran).
        void OnPerElementPointerDown(PointerDownEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerDownId, e)) return;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_DOWN, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppressionAndKeepFocus(e, flags);
        }

        void OnPerElementPointerUp(PointerUpEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerUpId, e)) return;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_UP, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerMove(PointerMoveEvent e) {
            if (!PointerEvents.MoveEventsEnabled) return;
            if (!IsNewDispatch(ref _lastDispatchedPointerMoveId, e)) return;
            int flags = _eventDispatchHandle >= 0
                ? DispatchEventFast(EVT_POINTER_MOVE, FindElementHandle(e.target), e.position.x, e.position.y, e.button, e.pointerId)
                : DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerCancel(PointerCancelEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerCancelId, e)) return;
            int flags = DispatchPointerEvent("pointercancel", e.target, e.position, e.button, e.pointerId);
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementPointerCapture(PointerCaptureEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerCaptureId, e)) return;
            DispatchPointerCaptureEvent("pointercapture", e.target, e.pointerId);
        }

        void OnPerElementPointerCaptureOut(PointerCaptureOutEvent e) {
            if (!IsNewDispatch(ref _lastDispatchedPointerCaptureOutId, e)) return;
            DispatchPointerCaptureEvent("pointercaptureout", e.target, e.pointerId);
        }

        void OnPerElementGeometryChanged(GeometryChangedEvent e) {
            int handle = FindElementHandle(e.target);
            if (handle == 0) return;
            DispatchGeometryEvent("geometrychanged", handle, e.oldRect, e.newRect);
        }

        // MARK: Per-Element Mouse, Input and Transition Handlers
        // Registered only on elements JS listens on, so an app that never asks for these
        // pays nothing for them. Each is sent to JS once, from its target; the bootstrap
        // then bubbles it as UI Toolkit does. Mouse events are the ones UI Toolkit raises
        // from the primary pointer beside the pointer events (MouseOver/Out and Down/Up/Move
        // bubble, Enter/Leave reach only the element entered or left); an InputEvent is a
        // TextField's text changing as the user types, and bubbles; the transition events
        // bubble too. preventDefault() on a mouse event is mirrored as on a pointer event.
        bool IsNewPerElementDispatch(EventBase e) {
            ulong id = s_eventId != null ? s_eventId(e) : (ulong)e.timestamp;
            if (_lastPerElementEventIds.TryGetValue(e.eventTypeId, out var last) && last == id) return false;
            _lastPerElementEventIds[e.eventTypeId] = id;
            return true;
        }

        void OnPerElementMouseDown(MouseDownEvent e) => DispatchMouse(e, EVT_MOUSE_DOWN, "mousedown");
        void OnPerElementMouseUp(MouseUpEvent e) => DispatchMouse(e, EVT_MOUSE_UP, "mouseup");
        void OnPerElementMouseMove(MouseMoveEvent e) {
            if (PointerEvents.MoveEventsEnabled) DispatchMouse(e, EVT_MOUSE_MOVE, "mousemove");
        }
        void OnPerElementMouseEnter(MouseEnterEvent e) => DispatchMouse(e, EVT_MOUSE_ENTER, "mouseenter");
        void OnPerElementMouseLeave(MouseLeaveEvent e) => DispatchMouse(e, EVT_MOUSE_LEAVE, "mouseleave");
        void OnPerElementMouseOver(MouseOverEvent e) => DispatchMouse(e, EVT_MOUSE_OVER, "mouseover");
        void OnPerElementMouseOut(MouseOutEvent e) => DispatchMouse(e, EVT_MOUSE_OUT, "mouseout");

        void DispatchMouse<T>(MouseEventBase<T> e, int fastId, string eventType) where T : MouseEventBase<T>, new() {
            if (!IsNewPerElementDispatch(e)) return;
            int handle = FindElementHandle(e.target);
            if (handle == 0) return;
            int flags;
            if (_eventDispatchHandle >= 0) {
                flags = DispatchEventFast(fastId, handle, e.mousePosition.x, e.mousePosition.y, e.button, 0);
            } else {
                string data = "{\"x\":" + JsonFloat(e.mousePosition.x) + ",\"y\":" + JsonFloat(e.mousePosition.y)
                            + ",\"button\":" + e.button.ToString(CultureInfo.InvariantCulture) + "}";
                flags = DispatchEventInternal(handle, eventType, data);
            }
            ApplyNativeSuppression(e, flags);
        }

        void OnPerElementInput(InputEvent e) {
            if (!IsNewPerElementDispatch(e)) return;
            DispatchEvent("input", e.target,
                "{\"value\":\"" + EscapeForJson(e.newData ?? "") + "\",\"previousValue\":\"" + EscapeForJson(e.previousData ?? "") + "\"}");
        }

        void OnPerElementTransitionRun(TransitionRunEvent e) => DispatchTransition(e, "transitionrun");
        void OnPerElementTransitionStart(TransitionStartEvent e) => DispatchTransition(e, "transitionstart");
        void OnPerElementTransitionEnd(TransitionEndEvent e) => DispatchTransition(e, "transitionend");
        void OnPerElementTransitionCancel(TransitionCancelEvent e) => DispatchTransition(e, "transitioncancel");

        // One JS event per property, as a browser sends one per property: UI Toolkit raises
        // one per property too, though its event can name several. propertyName is the USS
        // name ("background-color"), elapsedTime the seconds the transition had run.
        void DispatchTransition<T>(TransitionEventBase<T> e, string eventType) where T : TransitionEventBase<T>, new() {
            if (!IsNewPerElementDispatch(e)) return;
            int handle = FindElementHandle(e.target);
            if (handle == 0) return;
            foreach (var name in e.stylePropertyNames) {
                DispatchEventInternal(handle, eventType,
                    "{\"propertyName\":\"" + EscapeForJson(name.ToString()) + "\",\"elapsedTime\":"
                    + e.elapsedTime.ToString("R", CultureInfo.InvariantCulture) + "}");
            }
        }

        void DispatchGeometryEvent(string eventType, int handle, Rect oldRect, Rect newRect) {
            string data = "{\"oldRect\":" + RectToJson(oldRect)
                        + ",\"newRect\":" + RectToJson(newRect) + "}";
            DispatchEventInternal(handle, eventType, data);
        }

        static string RectToJson(Rect r) =>
            "{\"x\":" + JsonFloat(r.x) + ",\"y\":" + JsonFloat(r.y)
            + ",\"width\":" + JsonFloat(r.width) + ",\"height\":" + JsonFloat(r.height) + "}";

        // MARK: Data Builders
        static string BuildChangeData(string valueJson) => $"{{\"value\":{valueJson}}}";

        /// <summary>
        /// A float as JSON, written as the double it widens to: the value the numeric dispatch
        /// hands a handler, so an event carries the same number on WebGL as in the editor.
        /// G17 rather than R, which does not always round-trip on Mono. JSON has no NaN or
        /// infinity, so those go as null.
        /// </summary>
        static string JsonFloat(float f) =>
            float.IsFinite(f) ? ((double)f).ToString("G17", CultureInfo.InvariantCulture) : "null";

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
