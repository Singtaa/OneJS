using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// Whether the scene camera's depth texture is on URP: UniversalAdditionalCameraData's
    /// requiresDepthTexture, from OneJS.Runtime.Models.URP, which registers itself where URP is
    /// installed. The built-in pipeline's is the camera's own depthTextureMode, which needs no pipeline.
    /// </summary>
    public interface ICameraDepthPipeline {
        bool Get(Camera camera);
        void Set(Camera camera, bool on);
    }

    /// <summary>
    /// The camera's depth texture, asked for while anything in the scene reads it: a see-through
    /// mesh with `soften` or `deepColor` (ModelSurface.hlsl), and scene.depth. One switch for all of
    /// them, kept by who asked, so one letting go never turns it off under another, and what the
    /// camera had before the first asked is put back when the last lets go. The Play container
    /// has the depth every frame already (its URP asset asks for it), so there this changes
    /// nothing; a Unity project's own asset may not ask for it.
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>Set by the pipeline's assembly when it loads; null where the pipeline is not URP.</summary>
        public static ICameraDepthPipeline CameraDepthPipeline { get; set; }

        static readonly HashSet<object> _depthWanted = new();
        static Camera _depthCamera;
        static bool _depthPipelineWas;
        static DepthTextureMode _depthModeWas;

        /// <summary>Asks for the scene camera's depth texture on behalf of `who`, or lets it go.</summary>
        internal static void WantDepth(object who, bool on) {
            if (on ? _depthWanted.Add(who) : _depthWanted.Remove(who)) ApplyDepth();
        }

        /// <summary>Whether anything asks for the depth texture now.</summary>
        internal static bool DepthWanted => _depthWanted.Count > 0;

        // Turns the depth on for the scene camera while anything wants it, and puts back what the
        // camera had when nothing does, or when the scene's camera is a new one.
        static void ApplyDepth() {
            bool want = _depthWanted.Count > 0 && _camera != null;
            if (_depthCamera != null && (!want || _depthCamera != _camera)) {
                _depthCamera.depthTextureMode = _depthModeWas;
                CameraDepthPipeline?.Set(_depthCamera, _depthPipelineWas);
            }
            if (!want || _depthCamera == _camera) {
                if (!want) _depthCamera = null;
                return;
            }
            _depthCamera = _camera;
            _depthModeWas = _camera.depthTextureMode;
            _depthPipelineWas = CameraDepthPipeline?.Get(_camera) ?? false;
            _camera.depthTextureMode |= DepthTextureMode.Depth;
            CameraDepthPipeline?.Set(_camera, true);
        }

        // Nothing asks once the scene goes: its camera and its meshes go with it.
        static void ForgetDepth() {
            _depthWanted.Clear();
            ApplyDepth();
            _depthCamera = null;
        }
    }
}
