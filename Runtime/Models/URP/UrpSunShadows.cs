using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OneJS.Models {
    /// <summary>
    /// The sun's shadow settings on URP (ModelBridge.SetSunShadows): distance, cascades and the
    /// shadow map's resolution on the pipeline asset, and the soft edge's width on the sun.
    ///
    /// The asset is the project's, shared by every camera, and in the editor a file: a value set in
    /// play mode would be written into the project when it next saves. So the asset's own values
    /// are kept the first time a scene changes them and put back when the scene goes, when a cart
    /// stops asking for one, and on leaving play mode for a scene that never went. A web player's
    /// asset is gone with the page, so there it only matters within the page.
    ///
    /// A soft edge's width is a uniform per light on every URP target but mobile XR
    /// (ShadowUtils.SupportsPerLightSoftShadowQuality), so it needs no shader variant beyond the
    /// _SHADOWS_SOFT that ModelLit already has.
    /// </summary>
    sealed class UrpSunShadows : ISunShadowPipeline {
        UniversalRenderPipelineAsset _asset;
        float _distance;
        int _cascades, _resolution;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            if (ModelBridge.SunShadowPipeline is not UrpSunShadows) ModelBridge.SunShadowPipeline = new UrpSunShadows();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= LeavingPlayMode;
            UnityEditor.EditorApplication.playModeStateChanged += LeavingPlayMode;
#endif
        }

#if UNITY_EDITOR
        static void LeavingPlayMode(UnityEditor.PlayModeStateChange state) {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) ModelBridge.SunShadowPipeline?.Restore();
        }
#endif

        public string Apply(Light sun, float distance, int cascades, int softness, int resolution, bool pipeline) {
            if (sun != null) {
                var data = sun.GetUniversalAdditionalLightData();
                data.softShadowQuality = softness > 0 ? (SoftShadowQuality)softness : SoftShadowQuality.UsePipelineSettings;
            }
            if (!pipeline) return "";
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return "this project draws without a URP asset, so its shadow distance, cascades and resolution cannot be set";
            if (asset != _asset) {
                Restore();
                _asset = asset;
                _distance = asset.shadowDistance;
                _cascades = asset.shadowCascadeCount;
                _resolution = asset.mainLightShadowmapResolution;
            }
            asset.shadowDistance = distance > 0 ? distance : _distance;
            asset.shadowCascadeCount = cascades > 0 ? cascades : _cascades;
            asset.mainLightShadowmapResolution = resolution > 0 ? resolution : _resolution;
            return "";
        }

        public void Restore() {
            if (_asset == null) return;
            _asset.shadowDistance = _distance;
            _asset.shadowCascadeCount = _cascades;
            _asset.mainLightShadowmapResolution = _resolution;
            _asset = null;
        }
    }
}
