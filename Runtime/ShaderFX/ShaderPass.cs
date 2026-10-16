using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.ShaderFX {
    /// <summary>
    /// A shader, or a shader language program, drawn into a texture every frame.
    ///
    /// Made on its own (onejs-react's usePass), it is a pass: what another
    /// program reads through a texture2D it declares. A pass is drawn before
    /// anything that reads it, so a reader sees this frame's picture; it takes
    /// the size of the picture it ends up in, times its scale, unless given
    /// one; and it keeps its picture raw,
    /// 16 bits a channel in linear colour, so values over 1 survive to the next
    /// pass. Nothing reading it means nothing drawn.
    ///
    /// Inside a <see cref="ShaderEffectElement"/>, it is what the element shows:
    /// the element sizes it from its layout and gives it a target UI Toolkit
    /// can show. Everything else, the program, the clock, the previous frame
    /// and the drawing, is this class, for both.
    /// </summary>
    public class ShaderPass : IDisposable {
        internal const int MinRes = 8;
        internal const int MaxRes = 2048;

        Material _material;
        RenderTexture _rt;
        string _shaderName;
        string _programHash;
        bool _isProgram;
        bool _shaderMissing;
        bool _disposed;

        // Pending property values, applied to the material before each blit so JS
        // can set them before the shader/material exists.
        readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        readonly Dictionary<string, Vector4> _vectors = new Dictionary<string, Vector4>();
        readonly Dictionary<string, Vector4[]> _vectorArrays = new Dictionary<string, Vector4[]>();
        readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();

        int _resW, _resH;      // 0 = follow the layout, or what reads the pass
        int _rtW, _rtH;        // the size drawn at; 0 before the first
        float _scale = 1f;
        float _seconds;
        bool _paused;
        bool _drawAtSetTime;   // SetTime's frame is still to be drawn, paused or not
        bool _targetIsNew;     // the target's contents are undefined until its first draw or ClearIfNew

        // A program drawn frame after frame (Specs/SL_NEXT.md 4). A frame is
        // one step of this pass's clock: the last result becomes the previous
        // frame, the count goes up by one and the step is the time moved. A
        // draw at the same time draws the same frame again. A clear makes the
        // next frame frame 0, on a transparent black previous frame.
        RenderTexture[] _history;  // the raw frames, only for a program that reads the previous one
        int _current;              // the half of _history holding the last frame drawn
        bool _keeps;               // the last frame drawn went into _history
        int _frame;                // frames since the last clear
        float _step;               // the seconds from the frame before to this one
        float _unstepped;          // time moved since the last frame drawn, which the next one steps by
        bool _cleared = true;      // the next frame is frame 0
        static bool s_WarnedRawFormat;

        // Passes (Specs/SL_PASSES.md). What this reads, by the program's texture
        // slot, and what reads this, for its size. Never a cycle: SetProgramPass
        // refuses the binding that would close one.
        readonly Dictionary<int, ShaderPass> _inputs = new Dictionary<int, ShaderPass>();
        readonly List<ShaderPass> _readers = new List<ShaderPass>();
        int _drawnIn = -1;         // the draw (ShaderEffectBridge.Draws) this last took part in as an input

        /// <summary>
        /// The context that created this, so tearing that context down disposes
        /// it without touching another JSRunner's. 0 when made outside a JS call.
        /// </summary>
        internal int OwnerContextId { get; set; }

        /// <summary>A pass, drawn for whatever reads it.</summary>
        public ShaderPass() : this(standalone: true) { }

        /// <summary>The pass inside an element, which the element registers and ticks.</summary>
        protected ShaderPass(bool standalone) {
            if (standalone) ShaderEffectBridge.RegisterPass(this);
        }

        // MARK: what an element changes

        /// <summary>
        /// The size this draws at with no resolution set: for a pass, the
        /// picture it ends up in, scaled. False means there is no size yet.
        /// </summary>
        protected virtual bool TryGetNaturalSize(out int w, out int h) {
            if (!TryGetPictureSize(out w, out h)) return false;
            w = Mathf.RoundToInt(w * _scale);
            h = Mathf.RoundToInt(h * _scale);
            return true;
        }

        /// <summary>
        /// The size of the picture this ends up in: for a pass, the largest of
        /// what reads it, through any passes between; for an element, its own.
        /// A pass's scale is a fraction of this, so each pass in a chain says
        /// its own size rather than compounding the ones after it.
        /// </summary>
        protected virtual bool TryGetPictureSize(out int w, out int h) {
            // Read by nothing, and drawn by a Step of its own: what it draws is
            // the picture, and only a resolution says how big.
            if (_readers.Count == 0) return TryGetSetSize(out w, out h);
            w = h = 0;
            foreach (var reader in _readers) {
                if (!reader.TryGetPictureSize(out int rw, out int rh)) continue;
                w = Mathf.Max(w, rw);
                h = Mathf.Max(h, rh);
            }
            return w > 0 && h > 0;
        }

        /// <summary>
        /// A new target to draw into. A pass's is raw, like the previous frame:
        /// 16 bits a channel, linear, so what the next pass reads is what this
        /// program wrote, values over 1 included.
        /// </summary>
        protected virtual RenderTexture CreateTarget(int w, int h) =>
            CreateRaw(w, h, $"OneJS_ShaderPass_{w}x{h}");

        /// <summary>
        /// Whether a frame kept as the previous one is also copied into the
        /// target. An element shows its target; a pass is read from wherever
        /// the frame is, so it needs no copy, and no target at all then.
        /// </summary>
        protected virtual bool ShowsTarget => false;

        /// <summary>The target is new, or gone (null).</summary>
        protected virtual void TargetChanged(RenderTexture target) { }

        /// <summary>Something drawn or set that the picture shows.</summary>
        protected virtual void Repaint() { }

        /// <summary>Whether this can draw now. An element needs a panel, unless stepped at a set resolution.</summary>
        protected virtual bool CanDraw(bool stepping) => true;

        /// <summary>What warnings call this.</summary>
        protected virtual string Describe() => $"the pass drawing {Drawing}";

        /// <summary>What this draws, for a warning: a program by its hash, or a shader by name.</summary>
        protected string Drawing => _isProgram ? $"program {_programHash}" : $"\"{_shaderName}\"";

        /// <summary>Whether a resolution is set, rather than taken from the layout or a reader.</summary>
        protected bool HasResolution => _resW > 0 && _resH > 0;

        // MARK: JS-facing API (each call is a single interop crossing)

        /// <summary>Shader to run, e.g. "OneJS/Fire". Resolved through Resources so it survives builds.</summary>
        public void SetShader(string shaderName) {
            if (_shaderName == shaderName) return;
            _shaderName = shaderName;
            _shaderMissing = false;
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
        }

        int _programHandle = -1;
        static readonly int s_Res = Shader.PropertyToID("_Res");
        static readonly int s_Prev = Shader.PropertyToID("_Prev");

        /// <summary>
        /// True on a OneJS that draws a program with no VM encoding, which is
        /// what onejs-sl's compile() produces. A host reads it before sending
        /// such a program, so an older OneJS gets a message naming the version
        /// it needs rather than a refused empty buffer.
        /// </summary>
        public bool AcceptsCompiledPrograms => true;

        /// <summary>
        /// True on a OneJS that steps a program: keeps the frame it drew before
        /// for one that reads `previous`, and hands every program `frame` and
        /// `deltaTime`. A host reads it before sending a program that reads any
        /// of them, so an older OneJS gets a message rather than a program
        /// that draws its first frame forever.
        /// </summary>
        public bool AcceptsSteppedPrograms => true;

        /// <summary>
        /// True on a OneJS that has passes, and reads one through
        /// <see cref="SetProgramPass"/>. A host reads it before binding one, so
        /// an older OneJS gets a message rather than a missing method.
        /// </summary>
        public bool AcceptsPasses => true;

        /// <remarks>
        /// `dataObj`, `instructionCount`, `resultRegister` and `wire` are
        /// ignored. They carried the shader language VM's encoding, which OneJS
        /// no longer runs, and stay so an onejs-react that still sends them
        /// binds its programs.
        /// </remarks>
        /// <returns>
        /// True when the host should follow up with <see cref="RecordProgram"/>:
        /// the program has no compiled shader and an editor is attached that can
        /// generate one. False everywhere else, so JS never emits HLSL in Play.
        /// </returns>
        public bool SetProgram(object dataObj, int instructionCount, int resultRegister, string hash,
                               object uniformNamesObj = null, int wire = 1) {
            // Same program, same everything. Rebuilding would drop the render
            // target and restart the clock on every React render.
            if (_programHash == hash && _isProgram && SL.SLProgramBridge.Exists(_programHandle)) return false;
            _isProgram = true;
            _programHash = hash;
            _shaderMissing = false;
            // A new program starts on a clear previous frame at frame 0.
            _cleared = true;
            ReleaseProgram();
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
            try {
                _material = SL.SLProgramBridge.CreateMaterial(
                    hash, out var native, out _programHandle, ToStrings(uniformNamesObj));
                // Null until there is a generated shader, and always in a WebGL
                // player, where the page draws the program.
                if (_material != null) _material.hideFlags = HideFlags.HideAndDontSave;
                return !native && SL.SLProgramBridge.WantsSource(hash);
            } catch (System.Exception e) {
                _shaderMissing = true;
                // Expected failure: CreateMaterial throws to say the program's shader
                // was not generated, and its message names the fix.
                Debug.LogWarning($"[OneJS sl] {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// The program's HLSL, sent only after <see cref="SetProgram"/> asked for
        /// it. The editor records it and generates the shader, and the pass
        /// picks up a material on it on its next draw.
        /// </summary>
        public void RecordProgram(string hash, string hlsl) {
            SL.SLProgramBridge.RecordSource(hash, hlsl);
        }

        /// <summary>
        /// True when <see cref="SetProgramWeb"/> would be used: a WebGL player
        /// whose page can draw compiled programs. Asked once per program, so a
        /// host sends the two sources only where they run.
        /// </summary>
        public bool WantsWebSource => _programHandle >= 0 && SL.SLWeb.Available;

        /// <summary>
        /// The program as WGSL and GLSL ES. A WebGL player compiles the one its
        /// device speaks and draws it once it is ready; nothing is drawn until
        /// then, and for good if it fails to compile.
        /// </summary>
        public void SetProgramWeb(string wgsl, string glsl) {
            if (_programHandle < 0) return;
            SL.SLProgramBridge.SetWebSource(_programHandle, wgsl, glsl);
        }

        /// <summary>
        /// Changes nothing. False once asked a WebGL player to draw on the VM
        /// rather than compiled; it stays so an onejs-react that still calls it
        /// keeps working.
        /// </summary>
        public void SetCompiled(bool allowed) { }

        /// <summary>True when the program draws compiled: through a generated shader, or the page.</summary>
        public bool IsCompiled => _programHandle >= 0 && SL.SLProgramBridge.IsCompiled(_programHandle);

        /// <summary>
        /// Sets one of the program's uniforms, by the slot the compiler gave it.
        /// </summary>
        /// <remarks>
        /// By SLOT, not by name. The page's program reads its uniforms by slot
        /// and a generated shader by name, so SLProgramBridge.SetUniform maps
        /// the slot to the name the program gave it.
        /// </remarks>
        public void SetUniform(int slot, float x, float y, float z, float w) {
            if (_programHandle < 0) return;
            SL.SLProgramBridge.SetUniform(_programHandle, slot, x, y, z, w);
            Repaint();
        }

        /// <summary>
        /// Sets one of the program's textures, by the slot the compiler gave it,
        /// in place of whatever was there, a pass included.
        /// </summary>
        /// <remarks>
        /// By SLOT, for the same reason uniforms are. A program's textures are
        /// _Tex0 to _Tex7 on every backend, so a host handed the name an author
        /// wrote ("grain") set a material property nothing declares and bound
        /// nothing at all, in the browser and after an eject alike.
        /// </remarks>
        public void SetProgramTexture(int slot, Texture tex) {
            if (_programHandle < 0 || tex == null) return;
            Unbind(slot);
            SL.SLProgramBridge.SetTexture(_programHandle, slot, tex);
            Repaint();
        }

        /// <summary>The same, from the name of one of the built-in procedural textures.</summary>
        public void SetProgramBuiltinTexture(int slot, string builtin) {
            var tex = ShaderEffectBridge.GetBuiltinTexture(builtin);
            if (tex != null) SetProgramTexture(slot, tex);
        }

        /// <summary>
        /// Reads a pass at one of the program's texture slots: the pass draws
        /// first, every frame this draws, and the slot reads what it drew.
        /// </summary>
        /// <returns>
        /// False, with an error naming both, for a pass that reads this one,
        /// itself or through others: neither could be drawn first. A program
        /// reads what it drew the frame before through `previous`.
        /// </returns>
        public bool SetProgramPass(int slot, ShaderPass pass) {
            if (_programHandle < 0 || pass == null || pass._disposed) return false;
            if (pass == this || pass.Reads(this)) {
                Debug.LogError($"[OneJS sl] {Describe()} cannot read {pass.Describe()} at texture slot {slot}: " +
                               $"{(pass == this ? "that is itself" : "it reads this one back")}, so neither could be drawn first. " +
                               "To read what a program drew the frame before, read `previous`.");
                return false;
            }
            if (_inputs.TryGetValue(slot, out var bound) && bound == pass) return true;
            // Black until the pass draws; and a slot past the program's eight
            // throws here, saying so, rather than in a later tick.
            Unbind(slot);
            SL.SLProgramBridge.SetTexture(_programHandle, slot, Texture2D.blackTexture);
            _inputs[slot] = pass;
            pass._readers.Add(this);
            Repaint();
            return true;
        }

        /// <summary>Whether this reads `pass`, directly or through other passes.</summary>
        bool Reads(ShaderPass pass) {
            foreach (var input in _inputs.Values) {
                if (input == pass || input.Reads(pass)) return true;
            }
            return false;
        }

        /// <summary>
        /// Stops reading the pass at `slot`. The slot reads black until it is
        /// bound again, rather than a texture the pass may be about to destroy.
        /// </summary>
        void Unbind(int slot) {
            if (!_inputs.TryGetValue(slot, out var input)) return;
            _inputs.Remove(slot);
            input._readers.Remove(this);
            if (SL.SLProgramBridge.Exists(_programHandle)) SL.SLProgramBridge.SetTexture(_programHandle, slot, Texture2D.blackTexture);
        }

        void UnbindAll() {
            foreach (var input in _inputs.Values) input._readers.Remove(this);
            _inputs.Clear();
        }

        /// <summary>Uniform names in slot order, as they arrive from JS.</summary>
        static string[] ToStrings(object obj) {
            if (obj == null) return null;
            if (obj is string[] s) return s;
            if (obj is System.Collections.IEnumerable e) {
                var list = new List<string>();
                foreach (var item in e) list.Add(item?.ToString());
                return list.ToArray();
            }
            return null;
        }

        public void SetFloat(string name, float value) => _floats[name] = value;
        public void SetVector(string name, float x, float y, float z, float w) => _vectors[name] = new Vector4(x, y, z, w);
        public void SetColor(string name, float r, float g, float b, float a) => _vectors[name] = new Vector4(r, g, b, a);
        public void SetTexture(string name, Texture tex) => _textures[name] = tex;

        /// <summary>
        /// Sets a float4 array uniform from a flat array of 4*n floats. Layer stacks
        /// cross as one flat array rather than n separate calls, so a whole effect
        /// description is a couple of crossings regardless of how many layers it has.
        /// </summary>
        public void SetVectorArray(string name, object flatObj) {
            // Takes object, not float[]: a JS array arrives as the
            // {__csArray, __csArrayType:"float"} marker, which does not bind to a
            // float[] parameter and makes the whole method invisible to reflection
            // ("Method not found"). Same conversion PainterBridge and StyleBridge use.
            var flat = QuickJSNative.ConvertToTargetType(flatObj, typeof(float[])) as float[];
            if (flat == null || flat.Length == 0 || flat.Length % 4 != 0) {
                Debug.LogWarning($"[OneJS ShaderFX] \"{name}\" needs a flat array of 4 floats per element, got {flat?.Length ?? 0}.");
                return;
            }
            int n = flat.Length / 4;
            var arr = new Vector4[n];
            for (int i = 0; i < n; i++)
                arr[i] = new Vector4(flat[i * 4], flat[i * 4 + 1], flat[i * 4 + 2], flat[i * 4 + 3]);
            _vectorArrays[name] = arr;
        }

        /// <summary>Named built-in procedural texture, so effects need ship no assets.</summary>
        public void SetBuiltinTexture(string name, string builtin) {
            var tex = ShaderEffectBridge.GetBuiltinTexture(builtin);
            if (tex != null) _textures[name] = tex;
        }

        /// <summary>Builds a 256x1 gradient from evenly spaced RGBA stops and binds it.</summary>
        public void SetRamp(string name, float[] rgba) {
            var tex = ShaderEffectBridge.BuildRamp(rgba);
            if (tex != null) _textures[name] = tex;
        }

        /// <summary>Render resolution. Pass 0,0 to follow the layout, or for a pass, what reads it.</summary>
        public void SetResolution(int w, int h) {
            _resW = w;
            _resH = h;
        }

        /// <summary>
        /// A fraction of the size of the picture a pass ends up in: 0.5 draws
        /// half as wide and half as high. 1 by default; a set resolution ignores it.
        /// </summary>
        public float Scale {
            get => _scale;
            set => _scale = value > 0f ? value : 1f;
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        /// <summary>
        /// Resets the clock, so a restarted effect looks the same every time. A
        /// program's previous frame clears and its count restarts at 0. The
        /// passes this reads reset with it.
        /// </summary>
        public void ResetTime() {
            _seconds = 0f;
            _cleared = true;
            foreach (var input in _inputs.Values) input.ResetTime();
        }

        /// <summary>
        /// Sets the clock. The next frame draws at exactly this time, even while
        /// paused, so a paused effect shows the frame chosen rather than one a
        /// frame's delta away from it. A seek, not a step: a program's previous
        /// frame clears and that frame is frame 0. The passes this reads seek
        /// with it.
        /// </summary>
        public void SetTime(float seconds) {
            _seconds = seconds;
            _drawAtSetTime = true;
            _cleared = true;
            foreach (var input in _inputs.Values) input.SetTime(seconds);
        }

        /// <summary>
        /// Draws exactly one frame, paused or not, `dt` seconds after the last,
        /// with `deltaTime` `dt` and the frame count one higher. After a clear
        /// (a new program, a new size, <see cref="SetTime"/>) the step draws
        /// frame 0 at the clock's time instead, so SetTime(0) then n steps of dt
        /// draw frames 0 to n - 1 at 0, dt, 2dt: the same frames on every run,
        /// which is what a recorder or a test needs and a live clock cannot give.
        /// The passes this reads step with it, once each.
        /// </summary>
        public void Step(float dt) {
            ShaderEffectBridge.BeginDraw();
            StepFrame(dt);
        }

        void StepFrame(float dt) {
            if (!(dt > 0f) || _shaderMissing) return;
            DrawInputs(dt, stepping: true);
            if (!_cleared) _seconds += dt;
            Draw(_cleared ? 0f : dt, stepping: true);
        }

        /// <summary>The frames drawn since the previous frame was last cleared: a program's `frame`.</summary>
        public int Frame => _frame;

        public bool IsReady => (_material != null || (_isProgram && SL.SLProgramBridge.Exists(_programHandle))) && _rtW > 0;
        public int RenderWidth => _rtW;
        public int RenderHeight => _rtH;

        /// <summary>
        /// The texture holding the last frame drawn, which is what a reader
        /// samples: the target, or the half of the previous frames it went into.
        /// Null before the first.
        /// </summary>
        public Texture Output => _keeps ? (_history != null ? _history[_current] : null) : _rt;

        /// <summary>The target this draws into, which an element shows. Null until the first draw.</summary>
        internal RenderTexture Target => _rt;

        /// <summary>The size this draws at: the resolution set, or the natural size, clamped.</summary>
        internal bool TryGetSize(out int w, out int h) {
            if (TryGetSetSize(out w, out h)) return true;
            if (!TryGetNaturalSize(out w, out h)) return false;
            w = Mathf.Clamp(w, MinRes, MaxRes);
            h = Mathf.Clamp(h, MinRes, MaxRes);
            return true;
        }

        /// <summary>The resolution set, clamped. False when none is.</summary>
        bool TryGetSetSize(out int w, out int h) {
            w = Mathf.Clamp(_resW, MinRes, MaxRes);
            h = Mathf.Clamp(_resH, MinRes, MaxRes);
            return HasResolution;
        }

        // MARK: frame

        /// <summary>Advances the clock by `dt` and draws, after the passes this reads.</summary>
        internal void Tick(float dt) {
            if ((_paused && !_drawAtSetTime) || _shaderMissing) return;
            DrawInputs(dt, stepping: false);
            if (_drawAtSetTime) dt = 0f;
            else _seconds += dt;
            Draw(dt, stepping: false);
        }

        /// <summary>
        /// Draws every pass this reads that has not drawn in this draw yet, the
        /// same way this is drawn, so a pass read twice draws once and every
        /// reader sees this frame's picture.
        /// </summary>
        void DrawInputs(float dt, bool stepping) {
            if (_inputs.Count == 0 || !CanDraw(stepping)) return;
            int draw = ShaderEffectBridge.Draws;
            foreach (var input in _inputs.Values) {
                if (input._drawnIn == draw) continue;
                input._drawnIn = draw;
                if (stepping) input.StepFrame(dt);
                else input.Tick(dt);
            }
        }

        /// <summary>
        /// Draws at the clock's time. `dt` is how far the clock just moved: more
        /// than 0 advances a program to its next frame, 0 draws the last frame
        /// again. The clock has already moved.
        /// </summary>
        void Draw(float dt, bool stepping) {
            if (!_isProgram && string.IsNullOrEmpty(_shaderName)) return;
            if (!CanDraw(stepping)) return;
            if (!EnsureMaterial()) return;
            // A program with no compiled shader yet has no material, and gets one
            // when the editor has generated it, so it is asked for every frame
            // until then. Asked before anything else, since the shader is what
            // says whether the program reads the frame before.
            if (_material == null && _isProgram) _material = SL.SLProgramBridge.CurrentMaterial(_programHandle);
            bool readsPrevious = _isProgram && SL.SLProgramBridge.ReadsPrevious(_programHandle);
            if (!EnsureTarget(needsTarget: ShowsTarget || !readsPrevious)) return;
            _unstepped += dt;
            // A new pair of halves is a clear, so this comes before the plan.
            bool keeps = readsPrevious && EnsureHistory();

            // Which frame this is, committed only once it is drawn: a program
            // the page is still compiling draws nothing, and its clock runs on.
            int frame, into;
            float step;
            if (_cleared) { frame = 0; step = 0f; into = 0; }
            else if (_unstepped > 0f) { frame = _frame + 1; step = _unstepped; into = 1 - _current; }
            else { frame = _frame; step = _step; into = _current; }
            // The program draws its raw result into its history when it reads
            // the frame before, and straight into the target otherwise. Frame 0
            // reads transparent black, which is what a clear previous frame is,
            // rather than a half cleared first: in a WebGPU player Unity submits
            // its own clear at the end of the frame, after the page has drawn,
            // and would wipe the frame drawn over it.
            var drawInto = keeps ? _history[into] : _rt;
            if (drawInto == null) return;
            Texture previous = !keeps ? null : frame == 0 ? Texture2D.blackTexture : _history[1 - into];

            // What the passes this reads drew, this frame. Bound every frame:
            // a pass keeping its previous frame alternates between two halves,
            // and a resized one has a new texture.
            foreach (var kv in _inputs) {
                SL.SLProgramBridge.SetTexture(_programHandle, kv.Key, kv.Value.Output ?? Texture2D.blackTexture);
            }

            // Compiled by the page in a WebGL player, into the same target at the
            // same point in the frame a material would draw.
            if (_isProgram && SL.SLProgramBridge.TryRenderCompiled(_programHandle, drawInto, _seconds, frame, step, previous)) {
                // The page wrote every pixel the target shows, or the history
                // Drew copies in did, so a new target needs no clear.
                _targetIsNew = false;
                Drew(frame, step, into, keeps);
                return;
            }
            ClearIfNew();
            // Nothing to draw until then, and the target is clear.
            if (_material == null) return;
            _material.SetFloat("_Secs", _seconds);
            // Never flipped. A Blit into a render target already puts v = 0 on
            // texel row 0 on every API, and UI Toolkit shows that row at the
            // bottom, so uv.y = 0 is the BOTTOM of the element everywhere. This
            // used to flip wherever graphicsUVStartsAtTop is false, which drew
            // every effect upside down on WebGL2 (#127) while D3D, Metal and
            // WebGPU looked right. FxBridge has always blitted with 0.
            _material.SetFloat("_FlipY", 0f);
            // SDF shapes are drawn in a centred, aspect corrected space so a circle
            // stays round on a non square element. Without this every shape stretches
            // with the element, which is fine for a noise field and wrong for an outline.
            _material.SetFloat("_Aspect", _rtH > 0 ? _rtW / (float)_rtH : 1f);
            // A program's resolution, fragCoord and aspect inputs read the
            // target size from _Res. Nothing set it here, so every program in
            // an element saw a 1x1 target: aspect 1, fragCoord equal to uv.
            // Its frame count and step ride in _Res's spare zw.
            if (_isProgram) _material.SetVector(s_Res, new Vector4(_rtW, _rtH, frame, step));
            if (keeps) _material.SetTexture(s_Prev, previous);

            foreach (var kv in _floats) _material.SetFloat(kv.Key, kv.Value);
            foreach (var kv in _vectors) _material.SetVector(kv.Key, kv.Value);
            foreach (var kv in _vectorArrays) _material.SetVectorArray(kv.Key, kv.Value);
            foreach (var kv in _textures) if (kv.Value != null) _material.SetTexture(kv.Key, kv.Value);

            var active = RenderTexture.active;
            Graphics.Blit(null, drawInto, _material, 0);
            RenderTexture.active = active;
            Drew(frame, step, into, keeps);
        }

        /// <summary>
        /// A frame was drawn: it is now the last one, and a program keeping its
        /// frames, in an element, is shown through the display pass, a plain
        /// copy into the target that stores it as any program's result is stored.
        /// </summary>
        void Drew(int frame, float step, int into, bool keeps) {
            _frame = frame;
            _step = step;
            _current = into;
            _keeps = keeps;
            _unstepped = 0f;
            _cleared = false;
            _drawAtSetTime = false;
            if (keeps && ShowsTarget) {
                var active = RenderTexture.active;
                Graphics.Blit(_history[into], _rt);
                RenderTexture.active = active;
            }
            Repaint();
        }

        /// <summary>
        /// A raw texture: 16 bits a channel, linear, bilinear and clamped, as a
        /// program's previous frame and a pass are kept.
        /// </summary>
        RenderTexture CreateRaw(int w, int h, string name) {
            var format = RenderTextureFormat.ARGBHalf;
            if (!SystemInfo.SupportsRenderTextureFormat(format)) {
                // Every desktop and WebGPU device renders half floats, and a
                // WebGL2 one needs EXT_color_buffer_float. Without it a slow fade
                // stalls on 8 bit steps, which is still a picture.
                format = RenderTextureFormat.ARGB32;
                if (!s_WarnedRawFormat) {
                    s_WarnedRawFormat = true;
                    Debug.LogWarning($"[OneJS sl] this device cannot render 16 bit float targets, so {Describe()} keeps " +
                                     "its previous frame and passes at 8 bits a channel: a slow fade will step, and nothing passes on above 1.");
                }
            }
            var rt = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear) {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                // Read texel for texel, by the program and by the display
                // copy, so never anisotropic. The default 1 becomes 9 under
                // Quality Settings' Forced On, and a renderer whose
                // anisotropic path is inexact at a 1:1 read (Mesa's llvmpipe,
                // up to a fifth of a texel) then blends in a neighbour every
                // frame, so a program that moves or fades its history drifts.
                // Only 0 is exempt from Forced On.
                anisoLevel = 0,
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// The two halves of the history at the drawing size. A new pair is a
        /// clear, and needs none, since frame 0 never reads a half. False when
        /// there is no size yet.
        /// </summary>
        bool EnsureHistory() {
            if (_rtW <= 0) return false;
            if (_history != null && _history[0].width == _rtW && _history[0].height == _rtH) {
                RenderTextureUtils.EnsureCreated(_history[0]);
                RenderTextureUtils.EnsureCreated(_history[1]);
                return true;
            }
            ReleaseHistory();
            _history = new RenderTexture[2];
            for (int i = 0; i < 2; i++) _history[i] = CreateRaw(_rtW, _rtH, $"OneJS_ShaderFX_Previous{i}_{_rtW}x{_rtH}");
            _cleared = true;
            return true;
        }

        void ReleaseHistory() {
            if (_history == null) return;
            foreach (var h in _history) {
                h.Release();
                UnityEngine.Object.DestroyImmediate(h);
            }
            _history = null;
            _keeps = false;
        }

        bool EnsureMaterial() {
            if (_material != null) return true;
            // A program's material comes from SetProgram or, once its shader is
            // generated, CurrentMaterial. Reaching here with none means it is
            // waiting for that, the page draws it, or SetProgram failed and
            // already said why.
            if (_isProgram) return SL.SLProgramBridge.Exists(_programHandle);
            var shader = Resources.Load<Shader>(_shaderName);
            if (shader == null || !shader.isSupported) {
                _shaderMissing = true;
                Debug.LogWarning($"[OneJS ShaderFX] shader \"{_shaderName}\" not found under Resources or unsupported; " +
                                 "the effect will not render.");
                return false;
            }
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        /// <summary>
        /// The size to draw at, and the target to draw into when one is needed:
        /// a pass keeping its previous frame draws into that and needs none.
        /// False means there is no size yet, so the caller must try again later.
        /// </summary>
        bool EnsureTarget(bool needsTarget) {
            if (!TryGetSize(out int w, out int h)) return false;
            if (_rtW != w || _rtH != h) {
                ReleaseTarget();
                // A new size clears a program's previous frame: it is not resampled.
                _cleared = true;
                _rtW = w;
                _rtH = h;
            }
            if (!needsTarget) return true;
            if (_rt != null) {
                // The device can drop the texture without its size changing, and
                // UI Toolkit samples an element's as a backgroundImage.
                RenderTextureUtils.EnsureCreated(_rt);
                return true;
            }
            _rt = CreateTarget(w, h);
            _targetIsNew = true;
            TargetChanged(_rt);
            return true;
        }

        /// <summary>
        /// Clears a target <see cref="EnsureTarget"/> has just made, whose
        /// contents are undefined, when Unity rather than the page is the first
        /// to write it: before a material draws it, or when nothing does, as
        /// while the page is still compiling a program.
        ///
        /// Not done when the target is made (#131). In a WebGPU player Unity
        /// submits its commands at the end of the frame and the page submits
        /// its draw at once, so a clear issued with the target reached the GPU
        /// after the page's draw and wiped the frame. A page's draw writes
        /// every pixel, so a target it draws first needs no clear; a material
        /// still gets one just before its first draw, as it always has.
        /// </summary>
        void ClearIfNew() {
            if (!_targetIsNew || _rt == null) return;
            _targetIsNew = false;
            var active = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
        }

        /// <summary>Frees the target and the previous frames. The next draw makes them again.</summary>
        internal void ReleaseTarget() {
            ReleaseHistory();
            _rtW = _rtH = 0;
            if (_rt == null) return;
            TargetChanged(null);
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
            _rt = null;
        }

        /// <summary>
        /// Drops the program, which owns the material and its program texture,
        /// and the passes it read.
        /// </summary>
        void ReleaseProgram() {
            UnbindAll();
            if (_programHandle < 0) return;
            SL.SLProgramBridge.Release(_programHandle);
            _programHandle = -1;
            // Released with the program. Leaving it set would have this
            // destroy an object the bridge has already destroyed.
            _material = null;
        }

        /// <summary>Frees the target and material, and leaves what read this reading nothing. Safe to call twice.</summary>
        public virtual void Dispose() {
            _disposed = true;
            ShaderEffectBridge.UnregisterPass(this);
            foreach (var reader in _readers.ToArray()) {
                foreach (var kv in new List<KeyValuePair<int, ShaderPass>>(reader._inputs)) {
                    if (kv.Value == this) reader.Unbind(kv.Key);
                }
            }
            ReleaseTarget();
            ReleaseProgram();
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
