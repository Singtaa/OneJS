using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// Pictures on a mesh model (ModelBridge.Ground.cs): one texture tiled under its vertex colours
    /// (SetMeshTexture), and up to four painted by weights a vertex on GroundLit (SetMeshSplat).
    /// Before them a mesh model took uvs and had nothing to read them with: its base map stayed white.
    /// </summary>
    public class GroundEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A square in the XY plane facing the camera at -z.
        static readonly Vector3[] Square = { new(-1, -1, 0), new(-1, 1, 0), new(1, 1, 0), new(1, -1, 0) };
        static readonly Vector2[] Corners = { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
        static readonly uint[] TwoTriangles = { 0, 1, 2, 0, 2, 3 };

        static int WithUvs() => ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);

        static void Set(int model, float[] weights = null) =>
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, Corners, weights), Is.Empty);

        // A picture as a cart's loadTexture hands it over: clamped, and without mips on the web.
        static Texture2D Picture(Color color, int size = 4) {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            t.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            t.Apply();
            return t;
        }

        [Test]
        public void Texture_IsACopyThatRepeats_WithMips_TiledByItsScale() {
            var grass = Picture(Color.green);
            try {
                int model = WithUvs();
                Set(model);
                Assert.That(ModelBridge.SetMeshTexture(model, grass, 8, 4), Is.Empty);
                var material = ModelBridge.MaterialOf(model);
                var map = material.GetTexture("_BaseMap") as RenderTexture;
                Assert.That(map, Is.Not.Null, "the bridge's own copy, not the cart's picture");
                Assert.That(map.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(map.useMipMap, Is.True, "a mip chain, or 64 m of ground shimmers in the distance");
                Assert.That(map.filterMode, Is.EqualTo(FilterMode.Trilinear));
                Assert.That(material.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(8, 4)));
                Assert.That(grass.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), "the cart's picture is left as it was");
            } finally {
                Object.DestroyImmediate(grass);
            }
        }

        // Twenty-five chunks of one grass are one copy, which goes with the last of them.
        [Test]
        public void Texture_ModelsGivenOnePictureShareOneCopy_UntilTheLastGoes() {
            var grass = Picture(Color.green);
            try {
                int a = WithUvs(), b = WithUvs();
                Set(a);
                Set(b);
                ModelBridge.SetMeshTexture(a, grass, 1, 1);
                ModelBridge.SetMeshTexture(b, grass, 2, 2);
                Assert.That(ModelBridge.GroundCopyCount, Is.EqualTo(1));
                var copy = ModelBridge.MeshTextureOf(a);
                Assert.That(ModelBridge.MeshTextureOf(b), Is.SameAs(copy));
                Assert.That(ModelBridge.Unload(a), Is.True);
                Assert.That(copy != null, Is.True, "b still draws it");
                Assert.That(ModelBridge.SetMeshTexture(b, null, 1, 1), Is.Empty, "taken off");
                Assert.That(copy == null, Is.True, "and gone with its last user");
                Assert.That(ModelBridge.GroundCopyCount, Is.Zero);
            } finally {
                Object.DestroyImmediate(grass);
            }
        }

        [Test]
        public void Texture_RefusesWhatItCannotDraw_SayingWhy() {
            var grass = Picture(Color.green);
            try {
                int bare = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMeshTexture(bare, grass, 1, 1), Does.Contain("without uvs"));
                int model = WithUvs();
                Assert.That(ModelBridge.SetMeshTexture(model, grass, 0, 1), Does.Contain("tiling is 0 by 1"));
                Assert.That(ModelBridge.SetMeshTexture(model, grass, float.NaN, 1), Does.Contain("tiling"));
                Assert.That(ModelBridge.SetMeshTexture(12345, grass, 1, 1), Does.Contain("no mesh model"));
                Assert.That(ModelBridge.GroundCopyCount, Is.Zero, "nothing was copied for a refusal");
            } finally {
                Object.DestroyImmediate(grass);
            }
        }

        [Test]
        public void Texture_IsWhatItDraws_TimesTheVertexColours() {
            var blue = Picture(Color.blue);
            try {
                int model = ModelBridge.CreateMesh(false, true, true, 1, 1, 1, 1);
                var white = Enumerable.Repeat(1f, 12).ToArray();
                Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, white, Corners), Is.Empty);
                Assert.That(ModelBridge.SetMeshTexture(model, blue, 3, 3), Is.Empty);
                var centre = Draw(model);
                Assert.That(centre.b, Is.GreaterThan(0.9f), $"drew {centre}");
                Assert.That(centre.r + centre.g, Is.LessThan(0.1f), $"drew {centre}");
            } finally {
                Object.DestroyImmediate(blue);
            }
        }

        [Test]
        public void Splat_DrawsOnGroundLit_WithWeightsInTexcoord2_AndColoursFilledWhite() {
            var red = Picture(Color.red);
            var blue = Picture(Color.blue);
            try {
                int model = WithUvs();
                var before = ModelBridge.MaterialOf(model);
                Assert.That(ModelBridge.SetMeshSplat(model, red, blue, null, null, 8, 8, 0), Is.Empty);
                Set(model, new float[] { 0, 1, 0, 1, 0, 1, 0, 1 });
                var material = ModelBridge.MaterialOf(model);
                Assert.That(material.shader.name, Is.EqualTo("OneJS/GroundLit"));
                Assert.That(before == null, Is.True, "ModelLit's material went");
                Assert.That(material.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(8, 8)), "the layers' tiling");
                Assert.That(ModelBridge.SplatLayersOf(model), Has.Length.EqualTo(2));
                var mesh = ModelBridge.MeshOf(model);
                Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord2), Is.True);
                var weights = new System.Collections.Generic.List<Vector4>();
                mesh.GetUVs(2, weights);
                Assert.That(weights, Is.All.EqualTo(new Vector4(0, 1, 0, 0)), "one a layer, padded to four");
                Assert.That(mesh.colors, Is.All.EqualTo(Color.white), "GroundLit always reads colours");

                var centre = Draw(model);
                Assert.That(centre.b, Is.GreaterThan(0.9f), $"weighted all blue, drew {centre}");
                Assert.That(centre.r, Is.LessThan(0.1f), $"weighted all blue, drew {centre}");
                Assert.That(ModelBridge.SetMesh(model, Square, null, null, null, null, new float[] { 1, 0, 1, 0, 1, 0, 1, 0 }), Is.Empty, "new weights alone");
                centre = Draw(model);
                Assert.That(centre.r, Is.GreaterThan(0.9f), $"weighted all red, drew {centre}");
                Assert.That(centre.b, Is.LessThan(0.1f), $"weighted all red, drew {centre}");
            } finally {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        // Red stands a whole height above blue: at an even mix the weights alone draw purple, and a
        // height blend draws the red that stands out of it.
        [Test]
        public void Splat_HeightBlend_ShowsTheHigherLayer_WhereTheWeightsAloneMix() {
            var red = Picture(new Color(1, 0, 0, 1));
            var blue = Picture(new Color(0, 0, 1, 0));
            try {
                int model = WithUvs();
                ModelBridge.SetMeshSplat(model, red, blue, null, null, 1, 1, 0);
                Set(model, Enumerable.Repeat(0.5f, 8).ToArray());
                var mixed = Draw(model);
                Assert.That(mixed.r, Is.InRange(0.2f, 0.85f), $"an even mix, drew {mixed}");
                Assert.That(mixed.b, Is.InRange(0.2f, 0.85f), $"an even mix, drew {mixed}");
                Assert.That(Mathf.Abs(mixed.r - mixed.b), Is.LessThan(0.05f), $"an even mix, drew {mixed}");

                Assert.That(ModelBridge.SetMeshSplat(model, red, blue, null, null, 1, 1, 0.2f), Is.Empty, "a second splat keeps its layers' count");
                var blended = Draw(model);
                Assert.That(blended.r, Is.GreaterThan(0.9f), $"the higher layer, drew {blended}");
                Assert.That(blended.b, Is.LessThan(0.1f), $"the higher layer, drew {blended}");
            } finally {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        [Test]
        public void Splat_RefusesWhatItCannotDraw_SayingWhy_AndChangesNothing() {
            var red = Picture(Color.red);
            var blue = Picture(Color.blue);
            try {
                int model = WithUvs();
                Assert.That(ModelBridge.SetMeshSplat(model, null, null, null, null, 1, 1, 0), Does.Contain("at least one layer"));
                Assert.That(ModelBridge.SetMeshSplat(model, red, null, blue, null, 1, 1, 0), Does.Contain("in order"));
                Assert.That(ModelBridge.SetMeshSplat(model, red, null, null, null, 1, 1, 2), Does.Contain("heightBlend is 2"));
                Assert.That(ModelBridge.SetMeshSplat(ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1), red, null, null, null, 1, 1, 0), Does.Contain("without uvs"));
                Assert.That(ModelBridge.GroundCopyCount, Is.Zero, "nothing was copied for a refusal");
                Assert.That(ModelBridge.MaterialOf(model).shader.name, Is.EqualTo("OneJS/ModelLit"), "and nothing changed");

                Set(model);
                Assert.That(ModelBridge.SetMeshSplat(model, red, null, null, null, 1, 1, 0), Does.Contain("before the model's first vertices"));
                Assert.That(ModelBridge.SetMesh(model, Square, null, null, null, null, new float[4]), Does.Contain("no splat"));

                int splat = WithUvs();
                ModelBridge.SetMeshSplat(splat, red, blue, null, null, 1, 1, 0);
                Assert.That(ModelBridge.SetMesh(splat, Square, TwoTriangles, null, null, Corners, null), Does.Contain("weights are required"));
                Assert.That(ModelBridge.SetMesh(splat, Square, TwoTriangles, null, null, Corners, new float[4]), Does.Contain("weights has 4 numbers, and a splat of 2 layers takes 2 a vertex: 8"));
                Set(splat, new float[8]);
                Assert.That(ModelBridge.SetMeshSplat(splat, red, null, null, null, 1, 1, 0), Does.Contain("keeps 2"));
                Assert.That(ModelBridge.SetMeshTexture(splat, red, 1, 1), Does.Contain("painted by a splat"));

                int textured = WithUvs();
                ModelBridge.SetMeshTexture(textured, red, 1, 1);
                Assert.That(ModelBridge.SetMeshSplat(textured, red, null, null, null, 1, 1, 0), Does.Contain("has a texture"));
            } finally {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        // The bake's unwrap rebuilds the mesh with its light's uv in TEXCOORD1; a splat's weights
        // are in TEXCOORD2, and a rebuild that dropped them drew the first layer alone once baked.
        [Test]
        public void Splat_TheLightsUnwrapKeepsTheWeights() {
            var red = Picture(Color.red);
            var blue = Picture(Color.blue);
            try {
                int model = WithUvs();
                ModelBridge.SetMeshSplat(model, red, blue, null, null, 1, 1, 0);
                Set(model, new float[] { 0.25f, 0.75f, 0.25f, 0.75f, 0.25f, 0.75f, 0.25f, 0.75f });
                int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
                Assert.That(ModelBridge.SetLightUnwrap(model, new uint[] { 0, 1, 2, 3 }, new float[] { 0, 0, 0, 1, 1, 1, 1, 0 }, TwoTriangles), Is.Empty);
                var lit = ModelBridge.ActorObject(actor).GetComponent<MeshFilter>().sharedMesh;
                Assert.That(lit, Is.Not.SameAs(ModelBridge.MeshOf(model)), "the actor draws the rebuilt mesh");
                var weights = new System.Collections.Generic.List<Vector4>();
                lit.GetUVs(2, weights);
                Assert.That(weights, Has.Count.EqualTo(4));
                Assert.That(weights, Is.All.EqualTo(new Vector4(0.25f, 0.75f, 0, 0)));
            } finally {
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
            }
        }

        // An actor of the model, unlit, drawn at 32 by 32 from straight in front: the colour at its centre.
        static Color Draw(int model) {
            var target = new RenderTexture(32, 32, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            try {
                ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
                camera.transform.position = new Vector3(0, 0, -2);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                RenderTexture.active = was;
                return read.GetPixel(16, 16);
            } finally {
                ModelBridge.Destroy(actor);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }
    }
}
