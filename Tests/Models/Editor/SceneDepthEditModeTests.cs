using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.AcquireSceneDepth: each pixel's distance in metres from the camera to the opaque
    /// surface along its ray, for a program to read beside the scene's colour. The first reader
    /// asks for the camera's depth texture and makes the texture, the last frees both.
    /// </summary>
    public class SceneDepthEditModeTests {
        // Odd, so the centre pixel looks exactly where the camera does.
        const int Size = 65;
        const float Wall = 10;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(0, 0, 0, 0, 0, 1, 60);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        [Test]
        public void TheFirstReader_MakesTheTextureAndAsksForDepth_AndTheLastFreesBoth() {
            Assert.That(ModelBridge.DepthWanted, Is.False);
            var first = ModelBridge.AcquireSceneDepth();
            Assert.That(first, Is.Not.Null, "URP is in this project, so the depth is drawn");
            Assert.That(first.graphicsFormat, Is.EqualTo(GraphicsFormat.R16_SFloat), "metres as half floats, one channel");
            Assert.That(first.depthStencilFormat, Is.EqualTo(GraphicsFormat.None), "written by a full-screen pass, so no depth buffer of its own");
            Assert.That(ModelBridge.DepthWanted, Is.True, "the camera's depth texture is asked for while it is read");

            var second = ModelBridge.AcquireSceneDepth();
            Assert.That(second, Is.SameAs(first), "readers share one");
            ModelBridge.ReleaseSceneDepth();
            Assert.That(ModelBridge.DepthWanted, Is.True, "one reader is left");

            ModelBridge.ReleaseSceneDepth();
            Assert.That(ModelBridge.DepthWanted, Is.False);
            Assert.That(first == null, Is.True, "the texture is freed");
            ModelBridge.ReleaseSceneDepth();
            Assert.That(ModelBridge.SceneDepthReaders, Is.EqualTo(0), "a release too many is no harm");
        }

        [Test]
        public void TheSceneGoing_TakesTheTextureAlong() {
            var texture = ModelBridge.AcquireSceneDepth();
            ModelBridge.DisposeAll();
            Assert.That(texture == null, Is.True);
            Assert.That(ModelBridge.SceneDepthReaders, Is.EqualTo(0));
            Assert.That(ModelBridge.DepthWanted, Is.False);
        }

        [Test]
        public void ReleasingTheDepth_LeavesAMeshThatWantsItAlone() {
            var key = new object();
            ModelBridge.WantDepth(key, true);
            ModelBridge.AcquireSceneDepth();
            ModelBridge.ReleaseSceneDepth();
            Assert.That(ModelBridge.DepthWanted, Is.True, "a softened mesh still asks for the depth");
            ModelBridge.WantDepth(key, false);
        }

        // A wall across the left half of the view, 10 m ahead, and the sky to its right. Straight
        // ahead the wall is 10 m off; a pixel looking aside reads the longer way along its ray,
        // the radial distance, not the eye's z. The sky reads the camera's far distance.
        [Test]
        public void EachPixel_IsTheDistanceAlongItsRay_AndTheSkyIsTheFarDistance() {
            var camera = ModelBridge.SceneCamera;
            var metres = Read(SpawnWall());
            float At(int x, int y) => metres[y * Size + x];
            // The wall's edge runs down the middle column, so read just left of it.
            int left = Size / 2 - 1;
            Assert.That(At(left, Size / 2), Is.EqualTo(Expected(camera, left, Size / 2)).Within(0.02f), "near straight ahead, about 10 m");
            int corner = 2;
            float aside = Expected(camera, corner, corner);
            Assert.That(aside, Is.GreaterThan(Wall * 1.2f), "the corner's ray is much longer than the eye's z");
            Assert.That(At(corner, corner), Is.EqualTo(aside).Within(aside * 0.002f), "the distance along the corner pixel's ray");
            Assert.That(At(Size - 3, Size / 2), Is.EqualTo(camera.farClipPlane).Within(1f), "the sky reads the far distance");
        }

        [Test]
        public void TheTexture_IsSizedToWhatTheCameraDraws() {
            var camera = ModelBridge.SceneCamera;
            var depth = ModelBridge.AcquireSceneDepth();
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try {
                camera.targetTexture = target;
                camera.Render();
                Assert.That(depth.width, Is.EqualTo(Size));
                Assert.That(depth.height, Is.EqualTo(Size));
            } finally {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                ModelBridge.ReleaseSceneDepth();
            }
        }

        // Rays run parallel from an orthographic camera, so every pixel on a wall square to the view
        // reads the distance from the camera's plane: 10 m, corner and middle alike.
        [Test]
        public void AnOrthographicCamera_ReadsTheDistanceFromItsPlane() {
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 5;
            var metres = Read(SpawnWall());
            int left = Size / 2 - 1;
            Assert.That(metres[(Size / 2) * Size + left], Is.EqualTo(Wall).Within(0.02f), "the middle");
            Assert.That(metres[2 * Size + 2], Is.EqualTo(Wall).Within(0.02f), "a corner, no further than the middle");
            Assert.That(metres[(Size / 2) * Size + Size - 3], Is.EqualTo(camera.farClipPlane).Within(1f), "the sky reads the far distance");
        }

        // Where a GPU cannot draw into half floats, the depth is full floats and reads the same.
        [Test]
        public void FullFloats_WhereHalvesCannotBeDrawn_ReadTheSame() {
            var was = ModelBridge.SceneDepthFormats;
            ModelBridge.SceneDepthFormats = new[] { GraphicsFormat.R32_SFloat };
            try {
                var actor = SpawnWall();
                Assert.That(ModelBridge.AcquireSceneDepth().graphicsFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
                ModelBridge.ReleaseSceneDepth();
                var metres = Read(actor);
                int left = Size / 2 - 1;
                var camera = ModelBridge.SceneCamera;
                Assert.That(metres[(Size / 2) * Size + left], Is.EqualTo(Expected(camera, left, Size / 2)).Within(0.002f));
            } finally {
                ModelBridge.SceneDepthFormats = was;
            }
        }

        static int SpawnWall() {
            int wall = ModelBridge.CreateMesh(false, false, false, 0.5f, 0.5f, 0.5f, 1);
            var positions = new[] {
                new Vector3(-100, -100, Wall), new Vector3(-100, 100, Wall), new Vector3(0, 100, Wall), new Vector3(0, -100, Wall),
            };
            Assert.That(ModelBridge.SetMesh(wall, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            return ModelBridge.Spawn(wall, 0, 0, 0, 0, 1, false, false);
        }

        // One render of the scene camera into a Size square, and scene.depth read back as floats.
        static float[] Read(int actor) {
            var camera = ModelBridge.SceneCamera;
            var depth = ModelBridge.AcquireSceneDepth();
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try {
                camera.targetTexture = target;
                camera.Render();
                var read = AsyncGPUReadback.Request(depth, 0, TextureFormat.RFloat);
                read.WaitForCompletion();
                Assert.That(read.hasError, Is.False);
                return read.GetData<float>().ToArray();
            } finally {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                ModelBridge.Destroy(actor);
                ModelBridge.ReleaseSceneDepth();
            }
        }

        // The distance to the wall along the ray through a pixel's centre, for a camera at the
        // origin looking along z into the square it renders, whose aspect is 1.
        static float Expected(Camera camera, int x, int y) {
            float tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float ax = ((x + 0.5f) / Size * 2 - 1) * tan, ay = ((y + 0.5f) / Size * 2 - 1) * tan;
            return Wall * Mathf.Sqrt(1 + ax * ax + ay * ay);
        }
    }
}
