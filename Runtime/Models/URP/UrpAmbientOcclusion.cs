using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OneJS.Models {
    /// <summary>
    /// A scene's ambient occlusion on URP (ModelBridge.SetAmbientOcclusion): the
    /// ScreenSpaceAmbientOcclusion feature of every renderer the active pipeline asset lists. Off
    /// is the feature's own SetActive. Its intensity and radius are internal fields of its settings,
    /// read and written by name, and the pass reads them again each frame; where a URP renames
    /// them, Write says it cannot rather than guess. What the project set is kept on first change
    /// and put back by Restore.
    /// </summary>
    sealed class UrpAmbientOcclusion : IAmbientOcclusionPipeline {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        sealed class Kept {
            public ScreenSpaceAmbientOcclusion feature;
            public bool active;
            public float intensity, radius;
        }

        readonly List<Kept> _kept = new();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            if (ModelBridge.AmbientOcclusionPipeline is not UrpAmbientOcclusion) ModelBridge.AmbientOcclusionPipeline = new UrpAmbientOcclusion();
        }

        static List<ScreenSpaceAmbientOcclusion> Features() {
            var found = new List<ScreenSpaceAmbientOcclusion>();
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset) return found;
            foreach (var data in asset.rendererDataList) {
                if (data != null && data.TryGetRendererFeature<ScreenSpaceAmbientOcclusion>(out var feature)) found.Add(feature);
            }
            return found;
        }

        static object Settings(ScreenSpaceAmbientOcclusion feature) =>
            typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Hidden)?.GetValue(feature);

        static FieldInfo Field(object settings, string name) {
            var f = settings?.GetType().GetField(name, Hidden);
            return f != null && f.FieldType == typeof(float) ? f : null;
        }

        public bool Read(out bool on, out float intensity, out float radius) {
            on = false;
            intensity = radius = 0;
            var features = Features();
            if (features.Count == 0) return false;
            var first = features[0];
            var kept = _kept.Find(k => k.feature == first);
            on = kept != null ? kept.active : first.isActive;
            var settings = Settings(first);
            var i = Field(settings, "Intensity");
            var r = Field(settings, "Radius");
            if (kept != null) {
                intensity = kept.intensity;
                radius = kept.radius;
            } else if (i != null && r != null) {
                intensity = (float)i.GetValue(settings);
                radius = (float)r.GetValue(settings);
            }
            return true;
        }

        public bool Write(bool on, float intensity, float radius) {
            var features = Features();
            if (features.Count == 0) return false;
            foreach (var feature in features) {
                var settings = Settings(feature);
                var i = Field(settings, "Intensity");
                var r = Field(settings, "Radius");
                if (on && (i == null || r == null)) return false;
                if (_kept.Find(k => k.feature == feature) == null) {
                    _kept.Add(new Kept {
                        feature = feature, active = feature.isActive,
                        intensity = i != null ? (float)i.GetValue(settings) : 0,
                        radius = r != null ? (float)r.GetValue(settings) : 0,
                    });
                }
                feature.SetActive(on);
                if (!on) continue;
                i.SetValue(settings, intensity);
                r.SetValue(settings, radius);
            }
            return true;
        }

        public void Restore() {
            foreach (var k in _kept) {
                if (k.feature == null) continue;
                k.feature.SetActive(k.active);
                var settings = Settings(k.feature);
                Field(settings, "Intensity")?.SetValue(settings, k.intensity);
                Field(settings, "Radius")?.SetValue(settings, k.radius);
            }
            _kept.Clear();
        }
    }
}
