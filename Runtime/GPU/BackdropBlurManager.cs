using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.GPU {
    /// <summary>
    /// Manages screen capture and Gaussian blur for frosted glass UI elements.
    /// Auto-creates itself when the first FrostedGlassElement registers;
    /// auto-destroys when the last one unregisters. Users never touch this directly.
    ///
    /// Uses a lightweight clone camera to render ONLY the 3D scene (no UI) into
    /// a reduced-resolution RT, then applies a two-pass Gaussian blur.
    /// This is pipeline-agnostic (works with Built-in, URP, HDRP).
    /// </summary>
    public class BackdropBlurManager : MonoBehaviour {
        static BackdropBlurManager _instance;

        readonly HashSet<FrostedGlassElement> _elements = new HashSet<FrostedGlassElement>();

        RenderTexture _captureRT;
        RenderTexture _blurTemp;
        RenderTexture _blurResult;
        Material _blurMat;
        int _sigmaId;
        Camera _captureCam;

        // Downsample factor (2 = half res, 4 = quarter res)
        const int DownsampleFactor = 2;

        public static void Register(FrostedGlassElement element) {
            EnsureInstance();
            _instance._elements.Add(element);
        }

        public static void Unregister(FrostedGlassElement element) {
            if (_instance == null) return;
            _instance._elements.Remove(element);
            if (_instance._elements.Count == 0) {
                SafeDestroy(_instance.gameObject);
                _instance = null;
            }
        }

        static void EnsureInstance() {
            if (_instance != null) return;

            var go = new GameObject("[OneJS BackdropBlurManager]");
            go.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            _instance = go.AddComponent<BackdropBlurManager>();
            _instance.Initialize();
        }

        void Initialize() {
            var shader = Shader.Find("Hidden/OneJS/BackdropBlur");
            if (shader == null) {
                Debug.LogError("[BackdropBlurManager] Could not find BackdropBlur shader. " +
                    "Make sure BackdropBlur.shader is in a Resources folder.");
                return;
            }

            _blurMat = new Material(shader) {
                hideFlags = HideFlags.HideAndDontSave
            };
            _sigmaId = Shader.PropertyToID("_Sigma");
        }

        void OnDestroy() {
            DestroyCaptureCamera();
            ReleaseRTs();

            if (_blurMat != null) {
                SafeDestroy(_blurMat);
                _blurMat = null;
            }
        }

        static void SafeDestroy(Object obj) {
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        void EnsureCaptureCamera() {
            if (_captureCam != null) return;

            var go = new GameObject("[OneJS Capture Cam]");
            go.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            _captureCam = go.AddComponent<Camera>();
            _captureCam.enabled = false; // Manual render only
        }

        void DestroyCaptureCamera() {
            if (_captureCam != null) {
                SafeDestroy(_captureCam.gameObject);
                _captureCam = null;
            }
        }

        void ReleaseRTs() {
            if (_captureRT != null) { _captureRT.Release(); SafeDestroy(_captureRT); _captureRT = null; }
            if (_blurTemp != null) { _blurTemp.Release(); SafeDestroy(_blurTemp); _blurTemp = null; }
            if (_blurResult != null) { _blurResult.Release(); SafeDestroy(_blurResult); _blurResult = null; }
        }

        void EnsureRTs(int screenW, int screenH) {
            int w = screenW / DownsampleFactor;
            int h = screenH / DownsampleFactor;
            if (w <= 0 || h <= 0) return;

            if (_captureRT != null && _captureRT.width == w && _captureRT.height == h)
                return;

            ReleaseRTs();

            // Capture at reduced resolution — it's going to be blurred anyway
            _captureRT = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _captureRT.Create();

            _blurTemp = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _blurTemp.Create();

            _blurResult = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _blurResult.Create();
        }

        void LateUpdate() {
            if (_elements.Count == 0 || _blurMat == null) return;

            var mainCam = Camera.main;
            if (mainCam == null) return;

            int screenW = mainCam.pixelWidth;
            int screenH = mainCam.pixelHeight;
            EnsureRTs(screenW, screenH);
            if (_captureRT == null) return;
            EnsureCaptureCamera();

            // Find max blur radius
            float maxSigma = 0f;
            foreach (var el in _elements) {
                if (el.BlurRadius > maxSigma)
                    maxSigma = el.BlurRadius;
            }

            // Clone main camera settings and render 3D scene only into our RT.
            // UITK panels are NOT rendered per-camera — they composite on the
            // display surface. So the clone captures pure 3D scene content.
            _captureCam.CopyFrom(mainCam);
            _captureCam.targetTexture = _captureRT;
            _captureCam.enabled = false;
            // Force alpha=1 on clear color — URP defaults to alpha=0 which causes
            // sharp edges in the blur where geometry meets empty space
            var bg = _captureCam.backgroundColor;
            _captureCam.backgroundColor = new Color(bg.r, bg.g, bg.b, 1f);
            _captureCam.Render();

            // Apply two-pass Gaussian blur (skip if blur is zero — use raw capture)
            RenderTexture result;
            if (maxSigma > 0f) {
                float downsampledSigma = maxSigma / DownsampleFactor;
                _blurMat.SetFloat(_sigmaId, downsampledSigma);
                Graphics.Blit(_captureRT, _blurTemp, _blurMat, 0); // Horizontal
                Graphics.Blit(_blurTemp, _blurResult, _blurMat, 1); // Vertical
                result = _blurResult;
            } else {
                result = _captureRT;
            }

            // Update all registered elements
            foreach (var el in _elements) {
                el.UpdateBlurredBackground(result, screenW, screenH);
            }
        }
    }
}
