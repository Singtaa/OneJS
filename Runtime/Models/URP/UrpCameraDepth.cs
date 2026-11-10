using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OneJS.Models {
    /// <summary>
    /// The scene camera's depth texture on URP (ModelBridge.WantDepth): its additional data's
    /// requiresDepthTexture, which asks for the depth whatever the pipeline asset says.
    /// </summary>
    sealed class UrpCameraDepth : ICameraDepthPipeline {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            if (ModelBridge.CameraDepthPipeline is not UrpCameraDepth) ModelBridge.CameraDepthPipeline = new UrpCameraDepth();
        }

        public bool Get(Camera camera) => camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.requiresDepthTexture;

        public void Set(Camera camera, bool on) => camera.GetUniversalAdditionalCameraData().requiresDepthTexture = on;
    }
}
