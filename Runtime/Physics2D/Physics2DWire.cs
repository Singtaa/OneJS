using System;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Wire schema (v1) for the 2D physics world. This is the C#-JS contract:
    /// onejs-unity's physics2d.ts normalizes its ergonomic config into this flat,
    /// JsonUtility-compatible document. Keep the two sides in sync; the guard
    /// that compares them is Physics2DWireContractTests in the container, which
    /// reads this file and physics2d.ts. (An earlier version of this comment
    /// named a Physics2DTests.cs that never existed.)
    ///
    /// Follows the particle wire in shape and for the same reasons: an entire
    /// world crosses the boundary once, and nothing crosses again while it runs.
    /// Every field added later must default to its previous behaviour so older
    /// documents keep parsing; a newer document reaching an older parser is
    /// rejected by the version check rather than silently losing fields.
    /// </summary>
    [Serializable]
    public class Physics2DWireDoc {
        public int v;

        /// <summary>
        /// Panel units per physics unit.
        ///
        /// PhysX 2D is tuned for metres: its solver tolerances, sleep thresholds
        /// and contact offsets all assume bodies a few units across. A stage is
        /// hundreds of points wide, so simulating in points directly gives jitter
        /// and tunnelling that look like engine bugs. Dividing by this keeps
        /// bodies in the range the solver expects, and it is the same idea as a
        /// sprite's pixels-per-unit.
        /// </summary>
        public float pixelsPerUnit = 100f;

        /// <summary>Panel units per second squared, Y down, as the UI measures it.</summary>
        public float gravityX;
        public float gravityY = 980f;

        /// <summary>Solver iterations. Higher is steadier and slower.</summary>
        public int velocityIterations = 8;
        public int positionIterations = 3;

        /// <summary>
        /// Walls around the host element's rect, so bodies cannot leave the
        /// stage. Cheap and almost always wanted; off by default so a game that
        /// wants things to fall off the screen can have that.
        /// </summary>
        public bool bounds;
        public float boundsRestitution = 0.2f;
        public float boundsFriction = 0.4f;

        public WireBody[] bodies;
    }

    [Serializable]
    public class WireBody {
        /// <summary>0 = dynamic, 1 = kinematic, 2 = static.</summary>
        public int type;

        /// <summary>Panel units, relative to the host element's top-left, Y down.</summary>
        public float x;
        public float y;
        /// <summary>Degrees, clockwise, matching how UI Toolkit rotates.</summary>
        public float rotation;

        /// <summary>0 = box (w, h), 1 = circle (w = radius), 2 = capsule (w, h).</summary>
        public int shape;
        public float w = 32f;
        public float h = 32f;

        public float density = 1f;
        public float friction = 0.4f;
        public float restitution;
        public float linearDamping;
        public float angularDamping = 0.05f;
        public bool fixedRotation;

        /// <summary>Reports contacts to JS. Off by default: most bodies are scenery.</summary>
        public bool reportCollisions;

        /// <summary>Passes through contacts, still reporting them. For pickups and zones.</summary>
        public bool sensor;

        /// <summary>Opaque to physics, handed back with every event so JS knows what was hit.</summary>
        public int tag;

        /// <summary>Initial velocity, panel units per second.</summary>
        public float vx;
        public float vy;
        public float angularVelocity;
    }

    public static class Physics2DWire {
        public const int Version = 1;

        public static Physics2DWireDoc Parse(string json) {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("[OneJS Physics2D] empty config.");

            Physics2DWireDoc doc;
            try {
                doc = JsonUtility.FromJson<Physics2DWireDoc>(json);
            } catch (Exception e) {
                throw new ArgumentException($"[OneJS Physics2D] config is not valid JSON: {e.Message}");
            }
            if (doc == null)
                throw new ArgumentException("[OneJS Physics2D] config parsed to nothing.");

            // A newer document against an older parser is refused rather than
            // parsed with its new fields dropped, which would run and be subtly
            // wrong instead of failing where it can be fixed.
            if (doc.v > Version)
                throw new ArgumentException(
                    $"[OneJS Physics2D] config is version {doc.v}, this OneJS understands {Version}. Update the package.");

            if (!(doc.pixelsPerUnit > 0f) || float.IsInfinity(doc.pixelsPerUnit))
                throw new ArgumentException("[OneJS Physics2D] pixelsPerUnit must be positive and finite.");
            if (doc.bodies == null) doc.bodies = Array.Empty<WireBody>();

            for (int i = 0; i < doc.bodies.Length; i++) {
                var b = doc.bodies[i];
                if (b == null)
                    throw new ArgumentException($"[OneJS Physics2D] body {i} is null.");
                if (b.type < 0 || b.type > 2)
                    throw new ArgumentException($"[OneJS Physics2D] body {i} has unknown type {b.type}.");
                if (b.shape < 0 || b.shape > 2)
                    throw new ArgumentException($"[OneJS Physics2D] body {i} has unknown shape {b.shape}.");
                // A zero-sized collider is accepted by PhysX and then behaves as
                // if it were not there, which reads as "physics is broken".
                if (!(b.w > 0f) || !(b.h > 0f))
                    throw new ArgumentException($"[OneJS Physics2D] body {i} has a non-positive size ({b.w}x{b.h}).");
                if (b.density < 0f)
                    throw new ArgumentException($"[OneJS Physics2D] body {i} has negative density.");
            }
            return doc;
        }
    }
}
