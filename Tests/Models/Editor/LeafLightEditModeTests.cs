using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.SetMeshLeafLight: a mesh's `wrap` lets the sun reach past its shadow line, and
    /// its `backLight` lets the sun show through it from behind (ModelLeafLight.hlsl). Before
    /// them a leaf card turned from the sun took the ambient alone, so a crown's undersides went
    /// dark, and a card seen edge on drew as a dark line (SetMeshEdgeFade). A flat square seen from straight above, with the ambient off, so what changes is the sun's work.
    /// </summary>
    public class LeafLightEditModeTests {
        const int Size = 33;

        // The bytes a mesh that does not ask read back before leaf light existed (OneJS 3a025da,
        // reflections), by the GPU that read them: the same code on another GPU may round differently.
        static readonly Dictionary<string, string> Before = new() {
            ["NVIDIA GeForce RTX 3080"] = "cd14a11db9479fd390fb4e0e57fddd79",
        };

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
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A grey square two units either side of the origin, facing up.
        static int Square() {
            int model = ModelBridge.CreateMesh(false, false, false, 0.5f, 0.5f, 0.5f, 1);
            var positions = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            return model;
        }

        // A unit ball with its own normals, a little glossy, so every way a normal faces is in the picture.
        static int Ball() {
            const int rings = 32, segments = 64;
            var positions = new List<Vector3>();
            var indices = new List<uint>();
            for (int i = 0; i <= rings; i++) {
                float v = Mathf.PI * i / rings;
                for (int j = 0; j <= segments; j++) {
                    float u = 2 * Mathf.PI * j / segments;
                    positions.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)));
                }
            }
            for (int i = 0; i < rings; i++) {
                for (int j = 0; j < segments; j++) {
                    uint a = (uint)(i * (segments + 1) + j), b = a + segments + 1;
                    indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            }
            int model = ModelBridge.CreateMesh(true, false, false, 0.6f, 0.7f, 0.4f, 1);
            var p = positions.ToArray();
            Assert.That(ModelBridge.SetMesh(model, p, indices.ToArray(), p, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshSurface(model, 0.4f, 0.2f), Is.Empty);
            return model;
        }

        // White, the back light at a focus of 4.
        static string Leaf(int model, float wrap, float backLight) => ModelBridge.SetMeshLeafLight(model, wrap, backLight, 4, 1, 1, 1);

        // The sun travelling along `direction`, white, without shadows.
        static void Sun(float x, float y, float z) => ModelBridge.SetSun(true, x, y, z, 1, 1, 1, 1, false);

        static Texture2D Render(int model) {
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
                return read;
            } finally {
                ModelBridge.Destroy(actor);
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
            }
        }

        // The middle pixel, linear.
        static Color Middle(int model) {
            var read = Render(model);
            try {
                return read.GetPixel(Size / 2, Size / 2);
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        // The middle pixel's brightness, linear.
        static float Brightness(int model) {
            var c = Middle(model);
            return c.r + c.g + c.b;
        }

        [Test]
        public void AMeshTakesNoLeafLight_UntilItIsGivenSome() {
            var material = ModelBridge.MaterialOf(Square());
            Assert.That(material.GetVector("_LeafLight"), Is.EqualTo(new Vector4(0, 0, 4, 0)), "no wrap, no back light, its focus at 4");
            Assert.That(material.GetVector("_LeafEdge"), Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void Wrap_LightsAFaceTheSunIsJustBelow() {
            // The sun comes from a little below the square's plane: N.L is about -0.2, so plain
            // diffuse light gives it nothing.
            Sun(0.98f, 0.2f, 0);
            int plain = Square(), wrapped = Square();
            Assert.That(Leaf(wrapped, 0.6f, 0), Is.Empty);
            Assert.That(ModelBridge.MaterialOf(wrapped).GetVector("_LeafLight"), Is.EqualTo(new Vector4(0.6f, 0, 4, 0)));
            float without = Brightness(plain), with = Brightness(wrapped);
            // Without the sun's direct light only the scene's indirect light is left, the same for both.
            Assert.That(with, Is.GreaterThan(without + 0.1f), $"past its shadow line a wrapped square is lit: {with} against {without}");
        }

        [Test]
        public void Wrap_BrightensTheLitSideLessThanTheShadowSide() {
            // On a face the sun stands straight over, wrapping adds nothing: (1 + w) / (1 + w).
            Sun(0, -1, 0);
            int plain = Square(), wrapped = Square();
            Assert.That(Leaf(wrapped, 0.6f, 0), Is.Empty);
            float without = Brightness(plain), with = Brightness(wrapped);
            Assert.That(without, Is.GreaterThan(0.2f), "the square is not lit, so nothing here is measured");
            Assert.That(Mathf.Abs(with - without), Is.LessThan(0.02f * without), $"facing the sun: {with} against {without}");
        }

        [Test]
        public void BackLight_ShowsTheSunThroughAFaceLitFromBehind() {
            // The sun is below the square, shining up through it toward the camera.
            Sun(0.1f, 1, 0.05f);
            int plain = Square(), through = Square();
            Assert.That(Leaf(through, 0, 0.8f), Is.Empty);
            float without = Brightness(plain), with = Brightness(through);
            Assert.That(with, Is.GreaterThan(without + 0.3f), $"lit from behind, a back lit square glows: {with} against {without}");
        }

        [Test]
        public void BackLight_AddsNothingSeenFromTheSunsSide() {
            // The sun behind the camera: nothing shines through toward it.
            Sun(0.05f, -1, 0.05f);
            int plain = Square(), through = Square();
            Assert.That(Leaf(through, 0, 0.8f), Is.Empty);
            float without = Brightness(plain), with = Brightness(through);
            Assert.That(Mathf.Abs(with - without), Is.LessThan(0.01f), $"from the sun's side: {with} against {without}");
        }

        [Test]
        public void BackLight_TakesItsColor() {
            Sun(0.1f, 1, 0.05f);
            int plain = Square(), red = Square();
            Assert.That(ModelBridge.SetMeshLeafLight(red, 0, 0.8f, 4, 1, 0, 0), Is.Empty);
            Color a = Middle(plain), b = Middle(red);
            Assert.That(b.r, Is.GreaterThan(a.r + 0.1f), $"red shows through: {b} against {a}");
            Assert.That(Mathf.Abs(b.g - a.g) + Mathf.Abs(b.b - a.b), Is.LessThan(0.01f), $"and nothing else: {b} against {a}");
        }

        [Test]
        public void AFocus_GathersTheGlowRoundTheSunsLine() {
            // The sun below the square and 40 degrees off the camera's line: a broad glow reaches
            // this far round, a tight one hardly.
            Sun(0.64f, 0.77f, 0);
            int broad = Square(), tight = Square();
            Assert.That(ModelBridge.SetMeshLeafLight(broad, 0, 0.8f, 1, 1, 1, 1), Is.Empty);
            Assert.That(ModelBridge.SetMeshLeafLight(tight, 0, 0.8f, 16, 1, 1, 1), Is.Empty);
            float a = Brightness(broad), b = Brightness(tight);
            Assert.That(a, Is.GreaterThan(b + 0.1f), $"focus 1 {a} against focus 16 {b}");
        }

        // A white square, drawn unlit, tipped `degrees` up from lying flat about the x axis.
        static int Card(float degrees) {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            float a = degrees * Mathf.Deg2Rad;
            var along = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
            var positions = new[] { -Vector3.right - along, -Vector3.right + along, Vector3.right + along, Vector3.right - along };
            Assert.That(ModelBridge.SetMesh(model, positions, new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshTwoSided(model), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        // How many pixels of the frame the model covers, against a magenta clear.
        static int Covered(int model) {
            ModelBridge.SceneCamera.backgroundColor = Color.magenta;
            var read = Render(model);
            try {
                int n = 0;
                foreach (var c in read.GetPixels()) if (!(c.r > 0.98f && c.g < 0.02f && c.b > 0.98f)) n++;
                return n;
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void EdgeFade_DithersAwayACardSeenEdgeOn_AndKeepsOneSeenFaceOn() {
            // Tipped 80 degrees, the card meets the eye at |N.V| = 0.17.
            int edgeOn = Card(80), faded = Card(80);
            Assert.That(ModelBridge.SetMeshEdgeFade(faded, 0.3f, 0.5f), Is.Empty);
            int whole = Covered(edgeOn), left = Covered(faded);
            Assert.That(whole, Is.GreaterThan(50), "the card covers nothing, so nothing here is measured");
            Assert.That(left, Is.EqualTo(0), $"seen edge on it is gone: {left} of {whole} pixels left");
            int faceOn = Card(0), kept = Card(0);
            Assert.That(ModelBridge.SetMeshEdgeFade(kept, 0.3f, 0.5f), Is.Empty);
            Assert.That(Covered(kept), Is.EqualTo(Covered(faceOn)), "seen face on it keeps every pixel");
        }

        [Test]
        public void EdgeFade_DithersPartWayBetweenItsEnds() {
            // |N.V| = cos 70 = 0.34, halfway between 0.24 and 0.44.
            int card = Card(70), faded = Card(70);
            Assert.That(ModelBridge.SetMeshEdgeFade(faded, 0.24f, 0.44f), Is.Empty);
            int whole = Covered(card), left = Covered(faded);
            Assert.That(left, Is.InRange(whole / 4, 3 * whole / 4), $"{left} of {whole} pixels left");
        }

        [Test]
        public void EdgeFade_IsRefusedOutOfOrder_AndComesOffAtNoughtAndNought() {
            int card = Card(80);
            Assert.That(ModelBridge.SetMeshEdgeFade(card, 0.5f, 0.3f), Does.Contain("start < end"));
            Assert.That(ModelBridge.SetMeshEdgeFade(card, 0.2f, 1.5f), Does.Contain("start < end"));
            Assert.That(ModelBridge.SetMeshEdgeFade(card, 0.3f, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetMeshEdgeFade(card, 0, 0), Is.Empty);
            Assert.That(ModelBridge.MaterialOf(card).GetVector("_LeafEdge"), Is.EqualTo(Vector4.zero));
            Assert.That(Covered(card), Is.GreaterThan(50), "off again, the card draws whole");
        }

        [Test]
        public void TheLeafLight_IsRefusedOutsideNoughtToOne_AndForAModelThatIsNotAMesh() {
            int model = Square();
            Assert.That(Leaf(model, -0.1f, 0), Does.Contain("wrap"));
            Assert.That(Leaf(model, 0, 1.5f), Does.Contain("backLight"));
            Assert.That(Leaf(model, float.NaN, 0), Does.Contain("wrap"));
            Assert.That(Leaf(-1, 0.5f, 0.5f), Does.Contain("no mesh model"));
            Assert.That(ModelBridge.SetMeshLeafLight(model, 0, 0.5f, 0.5f, 1, 1, 1), Does.Contain("backLightFocus"));
            Assert.That(ModelBridge.SetMeshLeafLight(model, 0, 0.5f, 4, 1.2f, 1, 1), Does.Contain("backLightColor"));
            Assert.That(ModelBridge.MaterialOf(model).GetVector("_LeafLight"), Is.EqualTo(new Vector4(0, 0, 4, 0)), "a refused value changes nothing");
        }

        [Test]
        public void AMeshThatDoesNotAsk_ReadsTheBytesItReadBefore() {
            Sun(-0.4f, -0.6f, 0.7f);
            ModelBridge.SetAmbient(0.3f, 0.4f, 0.6f, 0.2f, 0.15f, 0.1f, 1);
            var read = Render(Ball());
            try {
                var hash = Hash128.Compute(read.GetRawTextureData()).ToString();
                var gpu = SystemInfo.graphicsDeviceName;
                if (!Before.TryGetValue(gpu, out var want)) {
                    Assert.Inconclusive($"no bytes recorded on {gpu}; this one read {hash}");
                }
                Assert.That(hash, Is.EqualTo(want), $"on {gpu}");
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void TakingItOffAgain_ReadsTheBytesOfNeverAsking() {
            Sun(-0.4f, -0.6f, 0.7f);
            ModelBridge.SetAmbient(0.3f, 0.4f, 0.6f, 0.2f, 0.15f, 0.1f, 1);
            int ball = Ball();
            var never = Render(ball);
            Assert.That(Leaf(ball, 0.5f, 0.5f), Is.Empty);
            var asked = Render(ball);
            Assert.That(Leaf(ball, 0, 0), Is.Empty);
            var again = Render(ball);
            try {
                Assert.That(asked.GetRawTextureData(), Is.Not.EqualTo(never.GetRawTextureData()), "asking changed nothing, so nothing here is measured");
                Assert.That(again.GetRawTextureData(), Is.EqualTo(never.GetRawTextureData()));
            } finally {
                Object.DestroyImmediate(never);
                Object.DestroyImmediate(asked);
                Object.DestroyImmediate(again);
            }
        }
    }
}
