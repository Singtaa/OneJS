using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's layered sway (ModelBridge.SetMeshSwayLayers, ModelWind.hlsl's
    /// ModelSwayedLayered): four numbers a vertex, a trunk that bends rather than slides, a limb
    /// that swings and a leaf that flutters along its normal, each at its phase, under a gust that
    /// is a field over the ground. Before it a mesh swayed by one number a vertex, sliding along the
    /// wind, which still draws the same bytes.
    /// </summary>
    public class SwayLayersEditModeTests {
        const int Size = 48;

        // What a plain sway's card drew before layered sway, by GPU (Plain_ReadsTheBytesItReadBefore).
        static readonly Dictionary<string, string> Before = new() {
            // OneJS leaf-light ef03b1c, before layered sway, Direct3D 12.
            ["NVIDIA GeForce RTX 3080"] = "ba287967c895e060c58190d3ec6ea433",
            // OneJS leaf-light 4ce2f58, before layered sway, Metal.
            ["Apple M4 Pro"] = "ba287967c895e060c58190d3ec6ea433",
        };

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
            ModelBridge.SetWindClock(0);
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A card 0.6 wide standing 1.6 tall on its foot at the origin, facing -z, its normals `normal`.
        static readonly Vector3[] Card = { new(-0.3f, 0, 0), new(-0.3f, 1.6f, 0), new(0.3f, 1.6f, 0), new(0.3f, 0, 0) };
        static readonly uint[] TwoTriangles = { 0, 1, 2, 0, 2, 3 };

        static int Plain(float top) {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSway(model), Is.Empty);
            Assert.That(ModelBridge.SetMesh(model, Card, TwoTriangles, null, null, null, null, new[] { 0, top, top, 0 }), Is.Empty);
            return model;
        }

        // A layered card: the foot's corners hold still, the top's carry `top` (trunk, limb, leaf, phase).
        static int Layered(Vector4 top, Vector3? normal = null) {
            bool normals = normal != null;
            int model = ModelBridge.CreateMesh(normals, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSwayLayers(model), Is.Empty);
            var foot = new Vector4(0, 0, 0, top.w);
            var sway = new[] { foot, top, top, foot }.SelectMany(s => new[] { s.x, s.y, s.z, s.w }).ToArray();
            var given = normals ? Enumerable.Repeat(normal.Value, 4).ToArray() : null;
            Assert.That(ModelBridge.SetMesh(model, Card, TwoTriangles, given, null, null, null, sway), Is.Empty);
            return model;
        }

        // The model's actor at `at` (or what is there already, with `spawn` false), unlit, seen from
        // 3 m in front of it at half its height, against magenta.
        static Color32[] Shot(int model, Vector3 at = default, bool spawn = true) {
            var target = new RenderTexture(Size, Size, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            int actor = spawn ? ModelBridge.Spawn(model, at.x, at.y, at.z, 0, 1, false, false) : 0;
            try {
                ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
                camera.transform.position = at + new Vector3(0, 0.8f, -3);
                camera.transform.LookAt(at + new Vector3(0, 0.8f, 0));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = was;
                return read.GetPixels32();
            } finally {
                if (spawn) ModelBridge.Destroy(actor);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }

        static bool Background(Color32 p) => p.r > 250 && p.g < 5 && p.b > 250;

        // The highest covered row.
        static int Top(Color32[] shot) {
            for (int y = Size - 1; y >= 0; y--) {
                for (int x = 0; x < Size; x++) if (!Background(shot[y * Size + x])) return y;
            }
            return -1;
        }

        // The mean column of the covered pixels in rows `from` to `to`, or of all of them.
        static float Column(Color32[] shot, int from = 0, int to = Size) {
            float sum = 0;
            int n = 0;
            for (int y = from; y < to; y++) {
                for (int x = 0; x < Size; x++) {
                    if (Background(shot[y * Size + x])) continue;
                    sum += x;
                    n++;
                }
            }
            return n == 0 ? float.NaN : sum / n;
        }

        // The lowest covered row.
        static int Bottom(Color32[] shot) {
            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) if (!Background(shot[y * Size + x])) return y;
            }
            return -1;
        }

        // The covered pixels' mean column along the card's foot, wherever its foot now is.
        static float FootColumn(Color32[] shot) {
            int bottom = Bottom(shot);
            return Column(shot, bottom, bottom + 2);
        }

        // The covered pixels' mean column in the card's top fifth, wherever its top now is.
        static float TopColumn(Color32[] shot) {
            int top = Top(shot);
            return Column(shot, top - 3, top + 1);
        }

        [Test]
        public void Plain_ReadsTheBytesItReadBefore() {
            int model = Plain(1);
            Assert.That(ModelBridge.SetWind(1, 0, 0.5f, 0.4f), Is.Empty);
            ModelBridge.SetWindClock(2.5f);
            var shot = Shot(model);
            var hash = Hash128.Compute(shot.SelectMany(p => new[] { p.r, p.g, p.b, p.a }).ToArray()).ToString();
            var gpu = SystemInfo.graphicsDeviceName;
            if (!Before.TryGetValue(gpu, out var want)) Assert.Inconclusive($"no bytes recorded on {gpu}; this one read {hash}");
            Assert.That(hash, Is.EqualTo(want), $"on {gpu}");
        }

        [Test]
        public void Layers_AreFourAVertex_OnTheMaterialAsTwo() {
            int model = Layered(new Vector4(1, 0, 0, 0));
            Assert.That(ModelBridge.MaterialOf(model).GetFloat("_Sway"), Is.EqualTo(2f));
            var mesh = ModelBridge.MeshOf(model);
            Assert.That(mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord3), Is.EqualTo(4));
            Assert.That(mesh.GetVertexAttributeStream(VertexAttribute.TexCoord3), Is.EqualTo(0), "beside the position, as a plain sway's");
            var sway = new List<Vector4>();
            mesh.GetUVs(3, sway);
            Assert.That(sway[1], Is.EqualTo(new Vector4(1, 0, 0, 0)));
            Assert.That(ModelBridge.MeshOf(Plain(1)).GetVertexAttributeDimension(VertexAttribute.TexCoord3), Is.EqualTo(1), "a plain sway stays one wide");
        }

        [Test]
        public void Trunk_BendsTheCardDownwind_AndItsTopSinks_WhereAPlainSwaySlides() {
            int plain = Plain(2), bent = Layered(new Vector4(2, 0, 0, 0));
            Assert.That(ModelBridge.SetWind(1, 0, 0, 0), Is.Empty);
            var still = Shot(bent);
            Assert.That(ModelBridge.SetWind(1, 0, 1, 0), Is.Empty);
            var slid = Shot(plain);
            var blown = Shot(bent);
            Assert.That(TopColumn(blown), Is.GreaterThan(TopColumn(still) + 3), "the top moves along the wind");
            Assert.That(Column(blown, 0, 4), Is.EqualTo(Column(still, 0, 4)).Within(0.5f), "the foot stays");
            Assert.That(Top(slid), Is.EqualTo(Top(still)).Within(1), "a plain sway slides at the same height");
            Assert.That(Top(blown), Is.LessThan(Top(still) - 2), "a bent trunk keeps its length, so its top sinks");
        }

        [Test]
        public void Limb_SwingsTheCard_AndAtAQuickerBeatThanTheTrunk() {
            int limb = Layered(new Vector4(0, 0.5f, 0, 0));
            Assert.That(ModelBridge.SetWind(1, 0, 0, 0), Is.Empty);
            float still = TopColumn(Shot(limb));
            Assert.That(ModelBridge.SetWind(1, 0, 1, 0), Is.Empty);
            // Over a second and a half the trunk's slow swing barely turns; a limb's goes through
            // more than a half beat, so its top's place spreads further.
            var at = new List<float>();
            for (float t = 0; t <= 1.5f; t += 0.25f) {
                ModelBridge.SetWindClock(t);
                at.Add(TopColumn(Shot(limb)));
            }
            Assert.That(at.Max() - at.Min(), Is.GreaterThan(3), $"the limb swings: {string.Join(", ", at)} against {still} still");
            int none = Layered(new Vector4(0, 0, 0, 0));
            Assert.That(TopColumn(Shot(none)), Is.EqualTo(still).Within(0.5f), "no layer, no movement");
        }

        [Test]
        public void Leaf_FluttersAlongItsNormal_NotAlongTheWind() {
            // The wind blows away from the camera, along +z; the card's normals point along +x.
            int leaf = Layered(new Vector4(0, 0, 0.4f, 0), Vector3.right);
            Assert.That(ModelBridge.SetWind(0, 1, 0, 0), Is.Empty);
            float still = TopColumn(Shot(leaf));
            Assert.That(ModelBridge.SetWind(0, 1, 1, 0), Is.Empty);
            var at = new List<float>();
            for (float t = 0; t < 1; t += 0.125f) {
                ModelBridge.SetWindClock(t);
                at.Add(TopColumn(Shot(leaf)));
            }
            Assert.That(at.Max() - at.Min(), Is.GreaterThan(3), $"the top flutters across the screen: {string.Join(", ", at)}");
            Assert.That(at.Max(), Is.GreaterThan(still + 1), "to the right");
            Assert.That(at.Min(), Is.LessThan(still - 1), "and to the left");
        }

        // A card whose foot's corners carry `foot` and whose top's carry `top`.
        static int LayeredWith(Vector4 foot, Vector4 top) {
            int model = ModelBridge.CreateMesh(true, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSwayLayers(model), Is.Empty);
            var sway = new[] { foot, top, top, foot }.SelectMany(s => new[] { s.x, s.y, s.z, s.w }).ToArray();
            Assert.That(ModelBridge.SetMesh(model, Card, TwoTriangles, Enumerable.Repeat(Vector3.right, 4).ToArray(), null, null, null, sway), Is.Empty);
            return model;
        }

        [Test]
        public void ACardsRoot_MovesWithTheWoodItHangsOn_AndOnlyItsTipFlutters() {
            // Wood and a leaf card on one limb (its phase 40/256 of a turn): at the root the same
            // trunk and limb and no leaf, at the tip the leaf alone adds flutter, at its own phase.
            var root = new Vector4(0.5f, 0.4f, 0, 40);
            int wood = LayeredWith(root, new Vector4(0.8f, 0.6f, 0, 40));
            int leaf = LayeredWith(root, new Vector4(0.8f, 0.6f, 0.3f, 40.7f));
            Assert.That(ModelBridge.SetWind(1, 0, 1, 0.3f), Is.Empty);
            bool tipsApart = false;
            for (float t = 0; t <= 1.5f; t += 0.25f) {
                ModelBridge.SetWindClock(t);
                Color32[] w = Shot(wood), l = Shot(leaf);
                Assert.That(Bottom(l), Is.EqualTo(Bottom(w)), $"the roots stand as one at {t} s");
                Assert.That(FootColumn(l), Is.EqualTo(FootColumn(w)).Within(0.5f), $"the roots move as one at {t} s");
                if (Mathf.Abs(TopColumn(l) - TopColumn(w)) > 1) tipsApart = true;
            }
            Assert.That(tipsApart, Is.True, "the card's tip flutters off the wood's");
        }

        [Test]
        public void Phase_SetsABeatApart_InTurns() {
            // A limb's phase is the whole part in 256ths of a turn: 128 is half a turn, 256 a whole one.
            int a = Layered(new Vector4(0, 0.5f, 0, 0)), b = Layered(new Vector4(0, 0.5f, 0, 128)), whole = Layered(new Vector4(0, 0.5f, 0, 256));
            Assert.That(ModelBridge.SetWind(1, 0, 1, 0), Is.Empty);
            bool apart = false;
            for (float t = 0; t <= 1.5f; t += 0.25f) {
                ModelBridge.SetWindClock(t);
                float x = TopColumn(Shot(a));
                if (Mathf.Abs(TopColumn(Shot(b)) - x) > 2) apart = true;
                Assert.That(TopColumn(Shot(whole)), Is.EqualTo(x).Within(0.1f), $"a whole turn is no turn, at {t} s");
            }
            Assert.That(apart, Is.True, "half a turn swings out of step");
        }

        [Test]
        public void Gust_IsAFieldOverTheGround_SoCopiesSideBySideAcrossTheWindTakeItDifferently() {
            // Copies 60 m apart across a wind along +x stand at the same place along it, so a plain
            // sway's gust, a swell along the wind, scales them alike; the layered sway's is a field.
            var places = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 60), new Vector3(0, 0, 120) };
            float Gain(int model, Vector3 at) {
                ModelBridge.SetWind(1, 0, 1, 0);
                float calm = TopColumn(Shot(model, at));
                ModelBridge.SetWind(1, 0, 1, 1);
                float gusty = TopColumn(Shot(model, at));
                ModelBridge.SetWind(1, 0, 0, 0);
                float still = TopColumn(Shot(model, at));
                return (gusty - still) / (calm - still);
            }
            int plain = Plain(1), layered = Layered(new Vector4(1, 0, 0, 0));
            var plainGains = places.Select(p => Gain(plain, p)).ToArray();
            var layeredGains = places.Select(p => Gain(layered, p)).ToArray();
            Assert.That(plainGains.Max() - plainGains.Min(), Is.LessThan(0.15f), $"plain: {string.Join(", ", plainGains)}");
            Assert.That(layeredGains.Max() - layeredGains.Min(), Is.GreaterThan(0.25f), $"layered: {string.Join(", ", layeredGains)}");
        }

        [Test]
        public void Layers_MoveAScatteredCopy_AsTheyMoveAnActor() {
            int model = Layered(new Vector4(1.5f, 0.3f, 0, 0.2f));
            Assert.That(ModelBridge.SetWind(1, 0, 1, 0.3f), Is.Empty);
            ModelBridge.SetWindClock(1.25f);
            int scatter = ModelBridge.Scatter(model, new float[] { 0, 0, 0, 0, 1 });
            var copy = Shot(model, spawn: false);
            ModelBridge.DestroyScatter(scatter);
            var actor = Shot(model);
            Assert.That(TopColumn(copy), Is.EqualTo(TopColumn(actor)).Within(0.5f));
            Assert.That(Top(copy), Is.EqualTo(Top(actor)).Within(1));
        }

        [Test]
        public void Layers_RefuseWhatTheyCannotTake_SayingWhy() {
            int plain = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSway(plain), Is.Empty);
            Assert.That(ModelBridge.SetMeshSwayLayers(plain), Does.Contain("takes no layers"));
            int layered = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSwayLayers(layered), Is.Empty);
            Assert.That(ModelBridge.SetMeshSwayLayers(layered), Is.Empty, "again is nothing");
            Assert.That(ModelBridge.SetMeshSway(layered), Does.Contain("in layers"));
            Assert.That(ModelBridge.SetMesh(layered, Card, TwoTriangles, null, null, null, null, new float[] { 0, 1, 1, 0 }), Does.Contain("4 a vertex"));
            Assert.That(ModelBridge.SetMesh(layered, Card, TwoTriangles, null, null, null, null, new float[16]), Is.Empty);
            Assert.That(ModelBridge.SetMeshSwayLayers(ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1)), Is.Empty);
            int written = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(written, Card, TwoTriangles, null, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshSwayLayers(written), Does.Contain("before the model's first vertices"));
        }

        [Test]
        public void Layers_TheLightsUnwrapKeepsAllFour() {
            int model = Layered(new Vector4(1, 0.5f, 0.25f, 0.75f));
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            Assert.That(ModelBridge.SetLightUnwrap(model, new uint[] { 0, 1, 2, 3 }, new float[] { 0, 0, 0, 1, 1, 1, 1, 0 }, TwoTriangles), Is.Empty);
            var lit = ModelBridge.ActorObject(actor).GetComponent<MeshFilter>().sharedMesh;
            Assert.That(lit, Is.Not.SameAs(ModelBridge.MeshOf(model)));
            Assert.That(lit.GetVertexAttributeDimension(VertexAttribute.TexCoord3), Is.EqualTo(4));
            var sway = new List<Vector4>();
            lit.GetUVs(3, sway);
            Assert.That(sway[1], Is.EqualTo(new Vector4(1, 0.5f, 0.25f, 0.75f)));
        }
    }
}
