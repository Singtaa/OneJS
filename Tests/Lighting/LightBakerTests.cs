using System;
using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Lighting {
    /// <summary>
    /// The runtime baker against light whose answer is known: an open floor under the ambient bakes to
    /// exactly what the ambient probe gives it, a probe in the open is the ambient probe, a wall darkens
    /// and tints the floor at its foot on the side it stands, and the sun's first hit is never baked.
    /// </summary>
    public class LightBakerTests {
        static readonly Color Sky = new Color(0.6f, 0.7f, 0.9f), Ground = new Color(0.3f, 0.2f, 0.1f);

        sealed class SceneBuilder {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            public readonly List<Color> Albedo = new List<Color>();

            /// <summary>A quad from `o` along `u` and `v`, facing `normal`, laid out in the atlas over `rect`: o + a u + b v sits at rect's (a, b).</summary>
            public SceneBuilder Quad(Vector3 o, Vector3 u, Vector3 v, Vector3 normal, Rect rect, Color albedo) {
                var first = Positions.Count;
                foreach (var (a, b) in new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f) }) {
                    Positions.Add(o + a * u + b * v);
                    Normals.Add(normal.normalized);
                    Uvs.Add(new Vector2(rect.xMin + a * rect.width, rect.yMin + b * rect.height));
                }
                Indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                Albedo.Add(albedo);
                Albedo.Add(albedo);
                return this;
            }

            /// <summary>A closed box facing out, its faces laid out in the corner of the atlas no test reads.</summary>
            public SceneBuilder Box(Vector3 centre, float size) {
                var h = size / 2;
                var r = new Rect(0.96f, 0.96f, 0.01f, 0.01f);
                var c = Color.gray;
                Quad(centre + new Vector3(-h, -h, -h), new Vector3(size, 0, 0), new Vector3(0, 0, size), Vector3.down, r, c);
                Quad(centre + new Vector3(-h, h, -h), new Vector3(size, 0, 0), new Vector3(0, 0, size), Vector3.up, r, c);
                Quad(centre + new Vector3(-h, -h, -h), new Vector3(size, 0, 0), new Vector3(0, size, 0), Vector3.back, r, c);
                Quad(centre + new Vector3(-h, -h, h), new Vector3(size, 0, 0), new Vector3(0, size, 0), Vector3.forward, r, c);
                Quad(centre + new Vector3(-h, -h, -h), new Vector3(0, 0, size), new Vector3(0, size, 0), Vector3.left, r, c);
                Quad(centre + new Vector3(h, -h, -h), new Vector3(0, 0, size), new Vector3(0, size, 0), Vector3.right, r, c);
                return this;
            }

            public BakeScene Build(int atlas = 64, int pages = 1) => new BakeScene {
                Positions = Positions.ToArray(), Normals = Normals.ToArray(), Uvs = Uvs.ToArray(), Indices = Indices.ToArray(), Albedo = Albedo.ToArray(),
                AtlasWidth = atlas, AtlasHeight = atlas, Pages = pages, Sky = Sky, Ground = Ground,
            };
        }

        // A 4 m floor in the left half of the atlas, its z along v; a 3 m wall standing at z = 0 in the right half.
        static readonly Rect FloorRect = new Rect(0.05f, 0.05f, 0.4f, 0.9f), WallRect = new Rect(0.55f, 0.05f, 0.4f, 0.9f);
        static Vector2 FloorUv(float x, float z) => new Vector2(FloorRect.xMin + x / 4 * FloorRect.width, FloorRect.yMin + z / 4 * FloorRect.height);
        static Vector2 WallUv(float x, float y) => new Vector2(WallRect.xMin + x / 4 * WallRect.width, WallRect.yMin + y / 3 * WallRect.height);

        static SceneBuilder Floor(Color albedo) => new SceneBuilder().Quad(Vector3.zero, new Vector3(4, 0, 0), new Vector3(0, 0, 4), Vector3.up, FloorRect, albedo);
        static SceneBuilder FloorAndWall(Color floor, Color wall, int wallPage = 0) {
            var rect = WallRect;
            rect.x += wallPage;
            return Floor(floor).Quad(Vector3.zero, new Vector3(4, 0, 0), new Vector3(0, 3, 0), Vector3.forward, rect, wall);
        }

        static BakeSettings Quick(int samples = 64) => new BakeSettings { Samples = samples, DraftSamples = samples, ProbeRays = 1024, Detail = 16 };

        static LightBaker Bake(BakeScene scene, BakeSettings settings) {
            var baker = new LightBaker(scene, settings);
            baker.RunToEnd();
            return baker;
        }

        /// <summary>What a model's shader reads from the lightmap at `uv`, on page `page`.</summary>
        static Color Sample(RenderTexture lightmap, Vector2 uv, int page = 0) {
            var shader = Shader.Find("Hidden/OneJS/Tests/LightmapSample");
            Assert.IsNotNull(shader, "the test's sampling shader");
            var mat = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try {
                mat.SetTexture("_Lightmap", lightmap);
                mat.SetVector("_At", uv);
                mat.SetFloat("_Slice", page);
                var cmd = new CommandBuffer();
                cmd.SetRenderTarget(target);
                cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                return Read(target)[0];
            } finally {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }

        /// <summary>A target's texels, or one slice's of the lightmap's.</summary>
        static Color[] Read(RenderTexture rt, int slice = 0) => TextureRead.Start(rt, 0, 0, rt.width, rt.height, slice).Wait().Colors();

        static void Near(Color expected, Color actual, float relative, string what, float absolute = 1e-3f) {
            for (var c = 0; c < 3; c++) Assert.AreEqual(expected[c], actual[c], Mathf.Max(absolute, Mathf.Abs(expected[c]) * relative), $"{what}, channel {c}: expected {expected}, got {actual}");
        }

        [TearDown]
        public void ReadAsTheGpuDoes() {
            TextureRead.UseReadPixels = false;
            TextureRead.Hold = false;
        }

        [SetUp]
        public void RequireGpu() {
            if (!LightBaker.Supported(out var why)) Assert.Ignore($"this GPU cannot bake: {why}");
        }

        [Test]
        public void AnOpenFloorBakesToTheSkyTheAmbientProbeGivesIt() {
            // Facing up with nothing above, a surface receives the sky alone, which is SampleSH's a + b for up.
            using var baker = Bake(Floor(Color.gray).Build(), Quick());
            var pixels = Read(baker.Lightmap);
            var covered = 0;
            foreach (var p in pixels) {
                if (p.a == 0) continue;
                covered++;
                Near(Sky, p, 0.01f, "a floor texel");
            }
            Assert.Greater(covered, 64 * 64 * 0.3f, "the floor's texels");
            Near(Sky, Sample(baker.Lightmap, FloorUv(2, 2)), 0.01f, "the floor's middle, read as a model reads it");
        }

        [Test]
        public void AProbeInTheOpenIsTheAmbientProbe() {
            // A scene of one tiny face far below: every ray from the grid meets the sky or the ground.
            var scene = new SceneBuilder().Quad(new Vector3(0, -1000, 0), new Vector3(0.01f, 0, 0), new Vector3(0, 0, 0.01f), Vector3.up, new Rect(0.1f, 0.1f, 0.5f, 0.5f), Color.gray).Build(16);
            scene.GridOrigin = Vector3.zero;
            scene.GridSpacing = 1;
            scene.GridCounts = new Vector3Int(2, 1, 2);
            using var baker = Bake(scene, Quick(4));
            Assert.IsTrue(baker.ProbesReady);
            Assert.AreEqual(4 * ProbeSolve.Floats, baker.ProbeSH.Length);
            var directions = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, new Vector3(1, 1, 1).normalized, new Vector3(-1, -0.5f, 0.3f).normalized };
            var results = new Color[directions.Length];
            for (var p = 0; p < 4; p++) {
                ProbeSolve.ToUnity(baker.ProbeSH, p).Evaluate(directions, results);
                for (var i = 0; i < directions.Length; i++) {
                    // HemisphereProbe: the average plus half the difference times the normal's height.
                    var expected = (Sky + Ground) * 0.5f + (Sky - Ground) * (0.5f * directions[i].y);
                    // 1,024 rays spread evenly: within 2% of the sky's brightest channel. Random rays missed 5% by half.
                    Near(expected, results[i], 0, $"probe {p} facing {directions[i]}", 0.02f * Sky.b);
                }
            }
        }

        [Test]
        public void AWallDarkensAndTintsTheFloorAtItsFootOnTheSideItStands() {
            using var baker = Bake(FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f)).Build(), Quick());
            var near = Sample(baker.Lightmap, FloorUv(2, 0.3f));
            var far = Sample(baker.Lightmap, FloorUv(2, 3.7f));
            Assert.Less(near.g, far.g * 0.8f, $"the floor at the wall's foot ({near}) is darker than across the room ({far})");
            Assert.Greater(near.r / near.g, far.r / far.g * 1.1f, $"and redder, from the wall's bounce ({near} against {far})");
            // The wall faces the floor and the sky in equal parts: half sky, half the floor's reflection.
            var wall = Sample(baker.Lightmap, WallUv(2, 2.5f));
            Assert.Greater(wall.b, 0.1f);
            Assert.Less(wall.b, Sky.b);
        }

        [Test]
        public void TheSunsFirstHitIsLeftLiveAndItsBounceIsBaked() {
            Color Wall(Color sun) {
                var scene = FloorAndWall(Color.gray, Color.gray).Build();
                scene.Sky = scene.Ground = Color.black;
                scene.Sun = sun;
                scene.SunDirection = new Vector3(0, 1, 1).normalized;
                using var baker = Bake(scene, Quick());
                return Sample(baker.Lightmap, WallUv(2, 0.5f));
            }
            var dark = Wall(Color.black);
            var lit = Wall(Color.white);
            Assert.Less(dark.maxColorComponent, 1e-3f, "no sun, no sky: no light");
            // Sunlit at cos 45 degrees, the wall would be 0.71 with its direct light; only the floor's bounce is baked.
            Assert.Greater(lit.r, 0.02f, $"the floor's bounce reaches the wall ({lit})");
            Assert.Less(lit.r, 0.3f, $"and the sun's own light does not ({lit})");
        }

        [Test]
        public void AProbeInsideASolidTakesItsNeighboursLight() {
            var scene = Floor(Color.gray).Box(new Vector3(2, 1, 2), 0.5f).Build();
            scene.GridOrigin = new Vector3(0, 1, 2);
            scene.GridSpacing = 2;
            scene.GridCounts = new Vector3Int(3, 1, 1);
            using var baker = Bake(scene, Quick(4));
            var sh = baker.ProbeSH;
            for (var i = 0; i < ProbeSolve.Floats; i++) {
                var mean = (sh[i] + sh[2 * ProbeSolve.Floats + i]) / 2;
                Assert.AreEqual(mean, sh[ProbeSolve.Floats + i], 1e-5f, $"the boxed probe's float {i}");
            }
            // The two outside are lit, not filled.
            Assert.Greater(sh[0], 0.1f);
        }

        [Test]
        public void MoreSamplesComeCloserToTheAnswer() {
            float Error(Color[] a, Color[] reference) {
                double se = 0, sum = 0;
                for (var i = 0; i < a.Length; i++) {
                    if (reference[i].a == 0) continue;
                    for (var c = 0; c < 3; c++) {
                        se += (a[i][c] - reference[i][c]) * (a[i][c] - reference[i][c]);
                        sum += reference[i][c];
                    }
                }
                return (float)(Math.Sqrt(se) / sum * Math.Sqrt(a.Length));
            }
            Color[] Baked(int samples) {
                var settings = Quick(samples);
                settings.DenoiseRadius = 0;
                using var baker = Bake(FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f)).Build(), settings);
                return Read(baker.Lightmap);
            }
            var reference = Baked(1024);
            float e16 = Error(Baked(16), reference), e128 = Error(Baked(128), reference);
            // Noise falls as the root of the samples: eight times the samples, a little under three times less.
            Assert.Less(e128, e16 / 2, $"16 samples {e16}, 128 samples {e128}");
        }

        [Test]
        public void TheSameSceneBakesTheSameLight() {
            using var first = Bake(FloorAndWall(Color.gray, Color.red).Build(), Quick(16));
            using var second = Bake(FloorAndWall(Color.gray, Color.red).Build(), Quick(16));
            CollectionAssert.AreEqual(Read(first.Lightmap), Read(second.Lightmap));
        }

        [Test]
        public void StepsDoTheProbesFirstThenTheDraftThenTheEnd() {
            var scene = FloorAndWall(Color.gray, Color.gray).Build();
            scene.GridOrigin = new Vector3(0.5f, 1, 0.5f);
            scene.GridCounts = new Vector3Int(3, 2, 3);
            using var baker = new LightBaker(scene, new BakeSettings { Samples = 16, DraftSamples = 8, ProbeRays = 64, TileSize = 32 });
            var progress = 0f;
            var order = new List<string>();
            // Waiting on what it reads back after each step, as a frame between steps would.
            for (var steps = 0; !baker.Step(1); steps++, AsyncGPUReadback.WaitAllRequests()) {
                Assert.Less(steps, 10_000, "a bake that never ends");
                Assert.GreaterOrEqual(baker.Progress, progress, "progress only grows");
                progress = baker.Progress;
                if (baker.ProbesReady && !order.Contains("probes")) order.Add("probes");
                if (baker.DraftReady && !order.Contains("draft")) order.Add("draft");
            }
            order.Add("done");
            CollectionAssert.AreEqual(new[] { "probes", "draft", "done" }, order);
            Assert.AreEqual(1f, baker.Progress);
            Assert.AreEqual(16, baker.Samples);
            Assert.AreEqual(TextureDimension.Tex2DArray, baker.Lightmap.dimension);
            Assert.AreEqual(1, baker.Lightmap.volumeDepth);
        }

        [Test]
        public void APageAtATime_EachLitByTheWholeWorld_AsOnePageWouldBe() {
            var settings = Quick();
            settings.TileSize = 32;
            using var one = Bake(FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f)).Build(), settings);
            using var two = new LightBaker(FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f), wallPage: 1).Build(pages: 2), settings);
            Assert.AreEqual(2, two.Pages);
            // A page at a time, in order, the first lit before the second is begun.
            var seen = new List<(int page, int lit)>();
            for (var steps = 0; ; steps++, two.Sync()) {
                Assert.Less(steps, 10_000, "a bake that never ends");
                var done = two.Step(two.TilePaths);
                if (seen.Count == 0 || seen[^1] != (two.Page, two.PagesLit)) seen.Add((two.Page, two.PagesLit));
                if (done) break;
            }
            CollectionAssert.AreEqual(new[] { (0, 0), (1, 1), (1, 2) }, seen);
            Assert.AreEqual(2, two.Lightmap.volumeDepth);
            // The floor at the wall's foot takes the wall's red from the other page's triangles, and the wall on page 1 is lit as on one page.
            Near(Sample(one.Lightmap, FloorUv(2, 0.3f)), Sample(two.Lightmap, FloorUv(2, 0.3f)), 0.15f, "the floor at the wall's foot");
            Near(Sample(one.Lightmap, WallUv(2, 1.5f)), Sample(two.Lightmap, WallUv(2, 1.5f), 1), 0.15f, "the wall, on its page");
            var foot = Sample(two.Lightmap, FloorUv(2, 0.3f));
            Assert.Greater(foot.r, foot.g * 1.05f, $"the wall's red reaches the floor across pages ({foot})");
            // And the wall's rectangle on page 0 holds nothing.
            Assert.AreEqual(0f, Read(two.Lightmap, 0)[(int)(WallUv(2, 1.5f).y * 64) * 64 + (int)(WallUv(2, 1.5f).x * 64)].a, "nothing of the wall on the floor's page");
        }

        static LightBaker ProbedBaker() {
            var scene = FloorAndWall(Color.gray, Color.gray).Build();
            scene.GridOrigin = new Vector3(0.5f, 1, 0.5f);
            scene.GridCounts = new Vector3Int(3, 2, 3);
            return new LightBaker(scene, new BakeSettings { Samples = 4, DraftSamples = 4, ProbeRays = 64 });
        }

        [Test]
        public void ABakeIsDoneOnlyOnceItsProbesAreReadBack() {
            if (!TextureRead.Async) Assert.Ignore("this GPU has no async readback, so its probes are read at once");
            using var baker = ProbedBaker();
            TextureRead.Hold = true;
            try {
                // One step makes every draw, and the probes' read is still on its way.
                Assert.IsFalse(baker.Step(long.MaxValue), "done before its probes were read");
                Assert.IsFalse(baker.Step(long.MaxValue), "a finished lightmap waits on the probes");
                Assert.IsNull(baker.ProbeSH);
                Assert.IsTrue(baker.DraftReady);
                Assert.Less(baker.Progress, 1f);
            } finally {
                TextureRead.Hold = false;
            }
            AsyncGPUReadback.WaitAllRequests();
            Assert.IsTrue(baker.Step(1));
            Assert.IsNotNull(baker.ProbeSH);
            Assert.AreEqual(1f, baker.Progress);
        }

        [Test]
        public void ProbesReadByReadPixelsAreTheSameAndReadAtOnce() {
            float[] async;
            using (var baker = ProbedBaker()) {
                baker.RunToEnd();
                async = baker.ProbeSH;
            }
            TextureRead.UseReadPixels = true;
            try {
                using var baker = ProbedBaker();
                Assert.IsTrue(baker.Step(long.MaxValue), "ReadPixels lands when it is made");
                CollectionAssert.AreEqual(async, baker.ProbeSH);
            } finally {
                TextureRead.UseReadPixels = false;
            }
        }

        [Test]
        public void RefusesAMalformedScene() {
            var scene = Floor(Color.gray).Build();
            scene.Albedo = new Color[1];
            Assert.Throws<ArgumentException>(() => new LightBaker(scene));
            scene = Floor(Color.gray).Build();
            scene.Indices = new[] { 0, 1 };
            Assert.Throws<ArgumentException>(() => new LightBaker(scene));
            // The wall on a second page of an atlas of one.
            Assert.Throws<ArgumentException>(() => new LightBaker(FloorAndWall(Color.gray, Color.gray, wallPage: 1).Build()));
        }
    }

    public class BvhTests {
        [Test]
        public void EveryTriangleIsInOneLeafInsideItsBox() {
            var rng = new System.Random(3);
            float R() => (float)rng.NextDouble() * 10;
            var positions = new Vector3[300];
            for (var i = 0; i < positions.Length; i++) positions[i] = new Vector3(R(), R(), R());
            var indices = new int[300];
            for (var i = 0; i < indices.Length; i++) indices[i] = i;
            var (nodes, order) = Bvh.Build(positions, indices);
            CollectionAssert.AreEquivalent(System.Linq.Enumerable.Range(0, 100), order);

            var seen = new int[100];
            void Walk(int n, Vector3 lo, Vector3 hi) {
                var o = n * 8;
                var min = new Vector3(nodes[o], nodes[o + 1], nodes[o + 2]);
                var max = new Vector3(nodes[o + 4], nodes[o + 5], nodes[o + 6]);
                Assert.IsTrue(min.x >= lo.x && min.y >= lo.y && min.z >= lo.z && max.x <= hi.x && max.y <= hi.y && max.z <= hi.z, $"node {n} inside its parent");
                var count = (int)nodes[o + 7];
                if (count > 0) {
                    Assert.LessOrEqual(count, Bvh.LeafSize);
                    for (var j = (int)nodes[o + 3]; j < (int)nodes[o + 3] + count; j++) {
                        var t = order[j];
                        seen[t]++;
                        for (var k = 0; k < 3; k++) {
                            var p = positions[indices[t * 3 + k]];
                            Assert.IsTrue(p.x >= min.x && p.y >= min.y && p.z >= min.z && p.x <= max.x && p.y <= max.y && p.z <= max.z, $"triangle {t} inside leaf {n}");
                        }
                    }
                    return;
                }
                Walk(n + 1, min, max);
                Walk((int)nodes[o + 3], min, max);
            }
            Walk(0, Vector3.one * float.MinValue, Vector3.one * float.MaxValue);
            CollectionAssert.AreEqual(System.Linq.Enumerable.Repeat(1, 100), seen);
        }
    }

    public class ProbeSolveTests {
        [Test]
        public void FillsASolidFromOutsideARingAtATime() {
            // A row of five, the middle three inside: the outer two fill their neighbours, then the middle.
            var sh = new float[5 * ProbeSolve.Floats];
            for (var i = 0; i < ProbeSolve.Floats; i++) {
                sh[i] = 1;
                sh[4 * ProbeSolve.Floats + i] = 3;
            }
            ProbeSolve.Fill(sh, new[] { true, false, false, false, true }, new Vector3Int(5, 1, 1), Color.white, Color.black);
            Assert.AreEqual(1, sh[1 * ProbeSolve.Floats]);
            Assert.AreEqual(2, sh[2 * ProbeSolve.Floats]);
            Assert.AreEqual(3, sh[3 * ProbeSolve.Floats]);
        }

        [Test]
        public void AGridEntirelyInsideTakesTheBareAmbient() {
            var sh = new float[2 * ProbeSolve.Floats];
            ProbeSolve.Fill(sh, new[] { false, false }, new Vector3Int(2, 1, 1), new Color(0.8f, 0.6f, 0.4f), new Color(0.2f, 0.2f, 0.2f));
            Assert.AreEqual(0.5f, sh[0], 1e-6f);
            Assert.AreEqual(0.3f, sh[1], 1e-6f);
            Assert.AreEqual(0.1f, sh[18 + 1], 1e-6f);
        }
    }
}
