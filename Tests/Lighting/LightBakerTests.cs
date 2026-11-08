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

            public BakeScene Build(int atlas = 64, int pages = 1, int height = 0) => new BakeScene {
                Positions = Positions.ToArray(), Normals = Normals.ToArray(), Uvs = Uvs.ToArray(), Indices = Indices.ToArray(), Albedo = Albedo.ToArray(),
                AtlasWidth = atlas, AtlasHeight = height > 0 ? height : atlas, Pages = pages, Sky = Sky, Ground = Ground,
            };
        }

        // A 4 m floor in the left half of the atlas, its z along v; a 3 m wall standing at z = 0 in the right half.
        static readonly Rect FloorRect = new Rect(0.05f, 0.05f, 0.4f, 0.9f), WallRect = new Rect(0.55f, 0.05f, 0.4f, 0.9f);
        static Vector2 FloorUv(float x, float z) => new Vector2(FloorRect.xMin + x / 4 * FloorRect.width, FloorRect.yMin + z / 4 * FloorRect.height);
        static Vector2 WallUv(float x, float y) => new Vector2(WallRect.xMin + x / 4 * WallRect.width, WallRect.yMin + y / 3 * WallRect.height);

        static SceneBuilder Floor(Color albedo, Rect? at = null) => new SceneBuilder().Quad(Vector3.zero, new Vector3(4, 0, 0), new Vector3(0, 0, 4), Vector3.up, at ?? FloorRect, albedo);
        // The wall, on the second column of a sheet of 64 texel pages when `secondColumn`: moved by where that column starts, so its uv on page 1 is WallUv.
        static SceneBuilder FloorAndWall(Color floor, Color wall, bool secondColumn = false, Rect? floorAt = null, Rect? wallAt = null) {
            var rect = wallAt ?? WallRect;
            if (secondColumn) rect.x += (64f - LightSheet.Seam) / 64f;
            return Floor(floor, floorAt).Quad(Vector3.zero, new Vector3(4, 0, 0), new Vector3(0, 3, 0), Vector3.forward, rect, wall);
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

        /// <summary>What ModelLit reads from the lightmap at `uv` on the sheet, the page and its uv picked as ModelLit picks them.</summary>
        static Color SampleSheet(RenderTexture lightmap, LightSheet sheet, Vector2 uv) {
            var shader = Shader.Find("Hidden/OneJS/Tests/LightmapSample");
            var mat = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try {
                mat.SetTexture("_Lightmap", lightmap);
                mat.SetVector("_At", uv);
                mat.SetFloat("_Slice", -1);
                mat.SetVector("_OjLightmapSeams", sheet.Seams);
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
            scene.GridSpacing = Vector3.one;
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
            scene.GridSpacing = Vector3.one * 2;
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
        public void OcclusionDarkensTheFloorWithinAMetreOfTheWall_TheMoreTheNearer_AndNothingPastIt() {
            Color[] At(float occlusion) {
                var settings = Quick();
                settings.Occlusion = occlusion;
                using var baker = Bake(FloorAndWall(Color.gray, Color.gray).Build(), settings);
                return new[] { Sample(baker.Lightmap, FloorUv(2, 0.2f)), Sample(baker.Lightmap, FloorUv(2, 0.6f)), Sample(baker.Lightmap, FloorUv(2, 2.5f)) };
            }
            var none = At(0);
            var half = At(0.5f);
            var full = At(1);
            Assert.Less(full[0].g, none[0].g * 0.85f, $"at the wall's foot, full occlusion darkens: {full[0]} against {none[0]}");
            Assert.Less(full[0].g, half[0].g, $"and more than half does: {full[0]} against {half[0]}");
            Assert.Less(half[0].g, none[0].g, $"and half some: {half[0]} against {none[0]}");
            Assert.Greater(full[1].g / none[1].g, full[0].g / none[0].g, $"less 0.6 m from the wall than 0.2 m: {full[1]}, {full[0]}");
            Near(none[2], full[2], 1e-4f, "2.5 m from the wall, past the reach, untouched", 1e-5f);
        }

        // A black roof 3 m square, half a metre over the floor, and no bounces: a path that meets the roof brings
        // nothing, one that misses it the sky. The floor's middle is 1.5 m from the roof's edges, past the 0.87 m
        // at which a path's first hit leaves the reach (0.5 m up over cos 60°), so every path within the reach
        // meets the roof at 0.5 / cos θ, and the cosine-weighted mean nearness, the integral of
        // (1 - 0.5 / c) 2c dc from 0.5 to 1, is 0.25. A pass darkens its paths' mean light by their mean
        // nearness, and a near path is already black, so of a pass's N by N pairs only the N (N - 1) of two
        // paths count: occlusion o keeps 1 - o 0.25 (N - 1) / N of the light, with N its paths a pass.
        [Test]
        public void Occlusion_KeepsOneLessTheMeanNearnessOfThePaths() {
            var settings = Quick(256);
            float n = settings.SamplesPerPass;
            Color Middle(float occlusion) {
                var scene = Floor(Color.white).Quad(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(3, 0, 0), new Vector3(0, 0, 3), Vector3.down, WallRect, Color.black).Build();
                scene.Sky = scene.Ground = Color.white;
                settings.Bounces = 0;
                settings.Occlusion = occlusion;
                using var baker = Bake(scene, settings);
                return Sample(baker.Lightmap, FloorUv(2, 2));
            }
            var none = Middle(0);
            Assert.Greater(none.g, 0.02f, $"the sky reaches under the roof's edges: {none}");
            Near(none * (1 - 0.25f * (n - 1) / n), Middle(1), 0.03f, "full occlusion");
            Near(none * (1 - 0.5f * 0.25f * (n - 1) / n), Middle(0.5f), 0.02f, "half");
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
            using var two = new LightBaker(FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f), secondColumn: true).Build(pages: 2), settings);
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

        // A 4 m floor from 0.3 to 1.6 pages each way, so both seams of a sheet of 4 pages of 64 texels, at 63 texels,
        // run through it at 2.1 m; its wall on the left, across the row seam; and a 1 m quad up out of the way whose
        // left edge is 3.5 texels past the column seam, below the floor's rows, so the texels both pages hold there
        // take their light from it by dilation alone.
        static readonly Rect FloorAcross = new Rect(0.3f, 0.3f, 1.3f, 1.3f), WallLeft = new Rect(0.02f, 0.3f, 0.25f, 1.3f),
            PastTheSeam = new Rect(66.5f / 64, 0.05f, 0.3f, 0.2f);

        static SceneBuilder AcrossTheSeams(Func<Rect, Rect> place) => FloorAndWall(Color.gray, new Color(0.8f, 0.1f, 0.1f), floorAt: place(FloorAcross), wallAt: place(WallLeft))
            .Quad(new Vector3(6, 3, 6), new Vector3(1, 0, 0), new Vector3(0, 0, 1), Vector3.up, place(PastTheSeam), Color.gray);

        [Test]
        public void AFloorAcrossTheSeams_IsTheSameOnEveryPageThatHoldsIt_AndAsOnePageOfTheSameTexels() {
            // Few samples, so each texel's own noise is large and two pages that traced it differently would show it.
            var settings = Quick(16);
            var sheet = new LightSheet(64, 64, 4);
            using var four = Bake(AcrossTheSeams(r => r).Build(64, 4), settings);
            Assert.AreEqual(4, four.Lightmap.volumeDepth);
            Assert.AreEqual(64, four.Lightmap.width);
            // The same scene on one page as big as the sheet: the same texels, traced with one page's seeds.
            var side = sheet.Width;
            using var one = Bake(AcrossTheSeams(r => new Rect(r.x * 64 / side, r.y * 64 / side, r.width * 64 / side, r.height * 64 / side)).Build(side), settings);

            // Every texel two pages hold, read raw from each: alike, light and coverage, gutters too, to within two
            // steps of a half float, as each page lays the G-buffer through its own window and a last bit can round
            // apart. Traced differently, a texel at 16 samples is several percent apart.
            var pages = new Color[4][];
            for (var p = 0; p < 4; p++) pages[p] = Read(four.Lightmap, p);
            var shared = 0;
            for (var p = 0; p < 4; p++) for (var q = p + 1; q < 4; q++) {
                var a = sheet.Origin(p);
                var b = sheet.Origin(q);
                for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) {
                    // Page p's texel (x, y) is the sheet's a + (x, y); page q holds it at a + (x, y) - b, if it holds it.
                    int qx = a.x + x - b.x, qy = a.y + y - b.y;
                    if (qx < 0 || qy < 0 || qx >= 64 || qy >= 64) continue;
                    Color c = pages[p][y * 64 + x], d = pages[q][qy * 64 + qx];
                    if (c.a > 0) shared++;
                    for (var k = 0; k < 4; k++) Assert.AreEqual(c[k], d[k], Mathf.Max(1e-4f, Mathf.Abs(c[k]) * 2e-3f), $"the sheet's texel {a.x + x}, {a.y + y} on page {p}, {c}, and on page {q}, {d}");
                }
            }
            Assert.Greater(shared, 2 * 2 * 80, "the floor's texels both sides of each seam");

            // Read as ModelLit reads it, the floor is lit as on one page of the same texels, but for noise: near the
            // wall on pages 0 and 1, far from it on pages 2 and 3, so a page read for another shows.
            Vector2 OnFloor(float x, float z) => new Vector2(FloorAcross.xMin + x / 4 * FloorAcross.width, FloorAcross.yMin + z / 4 * FloorAcross.height);
            var seen = new HashSet<int>();
            foreach (var x in new[] { 0.3f, 1.2f, 2.0f, 2.2f, 3.0f, 3.7f }) {
                foreach (var z in new[] { 0.15f, 0.4f, 2.0f, 2.2f, 3.6f }) {
                    var at = OnFloor(x, z);
                    seen.Add(sheet.PageAt(at));
                    Near(Sample(one.Lightmap, at * 64 / side), SampleSheet(four.Lightmap, sheet, at), 0.12f, $"the floor at {x}, {z}");
                }
            }
            Assert.AreEqual(4, seen.Count, "every page read");
            // Either side of each split, a quarter texel off, as ModelLit moves from one page to the next.
            var texel = 1f / 64;
            for (var z = 0.1f; z < 4; z += 0.3f) {
                var v = OnFloor(0, z).y;
                Near(SampleSheet(four.Lightmap, sheet, new Vector2(sheet.Seams.x - texel / 4, v)), SampleSheet(four.Lightmap, sheet, new Vector2(sheet.Seams.x + texel / 4, v)), 0.03f, $"either side of the column split, z {z}");
            }
            // The wall's red, on pages 0 and 2, reaches the floor's foot on page 1.
            var foot = SampleSheet(four.Lightmap, sheet, OnFloor(3.5f, 0.2f));
            Assert.AreEqual(1, sheet.PageAt(OnFloor(3.5f, 0.2f)));
            Assert.Greater(foot.r, foot.g * 1.05f, $"the wall's red on the floor's foot past the seam ({foot})");
        }

        static LightBaker ProbedBaker() {
            var scene = FloorAndWall(Color.gray, Color.gray).Build();
            scene.GridOrigin = new Vector3(0.5f, 1, 0.5f);
            scene.GridCounts = new Vector3Int(3, 2, 3);
            return new LightBaker(scene, new BakeSettings { Samples = 4, DraftSamples = 4, ProbeRays = 64 });
        }

        // A budget of paths learned on probe rays, a frame's worth of them, is spent on lightmap tiles in the frame
        // the probes end in, and at 4 bounces that was a 117 ms frame (review L2): the step ends with the probes instead.
        [Test]
        public void TheStepThatFinishesTheProbesEndsThere() {
            using var baker = ProbedBaker();
            Assert.AreEqual(BakePhase.Probes, baker.Phase);
            Assert.IsFalse(baker.Step(long.MaxValue));
            Assert.AreNotEqual(BakePhase.Probes, baker.Phase, "every probe ray traced");
            Assert.AreEqual(0, baker.Samples, "and no lightmap tile");
            Assert.IsFalse(baker.DraftReady);
            Assert.IsNull(baker.Lightmap);
            baker.Step(1);
            Assert.Greater(baker.Progress, 0.1f, "the next step starts the lightmap: the probes are the first tenth");
        }

        [Test]
        public void ABakeIsDoneOnlyOnceItsProbesAreReadBack() {
            if (!TextureRead.Async) Assert.Ignore("this GPU has no async readback, so its probes are read at once");
            using var baker = ProbedBaker();
            TextureRead.Hold = true;
            try {
                // One step traces the probes, the next makes every other draw, and the probes' read is still on its way.
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
                Assert.IsFalse(baker.Step(long.MaxValue), "the step that traces the probes ends with them");
                Assert.IsTrue(baker.ProbesReady, "ReadPixels lands when it is made");
                Assert.IsTrue(baker.Step(long.MaxValue));
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
            // The wall on a second column of an atlas of one page.
            Assert.Throws<ArgumentException>(() => new LightBaker(FloorAndWall(Color.gray, Color.gray, secondColumn: true).Build()));
            // A sheet is 1, 2 or 4 pages.
            Assert.Throws<ArgumentException>(() => new LightBaker(FloorAndWall(Color.gray, Color.gray, secondColumn: true).Build(pages: 3)));
        }
    }

    public class LightSheetTests {
        [Test]
        public void OnePage_IsTheAtlasAsBeforePages() {
            var one = new LightSheet(100, 50, 1);
            Assert.AreEqual((1, 1, 100, 50), (one.Columns, one.Rows, one.Width, one.Height));
            Assert.AreEqual(Vector2.one, one.Extent);
            Assert.AreEqual(0, one.PageAt(Vector2.one));
            Assert.AreEqual(0, one.LastPage(new Vector4(1, 1, 0, 0)));
            Assert.Greater(one.Seams.x, 1000f, "no uv on one page reaches a second column");
            Assert.Greater(one.Seams.y, 1000f);
        }

        [Test]
        public void FourPages_ShareTwoTexelsAtEachSeam_AndASampleEitherSideOfTheSplitIsWhollyOnOnePage() {
            var four = new LightSheet(64, 32, 4);
            Assert.AreEqual((2, 2, 126, 62), (four.Columns, four.Rows, four.Width, four.Height));
            Assert.AreEqual(new[] { new Vector2Int(0, 0), new Vector2Int(62, 0), new Vector2Int(0, 30), new Vector2Int(62, 30) },
                new[] { four.Origin(0), four.Origin(1), four.Origin(2), four.Origin(3) });
            Assert.AreEqual(new Vector4(63f / 64, 31f / 32, 62f / 64, 30f / 32), four.Seams);
            // A bilinear read reads the two texels whose centres are round it: at the split, in sheet texels
            // 63, those are 62 and 63, the two both pages hold (page 0 to 63, page 1 from 62).
            var split = four.Seams.x * 64;
            Assert.AreEqual(62, Mathf.FloorToInt(split - 0.5f));
            Assert.AreEqual(four.Origin(1).x, Mathf.FloorToInt(split - 0.5f), "the first texel read at the split is page 1's first");
            Assert.AreEqual(63, Mathf.FloorToInt(split - 0.5f) + 1, "and the second is page 0's last");
            Assert.AreEqual(0, four.PageAt(new Vector2(0.98f, 0.5f)));
            Assert.AreEqual(1, four.PageAt(new Vector2(0.99f, 0.5f)));
            Assert.AreEqual(2, four.PageAt(new Vector2(0.5f, 0.97f)));
            Assert.AreEqual(3, four.PageAt(new Vector2(1.5f, 1.5f)));
            // A rectangle reads last from the page of its far corner.
            Assert.AreEqual(0, four.LastPage(new Vector4(0.5f, 0.5f, 0.1f, 0.1f)));
            Assert.AreEqual(1, four.LastPage(new Vector4(1f, 0.5f, 0.1f, 0.1f)));
            Assert.AreEqual(3, four.LastPage(new Vector4(1f, 1f, 0.5f, 0.5f)));
            Assert.IsTrue(four.Holds(new Vector4(1, 1, 0.9f, 0.9f)));
            Assert.IsFalse(four.Holds(new Vector4(1, 1, 1f, 0.5f)), "past the sheet's 126 / 64");
        }

        [Test]
        public void TwoPages_SitSideBySide() {
            var two = new LightSheet(64, 64, 2);
            Assert.AreEqual((2, 1, 126, 64), (two.Columns, two.Rows, two.Width, two.Height));
            Assert.AreEqual(0, two.PageAt(new Vector2(0.5f, 0.999f)), "no second row");
        }

        [Test]
        public void IsOne_Two_OrFourPages() {
            Assert.Throws<ArgumentException>(() => new LightSheet(64, 64, 3));
            Assert.Throws<ArgumentException>(() => new LightSheet(64, 64, 0));
            Assert.Throws<ArgumentException>(() => new LightSheet(2, 64, 2), "a page no wider than the seam");
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
