using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OneJS.Tests.Models {
    /// <summary>
    /// While a program reads the scene (ModelBridge.AcquireSceneTexture), the scene's camera draws
    /// into a texture and a second camera only clears the screen. URP ran every renderer feature
    /// for that empty camera too: SSAO's depth-normals prepass and AO passes, and the opaque and
    /// depth copies the asset asks for, 8 of a Play container's 21 passes a frame. A probe feature
    /// on every renderer records the cameras it runs for.
    /// </summary>
    public class ClearCameraPlayModeTests {
        sealed class Probe : ScriptableRendererFeature {
            public readonly List<Camera> Cameras = new();
            public override void Create() { }
            public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) => Cameras.Add(data.cameraData.camera);
        }

        UniversalRenderPipelineAsset _asset;
        Probe _probe;

        [SetUp]
        public void Begin() {
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(_asset, Is.Not.Null, "this project does not draw with URP");
            _probe = ScriptableObject.CreateInstance<Probe>();
            _probe.name = "ClearCameraProbe";
            foreach (var data in _asset.rendererDataList) {
                if (data == null) continue;
                data.rendererFeatures.Add(_probe);
                data.SetDirty();
            }
            ModelBridge.BeginScene();
            ModelBridge.AcquireSceneTexture();
        }

        [TearDown]
        public void Dispose() {
            ModelBridge.DisposeAll();
            foreach (var data in _asset.rendererDataList) {
                if (data == null) continue;
                data.rendererFeatures.Remove(_probe);
                data.SetDirty();
            }
            Object.DestroyImmediate(_probe);
        }

        // Into a texture for the test's render: a screen camera rendered by hand in batch mode trips
        // URP's checks of the final blit against the batch backbuffer, with or without this fix. The
        // fix keys on the camera, not on what it draws into.
        static void RenderTheClearCamera(Camera clear) {
            var target = new RenderTexture(64, 64, 24);
            clear.targetTexture = target;
            try {
                clear.Render();
            } finally {
                clear.targetTexture = null;
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void TheClearCamera_RunsNoRendererFeature() {
            var clear = ModelBridge.ClearCamera;
            Assert.That(clear, Is.Not.Null, "reading the scene in play mode makes a camera to clear the screen");
            RenderTheClearCamera(clear);
            Assert.That(_probe.Cameras, Has.No.Member(clear), "a feature ran for the camera that only clears the screen");
            Assert.That(_probe.isActive, Is.True, "the feature stayed off after the clear camera's render, so every other camera lost it");
        }

        [Test]
        public void TheClearCamera_AsksForNoCopies() {
            var clear = ModelBridge.ClearCamera;
            RenderTheClearCamera(clear);
            var data = clear.GetUniversalAdditionalCameraData();
            Assert.That(data.requiresColorOption, Is.EqualTo(CameraOverrideOption.Off), "the opaque texture is copied for an empty view");
            Assert.That(data.requiresDepthOption, Is.EqualTo(CameraOverrideOption.Off), "the depth texture is made for an empty view");
            Assert.That(data.renderPostProcessing, Is.False);
        }

        [Test]
        public void TheScenesCamera_StillRunsThem() {
            ModelBridge.SceneCamera.Render();
            Assert.That(_probe.Cameras, Has.Member(ModelBridge.SceneCamera), "the scene's own camera lost its renderer features");
        }
    }
}
