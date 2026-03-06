using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Static helper for per-element pointer event handler registration.
    ///
    /// Unity 6's UI Toolkit dispatches captured pointer events directly to the
    /// capturing element, bypassing TrickleDown/BubbleUp propagation entirely.
    /// Since QuickJSUIBridge uses TrickleDown delegation on _root to catch events,
    /// captured pointer events are never seen by the bridge.
    ///
    /// This class enables the JS bootstrap to register per-element C# handlers
    /// for pointer events. These fire even during capture, ensuring onPointerMove
    /// (and other pointer events) work correctly with PointerCaptureHelper.
    ///
    /// Called from JS via: CS.OneJS.PointerCaptureSupport.RegisterHandler(element, eventType, contextId)
    /// </summary>
    public static class PointerCaptureSupport {
        static readonly Dictionary<int, QuickJSUIBridge> _bridges = new();

        public static void RegisterBridge(int contextId, QuickJSUIBridge bridge) {
            _bridges[contextId] = bridge;
        }

        public static void UnregisterBridge(int contextId) {
            _bridges.Remove(contextId);
        }

        public static void RegisterHandler(VisualElement element, string eventType, int contextId) {
            if (!_bridges.TryGetValue(contextId, out var bridge)) return;
            bridge.RegisterPerElementHandler(element, eventType);
        }

        public static void UnregisterHandler(VisualElement element, string eventType, int contextId) {
            if (!_bridges.TryGetValue(contextId, out var bridge)) return;
            bridge.UnregisterPerElementHandler(element, eventType);
        }
    }
}
