using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// The fog ModelLit, GroundLit and the Sky draw (OneJSFog.hlsl): Unity's own fog settings in
    /// RenderSettings, read every frame, and a height falloff Unity has no setting for. The shaders
    /// take it from three globals rather than Unity's fog keywords, so linear, exp and exp2 are one
    /// variant, a build's fog stripping cannot take a mode away, and a project that sets Unity's fog
    /// itself fogs its models in every mode.
    ///
    /// Linear fog measures as Unity's did, from the camera's plane, so a scene drawn with it before
    /// draws the same. Exp and exp2 measure along each pixel's ray from `RenderSettings.fogStartDistance`,
    /// so turning the camera never changes how fogged a thing is.
    /// </summary>
    public static class ModelFog {
        static readonly int ColorId = Shader.PropertyToID("_OneJSFogColor");
        static readonly int ParamsId = Shader.PropertyToID("_OneJSFogParams");
        static readonly int HeightId = Shader.PropertyToID("_OneJSFogHeight");

        /// <summary>
        /// The height in metres where an exp or exp2 fog's density is `RenderSettings.fogDensity`.
        /// It falls by e every <see cref="HeightThickness"/> metres above, and rises below.
        /// </summary>
        public static float HeightBase { get; set; }

        /// <summary>Metres a step of e in the density, or 0 for a fog as dense at every height.</summary>
        public static float HeightThickness { get; set; }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            RenderPipelineManager.beginContextRendering -= BeforeFrame;
            RenderPipelineManager.beginContextRendering += BeforeFrame;
            Camera.onPreCull -= BeforeCamera;
            Camera.onPreCull += BeforeCamera;
            Apply();
        }

        static void BeforeFrame(ScriptableRenderContext _, System.Collections.Generic.List<Camera> __) => Apply();

        static void BeforeCamera(Camera _) => Apply();

        /// <summary>Writes the globals from RenderSettings and the height. Each frame does it before drawing.</summary>
        public static void Apply() {
            float mode = !RenderSettings.fog ? 0 : RenderSettings.fogMode switch {
                FogMode.Linear => 1,
                FogMode.Exponential => 2,
                _ => 3,
            };
            var color = RenderSettings.fogColor;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.linear;
            Shader.SetGlobalVector(ColorId, new Vector4(color.r, color.g, color.b, mode));

            // The linear ramp as Unity's unity_FogParams carries it: the share of the surface left at
            // a distance z is z * x + y.
            float near = RenderSettings.fogStartDistance, far = RenderSettings.fogEndDistance;
            float span = Mathf.Max(far - near, 1e-4f);
            Shader.SetGlobalVector(ParamsId, new Vector4(-1 / span, far / span, Mathf.Max(0, near), Mathf.Max(0, RenderSettings.fogDensity)));

            float thickness = HeightThickness;
            Shader.SetGlobalVector(HeightId, new Vector4(HeightBase, thickness > 0 ? 1 / thickness : 0, 0, 0));
        }
    }
}
