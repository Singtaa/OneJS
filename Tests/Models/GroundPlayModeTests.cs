using System.Collections;
using System.Linq;
using NUnit.Framework;
using OneJS.Lighting;
using OneJS.Models;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A splat in the bake (ModelBridge.Ground.cs, BakeSceneBuilder): what a painted ground bounces
    /// is its layers' mean colours mixed by its weights. Before, the bake read GroundLit's white
    /// base map and bounced white light off red and blue ground alike.
    /// </summary>
    public class GroundPlayModeTests {
        static readonly Vector3[] Square = { new(-1, 0, -1), new(-1, 0, 1), new(1, 0, 1), new(1, 0, -1) };
        static readonly Vector2[] Corners = { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
        static readonly uint[] TwoTriangles = { 0, 1, 2, 0, 2, 3 };

        [SetUp]
        public void Begin() => ModelBridge.BeginScene();

        [TearDown]
        public void Clean() => ModelBridge.DisposeAll();

        static Texture2D Picture(Color color) {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            t.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            t.Apply();
            return t;
        }

        [UnityTest]
        public IEnumerator Bake_ASplatBouncesItsLayersMixedByItsWeights() {
            var red = Picture(Color.red);
            var blue = Picture(Color.blue);
            var scene = new BakeSceneBuilder();
            try {
                int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMeshSplat(model, red, blue, null, null, 8, 8, 0.3f), Is.Empty);
                var weights = Enumerable.Range(0, 4).SelectMany(_ => new[] { 1f, 3f }).ToArray();
                Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, Corners, weights), Is.Empty);
                scene.Add(ModelBridge.MeshOf(model), Matrix4x4.identity, new[] { ModelBridge.MaterialOf(model) }, new Vector4(1, 1, 0, 0));
                scene.ReadMeans();
                for (int frame = 0; frame < 120 && !scene.MeansReady; frame++) yield return null;
                Assert.That(scene.MeansReady, Is.True, "the layers' means were read back");
                var albedo = new Color[2];
                scene.FillAlbedo(albedo);
                foreach (var c in albedo) {
                    // A quarter red and three quarters blue, normalised as GroundLit normalises them.
                    Assert.That(c.r, Is.EqualTo(0.25f).Within(0.02f), $"bounced {c}");
                    Assert.That(c.g, Is.EqualTo(0f).Within(0.02f), $"bounced {c}");
                    Assert.That(c.b, Is.EqualTo(0.75f).Within(0.02f), $"bounced {c}");
                }
            } finally {
                scene.Dispose();
                Object.Destroy(red);
                Object.Destroy(blue);
            }
        }
    }
}
