using System;
using System.Globalization;
using System.IO;
using System.Text;
using OneJS.CustomStyleSheets;
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
    bool _disposed;
    float _startTime;
    bool _inEval; // Recursion guard to prevent re-entrant JS execution (all platforms)

    // Viewport tracking for responsive design
    float _lastViewportWidth;
    float _lastViewportHeight;

    public QuickJSContext Context => _ctx;
    public VisualElement Root => _root;
    public string WorkingDir => _workingDir;

    // MARK: Lifecycle
    public QuickJSUIBridge(VisualElement root, string workingDir = null, int bufferSize = 16 * 1024) {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _workingDir = workingDir ?? "";
        _ctx = new QuickJSContext(bufferSize);
        _ussCompiler = new UssCompiler(_workingDir);
        _startTime = Time.realtimeSinceStartup;

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
    /// </summary>
    /// <param name="ussContent">USS content</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>True if successful</returns>
    public bool CompileStyleSheet(string ussContent, string name = "inline") {
        try {
            var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
            styleSheet.name = name;
            _ussCompiler.Compile(styleSheet, ussContent);
            _root.styleSheets.Add(styleSheet);
            return true;
        } catch (Exception ex) {
            Debug.LogError($"[QuickJSUIBridge] CompileStyleSheet error ({name}): {ex.Message}");
            return false;
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        UnregisterEventDelegation();
        QuickJSNative.ClearPendingTasks();
        _ctx?.Dispose();

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
    /// </summary>
    public void Tick() {
        if (_disposed || _inEval) return;
        _inEval = true;
        try {
            // Process completed C# Tasks and resolve/reject their JS Promises
            QuickJSNative.ProcessCompletedTasks(_ctx);

            float timestamp = (Time.realtimeSinceStartup - _startTime) * 1000f;
            _ctx.Eval($"globalThis.__tick && __tick({timestamp.ToString("F2", CultureInfo.InvariantCulture)})");

            // Execute pending Promise jobs (microtasks) - critical for React scheduler
            _ctx.ExecutePendingJobs();
        } catch (System.Exception ex) {
            UnityEngine.Debug.LogError($"[QuickJSUIBridge] Tick error: {ex.Message}");
        } finally {
            _inEval = false;
        }
    }

    // MARK: Events
    void RegisterEventDelegation() {
        // Use TrickleDown to catch events during capture phase
        // This ensures we see events even if a child stops propagation during bubble
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
        _root.RegisterCallback<ChangeEvent<string>>(OnChangeString, TrickleDown.TrickleDown);
        _root.RegisterCallback<ChangeEvent<bool>>(OnChangeBool, TrickleDown.TrickleDown);
        _root.RegisterCallback<ChangeEvent<float>>(OnChangeFloat, TrickleDown.TrickleDown);
        _root.RegisterCallback<ChangeEvent<int>>(OnChangeInt, TrickleDown.TrickleDown);

        // Geometry changed for responsive design (only on root)
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
        _root.UnregisterCallback<ChangeEvent<string>>(OnChangeString, TrickleDown.TrickleDown);
        _root.UnregisterCallback<ChangeEvent<bool>>(OnChangeBool, TrickleDown.TrickleDown);
        _root.UnregisterCallback<ChangeEvent<float>>(OnChangeFloat, TrickleDown.TrickleDown);
        _root.UnregisterCallback<ChangeEvent<int>>(OnChangeInt, TrickleDown.TrickleDown);
        _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    // MARK: Handlers
    void OnClick(ClickEvent e) => DispatchPointerEvent("click", e.target, e.position, e.button);

    void OnPointerDown(PointerDownEvent e) =>
        DispatchPointerEvent("pointerdown", e.target, e.position, e.button, e.pointerId);

    void OnPointerUp(PointerUpEvent e) =>
        DispatchPointerEvent("pointerup", e.target, e.position, e.button, e.pointerId);

    void OnPointerMove(PointerMoveEvent e) =>
        DispatchPointerEvent("pointermove", e.target, e.position, e.button, e.pointerId);

    void OnPointerEnter(PointerEnterEvent e) =>
        DispatchPointerEvent("pointerenter", e.target, e.position, 0, e.pointerId);

    void OnPointerLeave(PointerLeaveEvent e) =>
        DispatchPointerEvent("pointerleave", e.target, e.position, 0, e.pointerId);

    void OnFocusIn(FocusInEvent e) => DispatchSimpleEvent("focus", e.target);
    void OnFocusOut(FocusOutEvent e) => DispatchSimpleEvent("blur", e.target);

    void OnKeyDown(KeyDownEvent e) =>
        DispatchKeyEvent("keydown", e.target, e.keyCode, e.character, e.modifiers);

    void OnKeyUp(KeyUpEvent e) => DispatchKeyEvent("keyup", e.target, e.keyCode, '\0', e.modifiers);

    void OnChangeString(ChangeEvent<string> e) =>
        DispatchChangeEvent("change", e.target, $"\"{EscapeString(e.newValue)}\"");

    void OnChangeBool(ChangeEvent<bool> e) =>
        DispatchChangeEvent("change", e.target, e.newValue ? "true" : "false");

    void OnChangeFloat(ChangeEvent<float> e) => DispatchChangeEvent("change", e.target,
        e.newValue.ToString("G", CultureInfo.InvariantCulture));

    void OnChangeInt(ChangeEvent<int> e) => DispatchChangeEvent("change", e.target, e.newValue.ToString());

    void OnGeometryChanged(GeometryChangedEvent e) {
        // Only dispatch if size actually changed (avoid spurious events)
        float newWidth = e.newRect.width;
        float newHeight = e.newRect.height;

        if (Mathf.Approximately(newWidth, _lastViewportWidth) &&
            Mathf.Approximately(newHeight, _lastViewportHeight)) {
            return;
        }

        _lastViewportWidth = newWidth;
        _lastViewportHeight = newHeight;

        DispatchViewportEvent(newWidth, newHeight);
    }

    // MARK: Dispatch
    int FindElementHandle(IEventHandler target) {
        var el = target as VisualElement;
        while (el != null) {
            int handle = QuickJSNative.GetHandleForObject(el);
            if (handle > 0) return handle;
            el = el.parent;
        }
        return 0;
    }

    void DispatchSimpleEvent(string eventType, IEventHandler target) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        // Recursion guard - prevent event dispatch during eval/tick
        if (_inEval) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast path for WebGL - direct function call instead of eval
        QuickJSNative.qjs_dispatch_event(handle, eventType, "{}");
#else
        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{})");

        try {
            SafeEval(_sb.ToString());
            _ctx.ExecutePendingJobs(); // Process microtasks scheduled by React
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
#endif
    }

    void DispatchPointerEvent(string eventType, IEventHandler target, Vector2 position, int button,
        int pointerId = 0) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        // Recursion guard - prevent event dispatch during eval/tick
        if (_inEval) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast path for WebGL - direct function call instead of eval
        _sb.Clear();
        _sb.Append("{\"x\":");
        _sb.Append(position.x.ToString("F2", CultureInfo.InvariantCulture));
        _sb.Append(",\"y\":");
        _sb.Append(position.y.ToString("F2", CultureInfo.InvariantCulture));
        _sb.Append(",\"button\":");
        _sb.Append(button);
        _sb.Append(",\"pointerId\":");
        _sb.Append(pointerId);
        _sb.Append("}");
        QuickJSNative.qjs_dispatch_event(handle, eventType, _sb.ToString());
#else
        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{\"x\":");
        _sb.Append(position.x.ToString("F2", CultureInfo.InvariantCulture));
        _sb.Append(",\"y\":");
        _sb.Append(position.y.ToString("F2", CultureInfo.InvariantCulture));
        _sb.Append(",\"button\":");
        _sb.Append(button);
        _sb.Append(",\"pointerId\":");
        _sb.Append(pointerId);
        _sb.Append("})");

        try {
            SafeEval(_sb.ToString());
            _ctx.ExecutePendingJobs(); // Process microtasks scheduled by React
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
#endif
    }

    void DispatchKeyEvent(string eventType, IEventHandler target, KeyCode keyCode, char character,
        EventModifiers modifiers) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        // Recursion guard - prevent event dispatch during eval/tick
        if (_inEval) return;

        _sb.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast path for WebGL
        _sb.Append("{\"keyCode\":");
        _sb.Append((int)keyCode);
        _sb.Append(",\"key\":\"");
        _sb.Append(keyCode.ToString());
        _sb.Append("\",\"char\":\"");
        if (character != '\0') _sb.Append(EscapeChar(character));
        _sb.Append("\",\"shift\":");
        _sb.Append((modifiers & EventModifiers.Shift) != 0 ? "true" : "false");
        _sb.Append(",\"ctrl\":");
        _sb.Append((modifiers & EventModifiers.Control) != 0 ? "true" : "false");
        _sb.Append(",\"alt\":");
        _sb.Append((modifiers & EventModifiers.Alt) != 0 ? "true" : "false");
        _sb.Append(",\"meta\":");
        _sb.Append((modifiers & EventModifiers.Command) != 0 ? "true" : "false");
        _sb.Append("}");
        QuickJSNative.qjs_dispatch_event(handle, eventType, _sb.ToString());
#else
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{\"keyCode\":");
        _sb.Append((int)keyCode);
        _sb.Append(",\"key\":\"");
        _sb.Append(keyCode.ToString());
        _sb.Append("\",\"char\":\"");
        if (character != '\0') _sb.Append(EscapeChar(character));
        _sb.Append("\",\"shift\":");
        _sb.Append((modifiers & EventModifiers.Shift) != 0 ? "true" : "false");
        _sb.Append(",\"ctrl\":");
        _sb.Append((modifiers & EventModifiers.Control) != 0 ? "true" : "false");
        _sb.Append(",\"alt\":");
        _sb.Append((modifiers & EventModifiers.Alt) != 0 ? "true" : "false");
        _sb.Append(",\"meta\":");
        _sb.Append((modifiers & EventModifiers.Command) != 0 ? "true" : "false");
        _sb.Append("})");

        try {
            SafeEval(_sb.ToString());
            _ctx.ExecutePendingJobs(); // Process microtasks scheduled by React
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
#endif
    }

    void DispatchChangeEvent(string eventType, IEventHandler target, string valueJson) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        // Recursion guard - prevent event dispatch during eval/tick
        if (_inEval) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast path for WebGL
        _sb.Clear();
        _sb.Append("{\"value\":");
        _sb.Append(valueJson);
        _sb.Append("}");
        QuickJSNative.qjs_dispatch_event(handle, eventType, _sb.ToString());
#else
        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{\"value\":");
        _sb.Append(valueJson);
        _sb.Append("})");

        try {
            SafeEval(_sb.ToString());
            _ctx.ExecutePendingJobs(); // Process microtasks scheduled by React
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
#endif
    }

    void DispatchViewportEvent(float width, float height) {
        // Recursion guard - prevent event dispatch during eval/tick
        if (_inEval) return;

        int handle = QuickJSNative.GetHandleForObject(_root);

#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast path for WebGL
        _sb.Clear();
        _sb.Append("{\"width\":");
        _sb.Append(width.ToString("F0", CultureInfo.InvariantCulture));
        _sb.Append(",\"height\":");
        _sb.Append(height.ToString("F0", CultureInfo.InvariantCulture));
        _sb.Append("}");
        QuickJSNative.qjs_dispatch_event(handle, "viewportchange", _sb.ToString());
#else
        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"viewportchange\",{\"width\":");
        _sb.Append(width.ToString("F0", CultureInfo.InvariantCulture));
        _sb.Append(",\"height\":");
        _sb.Append(height.ToString("F0", CultureInfo.InvariantCulture));
        _sb.Append("})");

        try {
            SafeEval(_sb.ToString());
            _ctx.ExecutePendingJobs(); // Process microtasks scheduled by React
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Viewport event dispatch error: {ex.Message}");
        }
#endif
    }

    // MARK: Utils
    static string EscapeString(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    static string EscapeChar(char c) {
        return c switch {
            '\\' => "\\\\",
            '"' => "\\\"",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString()
        };
    }
}