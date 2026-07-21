using System;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Wire schema (v1) for the 2D particle engine. This is the C#-JS contract:
    /// onejs-react's particles.ts normalizes its ergonomic config (number-or-range
    /// values, hex colors) into this flat, JsonUtility-compatible document. Keep
    /// the two sides in sync - parity fixtures live in particles.test.ts (JS) and
    /// ParticleTests.cs (C#).
    /// </summary>
    [Serializable]
    public class ParticleWireDoc {
        public int v;
        public int max;
        public int space; // 0 = local, 1 = panel
        public int seed;  // 0 = derive
        public WireEmitter[] emitters;
    }

    [Serializable]
    public class WireEmitter {
        public float rate;
        public bool emitting = true;
        public float x;
        public float y;
        public int shape; // 0 = point, 1 = circle (shapeW = radius), 2 = rect (shapeW/H), 3 = line (shapeW = length)
        public float shapeW;
        public float shapeH;
        public float angleMin;       // degrees; 0 = +X, 90 = +Y (down, panel coords)
        public float angleMax = 360f;
        public float speedMin;
        public float speedMax;
        public float lifeMin = 1f;
        public float lifeMax = 1f;
        public float sizeMin = 8f;
        public float sizeMax = 8f;
        public float gravityX;
        public float gravityY;
        public float drag;
        public float rotMin;
        public float rotMax;
        public float angVelMin;
        public float angVelMax;
        public float additiveness;   // 0 = normal alpha, 1 = pure additive
        public WireColorKey[] colorKeys;
        public WireFloatKey[] sizeKeys;
    }

    [Serializable]
    public class WireColorKey {
        public float t;
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;
    }

    [Serializable]
    public class WireFloatKey {
        public float t;
        public float v = 1f;
    }

    public static class ParticleWire {
        public const int Version = 1;
        public const int MaxParticlesLimit = 100000;
        public const int MaxEmitters = 32;
        public const int MaxCurveKeys = 8;

        /// <summary>
        /// Parses and validates a wire document. Throws ArgumentException with a
        /// descriptive message on any schema violation so JS gets a usable error.
        /// </summary>
        public static ParticleWireDoc Parse(string json) {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("[OneJS Particles] config JSON is empty.");

            ParticleWireDoc doc;
            try {
                doc = JsonUtility.FromJson<ParticleWireDoc>(json);
            } catch (Exception e) {
                throw new ArgumentException($"[OneJS Particles] config JSON failed to parse: {e.Message}");
            }
            if (doc == null)
                throw new ArgumentException("[OneJS Particles] config JSON parsed to null.");
            if (doc.v != Version)
                throw new ArgumentException($"[OneJS Particles] unsupported wire version {doc.v} (expected {Version}).");
            if (doc.max < 1 || doc.max > MaxParticlesLimit)
                throw new ArgumentException($"[OneJS Particles] max must be 1..{MaxParticlesLimit}, got {doc.max}.");
            if (doc.space != 0 && doc.space != 1)
                throw new ArgumentException($"[OneJS Particles] space must be 0 (local) or 1 (panel), got {doc.space}.");
            if (doc.emitters == null || doc.emitters.Length == 0)
                throw new ArgumentException("[OneJS Particles] at least one emitter is required.");
            if (doc.emitters.Length > MaxEmitters)
                throw new ArgumentException($"[OneJS Particles] at most {MaxEmitters} emitters, got {doc.emitters.Length}.");

            for (int i = 0; i < doc.emitters.Length; i++) {
                var e = doc.emitters[i];
                if (e == null)
                    throw new ArgumentException($"[OneJS Particles] emitter {i} is null.");
                if (e.shape < 0 || e.shape > 3)
                    throw new ArgumentException($"[OneJS Particles] emitter {i}: shape must be 0..3, got {e.shape}.");
                e.rate = Mathf.Clamp(e.rate, 0f, 100000f);
                e.additiveness = Mathf.Clamp01(e.additiveness);
                e.lifeMin = Mathf.Max(0.001f, e.lifeMin);
                e.lifeMax = Mathf.Max(e.lifeMin, e.lifeMax);
                e.colorKeys = NormalizeKeys(e.colorKeys, i, "colorKeys", () => new WireColorKey { t = 0f }, k => k.t);
                e.sizeKeys = NormalizeKeys(e.sizeKeys, i, "sizeKeys", () => new WireFloatKey { t = 0f, v = 1f }, k => k.t);
            }
            return doc;
        }

        static T[] NormalizeKeys<T>(T[] keys, int emitterIndex, string name, Func<T> makeDefault, Func<T, float> getT) {
            if (keys == null || keys.Length == 0)
                return new[] { makeDefault() };
            if (keys.Length > MaxCurveKeys)
                throw new ArgumentException($"[OneJS Particles] emitter {emitterIndex}: {name} allows at most {MaxCurveKeys} keys, got {keys.Length}.");
            // Keys must be sorted by t for the linear evaluators.
            Array.Sort(keys, (a, b) => getT(a).CompareTo(getT(b)));
            return keys;
        }
    }
}
