using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OneJS;
using OneJS.CustomStyleSheets;
using OneJS.Input;
using UnityEngine;
using UnityEngine.UIElements;

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
    float _startTime;
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
    const int EVT_FOCUS = 20;
    const int EVT_BLUR = 21;
    const int EVT_VIEWPORT_CHANGE = 30;
    const int EVT_NAVIGATION_MOVE = 40;
    const int EVT_NAVIGATION_SUBMIT = 41;
    const int EVT_NAVIGATION_CANCEL = 42;

    // Viewport tracking for responsive design
    float _lastViewportWidth;
    float _lastViewportHeight;

    // Per-element C# handler registry for events that don't reach _root's
    // TrickleDown hook: captured pointer events (Unity 6 delivers them directly
    // to the capturing element) and non-bubbling events like GeometryChangedEvent.
    readonly Dictionary<(int handle, string eventType), VisualElement> _perElementHandlers = new();
    // Dedup: prevent double-dispatch when both _root TrickleDown and per-element fire
    object _lastDispatchedPointerDown;
    object _lastDispatchedPointerUp;
    object _lastDispatchedPointerMove;

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
                Debug.LogWarning($"[QuickJSUIBridge] StyleSheet not found: {fullPath}");
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
        if (_disposed) return;
        _disposed = true;

        _tickCallbackHandle = -1;
        _eventDispatchHandle = -1;

        UnregisterEventDelegation();
        UnregisterAllPerElementHandlers();
        PerElementEventSupport.UnregisterBridge(_wsContextId);
        ClearStyleSheets(); // Clean up JS-loaded stylesheets
        WebSocketBridge.CloseAll(_wsContextId);
        WebSocketBridge.UnregisterContext(_wsContextId);
        QuickJSNative.ClearPendingTasks();
        _ctx?.Dispose();
        QuickJSNative.ClearAllHandles();

        GC.SuppressFinalize(this);
    }

    ~QuickJSUIBridge() {
        Dispose();
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
        try {
            var handleStr = _ctx.Eval("typeof __tick === 'function' ? __registerCallback(__tick) : -1");
            _tickCallbackHandle = int.Parse(handleStr);
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Failed to cache __tick callback: {ex.Message}");
            _tickCallbackHandle = -1;
        }
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
        _inEval = true;

        // Reset pointer event dedup references to prevent stale pooled-event matches
        _lastDispatchedPointerDown = null;
        _lastDispatchedPointerUp = null;
        _lastDispatchedPointerMove = null;

        try {
            // Process completed C# Tasks and resolve/reject their JS Promises
            QuickJSNative.ProcessCompletedTasks(_ctx);
            WebSocketBridge.ProcessEvents(_ctx, _wsContextId);

            float timestamp = (Time.realtimeSinceStartup - _startTime) * 1000f;

            if (_tickCallbackHandle >= 0) {
                // Zero-allocation path: invoke cached callback directly
                _ctx.InvokeCallbackNoAlloc(_tickCallbackHandle, timestamp);
            } else {
                // Fallback: use Eval (allocates strings)
                _ctx.Eval($"globalThis.__tick && __tick({timestamp.ToString("F2", CultureInfo.InvariantCulture)})");
            }

            // Execute pending Promise jobs (microtasks) - critical for React scheduler
            _ctx.ExecutePendingJobs();

            // Run GC if handle count exceeds threshold. The zero-allocation tick path
            // (InvokeCallbackNoAlloc) bypasses Eval(), which is the only other place
            // GC runs. Without this, FinalizationRegistry callbacks never fire and
            // C# handles leak unboundedly during normal operation.
            _ctx.MaybeRunGC(QuickJSContext.HandleCountThreshold);
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
    }

    void UnregisterEventDelegation() {
        _root.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        _root.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        _root.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
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
    }

    // MARK: Event Handlers

    void OnClick(ClickEvent e) {
        if (_eventDispatchHandle >= 0) {
            int handle = FindElementHandle(e.target);
            DispatchEventFast(EVT_CLICK, handle, e.position.x, e.position.y, e.button, 0);
        } else {
            DispatchPointerEvent("click", e.target, e.position, e.button);
        }
    }

    void OnPointerDown(PointerDownEvent e) {
        if (ReferenceEquals(e, _lastDispatchedPointerDown)) return;
        _lastDispatchedPointerDown = e;
        if (_eventDispatchHandle >= 0) {
            int handle = FindElementHandle(e.target);
            DispatchEventFast(EVT_POINTER_DOWN, handle, e.position.x, e.position.y, e.button, e.pointerId);
        } else {
            DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
        }
    }

    void OnPointerUp(PointerUpEvent e) {
        if (ReferenceEquals(e, _lastDispatchedPointerUp)) return;
        _lastDispatchedPointerUp = e;
        if (_eventDispatchHandle >= 0) {
            int handle = FindElementHandle(e.target);
            DispatchEventFast(EVT_POINTER_UP, handle, e.position.x, e.position.y, e.button, e.pointerId);
        } else {
            DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
        }
    }

    void OnPointerMove(PointerMoveEvent e) {
        if (!InputBridge.PointerMoveEventsEnabled) return;
        if (ReferenceEquals(e, _lastDispatchedPointerMove)) return;
        _lastDispatchedPointerMove = e;
        if (_eventDispatchHandle >= 0) {
            int handle = FindElementHandle(e.target);
            DispatchEventFast(EVT_POINTER_MOVE, handle, e.position.x, e.position.y, e.button, e.pointerId);
        } else {
            DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);
        }
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

    // MARK: Event Dispatch - Core
    int FindElementHandle(IEventHandler target) {
        var el = target as VisualElement;
        while (el != null) {
            int handle = QuickJSNative.GetHandleForObject(el);
            if (handle > 0) return handle;
            el = el.parent;
        }
        return 0;
    }

    /// <summary>
    /// Core dispatch method - all event dispatching goes through here.
    /// </summary>
    void DispatchEventInternal(int handle, string eventType, string dataJson) {
        if (handle == 0 || _inEval) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        QuickJSNative.qjs_dispatch_event(handle, eventType, dataJson);
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
            _ctx.Eval(_sb.ToString());
            _ctx.ExecutePendingJobs();
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}\nEval: {_sb}");
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

    void DispatchEventFast(int eventTypeId, int elemHandle, float x, float y, int button, int pointerId) {
        if (elemHandle == 0 || _inEval) return;
        _inEval = true;
        try {
            _ctx.InvokeCallbackNoAlloc(_eventDispatchHandle, eventTypeId, elemHandle, x, y, button, pointerId);
            _ctx.ExecutePendingJobs();
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error ({eventTypeId}): {ex.Message}");
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
    /// Dispatch an event with pre-built JSON data.
    /// </summary>
    void DispatchEvent(string eventType, IEventHandler target, string dataJson) {
        int handle = FindElementHandle(target);
        DispatchEventInternal(handle, eventType, dataJson);
    }

    /// <summary>
    /// Dispatch a pointer event with position and button data.
    /// </summary>
    void DispatchPointerEvent(string eventType, IEventHandler target, Vector2 position, int button, int pointerId = 0) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        string data = string.Format(CultureInfo.InvariantCulture,
            "{{\"x\":{0:F2},\"y\":{1:F2},\"button\":{2},\"pointerId\":{3}}}",
            position.x, position.y, button, pointerId);

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
            // Stale entry from recycled handle — unregister old before re-registering
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

    void OnPerElementPointerDown(PointerDownEvent e) {
        if (ReferenceEquals(e, _lastDispatchedPointerDown)) return;
        _lastDispatchedPointerDown = e;
        DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);
    }

    void OnPerElementPointerUp(PointerUpEvent e) {
        if (ReferenceEquals(e, _lastDispatchedPointerUp)) return;
        _lastDispatchedPointerUp = e;
        DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);
    }

    void OnPerElementPointerMove(PointerMoveEvent e) {
        if (!InputBridge.PointerMoveEventsEnabled) return;
        if (ReferenceEquals(e, _lastDispatchedPointerMove)) return;
        _lastDispatchedPointerMove = e;
        DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);
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
