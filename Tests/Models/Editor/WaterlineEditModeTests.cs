using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The scene's waterline (ModelBridge.SetWaterline): below a height everything drawn turns toward
    /// a colour. Drawn unlit, straight down, so the numbers are exact: a white sheet under and over a
    /// red waterline.
    /// </summary>
    public class WaterlineEditModeTests {
        const int Size = 16;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A white square 4 m across, unlit.
        static int Sheet() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        // The middle pixel, linear, with the model drawn as an actor `y` metres up.
        static Color Middle(int model, float y) {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            int actor = ModelBridge.Spawn(model, 0, y, 0, 0, 1, false, false);
            try {
                camera.targetTexture = target;
                // Twice, as MeshDepthFadeEditModeTests does, for Direct3D 12's first render after a compile.
                camera.Render();
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixel(Size / 2, Size / 2);
            } finally {
                ModelBridge.Destroy(actor);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void BelowIt_TheColourTakesOver_ByItsStrength_AndAboveItNothingChanges() {
            int sheet = Sheet();
            Assert.That(ModelBridge.SetWaterline(true, 1, 1, 0, 0, 1), Is.Empty);
            var under = Middle(sheet, 0);
            Assert.That(new[] { under.r, under.g }, Is.EqualTo(new[] { 1f, 0f }).Within(0.01f), "a metre under, wholly red");
            var over = Middle(sheet, 2);
            Assert.That(new[] { over.r, over.g }, Is.EqualTo(new[] { 1f, 1f }).Within(0.01f), "a metre over, white");
            Assert.That(ModelBridge.SetWaterline(true, 1, 1, 0, 0, 0.5f), Is.Empty);
            Assert.That(Middle(sheet, 0).g, Is.EqualTo(0.5f).Within(0.01f), "at half strength, halfway");
            Assert.That(ModelBridge.SetWaterline(false, 0, 0, 0, 0, 0), Is.Empty);
            Assert.That(Middle(sheet, 0).g, Is.EqualTo(1).Within(0.01f), "with none, white");
        }

        [Test]
        public void TheScenesTeardown_TakesItAway_AndItIsRefusedOutsideItsRange() {
            Assert.That(ModelBridge.SetWaterline(true, 1, 1, 0, 0, 2), Does.Contain("from 0 to 1"));
            Assert.That(ModelBridge.SetWaterline(true, float.NaN, 1, 0, 0, 1), Does.Contain("height in metres"));
            Assert.That(ModelBridge.SetWaterline(true, 1, 1, 0, 0, 1), Is.Empty);
            ModelBridge.DisposeAll();
            Assert.That(Shader.GetGlobalVector("_OjWaterline"), Is.EqualTo(Vector4.zero));
        }
    }
}
