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
    /// The drawing is a <see cref="ShaderPass"/>'s, the same as a pass drawn
    /// for another program to read: the program, the clock, the previous frame
    /// and the passes it reads. The element sizes it from its layout and shows
    /// what it draws. The calls below are the pass's, kept here because JS
    /// makes them on the element.
    ///
    /// Ticked by ShaderEffectBridge.TickAll from QuickJSUIBridge.Tick, which
    /// covers play mode, edit-mode preview and JSPad through one integration point.
    /// </summary>
    [UxmlElement]
    public partial class ShaderEffectElement : VisualElement {
        readonly Shown _pass;
        bool _pixelated;
        bool _paintingOnLayout;

        /// <summary>
        /// The context that created this, so tearing that context down disposes
        /// it without touching another JSRunner's. 0 when made outside a JS call.
        /// </summary>
        internal int OwnerContextId { get; set; }

        public ShaderEffectElement() {
            pickingMode = PickingMode.Ignore;
            _pass = new Shown(this);
            ShaderEffectBridge.Register(this);
            RegisterCallback<DetachFromPanelEvent>(_ => _pass.ReleaseTarget());
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
            // Showing the target assigns backgroundImage, which re-dirties layout, and an
            // auto-sized element measures against its own background. Without this
            // guard that feedback could re-enter here from the pass it triggers.
            if (_paintingOnLayout) return;
            if (!_pass.TryGetSize(out int w, out int h)) return;
            if (_pass.Target != null && _pass.RenderWidth == w && _pass.RenderHeight == h) return; // already the right size
            _paintingOnLayout = true;
            // dt 0: this is a catch-up paint, not a frame, so the effect clock must
            // not jump just because the element was resized.
            try {
                ShaderEffectBridge.BeginDraw();
                _pass.Tick(0f);
            } finally { _paintingOnLayout = false; }
        }

        /// <summary>
        /// The element's pass: sized by its layout, drawing into a target UI
        /// Toolkit can show, and copying a kept frame into it.
        /// </summary>
        sealed class Shown : ShaderPass {
            readonly ShaderEffectElement _el;

            public Shown(ShaderEffectElement el) : base(standalone: false) {
                _el = el;
            }

            protected override bool TryGetNaturalSize(out int w, out int h) {
                w = h = 0;
                var r = _el.contentRect;
                // NaN is the pre-layout state, and it is what an element reports for
                // the frame after a hot reload.
                if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return false;
                w = Mathf.RoundToInt(r.width);
                h = Mathf.RoundToInt(r.height);
                return true;
            }

            protected override bool TryGetPictureSize(out int w, out int h) => TryGetSize(out w, out h);

            protected override RenderTexture CreateTarget(int w, int h) {
                var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) {
                    name = $"OneJS_ShaderFX_{w}x{h}",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = _el.DisplayFilter,
                };
                rt.Create();
                return rt;
            }

            protected override bool ShowsTarget => true;

            protected override void TargetChanged(RenderTexture target) {
                _el.style.backgroundImage = target != null
                    ? new StyleBackground(Background.FromRenderTexture(target))
                    : new StyleBackground(StyleKeyword.Null);
            }

            // The draw command already references the target, so new contents
            // appear without re-tessellating, but edit-mode preview only repaints
            // dirty elements, so ask for one.
            protected override void Repaint() => _el.MarkDirtyRepaint();

            protected override bool CanDraw(bool stepping) => _el.panel != null || (stepping && HasResolution);

            protected override string Describe() =>
                string.IsNullOrEmpty(_el.name) ? $"the element drawing {Drawing}" : $"the element \"{_el.name}\" drawing {Drawing}";
        }

        // MARK: JS-facing API (each call is a single interop crossing), the pass's

        /// <summary>Shader to run, e.g. "OneJS/Fire". Resolved through Resources so it survives builds.</summary>
        public void SetShader(string shaderName) => _pass.SetShader(shaderName);

        /// <inheritdoc cref="ShaderPass.AcceptsCompiledPrograms"/>
        public bool AcceptsCompiledPrograms => _pass.AcceptsCompiledPrograms;

        /// <inheritdoc cref="ShaderPass.AcceptsSteppedPrograms"/>
        public bool AcceptsSteppedPrograms => _pass.AcceptsSteppedPrograms;

        /// <inheritdoc cref="ShaderPass.AcceptsPasses"/>
        public bool AcceptsPasses => _pass.AcceptsPasses;

        /// <inheritdoc cref="ShaderPass.SetProgram"/>
        public bool SetProgram(object dataObj, int instructionCount, int resultRegister, string hash,
                               object uniformNamesObj = null, int wire = 1) =>
            _pass.SetProgram(dataObj, instructionCount, resultRegister, hash, uniformNamesObj, wire);

        /// <inheritdoc cref="ShaderPass.RecordProgram"/>
        public void RecordProgram(string hash, string hlsl) => _pass.RecordProgram(hash, hlsl);

        /// <inheritdoc cref="ShaderPass.WantsWebSource"/>
        public bool WantsWebSource => _pass.WantsWebSource;

        /// <inheritdoc cref="ShaderPass.SetProgramWeb"/>
        public void SetProgramWeb(string wgsl, string glsl) => _pass.SetProgramWeb(wgsl, glsl);

        /// <inheritdoc cref="ShaderPass.SetCompiled"/>
        public void SetCompiled(bool allowed) { }

        /// <inheritdoc cref="ShaderPass.IsCompiled"/>
        public bool IsCompiled => _pass.IsCompiled;

        /// <inheritdoc cref="ShaderPass.SetUniform"/>
        public void SetUniform(int slot, float x, float y, float z, float w) => _pass.SetUniform(slot, x, y, z, w);

        /// <inheritdoc cref="ShaderPass.SetProgramTexture"/>
        public void SetProgramTexture(int slot, Texture tex) => _pass.SetProgramTexture(slot, tex);

        /// <inheritdoc cref="ShaderPass.SetProgramBuiltinTexture"/>
        public void SetProgramBuiltinTexture(int slot, string builtin) => _pass.SetProgramBuiltinTexture(slot, builtin);

        /// <inheritdoc cref="ShaderPass.SetProgramPass"/>
        public bool SetProgramPass(int slot, ShaderPass pass) => _pass.SetProgramPass(slot, pass);

        public void SetFloat(string name, float value) => _pass.SetFloat(name, value);
        public void SetVector(string name, float x, float y, float z, float w) => _pass.SetVector(name, x, y, z, w);
        public void SetColor(string name, float r, float g, float b, float a) => _pass.SetColor(name, r, g, b, a);
        public void SetTexture(string name, Texture tex) => _pass.SetTexture(name, tex);

        /// <inheritdoc cref="ShaderPass.SetVectorArray"/>
        public void SetVectorArray(string name, object flatObj) => _pass.SetVectorArray(name, flatObj);

        /// <summary>Named built-in procedural texture, so effects need ship no assets.</summary>
        public void SetBuiltinTexture(string name, string builtin) => _pass.SetBuiltinTexture(name, builtin);

        /// <summary>Builds a 256x1 gradient from evenly spaced RGBA stops and binds it.</summary>
        public void SetRamp(string name, float[] rgba) => _pass.SetRamp(name, rgba);

        /// <summary>Render resolution. Pass 0,0 to follow the element's layout size.</summary>
        public void SetResolution(int w, int h) => _pass.SetResolution(w, h);

        public void Pause() => _pass.Pause();
        public void Resume() => _pass.Resume();

        /// <summary>
        /// Shows the picture as hard-edged pixels when it is smaller than the
        /// element, as CSS's image-rendering: pixelated does, so a program drawn
        /// at 32x32 shows each pixel as a crisp block. Off by default, which
        /// smooths it. Only how the picture is shown: a program reading its
        /// previous frame reads it as before.
        /// </summary>
        public bool Pixelated {
            get => _pixelated;
            set {
                if (_pixelated == value) return;
                _pixelated = value;
                if (_pass.Target != null) _pass.Target.filterMode = DisplayFilter;
                MarkDirtyRepaint();
            }
        }

        FilterMode DisplayFilter => _pixelated ? FilterMode.Point : FilterMode.Bilinear;

        /// <inheritdoc cref="ShaderPass.ResetTime"/>
        public void ResetTime() => _pass.ResetTime();

        /// <inheritdoc cref="ShaderPass.SetTime"/>
        public void SetTime(float seconds) => _pass.SetTime(seconds);

        /// <inheritdoc cref="ShaderPass.Step"/>
        /// <remarks>Draws with no panel when the element has an explicit resolution.</remarks>
        public void Step(float dt) => _pass.Step(dt);

        /// <summary>The frames drawn since the previous frame was last cleared: a program's `frame`.</summary>
        public int Frame => _pass.Frame;

        public bool IsReady => _pass.IsReady;
        public int RenderWidth => _pass.RenderWidth;
        public int RenderHeight => _pass.RenderHeight;

        internal void Tick(float dt) => _pass.Tick(dt);

        /// <summary>Frees the target and material. Safe to call twice.</summary>
        public void Dispose() {
            ShaderEffectBridge.Unregister(this);
            _pass.Dispose();
        }
    }
}
