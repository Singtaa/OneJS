using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OneJS.Lighting;
using OneJS.Models;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The scene's baked light through ModelBridge (ModelBridge.Light.cs): a floor with a roof over
    /// part of it, both static, and a small moving actor under the roof. The unwraps are made by
    /// hand here, since xatlas is onejs-play's; the bake itself is LightBaker's, tested in
    /// Tests/Lighting. What these check is the wiring: the triangles out, the unwrap in, the
    /// atlas values and the probes onto the actors, and ModelLit drawing the lightmap.
    /// </summary>
    public class ModelLightPlayModeTests {
        const int AtlasWidth = 64, AtlasHeight = 32;
        static readonly Vector4 FloorST = new(0.45f, 0.9f, 0.025f, 0.05f);
        static readonly Vector4 RoofST = new(0.45f, 0.9f, 0.525f, 0.05f);
        static readonly float[] Grid = { -4, 0.25f, -4, 1, 9, 1, 9 };

        static readonly Vector3[] FloorCorners = { new(-4, 0, -4), new(4, 0, -4), new(4, 0, 4), new(-4, 0, 4) };
        static readonly Vector3[] RoofCorners = { new(-1.5f, 0.5f, -1.5f), new(1.5f, 0.5f, -1.5f), new(1.5f, 0.5f, 1.5f), new(-1.5f, 0.5f, 1.5f) };
        static readonly uint[] Up = { 0, 3, 2, 0, 2, 1 };
        static readonly uint[] Down = { 0, 2, 3, 0, 1, 2 };

        int _floor, _roof, _crate, _floorActor, _roofActor, _crateActor;

        [SetUp]
        public void MakeScene() {
            ModelBridge.BeginScene();
            ModelBridge.SetSun(false, 0, -1, 0, 1, 1, 1, 0, false);
            ModelBridge.SetAmbient(1, 1, 1, 0.1f, 0.1f, 0.1f, 1);
            ModelBridge.SetCamera(0, 0.3f, -3.5f, 0, 0, 0, 50);
            _floor = Mesh(FloorCorners, Up);
            _roof = Mesh(RoofCorners, Down);
            _crate = Mesh(new[] { new Vector3(-0.1f, 0, -0.1f), new Vector3(0.1f, 0, -0.1f), new Vector3(0.1f, 0, 0.1f), new Vector3(-0.1f, 0, 0.1f) }, Up);
            _floorActor = ModelBridge.Spawn(_floor, 0, 0, 0, 0, 1, true, true);
            _roofActor = ModelBridge.Spawn(_roof, 0, 0, 0, 0, 1, true, true);
            _crateActor = ModelBridge.Spawn(_crate, 0, 0.2f, 0, 0, 1, true, true);
        }

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            TextureRead.UseReadPixels = false;
            TextureRead.Hold = false;
            TextureRead.Fail = false;
        }

        /// <summary>A matte grey mesh model: ModelLit defaults to metal, which scatters no light to bake or to show.</summary>
        static int Mesh(Vector3[] corners, uint[] indices) {
            var model = ModelBridge.CreateMesh(false, false, false, 0.8f, 0.8f, 0.8f, 1);
            Assert.That(ModelBridge.SetMesh(model, corners, indices, null, null, null), Is.Empty);
            ModelBridge.MaterialOf(model).SetFloat("_Metallic", 0);
            return model;
        }

        /// <summary>A quad's unwrap: no vertex split, each corner at its place in the quad's square.</summary>
        static string Unwrap(int model, uint[] indices) =>
            ModelBridge.SetLightUnwrap(model, new uint[] { 0, 1, 2, 3 }, new float[] { 0, 0, 1, 0, 1, 1, 0, 1 }, indices);

        // The floor's rectangle on page 0, or on page 1, the sheet's second column: moved by where that column starts, so its uv on page 1 is FloorST's.
        static Vector4 FloorOn(int floorPage) => floorPage == 0 ? FloorST : FloorST + new Vector4(0, 0, (AtlasWidth - LightSheet.Seam) / (float)AtlasWidth, 0);

        float[] Actors(int floorPage = 0) => Actors(FloorOn(floorPage), RoofST);

        float[] Actors(Vector4 floor, Vector4 roof) => new[] { _floorActor, floor.x, floor.y, floor.z, floor.w, _roofActor, roof.x, roof.y, roof.z, roof.w };

        string Bake(int pages = 1, int floorPage = 0, int bounces = 2, float occlusion = 0, int samples = 32) {
            Assert.That(Unwrap(_floor, Up), Is.Empty);
            Assert.That(Unwrap(_roof, Down), Is.Empty);
            return ModelBridge.BeginBake(Actors(floorPage), Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, pages, 8, samples, bounces, occlusion);
        }

        string Bake(int pages, int width, int height, Vector4 floor, Vector4 roof, int samples = 32) {
            Assert.That(Unwrap(_floor, Up), Is.Empty);
            Assert.That(Unwrap(_roof, Down), Is.Empty);
            return ModelBridge.BeginBake(Actors(floor, roof), Array.Empty<float>(), Grid, width, height, pages, 8, samples, 2, 0);
        }

        // On a sheet of 4 pages of 64 by 32 (126 by 62 texels), the floor across both seams and the roof on page 0's corner.
        static readonly Vector4 FloorAcross = new(0.95f, 1.3f, 0.5f, 0.3f), RoofInTheCorner = new(0.45f, 0.25f, 0.025f, 0.025f);
        // The same texels on one page as big as the sheet.
        static Vector4 OnOnePage(Vector4 st) => Vector4.Scale(st, new Vector4(64f / 126, 32f / 62, 64f / 126, 32f / 62));

        // Steps the bake to its end, waiting between steps for what it reads back, as frames would.
        static void RunBake() {
            for (var i = 0; i < 100000; i++) {
                if (ModelBridge.StepBake(128) >= 1) return;
                AsyncGPUReadback.WaitAllRequests();
            }
            Assert.Fail("the bake never finished");
        }

        // The finished bake's light file, once its read lands.
        static string File() {
            for (var i = 0; i < 10; i++) {
                var file = ModelBridge.LightFile();
                if (file != "") return file;
                AsyncGPUReadback.WaitAllRequests();
            }
            Assert.Fail("the light file never read back");
            return null;
        }

        static Renderer RendererOf(int actor) => ModelBridge.ActorObject(actor).GetComponentInChildren<Renderer>();

        static Vector4 AtlasOf(int actor) {
            var block = new MaterialPropertyBlock();
            RendererOf(actor).GetPropertyBlock(block);
            return block.GetVector("_ActorLightmapST");
        }

        [Test]
        public void LightMesh_IsTheModelsTriangles() {
            var bytes = Convert.FromBase64String(ModelBridge.LightMesh(_floor));
            Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(4));
            Assert.That(BitConverter.ToUInt32(bytes, 4), Is.EqualTo(6));
            Assert.That(BitConverter.ToSingle(bytes, 8), Is.EqualTo(-4f));
            Assert.That(BitConverter.ToUInt32(bytes, 8 + 12 * 4 + 4), Is.EqualTo(3), "the second index of the first triangle");

            var depthOnly = Mesh(FloorCorners, Up);
            ModelBridge.SetDepthOnly(depthOnly);
            Assert.That(ModelBridge.LightMesh(depthOnly), Is.Empty, "a depth-only mesh is never seen, so it is not baked");
        }

        [Test]
        public void SetLightUnwrap_RebuildsTheMeshWithTheLayoutInTexcoord1() {
            Assert.That(ModelBridge.SetLightUnwrap(_floor, new uint[] { 0, 1, 2, 3, 0 }, new float[] { 0, 0, 1, 0, 1, 1, 0, 1, 0.5f, 0.5f }, new uint[] { 4, 3, 2, 0, 2, 1 }), Is.Empty);
            var mesh = ModelBridge.ActorObject(_floorActor).GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.Not.EqualTo(ModelBridge.MeshOf(_floor)), "the actor draws the rebuilt mesh");
            Assert.That(mesh.vertexCount, Is.EqualTo(5), "the split vertex is a vertex of its own");
            var uv1 = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(1, uv1);
            Assert.That(uv1[0], Is.EqualTo(new Vector2(0.5f, 0.5f)), "vertices come in the order the triangles first use them");
            Assert.That(mesh.vertices[0], Is.EqualTo(FloorCorners[0]), "a split vertex carries what it was split from");

            Assert.That(ModelBridge.SetLightUnwrap(_floor, new uint[] { 0, 1, 2 }, new float[] { 0, 0 }, Up), Does.Contain("uv numbers"));
            Assert.That(ModelBridge.SetLightUnwrap(_floor, new uint[] { 0, 1, 2, 9 }, new float[8], Up), Does.Contain("not in the part"));
            Assert.That(ModelBridge.SetLightUnwrap(_floor, new uint[] { 0, 1, 2, 3 }, new float[8], new uint[] { 0, 1, 2 }), Does.Contain("indices"));
        }

        [Test]
        public void Bake_LightsStaticActorsFromTheAtlasAndMovingOnesFromTheProbes() {
            Assert.That(Bake(), Is.Empty);
            Assert.That(ModelBridge.Baking, Is.True);
            RunBake();

            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorST));
            Assert.That(AtlasOf(_roofActor), Is.EqualTo(RoofST));
            Assert.That(AtlasOf(_crateActor), Is.EqualTo(Vector4.zero), "a moving actor has no rectangle");
            Assert.That(RendererOf(_crateActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided), "a moving actor takes its probe light");
            Assert.That(RendererOf(_floorActor).lightProbeUsage, Is.EqualTo(GraphicsSettings.currentRenderPipeline == null ? LightProbeUsage.CustomProvided : LightProbeUsage.BlendProbes));

            var under = ModelBridge.BlendProbes(new Vector3(0, 0.25f, 0));
            var open = ModelBridge.BlendProbes(new Vector3(3.5f, 0.25f, 3.5f));
            Assert.That(under[1, 0], Is.LessThan(open[1, 0] * 0.8f), $"under the roof the probe sees less sky: {under[1, 0]} against {open[1, 0]}");

            var shaded = Sample(Floor(0, 0));
            var lit = Sample(Floor(3.5f, 3.5f));
            Assert.That(shaded.g, Is.LessThan(lit.g * 0.6f), $"the floor under the roof bakes darker: {shaded} against {lit}");
            Assert.That(lit.g, Is.GreaterThan(0.3f), $"the open floor sees the sky: {lit}");
        }

        [UnityTest]
        public IEnumerator ModelLit_DrawsABakedActorFromTheLightmap() {
            var before = Render();
            Assert.That(Bake(), Is.Empty);
            RunBake();
            yield return null;
            var after = Render();
            Assert.That(before.g, Is.GreaterThan(0.05f), $"the floor is drawn at all: {before}");
            Assert.That(after.g, Is.LessThan(before.g * 0.7f), $"under the roof, drawn from the lightmap, the floor is darker than under the flat ambient: {after} against {before}");
        }

        [Test]
        public void Bake_OnTwoPages_LightsAnActorFromItsPageOnceThePageHasLight() {
            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            var pages = new List<int>();
            var floorWaited = false;
            for (var i = 0; i < 100000; i++) {
                // A tile a step, so each page takes steps of its own.
                var done = ModelBridge.StepBake(1) >= 1;
                if (pages.Count == 0 || pages[pages.Count - 1] != ModelBridge.BakePage()) pages.Add(ModelBridge.BakePage());
                // The roof's page has light and the floor's has not: the floor is still lit by the probes.
                if (AtlasOf(_roofActor) == RoofST && !floorWaited) {
                    // Anything that draws the floor afresh (a dissolve here) still draws it from the probes.
                    ModelBridge.SetDissolve(_floorActor, 0);
                    Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero), "the floor waits for its own page's light");
                }
                if (AtlasOf(_roofActor) == RoofST && AtlasOf(_floorActor) == Vector4.zero) {
                    floorWaited = true;
                    Assert.That(RendererOf(_floorActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided), "the floor takes the probes while its page bakes");
                }
                if (done) break;
                AsyncGPUReadback.WaitAllRequests();
            }
            Assert.That(pages, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(floorWaited, Is.True, "the floor waited for its page");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorOn(1)));
            Assert.That(AtlasOf(_roofActor), Is.EqualTo(RoofST));
            var shaded = Sample(Floor(0, 0), 1);
            var lit = Sample(Floor(3.5f, 3.5f), 1);
            Assert.That(shaded.g, Is.LessThan(lit.g * 0.6f), $"the floor on page 1 bakes darker under the roof on page 0: {shaded} against {lit}");
            Assert.That(Sample(Floor(3.5f, 3.5f), 0).a, Is.EqualTo(0f), "and page 0 holds nothing of it");
        }

        [Test]
        public void BeginBake_RefusesARectangleOnAPageTheAtlasHasNot() {
            Assert.That(Bake(pages: 1, floorPage: 1), Does.Contain("past its 1 pages"));
            Assert.That(ModelBridge.Baking, Is.False);
            Assert.That(Bake(pages: 0), Does.Contain("1, 2 or 4 pages"));
            Assert.That(Bake(pages: 3), Does.Contain("1, 2 or 4 pages"));
        }

        [UnityTest]
        public IEnumerator ModelLit_DrawsAnActorFromItsOwnPage() {
            Assert.That(Bake(), Is.Empty);
            RunBake();
            yield return null;
            var one = Render();
            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            RunBake();
            yield return null;
            var two = Render();
            // Read from page 0, where its rectangle holds nothing, the floor would be black.
            Assert.That(two.g, Is.EqualTo(one.g).Within(one.g * 0.15f), $"the floor on page 1 draws as it did on page 0: {two} against {one}");
        }

        [Test]
        public void Bake_OnFourPages_LightsAFloorAcrossTheSeamsOnceTheLastPageItReadsHasLight() {
            Assert.That(Bake(4, AtlasWidth, AtlasHeight, FloorAcross, RoofInTheCorner), Is.Empty);
            var pages = new List<int>();
            var roofFirst = false;
            var litOn = -1;
            for (var i = 0; i < 100000; i++) {
                var done = ModelBridge.StepBake(1) >= 1;
                if (pages.Count == 0 || pages[pages.Count - 1] != ModelBridge.BakePage()) pages.Add(ModelBridge.BakePage());
                if (AtlasOf(_roofActor) == RoofInTheCorner && AtlasOf(_floorActor) == Vector4.zero) {
                    roofFirst = true;
                    ModelBridge.SetDissolve(_floorActor, 0);
                    Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero), "the floor waits for every page it reads");
                }
                if (litOn < 0 && AtlasOf(_floorActor) != Vector4.zero) litOn = ModelBridge.BakePage();
                if (done) break;
                AsyncGPUReadback.WaitAllRequests();
            }
            Assert.That(pages, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(roofFirst, Is.True, "the roof, on page 0 alone, had light while the floor waited");
            Assert.That(litOn, Is.EqualTo(3), "the floor took its light with the last page's draft");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorAcross));
        }

        [UnityTest]
        public IEnumerator ModelLit_DrawsAFloorAcrossBothSeamsAsOnePageOfTheSameTexels() {
            Assert.That(Bake(1, 126, 62, OnOnePage(FloorAcross), OnOnePage(RoofInTheCorner), 128), Is.Empty);
            RunBake();
            yield return null;
            var one = RenderGrid();
            Assert.That(Bake(4, AtlasWidth, AtlasHeight, FloorAcross, RoofInTheCorner, 128), Is.Empty);
            RunBake();
            yield return null;
            var four = RenderGrid();
            var bright = 0f;
            var dark = float.MaxValue;
            for (var i = 0; i < one.Length; i++) {
                Assert.That(four[i].g, Is.EqualTo(one[i].g).Within(0.01f + one[i].g * 0.15f), $"pixel {i}: {four[i]} on four pages against {one[i]} on one");
                if (one[i].g > 0.01f) dark = Mathf.Min(dark, one[i].g);
                bright = Mathf.Max(bright, one[i].g);
            }
            // The floor is seen both under the roof and out from under it, so a page read wrong anywhere shows.
            Assert.That(dark, Is.LessThan(bright * 0.6f), $"the view holds light and shade: {dark} to {bright}");
        }

        [Test]
        public void LightFile_OfTwoPages_LoadsBackOntoBoth() {
            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            RunBake();
            var file = File();
            var shaded = Sample(Floor(0, 0), 1);
            var roof = Sample(new Vector2(RoofST.z + RoofST.x / 2, RoofST.w + RoofST.y / 2), 0);
            ModelBridge.ClearLight();
            Assert.That(ModelBridge.LoadLight(file, Actors(1), Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 1), Does.Contain("needs"));
            Assert.That(ModelBridge.LoadLight(file, Actors(1), Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 2), Is.Empty);
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorOn(1)));
            Assert.That(AtlasOf(_roofActor), Is.EqualTo(RoofST));
            var shadedAgain = Sample(Floor(0, 0), 1);
            var roofAgain = Sample(new Vector2(RoofST.z + RoofST.x / 2, RoofST.w + RoofST.y / 2), 0);
            Assert.That(shadedAgain.g, Is.EqualTo(shaded.g).Within(0.02f + shaded.g * 0.02f), $"the floor, page 1: {shadedAgain} against {shaded}");
            Assert.That(roofAgain.g, Is.EqualTo(roof.g).Within(0.02f + roof.g * 0.02f), $"the roof, page 0: {roofAgain} against {roof}");
        }

        [UnityTest]
        public IEnumerator Probes_BlendOnceAFrameHoweverManyCamerasRender() {
            Assert.That(Bake(), Is.Empty);
            RunBake();
            yield return null;
            // The frame's first camera blends the moved crate's probes; a second in the same frame does not.
            ModelBridge.Move(_crateActor, 0, 0.2f, 0.5f);
            Render();
            var under = ProbeOf(_crateActor);
            ModelBridge.Move(_crateActor, 3.5f, 0.2f, 3.5f);
            Render();
            Assert.That(ProbeOf(_crateActor), Is.EqualTo(under), "a second camera in the frame blends no probes again");
            yield return null;
            Render();
            var open = ProbeOf(_crateActor);
            Assert.That(open.w, Is.GreaterThan(under.w), $"the next frame blends the crate's probes where it now stands, in the open: {open} against {under}");
        }

        // The green band 0 and 1 terms ModelLit reads from an actor's property block.
        static Vector4 ProbeOf(int actor) {
            var block = new MaterialPropertyBlock();
            RendererOf(actor).GetPropertyBlock(block);
            return block.GetVector("unity_SHAg");
        }

        [Test]
        public void BakePhase_GoesFromTheProbesToTheLightmapAsTheyLandToFinishing_InStepsOfOneTile() {
            Assert.That(ModelBridge.BakePhase(), Is.EqualTo(0), "nothing baking");
            Assert.That(Bake(), Is.Empty);
            var seen = new List<int>();
            void Note() {
                var phase = ModelBridge.BakePhase();
                // 0 while the bake waits on its textures' means, before the baker exists.
                if (phase != 0 && (seen.Count == 0 || seen[seen.Count - 1] != phase)) seen.Add(phase);
            }
            for (var i = 0; i < 100000 && ModelBridge.StepBake(1) < 1; i++) {
                Note();
                AsyncGPUReadback.WaitAllRequests();
            }
            Note();
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            ModelBridge.ClearLight();
            Assert.That(ModelBridge.BakePhase(), Is.EqualTo(0));
        }

        [Test]
        public void ClearLight_GivesEveryActorTheAmbientBack() {
            Assert.That(Bake(), Is.Empty);
            RunBake();
            ModelBridge.ClearLight();
            var block = new MaterialPropertyBlock();
            RendererOf(_floorActor).GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.True);
            Assert.That(RendererOf(_crateActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.BlendProbes));
            Assert.That(ModelBridge.StepBake(1), Is.EqualTo(-1f));
        }

        // A rebake after a change of sun or ambient lays the light out exactly as before. Cleared at BeginBake, the
        // scene showed its flat ambient for the seconds a draft took, which a lesson reads as the bake breaking (review L1).
        [Test]
        public void Bake_AgainOnTheSameLayout_KeepsTheLightShowingUntilTheNewOneHasEveryPage() {
            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            RunBake();
            var old = ModelBridge.BakedLightmap;
            Assert.That(old, Is.Not.Null);
            var probe = ProbeOf(_crateActor);
            ModelBridge.SetAmbient(0.5f, 0.5f, 0.5f, 0.05f, 0.05f, 0.05f, 1);

            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            Assert.That(ModelBridge.BakedLightmap, Is.EqualTo(old), "the light showing stays");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorOn(1)), "and so does every rectangle");
            Assert.That(RendererOf(_crateActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided), "and the probes");
            Assert.That(ModelBridge.Baking, Is.True);
            var swapped = false;
            for (var i = 0; i < 100000 && !swapped; i++) {
                Assert.That(ModelBridge.StepBake(1), Is.GreaterThanOrEqualTo(0f));
                AsyncGPUReadback.WaitAllRequests();
                swapped = ModelBridge.BakedLightmap != old;
                Assert.That(ModelBridge.BakedLightmap, Is.Not.Null, $"step {i}: never no light between the two");
            }
            Assert.That(swapped, Is.True, "the new light shows");
            // One swap, once both pages have their draft: the floor on page 1 is lit at once, not black for page 0's draft.
            Assert.That(Sample(Floor(3.5f, 3.5f), 1).g, Is.GreaterThan(0.01f), "the floor's page has light the moment it shows");
            Assert.That(((RenderTexture)old).IsCreated(), Is.False, "the light it replaced is released");
            RunBake();
            Assert.That(ProbeOf(_crateActor), Is.Not.EqualTo(probe), "the new probes, under a darker ambient");
        }

        [Test]
        public void Bake_AgainOnAnotherLayout_ClearsTheLightFirst() {
            Assert.That(Bake(pages: 2, floorPage: 1), Is.Empty);
            RunBake();
            Assert.That(Bake(pages: 2, floorPage: 0), Is.Empty);
            Assert.That(ModelBridge.BakedLightmap, Is.Null, "a rectangle moved: the old light is another layout's");
            Assert.That(Bake(pages: 1), Is.Empty);
            RunBake();
            // Moving the roof leaves its rectangle, so this is the light's layout: what it changes is the light, which a rebake is for.
            ModelBridge.Place(_roofActor, 0, 0.1f, 0, 0);
            Assert.That(Bake(pages: 1), Is.Empty);
            Assert.That(ModelBridge.BakedLightmap, Is.Not.Null, "kept");
            ModelBridge.ClearLight();
            Assert.That(ModelBridge.BakedLightmap, Is.Null, "ClearLight drops a kept light too");
            Assert.That(ModelBridge.Baking, Is.False, "and stops the bake");
        }

        [Test]
        public void StepBake_WhenTheBakeFails_AnswersMinus2_AndBakeProblemSaysWhy() {
            Assert.That(Bake(), Is.Empty);
            Assert.That(ModelBridge.BakeProblem(), Is.Empty);
            // Stepped to the probes, then every read fails: the probes' read is the one a bake cannot do without.
            for (var i = 0; i < 1000 && ModelBridge.BakePhase() == 0; i++) {
                ModelBridge.StepBake(1);
                AsyncGPUReadback.WaitAllRequests();
            }
            TextureRead.Fail = true;
            var p = 0f;
            for (var i = 0; i < 100000 && p >= 0 && p < 1; i++) {
                p = ModelBridge.StepBake(1);
                AsyncGPUReadback.WaitAllRequests();
            }
            Assert.That(p, Is.EqualTo(-2f), "failed, not stopped");
            Assert.That(ModelBridge.BakeProblem(), Does.StartWith("the bake's probes could not be read"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null, "the light is cleared");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero));
            Assert.That(ModelBridge.StepBake(1), Is.EqualTo(-1f), "after which nothing is baking");
            Assert.That(ModelBridge.BakeProblem(), Is.Not.Empty, "and the reason stays until the next bake");
            TextureRead.Fail = false;
            Assert.That(Bake(), Is.Empty);
            Assert.That(ModelBridge.BakeProblem(), Is.Empty);
        }

        [Test]
        public void LoadLight_TakesAGridOfASpacingAnAxis_AndRefusesABadGrid() {
            Assert.That(ModelBridge.ProbeGridFloats(), Is.EqualTo(9));
            Assert.That(Bake(), Is.Empty);
            RunBake();
            var file = File();
            var actors = Actors();
            // The file's probes, a byte a coefficient, rather than the bake's own floats.
            var at = BlendOn(file, actors, new Vector3(0.5f, 0.25f, 0.5f));
            var nine = new float[] { Grid[0], Grid[1], Grid[2], Grid[3], Grid[3], Grid[3], Grid[4], Grid[5], Grid[6] };
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), nine, AtlasWidth, AtlasHeight, 1), Is.Empty, "nine floats: the same grid, a spacing an axis");
            Assert.That(ModelBridge.BlendProbes(new Vector3(0.5f, 0.25f, 0.5f)), Is.EqualTo(at));
            // The same probes stood twice as far apart in y and z only: the point blends from other probes.
            var stretched = new float[] { Grid[0], Grid[1], Grid[2], 1, 2, 2, Grid[4], Grid[5], Grid[6] };
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), stretched, AtlasWidth, AtlasHeight, 1), Is.Empty);
            // z = 3.5 is 3.75 spacings of 2 from the origin at -4, which on the test's grid of 1 is z = -0.25.
            Assert.That(ModelBridge.BlendProbes(new Vector3(0.5f, 0.25f, 3.5f)), Is.EqualTo(BlendOn(file, actors, new Vector3(0.5f, 0.25f, -0.25f))), "z at half the scale");

            var flat = (float[])Grid.Clone();
            flat[3] = 0;
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), flat, AtlasWidth, AtlasHeight, 1), Does.Contain("spacing above 0"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), new float[8], AtlasWidth, AtlasHeight, 1), Does.Contain("seven floats"));
            var backwards = (float[])Grid.Clone();
            backwards[4] = -1;
            Assert.That(ModelBridge.BeginBake(actors, Array.Empty<float>(), backwards, AtlasWidth, AtlasHeight, 1, 8, 32, 2, 0), Does.Contain("0 or more"));
            Assert.That(ModelBridge.Baking, Is.False);
        }

        // The probes at `at` once `file` is loaded on the test's own grid.
        static SphericalHarmonicsL2 BlendOn(string file, float[] actors, Vector3 at) {
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 1), Is.Empty);
            return ModelBridge.BlendProbes(at);
        }

        [Test]
        public void SetMesh_AfterAnUnwrap_DrawsTheModelUnbakedAgain() {
            Assert.That(Bake(), Is.Empty);
            RunBake();
            Assert.That(ModelBridge.SetMesh(_floor, FloorCorners, Up, null, null, null), Is.Empty);
            Assert.That(ModelBridge.ActorObject(_floorActor).GetComponentInChildren<MeshFilter>().sharedMesh, Is.EqualTo(ModelBridge.MeshOf(_floor)));
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero), "its rectangle was laid out for the old triangles");
        }

        [Test]
        public void Bake_GivesScatteredCopiesTheirRectangles() {
            var scatter = ModelBridge.Scatter(_roof, new float[] { 0, 0, 0, 0, 1, 2, 0, 0, 0, 1 });
            Assert.That(Unwrap(_floor, Up), Is.Empty);
            Assert.That(Unwrap(_roof, Down), Is.Empty);
            var actors = new[] { _floorActor, FloorST.x, FloorST.y, FloorST.z, FloorST.w };
            var copies = new[] { scatter, 1, RoofST.x, RoofST.y, RoofST.z, RoofST.w };
            Assert.That(ModelBridge.BeginBake(actors, copies, Grid, AtlasWidth, AtlasHeight, 1, 8, 16, 2, 0), Is.Empty);
            RunBake();
            Assert.That(ModelBridge.ScatterAtlas(scatter, 1), Is.EqualTo(RoofST));
            Assert.That(ModelBridge.ScatterDrawsBaked(scatter), Is.True, "its chunks draw with the rectangles");
            Assert.That(ModelBridge.ScatterAtlas(scatter, 0), Is.EqualTo(Vector4.zero), "a copy not named is not baked");
            Assert.That(ModelBridge.BeginBake(actors, new float[] { scatter, 7, 1, 1, 0, 0 }, Grid, AtlasWidth, AtlasHeight, 1, 8, 16, 2, 0), Does.Contain("no copy 7"));
        }

        // Read back async, as both browser backends do, and by ReadPixels, as a GPU without async readback does.
        [TestCase(false)]
        [TestCase(true)]
        public void LightFile_LoadsBackAsTheLightItWasBakedAs(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            Assert.That(ModelBridge.LightFile(), Is.Empty, "nothing has baked yet");
            Assert.That(Bake(), Is.Empty);
            Assert.That(ModelBridge.LightFile(), Is.Empty, "the bake has not finished");
            RunBake();
            TextureRead.Hold = true;
            if (TextureRead.Async) {
                Assert.That(ModelBridge.LightFile(), Is.Empty, "the lightmap's read has not landed");
                Assert.That(ModelBridge.LightFile(), Is.Empty, "nor has it a frame later");
            } else {
                Assert.That(ModelBridge.LightFile(), Is.Not.Empty, "ReadPixels lands when it is made");
            }
            TextureRead.Hold = false;
            var file = File();
            Assert.That(ModelBridge.LightFile(), Is.EqualTo(file), "the file is read once");
            var shaded = Sample(Floor(0, 0));
            var lit = Sample(Floor(3.5f, 3.5f));
            var under = ModelBridge.BlendProbes(new Vector3(0, 0.25f, 0));

            ModelBridge.ClearLight();
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
            var actors = new[] { _floorActor, FloorST.x, FloorST.y, FloorST.z, FloorST.w, _roofActor, RoofST.x, RoofST.y, RoofST.z, RoofST.w };
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 1), Is.Empty);
            Assert.That(ModelBridge.Baking, Is.False, "a loaded file bakes nothing");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorST));
            Assert.That(AtlasOf(_crateActor), Is.EqualTo(Vector4.zero));
            Assert.That(RendererOf(_crateActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided), "a moving actor takes its probe light from the file");
            // The probes to a byte against each probe's largest term (LightFile), and so the blend to well under 1%.
            var again = ModelBridge.BlendProbes(new Vector3(0, 0.25f, 0));
            for (var c = 0; c < 3; c++) Assert.That(again[c, 0], Is.EqualTo(under[c, 0]).Within(Mathf.Abs(under[c, 0]) * 0.01f), $"the probes' band 0, channel {c}");

            var shadedAgain = Sample(Floor(0, 0));
            var litAgain = Sample(Floor(3.5f, 3.5f));
            Assert.That(shadedAgain.g, Is.EqualTo(shaded.g).Within(0.02f + shaded.g * 0.02f), $"the shaded floor: {shadedAgain} against {shaded}");
            Assert.That(litAgain.g, Is.EqualTo(lit.g).Within(0.02f + lit.g * 0.02f), $"the open floor: {litAgain} against {lit}");
        }

        // A light file is asked for each frame until it reads back, so a read that keeps failing would warn each frame.
        [TestCase(false)]
        [TestCase(true)]
        public void LightFile_WarnsOnceABakeWhenItsReadFails(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            for (var bake = 0; bake < 2; bake++) {
                Assert.That(Bake(), Is.Empty);
                RunBake();
                TextureRead.Fail = true;
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("lightmap could not be read back"));
                for (var call = 0; call < 3; call++) Assert.That(ModelBridge.LightFile(), Is.Empty, $"bake {bake}, call {call}");
                LogAssert.NoUnexpectedReceived();
                TextureRead.Fail = false;
                Assert.That(File(), Is.Not.Empty, "a read that works after one that failed gives the file");
            }
        }

        [Test]
        public void LoadLight_RefusesAFileThatDoesNotFitAndLightsNothing() {
            Assert.That(Bake(), Is.Empty);
            RunBake();
            var file = File();
            var actors = new[] { _floorActor, FloorST.x, FloorST.y, FloorST.z, FloorST.w };
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), Grid, AtlasWidth * 2, AtlasHeight, 1), Does.Contain("needs"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero));
            Assert.That(ModelBridge.LoadLight("not base64!", actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 1), Does.Contain("base64"));
            Assert.That(ModelBridge.LoadLight(file, new float[] { 999, 1, 1, 0, 0 }, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 1), Does.Contain("no actor 999"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
        }

        // Under the roof, the floor's light is the sky past its edges and what the roof and floor bounce between them.
        [Test]
        public void Bake_BouncesAsManyTimesAsAsked() {
            Color Lit(int bounces) {
                try {
                    Assert.That(Bake(bounces: bounces), Is.Empty);
                    RunBake();
                    return Sample(Floor(0, 0));
                } finally {
                    ModelBridge.ClearLight();
                }
            }
            var none = Lit(0);
            var two = Lit(2);
            var four = Lit(4);
            Assert.That(none.g, Is.LessThan(two.g * 0.9f), $"bounces 0 leaves the floor only the sky past the roof: {none} against {two}");
            Assert.That(two.g, Is.LessThan(four.g), $"each bounce adds light: {two} against {four}");
        }

        // The roof is half a metre over the floor's middle: inside occlusion's reach. The floor's corner is not.
        [Test]
        public void Bake_DarkensUnderTheRoofWithOcclusion_AndNotTheOpenFloor() {
            Color[] Lit(float occlusion) {
                try {
                    Assert.That(Bake(occlusion: occlusion), Is.Empty);
                    RunBake();
                    return new[] { Sample(Floor(0, 0)), Sample(Floor(3.5f, 3.5f)) };
                } finally {
                    ModelBridge.ClearLight();
                }
            }
            var none = Lit(0);
            var full = Lit(1);
            Assert.That(full[0].g, Is.LessThan(none[0].g * 0.8f), $"under the roof: {full[0]} against {none[0]}");
            Assert.That(full[1].g, Is.EqualTo(none[1].g).Within(1e-4f), $"the open corner: {full[1]} against {none[1]}");
        }

        [Test]
        public void BeginBake_RefusesBouncesSamplesAndOcclusionPastTheirRange() {
            Assert.That(Bake(bounces: LightBaker.MaxBounces + 1), Does.Contain("bounces are 0 to 8, not 9"));
            Assert.That(Bake(bounces: -1), Does.Contain("bounces are 0 to 8, not -1"));
            Assert.That(Bake(samples: LightBaker.MaxSamples + 1), Does.Contain("samples are 1 to 4096, not 4097"));
            Assert.That(Bake(samples: 0), Does.Contain("samples and rays must be positive"));
            Assert.That(Bake(occlusion: 1.5f), Does.Contain("occlusion is 0 to 1, not 1.5"));
            Assert.That(Bake(occlusion: float.NaN), Does.Contain("occlusion is 0 to 1"));
            Assert.That(ModelBridge.BakePhase(), Is.EqualTo(0));
            Assert.That(Bake(bounces: 8, occlusion: 1, samples: 4096), Is.Empty);
        }

        // The roof's underside bounces the floor's light back down by its base map's mean colour, read back as each backend does.
        [TestCase(false)]
        [TestCase(true)]
        public void Bake_BouncesLightByATexturedSurfacesMeanColour(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            Color ShadedUnder(Color map) {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                var pixels = new Color[16];
                for (var i = 0; i < 16; i++) pixels[i] = map;
                tex.SetPixels(pixels);
                tex.Apply();
                try {
                    ModelBridge.MaterialOf(_roof).SetTexture("_BaseMap", tex);
                    Assert.That(Bake(), Is.Empty);
                    RunBake();
                    return Sample(Floor(0, 0));
                } finally {
                    ModelBridge.ClearLight();
                    ModelBridge.MaterialOf(_roof).SetTexture("_BaseMap", null);
                    Object.DestroyImmediate(tex);
                }
            }
            var white = ShadedUnder(Color.white);
            var black = ShadedUnder(Color.black);
            Assert.That(black.g, Is.LessThan(white.g * 0.9f), $"a black roof bounces less onto the floor under it: {black} against {white}");
        }

        [Test]
        public void StepBake_WaitsForATexturesMeanBeforeItBakes() {
            if (!TextureRead.Async) Assert.Ignore("this GPU has no async readback, so a mean is read at once");
            ModelBridge.MaterialOf(_roof).SetTexture("_BaseMap", Texture2D.whiteTexture);
            TextureRead.Hold = true;
            Assert.That(Bake(), Is.Empty);
            Assert.That(ModelBridge.Baking, Is.True);
            Assert.That(ModelBridge.StepBake(128), Is.EqualTo(0f), "no baker before the mean");
            Assert.That(ModelBridge.StepBake(128), Is.EqualTo(0f));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
            TextureRead.Hold = false;
            RunBake();
            Assert.That(ModelBridge.BakedLightmap, Is.Not.Null);
            Assert.That(ModelBridge.Baking, Is.False);
        }

        [Test]
        public void Sha256_IsTheStandardDigestOfBytesFromJSAndOfAFileOnDisk() {
            const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            Assert.That(ModelBridge.Sha256(new byte[] { 97, 98, 99 }), Is.EqualTo(Abc));
            Assert.That(ModelBridge.Sha256(Array.Empty<byte>()), Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));

            // The crossing onejs-play makes: a Uint8Array arrives as its bytes, and a view as only its own.
            using (var ctx = new QuickJSContext()) {
                Assert.That(ctx.Eval("CS.OneJS.Models.ModelBridge.Sha256(new Uint8Array([97, 98, 99]))"), Is.EqualTo(Abc));
                Assert.That(ctx.Eval("CS.OneJS.Models.ModelBridge.Sha256(new Uint8Array([0, 97, 98, 99, 0]).subarray(1, 4))"), Is.EqualTo(Abc));
            }
            QuickJSNative.ClearAllHandles();

            var path = System.IO.Path.GetTempFileName();
            try {
                System.IO.File.WriteAllBytes(path, new byte[] { 97, 98, 99 });
                Assert.That(ModelBridge.FileSha256(path), Is.EqualTo(Abc));
            } finally {
                System.IO.File.Delete(path);
            }
            Assert.Throws<System.IO.FileNotFoundException>(() => ModelBridge.FileSha256(path));
        }

        static Vector2 Floor(float x, float z) {
            var u = (x + 4) / 8;
            var v = (z + 4) / 8;
            return new Vector2(u * FloorST.x + FloorST.z, v * FloorST.y + FloorST.w);
        }

        static Color Sample(Vector2 uv, int page = 0) {
            var shader = Shader.Find("Hidden/OneJS/Tests/LightmapSample");
            Assert.IsNotNull(shader, "the test's sampling shader");
            var mat = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try {
                mat.SetTexture("_Lightmap", ModelBridge.BakedLightmap);
                mat.SetVector("_At", uv);
                mat.SetFloat("_Slice", page);
                var cmd = new CommandBuffer();
                cmd.SetRenderTarget(target);
                cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                return Read(target, 0, 0);
            } finally {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mat);
            }
        }

        /// <summary>The middle of what the scene's camera sees: the floor under the roof's edge, looking in from the side.</summary>
        // The view at 7 by 7 points over it.
        static Color[] RenderGrid() {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try {
                camera.targetTexture = target;
                camera.Render();
                var colors = new List<Color>();
                for (var y = 4; y < 64; y += 9) for (var x = 4; x < 64; x += 9) colors.Add(Read(target, x, y));
                return colors.ToArray();
            } finally {
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static Color Render() {
            var camera = ModelBridge.SceneCamera;
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try {
                camera.targetTexture = target;
                camera.Render();
                return Read(target, 32, 32);
            } finally {
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static Color Read(RenderTexture rt, int x, int y) {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = previous;
            var c = tex.GetPixel(x, y);
            Object.DestroyImmediate(tex);
            return c;
        }
    }
}
