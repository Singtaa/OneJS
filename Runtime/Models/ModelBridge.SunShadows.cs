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
    /// How far and how sharp the sun's realtime shadows are (onejs-play's `sun({ shadows: {
    /// distance, cascades, softness, resolution } })`, SHADOWS_DESIGN.md in the OneJS container).
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>Set by the pipeline's assembly when it loads; null where none can set shadows (built-in, HDRP).</summary>
        public static ISunShadowPipeline SunShadowPipeline { get; set; }

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
    }
}
