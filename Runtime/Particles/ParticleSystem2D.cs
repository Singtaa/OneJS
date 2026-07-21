using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// 2D particle system rendered inside a UI Toolkit element.
    ///
    /// The simulation (SoA arrays, autonomous emitters, curve evaluation) and the
    /// mesh write both live entirely in C#: JS configures the system once via the
    /// wire schema (see ParticleWire) and issues single-crossing imperative calls;
    /// the per-frame loop never touches JS.
    ///
    /// Rendering subscribes to the host element's generateVisualContent in C# (no
    /// native callback-table slot) and writes one quad per particle through
    /// MeshGenerationContext.Allocate. Blending uses the OneJS/UIEParticles
    /// premultiplied shader via style.unityMaterial, giving each emitter an
    /// "additiveness" continuum (0 = normal alpha, 1 = pure additive) in a single
    /// draw call. If the shader is unavailable the system falls back to the
    /// default material: everything still renders, additiveness is ignored.
    /// </summary>
    public class ParticleSystem2D {
        const int MaxQuadsPerAlloc = 16000; // 4 verts/quad under the 65535 ushort limit

        // MARK: Particle state (SoA, capacity fixed at creation)
        readonly float[] _posX, _posY, _velX, _velY, _life, _lifetime, _size, _rot, _angVel;
        readonly int[] _emitterIdx;
        int _alive;
        readonly int _max;

        class EmitterState {
            public WireEmitter cfg;
            public float x, y;
            public float rate;
            public bool emitting;
            public float acc;
        }

        readonly EmitterState[] _emitters;
        readonly VisualElement _ve;
        readonly Texture2D _texture;
        readonly bool _panelSpace;
        readonly bool _premultiplied; // custom shader active -> premultiplied tints
        uint _rng;
        bool _paused;
        bool _disposed;
        bool _wasActive; // one extra repaint after going idle to clear stale quads

        public bool IsDisposed => _disposed;
        public int AliveCount => _alive;
        public int EmitterCount => _emitters.Length;

        // MARK: Shared resources (lazy statics; recreated after domain reload)

        static Material s_Material;
        static bool s_MaterialResolved;
        static Texture2D s_SoftDiscPremultiplied;
        static Texture2D s_SoftDiscStraight;

        static Material ResolveMaterial() {
            if (!s_MaterialResolved) {
                s_MaterialResolved = true;
                var shader = Resources.Load<Shader>("OneJS/UIEParticles");
                if (shader != null && shader.isSupported) {
                    s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                } else {
                    Debug.LogWarning("[OneJS Particles] OneJS/UIEParticles shader unavailable; " +
                        "falling back to normal alpha blending (additiveness ignored).");
                }
            }
            return s_Material;
        }

        // Radial falloff sprite used when no texture is supplied. The premultiplied
        // variant (rgb = alpha = falloff) matches the custom shader's blend math;
        // the straight variant (white rgb, alpha falloff) matches the fallback path.
        static Texture2D ResolveSoftDisc(bool premultiplied) {
            var cached = premultiplied ? s_SoftDiscPremultiplied : s_SoftDiscStraight;
            if (cached == null) {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[size * size];
                float center = (size - 1) * 0.5f;
                for (int py = 0; py < size; py++) {
                    for (int px = 0; px < size; px++) {
                        float dx = (px - center) / center;
                        float dy = (py - center) / center;
                        float f = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        f *= f; // quadratic falloff reads as a soft glow
                        byte fb = (byte)(f * 255f + 0.5f);
                        byte rgb = premultiplied ? fb : (byte)255;
                        pixels[py * size + px] = new Color32(rgb, rgb, rgb, fb);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                if (premultiplied) s_SoftDiscPremultiplied = tex;
                else s_SoftDiscStraight = tex;
                cached = tex;
            }
            return cached;
        }

        // MARK: Lifecycle

        internal ParticleSystem2D(VisualElement ve, ParticleWireDoc doc, Texture2D texture) {
            _ve = ve;
            _max = doc.max;
            _panelSpace = doc.space == 1;
            _rng = doc.seed != 0 ? (uint)doc.seed : (uint)(Environment.TickCount | 1);

            _posX = new float[_max];
            _posY = new float[_max];
            _velX = new float[_max];
            _velY = new float[_max];
            _life = new float[_max];
            _lifetime = new float[_max];
            _size = new float[_max];
            _rot = new float[_max];
            _angVel = new float[_max];
            _emitterIdx = new int[_max];

            _emitters = new EmitterState[doc.emitters.Length];
            for (int i = 0; i < doc.emitters.Length; i++) {
                var cfg = doc.emitters[i];
                _emitters[i] = new EmitterState {
                    cfg = cfg,
                    x = cfg.x,
                    y = cfg.y,
                    rate = cfg.rate,
                    emitting = cfg.emitting,
                };
            }

            _premultiplied = ResolveMaterial() != null;
            _texture = texture != null ? texture : ResolveSoftDisc(_premultiplied);

            if (_premultiplied)
                _ve.style.unityMaterial = MaterialDefinition.FromMaterial(s_Material);
            _ve.generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>
        /// Detaches from the host element and stops all work. Safe to call twice.
        /// Invoked from JS (effect cleanup / teardown) or ParticleBridge.DisposeAll.
        /// </summary>
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _alive = 0;
            _ve.generateVisualContent -= OnGenerateVisualContent;
            if (_premultiplied)
                _ve.style.unityMaterial = StyleKeyword.Null;
            if (_ve.panel != null)
                _ve.MarkDirtyRepaint();
        }

        // MARK: JS-facing imperative API (each call is a single interop crossing)

        public void SetEmitterPos(int index, float x, float y) {
            var e = GetEmitter(index);
            if (e == null) return;
            e.x = x;
            e.y = y;
        }

        public void SetEmitterRate(int index, float rate) {
            var e = GetEmitter(index);
            if (e == null) return;
            e.rate = Mathf.Clamp(rate, 0f, 100000f);
        }

        public void StartEmitter(int index) {
            var e = GetEmitter(index);
            if (e != null) e.emitting = true;
        }

        public void StopEmitter(int index) {
            var e = GetEmitter(index);
            if (e != null) e.emitting = false;
        }

        /// <summary>One-shot emission of count particles at (x, y) using emitter index's ranges.</summary>
        public void Burst(int index, float x, float y, int count) {
            var e = GetEmitter(index);
            if (e == null || _disposed) return;
            for (int i = 0; i < count && _alive < _max; i++)
                Spawn(e, IndexOf(e), x, y);
            if (_ve.panel != null)
                _ve.MarkDirtyRepaint();
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void Clear() {
            _alive = 0;
            if (!_disposed && _ve.panel != null)
                _ve.MarkDirtyRepaint();
        }

        // Debug/test accessors (bounds-safe; NaN when out of range).
        public float GetParticleX(int i) => i >= 0 && i < _alive ? _posX[i] : float.NaN;
        public float GetParticleY(int i) => i >= 0 && i < _alive ? _posY[i] : float.NaN;

        EmitterState GetEmitter(int index) {
            if (_disposed) return null;
            if (index < 0 || index >= _emitters.Length) {
                Debug.LogWarning($"[OneJS Particles] emitter index {index} out of range (0..{_emitters.Length - 1}).");
                return null;
            }
            return _emitters[index];
        }

        int IndexOf(EmitterState e) {
            for (int i = 0; i < _emitters.Length; i++)
                if (_emitters[i] == e)
                    return i;
            return 0;
        }

        // MARK: Simulation

        /// <summary>
        /// Advances the simulation by dt seconds and requests a repaint while
        /// active. Called by ParticleBridge.TickAll; public so tests (and JS, if
        /// it ever needs manual stepping) can drive it directly. Runs without a
        /// panel; only the repaint requires attachment.
        /// </summary>
        public void Tick(float dt) {
            if (_disposed || _paused) return;

            // Emission
            for (int ei = 0; ei < _emitters.Length; ei++) {
                var e = _emitters[ei];
                if (!e.emitting || e.rate <= 0f) continue;
                e.acc += e.rate * dt;
                int spawn = (int)e.acc;
                if (spawn <= 0) continue;
                e.acc -= spawn;
                for (int i = 0; i < spawn && _alive < _max; i++)
                    Spawn(e, ei, e.x, e.y);
            }

            // Integration + swap-back reaping
            for (int i = 0; i < _alive; i++) {
                _life[i] += dt;
                if (_life[i] >= _lifetime[i]) {
                    int last = _alive - 1;
                    if (i != last) {
                        _posX[i] = _posX[last]; _posY[i] = _posY[last];
                        _velX[i] = _velX[last]; _velY[i] = _velY[last];
                        _life[i] = _life[last]; _lifetime[i] = _lifetime[last];
                        _size[i] = _size[last]; _rot[i] = _rot[last];
                        _angVel[i] = _angVel[last]; _emitterIdx[i] = _emitterIdx[last];
                    }
                    _alive = last;
                    i--;
                    continue;
                }

                var cfg = _emitters[_emitterIdx[i]].cfg;
                _velX[i] += cfg.gravityX * dt;
                _velY[i] += cfg.gravityY * dt;
                if (cfg.drag > 0f) {
                    float damp = 1f - cfg.drag * dt;
                    if (damp < 0f) damp = 0f;
                    _velX[i] *= damp;
                    _velY[i] *= damp;
                }
                _posX[i] += _velX[i] * dt;
                _posY[i] += _velY[i] * dt;
                _rot[i] += _angVel[i] * dt;
            }

            bool active = _alive > 0 || AnyEmitterActive();
            if ((active || _wasActive) && _ve.panel != null)
                _ve.MarkDirtyRepaint();
            _wasActive = active;
        }

        bool AnyEmitterActive() {
            for (int i = 0; i < _emitters.Length; i++)
                if (_emitters[i].emitting && _emitters[i].rate > 0f)
                    return true;
            return false;
        }

        void Spawn(EmitterState e, int emitterIndex, float x, float y) {
            var cfg = e.cfg;
            float px = x, py = y;
            switch (cfg.shape) {
                case 1: { // circle
                    float ang = NextFloat() * Mathf.PI * 2f;
                    float rad = Mathf.Sqrt(NextFloat()) * cfg.shapeW; // sqrt for uniform area
                    px += Mathf.Cos(ang) * rad;
                    py += Mathf.Sin(ang) * rad;
                    break;
                }
                case 2: // rect (centered)
                    px += (NextFloat() - 0.5f) * cfg.shapeW;
                    py += (NextFloat() - 0.5f) * cfg.shapeH;
                    break;
                case 3: // horizontal line (centered)
                    px += (NextFloat() - 0.5f) * cfg.shapeW;
                    break;
            }

            if (_panelSpace) {
                var p = _ve.worldTransform.MultiplyPoint3x4(new Vector3(px, py, 0f));
                px = p.x;
                py = p.y;
            }

            float dir = Range(cfg.angleMin, cfg.angleMax) * Mathf.Deg2Rad;
            float speed = Range(cfg.speedMin, cfg.speedMax);

            int i = _alive++;
            _posX[i] = px;
            _posY[i] = py;
            _velX[i] = Mathf.Cos(dir) * speed;
            _velY[i] = Mathf.Sin(dir) * speed;
            _life[i] = 0f;
            _lifetime[i] = Range(cfg.lifeMin, cfg.lifeMax);
            _size[i] = Range(cfg.sizeMin, cfg.sizeMax);
            _rot[i] = Range(cfg.rotMin, cfg.rotMax) * Mathf.Deg2Rad;
            _angVel[i] = Range(cfg.angVelMin, cfg.angVelMax) * Mathf.Deg2Rad;
            _emitterIdx[i] = emitterIndex;
        }

        float NextFloat() { // xorshift32 -> [0, 1)
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        float Range(float min, float max) => min + (max - min) * NextFloat();

        // MARK: Rendering

        void OnGenerateVisualContent(MeshGenerationContext mgc) {
            if (_disposed || _alive == 0) return;

            Matrix4x4 inv = _panelSpace ? _ve.worldTransform.inverse : Matrix4x4.identity;

            int written = 0;
            while (written < _alive) {
                int n = Math.Min(_alive - written, MaxQuadsPerAlloc);
                // Raw 0..1 UVs: the renderer remaps into the dynamic atlas region.
                var mwd = mgc.Allocate(n * 4, n * 6, _texture);
                const float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
                float z = Vertex.nearZ;

                ushort vi = 0;
                for (int k = 0; k < n; k++) {
                    int i = written + k;
                    var cfg = _emitters[_emitterIdx[i]].cfg;
                    float t = _life[i] / _lifetime[i];

                    EvalColor(cfg.colorKeys, t, out float cr, out float cg, out float cb, out float ca);
                    float half = _size[i] * EvalFloat(cfg.sizeKeys, t) * 0.5f;

                    Color32 tint;
                    if (_premultiplied) {
                        tint = new Color32(
                            (byte)(cr * ca * 255f + 0.5f),
                            (byte)(cg * ca * 255f + 0.5f),
                            (byte)(cb * ca * 255f + 0.5f),
                            (byte)(ca * (1f - cfg.additiveness) * 255f + 0.5f));
                    } else {
                        tint = new Color32(
                            (byte)(cr * 255f + 0.5f),
                            (byte)(cg * 255f + 0.5f),
                            (byte)(cb * 255f + 0.5f),
                            (byte)(ca * 255f + 0.5f));
                    }

                    float c = Mathf.Cos(_rot[i]) * half;
                    float s = Mathf.Sin(_rot[i]) * half;
                    float cx = _posX[i], cy = _posY[i];
                    // rotated half-extent basis: right = (c, s), up = (-s, c)
                    float tlx = cx - c + s, tly = cy - s - c;
                    float trx = cx + c + s, trY = cy + s - c;
                    float brx = cx + c - s, brY = cy + s + c;
                    float blx = cx - c - s, blY = cy - s + c;

                    if (_panelSpace) {
                        Vector3 p;
                        p = inv.MultiplyPoint3x4(new Vector3(tlx, tly, 0f)); tlx = p.x; tly = p.y;
                        p = inv.MultiplyPoint3x4(new Vector3(trx, trY, 0f)); trx = p.x; trY = p.y;
                        p = inv.MultiplyPoint3x4(new Vector3(brx, brY, 0f)); brx = p.x; brY = p.y;
                        p = inv.MultiplyPoint3x4(new Vector3(blx, blY, 0f)); blx = p.x; blY = p.y;
                    }

                    mwd.SetNextVertex(new Vertex { position = new Vector3(tlx, tly, z), tint = tint, uv = new Vector2(u0, v1) });
                    mwd.SetNextVertex(new Vertex { position = new Vector3(trx, trY, z), tint = tint, uv = new Vector2(u1, v1) });
                    mwd.SetNextVertex(new Vertex { position = new Vector3(brx, brY, z), tint = tint, uv = new Vector2(u1, v0) });
                    mwd.SetNextVertex(new Vertex { position = new Vector3(blx, blY, z), tint = tint, uv = new Vector2(u0, v0) });
                    mwd.SetNextIndex(vi);
                    mwd.SetNextIndex((ushort)(vi + 1));
                    mwd.SetNextIndex((ushort)(vi + 2));
                    mwd.SetNextIndex((ushort)(vi + 2));
                    mwd.SetNextIndex((ushort)(vi + 3));
                    mwd.SetNextIndex(vi);
                    vi += 4;
                }
                written += n;
            }
        }

        static void EvalColor(WireColorKey[] keys, float t, out float r, out float g, out float b, out float a) {
            var last = keys[keys.Length - 1];
            if (keys.Length == 1 || t <= keys[0].t) {
                var k = t <= keys[0].t ? keys[0] : last;
                r = k.r; g = k.g; b = k.b; a = k.a;
                return;
            }
            for (int i = 1; i < keys.Length; i++) {
                if (t <= keys[i].t) {
                    var k0 = keys[i - 1];
                    var k1 = keys[i];
                    float span = k1.t - k0.t;
                    float f = span > 0f ? (t - k0.t) / span : 1f;
                    r = k0.r + (k1.r - k0.r) * f;
                    g = k0.g + (k1.g - k0.g) * f;
                    b = k0.b + (k1.b - k0.b) * f;
                    a = k0.a + (k1.a - k0.a) * f;
                    return;
                }
            }
            r = last.r; g = last.g; b = last.b; a = last.a;
        }

        static float EvalFloat(WireFloatKey[] keys, float t) {
            if (keys.Length == 1 || t <= keys[0].t) return t <= keys[0].t ? keys[0].v : keys[keys.Length - 1].v;
            for (int i = 1; i < keys.Length; i++) {
                if (t <= keys[i].t) {
                    var k0 = keys[i - 1];
                    var k1 = keys[i];
                    float span = k1.t - k0.t;
                    float f = span > 0f ? (t - k0.t) / span : 1f;
                    return k0.v + (k1.v - k0.v) * f;
                }
            }
            return keys[keys.Length - 1].v;
        }
    }
}
