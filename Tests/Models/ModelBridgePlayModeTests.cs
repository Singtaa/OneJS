using System.Collections;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge in play mode and in a player, where a cart's scene is the scene: the bridge
    /// switches off the cameras and suns already there so nothing draws or lights twice, sets the
    /// ambient and fog the cart asked for, and gives all of it back when the scene is disposed
    /// (a hot reload, or leaving play mode).
    /// </summary>
    public class ModelBridgePlayModeTests {
        Camera _camera;
        Light _light;

        [SetUp]
        public void MakeScene() {
            _camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            _light = new GameObject("Directional Light").AddComponent<Light>();
            _light.type = LightType.Directional;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);
        }

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            Object.Destroy(_camera.gameObject);
            Object.Destroy(_light.gameObject);
        }

        [UnityTest]
        public IEnumerator PlayMode_TakesTheSceneOverAndGivesItBack() {
            var probe = RenderSettings.ambientProbe;
            var sun = RenderSettings.sun;

            ModelBridge.BeginScene();
            ModelBridge.SetSun(true, 0, -1, 1, 1, 1, 1, 1.2f, true);
            ModelBridge.SetAmbient(0.6f, 0.7f, 0.9f, 0.3f, 0.25f, 0.2f, 1);
            ModelBridge.SetFog(true, 0.5f, 0.5f, 0.5f, 3, 20);
            yield return null;

            Assert.That(_camera.enabled, Is.False, "the scene's camera would draw under the bridge's");
            Assert.That(_light.enabled, Is.False, "the scene's sun would light every model twice");
            Assert.That(ModelBridge.SceneCamera.enabled, Is.True);
            Assert.That(RenderSettings.sun, Is.Not.EqualTo(_light));
            Assert.That(RenderSettings.sun.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(RenderSettings.ambientProbe, Is.Not.EqualTo(probe));
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(20f));

            ModelBridge.DisposeAll();
            yield return null;

            Assert.That(_camera.enabled, Is.True);
            Assert.That(_light.enabled, Is.True);
            Assert.That(RenderSettings.sun, Is.EqualTo(sun));
            Assert.That(RenderSettings.ambientProbe, Is.EqualTo(probe));
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
            Assert.That(RenderSettings.fog, Is.False);
        }

        [UnityTest]
        public IEnumerator PlayMode_LeavesRenderTextureCamerasRunning() {
            var offscreen = new GameObject("Minimap").AddComponent<Camera>();
            offscreen.targetTexture = new RenderTexture(16, 16, 16);
            ModelBridge.BeginScene();
            yield return null;
            Assert.That(offscreen.enabled, Is.True, "a camera drawing into a texture is not competing for the screen");
            ModelBridge.DisposeAll();
            Object.Destroy(offscreen.targetTexture);
            Object.Destroy(offscreen.gameObject);
        }
    }
}
