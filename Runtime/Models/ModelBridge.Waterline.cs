using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// The scene's waterline (onejs-play's `scene.waterline`): below a height, everything ModelLit and
    /// GroundLit draw turns toward a colour, so a body wading in see-through water shows a clean line
    /// where the water meets it, whatever the water's alpha, and what lies under the water takes its
    /// colour. Two global values the forward pass reads per pixel, so loaded, skinned and mesh models,
    /// actors and scattered copies take it alike, and a level that rises each frame costs one call a frame.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int WaterlineId = Shader.PropertyToID("_OjWaterline");
        static readonly int WaterlineColorId = Shader.PropertyToID("_OjWaterlineColor");

        /// <summary>
        /// Below `y` metres, turns every model's shading toward `r, g, b` (sRGB) by `strength`, 0 to
        /// 1, over a 3 cm edge; or none, when `on` is false. Drawn by URP's lit pass alone. "" when it
        /// took it, or what was wrong.
        /// </summary>
        public static string SetWaterline(bool on, float y, float r, float g, float b, float strength) {
            if (!on) {
                StopWaterline();
                return "";
            }
            if (!Finite(y)) return $"the waterline is at {y}, and is a height in metres";
            if (!Finite(r) || !Finite(g) || !Finite(b)) return "its colour is not a colour";
            if (!(strength >= 0 && strength <= 1)) return $"its strength is {strength}, and runs from 0 to 1";
            Shader.SetGlobalVector(WaterlineId, new Vector4(y, strength, 1, 0));
            // A global takes no colour space of its own: linear, as a material's SetColor keeps it.
            Shader.SetGlobalColor(WaterlineColorId, new Color(r, g, b, 1).linear);
            return "";
        }

        /// <summary>No waterline, as before any scene: the scene's teardown.</summary>
        static void StopWaterline() => Shader.SetGlobalVector(WaterlineId, Vector4.zero);
    }
}
