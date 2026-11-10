using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.SetSkyReflect: a glossy surface shows the scene's sky along its reflected ray
    /// once the cart asks, and draws exactly as it did before when it does not. Before it, every
    /// glossy surface reflected the ambient's flat average, the same colour whichever way it faced.
    /// A chrome ball under a blue sky with an orange horizon, seen side on.
    /// </summary>
    public class SkyReflectEditModeTests {
        // Odd, so the centre pixel looks exactly where the camera does.
        const int Size = 65;
        static readonly Color Top = new(0.1f, 0.3f, 1f);
        static readonly Color Horizon = new(1f, 0.5f, 0.1f);

        // The bytes the unasked scene read back before reflections existed (OneJS a9643f1), by
        // the GPU that read them: the same shader code on another GPU may round differently.
        static readonly Dictionary<string, string> Before = new() {
            ["Apple M4 Pro"] = "645968ba9d5dd01d537a078ff72d7de9",
        };

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(0, 0, -6, 0, 0, 0, 30);
            ModelBridge.SetSky(true, Top.r, Top.g, Top.b, Horizon.r, Horizon.g, Horizon.b, Horizon.r, Horizon.g, Horizon.b, 0, 0, 0);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A unit ball with its own normals, a mirror unless told otherwise.
        static int Ball(float roughness = 0.02f, float metallic = 1) {
            const int rings = 48, segments = 96;
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
            int model = ModelBridge.CreateMesh(true, false, false, 0.95f, 0.95f, 0.95f, 1);
            var p = positions.ToArray();
            Assert.That(ModelBridge.SetMesh(model, p, indices.ToArray(), p, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshSurface(model, roughness, metallic), Is.Empty);
            return model;
        }

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

        [Test]
        public void AMirrorBall_ShowsTheSkyAboveAndTheHorizonAtItsMiddle() {
            ModelBridge.SetSkyReflect(true);
            var read = Render(Ball());
            try {
                // The ball is about 20 pixels across its radius. At its middle the ray comes
                // straight back, level, to the horizon; 16 pixels up it turns about 50 degrees
                // into the sky.
                var middle = read.GetPixel(Size / 2, Size / 2);
                var upper = read.GetPixel(Size / 2, Size / 2 + 16);
                Assert.That(middle.r, Is.GreaterThan(2 * middle.b), $"the middle reflects the orange horizon: {middle}");
                Assert.That(upper.b, Is.GreaterThan(2 * upper.r), $"the upper part reflects the blue sky: {upper}");
                Assert.That(middle.r, Is.GreaterThan(0.3f), $"and brightly, as chrome does: {middle}");
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void ARoughBall_KeepsCloseToTheFlatLook() {
            int ball = Ball(1, 0);
            var flat = Render(ball);
            ModelBridge.SetSkyReflect(true);
            var asked = Render(ball);
            try {
                var a = flat.GetPixel(Size / 2, Size / 2 + 16);
                var b = asked.GetPixel(Size / 2, Size / 2 + 16);
                float moved = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                Assert.That(moved, Is.LessThan(0.15f), $"a rough non-metal barely reflects: {a} against {b}");
            } finally {
                Object.DestroyImmediate(flat);
                Object.DestroyImmediate(asked);
            }
        }

        [Test]
        public void ASceneThatDoesNotAsk_ReadsTheBytesItReadBefore() {
            ModelBridge.SetSun(true, -0.4f, -0.6f, 0.7f, 1, 0.95f, 0.9f, 1.2f, false);
            var read = Render(Ball(0.06f, 0.4f));
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

        // A rough floor seen at a grazing angle, its middle pixel, under a sky whose colour below the
        // horizon is `bottom`.
        static Color RoughFloorAtGrazing(Color bottom) {
            ModelBridge.SetSky(true, Top.r, Top.g, Top.b, Horizon.r, Horizon.g, Horizon.b, bottom.r, bottom.g, bottom.b, 0, 0, 0);
            ModelBridge.SetSkyReflect(true);
            ModelBridge.SetCamera(0, 1.5f, -20, 0, 0, 20, 30);
            int floor = ModelBridge.CreateMesh(true, false, false, 0.3f, 0.3f, 0.3f, 1);
            var p = new[] { new Vector3(-50, 0, -50), new Vector3(-50, 0, 50), new Vector3(50, 0, 50), new Vector3(50, 0, -50) };
            var n = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            Assert.That(ModelBridge.SetMesh(floor, p, new uint[] { 0, 1, 2, 0, 2, 3 }, n, null, null), Is.Empty);
            var read = Render(floor);
            try {
                return read.GetPixel(Size / 2, Size / 2 - 8);
            } finally {
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void ARoughFloorAtGrazing_ReflectsTheSkyAboveIt_NotTheColourBelowTheHorizon() {
            var dark = RoughFloorAtGrazing(Color.black);
            var light = RoughFloorAtGrazing(Color.white);
            float moved = Mathf.Abs(dark.r - light.r) + Mathf.Abs(dark.g - light.g) + Mathf.Abs(dark.b - light.b);
            Assert.That(dark.r + dark.g + dark.b, Is.GreaterThan(0.05f), $"the floor is not lit, so nothing here is measured: {dark}");
            Assert.That(moved, Is.LessThan(0.004f), $"a rough floor took the colour below the horizon: {dark} under a black one, {light} under a white one");
        }

        [Test]
        public void TurningItOffAgain_ReadsTheBytesOfNeverAsking() {
            int ball = Ball(0.06f, 0.4f);
            var never = Render(ball);
            ModelBridge.SetSkyReflect(true);
            Object.DestroyImmediate(Render(ball));
            ModelBridge.SetSkyReflect(false);
            var again = Render(ball);
            try {
                Assert.That(again.GetRawTextureData(), Is.EqualTo(never.GetRawTextureData()));
            } finally {
                Object.DestroyImmediate(never);
                Object.DestroyImmediate(again);
            }
        }

        [Test]
        public void TheSkyGoing_TakesTheReflectionWithIt() {
            ModelBridge.SetSkyReflect(true);
            ModelBridge.SetSky(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.That(Shader.GetGlobalVector("_OjSkyReflect").x, Is.EqualTo(0f), "no sky, nothing to reflect");
        }
    }
}
