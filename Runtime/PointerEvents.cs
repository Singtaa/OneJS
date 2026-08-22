namespace OneJS.Input {
    /// <summary>
    /// Whether pointermove events are dispatched to JavaScript.
    ///
    /// Lives here rather than on InputBridge because QuickJSUIBridge reads it on
    /// every pointer move, and InputBridge only exists when the Input System
    /// package is installed. The flag has nothing to do with that package: it is
    /// a switch for how much this bridge talks to JS, and a project polling
    /// input instead of listening for React events wants it off either way.
    /// </summary>
    public static class PointerEvents {
        /// <summary>
        /// When false, QuickJSUIBridge will not dispatch pointermove to JS.
        /// Eliminates about 0.6KB/frame of GC allocation when reading the mouse
        /// by polling instead. onPointerEnter and onPointerLeave still fire.
        /// </summary>
        public static bool MoveEventsEnabled { get; private set; } = true;

        /// <summary>Set from JS via input.setPointerMoveEventsEnabled(false).</summary>
        public static void SetMoveEventsEnabled(bool enabled) {
            MoveEventsEnabled = enabled;
        }
    }
}
