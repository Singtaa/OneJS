using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.ShaderFX {
    /// <summary>
    /// A UI element whose background is generated every frame by a shader.
    ///
    /// The effect is blitted into a RenderTexture and shown through
    /// style.backgroundImage, rather than assigned to the element as a
    /// unityMaterial. That choice matters:
    ///
    ///  - the shader is an ordinary unlit shader, not a UI Toolkit one, so it
    ///    keeps full control of its fragment and does not depend on the engine's
    ///    private UnityUIE.cginc entry points;
    ///  - the element stays a normal UI element, so border-radius, clipping,
    ///    opacity and antialiasing all still work on it. (Assigning a custom
    ///    unityMaterial costs an element its analytic AA: see the particle
    ///    engine's host-styling warning.)
    ///
    /// It is deliberately generic: any shader, any float/vector/colour/texture
    /// property. Effects like fire are a shader plus a thin JS wrapper, not a new
    /// C# class, so adding one costs no engine code.
    ///
    /// Ticked by ShaderEffectBridge.TickAll from QuickJSUIBridge.Tick, which
    /// covers play mode, edit-mode preview and JSPad through one integration point.
    /// </summary>
    [UxmlElement]
    public partial class ShaderEffectElement : VisualElement {
        const int MinRes = 8;
        const int MaxRes = 2048;

        Material _material;
        RenderTexture _rt;
        string _shaderName;
        string _programHash;
        bool _isProgram;
        bool _shaderMissing;

        // Pending property values, applied to the material before each blit so JS
        // can set them before the shader/material exists.
        readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        readonly Dictionary<string, Vector4> _vectors = new Dictionary<string, Vector4>();
        readonly Dictionary<string, Vector4[]> _vectorArrays = new Dictionary<string, Vector4[]>();
        readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();

        int _resW, _resH;      // 0 = follow the element's layout
        int _rtW, _rtH;
        float _seconds;
        bool _paused;
        bool _drawAtSetTime;   // SetTime's frame is still to be drawn, paused or not
        bool _paintingOnLayout;
        bool _targetIsNew;     // the target's contents are undefined until its first draw or ClearIfNew

        // A program drawn frame after frame (Specs/SL_NEXT.md 4). A frame is
        // one step of this element's clock: the last result becomes the
        // previous frame, the count goes up by one and the step is the time
        // moved. A draw at the same time draws the same frame again. A clear
        // makes the next frame frame 0, on a transparent black previous frame.
        RenderTexture[] _history;  // the raw frames, only for a program that reads the previous one
        int _current;              // the half of _history holding the last frame drawn
        int _frame;                // frames since the last clear
        float _step;               // the seconds from the frame before to this one
        float _unstepped;          // time moved since the last frame drawn, which the next one steps by
        bool _cleared = true;      // the next frame is frame 0
        static bool s_WarnedHistoryFormat;

        /// <summary>
        /// The context that created this, so tearing that context down disposes
        /// it without touching another JSRunner's. 0 when made outside a JS call.
        /// </summary>
        internal int OwnerContextId { get; set; }

        public ShaderEffectElement() {
            pickingMode = PickingMode.Ignore;
            ShaderEffectBridge.Register(this);
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseTexture());
            RegisterCallback<GeometryChangedEvent>(_ => PaintOnLayout());
        }

        /// <summary>
        /// Paints as soon as the element has a rect, rather than waiting for the
        /// next bridge tick.
        ///
        /// A freshly attached element has no layout yet, so the first Tick cannot
        /// size a render target and leaves the background empty. Deferring to "the
        /// next tick" is fine in play mode, but edit-mode preview ticks from
        /// EditorApplication.update, which the editor throttles hard while
        /// unfocused: the gap stretches from a frame to seconds, and every hot
        /// reload reads as a broken effect. Painting on the layout pass that
        /// produced the rect removes the dependency on tick cadence entirely.
        /// </summary>
        void PaintOnLayout() {
            // EnsureTarget assigns backgroundImage, which re-dirties layout, and an
            // auto-sized element measures against its own background. Without this
            // guard that feedback could re-enter here from the pass it triggers.
            if (_paintingOnLayout) return;
            if (!TryGetTargetSize(out int w, out int h)) return;
            if (_rt != null && _rtW == w && _rtH == h) return; // already the right size
            _paintingOnLayout = true;
            // dt 0: this is a catch-up paint, not a frame, so the effect clock must
            // not jump just because the element was resized.
            try { Tick(0f); } finally { _paintingOnLayout = false; }
        }

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

        /// <summary>
        /// Runs a shader language program instead of a named shader.
        ///
        /// The element already owns a render target, a clock, the layout driven
        /// resize and the backgroundImage plumbing, so a program reuses all of
        /// it rather than getting a parallel element with the same machinery and
        /// its own bugs.
        ///
        /// SLProgramBridge finds the shader generated from this program. Until
        /// one exists the element draws nothing; the editor generates it the
        /// first time it sees the program, and a player ships it.
        /// </summary>
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
        /// it. The editor records it and generates the shader, and the element
        /// picks up a material on it on its next tick.
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
        /// device speaks and draws it once it is ready; the element draws
        /// nothing until then, and for good if it fails to compile.
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
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Sets one of the program's textures, by the slot the compiler gave it.
        /// </summary>
        /// <remarks>
        /// By SLOT, for the same reason uniforms are. A program's textures are
        /// _Tex0 to _Tex3 on every backend, so a host handed the name an author
        /// wrote ("grain") set a material property nothing declares and bound
        /// nothing at all, in the browser and after an eject alike.
        /// </remarks>
        public void SetProgramTexture(int slot, Texture tex) {
            if (_programHandle < 0 || tex == null) return;
            SL.SLProgramBridge.SetTexture(_programHandle, slot, tex);
            MarkDirtyRepaint();
        }

        /// <summary>The same, from the name of one of the built-in procedural textures.</summary>
        public void SetProgramBuiltinTexture(int slot, string builtin) {
            var tex = ShaderEffectBridge.GetBuiltinTexture(builtin);
            if (tex != null) SetProgramTexture(slot, tex);
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

        /// <summary>Render resolution. Pass 0,0 to follow the element's layout size.</summary>
        public void SetResolution(int w, int h) {
            _resW = w;
            _resH = h;
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;
        /// <summary>
        /// Resets the effect clock, so a restarted effect looks the same every
        /// time. A program's previous frame clears and its count restarts at 0.
        /// </summary>
        public void ResetTime() {
            _seconds = 0f;
            _cleared = true;
        }

        /// <summary>
        /// Sets the effect clock. The next frame draws at exactly this time, even
        /// while paused, so a paused effect shows the frame chosen rather than
        /// one a frame's delta away from it. A seek, not a step: a program's
        /// previous frame clears and that frame is frame 0.
        /// </summary>
        public void SetTime(float seconds) {
            _seconds = seconds;
            _drawAtSetTime = true;
            _cleared = true;
        }

        /// <summary>
        /// Draws exactly one frame, paused or not, `dt` seconds after the last,
        /// with `deltaTime` `dt` and the frame count one higher. After a clear
        /// (a new program, a new size, <see cref="SetTime"/>) the step draws
        /// frame 0 at the clock's time instead, so SetTime(0) then n steps of dt
        /// draw frames 0 to n - 1 at 0, dt, 2dt: the same frames on every run,
        /// which is what a recorder or a test needs and a live clock cannot give.
        /// Draws with no panel when the element has an explicit resolution.
        /// </summary>
        public void Step(float dt) {
            if (!(dt > 0f) || _shaderMissing) return;
            if (!_cleared) _seconds += dt;
            Draw(_cleared ? 0f : dt, stepping: true);
        }

        /// <summary>The frames drawn since the previous frame was last cleared: a program's `frame`.</summary>
        public int Frame => _frame;

        public bool IsReady => (_material != null || (_isProgram && SL.SLProgramBridge.Exists(_programHandle))) && _rt != null;
        public int RenderWidth => _rtW;
        public int RenderHeight => _rtH;

        // MARK: frame

        internal void Tick(float dt) {
            if ((_paused && !_drawAtSetTime) || _shaderMissing) return;
            if (_drawAtSetTime) dt = 0f;
            else _seconds += dt;
            Draw(dt, stepping: false);
        }

        /// <summary>
        /// Draws at the clock's time. `dt` is how far the clock just moved: more
        /// than 0 advances a program to its next frame, 0 draws the last frame
        /// again. The clock has already moved.
        /// </summary>
        void Draw(float dt, bool stepping) {
            if (!_isProgram && string.IsNullOrEmpty(_shaderName)) return;
            if (panel == null && !(stepping && _resW > 0 && _resH > 0)) return;
            if (!EnsureMaterial()) return;
            if (!EnsureTarget()) return;
            _unstepped += dt;
            // A program with no compiled shader yet has no material, and gets one
            // when the editor has generated it, so it is asked for every frame
            // until then. Asked before anything else, since the shader is what
            // says whether the program reads the frame before.
            if (_material == null && _isProgram) _material = SL.SLProgramBridge.CurrentMaterial(_programHandle);
            // A new pair of halves is a clear, so this comes before the plan.
            bool keeps = _isProgram && SL.SLProgramBridge.ReadsPrevious(_programHandle) && EnsureHistory();

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
            Texture previous = !keeps ? null : frame == 0 ? Texture2D.blackTexture : _history[1 - into];

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
        /// frames shows it through the display pass, a plain copy into the
        /// target that stores it as any program's result is stored.
        /// </summary>
        void Drew(int frame, float step, int into, bool keeps) {
            _frame = frame;
            _step = step;
            _current = into;
            _unstepped = 0f;
            _cleared = false;
            _drawAtSetTime = false;
            if (keeps) {
                var active = RenderTexture.active;
                Graphics.Blit(_history[into], _rt);
                RenderTexture.active = active;
            }
            // The draw command already references this texture, so the new contents
            // appear without re-tessellating, but edit-mode preview only repaints
            // dirty elements, so ask for one.
            MarkDirtyRepaint();
        }

        /// <summary>
        /// The two halves of the history at the target's size: 16 bits a
        /// channel, linear, bilinear and clamped. A new pair is a clear, and
        /// needs none, since frame 0 never reads a half. False when there is
        /// no target yet.
        /// </summary>
        bool EnsureHistory() {
            if (_rt == null) return false;
            if (_history != null && _history[0].width == _rtW && _history[0].height == _rtH) {
                RenderTextureUtils.EnsureCreated(_history[0]);
                RenderTextureUtils.EnsureCreated(_history[1]);
                return true;
            }
            ReleaseHistory();
            var format = RenderTextureFormat.ARGBHalf;
            if (!SystemInfo.SupportsRenderTextureFormat(format)) {
                // Every desktop and WebGPU device renders half floats, and a
                // WebGL2 one needs EXT_color_buffer_float. Without it a slow fade
                // stalls on 8 bit steps, which is still a picture.
                format = RenderTextureFormat.ARGB32;
                if (!s_WarnedHistoryFormat) {
                    s_WarnedHistoryFormat = true;
                    Debug.LogWarning($"[OneJS sl] this device cannot render 16 bit float targets, so the previous frame " +
                                     $"of \"{name}\" is kept at 8 bits a channel and a slow fade will step.");
                }
            }
            _history = new RenderTexture[2];
            for (int i = 0; i < 2; i++) {
                _history[i] = new RenderTexture(_rtW, _rtH, 0, format, RenderTextureReadWrite.Linear) {
                    name = $"OneJS_ShaderFX_Previous{i}_{_rtW}x{_rtH}",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                _history[i].Create();
            }
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
        /// Resolves the render size in px. False means layout has not run yet, so
        /// there is no size to render at and the caller must try again later.
        /// </summary>
        bool TryGetTargetSize(out int w, out int h) {
            w = _resW;
            h = _resH;
            if (w <= 0 || h <= 0) {
                var r = contentRect;
                // NaN is the pre-layout state, and it is what an element reports for
                // the frame after a hot reload.
                if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return false;
                w = Mathf.RoundToInt(r.width);
                h = Mathf.RoundToInt(r.height);
            }
            w = Mathf.Clamp(w, MinRes, MaxRes);
            h = Mathf.Clamp(h, MinRes, MaxRes);
            return true;
        }

        bool EnsureTarget() {
            if (!TryGetTargetSize(out int w, out int h)) return false;
            if (_rt != null && _rtW == w && _rtH == h) {
                // The device can drop the texture without its size changing, and
                // UI Toolkit samples this one as a backgroundImage.
                RenderTextureUtils.EnsureCreated(_rt);
                return true;
            }

            ReleaseTexture();
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) {
                name = $"OneJS_ShaderFX_{w}x{h}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _rt.Create();
            // A new size clears a program's previous frame: it is not resampled.
            _cleared = true;
            _targetIsNew = true;
            _rtW = w;
            _rtH = h;
            style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_rt));
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
            if (!_targetIsNew) return;
            _targetIsNew = false;
            var active = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
        }

        void ReleaseTexture() {
            ReleaseHistory();
            if (_rt == null) return;
            style.backgroundImage = StyleKeyword.Null;
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
            _rt = null;
            _rtW = _rtH = 0;
        }

        /// <summary>Frees the target and material. Safe to call twice.</summary>
        /// <summary>
        /// Drops the program, which owns the material and its program texture.
        /// </summary>
        void ReleaseProgram() {
            if (_programHandle < 0) return;
            SL.SLProgramBridge.Release(_programHandle);
            _programHandle = -1;
            // Released with the program. Leaving it set would have the element
            // destroy an object the bridge has already destroyed.
            _material = null;
        }

        public void Dispose() {
            ShaderEffectBridge.Unregister(this);
            ReleaseTexture();
            ReleaseProgram();
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
