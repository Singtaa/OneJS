namespace OneJS {
    /// <summary>
    /// How far <see cref="VirtualClock"/> moved since a bridge's previous tick,
    /// for the bridges every <see cref="QuickJSUIBridge.Tick"/> advances: shader
    /// effects, particles and 2D physics. Several bridges tick each frame, so a
    /// tick that finds the clock where the last one left it is that frame again.
    ///
    /// The clock is not one line. Engine realtime differs between play mode and
    /// edit mode, so a play session leaves the last tick seconds ahead of edit
    /// mode's clock, and stays so until a domain reload; the offline recorder
    /// also hands back to realtime after running ahead of it. A tick that finds
    /// the clock behind it starts over from there, with dt 0: it draws without
    /// advancing. Dropping it instead left the first frame after play mode
    /// undrawn.
    /// </summary>
    internal sealed class TickClock {
        /// <summary>Closer than this to the previous tick is the same frame.</summary>
        const double SameFrame = 0.0005;

        double _last;

        /// <summary>
        /// False for a second tick in the same frame. Otherwise true, with the
        /// seconds since the previous tick, or 0 when the clock went backwards.
        /// </summary>
        public bool Next(out float dt) {
            double now = VirtualClock.RealtimeSeconds;
            if (now < _last) {
                _last = now;
                dt = 0f;
                return true;
            }
            dt = (float)(now - _last);
            if (dt <= SameFrame) return false;
            _last = now;
            return true;
        }
    }
}
