using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// Models made from arrays (ModelBridge.CreateMesh and SetMesh): a terrain built once, and a
    /// shadow redrawn every frame at a varying size, which must stop allocating once its buffers
    /// have room.
    /// </summary>
    public class MeshModelEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A square in the XY plane facing the camera at -z: (0, 1, 2) and (0, 2, 3) wind clockwise from there.
        static readonly Vector3[] Square = { new(-1, -1, 0), new(-1, 1, 0), new(1, 1, 0), new(1, -1, 0) };
        static readonly uint[] TwoTriangles = { 0, 1, 2, 0, 2, 3 };

        static int Made(Vector3[] positions, uint[] indices, Vector3[] normals = null, float[] colors = null) {
            int model = ModelBridge.CreateMesh(normals != null, colors != null, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, positions, indices, normals, colors, null), Is.Empty);
            return model;
        }

        [Test]
        public void Normals_AreComputedWhenLeftOut_AndKeptWhenGiven() {
            var mesh = ModelBridge.MeshOf(Made(Square, TwoTriangles));
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.GetIndices(0), Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
            foreach (var n in mesh.normals) Assert.That(Vector3.Distance(n, Vector3.back), Is.LessThan(1e-4f), "faces the camera at -z");

            var given = Enumerable.Repeat(Vector3.up, 4).ToArray();
            var kept = ModelBridge.MeshOf(Made(Square, TwoTriangles, given));
            Assert.That(kept.normals, Is.EqualTo(given));
        }

        [Test]
        public void Colors_AreSrgbMadeLinearInALinearProject_AndThreeOrFourAVertex() {
            var grey = new Color(0.5f, 0.5f, 0.5f, 1);
            var want = QualitySettings.activeColorSpace == ColorSpace.Linear ? grey.linear : grey;
            var rgb = ModelBridge.MeshOf(Made(Square, TwoTriangles, null, Enumerable.Repeat(0.5f, 12).ToArray()));
            foreach (var c in rgb.colors) AssertColor(c, want);
            var rgba = ModelBridge.MeshOf(Made(Square, TwoTriangles, null, Enumerable.Repeat(0.5f, 16).ToArray()));
            Assert.That(rgba.colors[0].a, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void PastSixtyFiveThousandVertices_IndicesAreThirtyTwoBit() {
            const int n = 70000;
            var positions = new Vector3[n];
            for (int i = 0; i < n; i++) positions[i] = new Vector3(i % 300, 0, i / 300);
            var indices = new uint[] { 0, 1, n - 1 };
            var mesh = ModelBridge.MeshOf(Made(positions, indices));
            Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));
            Assert.That(mesh.GetIndices(0)[2], Is.EqualTo(n - 1));
            Assert.That(ModelBridge.MeshOf(Made(Square, TwoTriangles)).indexFormat, Is.EqualTo(IndexFormat.UInt16), "a small mesh stays 16-bit");
        }

        // Gunyard's shadow, 200 to 400 vertices a frame: within capacity a set writes into the
        // buffers it has; past it they grow to twice, once; a shorter set draws only what it gave.
        [Test]
        public void Set_WithinCapacity_KeepsItsBuffers_AndPastItGrowsToTwice() {
            int model = Made(Fan(400), FanIndices(400));
            Assert.That(ModelBridge.MeshCapacity(model), Is.EqualTo(new Vector2Int(400, 398 * 3)), "made exactly its size");
            var mesh = ModelBridge.MeshOf(model);

            // The first set after the one that made it marks the mesh dynamic, which remakes its buffers once.
            Assert.That(ModelBridge.SetMesh(model, Fan(300), FanIndices(300), null, null, null), Is.Empty);
            Assert.That(ModelBridge.MeshCapacity(model), Is.EqualTo(new Vector2Int(400, 398 * 3)), "a shorter set fits");
            Assert.That(mesh.GetSubMesh(0).indexCount, Is.EqualTo(298 * 3), "and draws only what it gave");
            Assert.That(mesh.GetSubMesh(0).vertexCount, Is.EqualTo(300));
            var buffer = VertexBuffer(mesh);
            Assert.That(ModelBridge.SetMesh(model, Fan(350), FanIndices(350), null, null, null), Is.Empty);
            Assert.That(VertexBuffer(mesh), Is.EqualTo(buffer), "the same vertex buffer");

            Assert.That(ModelBridge.SetMesh(model, Fan(500), FanIndices(500), null, null, null), Is.Empty);
            Assert.That(ModelBridge.MeshCapacity(model), Is.EqualTo(new Vector2Int(800, 398 * 6)), "grown to twice");
            Assert.That(ModelBridge.SetMesh(model, Fan(700), FanIndices(700), null, null, null), Is.Empty);
            Assert.That(ModelBridge.MeshCapacity(model), Is.EqualTo(new Vector2Int(800, 398 * 6)), "and the next larger set fits");
            Assert.That(mesh.GetSubMesh(0).indexCount, Is.EqualTo(698 * 3));
        }

        [Test]
        public void Set_LeavingIndicesOut_KeepsThem_AndPositionsAloneMoveTheVertices() {
            int model = Made(Square, TwoTriangles);
            var moved = Square.Select(p => p + Vector3.up * 2).ToArray();
            Assert.That(ModelBridge.SetMesh(model, moved, null, null, null, null), Is.Empty);
            var mesh = ModelBridge.MeshOf(model);
            Assert.That(mesh.GetIndices(0), Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
            Assert.That(mesh.bounds.center.y, Is.EqualTo(2).Within(1e-5f), "bounds follow, for culling");
            Assert.That(ModelBridge.Height(model), Is.EqualTo(2).Within(1e-5f));
        }

        [Test]
        public void Set_RefusesWhatItCannotDraw_SayingWhy_AndChangesNothing() {
            int model = Made(Square, TwoTriangles);
            var mesh = ModelBridge.MeshOf(model);
            Assert.That(ModelBridge.SetMesh(model, Square, new uint[] { 0, 1, 9 }, null, null, null), Is.EqualTo("index 2 is 9, and there are 4 vertices (0 to 3)"));
            Assert.That(ModelBridge.SetMesh(model, Square, new int[] { 0, -1, 2 }, null, null, null), Does.StartWith("index 1 is -1"));
            Assert.That(ModelBridge.SetMesh(model, Square, new uint[] { 0, 1 }, null, null, null), Does.Contain("not three a triangle"));
            Assert.That(ModelBridge.SetMesh(model, Square.Take(2).ToArray(), null, null, null, null), Does.Contain("reach vertex"));
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, Square, null, null), Does.Contain("made without normals"));
            Assert.That(ModelBridge.SetMesh(model, Square, new float[] { 0, 1, 2 }, null, null, null), Does.Contain("Uint32Array"));
            Assert.That(ModelBridge.SetMesh(model, null, TwoTriangles, null, null, null), Is.EqualTo("positions are required"));
            Assert.That(ModelBridge.SetMesh(12345, Square, TwoTriangles, null, null, null), Does.Contain("no mesh model"));
            Assert.That(mesh.GetIndices(0), Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));

            int colored = Made(Square, TwoTriangles, null, new float[12]);
            Assert.That(ModelBridge.SetMesh(colored, Fan(5), FanIndices(5), null, null, null), Does.Contain("colors are required"));
            Assert.That(ModelBridge.SetMesh(colored, Square, TwoTriangles, null, new float[10], null), Does.Contain("neither 3 nor 4"));
            Assert.That(ModelBridge.SetMesh(colored, Square, null, null, null, null), Is.Empty, "the same vertices keep their colours");
        }

        [Test]
        public void Spawned_ShareTheMesh_AndAClickHitsItsNewSize() {
            int model = Made(Square, TwoTriangles);
            int a = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int b = ModelBridge.Spawn(model, 5, 0, 0, 0, 1, true, true);
            Mesh Of(int actor) => ModelBridge.ActorObject(actor).GetComponent<MeshFilter>().sharedMesh;
            Assert.That(Of(a), Is.SameAs(ModelBridge.MeshOf(model)));
            Assert.That(Of(b), Is.SameAs(Of(a)));

            var tall = Square.Select(p => new Vector3(p.x, p.y * 3, p.z)).ToArray();
            ModelBridge.SetMesh(model, tall, null, null, null, null);
            Assert.That(ModelBridge.ActorObject(a).GetComponent<BoxCollider>().size.y, Is.EqualTo(6).Within(1e-5f));
        }

        [Test]
        public void Unload_FreesItsMeshAndMaterial() {
            int model = Made(Square, TwoTriangles);
            var mesh = ModelBridge.MeshOf(model);
            var material = ModelBridge.ActorObject(ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true)).GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(ModelBridge.Unload(model), Is.False, "an actor of it is in the world");
            ModelBridge.DisposeAll();
            Assert.That(mesh == null, Is.True, "the mesh went with the scene");
            Assert.That(material == null, Is.True, "and its material");

            ModelBridge.BeginScene();
            int alone = Made(Square, TwoTriangles);
            var freed = ModelBridge.MeshOf(alone);
            Assert.That(ModelBridge.Unload(alone), Is.True);
            Assert.That(freed == null, Is.True);
            Assert.That(ModelBridge.MeshOf(alone), Is.Null);
        }

        [Test]
        public void AlphaBelowOne_DrawsSeeThrough_AndBackToOneOpaque() {
            int model = Made(Square, TwoTriangles, null, Enumerable.Repeat(1f, 16).ToArray());
            var material = ModelBridge.ActorObject(ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true)).GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
            var half = Enumerable.Range(0, 16).Select(i => i % 4 == 3 ? 0.5f : 1f).ToArray();
            ModelBridge.SetMesh(model, Square, null, null, half, null);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(0f));
            ModelBridge.SetMesh(model, Square, null, null, Enumerable.Repeat(1f, 16).ToArray(), null);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1f));
        }

        // ModelLit read no vertex colour before _VERTEX_COLOR: this square drew white.
        [Test]
        public void VertexColours_AreWhatItDraws() {
            var target = new RenderTexture(32, 32, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try {
                var red = Enumerable.Range(0, 4).SelectMany(_ => new[] { 1f, 0f, 0f }).ToArray();
                int model = Made(Square, TwoTriangles, null, red);
                var actor = ModelBridge.ActorObject(ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false));
                actor.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Unlit", 1);
                camera.transform.position = new Vector3(0, 0, -2);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                RenderTexture.active = was;
                var centre = read.GetPixel(16, 16);
                Assert.That(centre.r, Is.GreaterThan(0.9f), $"drew {centre}");
                Assert.That(centre.g, Is.LessThan(0.1f), $"drew {centre}");
                Assert.That(centre.b, Is.LessThan(0.1f), $"drew {centre}");
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }

        // What onejs-play sends: a Float32Array becomes the Vector3[] the method takes, each kind of
        // index array its own element type, and a stream left out a null.
        [Test]
        public void FromJs_TypedArraysAndNullsArriveAsTheMethodTakesThem() {
            var ctx = new QuickJSContext();
            try {
                var answer = ctx.Eval(@"
                    var b = CS.OneJS.Models.ModelBridge;
                    var id = b.CreateMesh(false, true, true, 1, 1, 1, 1);
                    var square = new Float32Array([-1, -1, 0, -1, 1, 0, 1, 1, 0, 1, -1, 0]);
                    var said = [
                        b.SetMesh(id, square, new Uint16Array([0, 1, 2, 0, 2, 3]), null, new Float32Array(12).fill(0.5), new Float32Array(8)),
                        b.SetMesh(id, square, null, null, null, null),
                        b.SetMesh(id, square, new Uint32Array([0, 2, 3]), null, null, null),
                        b.SetMesh(id, square, new Int32Array([0, 1, 4]), null, null, null),
                    ];
                    id + '|' + said.join('|');
                ");
                var parts = answer.Split('|');
                int model = int.Parse(parts[0]);
                Assert.That(parts.Skip(1).Take(3), Is.All.Empty, answer);
                Assert.That(parts[4], Does.StartWith("index 2 is 4"), "an Int32Array is checked as signed");
                var mesh = ModelBridge.MeshOf(model);
                Assert.That(mesh.vertices[2], Is.EqualTo(new Vector3(1, 1, 0)));
                Assert.That(mesh.GetIndices(0), Is.EqualTo(new[] { 0, 2, 3 }));
                Assert.That(mesh.colors[0].a, Is.EqualTo(1f), "three numbers a vertex: opaque");
                Assert.That(mesh.uv.Length, Is.EqualTo(4));
            } finally {
                ctx.Dispose();
                QuickJSNative.ClearAllHandles();
            }
        }

        /// <summary>A fan of `n` vertices around the origin: n - 2 triangles.</summary>
        static Vector3[] Fan(int n) =>
            Enumerable.Range(0, n).Select(i => i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(i * 0.01f), 0, Mathf.Sin(i * 0.01f))).ToArray();

        static uint[] FanIndices(int n) =>
            Enumerable.Range(1, n - 2).SelectMany(i => new[] { 0u, (uint)i, (uint)(i + 1) }).ToArray();

        // The native buffer where there is a GPU device; without one, the buffer's own size.
        static long VertexBuffer(Mesh mesh) {
            using var buffer = mesh.GetVertexBuffer(0);
            var native = buffer.GetNativeBufferPtr().ToInt64();
            return native != 0 ? native : buffer.count;
        }

        static void AssertColor(Color got, Color want) {
            Assert.That(got.r, Is.EqualTo(want.r).Within(2e-3), "r");
            Assert.That(got.g, Is.EqualTo(want.g).Within(2e-3), "g");
            Assert.That(got.b, Is.EqualTo(want.b).Within(2e-3), "b");
        }
    }
}
