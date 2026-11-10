using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OneJS.Models {
    /// <summary>
    /// Keeps the camera that only clears the screen while the scene draws into a texture
    /// (ModelBridge.ClearCamera) to clearing. It draws nothing, yet URP ran every renderer feature
    /// for it as for any camera: SSAO's depth-normals prepass and AO passes, and the opaque and depth
    /// copies the asset asks for, a full screen of work each for an empty view. So it asks for no
    /// textures, and the features active on the pipeline's renderers are switched off for its render
    /// alone, from URP's begin of that camera to its end, which enclose the frame's AddRenderPasses.
    /// Every other camera finds them as they were.
    /// </summary>
    static class UrpClearCamera {
        static readonly List<ScriptableRendererFeature> s_Suspended = new();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering -= End;
            RenderPipelineManager.endCameraRendering += End;
        }

        static void Begin(ScriptableRenderContext context, Camera camera) {
            if (camera == null || camera != ModelBridge.ClearCamera) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset) return;
            foreach (var renderer in asset.rendererDataList) {
                if (renderer == null) continue;
                foreach (var feature in renderer.rendererFeatures) {
                    if (feature == null || !feature.isActive) continue;
                    feature.SetActive(false);
                    s_Suspended.Add(feature);
                }
            }
        }

        static void End(ScriptableRenderContext context, Camera camera) {
            if (s_Suspended.Count == 0) return;
            foreach (var feature in s_Suspended)
                if (feature != null) feature.SetActive(true);
            s_Suspended.Clear();
        }
    }
}
