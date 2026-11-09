using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// What the render pipeline does with the sun's shadow settings (ModelBridge.SetSunShadows):
    /// URP's, from OneJS.Runtime.Models.URP, which registers itself where URP is installed.
    /// </summary>
    public interface ISunShadowPipeline {
        /// <summary>
        /// Gives `sun` and the pipeline the settings, each negative for the project's own:
        /// `distance` in metres, 1 to 4 `cascades`, `softness` 0 (hard) to 3 (a 7x7 filter), and
        /// the shadow map's `resolution`. The pipeline's own settings change only when `pipeline`,
        /// which is play mode, and are put back by Restore. "" when it took them, or why not.
        /// </summary>
        string Apply(Light sun, float distance, int cascades, int softness, int resolution, bool pipeline);

        /// <summary>Puts back the pipeline settings Apply changed. Safe to call when it changed none.</summary>
        void Restore();
    }

    /// <summary>
    /// Shadows for big streamed worlds (SHADOWS_DESIGN.md in the OneJS container): how far and how
    /// sharp the sun's realtime shadows are (onejs-play's `sun({ shadows: { distance, cascades,
    /// softness, resolution } })`), and a grid of shade on a mesh model, for the ground past where
    /// realtime shadows reach (`scene.mesh({ shade })`), laid by ModelLighting.hlsl as the darker
    /// of it and the realtime shadow.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int ShadeMapId = Shader.PropertyToID("_ShadeMap");

        /// <summary>Set by the pipeline's assembly when it loads; null where none can set shadows (built-in, HDRP).</summary>
        public static ISunShadowPipeline SunShadowPipeline { get; set; }

        /// <summary>The most samples along a side of a shade grid.</summary>
        public const int ShadeMaxSize = 1024;

        /// <summary>
        /// How far and how sharp the sun's shadows are, after SetSun with shadows on. Each value
        /// negative keeps the project's own: `distance` in metres from the camera, 1 to 4
        /// `cascades`, `softness` 0 (hard), 1 (3x3), 2 (5x5) or 3 (7x7), and a `resolution` of
        /// 1024, 2048 or 4096. In play mode the pipeline's settings change until the scene goes;
        /// the edit-mode preview keeps the project's, and a scene's own sun keeps its settings.
        /// "" when it took them, or what was wrong.
        /// </summary>
        public static string SetSunShadows(float distance, int cascades, int softness, int resolution) {
            if (distance == 0 || float.IsNaN(distance) || float.IsInfinity(distance)) return $"distance is {distance}: metres above 0";
            if (cascades == 0 || cascades > 4) return $"cascades is {cascades}: 1 to 4";
            if (softness > 3) return $"softness is {softness}: 0 (hard) to 3";
            if (resolution >= 0 && resolution != 1024 && resolution != 2048 && resolution != 4096) return $"resolution is {resolution}: 1024, 2048 or 4096";
            // Hard or soft is the light's own, in any pipeline; the rest is URP's.
            bool needsPipeline = distance > 0 || cascades > 0 || softness > 0 || resolution > 0;
            if (needsPipeline && SunShadowPipeline == null) {
                return "this project's render pipeline is not URP, and OneJS sets shadow distance, cascades, a soft edge's width and resolution on URP alone";
            }
            var sun = _sun != null && _sun.enabled && _sun.shadows != LightShadows.None ? _sun : null;
            if (sun != null) sun.shadows = softness == 0 ? LightShadows.Hard : LightShadows.Soft;
            return SunShadowPipeline?.Apply(sun, distance, cascades, softness, resolution, Playing) ?? "";
        }

        /// <summary>
        /// A grid of shade over a mesh model, `width` by `height` samples row by row along u: how
        /// much of the sun each blocks, 0 none to 255 all of it, read across the model's uvs before
        /// tiling. The samples sit on the grid's corners, so the first and last rows and columns
        /// land on the uvs' 0 and 1, and a chunk whose edge row equals its neighbour's shows no
        /// seam. It dims the sun alone, as a realtime shadow does, and a spot in both darkens once
        /// (ModelLighting.hlsl). Null takes it off. "" when it took it, or what was wrong.
        /// </summary>
        public static string SetMeshShade(int model, int width, int height, byte[] shade) {
            if (!_models.TryGetValue(model, out var m) || m.built == null) return $"there is no mesh model {model}";
            var b = m.built;
            if (shade == null) {
                ReleaseShade(b);
                ApplyShade(b);
                return "";
            }
            if (!b.givesUvs) return "this model was made without uvs, which a shade is read at: give uvs with its first vertices";
            if (b.depthOnly) return "this model draws depth alone, and nothing for a shade to fall on";
            if (width < 2 || height < 2 || width > ShadeMaxSize || height > ShadeMaxSize) return $"a shade grid is {width} by {height}: 2 to {ShadeMaxSize} samples a side";
            if (shade.Length != width * height) return $"a {width} by {height} shade grid has {width * height} values, and this has {shade.Length}";

            if (b.shade == null || b.shade.width != width || b.shade.height != height) {
                ReleaseShade(b);
                b.shade = new Texture2D(width, height, TextureFormat.R8, true, true) {
                    name = "shade",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                };
                if (!Playing) b.shade.hideFlags = HideFlags.DontSave;
            }
            b.shade.SetPixelData(shade, 0);
            b.shade.Apply(true, false);
            ApplyShade(b);
            return "";
        }

        // On the model's material now, and again whenever it is replaced (BecomeSplat calls ApplyLook).
        static void ApplyShade(BuiltMesh b) {
            if (b.material != null && b.material.HasTexture(ShadeMapId)) b.material.SetTexture(ShadeMapId, b.shade);
        }

        static void ReleaseShade(BuiltMesh b) {
            if (b.shade != null) Kill(b.shade);
            b.shade = null;
        }

        internal static Texture2D ShadeOf(int model) => _models.TryGetValue(model, out var m) ? m.built?.shade : null;
    }
}
