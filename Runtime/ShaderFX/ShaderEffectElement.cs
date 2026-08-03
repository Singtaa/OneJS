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
    ///    unityMaterial costs an element its analytic AA - see the particle
    ///    engine's host-styling warning.)
    ///
    /// It is deliberately generic: any shader, any float/vector/colour/texture
    /// property. Effects like fire are a shader plus a thin JS wrapper, not a new
    /// C# class, so adding one costs no engine code.
    ///
    /// Ticked by ShaderEffectBridge.TickAll from QuickJSUIBridge.Tick, which
    /// covers play mode, edit-mode preview and JSPad through one integration point.
    /// </summary>
    public class ShaderEffectElement : VisualElement {
        const int MinRes = 8;
        const int MaxRes = 2048;

        Material _material;
        RenderTexture _rt;
        string _shaderName;
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

        public new class UxmlFactory : UxmlFactory<ShaderEffectElement> { }

        public ShaderEffectElement() {
            pickingMode = PickingMode.Ignore;
            ShaderEffectBridge.Register(this);
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseTexture());
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
        /// <summary>Resets the effect clock, so a restarted effect looks the same every time.</summary>
        public void ResetTime() => _seconds = 0f;

        public bool IsReady => _material != null && _rt != null;
        public int RenderWidth => _rtW;
        public int RenderHeight => _rtH;

        // MARK: frame

        internal void Tick(float dt) {
            if (_paused || string.IsNullOrEmpty(_shaderName) || _shaderMissing) return;
            if (panel == null) return;
            if (!EnsureMaterial()) return;
            if (!EnsureTarget()) return;

            _seconds += dt;
            _material.SetFloat("_Secs", _seconds);
            // Render-target UV origin differs across graphics APIs; correct it here
            // so a shader can always treat uv.y = 0 as the BOTTOM of the element and
            // never care which API it is running on. Verified visually on D3D11,
            // where graphicsUVStartsAtTop is true and no flip is what reads upright.
            _material.SetFloat("_FlipY", SystemInfo.graphicsUVStartsAtTop ? 0f : 1f);

            foreach (var kv in _floats) _material.SetFloat(kv.Key, kv.Value);
            foreach (var kv in _vectors) _material.SetVector(kv.Key, kv.Value);
            foreach (var kv in _vectorArrays) _material.SetVectorArray(kv.Key, kv.Value);
            foreach (var kv in _textures) if (kv.Value != null) _material.SetTexture(kv.Key, kv.Value);

            var prev = RenderTexture.active;
            Graphics.Blit(null, _rt, _material, 0);
            RenderTexture.active = prev;

            // The draw command already references this texture, so the new contents
            // appear without re-tessellating - but edit-mode preview only repaints
            // dirty elements, so ask for one.
            MarkDirtyRepaint();
        }

        bool EnsureMaterial() {
            if (_material != null) return true;
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

        bool EnsureTarget() {
            int w = _resW, h = _resH;
            if (w <= 0 || h <= 0) {
                var r = contentRect;
                if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return false; // not laid out yet
                w = Mathf.Clamp(Mathf.RoundToInt(r.width), MinRes, MaxRes);
                h = Mathf.Clamp(Mathf.RoundToInt(r.height), MinRes, MaxRes);
            } else {
                w = Mathf.Clamp(w, MinRes, MaxRes);
                h = Mathf.Clamp(h, MinRes, MaxRes);
            }
            if (_rt != null && _rtW == w && _rtH == h) return true;

            ReleaseTexture();
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) {
                name = $"OneJS_ShaderFX_{w}x{h}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _rt.Create();
            _rtW = w;
            _rtH = h;
            style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_rt));
            return true;
        }

        void ReleaseTexture() {
            if (_rt == null) return;
            style.backgroundImage = StyleKeyword.Null;
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
            _rt = null;
            _rtW = _rtH = 0;
        }

        /// <summary>Frees the target and material. Safe to call twice.</summary>
        public void Dispose() {
            ShaderEffectBridge.Unregister(this);
            ReleaseTexture();
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
