using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A scene's ambient occlusion (ModelBridge.SetAmbientOcclusion, UrpAmbientOcclusion): off,
    /// or drawn at an intensity and radius, on the SSAO feature of the project's own renderer, and
    /// the project's settings back once the scene goes. Read here through the assets' serialized
    /// fields, not the reflection the bridge writes them by, so a write that missed shows. Needs a
    /// URP renderer with SSAO, as this project's is; elsewhere it is inconclusive.
    /// </summary>
    public class AmbientOcclusionEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // Every SSAO feature the active pipeline asset's renderers list.
        static List<ScriptableObject> Features() {
            var found = new List<ScriptableObject>();
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return found;
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (list == null) return found;
            for (int i = 0; i < list.arraySize; i++) {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is not Object data || data == null) continue;
                var features = new SerializedObject(data).FindProperty("m_RendererFeatures");
                for (int j = 0; features != null && j < features.arraySize; j++) {
                    if (features.GetArrayElementAtIndex(j).objectReferenceValue is ScriptableObject f && f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") found.Add(f);
                }
            }
            return found;
        }

        static (bool active, float intensity, float radius) Read(ScriptableObject feature) {
            var so = new SerializedObject(feature);
            return (so.FindProperty("m_Active").boolValue, so.FindProperty("m_Settings.Intensity").floatValue, so.FindProperty("m_Settings.Radius").floatValue);
        }

        [Test]
        public void Occlusion_TurnsOffAndSets_AndTheProjectsComesBack() {
            var features = Features();
            Assume.That(features.Count, Is.GreaterThan(0), "this project's renderer has no SSAO to set");
            var feature = features[0];
            var own = Read(feature);

            Assert.That(ModelBridge.SetAmbientOcclusion(false, -1, -1), Is.Empty);
            Assert.That(Read(feature).active, Is.False, "off");

            Assert.That(ModelBridge.SetAmbientOcclusion(true, 0.5f, 0.25f), Is.Empty);
            var set = Read(feature);
            Assert.That(set.active, Is.True, "on again");
            Assert.That(set.intensity, Is.EqualTo(0.5f), "its intensity");
            Assert.That(set.radius, Is.EqualTo(0.25f), "its radius");

            Assert.That(ModelBridge.SetAmbientOcclusion(true, -1, 0.1f), Is.Empty);
            Assert.That(Read(feature).intensity, Is.EqualTo(own.intensity), "an intensity below 0 is the project's own, not the last one set");
            Assert.That(Read(feature).radius, Is.EqualTo(0.1f));

            ModelBridge.DisposeAll();
            Assert.That(Read(feature), Is.EqualTo(own), "the scene going puts back what the project set");
        }

        [Test]
        public void Occlusion_RefusesWhatIsNoOcclusion() {
            Assert.That(ModelBridge.SetAmbientOcclusion(true, 5, 1), Does.Contain("runs from 0 to 4"));
            Assert.That(ModelBridge.SetAmbientOcclusion(true, 1, 0), Does.Contain("metres above 0"));
            Assert.That(ModelBridge.SetAmbientOcclusion(true, float.NaN, 1), Does.Contain("runs from 0 to 4"));
        }
    }
}
