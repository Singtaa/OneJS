using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Static helper for per-element C# event handler registration.
    ///
    /// QuickJSUIBridge registers most UI Toolkit callbacks on <c>_root</c> with
    /// TrickleDown and delegates events from there — fine for events that pass
    /// through the root during propagation, but not for:
    ///   • captured pointer events (Unity 6 delivers these directly to the
    ///     capturing element, bypassing TrickleDown), and
    ///   • non-bubbling events like <see cref="GeometryChangedEvent"/>, which
    ///     only fire on their own target.
    ///
    /// This class lets the JS bootstrap register per-element C# handlers so
    /// those events still reach JS. The eventType → UI Toolkit Event mapping
    /// lives in <c>QuickJSUIBridge.RegisterPerElementHandler</c>.
    ///
    /// Called from JS via: CS.OneJS.PerElementEventSupport.RegisterHandler(element, eventType, contextId)
    /// </summary>
    public static class PerElementEventSupport {
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
