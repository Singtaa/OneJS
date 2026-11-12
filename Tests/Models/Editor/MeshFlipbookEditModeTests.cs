using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's texture played as a flipbook (ModelBridge.SetMeshFlipbook): each frame in turn on
    /// the scene's clock, cross-faded into the next. Drawn unlit, straight down, so the numbers are
    /// exact: a white sheet flipping between a red frame and a blue one over two seconds.
    /// </summary>
    public class MeshFlipbookEditModeTests {
        const int Size = 16;
        Texture2D _red, _blue;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
            _red = Solid(Color.red);
            _blue = Solid(Color.blue);
        }

        [TearDown]
        public void Dispose() {
            ModelBridge.DisposeAll();
            Object.DestroyImmediate(_red);
            Object.DestroyImmediate(_blue);
        }

        static Texture2D Solid(Color c) {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        // A white square 4 m across with uvs, unlit.
        static int Sheet() {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        int Playing(int model) {
            Assert.That(ModelBridge.SetMeshFlipbook(model, 2, 2, 1, 1), Is.Empty);
            Assert.That(ModelBridge.SetMeshFlipbookFrame(model, 0, _red), Is.Empty);
            Assert.That(ModelBridge.SetMeshFlipbookFrame(model, 1, _blue), Is.Empty);
            return model;
        }

        // The middle pixel, linear, with the model drawn as an actor at the origin.
        static Color Middle(int model) {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
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
        public void ItShowsEachFrameInTurn_CrossFadedBetween_ByTheScenesClock() {
            int sheet = Playing(Sheet());
            ModelBridge.SetWindClock(0);
            var first = Middle(sheet);
            Assert.That(new[] { first.r, first.b }, Is.EqualTo(new[] { 1f, 0f }).Within(0.01f), "at 0 s, the first frame");
            ModelBridge.SetWindClock(0.5f);
            var between = Middle(sheet);
            Assert.That(new[] { between.r, between.b }, Is.EqualTo(new[] { 0.5f, 0.5f }).Within(0.02f), "a quarter through, halfway from the first to the second");
            ModelBridge.SetWindClock(1);
            var second = Middle(sheet);
            Assert.That(new[] { second.r, second.b }, Is.EqualTo(new[] { 0f, 1f }).Within(0.01f), "halfway through, the second frame");
            ModelBridge.SetWindClock(1.5f);
            var back = Middle(sheet);
            Assert.That(new[] { back.r, back.b }, Is.EqualTo(new[] { 0.5f, 0.5f }).Within(0.02f), "three quarters through, halfway back to the first: it loops");
        }

        [Test]
        public void ATexture_TakesItsPlace_AndUnloadingLetsItGo() {
            int sheet = Playing(Sheet());
            Assert.That(ModelBridge.FlipbookCount, Is.EqualTo(1));
            Assert.That(ModelBridge.SetMeshTexture(sheet, _blue, 1, 1), Is.Empty);
            Assert.That(ModelBridge.FlipbookCount, Is.EqualTo(0), "a texture stops the flipbook");
            ModelBridge.SetWindClock(0);
            Assert.That(Middle(sheet).b, Is.EqualTo(1).Within(0.01f));
            Playing(sheet);
            Assert.That(ModelBridge.Unload(sheet), Is.True);
            Assert.That(ModelBridge.FlipbookCount, Is.EqualTo(0), "unloaded, it plays no more");
        }

        [Test]
        public void IsRefused_WhereItCannotBe() {
            int sheet = Sheet();
            Assert.That(ModelBridge.SetMeshFlipbook(sheet, 1, 2, 1, 1), Does.Contain("from 2 to"));
            Assert.That(ModelBridge.SetMeshFlipbook(sheet, 2, 0, 1, 1), Does.Contain("more than 0"));
            Assert.That(ModelBridge.SetMeshFlipbookFrame(sheet, 0, _red), Does.Contain("no flipbook"));
            Playing(sheet);
            Assert.That(ModelBridge.SetMeshFlipbookFrame(sheet, 2, _red), Does.Contain("not one of its 2"));
            int bare = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshFlipbook(bare, 2, 2, 1, 1), Does.Contain("without uvs"));
        }
    }
}
