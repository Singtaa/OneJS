using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.Scatter: copies of a model that never move, drawn as instanced chunks with no
    /// GameObject, culled by Unity per camera and per shadow pass by each chunk's bounds.
    /// </summary>
    public class ScatterEditModeTests {
        static string Fixture(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here)), "Fixtures~", name);

        static IEnumerator Load(System.Action<int> done, string fixture) {
            Task<int> task = ModelBridge.Load(Fixture(fixture));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            done(task.Result);
        }

        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        /// <summary>A grid of `w` by `d` copies one unit apart, five floats a copy.</summary>
        static float[] Grid(int w, int d, float y = 0) {
            var data = new float[w * d * 5];
            for (int i = 0; i < w * d; i++) {
                data[i * 5] = i % w;
                data[i * 5 + 1] = y;
                data[i * 5 + 2] = i / w;
                data[i * 5 + 3] = 0;
                data[i * 5 + 4] = 0.4f;
            }
            return data;
        }

        static int Square() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            ModelBridge.SetMesh(model, new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) },
                new uint[] { 0, 1, 2, 0, 2, 3 }, null, null, null);
            return model;
        }

        [UnityTest]
        public IEnumerator Copies_Draw_AndGoWhenDestroyed() {
            int model = 0;
            yield return Load(m => model = m, "cubes.glb");
            var target = new RenderTexture(64, 64, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            try {
                camera.transform.position = new Vector3(25, 30, 20);
                camera.transform.LookAt(new Vector3(25, 0, 20));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                int Covered() {
                    camera.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    RenderTexture.active = was;
                    return read.GetPixels32().Count(p => p.r < 250 || p.g > 5 || p.b < 250);
                }
                Assert.That(Covered(), Is.EqualTo(0), "nothing yet");
                int scatter = ModelBridge.Scatter(model, Grid(50, 40));
                Assert.That(scatter, Is.GreaterThan(0));
                // Cubes a unit wide at scale 0.4, a unit apart: about 16% of the view from above.
                Assert.That(Covered(), Is.GreaterThan(64 * 64 / 10), "2,000 copies of a model of two parts draw");
                ModelBridge.DestroyScatter(scatter);
                Assert.That(Covered(), Is.EqualTo(0), "and go with destroy");
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }

        [Test]
        public void Copies_AreChunksOfNeighbours_EachWithinDrawLimit() {
            int scatter = ModelBridge.Scatter(Square(), Grid(50, 40));
            var chunks = ModelBridge.ScatterChunks(scatter);
            Assert.That(chunks.Select(c => c.count), Is.EqualTo(new[] { 1023, 977 }));
            // Neighbours, not stripes across the whole grid: each chunk's footprint is well short of the field's.
            float field = 50 * 40;
            foreach (var c in chunks) Assert.That(c.bounds.size.x * c.bounds.size.z, Is.LessThan(field * 0.75f), c.bounds.ToString());
            var all = chunks[0].bounds;
            all.Encapsulate(chunks[1].bounds);
            // A copy may turn, so its footprint is the square's turning radius, sqrt(2) at scale 0.4.
            Assert.That(all.min.x, Is.EqualTo(-0.4f * Mathf.Sqrt(2)).Within(1e-4f));
            Assert.That(all.max.z, Is.GreaterThanOrEqualTo(39.4f));
        }

        [UnityTest]
        public IEnumerator ASkinnedModel_IsRefused() {
            int model = 0;
            yield return Load(m => model = m, "skinned.glb");
            Assert.That(ModelBridge.Scatter(model, Grid(2, 2)), Is.EqualTo(0));
            Assert.That(ModelBridge.ScatterCount, Is.EqualTo(0));
        }

        [Test]
        public void TheDraw_IsRegisteredOnlyWhileAScatterExists() {
            Assert.That(ModelBridge.ScatterDrawing, Is.False, "a cart that scatters nothing has no callback");
            int model = Square();
            int a = ModelBridge.Scatter(model, Grid(2, 2));
            int b = ModelBridge.Scatter(model, Grid(3, 3));
            Assert.That(ModelBridge.ScatterDrawing, Is.True);
            ModelBridge.DestroyScatter(a);
            Assert.That(ModelBridge.ScatterDrawing, Is.True, "one is left");
            ModelBridge.DestroyScatter(b);
            Assert.That(ModelBridge.ScatterDrawing, Is.False);
            ModelBridge.Scatter(model, Grid(2, 2));
            ModelBridge.DisposeAll();
            Assert.That(ModelBridge.ScatterDrawing, Is.False, "the scene takes it along");
            Assert.That(ModelBridge.ScatterCount, Is.EqualTo(0));
        }

        [Test]
        public void AModelWithCopiesScattered_StaysLoaded() {
            int model = Square();
            int scatter = ModelBridge.Scatter(model, Grid(2, 2));
            Assert.That(ModelBridge.Unload(model), Is.False, "the copies draw its mesh");
            ModelBridge.DestroyScatter(scatter);
            Assert.That(ModelBridge.Unload(model), Is.True);
        }

        // A mesh model's set moves its bounds; chunks measured with the old ones would be culled
        // where the copies no longer are.
        [Test]
        public void AMeshModelsSet_IsFollowedByTheChunksBounds() {
            var target = new RenderTexture(16, 16, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            try {
                int model = Square();
                int scatter = ModelBridge.Scatter(model, Grid(4, 4));
                Assert.That(ModelBridge.ScatterChunks(scatter)[0].bounds.size.y, Is.EqualTo(0).Within(1e-5f));
                ModelBridge.SetMesh(model, new[] { new Vector3(-1, 0, -1), new Vector3(-1, 5, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) }, null, null, null, null);
                camera.targetTexture = target;
                camera.Render();
                Assert.That(ModelBridge.ScatterChunks(scatter)[0].bounds.size.y, Is.EqualTo(2).Within(1e-4f), "five tall at scale 0.4");
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                target.Release();
            }
        }
    }
}
