using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's surface (ModelBridge.Material.cs): its roughness and metallic, given at
    /// creation or after, and its colour after it is made. Before them a mesh model was a rough
    /// non-metal for good, whose sun highlight was spread too wide to see, and its colour was fixed
    /// when it was made. Drawn under the sun, since the highlight is the point.
    /// </summary>
    public class MeshMaterialEditModeTests {
        const int Size = 32;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            // Straight down at a flat square, with the sun nearly behind the camera: every pixel
            // sees the sun's mirror image, so a glossy surface is bright all over.
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
            ModelBridge.SetSun(true, 0.05f, -1, 0.05f, 1, 1, 1, 1, false);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        static int Square(float r = 0.5f, float g = 0.5f, float b = 0.5f, float a = 1, bool colors = false) {
            int model = ModelBridge.CreateMesh(false, colors, true, r, g, b, a);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs), Is.Empty);
            return model;
        }

        // The middle pixel's brightness, linear, of the model drawn as an actor at the origin.
        static float Brightness(int model) {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            try {
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                var c = read.GetPixel(Size / 2, Size / 2);
                return c.r + c.g + c.b;
            } finally {
                ModelBridge.Destroy(actor);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void AMeshIsARoughNonMetal_UntilItIsGivenASurface() {
            var material = ModelBridge.MaterialOf(Square());
            Assert.That(material.GetFloat("_Roughness"), Is.EqualTo(1f));
            Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0f));
        }

        [Test]
        public void LowRoughness_ShowsTheSunsHighlight() {
            int rough = Square(), glossy = Square();
            Assert.That(ModelBridge.SetMeshSurface(glossy, 0.15f, 0), Is.Empty);
            var material = ModelBridge.MaterialOf(glossy);
            Assert.That(material.GetFloat("_Roughness"), Is.EqualTo(0.15f));
            Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0f));
            float matte = Brightness(rough), shiny = Brightness(glossy);
            Assert.That(matte, Is.GreaterThan(0.05f), "the rough square is not lit, so nothing here is measured");
            Assert.That(shiny, Is.GreaterThan(1.5f * matte), $"glossy {shiny} against rough {matte}");
        }

        [Test]
        public void Metallic_TakesAwayTheDiffuseLight() {
            int plain = Square(), metal = Square();
            Assert.That(ModelBridge.SetMeshSurface(metal, 1, 1), Is.Empty);
            Assert.That(ModelBridge.MaterialOf(metal).GetFloat("_Metallic"), Is.EqualTo(1f));
            float diffuse = Brightness(plain), metallic = Brightness(metal);
            Assert.That(metallic, Is.LessThan(0.7f * diffuse), $"metal {metallic} against plain {diffuse}");
        }

        [Test]
        public void TheSurface_IsRefusedOutsideNoughtToOne_AndForAModelThatIsNotAMesh() {
            int model = Square();
            Assert.That(ModelBridge.SetMeshSurface(model, -0.1f, 0), Is.EqualTo("roughness is -0.1, and runs from 0 to 1"));
            Assert.That(ModelBridge.SetMeshSurface(model, 0.5f, float.NaN), Is.EqualTo("metallic is NaN, and runs from 0 to 1"));
            Assert.That(ModelBridge.MaterialOf(model).GetFloat("_Roughness"), Is.EqualTo(1f), "a refused surface changes nothing");
            Assert.That(ModelBridge.SetMeshSurface(9999, 0.5f, 0), Is.EqualTo("there is no mesh model 9999"));
            Assert.That(ModelBridge.SetMeshColor(9999, 1, 1, 1, 1), Is.EqualTo("there is no mesh model 9999"));
        }

        [Test]
        public void TheSurface_HoldsThroughASplat_WhichReplacesTheMaterial() {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSurface(model, 0.3f, 0.2f), Is.Empty);
            var layer = new Texture2D(4, 4) { hideFlags = HideFlags.DontSave };
            try {
                Assert.That(ModelBridge.SetMeshSplat(model, layer, null, null, null, 1, 1, 0), Is.Empty);
                var material = ModelBridge.MaterialOf(model);
                Assert.That(material.shader, Is.EqualTo(ModelMaterialGenerator.GroundShader));
                Assert.That(material.GetFloat("_Roughness"), Is.EqualTo(0.3f).Within(1e-6f));
                Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0.2f).Within(1e-6f));
            } finally {
                Object.DestroyImmediate(layer);
            }
        }

        [Test]
        public void TheColour_ChangesAfterTheModelIsMade_AndMovesItBetweenOpaqueAndSeeThrough() {
            int model = Square(0.2f, 0.2f, 0.2f, 1);
            var material = ModelBridge.MaterialOf(model);
            float dark = Brightness(model);
            Assert.That(ModelBridge.SetMeshColor(model, 0.9f, 0.9f, 0.9f, 1), Is.Empty);
            Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.9f, 0.9f, 0.9f, 1)), "GetColor gives back the sRGB value SetColor took");
            Assert.That(Brightness(model), Is.GreaterThan(2 * dark), "the same actor draws the new colour");
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));

            Assert.That(ModelBridge.SetMeshColor(model, 0.9f, 0.9f, 0.9f, 0.5f), Is.Empty);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent), "an alpha below 1 draws it see-through");
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(ModelBridge.SetMeshColor(model, 0.9f, 0.9f, 0.9f, 1), Is.Empty);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry), "and back to opaque at 1");
        }

        [Test]
        public void TheColour_KeepsItSeeThrough_WhileAVertexColourIs() {
            int model = ModelBridge.CreateMesh(false, true, false, 1, 1, 1, 1);
            var positions = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 0) };
            float[] colors = { 1, 1, 1, 0.5f, 1, 1, 1, 1, 1, 1, 1, 1 };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2 }, null, colors, null), Is.Empty);
            var material = ModelBridge.MaterialOf(model);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(ModelBridge.SetMeshColor(model, 0.5f, 0.5f, 0.5f, 1), Is.Empty);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent), "its vertex alpha still asks for see-through");
        }

        [Test]
        public void TheColour_LeavesACutoutOrDepthOnlyMeshDrawingAsItDoes() {
            int card = Square(), mask = Square();
            Assert.That(ModelBridge.SetMeshCutout(card, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetDepthOnly(mask), Is.True);
            Assert.That(ModelBridge.SetMeshColor(card, 1, 1, 1, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetMeshColor(mask, 1, 1, 1, 0.5f), Is.Empty);
            Assert.That(ModelBridge.MaterialOf(card).renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
            Assert.That(ModelBridge.MaterialOf(mask).renderQueue, Is.EqualTo((int)RenderQueue.Geometry - 1));
        }
    }
}
