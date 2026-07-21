using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// JS entry point and registry for 2D particle systems.
    ///
    /// Called from JS via:
    ///   CS.OneJS.ParticleBridge.Create(element, wireJson, textureOrNull)
    ///
    /// TickAll is driven from QuickJSUIBridge.Tick(), which covers play mode,
    /// edit-mode preview (30Hz) and JSPad through a single integration point.
    /// dt is derived from realtime deltas and guarded so that multiple live
    /// bridges ticking in the same frame do not double-step the simulations.
    /// </summary>
    public static class ParticleBridge {
        static readonly List<ParticleSystem2D> s_Systems = new List<ParticleSystem2D>();
        static double s_LastTick;

        /// <summary>
        /// Creates a particle system bound to a host element. Throws
        /// ArgumentException with a descriptive message on schema violations.
        /// texture may be null (built-in soft disc).
        /// </summary>
        public static ParticleSystem2D Create(VisualElement ve, string json, Texture2D texture) {
            if (ve == null)
                throw new ArgumentException("[OneJS Particles] Create requires a VisualElement.");
            var doc = ParticleWire.Parse(json);
            var sys = new ParticleSystem2D(ve, doc, texture);
            s_Systems.Add(sys);
            return sys;
        }

        /// <summary>Live (non-disposed) system count, for monitoring/tests.</summary>
        public static int LiveSystemCount {
            get {
                int n = 0;
                for (int i = 0; i < s_Systems.Count; i++)
                    if (!s_Systems[i].IsDisposed)
                        n++;
                return n;
            }
        }

        /// <summary>Advances all live systems. Safe to call from multiple bridges per frame.</summary>
        public static void TickAll() {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = (float)(now - s_LastTick);
            if (dt <= 0.0005f) return; // second bridge ticking the same frame
            s_LastTick = now;
            if (dt > 0.05f) dt = 0.05f; // first tick / editor hitches

            for (int i = s_Systems.Count - 1; i >= 0; i--) {
                var sys = s_Systems[i];
                if (sys.IsDisposed) {
                    s_Systems.RemoveAt(i);
                    continue;
                }
                sys.Tick(dt);
            }
        }

        /// <summary>
        /// Safety net for context teardown (hot reload, stop): disposes any
        /// systems the JS side leaked. Normal disposal happens via JS effect
        /// cleanups during the teardown hooks, before this runs.
        /// </summary>
        public static void DisposeAll() {
            for (int i = 0; i < s_Systems.Count; i++)
                s_Systems[i].Dispose();
            s_Systems.Clear();
        }
    }
}
