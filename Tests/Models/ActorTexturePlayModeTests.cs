using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// An actor's own texture (ModelBridge.Texture.cs), as it is drawn: a texture replaces the
    /// model's own on that actor alone, null gives it back, actors wearing one texture share one
    /// copy and one material, and nothing is left once the last of them lets go. Each reading is
    /// the mean colour of the pixels an actor covers, lit by a white ambient alone.
    /// </summary>
    public class ActorTexturePlayModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", "reach.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        RenderTexture _screen;
        Texture2D _read, _red, _blue;

        [SetUp]
        public void Stage() {
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(0, 1, -6, 0, 1, 0, 40);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 1, 1, 1, false);
            ModelBridge.SetAmbient(1, 1, 1, 1, 1, 1, 1);
            ModelBridge.SetBackground(0, 0, 0);
            _screen = new RenderTexture(160, 160, 24);
            ModelBridge.SceneCamera.targetTexture = _screen;
            _read = new Texture2D(160, 160, TextureFormat.RGBA32, false);
            _red = Solid(new Color32(255, 0, 0, 255));
            _blue = Solid(new Color32(0, 0, 255, 255));
        }

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            Object.Destroy(_screen);
            Object.Destroy(_read);
            Object.Destroy(_red);
            Object.Destroy(_blue);
        }

        static Texture2D Solid(Color32 texel) {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var all = new Color32[16];
            for (int i = 0; i < all.Length; i++) all[i] = texel;
            t.SetPixels32(all);
            t.Apply();
            return t;
        }

        // The mean colour of what is drawn over the black background, after two frames.
        IEnumerator Drawn(System.Action<Color> done) {
            for (int i = 0; i < 2; i++) {
                yield return null;
                ModelBridge.SceneCamera.Render();
            }
            var was = RenderTexture.active;
            RenderTexture.active = _screen;
            _read.ReadPixels(new Rect(0, 0, 160, 160), 0, 0);
            RenderTexture.active = was;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in _read.GetPixels()) {
                if (p.r + p.g + p.b < 0.02f) continue;
                sum += new Vector3(p.r, p.g, p.b);
                n++;
            }
            Assert.That(n, Is.GreaterThan(200), "the actor was not drawn, so no colour was read");
            done(new Color(sum.x / n, sum.y / n, sum.z / n));
        }

        [UnityTest]
        public IEnumerator ATexture_ReplacesTheModelsOwn_AndNullGivesItBack() {
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            Color plain = default, red = default, again = default;
            yield return Drawn(c => plain = c);
            Assert.That(plain.g, Is.GreaterThan(0.2f), "the fixture must have green to lose");

            Assert.That(ModelBridge.SetTexture(actor, _red), Is.EqualTo(""));
            Assert.That(ModelBridge.TextureOf(actor), Is.Not.Null);
            yield return Drawn(c => red = c);
            // What is left is the light its surface mirrors, which is no part of its texture.
            Assert.That(red.g, Is.LessThan(plain.g * 0.3f), $"wearing a red texture, it still drew green: {red} against {plain}");
            Assert.That(red.b, Is.LessThan(plain.b * 0.3f));

            Assert.That(ModelBridge.SetTexture(actor, null), Is.EqualTo(""));
            Assert.That(ModelBridge.TextureOf(actor), Is.Null);
            yield return Drawn(c => again = c);
            Assert.That(again.g, Is.EqualTo(plain.g).Within(0.02f), "null draws the model's own texture again");
        }

        [UnityTest]
        public IEnumerator ActorsWearingOneTexture_ShareItsCopyAndMaterial_AndLeaveNothingBehind() {
            int model = 0;
            yield return Load(m => model = m);
            int copiesBefore = ModelBridge.GroundCopyCount;
            int a = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int b = ModelBridge.Spawn(model, 2, 0, 0, 0, 1, true, true);
            int c = ModelBridge.Spawn(model, 4, 0, 0, 0, 1, true, true);
            Assert.That(ModelBridge.SetTexture(a, _red), Is.EqualTo(""));
            Assert.That(ModelBridge.SetTexture(b, _red), Is.EqualTo(""));
            int shared = ModelBridge.WornMaterialCount;
            Assert.That(shared, Is.GreaterThan(0));
            Assert.That(ModelBridge.GroundCopyCount, Is.EqualTo(copiesBefore + 1), "one copy of a texture however many wear it");
            var ra = ModelBridge.ActorObject(a).GetComponentInChildren<Renderer>();
            var rb = ModelBridge.ActorObject(b).GetComponentInChildren<Renderer>();
            var rc = ModelBridge.ActorObject(c).GetComponentInChildren<Renderer>();
            Assert.That(ra.sharedMaterial, Is.SameAs(rb.sharedMaterial), "two actors wearing one texture draw with one material");
            Assert.That(ra.sharedMaterial, Is.Not.SameAs(rc.sharedMaterial), "an actor wearing none keeps its model's");
            Assert.That(ra.HasPropertyBlock(), Is.False, "a property block takes the actor out of the SRP Batcher");

            Assert.That(ModelBridge.SetTexture(c, _blue), Is.EqualTo(""));
            Assert.That(ModelBridge.WornMaterialCount, Is.EqualTo(shared * 2));
            Assert.That(ModelBridge.GroundCopyCount, Is.EqualTo(copiesBefore + 2));

            ModelBridge.Destroy(a);
            Assert.That(ModelBridge.WornMaterialCount, Is.EqualTo(shared * 2), "b still wears the red one");
            ModelBridge.Destroy(b);
            Assert.That(ModelBridge.WornMaterialCount, Is.EqualTo(shared), "the last to let go of red frees its material");
            Assert.That(ModelBridge.GroundCopyCount, Is.EqualTo(copiesBefore + 1), "and its copy");
            ModelBridge.DisposeAll();
            Assert.That(ModelBridge.WornMaterialCount, Is.EqualTo(0));
            Assert.That(ModelBridge.GroundCopyCount, Is.EqualTo(copiesBefore));
            yield return null;
        }
    }
}
