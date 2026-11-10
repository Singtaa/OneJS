using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's normal map (ModelBridge.SetMeshNormalMap, ModelSurface.hlsl): read from a
    /// linear copy of an sRGB picture, in a frame worked out per pixel since a mesh has no tangents,
    /// and sliding by the scene's clock. Drawn under a low sun from +x, looking straight down at a
    /// square whose u runs along +x: a normal tilted toward the sun is brighter than a flat one, and
    /// one tilted away darker. Before them a mesh read the flat `bump` picture in a frame resting on
    /// tangents it did not have.
    /// </summary>
    public class MeshNormalMapEditModeTests {
        const int Size = 32;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
            // From +x and above, toward -x.
            ModelBridge.SetSun(true, -0.6f, -0.8f, 0, 1, 1, 1, 1, false);
            ModelBridge.SetWindClock(0);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A flat grey square 4 m across, u along +x and v along +z.
        static int Square() {
            int model = ModelBridge.CreateMesh(false, false, true, 0.6f, 0.6f, 0.6f, 1);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs), Is.Empty);
            return model;
        }

        // An sRGB picture, as tex and loadTexture make them, whose texels encode the normal `left` on
        // its left half and `right` on its right, x along u.
        static Texture2D Normals(Vector3 left, Vector3 right) {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            Color32 Encode(Vector3 n) => new((byte)Mathf.RoundToInt((n.x + 1) * 127.5f), (byte)Mathf.RoundToInt((n.y + 1) * 127.5f), (byte)Mathf.RoundToInt((n.z + 1) * 127.5f), 255);
            t.SetPixels32(Enumerable.Range(0, 64).Select(i => Encode(i % 8 < 4 ? left : right)).ToArray());
            t.Apply();
            return t;
        }

        static readonly Vector3 Flat = new(0, 0, 1), TowardSun = new Vector3(0.8f, 0, 0.6f), AwayFromSun = new Vector3(-0.8f, 0, 0.6f);

        // The brightness, linear, at world x (z 0), with the model drawn as an actor at the origin.
        static float Brightness(int model, float x = 0) {
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
                var c = read.GetPixel(Mathf.Clamp(Mathf.FloorToInt((x / 4 + 0.5f) * Size), 0, Size - 1), Size / 2);
                return c.r + c.g + c.b;
            } finally {
                ModelBridge.Destroy(actor);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void AFlatNormalMap_DrawsAsNoneDoes_SinceItIsReadAsData() {
            var flat = Normals(Flat, Flat);
            try {
                int plain = Square(), mapped = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(mapped, flat, 1, 1, 1, 0, 0), Is.Empty);
                float without = Brightness(plain), with = Brightness(mapped);
                Assert.That(without, Is.GreaterThan(0.1f), "the square is not lit, so nothing here is measured");
                // Read through an sRGB view, 128 would decode to 0.22 and tilt the normal toward -x and -y.
                Assert.That(with, Is.EqualTo(without).Within(0.03f * without), $"flat map {with} against none {without}");
            } finally {
                Object.DestroyImmediate(flat);
            }
        }

        [Test]
        public void ATiltedNormal_TurnsTheSurfaceTowardOrAwayFromTheSun() {
            var toward = Normals(TowardSun, TowardSun);
            var away = Normals(AwayFromSun, AwayFromSun);
            try {
                int plain = Square(), lit = Square(), shaded = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(lit, toward, 1, 1, 1, 0, 0), Is.Empty);
                Assert.That(ModelBridge.SetMeshNormalMap(shaded, away, 1, 1, 1, 0, 0), Is.Empty);
                float flat = Brightness(plain), bright = Brightness(lit), dark = Brightness(shaded);
                Assert.That(bright, Is.GreaterThan(1.15f * flat), $"tilted toward the sun {bright}, flat {flat}");
                Assert.That(dark, Is.LessThan(0.85f * flat), $"tilted away from it {dark}, flat {flat}");
            } finally {
                Object.DestroyImmediate(toward);
                Object.DestroyImmediate(away);
            }
        }

        [Test]
        public void Scale_BendsItFurther_AndZeroLeavesItFlat() {
            var toward = Normals(TowardSun, TowardSun);
            try {
                int plain = Square(), none = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(none, toward, 1, 1, 0, 0, 0), Is.Empty);
                float flat = Brightness(plain), unbent = Brightness(none);
                Assert.That(unbent, Is.EqualTo(flat).Within(0.03f * flat), $"scale 0 {unbent}, flat {flat}");
            } finally {
                Object.DestroyImmediate(toward);
            }
        }

        [Test]
        public void Flow_SlidesItByTheScenesClock() {
            // Toward the sun on the left half of the picture, away on the right: at x = -1 (u 0.25) the
            // left half shows, and half a picture later the right half does.
            var halves = Normals(TowardSun, AwayFromSun);
            try {
                int model = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(model, halves, 1, 1, 1, 0.25f, 0), Is.Empty);
                var flow = ModelBridge.MaterialOf(model).GetVector("_NormalFlow");
                Assert.That(new Vector3(flow.x, flow.y, flow.z), Is.EqualTo(new Vector3(0.25f, 0, 1)));
                float still = Brightness(model, -1);
                ModelBridge.SetWindClock(2);
                float slid = Brightness(model, -1);
                Assert.That(slid, Is.LessThan(0.8f * still), $"after 2 s at a quarter picture a second {slid}, at 0 s {still}");
            } finally {
                Object.DestroyImmediate(halves);
            }
        }

        [Test]
        public void TheCopy_IsSharedByEveryMeshGivenThePicture_AndFreedWithTheLast() {
            var flat = Normals(Flat, Flat);
            try {
                int before = ModelBridge.NormalCopyCount;
                int a = Square(), b = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(a, flat, 1, 1, 1, 0, 0), Is.Empty);
                Assert.That(ModelBridge.SetMeshNormalMap(b, flat, 2, 2, 1, 0, 0), Is.Empty);
                Assert.That(ModelBridge.NormalCopyCount, Is.EqualTo(before + 1));
                Assert.That(ModelBridge.MaterialOf(a).GetTexture("_NormalMap"), Is.SameAs(ModelBridge.MaterialOf(b).GetTexture("_NormalMap")));
                Assert.That(ModelBridge.Unload(a), Is.True);
                Assert.That(ModelBridge.NormalCopyCount, Is.EqualTo(before + 1));
                Assert.That(ModelBridge.SetMeshNormalMap(b, null, 1, 1, 1, 0, 0), Is.Empty);
                Assert.That(ModelBridge.NormalCopyCount, Is.EqualTo(before), "taken off the last mesh, the copy goes");
                Assert.That(ModelBridge.MaterialOf(b).GetVector("_NormalFlow").z, Is.EqualTo(0f), "and the mesh reads as it did");
            } finally {
                Object.DestroyImmediate(flat);
            }
        }

        [Test]
        public void IsRefused_WithoutUvs_OnASplat_AndOutsideItsRange() {
            var flat = Normals(Flat, Flat);
            try {
                int bare = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMeshNormalMap(bare, flat, 1, 1, 1, 0, 0), Does.StartWith("this model was made without uvs"));
                int ground = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMeshSplat(ground, flat, null, null, null, 1, 1, 0), Is.Empty);
                Assert.That(ModelBridge.SetMeshNormalMap(ground, flat, 1, 1, 1, 0, 0), Is.EqualTo("a splat is lit by its vertex normals, and takes no normal map"));
                int model = Square();
                Assert.That(ModelBridge.SetMeshNormalMap(model, flat, 0, 1, 1, 0, 0), Is.EqualTo("tiling is 0 by 1, and is a number above 0"));
                Assert.That(ModelBridge.SetMeshNormalMap(model, flat, 1, 1, float.NaN, 0, 0), Is.EqualTo("scale is NaN, and is a number"));
                Assert.That(ModelBridge.SetMeshNormalMap(model, flat, 1, 1, 1, float.NaN, 0), Is.EqualTo("flow is NaN by 0, and is uvs a second"));
                Assert.That(ModelBridge.SetMeshNormalMap(9999, flat, 1, 1, 1, 0, 0), Is.EqualTo("there is no mesh model 9999"));
                Assert.That(ModelBridge.MaterialOf(model).GetVector("_NormalFlow").z, Is.EqualTo(0f), "nothing refused changes the mesh");
            } finally {
                Object.DestroyImmediate(flat);
            }
        }
    }
}
