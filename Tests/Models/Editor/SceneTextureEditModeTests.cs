using NUnit.Framework;
using OneJS.Models;
using OneJS.ShaderFX;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.AcquireSceneTexture: the scene drawn into a texture a program reads, by the
    /// first reader, rendered just before the effects draw so they read this frame, and given
    /// back to the screen by the last.
    /// </summary>
    public class SceneTextureEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        [Test]
        public void TheFirstReader_PointsTheCameraAtATexture_AndTheLastGivesItBack() {
            var camera = ModelBridge.SceneCamera;
            Assert.That(camera.targetTexture, Is.Null, "nothing reads it: the scene draws to the screen");
            var first = ModelBridge.AcquireSceneTexture();
            Assert.That(first, Is.Not.Null);
            Assert.That(camera.targetTexture, Is.SameAs(first));
            Assert.That(camera.enabled, Is.False, "rendered by hand, before the effects draw");
            // URP's CoreUtils.GetDefaultDepthStencilFormat for this editor's targets, so the scene's lit
            // pipelines are the ones a cart drawing to the screen compiles (WebGPU's depth32float-stencil8).
            Assert.That(first.depthStencilFormat, Is.EqualTo(GraphicsFormat.D32_SFloat_S8_UInt), "the screen camera's depth and stencil");
            Assert.That(first.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat), "half floats, as the scene was drawn");

            var second = ModelBridge.AcquireSceneTexture();
            Assert.That(second, Is.SameAs(first), "readers share one");
            ModelBridge.ReleaseSceneTexture();
            Assert.That(camera.targetTexture, Is.SameAs(first), "one reader is left");

            ModelBridge.ReleaseSceneTexture();
            Assert.That(camera.targetTexture, Is.Null);
            Assert.That(camera.enabled, Is.True);
            Assert.That(first == null, Is.True, "the texture is freed");
            ModelBridge.ReleaseSceneTexture();
            Assert.That(ModelBridge.SceneReaders, Is.EqualTo(0), "a release too many is no harm");
        }

        [Test]
        public void TheSceneGoing_TakesTheTextureAlong() {
            var texture = ModelBridge.AcquireSceneTexture();
            ModelBridge.DisposeAll();
            Assert.That(texture == null, Is.True);
            Assert.That(ModelBridge.SceneReaders, Is.EqualTo(0));
            ModelBridge.BeginScene();
            Assert.That(ModelBridge.SceneCamera.targetTexture, Is.Null, "the next scene starts on the screen");
        }

        // Each frame's TickAll renders the scene before any effect draws, so what a program reads
        // there is this frame's picture: red, then blue the frame after it turned blue.
        [Test]
        public void EachFrame_TheTextureIsThatFramesScene() {
            var texture = ModelBridge.AcquireSceneTexture();
            var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBAHalf, false);
            try {
                Color Centre() {
                    var was = RenderTexture.active;
                    RenderTexture.active = texture;
                    read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    RenderTexture.active = was;
                    return read.GetPixel(texture.width / 2, texture.height / 2);
                }
                ModelBridge.SetBackground(1, 0, 0);
                Tick();
                Assert.That(Centre().r, Is.GreaterThan(0.9f), "red");
                ModelBridge.SetBackground(0, 0, 1);
                Tick();
                var blue = Centre();
                Assert.That(blue.b, Is.GreaterThan(0.9f), $"blue, not last frame's red: {blue}");
                Assert.That(blue.r, Is.LessThan(0.1f), $"blue, not last frame's red: {blue}");
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void NothingRenders_WhileNothingReads() {
            int renders = 0;
            void Count(Camera c) { if (c == ModelBridge.SceneCamera) renders++; }
            Camera.onPreRender += Count;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += CountSrp;
            void CountSrp(UnityEngine.Rendering.ScriptableRenderContext _, Camera c) => Count(c);
            try {
                ModelBridge.AcquireSceneTexture();
                ModelBridge.ReleaseSceneTexture();
                Tick();
                Assert.That(renders, Is.EqualTo(0), "the last reader gone, the effects' tick renders nothing");
            } finally {
                Camera.onPreRender -= Count;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= CountSrp;
            }
        }

        // TickAll counts a call within half a millisecond of the last as the same frame.
        static void Tick() {
            System.Threading.Thread.Sleep(2);
            ShaderEffectBridge.TickAll();
        }
    }
}
