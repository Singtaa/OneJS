using System;
using System.Globalization;
using System.Text;
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
    bool _disposed;
    float _startTime;

    public QuickJSContext Context => _ctx;
    public VisualElement Root => _root;

    // MARK: Lifecycle
    public QuickJSUIBridge(VisualElement root, int bufferSize = 16 * 1024) {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _ctx = new QuickJSContext(bufferSize);
        _startTime = Time.realtimeSinceStartup;

        RegisterEventDelegation();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        UnregisterEventDelegation();
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
    /// Call every frame from Update() to drive RAF and timers.
    /// </summary>
    public void Tick() {
        if (_disposed) return;
        float timestamp = (Time.realtimeSinceStartup - _startTime) * 1000f;
        _ctx.Eval($"globalThis.__tick && __tick({timestamp.ToString("F2", CultureInfo.InvariantCulture)})");
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

        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{})");

        try {
            _ctx.Eval(_sb.ToString());
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
    }

    void DispatchPointerEvent(string eventType, IEventHandler target, Vector2 position, int button,
        int pointerId = 0) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

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
            _ctx.Eval(_sb.ToString());
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
    }

    void DispatchKeyEvent(string eventType, IEventHandler target, KeyCode keyCode, char character,
        EventModifiers modifiers) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        _sb.Clear();
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
            _ctx.Eval(_sb.ToString());
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
    }

    void DispatchChangeEvent(string eventType, IEventHandler target, string valueJson) {
        int handle = FindElementHandle(target);
        if (handle == 0) return;

        _sb.Clear();
        _sb.Append("globalThis.__dispatchEvent && __dispatchEvent(");
        _sb.Append(handle);
        _sb.Append(",\"");
        _sb.Append(eventType);
        _sb.Append("\",{\"value\":");
        _sb.Append(valueJson);
        _sb.Append("})");

        try {
            _ctx.Eval(_sb.ToString());
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJSUIBridge] Event dispatch error: {ex.Message}");
        }
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