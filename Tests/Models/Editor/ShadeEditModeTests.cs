using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's grid of shade (ModelBridge.Shade.cs, ModelLighting.hlsl), drawn: it dims the
    /// sun as a realtime shadow does, a spot in both darkens once (the darker of the two, never
    /// their product), and two chunks whose edge samples agree meet without a seam.
    /// </summary>
    public class ShadeEditModeTests {
        const int Size = 64;
        // Metres across the picture, looking straight down at the ground round the origin.
        const float View = 4.4f;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = View / 2;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // Ground lying flat from (x0, z0) to (x1, z1), its uvs 0 to 1 across it.
        static int Ground(float x0, float z0, float x1, float z1) {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            var positions = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) };
            var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs), Is.Empty);
            ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, true);
            return model;
        }

        static byte[] Bytes(params float[] shade) => shade.Select(s => (byte)Mathf.RoundToInt(s * 255)).ToArray();

        // The bridge's own sun, which the edit-mode preview marks not to save, and FindObjectsByType passes over.
        static Light Sun() => Resources.FindObjectsOfTypeAll<Light>().Single(l => l.type == LightType.Directional && l.gameObject.scene.IsValid());

        // The picture's brightness, linear, from the camera's straight-down view.
        static float[] Render() {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            try {
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixels().Select(c => c.r + c.g + c.b).ToArray();
            } finally {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        // The brightness at a point on the ground: the picture's x is the world's x, its y the world's z.
        static float At(float[] picture, float x, float z) {
            int px = Mathf.Clamp(Mathf.FloorToInt((x / View + 0.5f) * Size), 0, Size - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt((z / View + 0.5f) * Size), 0, Size - 1);
            return picture[py * Size + px];
        }

        [Test]
        public void ASpotInShadeAndInARealtimeShadow_DarkensOnce_AsTheDarkerOfTheTwo() {
            // A sun almost overhead, its realtime shadows at half strength, so a realtime shadow and
            // a shade of 0.5 each leave half the sun: the darker of them is half the sun again, and
            // their product would be a quarter.
            ModelBridge.SetSun(true, 0.02f, -1, 0.02f, 1, 1, 1, 1, true);
            Sun().shadowStrength = 0.5f;
            // A slab over the left half that casts and is not drawn.
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try {
                slab.transform.position = new Vector3(-1.2f, 1, 0);
                slab.transform.localScale = new Vector3(2.4f, 0.1f, 5);
                slab.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                int ground = Ground(-2, -2, 2, 2);
                // Shade over the far half: rows from v = 0.625 (z = 0.5) at 0.5, none up to v = 0.5 (z = 0).
                var shade = new float[9 * 9];
                for (int row = 5; row < 9; row++) for (int col = 0; col < 9; col++) shade[row * 9 + col] = 0.5f;
                Assert.That(ModelBridge.SetMeshShade(ground, 9, 9, Bytes(shade)), Is.Empty);

                var picture = Render();
                float lit = At(picture, 1.1f, -1.25f);
                float realtime = At(picture, -1.1f, -1.25f);
                float shaded = At(picture, 1.1f, 1.25f);
                float both = At(picture, -1.1f, 1.25f);
                string drew = $"lit {lit}, realtime shadow {realtime}, shade {shaded}, both {both}";
                float sun = lit - realtime;
                Assert.That(sun, Is.GreaterThan(0.1f * lit), $"the slab's realtime shadow did not fall, so nothing here is measured: {drew}");
                Assert.That(Mathf.Abs(shaded - realtime), Is.LessThan(0.1f * sun), $"a shade of 0.5 dims the sun as a half-strength realtime shadow does: {drew}");
                Assert.That(Mathf.Abs(both - realtime), Is.LessThan(0.1f * sun), $"both together darken once, not twice: {drew}");
            } finally {
                Object.DestroyImmediate(slab);
            }
        }

        [Test]
        public void TwoChunksWhoseEdgeSamplesAgree_MeetWithoutASeam_AndShadeRunsStraightAcrossThem() {
            // No realtime shadows: the shade alone. Two chunks side by side, each 3 by 2 samples, a ramp
            // from no shade at x = -2 to full shade at x = 2 whose middle sample both chunks hold.
            ModelBridge.SetSun(true, 0.02f, -1, 0.02f, 1, 1, 1, 1, false);
            int left = Ground(-2, -1, 0, 1), right = Ground(0, -1, 2, 1);
            Assert.That(ModelBridge.SetMeshShade(left, 3, 2, Bytes(0, 0.25f, 0.5f, 0, 0.25f, 0.5f)), Is.Empty);
            Assert.That(ModelBridge.SetMeshShade(right, 3, 2, Bytes(0.5f, 0.75f, 1, 0.5f, 0.75f, 1)), Is.Empty);

            var picture = Render();
            float Shade(float x) => (x + 2) / 4;
            float near = -1.75f, far = 1.75f;
            float bright = At(picture, near, 0), dark = At(picture, far, 0);
            Assert.That(bright - dark, Is.GreaterThan(0.1f * bright), $"the shade dimmed nothing: {bright} at x = {near}, {dark} at x = {far}");
            // Brightness falls straight with the shade, so each point lies on the line through the two ends.
            foreach (var x in new[] { -1.5f, -1f, -0.5f, -0.25f, -0.06f, 0.06f, 0.25f, 0.5f, 1f, 1.5f }) {
                float want = bright + (dark - bright) * (Shade(x) - Shade(near)) / (Shade(far) - Shade(near));
                Assert.That(Mathf.Abs(At(picture, x, 0) - want), Is.LessThan(0.03f * (bright - dark)),
                    $"at x = {x} the shade should be {Shade(x)}: drew {At(picture, x, 0)} where the straight line runs through {want}");
            }
        }

        [Test]
        public void Shade_IsTheModelsOwn_SurvivesASplat_AndGoesWithTheModel() {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshShade(model, 2, 2, new byte[] { 0, 64, 128, 255 }), Is.Empty);
            var shade = ModelBridge.ShadeOf(model);
            Assert.That(shade.format, Is.EqualTo(TextureFormat.R8));
            Assert.That(shade.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), "a chunk's edge reads its own edge, not the far side");
            Assert.That(shade.mipmapCount, Is.GreaterThan(1), "a far chunk reads a mip, or its shade shimmers");
            Assert.That(ModelBridge.MaterialOf(model).GetTexture("_ShadeMap"), Is.SameAs(shade));

            var layer = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
            try {
                Assert.That(ModelBridge.SetMeshSplat(model, layer, null, null, null, 1, 1, 0), Is.Empty);
                Assert.That(ModelBridge.MaterialOf(model).shader.name, Is.EqualTo("OneJS/GroundLit"));
                Assert.That(ModelBridge.MaterialOf(model).GetTexture("_ShadeMap"), Is.SameAs(shade), "GroundLit's new material keeps it");
            } finally {
                Object.DestroyImmediate(layer);
            }
            Assert.That(ModelBridge.SetMeshShade(model, 2, 2, new byte[] { 1, 2, 3, 4 }), Is.Empty);
            Assert.That(ModelBridge.ShadeOf(model), Is.SameAs(shade), "a grid of the same size is written in place");
            Assert.That(ModelBridge.SetMeshShade(model, 0, 0, null), Is.Empty);
            Assert.That(shade == null, Is.True, "taken off, and destroyed");
            Assert.That(ModelBridge.MaterialOf(model).GetTexture("_ShadeMap"), Is.Null);

            Assert.That(ModelBridge.SetMeshShade(model, 2, 2, new byte[] { 0, 0, 0, 0 }), Is.Empty);
            shade = ModelBridge.ShadeOf(model);
            Assert.That(ModelBridge.Unload(model), Is.True);
            Assert.That(shade == null, Is.True, "it goes with its model");
        }

        [Test]
        public void Shade_RefusesWhatItCannotDraw_SayingWhy() {
            int bare = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshShade(bare, 2, 2, new byte[4]), Does.Contain("without uvs"));
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshShade(model, 1, 2, new byte[2]), Does.Contain("2 to 1024 samples a side"));
            Assert.That(ModelBridge.SetMeshShade(model, 3, 3, new byte[8]), Does.Contain("has 9 values, and this has 8"));
            Assert.That(ModelBridge.SetMeshShade(12345, 2, 2, new byte[4]), Does.Contain("no mesh model"));
            Assert.That(ModelBridge.ShadeOf(model), Is.Null, "nothing was made for a refusal");
        }
    }
}
