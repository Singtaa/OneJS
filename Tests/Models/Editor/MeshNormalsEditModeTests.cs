using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's normals as the cart gives them, kept on every path it draws by: an actor, a
    /// scattered copy (the scatter variant, at a scale), and copies drawn within a near and a far
    /// range. A foliage crown's normals are bent out from its centre on purpose, and a path that
    /// worked them out again from the triangles, or turned them by the wrong matrix, would light
    /// its near and far copies differently at the swap. A square lying flat whose normals all lean
    /// toward a low sun: as given it is bright, worked out from its triangles it is dim.
    /// </summary>
    public class MeshNormalsEditModeTests {
        const int Size = 32;

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            var camera = ModelBridge.SceneCamera;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.SetPositionAndRotation(new Vector3(0, 6, 0), Quaternion.Euler(90, 0, 0));
            camera.backgroundColor = Color.black;
            ModelBridge.SetAmbient(0, 0, 0, 0, 0, 0, 0);
            // Low from +x: about 11 degrees over the horizon.
            ModelBridge.SetSun(true, -1, -0.2f, 0, 1, 1, 1, 1, false);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A flat grey square two units either side of the origin, with its normals leaning 45
        // degrees toward +x when `bent`, or worked out from its triangles (straight up) when not.
        static int Square(bool bent) {
            int model = ModelBridge.CreateMesh(bent, false, false, 0.5f, 0.5f, 0.5f, 1);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            var lean = new Vector3(1, 1, 0).normalized;
            var normals = bent ? new[] { lean, lean, lean, lean } : null;
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, normals, null, null), Is.Empty);
            return model;
        }

        // The middle pixel's brightness, linear, of whatever the scene draws.
        static float Brightness() {
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
                var c = read.GetPixel(Size / 2, Size / 2);
                return c.r + c.g + c.b;
            } finally {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }

        static float AsActor(int model) {
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            try {
                return Brightness();
            } finally {
                ModelBridge.Destroy(actor);
            }
        }

        // One copy at the origin, scaled up alike on every axis (the scatter variant's case), within `from` and `to`.
        static float AsCopy(int model, float from, float to) {
            int scatter = ModelBridge.ScatterWith(model, new[] { 0f, 0, 0, 0, 1.5f }, 5, from, to, 0);
            Assert.That(scatter, Is.GreaterThan(0));
            try {
                return Brightness();
            } finally {
                ModelBridge.DestroyScatter(scatter);
            }
        }

        [Test]
        public void GivenNormals_LightTheSquareAsGiven_NotAsItsTriangles() {
            float given = AsActor(Square(true)), worked = AsActor(Square(false));
            Assert.That(worked, Is.GreaterThan(0.01f), "the square is not lit, so nothing here is measured");
            // N.L is 0.83 as given and 0.2 straight up, over the same indirect light.
            Assert.That(given, Is.GreaterThan(1.8f * worked), $"leaning toward the sun {given}, straight up {worked}");
        }

        [Test]
        public void AScatteredCopy_NearOrFar_IsLitByTheSameNormalsAsAnActor() {
            int model = Square(true);
            float actor = AsActor(model);
            // The camera is 6 m from the copy's foot: inside a near range to 50 m and a far range from 2 m.
            float near = AsCopy(model, 0, 50), far = AsCopy(model, 2, float.PositiveInfinity), whole = AsCopy(model, 0, float.PositiveInfinity);
            foreach (var (what, copy) in new[] { ("near", near), ("far", far), ("unranged", whole) }) {
                Assert.That(Mathf.Abs(copy - actor), Is.LessThan(0.02f * actor), $"a {what} copy {copy} against the actor {actor}");
            }
        }
    }
}
