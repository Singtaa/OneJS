using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /// <summary>
    /// Draws a <see cref="PanelSettings"/>' runtime UI Toolkit panel into an
    /// offscreen <see cref="RenderTexture"/> at an exact size, independent of the
    /// Game view's dimensions and free of any editor chrome. Works in edit mode
    /// and play mode.
    ///
    /// While alive it also redirects the panel's own clock (the one driving USS
    /// transitions and the panel scheduler) to <see cref="VirtualClock"/>, so a
    /// frame-stepped renderer gets transitions in lockstep with JS animation
    /// rather than running on wall time. Both the render target and the clock are
    /// restored on <see cref="Dispose"/>.
    ///
    /// This leans on UIElements internals, which is the only way to render a panel
    /// on demand. Everything is resolved once up front and a miss throws a
    /// descriptive <see cref="NotSupportedException"/> rather than silently
    /// producing blank frames, so a Unity upgrade that renames something fails
    /// loudly and points at this file.
    /// </summary>
    public sealed class OffscreenPanelRenderer : IDisposable {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance |
                                   BindingFlags.Public | BindingFlags.NonPublic;

        static MethodInfo s_UpdatePanels;
        static MethodInfo s_RenderPanel;
        static MethodInfo s_RenderOffscreenPanels;
        static PropertyInfo s_PanelSettingsPanel;
        static Type s_TimeFunctionType;
        static bool s_Resolved;

        readonly PanelSettings _panelSettings;
        readonly RenderTexture _rt;
        readonly RenderTexture _previousTarget;
        readonly object _runtimePanel;
        readonly PropertyInfo _timeFuncProp;
        readonly object _previousTimeFunc;
        bool _disposed;

        /// <summary>The texture written by <see cref="Render"/>.</summary>
        public RenderTexture Texture => _rt;

        public OffscreenPanelRenderer(PanelSettings panelSettings, int width, int height) {
            _panelSettings = panelSettings
                ? panelSettings
                : throw new ArgumentNullException(nameof(panelSettings));
            if (width < 2 || height < 2)
                throw new ArgumentOutOfRangeException(nameof(width), "Size must be at least 2x2.");

            Resolve();

            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) {
                name = "OneJS_OffscreenPanel_RT"
            };
            _rt.Create();

            _previousTarget = _panelSettings.targetTexture;
            _panelSettings.targetTexture = _rt;

            // One update so the panel notices the targetTexture swap and re-registers
            // itself as offscreen before anything tries to render it.
            s_UpdatePanels.Invoke(null, null);

            _runtimePanel = s_PanelSettingsPanel?.GetValue(_panelSettings);
            if (_runtimePanel == null) {
                _panelSettings.targetTexture = _previousTarget;
                DestroyTexture();
                throw new NotSupportedException(
                    "PanelSettings has no runtime panel yet. Make sure the JSRunner is running " +
                    "(edit-mode preview or play mode) before recording.");
            }

            // Panel clock -> VirtualClock, so USS transitions advance with our steps.
            _timeFuncProp = _runtimePanel.GetType().GetProperty("TimeSinceStartupFunc", Flags);
            if (_timeFuncProp != null && _timeFuncProp.CanWrite && s_TimeFunctionType != null) {
                _previousTimeFunc = _timeFuncProp.GetValue(_runtimePanel);
                var getter = typeof(VirtualClock).GetMethod(
                    nameof(VirtualClock.GetRealtimeSeconds),
                    BindingFlags.Static | BindingFlags.Public);
                _timeFuncProp.SetValue(_runtimePanel,
                    Delegate.CreateDelegate(s_TimeFunctionType, getter));
            }
        }

        /// <summary>
        /// Draws the panel's current state into <see cref="Texture"/>. Call after
        /// stepping the clock and ticking the bridge.
        /// </summary>
        public void Render() {
            if (_disposed) throw new ObjectDisposedException(nameof(OffscreenPanelRenderer));

            // Offline rendering, so always repaint rather than trusting dirty flags.
            (_runtimePanel as IPanel)?.visualTree?.MarkDirtyRepaint();

            s_UpdatePanels.Invoke(null, null);
            EnsureRenderTree();

            // RenderPanel bypasses the offscreen-list filtering that can skip a
            // freshly reassigned panel on its first frame; fall back if it moved.
            if (s_RenderPanel != null) {
                s_RenderPanel.Invoke(null, new[] { _runtimePanel, (object)true });
            } else {
                s_RenderOffscreenPanels.Invoke(null, null);
            }

            GL.Flush();
        }

        /// <summary>
        /// In edit mode nothing runs the panel's repaint-phase updater, so its
        /// render tree may not exist and Render() would silently draw nothing.
        /// The updater's Update() is idempotent and is exactly what the play-mode
        /// frame loop calls every frame.
        /// </summary>
        void EnsureRenderTree() {
            object renderer = null;
            for (var t = _runtimePanel.GetType(); t != null; t = t.BaseType) {
                var f = t.GetField("panelRenderer", Flags | BindingFlags.DeclaredOnly);
                if (f != null) {
                    renderer = f.GetValue(_runtimePanel);
                    break;
                }
            }
            renderer?.GetType()
                .GetMethod("Update", Flags, null, Type.EmptyTypes, null)
                ?.Invoke(renderer, null);
        }

        static void Resolve() {
            if (s_Resolved) return;

            var asm = typeof(PanelSettings).Assembly;
            var util = asm.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            if (util == null)
                throw new NotSupportedException(
                    "UIElementsRuntimeUtility not found in this Unity version. " +
                    "OneJS offscreen panel rendering needs updating.");

            s_UpdatePanels = util.GetMethod("UpdatePanels", Flags, null, Type.EmptyTypes, null);
            s_RenderOffscreenPanels = util.GetMethod("RenderOffscreenPanels", Flags, null, Type.EmptyTypes, null);

            var runtimePanelType = asm.GetType("UnityEngine.UIElements.BaseRuntimePanel");
            if (runtimePanelType != null)
                s_RenderPanel = util.GetMethod("RenderPanel", Flags, null,
                    new[] { runtimePanelType, typeof(bool) }, null);

            s_PanelSettingsPanel = typeof(PanelSettings).GetProperty("panel", Flags);
            s_TimeFunctionType = asm.GetType("UnityEngine.UIElements.TimeFunction");

            if (s_UpdatePanels == null || (s_RenderPanel == null && s_RenderOffscreenPanels == null))
                throw new NotSupportedException(
                    "UIElementsRuntimeUtility.UpdatePanels/RenderPanel not available in this Unity " +
                    "version. OneJS offscreen panel rendering needs updating.");

            s_Resolved = true;
        }

        void DestroyTexture() {
            if (_rt == null) return;
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            if (_timeFuncProp != null && _runtimePanel != null)
                _timeFuncProp.SetValue(_runtimePanel, _previousTimeFunc);
            if (_panelSettings) _panelSettings.targetTexture = _previousTarget;
            DestroyTexture();
        }
    }
}
