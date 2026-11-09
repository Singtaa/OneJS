using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// An actor that keeps no shadow on itself, drawn: it is lit as if the caster were not there,
    /// while an actor beside it and a scattered copy keep theirs. What says so is the renderer's
    /// user value, read in the SRP Batcher's per-draw data and in an instanced draw's arrays, so
    /// a drawing is the only proof that each path reads it.
    /// </summary>
    public class ShadowReceiveEditModeTests {
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

        // A flat square `size` metres across, lying round its origin.
        static int Square(float size) {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            float h = size / 2;
            var positions = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            var uvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs), Is.Empty);
            return model;
        }

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
        public void AnActorThatKeepsNoShadowOnItself_DrawsInTheSun_WhileOneThatDoes_AndAScatteredCopy_AreShaded() {
            // A slab across the middle casts on three squares side by side: an actor that keeps no
            // shadow on itself, one that keeps it, and a scattered copy.
            ModelBridge.SetSun(true, 0.02f, -1, 0.02f, 1, 1, 1, 1, true);
            Sun().shadowStrength = 1;
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try {
                slab.transform.position = new Vector3(0, 1, 0);
                slab.transform.localScale = new Vector3(5, 0.1f, 1f);
                slab.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                int square = Square(1.4f);
                ModelBridge.Spawn(square, -1.45f, 0, 0, 0, 1, false, false);
                ModelBridge.Spawn(square, 0, 0, 0, 0, 1, false, true);
                ModelBridge.Scatter(square, new float[] { 1.45f, 0, 0, 0, 1 });

                var picture = Render();
                string Drew(float x) => $"{At(picture, x, 0)} under the slab, {At(picture, x, -0.6f)} beside it";
                Assert.That(At(picture, 0, 0), Is.LessThan(0.6f * At(picture, 0, -0.6f)), $"the slab's shadow did not fall, so nothing here is measured: {Drew(0)}");
                Assert.That(At(picture, -1.45f, 0), Is.EqualTo(At(picture, -1.45f, -0.6f)).Within(0.02f * At(picture, -1.45f, -0.6f)), $"keeping no shadow on itself, it is lit as if the slab were not there: {Drew(-1.45f)}");
                Assert.That(At(picture, 1.45f, 0), Is.LessThan(0.6f * At(picture, 1.45f, -0.6f)), $"a scattered copy keeps its shadow: {Drew(1.45f)}");
            } finally {
                Object.DestroyImmediate(slab);
            }
        }
    }
}
