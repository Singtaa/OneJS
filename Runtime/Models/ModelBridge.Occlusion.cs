namespace OneJS.Models {
    /// <summary>
    /// The project's screen-space ambient occlusion, which only a render pipeline has: URP's
    /// renderer feature. OneJS.Runtime.Models.URP registers one where URP is installed.
    /// </summary>
    public interface IAmbientOcclusionPipeline {
        /// <summary>Whether the project draws any, and its intensity and radius in metres as the project set them.</summary>
        bool Read(out bool on, out float intensity, out float radius);
        /// <summary>Draws it at `intensity` and `radius`, or none when `on` is false. False where it cannot.</summary>
        bool Write(bool on, float intensity, float radius);
        /// <summary>Puts back what the project set, after a scene changed it.</summary>
        void Restore();
    }

    public static partial class ModelBridge {
        /// <summary>Set by the pipeline's assembly when it loads; null where the pipeline is not URP.</summary>
        public static IAmbientOcclusionPipeline AmbientOcclusionPipeline { get; set; }

        static bool _occlusionChanged;

        /// <summary>
        /// The scene's ambient occlusion (onejs-play's `ambient: { occlusion }`): none when `on` is
        /// false, and otherwise drawn at `intensity` (0 to 4) and `radius` (metres above 0), either
        /// of which below 0 keeps the project's own. The project's own comes back when the scene
        /// goes, so an asset changed in the editor is never left changed. "" when it took it, or
        /// what was wrong.
        /// </summary>
        public static string SetAmbientOcclusion(bool on, float intensity, float radius) {
            BeginScene();
            if (!Finite(intensity) || intensity > 4) return $"occlusion's intensity is {intensity}, and runs from 0 to 4";
            if (!Finite(radius) || radius == 0) return $"occlusion's radius is {radius}, and is metres above 0";
            var pipeline = AmbientOcclusionPipeline;
            if (pipeline == null || !pipeline.Read(out _, out float ownIntensity, out float ownRadius)) {
                // Off is what a project without any already draws.
                return on ? "this project's renderer has no screen-space ambient occlusion for a scene to set" : "";
            }
            if (!pipeline.Write(on, intensity < 0 ? ownIntensity : intensity, radius < 0 ? ownRadius : radius)) {
                return "this URP keeps its occlusion settings where OneJS cannot reach them, so only the project's own draws";
            }
            _occlusionChanged = true;
            return "";
        }

        static void StopOcclusion() {
            if (!_occlusionChanged) return;
            _occlusionChanged = false;
            AmbientOcclusionPipeline?.Restore();
        }
    }
}
