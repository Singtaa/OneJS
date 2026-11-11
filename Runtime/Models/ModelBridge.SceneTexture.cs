using OneJS.ShaderFX;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OneJS.Models {
    public static partial class ModelBridge {
        static RenderTexture _sceneTexture;
        static int _sceneReaders;
        // Play mode only: while the scene draws into a texture, something still has to clear the
        // screen, since the bridge switched off every other camera that would have.
        static Camera _clear;

        /// <summary>
        /// The scene as a texture, for a program to read (onejs-play's `scene.texture`): the first
        /// reader points the scene's camera at a screen-sized texture and renders it by hand just
        /// before the effects draw each frame, so a program reads this frame's picture; the last
        /// <see cref="ReleaseSceneTexture"/> puts the camera back on the screen and frees the
        /// texture. Readers share one texture, resized in place when the screen changes, so a
        /// program's binding stays good.
        /// </summary>
        public static RenderTexture AcquireSceneTexture() {
            BeginScene();
            if (_sceneReaders++ > 0) return _sceneTexture;
            var size = ScreenPixels();
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            var desc = new RenderTextureDescriptor(size.x, size.y, format, 0) {
                sRGB = false, depthStencilFormat = SceneTextureDepthFormat,
            };
            _sceneTexture = new RenderTexture(desc) {
                name = "OneJS Scene", hideFlags = HideFlags.HideAndDontSave,
            };
            _sceneTexture.Create();
            _camera.targetTexture = _sceneTexture;
            _camera.enabled = false;
            if (Playing) {
                var go = new GameObject("OneJS Clear");
                go.transform.SetParent(Root, false);
                _clear = go.AddComponent<Camera>();
                _clear.cullingMask = 0;
                _clear.clearFlags = CameraClearFlags.SolidColor;
                _clear.backgroundColor = _camera.backgroundColor;
                _clear.depth = _camera.depth;
            }
            ShaderEffectBridge.BeforeDraw += RenderScene;
            return _sceneTexture;
        }

        /// <summary>One reader done. The last puts the camera back on the screen and frees the texture.</summary>
        public static void ReleaseSceneTexture() {
            if (_sceneReaders == 0 || --_sceneReaders > 0) return;
            StopSceneTexture(restoreCamera: true);
        }

        // The depth URP gives a camera that draws to the screen (CoreUtils.GetDefaultDepthStencilFormat),
        // so the scene's lit pipelines drawn into this texture are the ones a cart without it compiles.
        // OneJS.Runtime references no render pipeline, hence the copy rather than the call.
        internal static GraphicsFormat SceneTextureDepthFormat =>
#if UNITY_SWITCH || UNITY_SWITCH2 || UNITY_EMBEDDED_LINUX || UNITY_QNX || UNITY_ANDROID
            GraphicsFormat.D24_UNorm_S8_UInt;
#else
            GraphicsFormat.D32_SFloat_S8_UInt;
#endif

        internal static int SceneReaders => _sceneReaders;

        /// <summary>
        /// The camera that only clears the screen while the scene draws into a texture, or null.
        /// It draws nothing, so a pipeline's own assembly keeps it from doing more than clear
        /// (UrpClearCamera).
        /// </summary>
        internal static Camera ClearCamera => _clear;
        internal static RenderTexture SceneTexture => _sceneTexture;

        static void StopSceneTexture(bool restoreCamera) {
            ShaderEffectBridge.BeforeDraw -= RenderScene;
            _sceneReaders = 0;
            if (restoreCamera && _camera != null) {
                _camera.targetTexture = null;
                _camera.enabled = true;
            }
            if (_clear != null) Kill(_clear.gameObject);
            _clear = null;
            if (_sceneTexture != null) {
                _sceneTexture.Release();
                Kill(_sceneTexture);
            }
            _sceneTexture = null;
        }

        static void RenderScene() {
            if (_camera == null || _sceneTexture == null) return;
            var size = ScreenPixels();
            if (size.x != _sceneTexture.width || size.y != _sceneTexture.height) {
                // The same texture, so every program reading it keeps its binding.
                _sceneTexture.Release();
                _sceneTexture.width = size.x;
                _sceneTexture.height = size.y;
                _sceneTexture.Create();
            }
            if (_clear != null) { _clear.backgroundColor = _camera.backgroundColor; _clear.enabled = !ArmNoClearCamera; }
            _camera.Render();
        }

        // The screen in physical pixels. Outside play mode Screen can report a window that is not the
        // Game view's, or nothing at all; a texture is still made, at a size that draws.
        static Vector2Int ScreenPixels() {
            int w = Screen.width, h = Screen.height;
            return w > 0 && h > 0 ? new Vector2Int(w, h) : new Vector2Int(1280, 720);
        }
    }
}
