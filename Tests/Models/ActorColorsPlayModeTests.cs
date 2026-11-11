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
    /// An actor's own colours (ModelBridge.Colors.cs), as they are drawn: a tint multiplies the
    /// whole actor, a colour multiplies it where the model's mask says, a flash adds to it after
    /// its light, an actor with none of them
    /// draws as it did, and a coloured actor carries no property block, so it stays in the SRP
    /// Batcher. Each reading is the mean colour of the pixels an actor covers, lit by a white
    /// ambient alone, against the same actor uncoloured.
    /// </summary>
    public class ActorColorsPlayModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", "reach.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        RenderTexture _screen;
        Texture2D _read, _mask;

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
        }

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            Object.Destroy(_screen);
            Object.Destroy(_read);
            if (_mask != null) Object.Destroy(_mask);
            _mask = null;
        }

        // A mask that is one colour all over: how much of colours 1 to 4 every texel takes.
        Texture2D Mask(byte r, byte g, byte b, byte a) {
            _mask = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var texel = new Color32(r, g, b, a);
            _mask.SetPixels32(new[] { texel, texel, texel, texel });
            _mask.Apply();
            return _mask;
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
        public IEnumerator ATint_MultipliesTheWholeActor_AndWhiteGivesItBack() {
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            Color plain = default, red = default, again = default;
            yield return Drawn(c => plain = c);
            Assert.That(plain.g, Is.GreaterThan(0.2f), "the fixture must have green to lose");

            Assert.That(ModelBridge.SetTint(actor, 1, 0, 0), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.EqualTo(1));
            yield return Drawn(c => red = c);
            // What is left is the light its surface mirrors, which is no part of its colour.
            Assert.That(red.g, Is.LessThan(plain.g * 0.3f), $"tinted red, it still drew green: {red} against {plain}");
            Assert.That(red.b, Is.LessThan(plain.b * 0.3f));
            Assert.That(red.r, Is.EqualTo(plain.r).Within(0.05f), "red times its own red is itself");

            Assert.That(ModelBridge.SetTint(actor, 1, 1, 1), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.EqualTo(0), "a white actor gives its row back");
            yield return Drawn(c => again = c);
            Assert.That(again.g, Is.EqualTo(plain.g).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator AFlash_AddsToTheLitActor_AndBlackGivesItBack() {
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            Color plain = default, flashed = default, white = default, again = default;
            yield return Drawn(c => plain = c);

            Assert.That(ModelBridge.SetFlash(actor, 0, 0.5f, 0), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.EqualTo(1));
            yield return Drawn(c => flashed = c);
            Assert.That(flashed.g, Is.GreaterThan(plain.g + 0.1f), $"flashed green, it drew no more green: {flashed} against {plain}");
            Assert.That(flashed.r, Is.EqualTo(plain.r).Within(0.05f), "a green flash adds no red");
            Assert.That(flashed.b, Is.EqualTo(plain.b).Within(0.05f), "a green flash adds no blue");

            // Brighter than any tint can make it, which multiplies by at most white.
            Assert.That(ModelBridge.SetFlash(actor, 1, 1, 1), Is.EqualTo(""));
            yield return Drawn(c => white = c);
            Assert.That(white.r + white.g + white.b, Is.GreaterThan(plain.r + plain.g + plain.b + 0.5f), $"flashed white it is {white} against {plain}");

            Assert.That(ModelBridge.SetFlash(actor, 0, 0, 0), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.EqualTo(0), "an actor with no flash, tint or colours gives its row back");
            yield return Drawn(c => again = c);
            Assert.That(again.g, Is.EqualTo(plain.g).Within(0.02f));
            Assert.That(ModelBridge.SetFlash(actor, float.NaN, 0, 0), Does.Contain("is a colour"));
        }

        [UnityTest]
        public IEnumerator Colors_PaintWhereTheMaskSays_AndNowhereElse() {
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            Color plain = default, painted = default, other = default;
            yield return Drawn(c => plain = c);

            Assert.That(ModelBridge.SetColors(actor, new[] { 0f, 0f, 1f }), Does.Contain("without a colorMask"));
            // Every texel is all of colour 2 and none of the others.
            Assert.That(ModelBridge.SetColorMask(model, Mask(0, 255, 0, 0)), Is.EqualTo(""));
            // Colour 2 is blue: the actor loses its red and green. Colour 1 is red, and paints nothing.
            Assert.That(ModelBridge.SetColors(actor, new[] { 1f, 0f, 0f, 0f, 0f, 1f }), Is.EqualTo(""));
            yield return Drawn(c => painted = c);
            Assert.That(painted.r, Is.LessThan(plain.r * 0.3f), $"painted blue through the mask, it still drew red: {painted} against {plain}");
            Assert.That(painted.b, Is.EqualTo(plain.b).Within(0.05f));

            // The same colours through a mask that gives them nothing leave the actor as it was.
            Assert.That(ModelBridge.SetColorMask(model, Mask(0, 0, 0, 0)), Is.EqualTo(""));
            yield return Drawn(c => other = c);
            Assert.That(other.r, Is.EqualTo(plain.r).Within(0.02f));
            Assert.That(other.g, Is.EqualTo(plain.g).Within(0.02f));

            Assert.That(ModelBridge.SetColors(actor, new[] { 1f, 0f }), Does.Contain("three each"));
            Assert.That(ModelBridge.SetColors(actor, new float[15]), Does.Contain("up to 4"));
        }

        [UnityTest]
        public IEnumerator AColouredActor_HasNoPropertyBlock_AndItsRowIsReused() {
            int model = 0;
            yield return Load(m => model = m);
            int a = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, false);
            int b = ModelBridge.Spawn(model, 2, 0, 0, 0, 1, true, true);
            ModelBridge.SetTint(a, 1, 0, 0);
            ModelBridge.SetTint(b, 0, 1, 0);
            Assert.That(ModelBridge.ColorRow(a), Is.EqualTo(1));
            Assert.That(ModelBridge.ColorRow(b), Is.EqualTo(2));
            foreach (var r in ModelBridge.ActorObject(a).GetComponentsInChildren<Renderer>(true)) {
                Assert.That(r.HasPropertyBlock(), Is.False, "a property block takes the actor out of the SRP Batcher");
                uint value = r is SkinnedMeshRenderer s ? s.GetShaderUserValue() : ((MeshRenderer)r).GetShaderUserValue();
                Assert.That(value, Is.EqualTo(1u << 1 | 1u), "row 1 above the bit that says it keeps no shadow");
            }
            ModelBridge.Destroy(a);
            int c = ModelBridge.Spawn(model, 4, 0, 0, 0, 1, true, true);
            ModelBridge.SetTint(c, 0, 0, 1);
            Assert.That(ModelBridge.ColorRow(c), Is.EqualTo(1), "a destroyed actor's row goes to the next");
            ModelBridge.UploadColors();
            Assert.That((Color)ModelBridge.ActorColors.GetPixel(0, 1), Is.EqualTo(Color.blue), "and holds the new actor's colours, not the old one's");
        }
    }
}
