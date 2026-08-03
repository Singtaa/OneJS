using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Deterministic stand-in for <see cref="Time.realtimeSinceStartupAsDouble"/>.
    ///
    /// While inactive (the default) every reader gets engine realtime, so play
    /// mode and edit-mode preview behave exactly as before. While active, time
    /// only moves when <see cref="Advance"/> is called, which lets an offline
    /// renderer step the UI by an exact frame interval no matter how long the
    /// frame actually took to draw.
    ///
    /// Every time-dependent OneJS subsystem reads <see cref="RealtimeSeconds"/>
    /// instead of Unity's clock directly:
    /// <list type="bullet">
    /// <item><see cref="QuickJSUIBridge.Tick"/> derives the timestamp it hands to
    /// <c>__tick</c>, and therefore every requestAnimationFrame callback, JS timer
    /// and React scheduler wake-up.</item>
    /// <item><see cref="ParticleBridge.TickAll"/> derives its dt.</item>
    /// </list>
    /// UI Toolkit's own panel clock (which drives USS transitions and the panel
    /// scheduler) is separate and lives outside OneJS; the editor-side recorder
    /// redirects it per panel so transitions stay in lockstep with this clock.
    ///
    /// Not thread-safe, and not meant to be: every caller is on the main thread.
    /// </summary>
    public static class VirtualClock {
        static double s_TimeSeconds;

        /// <summary>True while time is driven manually by <see cref="Advance"/>.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Virtual time while active, engine realtime otherwise. Drop-in
        /// replacement for <see cref="Time.realtimeSinceStartupAsDouble"/>.
        /// </summary>
        public static double RealtimeSeconds => GetRealtimeSeconds();

        /// <summary>
        /// Method form of <see cref="RealtimeSeconds"/>. Exists so callers can bind
        /// it as a delegate (UI Toolkit's panel clock is a <c>Func&lt;double&gt;</c>
        /// shaped delegate); prefer the property in normal code.
        /// </summary>
        public static double GetRealtimeSeconds() {
            return IsActive ? s_TimeSeconds : Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>
        /// Takes over the clock, seeding from engine realtime so readers observe no
        /// jump at the hand-off. Idempotent rather than nesting: a second Begin()
        /// while already active does nothing, and one <see cref="End"/> releases.
        ///
        /// Callers must pair this with <see cref="End"/> in a finally block. A
        /// leaked active clock freezes every animation in the editor until the next
        /// domain reload, which is confusing to diagnose from the symptom.
        /// </summary>
        public static void Begin() {
            if (IsActive) return;
            s_TimeSeconds = Time.realtimeSinceStartupAsDouble;
            IsActive = true;
        }

        /// <summary>
        /// Moves virtual time forward. No-op when the clock is not active, and for
        /// non-positive deltas (time must be monotonic for the readers above).
        /// </summary>
        public static void Advance(double deltaSeconds) {
            if (!IsActive || deltaSeconds <= 0.0) return;
            s_TimeSeconds += deltaSeconds;
        }

        /// <summary>Returns readers to engine realtime. Safe to call when inactive.</summary>
        public static void End() {
            IsActive = false;
        }
    }
}
