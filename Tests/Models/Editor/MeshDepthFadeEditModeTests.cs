using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A see-through mesh faded by the opaque world behind it (ModelBridge.SetMeshDepthFade,
    /// ModelSurface.hlsl): `soften` fades its alpha at a shore, and `deepColor` moves its colour
    /// and alpha toward another as the water deepens. Drawn, unlit so the numbers are exact: a
    /// white sheet at an alpha of 0.5 over a black floor some metres below, seen straight down.
    /// Before them a sheet drew the same over any depth, and ended on the triangle where it met
    /// the shore.
    /// </summary>
    public class MeshDepthFadeEditModeTests {
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

        // A flat square 4 m across at height `y`, unlit, in `color` (sRGB).
        static int Sheet(float y, Color color) {
            int model = ModelBridge.CreateMesh(false, false, false, color.r, color.g, color.b, color.a);
            var positions = new[] { new Vector3(-2, y, -2), new Vector3(-2, y, 2), new Vector3(2, y, 2), new Vector3(2, y, -2) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        static readonly Color Water = new(1, 1, 1, 0.5f);

        // The middle pixel, linear, with each model drawn as an actor at the origin, and `scattered`
        // as a scattered copy there.
        static Color Middle(params int[] models) => Middle(-1, models);

        static Color Middle(int scattered, params int[] models) {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            var actors = new int[models.Length];
            for (int k = 0; k < models.Length; k++) actors[k] = ModelBridge.Spawn(models[k], 0, 0, 0, 0, 1, false, false);
            int scatter = scattered < 0 ? -1 : ModelBridge.Scatter(scattered, new float[] { 0, 0, 0, 0, 1 });
            try {
                camera.targetTexture = target;
                // Twice, for one editor quirk only, not as a habit: on Direct3D 12 the editor's first
                // render after ModelLit compiles afresh in its session drew a faded sheet over white
                // rather than over the floor and the clear. Win 003 reproduced it in every fresh
                // compile of 18 runs on an RTX 3080 (async compilation off changed nothing), and every
                // render after the first was right. A player's shaders are compiled at build; the
                // Play container's first frame is checked by onejs-play's first-frame fixture.
                camera.Render();
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixel(Size / 2, Size / 2);
            } finally {
                foreach (var a in actors) ModelBridge.Destroy(a);
                if (scatter >= 0) ModelBridge.DestroyScatter(scatter);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void ASheetGivenNeither_DrawsTheSameOverAnyDepth_AndAsksForNoDepth() {
            int floor = Sheet(-0.25f, Color.black), water = Sheet(0, Water);
            Assert.That(Middle(floor, water).r, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(ModelBridge.SceneCamera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.None));
        }

        [Test]
        public void Soften_FadesTheAlphaWhereTheWorldBehindIsNear() {
            int shallow = Sheet(-0.25f, Color.black), water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(water, 1, 0, 0, 0, 0, 0), Is.Empty);
            // A quarter of a metre over a metre's soften: a quarter of the alpha of 0.5.
            Assert.That(Middle(shallow, water).r, Is.EqualTo(0.125f).Within(0.01f), "a quarter metre deep");
            int deep = Sheet(-2, Color.black);
            Assert.That(Middle(deep, water).r, Is.EqualTo(0.5f).Within(0.01f), "past soften's metres the alpha is whole");
        }

        // A scattered copy draws with ModelLit's scatter variant on a copy of the model's material
        // (ModelBridge.Scatter.cs), which must carry the fade.
        [Test]
        public void AScatteredCopy_FadesAsAnActorDoes() {
            int shallow = Sheet(-0.25f, Color.black), water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(water, 1, 0, 0, 0, 0, 0), Is.Empty);
            Assert.That(Middle(water, shallow).r, Is.EqualTo(0.125f).Within(0.01f), "a quarter metre deep, scattered");
        }

        [Test]
        public void DeepColor_TakesTheColourAndAlphaTowardItsOwnWithDepth() {
            int deep = Sheet(-3, Color.black), water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(water, 0, 1, 0, 0, 1, 1), Is.Empty);
            // Three metres over one: 1 - exp(-9) of the way to an opaque red.
            var c = Middle(deep, water);
            Assert.That(c.r, Is.EqualTo(1).Within(0.01f), $"deep water is red: {c}");
            Assert.That(c.g, Is.LessThan(0.01f), $"and opaque, so none of the white sheet's own colour: {c}");

            ModelBridge.DisposeAll();
            NewScene();
            int shallow = Sheet(-0.1f, Color.black);
            water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(water, 0, 1, 0, 0, 1, 1), Is.Empty);
            // A tenth of a metre: k = 1 - exp(-0.3); colour (1, 1 - k, 1 - k) at an alpha of 0.5 + 0.5 k.
            float k = 1 - Mathf.Exp(-0.3f), alpha = 0.5f + 0.5f * k;
            var s = Middle(shallow, water);
            Assert.That(s.r, Is.EqualTo(alpha).Within(0.02f), $"shallow: {s}");
            Assert.That(s.g, Is.EqualTo((1 - k) * alpha).Within(0.02f), $"shallow: {s}");
        }

        [Test]
        public void TheDepthIsAskedFor_WhileASoftenedMeshLives() {
            var camera = ModelBridge.SceneCamera;
            // URP's own switch, through the bridge's hook for it (UrpCameraDepth).
            var pipeline = ModelBridge.CameraDepthPipeline;
            Assume.That(pipeline, Is.Not.Null, "this project draws with URP");
            pipeline.Set(camera, false);
            int water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(water, 1, 0, 0, 0, 0, 0), Is.Empty);
            Assert.That(pipeline.Get(camera), Is.True);
            Assert.That(camera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.Depth));
            Assert.That(ModelBridge.Unload(water), Is.True);
            Assert.That(pipeline.Get(camera), Is.False, "and put back as it was when the last one goes");
            Assert.That(camera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.None));
        }

        [Test]
        public void IsRefused_ByAMeshThatDrawsOpaque_AndOutsideItsRange() {
            int opaque = Sheet(0, Color.white), card = Sheet(0, Water), mask = Sheet(0, Water), water = Sheet(0, Water);
            Assert.That(ModelBridge.SetMeshDepthFade(opaque, 1, 0, 0, 0, 0, 0), Does.StartWith("this mesh draws opaque"));
            Assert.That(ModelBridge.SetMeshCutout(card, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetMeshDepthFade(card, 1, 0, 0, 0, 0, 0), Does.StartWith("a mesh with a cutout draws opaque"));
            Assert.That(ModelBridge.SetDepthOnly(mask), Is.True);
            Assert.That(ModelBridge.SetMeshDepthFade(mask, 1, 0, 0, 0, 0, 0), Is.EqualTo("a depth-only mesh draws no colour to fade"));
            Assert.That(ModelBridge.SetMeshDepthFade(water, -1, 0, 0, 0, 0, 0), Is.EqualTo("soften is -1, and is metres from 0"));
            Assert.That(ModelBridge.SetMeshDepthFade(water, 0, 0, 0, 0, 0, float.NaN), Is.EqualTo("deepColor's metres is NaN, and is metres from 0"));
            Assert.That(ModelBridge.SetMeshDepthFade(9999, 1, 0, 0, 0, 0, 0), Is.EqualTo("there is no mesh model 9999"));
            Assert.That(ModelBridge.SceneCamera.depthTextureMode & DepthTextureMode.Depth, Is.EqualTo(DepthTextureMode.None), "nothing refused asks for the depth");
        }
    }
}
