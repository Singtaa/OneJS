using OneJS.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    public static partial class ModelBridge {
        static Material _sky;
        static bool _skyHooked;
        // Edit mode only: the open scene's own sun, which SetSun leaves in place and RenderSettings
        // may not name. Found once a SetSky, never a frame.
        static Light _sceneSun;

        static readonly int TopId = Shader.PropertyToID("_Top");
        static readonly int HorizonId = Shader.PropertyToID("_Horizon");
        static readonly int BottomId = Shader.PropertyToID("_Bottom");
        static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        static readonly int SunShapeId = Shader.PropertyToID("_SunShape");
        static readonly int StarsId = Shader.PropertyToID("_Stars");

        /// <summary>
        /// A sky behind the scene (onejs-play's `scene.sky`), or with <paramref name="on"/> false
        /// the background colour again. Three sRGB colours, top, horizon and bottom; the sun's disc
        /// and glow in degrees across and out from its centre (0 draws none), where the scene's
        /// sun shines from and in its colour; stars from 0 to 1. Drawn as the skybox of the
        /// scene's camera alone, so the open scene's skybox and lighting stay as they are, and
        /// while the scene has fog the horizon fades into the fog's colour: with height fog, the
        /// haze along each ray, thinning as it climbs (ModelFog).
        /// </summary>
        public static void SetSky(bool on, float topR, float topG, float topB, float horizonR, float horizonG, float horizonB,
                                  float bottomR, float bottomG, float bottomB, float sunSize, float sunGlow, float stars) {
            BeginScene();
            if (!on) {
                StopSky();
                return;
            }
            if (_sky == null) {
                var shader = Resources.Load<Shader>("OneJS/Sky");
                if (shader == null || !shader.isSupported) {
                    Debug.LogWarning("[OneJS] The sky's shader (OneJS/Sky) is missing or unsupported here, so the scene keeps its background colour.");
                    return;
                }
                _sky = new Material(shader) { name = "OneJS Sky", hideFlags = HideFlags.HideAndDontSave };
            }
            // SetColor takes sRGB and makes it linear in a linear project, as a Light's colour is.
            _sky.SetColor(TopId, new Color(topR, topG, topB, 1));
            _sky.SetColor(HorizonId, new Color(horizonR, horizonG, horizonB, 1));
            _sky.SetColor(BottomId, new Color(bottomR, bottomG, bottomB, 1));
            _sky.SetVector(SunShapeId, new Vector4(Mathf.Max(0, sunSize) * 0.5f * Mathf.Deg2Rad, Mathf.Max(0, sunGlow) * Mathf.Deg2Rad, 0, 0));
            _sky.SetFloat(StarsId, Mathf.Clamp01(stars));

            if (!_camera.TryGetComponent<Skybox>(out var box)) box = _camera.gameObject.AddComponent<Skybox>();
            box.material = _sky;
            box.enabled = true;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _sceneSun = Playing ? null : FindSceneSun();
            HookSky(true);
            UpdateSky();
        }

        internal static Material SkyMaterial => _sky;
        internal static bool SkyHooked => _skyHooked;

        static void StopSky() {
            HookSky(false);
            if (_camera != null) {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                if (_camera.TryGetComponent<Skybox>(out var box)) Kill(box);
            }
            if (_sky != null) Kill(_sky);
            _sky = null;
            _sceneSun = null;
        }

        // Only while a sky is up, so a scene without one has no callback at all.
        static void HookSky(bool on) {
            if (on == _skyHooked) return;
            _skyHooked = on;
            if (on) {
                RenderPipelineManager.beginCameraRendering += SkyFor;
                Camera.onPreCull += SkyFor;
            } else {
                RenderPipelineManager.beginCameraRendering -= SkyFor;
                Camera.onPreCull -= SkyFor;
            }
        }

        static void SkyFor(ScriptableRenderContext _, Camera camera) => SkyFor(camera);

        static void SkyFor(Camera camera) {
            if (camera == _camera) UpdateSky();
        }

        // The sun as it is this frame, so a sun the cart moves takes its disc along. The fog is
        // ModelFog's globals, which it sets each frame for every model and the sky alike.
        static void UpdateSky() {
            if (_sky == null) return;
            var sun = Lit(_sun) ?? Lit(RenderSettings.sun) ?? Lit(_sceneSun);
            if (sun != null) {
                _sky.SetColor(SunColorId, sun.color);
                var toward = -sun.transform.forward;
                _sky.SetVector(SunDirId, new Vector4(toward.x, toward.y, toward.z, 1));
            } else {
                _sky.SetVector(SunDirId, new Vector4(0, 1, 0, 0));
            }
        }

        static Light Lit(Light light) => light != null && light.isActiveAndEnabled && light.type == LightType.Directional ? light : null;

        static Light FindSceneSun() {
            foreach (var light in FindCompat.FindObjectsByType<Light>()) {
                if (light.type == LightType.Directional && light.isActiveAndEnabled) return light;
            }
            return null;
        }
    }
}
