using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// What JavaScript talks to for 2D physics.
    ///
    /// The same shape as ParticleBridge, and for the same reasons: a world is
    /// created from one JSON document, ticked from QuickJSUIBridge.Tick() so it
    /// runs in play mode, edit-mode preview and JSPad alike, and disposed with
    /// the context.
    ///
    /// Everything here is either setup, one call per change, or one call per
    /// frame carrying everything. Nothing is per body per frame.
    /// </summary>
    public static class Physics2DBridge {
        static readonly List<PhysicsWorld2D> s_Worlds = new List<PhysicsWorld2D>();
        static double s_LastTick;

        /// <summary>
        /// Creates a world bound to a host element. Throws ArgumentException with
        /// a readable message on schema violations, so a bad config fails where
        /// the author can see it rather than simulating nothing.
        /// </summary>
        public static PhysicsWorld2D Create(VisualElement host, string json) {
            if (host == null)
                throw new ArgumentException("[OneJS Physics2D] Create requires a VisualElement.");
            var doc = Physics2DWire.Parse(json);
            var world = new PhysicsWorld2D(host, doc);
            s_Worlds.Add(world);
            return world;
        }

        /// <summary>Live world count, for monitoring and tests.</summary>
        public static int LiveWorldCount {
            get {
                int n = 0;
                for (int i = 0; i < s_Worlds.Count; i++)
                    if (!s_Worlds[i].IsDisposed) n++;
                return n;
            }
        }

        /// <summary>Advances every live world. Safe to call more than once a frame.</summary>
        public static void TickAll() {
            double now = VirtualClock.RealtimeSeconds;
            float dt = (float)(now - s_LastTick);
            s_LastTick = now;
            if (dt <= 0f) return;

            for (int i = s_Worlds.Count - 1; i >= 0; i--) {
                var world = s_Worlds[i];
                if (world.IsDisposed) { s_Worlds.RemoveAt(i); continue; }
                world.Tick(dt);
            }
        }

        public static void DisposeAll() {
            for (int i = 0; i < s_Worlds.Count; i++) s_Worlds[i].Dispose();
            s_Worlds.Clear();
        }
    }
}
