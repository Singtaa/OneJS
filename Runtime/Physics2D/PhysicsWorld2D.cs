using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// A 2D physics world whose bodies drive VisualElements.
    ///
    /// THE POINT OF THIS CLASS
    ///
    /// It owns the simulation *and* the binding from simulation to what you see.
    /// That is the whole reason it exists rather than exposing Rigidbody2D to
    /// JavaScript. Handing raw bodies over would mean reading a position and
    /// writing an element transform per body per frame, from JS, which is the
    /// per-entity cost DESIGN.md exists to prevent. Here a hundred bodies cost
    /// JavaScript nothing at all until one of them hits something.
    ///
    /// COORDINATES
    ///
    /// UI Toolkit measures in points from the top-left with Y down. Physics
    /// works in units from an origin with Y up. Everything crossing the boundary
    /// is in panel units, and this class is the only place the two meet.
    ///
    /// Elements are moved with transform.position rather than style.left/top:
    /// a transform is applied at render time, while left and top are layout and
    /// would re-run the layout engine for every body, every frame.
    /// </summary>
    public class PhysicsWorld2D : IDisposable {
        /// <summary>What one contact takes in the event buffer.</summary>
        public const int EventStride = 6;

        readonly VisualElement _host;
        readonly Physics2DWireDoc _doc;
        readonly GameObject _root;
        readonly List<Body> _bodies = new List<Body>();
        readonly List<float> _events = new List<float>();
        readonly List<GameObject> _walls = new List<GameObject>();

        Rect _lastHostRect;
        bool _disposed;

        public bool IsDisposed => _disposed;
        public int BodyCount => _bodies.Count;
        public int PendingEventCount => _events.Count / EventStride;

        class Body {
            public Rigidbody2D Rb;
            public VisualElement Element;
            public int Index;
            public int Tag;
            public float HalfW;
            public float HalfH;
        }

        public PhysicsWorld2D(VisualElement host, Physics2DWireDoc doc) {
            _host = host ?? throw new ArgumentException("[OneJS Physics2D] needs a host VisualElement.");
            _doc = doc;

            _root = new GameObject("OneJS Physics2D");
            _root.hideFlags = HideFlags.HideAndDontSave;

            // Stepped from OneJS's tick rather than by Unity's fixed loop. The
            // panel already has a clock, edit-mode preview has no fixed loop at
            // all, and stepping it ourselves keeps the two in step.
            UnityEngine.Physics2D.simulationMode = SimulationMode2D.Script;
            UnityEngine.Physics2D.velocityIterations = Mathf.Max(1, doc.velocityIterations);
            UnityEngine.Physics2D.positionIterations = Mathf.Max(1, doc.positionIterations);
            UnityEngine.Physics2D.gravity = new Vector2(
                doc.gravityX / doc.pixelsPerUnit,
                // Y down in the UI, Y up in physics.
                -doc.gravityY / doc.pixelsPerUnit);

            for (int i = 0; i < doc.bodies.Length; i++) AddBody(doc.bodies[i], i);
        }

        /// <summary>Binds body i to an element. Separate from construction because
        /// elements cannot travel inside a JSON document.</summary>
        public void Bind(int index, VisualElement element) {
            if (index < 0 || index >= _bodies.Count) return;
            _bodies[index].Element = element;
        }

        void AddBody(WireBody wire, int index) {
            var ppu = _doc.pixelsPerUnit;
            var go = new GameObject($"body{index}");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(wire.x / ppu, -wire.y / ppu, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, -wire.rotation);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = wire.type == 0 ? RigidbodyType2D.Dynamic
                : wire.type == 1 ? RigidbodyType2D.Kinematic
                : RigidbodyType2D.Static;
            rb.linearDamping = wire.linearDamping;
            rb.angularDamping = wire.angularDamping;
            rb.freezeRotation = wire.fixedRotation;
            rb.linearVelocity = new Vector2(wire.vx / ppu, -wire.vy / ppu);
            rb.angularVelocity = -wire.angularVelocity;
            // Small fast things tunnel through thin walls with discrete
            // collision, and a ball leaving a sealed box reads as broken.
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var material = new PhysicsMaterial2D($"m{index}") {
                friction = wire.friction,
                bounciness = wire.restitution,
            };

            Collider2D collider;
            switch (wire.shape) {
                case 1: {
                    var c = go.AddComponent<CircleCollider2D>();
                    c.radius = wire.w / ppu;
                    collider = c;
                    break;
                }
                case 2: {
                    var c = go.AddComponent<CapsuleCollider2D>();
                    c.size = new Vector2(wire.w / ppu, wire.h / ppu);
                    collider = c;
                    break;
                }
                default: {
                    var c = go.AddComponent<BoxCollider2D>();
                    c.size = new Vector2(wire.w / ppu, wire.h / ppu);
                    collider = c;
                    break;
                }
            }
            collider.sharedMaterial = material;
            collider.isTrigger = wire.sensor;
            collider.density = wire.density;

            var body = new Body {
                Rb = rb,
                Index = index,
                Tag = wire.tag,
                HalfW = wire.shape == 1 ? wire.w : wire.w * 0.5f,
                HalfH = wire.shape == 1 ? wire.w : wire.h * 0.5f,
            };
            _bodies.Add(body);

            if (wire.reportCollisions || wire.sensor) {
                var reporter = go.AddComponent<ContactReporter>();
                reporter.World = this;
                reporter.Index = index;
            }
        }

        /// <summary>
        /// Walls around the host rect. Rebuilt when the rect changes, because a
        /// stage that resizes with the window would otherwise keep the old ones.
        /// </summary>
        void SyncBounds(Rect rect) {
            foreach (var wall in _walls) if (wall != null) UnityEngine.Object.Destroy(wall);
            _walls.Clear();
            if (!_doc.bounds) return;

            var ppu = _doc.pixelsPerUnit;
            var w = rect.width / ppu;
            var h = rect.height / ppu;
            const float thickness = 1f;

            // left, right, top, bottom; each centred outside the play area so
            // the inner face sits exactly on the boundary.
            AddWall(-thickness * 0.5f, -h * 0.5f, thickness, h + thickness * 2f);
            AddWall(w + thickness * 0.5f, -h * 0.5f, thickness, h + thickness * 2f);
            AddWall(w * 0.5f, thickness * 0.5f, w + thickness * 2f, thickness);
            AddWall(w * 0.5f, -h - thickness * 0.5f, w + thickness * 2f, thickness);
        }

        void AddWall(float cx, float cy, float w, float h) {
            var go = new GameObject("wall");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(cx, cy, 0f);
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(w, h);
            collider.sharedMaterial = new PhysicsMaterial2D("bounds") {
                friction = _doc.boundsFriction,
                bounciness = _doc.boundsRestitution,
            };
            _walls.Add(go);
        }

        /// <summary>
        /// Advances the world and writes every body onto its element.
        ///
        /// This is the loop that would otherwise be in JavaScript, once per body
        /// per frame. It is the reason the wrapper exists.
        /// </summary>
        public void Tick(float dt) {
            if (_disposed) return;

            var rect = _host.contentRect;
            if (rect.width > 0f && rect.height > 0f &&
                (Mathf.Abs(rect.width - _lastHostRect.width) > 0.5f ||
                 Mathf.Abs(rect.height - _lastHostRect.height) > 0.5f)) {
                _lastHostRect = rect;
                SyncBounds(rect);
            }

            // Clamped: a tab restored after a minute must not advance the world
            // by a minute in one step, which throws everything through a wall.
            UnityEngine.Physics2D.Simulate(Mathf.Min(dt, 0.05f));

            var ppu = _doc.pixelsPerUnit;
            for (int i = 0; i < _bodies.Count; i++) {
                var body = _bodies[i];
                if (body.Element == null || body.Rb == null) continue;
                var p = body.Rb.position;
                // transform.position is relative to where layout already put the
                // element, so the body's centre becomes an offset from its own
                // top-left corner.
                body.Element.transform.position = new Vector3(
                    p.x * ppu - body.HalfW,
                    -p.y * ppu - body.HalfH,
                    0f);
                body.Element.transform.rotation = Quaternion.Euler(0f, 0f, -body.Rb.rotation);
            }
        }

        internal void ReportContact(int a, int b, Vector2 point) {
            // Bounded, so a pathological frame cannot grow this without limit
            // when JavaScript is not draining it.
            if (_events.Count >= EventStride * 512) return;
            var ppu = _doc.pixelsPerUnit;
            _events.Add(a);
            _events.Add(b);
            _events.Add(a >= 0 && a < _bodies.Count ? _bodies[a].Tag : 0);
            _events.Add(b >= 0 && b < _bodies.Count ? _bodies[b].Tag : 0);
            _events.Add(point.x * ppu);
            _events.Add(-point.y * ppu);
        }

        /// <summary>
        /// Every contact since the last call, as one flat array, then cleared.
        ///
        /// One crossing per frame regardless of how many contacts happened,
        /// rather than one per contact. Six floats each: bodyA, bodyB, tagA,
        /// tagB, x, y.
        /// </summary>
        public string DrainEvents() {
            if (_events.Count == 0) return "";
            var packed = Pack(_events);
            _events.Clear();
            return packed;
        }

        /// <summary>
        /// A flat numeric array as a JSON string.
        ///
        /// A returned float[] arrives in JavaScript as a wrapped C# object, not
        /// an array: length reads as null and Array.from gives nothing. Going
        /// the other way there is a typed-array path (__csArray, which is how
        /// PainterBridge sends its command buffer), but nothing equivalent comes
        /// back, so this is the v1 answer for the same reason PainterBridge
        /// documents. It is one crossing either way; what it costs extra is a
        /// parse, on a payload bounded at 512 contacts.
        /// </summary>
        static string Pack(List<float> values) {
            var sb = new System.Text.StringBuilder(values.Count * 8 + 2);
            sb.Append('[');
            for (int i = 0; i < values.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ---------- imperative, one crossing each, only when something changes ----------

        public void ApplyImpulse(int index, float x, float y) {
            var rb = RigidbodyAt(index);
            if (rb == null) return;
            var ppu = _doc.pixelsPerUnit;
            rb.AddForce(new Vector2(x / ppu, -y / ppu), ForceMode2D.Impulse);
        }

        public void SetVelocity(int index, float x, float y) {
            var rb = RigidbodyAt(index);
            if (rb == null) return;
            var ppu = _doc.pixelsPerUnit;
            rb.linearVelocity = new Vector2(x / ppu, -y / ppu);
        }

        /// <summary>
        /// Moves a body, whether or not it is currently simulating.
        ///
        /// Unity discards a write to <c>rb.position</c> on a body with
        /// <c>simulated = false</c>: the assignment is accepted, nothing
        /// happens, and the body later wakes wherever it was parked. That cost
        /// a game a whole afternoon and never looked like a position bug,
        /// because the symptoms were rounds ending early and players who were
        /// never visible.
        ///
        /// What makes it genuinely hard to find is that the property reads
        /// back the value that was never applied: write (99, 99) to a parked
        /// body and it reports (99, 99) until the moment it starts simulating,
        /// at which point it snaps to its transform and the write is gone. So
        /// the obvious check agrees with you, and the position is a lie rather
        /// than an error, which leaves everything downstream correct about it.
        ///
        /// So a parked body is moved through its transform, which always takes
        /// and which the body adopts when it starts simulating. The transform
        /// write is the slower of the two, which is why it is used only for the
        /// case where the fast one silently does nothing.
        /// </summary>
        public void SetPosition(int index, float x, float y) {
            var rb = RigidbodyAt(index);
            if (rb == null) return;
            var ppu = _doc.pixelsPerUnit;
            var target = new Vector2(x / ppu, -y / ppu);
            if (rb.simulated) {
                rb.position = target;
            } else {
                rb.transform.position = new Vector3(target.x, target.y, rb.transform.position.z);
            }
        }

        public void SetGravity(float x, float y) {
            var ppu = _doc.pixelsPerUnit;
            UnityEngine.Physics2D.gravity = new Vector2(x / ppu, -y / ppu);
        }

        public void SetBodyEnabled(int index, bool enabled) {
            var rb = RigidbodyAt(index);
            if (rb == null) return;
            rb.simulated = enabled;
        }

        /// <summary>
        /// Every body's position and rotation, one flat array: x, y, degrees.
        ///
        /// For the rare case where JavaScript genuinely needs the whole world,
        /// such as saving a game. Rule 3: one crossing carrying everything,
        /// never a property read per body.
        /// </summary>
        public string ReadTransforms() {
            var ppu = _doc.pixelsPerUnit;
            var packed = new List<float>(_bodies.Count * 3);
            for (int i = 0; i < _bodies.Count; i++) {
                var rb = _bodies[i].Rb;
                packed.Add(rb == null ? 0f : rb.position.x * ppu);
                packed.Add(rb == null ? 0f : -rb.position.y * ppu);
                packed.Add(rb == null ? 0f : -rb.rotation);
            }
            return Pack(packed);
        }

        Rigidbody2D RigidbodyAt(int index) =>
            index >= 0 && index < _bodies.Count ? _bodies[index].Rb : null;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _events.Clear();
            _bodies.Clear();
            _walls.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        /// <summary>
        /// Turns Unity's per-collider callbacks into entries in the world's
        /// queue. A MonoBehaviour because that is the only way Unity delivers
        /// them, and it does no work beyond forwarding.
        /// </summary>
        class ContactReporter : MonoBehaviour {
            public PhysicsWorld2D World;
            public int Index;

            void OnCollisionEnter2D(Collision2D collision) {
                var other = Resolve(collision.collider);
                var point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
                World?.ReportContact(Index, other, point);
            }

            void OnTriggerEnter2D(Collider2D collider) {
                World?.ReportContact(Index, Resolve(collider), collider.transform.position);
            }

            int Resolve(Collider2D collider) {
                if (World == null || collider == null) return -1;
                for (int i = 0; i < World._bodies.Count; i++) {
                    var rb = World._bodies[i].Rb;
                    if (rb != null && collider.attachedRigidbody == rb) return i;
                }
                return -1;   // a wall or something not in the world
            }
        }
    }
}
