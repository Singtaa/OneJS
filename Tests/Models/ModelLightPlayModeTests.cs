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

        string Bake() {
            Assert.That(Unwrap(_floor, Up), Is.Empty);
            Assert.That(Unwrap(_roof, Down), Is.Empty);
            var actors = new[] { _floorActor, FloorST.x, FloorST.y, FloorST.z, FloorST.w, _roofActor, RoofST.x, RoofST.y, RoofST.z, RoofST.w };
            return ModelBridge.BeginBake(actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight, 8, 32);
        }

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
            Assert.That(ModelBridge.BeginBake(actors, copies, Grid, AtlasWidth, AtlasHeight, 8, 16), Is.Empty);
            RunBake();
            Assert.That(ModelBridge.ScatterAtlas(scatter, 1), Is.EqualTo(RoofST));
            Assert.That(ModelBridge.ScatterDrawsBaked(scatter), Is.True, "its chunks draw with the rectangles");
            Assert.That(ModelBridge.ScatterAtlas(scatter, 0), Is.EqualTo(Vector4.zero), "a copy not named is not baked");
            Assert.That(ModelBridge.BeginBake(actors, new float[] { scatter, 7, 1, 1, 0, 0 }, Grid, AtlasWidth, AtlasHeight, 8, 16), Does.Contain("no copy 7"));
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
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight), Is.Empty);
            Assert.That(ModelBridge.Baking, Is.False, "a loaded file bakes nothing");
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(FloorST));
            Assert.That(AtlasOf(_crateActor), Is.EqualTo(Vector4.zero));
            Assert.That(RendererOf(_crateActor).lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided), "a moving actor takes its probe light from the file");
            Assert.That(ModelBridge.BlendProbes(new Vector3(0, 0.25f, 0)), Is.EqualTo(under), "the probes, exactly");

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
            Assert.That(ModelBridge.LoadLight(file, actors, Array.Empty<float>(), Grid, AtlasWidth * 2, AtlasHeight), Does.Contain("needs"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
            Assert.That(AtlasOf(_floorActor), Is.EqualTo(Vector4.zero));
            Assert.That(ModelBridge.LoadLight("not base64!", actors, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight), Does.Contain("base64"));
            Assert.That(ModelBridge.LoadLight(file, new float[] { 999, 1, 1, 0, 0 }, Array.Empty<float>(), Grid, AtlasWidth, AtlasHeight), Does.Contain("no actor 999"));
            Assert.That(ModelBridge.BakedLightmap, Is.Null);
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

        static Color Sample(Vector2 uv) {
            var shader = Shader.Find("Hidden/OneJS/Tests/LightmapSample");
            Assert.IsNotNull(shader, "the test's sampling shader");
            var mat = new Material(shader);
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try {
                mat.SetTexture("_Lightmap", ModelBridge.BakedLightmap);
                mat.SetVector("_At", uv);
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
