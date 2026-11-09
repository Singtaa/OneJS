using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The sun's shadow settings from a cart (ModelBridge.SetSunShadows), on URP's asset and the
    /// sun: a cart that asks for nothing leaves the project's own exactly as they were, one that
    /// asks changes them, and they go back with the scene.
    /// </summary>
    public class SunShadowsPlayModeTests {
        UniversalRenderPipelineAsset _asset;
        (float distance, int cascades, int resolution) _before;

        [Test]
        public void ThePipelinesAssembly_IsAlwaysLinked_SinceNothingReferencesIt() {
            var pipeline = ModelBridge.SunShadowPipeline;
            Assert.That(pipeline, Is.Not.Null, "URP's assembly did not join ModelBridge on load");
            Assert.That(pipeline.GetType().Assembly.IsDefined(typeof(UnityEngine.Scripting.AlwaysLinkAssemblyAttribute), false), Is.True,
                "a player build's linker drops an assembly nothing references, with its load hook, so the sun's shadow settings are refused in the player");
        }

        [SetUp]
        public void Begin() {
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(_asset, Is.Not.Null, "this project does not draw with URP");
            _before = Now();
            ModelBridge.BeginScene();
            ModelBridge.SetSun(true, 0, -1, 1, 1, 1, 1, 1, true);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        (float, int, int) Now() => (_asset.shadowDistance, _asset.shadowCascadeCount, _asset.mainLightShadowmapResolution);

        static Light Sun() => Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l => l.type == LightType.Directional && l.enabled);

        static SoftShadowQuality Quality(Light sun) => sun.TryGetComponent<UniversalAdditionalLightData>(out var data) ? data.softShadowQuality : SoftShadowQuality.UsePipelineSettings;

        [Test]
        public void AskingForNothing_LeavesTheProjectsOwn() {
            Assert.That(ModelBridge.SetSunShadows(-1, -1, -1, -1), Is.Empty);
            Assert.That(Now(), Is.EqualTo(_before));
            Assert.That(Sun().shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(Quality(Sun()), Is.EqualTo(SoftShadowQuality.UsePipelineSettings));
        }

        [Test]
        public void WhatACartAsksFor_IsSet_ThenThePipelinesOwnComeBack_WhenItStopsAsking_AndWithTheScene() {
            Assert.That(ModelBridge.SetSunShadows(150, 4, 2, 4096), Is.Empty);
            Assert.That(Now(), Is.EqualTo((150f, 4, 4096)));
            Assert.That(Sun().shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(Quality(Sun()), Is.EqualTo(SoftShadowQuality.Medium));

            Assert.That(ModelBridge.SetSunShadows(-1, -1, 0, -1), Is.Empty);
            Assert.That(Now(), Is.EqualTo(_before), "what it no longer asks for is the project's own again");
            Assert.That(Sun().shadows, Is.EqualTo(LightShadows.Hard));

            Assert.That(ModelBridge.SetSunShadows(600, 1, 3, 1024), Is.Empty);
            ModelBridge.DisposeAll();
            Assert.That(Now(), Is.EqualTo(_before), "the scene took its settings with it");
        }

        [Test]
        public void WhatCannotBeSet_IsRefused_SayingWhy() {
            Assert.That(ModelBridge.SetSunShadows(0, -1, -1, -1), Does.Contain("distance is 0"));
            Assert.That(ModelBridge.SetSunShadows(-1, 5, -1, -1), Does.Contain("cascades is 5"));
            Assert.That(ModelBridge.SetSunShadows(-1, -1, 4, -1), Does.Contain("softness is 4"));
            Assert.That(ModelBridge.SetSunShadows(-1, -1, -1, 3000), Does.Contain("resolution is 3000"));
            Assert.That(Now(), Is.EqualTo(_before));
        }
    }
}
