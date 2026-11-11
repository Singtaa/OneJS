using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace OneJS.Models {
    /// <summary>
    /// scene.depth on URP (ModelBridge.AcquireSceneDepth): one full-screen pass on the scene's
    /// camera, after the opaques, that reads URP's depth texture and writes each pixel's distance
    /// along its ray in metres into the bridge's texture. Enqueued from the camera's own begin, so
    /// it needs no renderer feature in the project's renderer, and only while a program reads it.
    /// The Play container's URP asset asks for the depth texture every frame, so the pass is the
    /// only new work: one read and one write a pixel.
    /// </summary>
    sealed class UrpSceneDepth : ISceneDepthPipeline {
        static readonly int DepthId = Shader.PropertyToID("_OjDepth");
        static readonly int RayId = Shader.PropertyToID("_OjDepthRay");
        static readonly int ClipId = Shader.PropertyToID("_OjDepthClip");

        Camera _camera;
        RenderTexture _target;
        RTHandle _handle;
        DistancePass _pass;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Join() {
            if (ModelBridge.SceneDepthPipeline is not UrpSceneDepth) ModelBridge.SceneDepthPipeline = new UrpSceneDepth();
        }

        public void Begin(Camera camera, RenderTexture target) {
            End();
            var shader = Resources.Load<Shader>("OneJS/SceneDepth");
            if (shader == null) {
                Debug.LogWarning("[OneJS] scene.depth's shader (OneJS/SceneDepth) is missing from this build, so the program reads nothing.");
                return;
            }
            _camera = camera;
            _target = target;
            _handle = RTHandles.Alloc(target);
            _pass = new DistancePass(this, new Material(shader) { name = "OneJS Scene Depth", hideFlags = HideFlags.HideAndDontSave });
            RenderPipelineManager.beginCameraRendering += Enqueue;
        }

        public void End() {
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            _handle?.Release();
            _handle = null;
            if (_pass != null) Object.DestroyImmediate(_pass.Material);
            _pass = null;
            _camera = null;
            _target = null;
        }

        void Enqueue(ScriptableRenderContext _, Camera camera) {
            if (camera != _camera || _target == null) return;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer?.EnqueuePass(_pass);
        }

        // Kept the size the camera draws at, in place, so a program's binding stays good.
        void Fit(int width, int height) {
            if (_target.width == width && _target.height == height) return;
            _target.Release();
            _target.width = width;
            _target.height = height;
            _target.Create();
        }

        sealed class DistancePass : ScriptableRenderPass {
            readonly UrpSceneDepth _owner;
            public readonly Material Material;

            sealed class Data {
                public TextureHandle depth;
                public Material material;
                public Vector4 ray, clip;
                public MaterialPropertyBlock props;
            }

            readonly MaterialPropertyBlock _props = new();

            public DistancePass(UrpSceneDepth owner, Material material) {
                _owner = owner;
                Material = material;
                // After the opaques and URP's depth copy, before anything see-through, which the
                // depth leaves out.
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame) {
                var resources = frame.Get<UniversalResourceData>();
                var cameraData = frame.Get<UniversalCameraData>();
                var depth = resources.cameraDepthTexture;
                if (!depth.IsValid() || _owner._target == null) return;
                var size = cameraData.cameraTargetDescriptor;
                _owner.Fit(size.width, size.height);
                var target = graph.ImportTexture(_owner._handle);

                var camera = cameraData.camera;
                var projection = camera.projectionMatrix;
                using var builder = graph.AddRasterRenderPass<Data>("OneJS Scene Depth", out var data);
                data.depth = depth;
                data.material = Material;
                data.props = _props;
                // A view ray with z = 1 is (x / P00, y / P11, 1) at normalized x and y.
                data.ray = new Vector4(1 / projection.m00, 1 / projection.m11, camera.orthographic ? 1 : 0, 0);
                data.clip = new Vector4(camera.nearClipPlane, camera.farClipPlane, 1f / size.width, 1f / size.height);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) => {
                    d.props.SetTexture(DepthId, d.depth);
                    d.props.SetVector(RayId, d.ray);
                    d.props.SetVector(ClipId, d.clip);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.props);
                });
            }
        }
    }
}
