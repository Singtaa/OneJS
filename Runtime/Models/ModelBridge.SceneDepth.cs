using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OneJS.Models {
    /// <summary>
    /// The drawing of scene.depth, which only a render pipeline can do: it reads the camera's
    /// depth texture inside the camera's render. OneJS.Runtime.Models.URP registers one where URP
    /// is installed; elsewhere scene.depth reads nothing.
    /// </summary>
    public interface ISceneDepthPipeline {
        /// <summary>Starts writing each render of `camera` into `target`, resizing it to what the camera draws.</summary>
        void Begin(Camera camera, RenderTexture target);
        void End();
    }

    public static partial class ModelBridge {
        /// <summary>Set by the pipeline's assembly when it loads; null where the pipeline is not URP.</summary>
        public static ISceneDepthPipeline SceneDepthPipeline { get; set; }

        static readonly object DepthReader = new();
        static RenderTexture _sceneDepth;
        static int _sceneDepthReaders;
        static bool _saidNoSceneDepth;

        /// <summary>
        /// The scene's distances, for a program to read (onejs-play's `scene.depth`): in its red
        /// channel, the metres from the camera to the opaque surface along each pixel's ray, and
        /// the camera's far distance where nothing is. Written after the opaques each time the
        /// scene's camera renders, so a program reading it beside scene.texture reads the same
        /// frame, and one reading it alone reads the frame before. Readers share one texture,
        /// resized in place, so a program's binding stays good; the last <see cref="ReleaseSceneDepth"/>
        /// frees it. Null where no pipeline can draw it.
        /// </summary>
        public static RenderTexture AcquireSceneDepth() {
            BeginScene();
            if (_sceneDepthReaders > 0) {
                _sceneDepthReaders++;
                return _sceneDepth;
            }
            if (SceneDepthPipeline == null) {
                if (!_saidNoSceneDepth) Debug.LogWarning("[OneJS] scene.depth is drawn on URP only, so the program reads nothing here.");
                _saidNoSceneDepth = true;
                return null;
            }
            var format = SceneDepthFormat;
            if (format == GraphicsFormat.None) {
                if (!_saidNoSceneDepth) Debug.LogWarning("[OneJS] scene.depth needs a float render texture, which this GPU cannot draw into.");
                _saidNoSceneDepth = true;
                return null;
            }
            _sceneDepthReaders = 1;
            var size = ScreenPixels();
            _sceneDepth = new RenderTexture(new RenderTextureDescriptor(size.x, size.y, format, GraphicsFormat.None)) {
                name = "OneJS Scene Depth", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point,
            };
            _sceneDepth.Create();
            WantDepth(DepthReader, true);
            SceneDepthPipeline.Begin(_camera, _sceneDepth);
            return _sceneDepth;
        }

        /// <summary>One reader done. The last stops the pass and frees the texture.</summary>
        public static void ReleaseSceneDepth() {
            if (_sceneDepthReaders == 0 || --_sceneDepthReaders > 0) return;
            StopSceneDepth();
        }

        internal static int SceneDepthReaders => _sceneDepthReaders;

        // Half floats step 2 mm at 2 m and 2 m at 2,900 m, enough for fog and edges; full floats
        // where a GPU cannot draw into halves. The first this GPU draws into is taken; a test
        // narrows the list to reach the fallback.
        internal static GraphicsFormat[] SceneDepthFormats = { GraphicsFormat.R16_SFloat, GraphicsFormat.R32_SFloat };

        static GraphicsFormat SceneDepthFormat {
            get {
                foreach (var f in SceneDepthFormats)
                    if (SystemInfo.IsFormatSupported(f, GraphicsFormatUsage.Render)) return f;
                return GraphicsFormat.None;
            }
        }

        static void StopSceneDepth() {
            _sceneDepthReaders = 0;
            if (_sceneDepth == null) return;
            SceneDepthPipeline?.End();
            WantDepth(DepthReader, false);
            _sceneDepth.Release();
            Kill(_sceneDepth);
            _sceneDepth = null;
        }
    }
}
