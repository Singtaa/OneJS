using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A mesh model's look besides its colours and pictures (ModelBridge.Look.cs): cut out by its
    /// alpha, drawn from both sides with its normals as given, unlit, added to what is behind it,
    /// and swayed by the scene's wind. What a
    /// leaf card needs, and before them a mesh model drew every texel opaque, one side only, still.
    /// </summary>
    public class MeshLookEditModeTests {
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

        // A picture whose left half is clear and right half white.
        static Texture2D HalfClear() {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            t.SetPixels(Enumerable.Range(0, 64).Select(i => i % 8 < 4 ? new Color(1, 1, 1, 0) : Color.white).ToArray());
            t.Apply();
            return t;
        }

        // The model as an actor at the origin (or what is there already, with `spawn` false), seen
        // from `z` on the z axis looking at it, at 32 by 32.
        static Color32[] Shot(int model, float z = -2, bool unlit = true, bool spawn = true) {
            var target = new RenderTexture(32, 32, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            int actor = spawn ? ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false) : 0;
            try {
                if (unlit) ModelBridge.MaterialOf(model).SetFloat("_Unlit", 1);
                camera.transform.position = new Vector3(0, 0, z);
                camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
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
        static int Covered(Color32[] shot) => shot.Count(p => !Background(p));

        [Test]
        public void Cutout_CutsWhereThePicturesAlphaIsBelowIt() {
            var picture = HalfClear();
            try {
                int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
                Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, Corners), Is.Empty);
                Assert.That(ModelBridge.SetMeshTexture(model, picture, 1, 1), Is.Empty);
                int whole = Covered(Shot(model));
                Assert.That(ModelBridge.SetMeshCutout(model, 0.5f), Is.Empty);
                var material = ModelBridge.MaterialOf(model);
                Assert.That(material.GetFloat("_AlphaClip"), Is.EqualTo(1f));
                Assert.That(material.GetFloat("_Cutoff"), Is.EqualTo(0.5f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
                int cut = Covered(Shot(model));
                Assert.That(cut, Is.InRange(whole * 4 / 10, whole * 6 / 10), $"half of {whole} pixels");
                var shot = Shot(model);
                Assert.That(Background(shot[16 * 32 + 10]), Is.True, "the clear half is cut away");
                Assert.That(Background(shot[16 * 32 + 22]), Is.False, "the white half stays");
            } finally {
                Object.DestroyImmediate(picture);
            }
        }

        [Test]
        public void Cutout_TestsTheVertexAlpha_AndKeepsTheModelOpaque() {
            int model = ModelBridge.CreateMesh(false, true, false, 1, 1, 1, 1);
            // Clear on the left two corners, whole on the right: at 0.5 the left half goes.
            var colors = new float[] { 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1 };
            Assert.That(ModelBridge.SetMeshCutout(model, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, colors, null), Is.Empty);
            var material = ModelBridge.MaterialOf(model);
            Assert.That(material.GetFloat("_Surface"), Is.EqualTo(0f), "an alpha below 1 does not make a cut-out model see-through");
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1f));
            var shot = Shot(model);
            Assert.That(Background(shot[16 * 32 + 6]), Is.True, "the clear side is cut away");
            Assert.That(Background(shot[16 * 32 + 26]), Is.False, "the whole side stays");
        }

        [Test]
        public void Cutout_RefusesWhatItCannotCut() {
            int model = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshCutout(model, 1.5f), Does.Contain("runs from 0 to 1"));
            Assert.That(ModelBridge.SetMeshCutout(model, float.NaN), Does.Contain("runs from 0 to 1"));
            Assert.That(ModelBridge.SetMeshCutout(12345, 0.5f), Does.Contain("no mesh model"));
            var picture = HalfClear();
            try {
                ModelBridge.SetMeshSplat(model, picture, null, null, null, 1, 1, 0);
                Assert.That(ModelBridge.SetMeshCutout(model, 0.5f), Does.Contain("no alpha"));
            } finally {
                Object.DestroyImmediate(picture);
            }
        }

        [Test]
        public void Unlit_SetsTheMaterialsUnlit_AndHoldsThroughAColour() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.MaterialOf(model).GetFloat("_Unlit"), Is.EqualTo(0f));
            Assert.That(ModelBridge.SetMeshUnlit(model), Is.Empty);
            Assert.That(ModelBridge.SetMeshColor(model, 1, 0, 0, 0.5f), Is.Empty);
            Assert.That(ModelBridge.MaterialOf(model).GetFloat("_Unlit"), Is.EqualTo(1f));
            Assert.That(ModelBridge.SetMeshUnlit(12345), Does.Contain("no mesh model"));
        }

        [Test]
        public void Additive_AddsItsColourToWhatIsBehind() {
            int model = ModelBridge.CreateMesh(false, false, false, 0, 0.5f, 0, 1);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null), Is.Empty);
            var over = Shot(model)[32 * 16 + 16];
            Assert.That(over.r, Is.LessThan(5), $"opaque, it hides the magenta behind it: {over}");
            Assert.That(ModelBridge.SetMeshAdditive(model), Is.Empty);
            var added = Shot(model)[32 * 16 + 16];
            Assert.That(added.r, Is.GreaterThan(250), $"added, the magenta stays: {added}");
            Assert.That(added.b, Is.GreaterThan(250), $"added, the magenta stays: {added}");
            Assert.That(added.g, Is.GreaterThan(40), $"and the green is added to it: {added}");
        }

        [Test]
        public void Additive_DrawsAfterTheOpaques_WritesNoDepth_CastsNoShadow_AndStaysThroughAnAlpha() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null), Is.Empty);
            int before = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            Assert.That(ModelBridge.SetMeshAdditive(model), Is.Empty);
            int after = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            void Check(string when) {
                var material = ModelBridge.MaterialOf(model);
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha), when);
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.One), when);
                Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(0f), when);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent), when);
            }
            Check("made additive");
            foreach (var actor in new[] { before, after }) {
                foreach (var r in ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>()) {
                    Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), $"actor {actor}");
                }
            }
            Assert.That(ModelBridge.SetMeshColor(model, 1, 1, 1, 0.5f), Is.Empty);
            Check("an alpha below 1");
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshColor(model, 1, 1, 1, 1), Is.Empty);
            Check("an alpha back at 1");
        }

        [Test]
        public void Additive_RefusesWhatItCannotAdd() {
            int cut = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshCutout(cut, 0.5f), Is.Empty);
            Assert.That(ModelBridge.SetMeshAdditive(cut), Does.Contain("cut out"));
            int added = ModelBridge.CreateMesh(false, false, true, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshAdditive(added), Is.Empty);
            Assert.That(ModelBridge.SetMeshCutout(added, 0.5f), Does.Contain("nothing to cut out"));
            var picture = HalfClear();
            try {
                Assert.That(ModelBridge.SetMeshSplat(added, picture, null, null, null, 1, 1, 0), Does.Contain("added to what is behind it"));
            } finally {
                Object.DestroyImmediate(picture);
            }
            int depth = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetDepthOnly(depth), Is.True);
            Assert.That(ModelBridge.SetMeshAdditive(depth), Does.Contain("depth-only"));
            Assert.That(ModelBridge.SetMeshUnlit(depth), Does.Contain("depth-only"));
        }

        [Test]
        public void TwoSided_DrawsTheBack_LitWithItsNormalAsGiven() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null), Is.Empty);
            Assert.That(Covered(Shot(model, 2)), Is.Zero, "one-sided, its back is culled");
            Assert.That(ModelBridge.SetMeshTwoSided(model), Is.Empty);
            var material = ModelBridge.MaterialOf(model);
            Assert.That(material.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off));
            Assert.That(material.GetFloat("_KeepBackNormal"), Is.EqualTo(1f));
            Assert.That(Covered(Shot(model, 2)), Is.GreaterThan(32 * 32 / 2), "two-sided, its back draws");

            // Lit by a sun on either side, the back looks as the front does: the normal is not flipped.
            foreach (var z in new[] { 1f, -1f }) {
                ModelBridge.SetSun(true, 0.3f, -0.2f, z, 1, 1, 1, 1, false);
                var front = Shot(model, -2, unlit: false)[16 * 32 + 16];
                var back = Shot(model, 2, unlit: false)[16 * 32 + 16];
                Assert.That(Mathf.Abs(front.g - back.g), Is.LessThanOrEqualTo(3), $"sun toward z {z}: front {front}, back {back}");
            }
        }

        // A square whose top edge sways a unit and whose foot is fixed, with the wind along +x.
        static int Swaying() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshSway(model), Is.Empty);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null, null, new float[] { 0, 1, 1, 0 }), Is.Empty);
            return model;
        }

        // The mean column of the covered pixels in the top or the bottom quarter of a shot.
        static float Column(Color32[] shot, bool top) {
            float sum = 0;
            int n = 0;
            for (int y = top ? 24 : 0; y < (top ? 32 : 8); y++) {
                for (int x = 0; x < 32; x++) {
                    if (Background(shot[y * 32 + x])) continue;
                    sum += x;
                    n++;
                }
            }
            return n == 0 ? float.NaN : sum / n;
        }

        [Test]
        public void Sway_MovesTheVerticesAlongTheWind_ByTheirSway() {
            int model = Swaying();
            Assert.That(ModelBridge.MaterialOf(model).GetFloat("_Sway"), Is.EqualTo(1f));
            Assert.That(ModelBridge.MeshOf(model).HasVertexAttribute(VertexAttribute.TexCoord3), Is.True);
            ModelBridge.SetWindClock(0);
            Assert.That(ModelBridge.SetWind(1, 0, 0, 0), Is.Empty);
            var still = Shot(model);
            Assert.That(ModelBridge.SetWind(1, 0, 0.5f, 0), Is.Empty);
            var blown = Shot(model);
            // From z = -2 looking at +z, +x is the screen's right.
            Assert.That(Column(blown, true), Is.GreaterThan(Column(still, true) + 1), "the top moves along the wind");
            Assert.That(Column(blown, false), Is.EqualTo(Column(still, false)).Within(0.5f), "the foot stays");
            Assert.That(ModelBridge.SetWind(-1, 0, 0.5f, 0), Is.Empty);
            Assert.That(Column(Shot(model), true), Is.LessThan(Column(still, true) - 1), "and the other way when the wind turns");
            ModelBridge.SetWind(1, 0, 0.5f, 0);
            // At the origin a copy's swing is 0.62 at 0 s and 0.33 at 3.5 s (ModelWind.hlsl).
            ModelBridge.SetWindClock(3.5f);
            Assert.That(Column(Shot(model), true), Is.Not.EqualTo(Column(blown, true)).Within(0.25f), "and swings with the scene's clock");
        }

        [Test]
        public void Sway_MovesAScatteredCopy_AsItMovesAnActor() {
            int model = Swaying();
            ModelBridge.SetWindClock(0);
            int scatter = ModelBridge.Scatter(model, new float[] { 0, 0, 0, 0, 1 });
            ModelBridge.SetWind(1, 0, 0, 0);
            var still = Shot(model, spawn: false);
            ModelBridge.SetWind(1, 0, 0.5f, 0);
            var copy = Shot(model, spawn: false);
            // Equal to the actor alone would pass with no sway at all, so the copy is held to moving first.
            Assert.That(Column(copy, true), Is.GreaterThan(Column(still, true) + 1), "the copy's top moves along the wind");
            ModelBridge.DestroyScatter(scatter);
            var actor = Shot(model);
            Assert.That(Column(copy, true), Is.EqualTo(Column(actor, true)).Within(0.5f), "as far as the actor's does");
        }

        [Test]
        public void Sway_RefusesWhatItCannotTake_SayingWhy() {
            int model = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null, null, new float[] { 0, 1, 1, 0 }), Does.Contain("without sway"));
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, null, null, null), Is.Empty);
            Assert.That(ModelBridge.SetMeshSway(model), Does.Contain("before the model's first vertices"));
            int swaying = ModelBridge.CreateMesh(false, false, false, 1, 1, 1, 1);
            ModelBridge.SetMeshSway(swaying);
            Assert.That(ModelBridge.SetMesh(swaying, Square, TwoTriangles, null, null, null), Does.Contain("sway is required"));
            Assert.That(ModelBridge.SetMesh(swaying, Square, TwoTriangles, null, null, null, null, new float[] { 0, 1 }), Does.Contain("2 sways for 4 positions"));
            Assert.That(ModelBridge.SetMesh(swaying, Square, TwoTriangles, null, null, null, null, new float[] { 0, 1, 1, 0 }), Is.Empty);
            Assert.That(ModelBridge.SetMesh(swaying, Square.Select(p => p * 2).ToArray(), null, null, null, null), Is.Empty, "new positions keep the sway");
            var sway = new System.Collections.Generic.List<Vector2>();
            ModelBridge.MeshOf(swaying).GetUVs(3, sway);
            Assert.That(sway.Select(s => s.x), Is.EqualTo(new[] { 0f, 1f, 1f, 0f }));
        }

        [Test]
        public void Sway_TheLightsUnwrapKeepsIt() {
            int model = Swaying();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            Assert.That(ModelBridge.SetLightUnwrap(model, new uint[] { 0, 1, 2, 3 }, new float[] { 0, 0, 0, 1, 1, 1, 1, 0 }, TwoTriangles), Is.Empty);
            var lit = ModelBridge.ActorObject(actor).GetComponent<MeshFilter>().sharedMesh;
            Assert.That(lit, Is.Not.SameAs(ModelBridge.MeshOf(model)));
            var sway = new System.Collections.Generic.List<Vector2>();
            lit.GetUVs(3, sway);
            Assert.That(sway.Select(s => s.x), Is.EqualTo(new[] { 0f, 1f, 1f, 0f }));
        }

        [Test]
        public void Wind_RefusesWhatIsNoWind_AndGoesWithTheScene() {
            Assert.That(ModelBridge.SetWind(0, 0, 1, 0), Does.Contain("points nowhere"));
            Assert.That(ModelBridge.SetWind(1, 0, -1, 0), Does.Contain("0 or more"));
            Assert.That(ModelBridge.SetWind(1, 0, 1, 2), Does.Contain("runs from 0 to 1"));
            Assert.That(ModelBridge.SetWind(3, 4, 1.5f, 0.3f), Is.Empty);
            var wind = ModelBridge.Wind;
            Assert.That(wind.x, Is.EqualTo(0.6f).Within(1e-6f), "its direction made length 1");
            Assert.That(wind.y, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(wind.z, Is.EqualTo(1.5f));
            Assert.That(wind.w, Is.EqualTo(0.3f).Within(1e-6f));
            ModelBridge.DisposeAll();
            Assert.That(ModelBridge.Wind, Is.EqualTo(Vector4.zero), "no wind once the scene is gone");
        }
    }
}
