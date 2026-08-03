using System;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Wire schema (v4) for the 2D particle engine. This is the C#-JS contract:
    /// onejs-react's particles.ts normalizes its ergonomic config (number-or-range
    /// values, hex colors) into this flat, JsonUtility-compatible document. Keep
    /// the two sides in sync - parity fixtures live in particles.test.ts (JS) and
    /// ParticleTests.cs (C#).
    ///
    /// v2 added per-particle aspect, random tint palettes, target attraction and
    /// edge behavior. v3 added flipbook animation, v4 a sprite pivot. Every added
    /// field defaults to its previous behavior, so older documents still parse
    /// (a newer OneJS package keeps working with older onejs-react). The reverse
    /// - a newer document reaching an older parser - is rejected by the version
    /// check rather than silently dropping the new fields.
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
        public float aspectMin = 1f;  // quad width:height ratio; 1 = square
        public float aspectMax = 1f;
        public float gravityX;
        public float gravityY;
        public float drag;
        public float rotMin;
        public float rotMax;
        public float angVelMin;
        public float angVelMax;
        public float additiveness;   // 0 = normal alpha, 1 = pure additive
        public float attractX;       // target point, emitter-local px
        public float attractY;
        public float attractStrength; // 0 = disabled, 1 = exact arrival at end of life
        public int attractEase;      // 0 = linear, 1 = in (default), 2 = out
        public int edge;             // 0 = none, 1 = kill, 2 = bounce, 3 = stick
        public float bounciness = 0.5f;
        // Which point of the sprite sits on the particle position, in normalized quad
        // coords: 0,0 = center (default), 0,0.5 = bottom edge (Y is down). Also the
        // point the quad rotates around.
        public float pivotX;
        public float pivotY;
        public int sheetCols = 1;    // flipbook grid; 1x1 = no sheet animation
        public int sheetRows = 1;
        public int sheetMode;        // 0 = play once over life, 1 = fixed fps (loops)
        public float sheetFps = 24f;
        public int sheetFrames;      // 0 = cols*rows; Parse resolves it to a positive count
        public bool sheetRandomStart;
        public WireColorKey[] colorKeys;
        public WireFloatKey[] sizeKeys;
        public WireRGBA[] tintPalette; // null/empty = no per-particle tint
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

    /// <summary>A flat color with no curve time, used for random per-particle tints.</summary>
    [Serializable]
    public class WireRGBA {
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;
    }

    public static class ParticleWire {
        public const int Version = 4;
        public const int MinVersion = 1;
        public const int MaxParticlesLimit = 100000;
        public const int MaxEmitters = 32;
        public const int MaxCurveKeys = 8;
        public const int MaxPaletteColors = 16;
        public const int MaxSheetDim = 64;
        // Random start frames are stored as one byte per particle.
        public const int MaxSheetFrames = 256;

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
            if (doc.v < MinVersion || doc.v > Version)
                throw new ArgumentException(
                    $"[OneJS Particles] unsupported wire version {doc.v} (this package supports {MinVersion}..{Version}). " +
                    "Update the OneJS package to match your onejs-react version.");
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
                if (e.edge < 0 || e.edge > 3)
                    throw new ArgumentException($"[OneJS Particles] emitter {i}: edge must be 0..3, got {e.edge}.");
                if (e.attractEase < 0 || e.attractEase > 2)
                    throw new ArgumentException($"[OneJS Particles] emitter {i}: attractEase must be 0..2, got {e.attractEase}.");
                if (e.tintPalette != null && e.tintPalette.Length > MaxPaletteColors)
                    throw new ArgumentException(
                        $"[OneJS Particles] emitter {i}: tintPalette allows at most {MaxPaletteColors} colors, got {e.tintPalette.Length}.");
                if (e.sheetCols < 1 || e.sheetCols > MaxSheetDim || e.sheetRows < 1 || e.sheetRows > MaxSheetDim)
                    throw new ArgumentException(
                        $"[OneJS Particles] emitter {i}: sheet cols/rows must be 1..{MaxSheetDim}, got {e.sheetCols}x{e.sheetRows}.");
                if (e.sheetMode < 0 || e.sheetMode > 1)
                    throw new ArgumentException($"[OneJS Particles] emitter {i}: sheetMode must be 0..1, got {e.sheetMode}.");
                // Resolve the frame count once so the renderer never has to.
                int sheetTotal = e.sheetCols * e.sheetRows;
                if (e.sheetFrames <= 0) e.sheetFrames = sheetTotal;
                if (e.sheetFrames > sheetTotal)
                    throw new ArgumentException(
                        $"[OneJS Particles] emitter {i}: sheetFrames {e.sheetFrames} exceeds the {e.sheetCols}x{e.sheetRows} grid ({sheetTotal} cells).");
                if (e.sheetFrames > MaxSheetFrames)
                    throw new ArgumentException(
                        $"[OneJS Particles] emitter {i}: at most {MaxSheetFrames} sheet frames, got {e.sheetFrames}.");
                e.sheetFps = Mathf.Max(0.001f, e.sheetFps);
                e.rate = Mathf.Clamp(e.rate, 0f, 100000f);
                e.additiveness = Mathf.Clamp01(e.additiveness);
                e.attractStrength = Mathf.Clamp01(e.attractStrength);
                e.bounciness = Mathf.Clamp01(e.bounciness);
                e.lifeMin = Mathf.Max(0.001f, e.lifeMin);
                e.lifeMax = Mathf.Max(e.lifeMin, e.lifeMax);
                e.aspectMin = Mathf.Max(0.001f, e.aspectMin);
                e.aspectMax = Mathf.Max(e.aspectMin, e.aspectMax);
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
