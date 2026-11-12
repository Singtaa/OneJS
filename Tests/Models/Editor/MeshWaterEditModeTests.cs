using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh drawn as water (ModelBridge.SetMeshWater, ModelSurface.hlsl): it reflects its sky by
    /// water's fresnel, foams where the world behind is near, and its swell makes its bounds taller.
    /// Drawn unlit through an orthographic camera, so every pixel meets the surface at one angle and
    /// the numbers are exact: a black sheet reflecting a red sky.
    /// </summary>
    public class MeshWaterEditModeTests {
        const int Size = 16;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.backgroundColor = Color.black;
            Look(90);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // The camera looking at the origin from `degrees` above the horizontal, 6 m away.
        static void Look(float degrees) {
            var turn = Quaternion.Euler(degrees, 0, 0);
            ModelBridge.SceneCamera.transform.SetPositionAndRotation(turn * Vector3.back * 6, turn);
        }

        // Water's fresnel as ModelWaterReflect works it out, looking `degrees` above the horizontal.
        static float Fresnel(float degrees) => 0.02f + 0.98f * Mathf.Pow(1 - Mathf.Sin(degrees * Mathf.Deg2Rad), 5);

        // A flat square 4 m across at height `y`, unlit, in `color` (sRGB).
        static int Sheet(float y, Color color) {
            int model = ModelBridge.CreateMesh(false, false, false, color.r, color.g, color.b, color.a);
            var positions = new[] { new Vector3(-2, y, -2), new Vector3(-2, y, 2), new Vector3(2, y, 2), new Vector3(2, y, -2) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        static string Red(int model, float foamMetres = 0, float swell = 0) =>
            ModelBridge.SetMeshWater(model, true, true, 1, 0, 0, 1, 1, 1, 1, foamMetres, swell);

        // The middle pixel, linear, with each model drawn as an actor at the origin.
        static Color Middle(params int[] models) {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            var actors = new int[models.Length];
            for (int k = 0; k < models.Length; k++) actors[k] = ModelBridge.Spawn(models[k], 0, 0, 0, 0, 1, false, false);
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
                foreach (var a in actors) ModelBridge.Destroy(a);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void ItReflectsItsSky_ByHowSteeplyItIsSeen() {
            int sheet = Sheet(0, Color.black);
            Look(10);
            Assert.That(Middle(sheet).r, Is.EqualTo(0).Within(0.01f), "not water, a black sheet is black");
            Assert.That(Red(sheet), Is.Empty);
            Assert.That(Middle(sheet).r, Is.EqualTo(Fresnel(10)).Within(0.02f), "10 degrees above the surface");
            Look(90);
            Assert.That(Middle(sheet).r, Is.EqualTo(Fresnel(90)).Within(0.01f), "straight down, 2%");
        }

        [Test]
        public void ASeeThroughSurface_ShowsItsReflectionWhole_AndWhatIsBehindTheRest() {
            // A fifth of a metre down, so the ray at 10 degrees meets it 1.1 m out, inside the sheet.
            int floor = Sheet(-0.2f, new Color(0, 1, 0, 1)), water = Sheet(0, new Color(0, 0, 0, 0.5f));
            Look(10);
            Assert.That(Red(water), Is.Empty);
            float f = Fresnel(10);
            var seen = Middle(floor, water);
            Assert.That(seen.r, Is.EqualTo(f).Within(0.02f), "the sky, in its share");
            Assert.That(seen.g, Is.EqualTo(0.5f * (1 - f)).Within(0.02f), "the floor through the water's alpha, less the reflection");
        }

        [Test]
        public void Foam_ShowsWhereTheWorldBehindIsNear_AndAsksForTheDepth() {
            int shallow = Sheet(-0.1f, Color.black), water = Sheet(0, new Color(0, 0, 0, 0.5f));
            Assert.That(Red(water, foamMetres: 1), Is.Empty);
            Assert.That(ModelBridge.SceneCamera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.Depth));
            // A tenth of a metre over a metre's foam: whole, white, over the 2% of red it reflects.
            Assert.That(Middle(shallow, water).g, Is.EqualTo(1).Within(0.02f), "a tenth of a metre deep");
            ModelBridge.Unload(shallow);
            int deep = Sheet(-2, Color.black);
            Assert.That(Middle(deep, water).g, Is.EqualTo(0).Within(0.01f), "two metres deep, past the foam");
            Assert.That(ModelBridge.SetMeshWater(water, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0), Is.Empty);
            Assert.That(ModelBridge.SceneCamera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.None), "plain again, it asks for no depth");
        }

        [Test]
        public void Swell_MakesItsBoundsTaller_AndPlainPutsThemBack() {
            int water = Sheet(0, new Color(0, 0, 0, 0.5f));
            Assert.That(ModelBridge.MeshOf(water).bounds.size.y, Is.EqualTo(0).Within(1e-4f));
            Assert.That(Red(water, swell: 0.5f), Is.Empty);
            Assert.That(ModelBridge.MeshOf(water).bounds.size.y, Is.EqualTo(1.4f).Within(1e-4f), "1.4 times the swell each way");
            var positions = new[] { new Vector3(-3, 0, -3), new Vector3(-3, 0, 3), new Vector3(3, 0, 3), new Vector3(3, 0, -3) };
            Assert.That(ModelBridge.SetMesh(water, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            Assert.That(ModelBridge.MeshOf(water).bounds.size.y, Is.EqualTo(1.4f).Within(1e-4f), "new vertices keep it");
            Assert.That(ModelBridge.SetMeshWater(water, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0), Is.Empty);
            Assert.That(ModelBridge.MeshOf(water).bounds.size.y, Is.EqualTo(0).Within(1e-4f));
            Assert.That(ModelBridge.MaterialOf(water).GetVector("_Water"), Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void IsRefused_WhereItCannotBe() {
            int opaque = Sheet(0, Color.black);
            Assert.That(Red(opaque, foamMetres: 0.3f), Does.Contain("draws opaque"));
            Assert.That(Red(opaque, swell: 3), Does.Contain("from 0 to 2"));
            Assert.That(Red(opaque, foamMetres: -1), Does.Contain("metres from 0"));
            int added = Sheet(0, new Color(1, 1, 1, 0.5f));
            Assert.That(ModelBridge.SetMeshAdditive(added), Is.Empty);
            Assert.That(Red(added), Does.Contain("added mesh"));
            Assert.That(Red(12345), Does.Contain("no mesh model"));
            Assert.That(ModelBridge.MaterialOf(opaque).GetVector("_Water"), Is.EqualTo(Vector4.zero), "a refusal keeps nothing");
        }
    }
}
