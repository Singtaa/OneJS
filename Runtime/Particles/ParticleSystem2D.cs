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
    ///
    /// Emitters may override the system texture; particles are then grouped by
    /// texture at repaint so each distinct sprite costs exactly one Allocate.
    /// An emitter may also treat its texture as a flipbook grid, in which case
    /// the quad's UVs narrow to one cell chosen from the particle's age.
    /// </summary>
    public class ParticleSystem2D {
        const int MaxQuadsPerAlloc = 16000; // 4 verts/quad under the 65535 ushort limit
        const byte FlagStuck = 1;

        // MARK: Particle state (SoA, capacity fixed at creation)
        readonly float[] _posX, _posY, _velX, _velY, _life, _lifetime, _size, _aspect, _rot, _angVel;
        readonly int[] _emitterIdx;
        readonly byte[] _tintIdx;    // index into the emitter's tintPalette
        readonly byte[] _frameStart; // flipbook start frame (only drawn when sheetRandomStart)
        readonly byte[] _flags;
        int _alive;
        readonly int _max;

        class EmitterState {
            public WireEmitter cfg;
            public float x, y;
            public float attractX, attractY; // target in emitter-local px
            public float attrPX, attrPY;     // resolved into simulation space each tick
            public float rate;
            public bool emitting;
            public float acc;
        }

        readonly EmitterState[] _emitters;
        readonly VisualElement _ve;
        readonly Texture2D _texture; // system default; per-emitter overrides live in _emitterTex
        readonly bool _panelSpace;
        readonly bool _premultiplied; // custom shader active -> premultiplied tints
        readonly bool _anyAttract;    // attractStrength is immutable, so this is fixed at creation
        readonly bool _anyEdge;
        uint _rng;
        bool _paused;
        bool _disposed;
        bool _wasActive;     // one extra repaint after going idle to clear stale quads
        bool _chromeChecked; // host-styling warning is emitted at most once per system

        // Texture grouping (only the arrays for the multi-texture path are lazy)
        readonly Texture2D[] _emitterTex;
        readonly Texture2D[] _groupTex;
        readonly int[] _emitterGroup;
        int _groupCount;
        bool _multiTexture;
        int[] _order, _groupStart, _cursor;

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
            _aspect = new float[_max];
            _rot = new float[_max];
            _angVel = new float[_max];
            _emitterIdx = new int[_max];
            _tintIdx = new byte[_max];
            _frameStart = new byte[_max];
            _flags = new byte[_max];

            _emitters = new EmitterState[doc.emitters.Length];
            bool anyAttract = false, anyEdge = false;
            for (int i = 0; i < doc.emitters.Length; i++) {
                var cfg = doc.emitters[i];
                _emitters[i] = new EmitterState {
                    cfg = cfg,
                    x = cfg.x,
                    y = cfg.y,
                    attractX = cfg.attractX,
                    attractY = cfg.attractY,
                    rate = cfg.rate,
                    emitting = cfg.emitting,
                };
                if (cfg.attractStrength > 0f) anyAttract = true;
                if (cfg.edge != 0) anyEdge = true;
            }
            _anyAttract = anyAttract;
            _anyEdge = anyEdge;

            _premultiplied = ResolveMaterial() != null;
            _texture = texture != null ? texture : ResolveSoftDisc(_premultiplied);

            _emitterTex = new Texture2D[_emitters.Length];
            _groupTex = new Texture2D[_emitters.Length];
            _emitterGroup = new int[_emitters.Length];
            for (int i = 0; i < _emitterTex.Length; i++)
                _emitterTex[i] = _texture;
            RebuildTextureGroups();

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

        /// <summary>Moves the attraction target. No effect unless attractStrength &gt; 0.</summary>
        public void SetEmitterAttractor(int index, float x, float y) {
            var e = GetEmitter(index);
            if (e == null) return;
            e.attractX = x;
            e.attractY = y;
        }

        /// <summary>
        /// Overrides the sprite for one emitter. Setup-time call: emitters sharing
        /// a texture still share a draw, so keep the number of distinct textures low.
        /// Passing null restores the system texture.
        /// </summary>
        public void SetEmitterTexture(int index, Texture2D texture) {
            if (GetEmitter(index) == null) return;
            _emitterTex[index] = texture != null ? texture : _texture;
            RebuildTextureGroups();
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

        /// <summary>Distinct texture groups, i.e. draw entries per repaint. For tests/monitoring.</summary>
        public int TextureGroupCount => _groupCount;

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

        void RebuildTextureGroups() {
            int groups = 0;
            for (int ei = 0; ei < _emitterTex.Length; ei++) {
                var tex = _emitterTex[ei];
                int g = -1;
                for (int k = 0; k < groups; k++) {
                    if (ReferenceEquals(_groupTex[k], tex)) {
                        g = k;
                        break;
                    }
                }
                if (g < 0) {
                    g = groups++;
                    _groupTex[g] = tex;
                }
                _emitterGroup[ei] = g;
            }
            _groupCount = groups;
            _multiTexture = groups > 1;
            if (_multiTexture && _order == null) {
                _order = new int[_max];
                _groupStart = new int[_emitterTex.Length + 1];
                _cursor = new int[_emitterTex.Length];
            }
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

            WarnIfHostIsStyled();

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

            // Attraction targets are authored in emitter-local px; resolve them into
            // simulation space once per emitter rather than once per particle.
            if (_anyAttract) {
                for (int ei = 0; ei < _emitters.Length; ei++) {
                    var e = _emitters[ei];
                    if (e.cfg.attractStrength <= 0f) continue;
                    if (_panelSpace) {
                        var p = _ve.worldTransform.MultiplyPoint3x4(new Vector3(e.attractX, e.attractY, 0f));
                        e.attrPX = p.x;
                        e.attrPY = p.y;
                    } else {
                        e.attrPX = e.attractX;
                        e.attrPY = e.attractY;
                    }
                }
            }

            // Edge collision needs a resolved rect; skip while detached/unlaid-out.
            var bounds = _anyEdge ? (_panelSpace ? _ve.worldBound : _ve.contentRect) : default;
            bool edgeActive = _anyEdge && bounds.width > 0f && bounds.height > 0f;

            // Integration + swap-back reaping
            for (int i = 0; i < _alive; i++) {
                _life[i] += dt;
                bool dead = _life[i] >= _lifetime[i];

                if (!dead && (_flags[i] & FlagStuck) == 0) {
                    var es = _emitters[_emitterIdx[i]];
                    var cfg = es.cfg;

                    _velX[i] += cfg.gravityX * dt;
                    _velY[i] += cfg.gravityY * dt;
                    if (cfg.drag > 0f) {
                        float damp = 1f - cfg.drag * dt;
                        if (damp < 0f) damp = 0f;
                        _velX[i] *= damp;
                        _velY[i] *= damp;
                    }

                    float vx = _velX[i], vy = _velY[i];
                    if (cfg.attractStrength > 0f) {
                        float remaining = _lifetime[i] - _life[i];
                        if (remaining > 1e-4f) {
                            // Blend toward the velocity that lands exactly on the target
                            // at end of life. The weight is a function of normalized life,
                            // so convergence is framerate-independent.
                            float w = cfg.attractStrength * Ease(cfg.attractEase, _life[i] / _lifetime[i]);
                            vx += ((es.attrPX - _posX[i]) / remaining - vx) * w;
                            vy += ((es.attrPY - _posY[i]) / remaining - vy) * w;
                        }
                    }

                    _posX[i] += vx * dt;
                    _posY[i] += vy * dt;
                    _rot[i] += _angVel[i] * dt;

                    if (cfg.edge != 0 && edgeActive)
                        dead = ApplyEdge(i, cfg, bounds);
                }

                if (dead) {
                    ReapAt(i);
                    i--;
                }
            }

            bool active = _alive > 0 || AnyEmitterActive();
            if ((active || _wasActive) && _ve.panel != null)
                _ve.MarkDirtyRepaint();
            _wasActive = active;
        }

        /// <summary>
        /// A host is particle-owned: assigning style.unityMaterial replaces the standard
        /// UI material for that element's draw, which costs it UI Toolkit's analytic
        /// antialiasing - a bordered/rounded host renders visibly jagged corners.
        /// (Measured: the same corner drops from a ~27-level coverage ramp to 3 levels.
        /// Subscribing generateVisualContent alone is free; only the material matters,
        /// hence the _premultiplied gate - the fallback path keeps the host's material.)
        ///
        /// Deferred to Tick because resolvedStyle is only meaningful once the element is
        /// attached and laid out. Emitted at most once per system.
        /// </summary>
        void WarnIfHostIsStyled() {
            if (_chromeChecked || !_premultiplied) return;
            if (_ve.panel == null) return;
            var rs = _ve.resolvedStyle;
            if (float.IsNaN(rs.width) || rs.width <= 0f) return; // not laid out yet
            _chromeChecked = true;

            bool border = rs.borderTopWidth > 0f || rs.borderRightWidth > 0f
                       || rs.borderBottomWidth > 0f || rs.borderLeftWidth > 0f;
            bool radius = rs.borderTopLeftRadius > 0f || rs.borderTopRightRadius > 0f
                       || rs.borderBottomLeftRadius > 0f || rs.borderBottomRightRadius > 0f;
            if (!border && !radius) return;

            var name = string.IsNullOrEmpty(_ve.name) ? _ve.GetType().Name : $"\"{_ve.name}\"";
            Debug.LogWarning(
                $"[OneJS Particles] host element {name} has a border and/or border-radius. A particle host " +
                "is particle-owned - the system assigns style.unityMaterial to it, which replaces the " +
                "standard UI material and costs that element UI Toolkit's antialiasing, so its corners " +
                "render jagged. Move the system to a dedicated unstyled overlay element and put the " +
                "border/radius on a sibling.");
        }

        /// <summary>Returns true when the particle should die (edge mode "kill").</summary>
        bool ApplyEdge(int i, WireEmitter cfg, Rect b) {
            float x = _posX[i], y = _posY[i];
            if (x >= b.xMin && x <= b.xMax && y >= b.yMin && y <= b.yMax)
                return false;

            switch (cfg.edge) {
                case 1: // kill
                    return true;
                case 2: { // bounce - drive the velocity inward rather than negating, so a
                          // particle already heading back in never re-reflects.
                    float r = cfg.bounciness;
                    if (x < b.xMin) { _posX[i] = b.xMin; _velX[i] = Mathf.Abs(_velX[i]) * r; }
                    else if (x > b.xMax) { _posX[i] = b.xMax; _velX[i] = -Mathf.Abs(_velX[i]) * r; }
                    if (y < b.yMin) { _posY[i] = b.yMin; _velY[i] = Mathf.Abs(_velY[i]) * r; }
                    else if (y > b.yMax) { _posY[i] = b.yMax; _velY[i] = -Mathf.Abs(_velY[i]) * r; }
                    return false;
                }
                case 3: // stick - freeze in place; the particle still ages and fades out
                    _posX[i] = Mathf.Clamp(x, b.xMin, b.xMax);
                    _posY[i] = Mathf.Clamp(y, b.yMin, b.yMax);
                    _velX[i] = 0f;
                    _velY[i] = 0f;
                    _angVel[i] = 0f;
                    _flags[i] |= FlagStuck;
                    return false;
            }
            return false;
        }

        void ReapAt(int i) {
            int last = _alive - 1;
            if (i != last) {
                _posX[i] = _posX[last]; _posY[i] = _posY[last];
                _velX[i] = _velX[last]; _velY[i] = _velY[last];
                _life[i] = _life[last]; _lifetime[i] = _lifetime[last];
                _size[i] = _size[last]; _aspect[i] = _aspect[last];
                _rot[i] = _rot[last]; _angVel[i] = _angVel[last];
                _emitterIdx[i] = _emitterIdx[last];
                _tintIdx[i] = _tintIdx[last]; _flags[i] = _flags[last];
                _frameStart[i] = _frameStart[last];
            }
            _alive = last;
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
            _flags[i] = 0;
            // The v2 draws below are taken only when the feature is actually in use,
            // so configs that don't set aspect/tintPalette keep their v1 RNG stream.
            _aspect[i] = cfg.aspectMax > cfg.aspectMin ? Range(cfg.aspectMin, cfg.aspectMax) : cfg.aspectMin;
            _tintIdx[i] = cfg.tintPalette != null && cfg.tintPalette.Length > 0
                ? (byte)(NextFloat() * cfg.tintPalette.Length)
                : (byte)0;
            _frameStart[i] = cfg.sheetRandomStart && cfg.sheetFrames > 1
                ? (byte)(NextFloat() * cfg.sheetFrames)
                : (byte)0;
        }

        float NextFloat() { // xorshift32 -> [0, 1)
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>
        /// Flipbook frame for a particle, given its age. Pure so it can be tested
        /// without a GPU: mode 0 spreads sheetFrames evenly over the lifetime
        /// (t is normalized life), mode 1 plays at sheetFps and loops. The result
        /// is always in 0..sheetFrames-1.
        /// </summary>
        public static int SheetFrame(WireEmitter cfg, float life, float t, int startFrame) {
            int frames = cfg.sheetFrames;
            if (frames <= 1) return 0;
            int f = cfg.sheetMode == 1
                ? (int)(life * cfg.sheetFps) // fixed fps, loops
                : (int)(t * frames);         // one pass over life
            f = (f + startFrame) % frames;
            return f < 0 ? f + frames : f;
        }

        static float Ease(int mode, float t) {
            switch (mode) {
                case 1: return t * t;                              // in: hold the spread, then whoosh
                case 2: { float u = 1f - t; return 1f - u * u; }   // out: snap away, then coast
                default: return t;                                 // linear
            }
        }

        // MARK: Rendering

        void OnGenerateVisualContent(MeshGenerationContext mgc) {
            if (_disposed || _alive == 0) return;

            Matrix4x4 inv = _panelSpace ? _ve.worldTransform.inverse : Matrix4x4.identity;

            if (!_multiTexture) {
                EmitRun(mgc, null, 0, _alive, _texture, inv);
                return;
            }

            // Counting-sort the alive set into per-texture runs so each distinct
            // sprite costs one Allocate (one draw entry) instead of one per streak.
            for (int g = 0; g <= _groupCount; g++)
                _groupStart[g] = 0;
            for (int i = 0; i < _alive; i++)
                _groupStart[_emitterGroup[_emitterIdx[i]] + 1]++;
            for (int g = 0; g < _groupCount; g++)
                _groupStart[g + 1] += _groupStart[g];
            Array.Copy(_groupStart, _cursor, _groupCount);
            for (int i = 0; i < _alive; i++)
                _order[_cursor[_emitterGroup[_emitterIdx[i]]]++] = i;

            for (int g = 0; g < _groupCount; g++) {
                int count = _groupStart[g + 1] - _groupStart[g];
                if (count > 0)
                    EmitRun(mgc, _order, _groupStart[g], count, _groupTex[g], inv);
            }
        }

        /// <summary>
        /// Writes count quads as one or more Allocate chunks. order maps slot -&gt;
        /// particle index; null means the run is the alive array itself.
        /// </summary>
        void EmitRun(MeshGenerationContext mgc, int[] order, int start, int count, Texture2D tex, Matrix4x4 inv) {
            float z = Vertex.nearZ;

            int written = 0;
            while (written < count) {
                int n = Math.Min(count - written, MaxQuadsPerAlloc);
                var mwd = mgc.Allocate(n * 4, n * 6, tex);

                ushort vi = 0;
                for (int k = 0; k < n; k++) {
                    int slot = start + written + k;
                    int i = order == null ? slot : order[slot];
                    var cfg = _emitters[_emitterIdx[i]].cfg;
                    float t = _life[i] / _lifetime[i];

                    EvalColor(cfg.colorKeys, t, out float cr, out float cg, out float cb, out float ca);
                    var palette = cfg.tintPalette;
                    if (palette != null && palette.Length > 0) {
                        var p = palette[_tintIdx[i]];
                        cr *= p.r; cg *= p.g; cb *= p.b; ca *= p.a;
                    }

                    float halfH = _size[i] * EvalFloat(cfg.sizeKeys, t) * 0.5f;
                    float halfW = halfH * _aspect[i];

                    // Raw 0..1 UVs (the renderer remaps them into the dynamic atlas
                    // region), narrowed to one cell when the emitter uses a flipbook.
                    float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
                    if (cfg.sheetFrames > 1) {
                        int f = SheetFrame(cfg, _life[i], t, _frameStart[i]);
                        int col = f % cfg.sheetCols;
                        int row = f / cfg.sheetCols;
                        float du = 1f / cfg.sheetCols, dv = 1f / cfg.sheetRows;
                        u0 = col * du;
                        u1 = u0 + du;
                        // Frame 0 is the sheet's top-left; texture V runs bottom-up.
                        v1 = 1f - row * dv;
                        v0 = v1 - dv;
                    }

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

                    // Rotated half-extent basis. Panel coords are Y-down, so screen-up
                    // is (sin, -cos); right is (cos, sin). Non-square quads scale the
                    // two axes independently via aspect (width:height).
                    float cos = Mathf.Cos(_rot[i]), sin = Mathf.Sin(_rot[i]);
                    float rx = cos * halfW, ry = sin * halfW;
                    float ux = sin * halfH, uy = -cos * halfH;
                    float cx = _posX[i], cy = _posY[i];
                    float tlx = cx - rx + ux, tly = cy - ry + uy;
                    float trx = cx + rx + ux, trY = cy + ry + uy;
                    float brx = cx + rx - ux, brY = cy + ry - uy;
                    float blx = cx - rx - ux, blY = cy - ry - uy;

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
