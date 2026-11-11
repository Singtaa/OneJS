using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// An actor's own colours: a tint the whole of it is multiplied by, up to four colours
    /// painted through its model's mask (a picture laid out as the model's, whose red, green,
    /// blue and alpha say how much of colours 1 to 4 a texel takes), and a flash added after its
    /// light.
    ///
    /// Each coloured actor has a row of six texels (the tint, the four colours, the flash) in one
    /// small texture, and the row's number in its renderers' user value above the shadow bit
    /// (ModelBridge.Apply). ModelLit reads the row in its lit pass (ModelLitURP.hlsl, Painted).
    /// The user value rides in the SRP Batcher's per-draw data, so a thousand differently
    /// coloured actors of one model still batch, which a property block would end: 400 meshes
    /// each with a block measured 807 set-pass calls against 9. Row 0 is no colours at all, what
    /// every other actor reads. URP only: the built-in pipeline draws an actor as its file has it.
    /// </summary>
    public static partial class ModelBridge {
        const int ColorsWide = 6;
        // The colours painted through a mask, texels 1 to 4; the flash is the last.
        const int MaskColors = 4;
        const int FlashTexel = 5;
        // The user value keeps bit 0 for the shadow, so a row has 31 bits; a texture's height ends it far sooner.
        const int MostColorRows = 8192;
        static readonly int ActorColorsId = Shader.PropertyToID("_OjActorColors");
        static readonly int PaintMaskId = Shader.PropertyToID("_PaintMask");
        static readonly int PaintedId = Shader.PropertyToID("_Painted");

        static Texture2D _actorColors;
        // The texture's texels, a row an actor; row 0 stays white.
        static Color32[] _colorRows = new Color32[0];
        static int _colorRowsUsed = 1;
        static readonly Stack<int> _freeColorRows = new();
        static bool _colorsChanged, _colorsWatching, _saidColorsBuiltIn;
        static readonly Color32 White = new(255, 255, 255, 255);

        /// <summary>
        /// The colour an actor is multiplied by, as sRGB: white is the actor as its file has it.
        /// "" when set, or what was wrong.
        /// </summary>
        public static string SetTint(int actor, float r, float g, float b) {
            if (!_actors.TryGetValue(actor, out var a)) return "";
            if (!Finite(r) || !Finite(g) || !Finite(b)) return $"the tint is {r}, {g}, {b}, and is a colour";
            a.tint = new Color(r, g, b, 1);
            return Recolor(a);
        }

        /// <summary>
        /// The colour added to an actor after its light, as sRGB from 0 to 1: black, the default,
        /// adds nothing, and white makes it as bright as it can be. A hit's flash for a frame or
        /// two. Fog falls over it. "" when set, or what was wrong.
        /// </summary>
        public static string SetFlash(int actor, float r, float g, float b) {
            if (!_actors.TryGetValue(actor, out var a)) return "";
            if (!Finite(r) || !Finite(g) || !Finite(b)) return $"the flash is {r}, {g}, {b}, and is a colour";
            a.flash = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1);
            return Recolor(a);
        }

        /// <summary>
        /// The colours painted through the actor's model's mask, three floats each as sRGB, up to
        /// four; one left out is white, which paints nothing. Null or empty clears them. "" when
        /// set, or what was wrong.
        /// </summary>
        public static string SetColors(int actor, float[] rgb) {
            if (!_actors.TryGetValue(actor, out var a)) return "";
            int n = rgb == null ? 0 : rgb.Length / 3;
            if (rgb != null && (rgb.Length % 3 != 0 || n > MaskColors)) return $"colors are {rgb.Length} numbers, and are three each for up to {MaskColors} colours";
            if (n > 0 && (!_models.TryGetValue(a.model, out var m) || m.paintMask == null)) return "this model was loaded without a colorMask, which says where each colour goes";
            if (n == 0) {
                a.colors = null;
            } else {
                a.colors = new Color[n];
                for (int i = 0; i < n; i++) {
                    if (!Finite(rgb[i * 3]) || !Finite(rgb[i * 3 + 1]) || !Finite(rgb[i * 3 + 2])) return $"colour {i + 1} is not a colour";
                    a.colors[i] = new Color(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], 1);
                }
            }
            return Recolor(a);
        }

        /// <summary>
        /// Gives a loaded model its colour mask, or takes it away with null. The mask is kept as
        /// a linear copy, since its channels are amounts and not colours. "" when set, or what was wrong.
        /// </summary>
        public static string SetColorMask(int model, Texture picture) {
            if (!_models.TryGetValue(model, out var m) || m.template == null) return "there is no such model";
            if (m.built != null) return "a mesh model takes its colours from its vertices, and has no colorMask";
            if (ModelMaterialGenerator.ModelShader == null) return "this project draws models with glTFast's own materials, which have no colorMask";
            var copy = picture == null ? null : TakeNormalCopy(picture);
            ReleaseNormalCopy(m.paintMask);
            m.paintMask = copy;
            foreach (var r in m.template.GetComponentsInChildren<Renderer>(true)) {
                foreach (var material in r.sharedMaterials) {
                    if (material == null || material.shader != ModelMaterialGenerator.ModelShader) continue;
                    material.SetTexture(PaintMaskId, copy);
                    material.SetFloat(PaintedId, copy == null ? 0 : 1);
                }
            }
            return "";
        }

        static void FreeColorMask(Model m) {
            ReleaseNormalCopy(m.paintMask);
            m.paintMask = null;
        }

        /// <summary>The row of the colours texture an actor reads, 0 for one with no colours of its own.</summary>
        internal static int ColorRow(int actor) => _actors.TryGetValue(actor, out var a) ? a.colorRow : 0;

        /// <summary>The colours texture, null until an actor has colours.</summary>
        internal static Texture2D ActorColors => _actorColors;

        // Gives the actor a row, or its row back when it is white all over, and writes its colours there.
        static string Recolor(Actor a) {
            bool plain = a.colors == null && a.tint == Color.white && a.flash == Color.black;
            if (!plain && BuiltInPipeline) {
                if (!_saidColorsBuiltIn) {
                    _saidColorsBuiltIn = true;
                    Debug.LogWarning("[OneJS] An actor's tint and colors draw in URP only, so actors here keep the colours of their files.");
                }
                return "";
            }
            if (plain) {
                if (a.colorRow == 0) return "";
                FreeColorRow(a);
                Apply(a);
                return "";
            }
            if (a.colorRow == 0) {
                if (_freeColorRows.Count == 0 && _colorRowsUsed >= MostColorRows) return $"{MostColorRows - 1} actors already have colours of their own, which is the most";
                a.colorRow = _freeColorRows.Count > 0 ? _freeColorRows.Pop() : _colorRowsUsed++;
                Apply(a);
            }
            if (_colorRows.Length < _colorRowsUsed * ColorsWide) {
                int rows = Mathf.Max(64, Mathf.NextPowerOfTwo(_colorRowsUsed));
                int had = _colorRows.Length;
                Array.Resize(ref _colorRows, rows * ColorsWide);
                for (int i = had; i < _colorRows.Length; i++) _colorRows[i] = White;
            }
            int at = a.colorRow * ColorsWide;
            _colorRows[at] = a.tint;
            for (int i = 1; i <= MaskColors; i++) _colorRows[at + i] = a.colors != null && i - 1 < a.colors.Length ? (Color32)a.colors[i - 1] : White;
            _colorRows[at + FlashTexel] = a.flash;
            _colorsChanged = true;
            WatchColors(true);
            return "";
        }

        // On Destroy: the row goes back, and the next actor to take it writes its own colours first.
        static void FreeColorRow(Actor a) {
            if (a.colorRow == 0) return;
            _freeColorRows.Push(a.colorRow);
            a.colorRow = 0;
        }

        static void WatchColors(bool on) {
            if (on == _colorsWatching) return;
            _colorsWatching = on;
            if (on) {
                RenderPipelineManager.beginContextRendering += ColorsFor;
            } else {
                RenderPipelineManager.beginContextRendering -= ColorsFor;
            }
        }

        static void ColorsFor(ScriptableRenderContext context, List<Camera> cameras) => UploadColors();

        /// <summary>
        /// Sends the rows that changed to the texture, once before a frame draws however many
        /// actors were coloured in it. The texture is sRGB, so the shader reads linear colours.
        /// </summary>
        internal static void UploadColors() {
            if (!_colorsChanged) return;
            _colorsChanged = false;
            int rows = _colorRows.Length / ColorsWide;
            if (_actorColors == null || _actorColors.height != rows) {
                if (_actorColors != null) Kill(_actorColors);
                _actorColors = new Texture2D(ColorsWide, rows, TextureFormat.RGBA32, false, false) {
                    name = "actor colours", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                };
                Shader.SetGlobalTexture(ActorColorsId, _actorColors);
            }
            _actorColors.SetPixels32(_colorRows);
            _actorColors.Apply(false);
        }

        static void StopColors() {
            WatchColors(false);
            if (_actorColors != null) Kill(_actorColors);
            _actorColors = null;
            Shader.SetGlobalTexture(ActorColorsId, null);
            _colorRows = new Color32[0];
            _colorRowsUsed = 1;
            _freeColorRows.Clear();
            _colorsChanged = false;
        }
    }
}
