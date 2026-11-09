using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// What a scatter's copies carry of their own (ModelBridge.ScatterWith): a tint and a tilt
    /// each, ten floats a copy, and a range of the camera they draw within, dithered across its
    /// edges so a near mesh and a far one swap per copy. Before them a copy had a place, a yaw and a
    /// scale, and every copy of a shape drew the same colour, upright, at every distance.
    /// </summary>
    public class ScatterLookEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A square two units wide lying on the ground, facing up, drawn unlit so a pixel is its colour.
        static int Square(bool uvs = false) {
            int model = ModelBridge.CreateMesh(false, false, uvs, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) },
                new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, uvs ? new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) } : null), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
            return model;
        }

        // Ten floats a copy: x, y, z, yaw, pitch, roll, scale, r, g, b.
        static float[] Copy(float x, float z, float scale = 0.9f, float pitch = 0, float roll = 0, Color? tint = null) {
            var c = tint ?? Color.white;
            return new[] { x, 0, z, 0, pitch, roll, scale, c.r, c.g, c.b };
        }

        // The ground from straight above at `size` pixels, `half` units either side of the middle,
        // with the camera `height` up: magenta where nothing drew.
        static Color32[] Shot(float half = 4, int size = 64, float height = 10) {
            var target = new RenderTexture(size, size, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try {
                camera.transform.SetPositionAndRotation(new Vector3(0, height, 0), Quaternion.Euler(90, 0, 0));
                camera.orthographic = true;
                camera.orthographicSize = half;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixels32();
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }

        static bool Background(Color32 p) => p.r > 250 && p.g < 5 && p.b > 250;
        static int Covered(Color32[] shot) => shot.Count(p => !Background(p));

        [Test]
        public void ATint_IsMultipliedIntoEachCopysColour() {
            int model = Square();
            var data = Copy(-2, 0, tint: new Color(1, 0, 0)).Concat(Copy(2, 0, tint: new Color(0, 0, 1))).ToArray();
            Assert.That(ModelBridge.ScatterWith(model, data, 10, 0, float.PositiveInfinity, 0), Is.GreaterThan(0));
            var shot = Shot();
            Color32 left = shot[32 * 64 + 16], right = shot[32 * 64 + 48];
            Assert.That(left.r, Is.GreaterThan(240), $"the left copy is tinted red: {left}");
            Assert.That(left.g + left.b, Is.LessThan(10), $"the left copy is tinted red: {left}");
            Assert.That(right.b, Is.GreaterThan(240), $"the right copy is tinted blue: {right}");
            Assert.That(right.r + right.g, Is.LessThan(10), $"the right copy is tinted blue: {right}");
        }

        [Test]
        public void ATintIsSrgb_AsVertexColoursAre() {
            int model = Square();
            ModelBridge.ScatterWith(model, Copy(0, 0, 3, tint: new Color(0.5f, 0.5f, 0.5f)), 10, 0, float.PositiveInfinity, 0);
            var centre = Shot()[32 * 64 + 32];
            // The target is sRGB, so a half grey in sRGB reads back as 128 whatever the project's colour space.
            Assert.That(centre.g, Is.InRange(120, 136), $"half grey: {centre}");
        }

        [Test]
        public void ATilt_TurnsACopyOffUpright() {
            int model = Square();
            ModelBridge.ScatterWith(model, Copy(0, 0, 2), 10, 0, float.PositiveInfinity, 0);
            int flat = Covered(Shot());
            Assert.That(flat, Is.GreaterThan(64 * 64 / 5), "a square lying flat covers the view's middle");
            ModelBridge.DisposeAll();
            ModelBridge.BeginScene();
            model = Square();
            int pitched = ModelBridge.ScatterWith(model, Copy(0, 0, 2, pitch: 90), 10, 0, float.PositiveInfinity, 0);
            Assert.That(Covered(Shot()), Is.LessThan(flat / 10), "pitched a quarter turn it stands on edge to the camera");
            var bounds = ModelBridge.ScatterChunks(pitched)[0].bounds;
            Assert.That(bounds.max.y, Is.GreaterThanOrEqualTo(1.99f), $"and its chunk is measured standing: {bounds}");
            ModelBridge.DisposeAll();
            ModelBridge.BeginScene();
            model = Square();
            ModelBridge.ScatterWith(model, Copy(0, 0, 2, roll: 90), 10, 0, float.PositiveInfinity, 0);
            Assert.That(Covered(Shot()), Is.LessThan(flat / 10), "rolled too");
        }

        [Test]
        public void TenFloatsWithNoTiltOrTint_DrawAsFive() {
            int model = Square();
            ModelBridge.Scatter(model, new float[] { -2, 0, 1, 30, 0.8f, 2, 0, -1, 70, 0.6f });
            var five = Shot();
            ModelBridge.DisposeAll();
            ModelBridge.BeginScene();
            model = Square();
            ModelBridge.ScatterWith(model, new float[] { -2, 0, 1, 30, 0, 0, 0.8f, 1, 1, 1, 2, 0, -1, 70, 0, 0, 0.6f, 1, 1, 1 }, 10, 0, float.PositiveInfinity, 0);
            Assert.That(Shot(), Is.EqualTo(five));
        }

        // A grid of copies a unit apart under the camera, 10 to 14 units from it.
        static float[] Grid(Color tint) {
            var data = new System.Collections.Generic.List<float>();
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++) data.AddRange(Copy(x, z, 0.45f, tint: tint));
            return data.ToArray();
        }

        [Test]
        public void ARange_DrawsOnlyTheCopiesWithinIt() {
            int model = Square();
            ModelBridge.ScatterWith(model, Grid(Color.white), 10, 0, float.PositiveInfinity, 0);
            int all = Covered(Shot(8, 128));
            ModelBridge.DisposeAll();
            ModelBridge.BeginScene();
            model = Square();
            ModelBridge.ScatterWith(model, Grid(Color.white), 10, 0, 12, 0);
            var near = Shot(8, 128);
            Assert.That(Covered(near), Is.InRange(all / 5, all * 4 / 5), "the copies within 12 of the camera, the middle of the grid");
            Assert.That(Background(near[8 * 128 + 8]), Is.True, "a corner copy, 14 away, is not drawn");
            Assert.That(Background(near[64 * 128 + 64]), Is.False, "the middle one, 10 away, is");
        }

        // The point of the dither: a near mesh and a far one meeting at 12 together cover exactly
        // what one scatter of every copy covers, each pixel by one of them, across the fade too.
        [Test]
        public void ANearAndAFarRangeMeetingAtAnEdge_CoverEveryPixelOnce() {
            int model = Square();
            ModelBridge.ScatterWith(model, Grid(Color.white), 10, 0, float.PositiveInfinity, 0);
            var whole = Shot(8, 128);
            ModelBridge.DisposeAll();
            ModelBridge.BeginScene();
            model = Square();
            int near = ModelBridge.ScatterWith(model, Grid(Color.red), 10, 0, 12, 2);
            int nearAlone = Covered(Shot(8, 128));
            int far = ModelBridge.ScatterWith(model, Grid(Color.blue), 10, 12, float.PositiveInfinity, 2);
            var pair = Shot(8, 128);
            ModelBridge.DestroyScatter(near);
            int farAlone = Covered(Shot(8, 128));
            Assert.That(nearAlone + farAlone, Is.EqualTo(Covered(whole)), "no pixel is drawn by both, and none by neither");
            int red = pair.Count(p => p.r > 240 && p.b < 15), blue = pair.Count(p => p.b > 240 && p.r < 15);
            Assert.That(red, Is.GreaterThan(0));
            Assert.That(blue, Is.GreaterThan(0));
            Assert.That(red + blue, Is.EqualTo(Covered(pair)), "every pixel drawn is one or the other");
            for (int i = 0; i < whole.Length; i++) {
                if (Background(whole[i]) != Background(pair[i])) Assert.Fail($"pixel {i % 128}, {i / 128}: one scatter {(Background(whole[i]) ? "missed" : "drew")} it, the pair {(Background(pair[i]) ? "missed" : "drew")} it");
            }
            // And the fade dithered: some copy within a unit of the edge has pixels of both.
            int mixed = 0;
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++) {
                float d = Mathf.Sqrt(x * x + z * z + 100);
                if (Mathf.Abs(d - 12) > 0.8f) continue;
                int px = Mathf.RoundToInt((x + 8) * 8), pz = Mathf.RoundToInt((z + 8) * 8);
                bool r = false, b = false;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) {
                    var p = pair[(pz + dz) * 128 + px + dx];
                    r |= p.r > 240 && p.b < 15;
                    b |= p.b > 240 && p.r < 15;
                }
                if (r && b) mixed++;
            }
            Assert.That(mixed, Is.GreaterThan(0), "a copy across the fade is drawn partly by each");
        }

        [Test]
        public void AChunkWhollyOutsideItsRange_IsNotDrawnAtAll() {
            var bounds = new Bounds(new Vector3(100, 0, 0), new Vector3(10, 2, 10));
            var eye = Vector3.zero;
            Assert.That(ModelBridge.OutOfRange(bounds, eye, new Vector4(-1e30f, 50, 2, 1)), Is.True, "95 away at its nearest, past 50 and its fade");
            Assert.That(ModelBridge.OutOfRange(bounds, eye, new Vector4(-1e30f, 94, 2, 1)), Is.False, "within the fade of 94");
            Assert.That(ModelBridge.OutOfRange(bounds, eye, new Vector4(200, 1e30f, 2, 1)), Is.True, "105 away at its furthest, short of 200");
            Assert.That(ModelBridge.OutOfRange(bounds, eye, new Vector4(100, 1e30f, 2, 1)), Is.False, "some of it is past 100");
        }

        [Test]
        public void ScatterWith_RefusesWhatItCannotDraw() {
            int model = Square();
            Assert.Throws<System.ArgumentException>(() => ModelBridge.ScatterWith(model, new float[12], 6, 0, float.PositiveInfinity, 0), "a stride of 6");
            Assert.Throws<System.ArgumentException>(() => ModelBridge.ScatterWith(model, new float[15], 10, 0, float.PositiveInfinity, 0), "not ten a copy");
            Assert.Throws<System.ArgumentException>(() => ModelBridge.ScatterWith(model, new float[10], 10, 50, 40, 0), "to short of from");
            Assert.Throws<System.ArgumentException>(() => ModelBridge.ScatterWith(model, new float[10], 10, -1, 40, 0), "from below 0");
            Assert.Throws<System.ArgumentException>(() => ModelBridge.ScatterWith(model, new float[10], 10, 0, 40, float.NaN), "fade NaN");
            Assert.That(ModelBridge.ScatterCount, Is.Zero);
        }

        [Test]
        public void AScatterWithNeitherTintNorRange_DrawsWithNoBlock_AsBefore() {
            int model = Square();
            int plain = ModelBridge.Scatter(model, new float[] { 0, 0, 0, 0, 1 });
            Assert.That(ModelBridge.ScatterBlock(plain, 0), Is.Null, "the draw it always was");
            Assert.That(ModelBridge.ScatterRange(plain).w, Is.Zero);
            int ranged = ModelBridge.ScatterWith(model, new float[] { 0, 0, 0, 0, 1 }, 5, 10, 20, 4);
            Assert.That(ModelBridge.ScatterRange(ranged), Is.EqualTo(new Vector4(10, 20, 2, 1)));
            Assert.That(ModelBridge.ScatterBlock(ranged, 0).GetVector("_ScatterRange"), Is.EqualTo(new Vector4(10, 20, 2, 1)));
            int far = ModelBridge.ScatterWith(model, new float[] { 0, 0, 0, 0, 1 }, 5, 0, 20, 0);
            Assert.That(ModelBridge.ScatterRange(far).x, Is.LessThan(-1e29f), "no near edge");
        }

        // PrimoEden's rocks: a mesh painted by a splat of two triplanar layers, scattered.
        [Test]
        public void ASplatMesh_DrawsThroughScatter_TintedPerCopy() {
            var grey = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            grey.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            grey.Apply();
            try {
                int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMeshSplat(model, grey, grey, null, null, 1, 1, 0), Is.Empty);
                Assert.That(ModelBridge.SetMesh(model, new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) },
                    new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                    new float[] { 1, 0, 1, 0, 0, 1, 0, 1 }), Is.Empty);
                Assert.That(ModelBridge.SetMeshSplatTriplanar(model, 2, 3, 0, 0), Is.Empty);
                var material = ModelBridge.MaterialOf(model);
                Assert.That(material.shader.name, Is.EqualTo("OneJS/GroundLit"));
                material.SetFloat("_Unlit", 1);
                ModelBridge.ScatterWith(model, Copy(-2, 0, tint: new Color(0, 1, 0)).Concat(Copy(2, 0, pitch: 10)).ToArray(), 10, 0, float.PositiveInfinity, 0);
                var shot = Shot();
                Color32 left = shot[32 * 64 + 16], right = shot[32 * 64 + 48];
                Assert.That(left.g, Is.GreaterThan(240), $"the green copy: {left}");
                Assert.That(left.r + left.b, Is.LessThan(10), $"the green copy: {left}");
                Assert.That(right.r > 240 && right.g > 240 && right.b > 240, Is.True, $"the white one: {right}");
            } finally {
                Object.DestroyImmediate(grey);
            }
        }
    }
}
