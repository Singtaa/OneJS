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
    /// Shine (ModelBridge.Shine.cs), as it is drawn: the add picture brightens a model given
    /// "add", the multiply picture darkens or keeps one given "multiply" by twice its bytes, a
    /// mode with no picture shines nothing, an actor's own share scales it without a property
    /// block, and the picture's alpha says where. Skinned on the graphics card and on the CPU
    /// alike, since the fixture is a skin. Each reading is the mean colour of the pixels an actor
    /// covers, lit by a white ambient alone, against the same actor without shine.
    /// </summary>
    public class ShinePlayModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", "reach.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        RenderTexture _screen;
        Texture2D _read;
        readonly System.Collections.Generic.List<Texture2D> _pictures = new();
        bool _gpuWas;

        [SetUp]
        public void Stage() {
            _gpuWas = ModelBridge.GpuSkinning;
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(0, 1, -6, 0, 1, 0, 40);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 1, 1, 1, false);
            ModelBridge.SetAmbient(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 1);
            ModelBridge.SetBackground(0, 0, 0);
            _screen = new RenderTexture(160, 160, 24);
            ModelBridge.SceneCamera.targetTexture = _screen;
            _read = new Texture2D(160, 160, TextureFormat.RGBA32, false);
        }

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            ModelBridge.GpuSkinning = _gpuWas;
            Object.Destroy(_screen);
            Object.Destroy(_read);
            foreach (var p in _pictures) Object.Destroy(p);
            _pictures.Clear();
        }

        // A picture one value all over, as bytes.
        Texture2D Flat(byte value, byte alpha = 255) {
            var picture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var texel = new Color32(value, value, value, alpha);
            var texels = new Color32[16];
            for (int i = 0; i < texels.Length; i++) texels[i] = texel;
            picture.SetPixels32(texels);
            picture.Apply();
            _pictures.Add(picture);
            return picture;
        }

        // The mean brightness of what is drawn over the black background, after two frames.
        IEnumerator Drawn(System.Action<float> done) {
            for (int i = 0; i < 2; i++) {
                yield return null;
                ModelBridge.SceneCamera.Render();
            }
            var was = RenderTexture.active;
            RenderTexture.active = _screen;
            _read.ReadPixels(new Rect(0, 0, 160, 160), 0, 0);
            RenderTexture.active = was;
            float sum = 0;
            int n = 0;
            foreach (var p in _read.GetPixels()) {
                if (p.r + p.g + p.b < 0.02f) continue;
                sum += (p.r + p.g + p.b) / 3;
                n++;
            }
            Assert.That(n, Is.GreaterThan(200), "the actor was not drawn, so nothing was read");
            done(sum / n);
        }

        [UnityTest]
        public IEnumerator Add_BrightensTheModel_OnlyOnceTheSceneHasItsPicture([Values(true, false)] bool gpu) {
            ModelBridge.GpuSkinning = gpu;
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            float plain = 0, noPicture = 0, shone = 0, off = 0;
            yield return Drawn(v => plain = v);

            Assert.That(ModelBridge.SetModelShine(model, "add", 1), Is.EqualTo(""));
            yield return Drawn(v => noPicture = v);
            Assert.That(noPicture, Is.EqualTo(plain).Within(0.01f), "a mode with no picture shines nothing");

            ModelBridge.SetShinePictures(Flat(128), null);
            yield return Drawn(v => shone = v);
            Assert.That(shone, Is.GreaterThan(plain + 0.1f), $"with gpu {gpu}, an add picture of mid grey brightened it to {shone} from {plain}");

            Assert.That(ModelBridge.SetModelShine(model, "none", 1), Is.EqualTo(""));
            yield return Drawn(v => off = v);
            Assert.That(off, Is.EqualTo(plain).Within(0.01f));
            Assert.That(ModelBridge.ActorObject(actor), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Multiply_KeepsTheModelAtMidGrey_AndDarkensItBelow() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            float plain = 0, grey = 0, dark = 0;
            yield return Drawn(v => plain = v);
            Assert.That(ModelBridge.SetModelShine(model, "multiply", 1), Is.EqualTo(""));

            // Its bytes are read as they are, so 128 is one half and twice it is the model as it was.
            ModelBridge.SetShinePictures(null, Flat(128));
            yield return Drawn(v => grey = v);
            Assert.That(grey, Is.EqualTo(plain).Within(0.03f), $"twice a mid grey is no change: {grey} against {plain}");

            ModelBridge.SetShinePictures(null, Flat(32));
            yield return Drawn(v => dark = v);
            Assert.That(dark, Is.LessThan(plain * 0.7f), $"a dark multiply picture left it at {dark} from {plain}");
        }

        [UnityTest]
        public IEnumerator AnActorsShare_ScalesItsShine_AndKeepsItBatching([Values(true, false)] bool gpu) {
            ModelBridge.GpuSkinning = gpu;
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            float plain = 0, none = 0, half = 0, all = 0;
            yield return Drawn(v => plain = v);
            ModelBridge.SetShinePictures(Flat(160), null);
            Assert.That(ModelBridge.SetModelShine(model, "add", 1), Is.EqualTo(""));
            yield return Drawn(v => all = v);

            Assert.That(ModelBridge.SetActorShine(actor, 0), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.Not.EqualTo(0), "an actor with a share of its own takes a colour row");
            yield return Drawn(v => none = v);
            Assert.That(none, Is.EqualTo(plain).Within(0.01f), "an actor's share of 0 shines nothing");

            Assert.That(ModelBridge.SetActorShine(actor, 0.5f), Is.EqualTo(""));
            yield return Drawn(v => half = v);
            Assert.That(half, Is.GreaterThan(plain + 0.03f).And.LessThan(all - 0.03f), $"half its share drew {half}, between {plain} and {all}");

            foreach (var r in ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>(true)) {
                Assert.That(r.HasPropertyBlock(), Is.False, "a property block takes the actor out of the SRP Batcher");
            }

            // A tint keeps the share, and a share of 1 with a white tint gives the row back.
            Assert.That(ModelBridge.SetTint(actor, 1, 1, 1), Is.EqualTo(""));
            Assert.That(ModelBridge.ActorShine(actor), Is.EqualTo(0.5f));
            Assert.That(ModelBridge.SetActorShine(actor, 1), Is.EqualTo(""));
            Assert.That(ModelBridge.ColorRow(actor), Is.EqualTo(0));
            Assert.That(ModelBridge.SetActorShine(actor, 2), Does.Contain("runs from 0 to 1"));
        }

        [UnityTest]
        public IEnumerator ThePicturesAlpha_SaysWhereItShines() {
            int model = 0;
            yield return Load(m => model = m);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            ModelBridge.SetShinePictures(Flat(160), null);
            Assert.That(ModelBridge.SetModelShine(model, "add", 1), Is.EqualTo(""));
            float bare = 0, masked = 0, whole = 0;
            Assert.That(ModelBridge.SetTexture(actor, Flat(128, 0)), Is.EqualTo(""));
            yield return Drawn(v => masked = v);
            Assert.That(ModelBridge.SetTexture(actor, Flat(128, 255)), Is.EqualTo(""));
            yield return Drawn(v => whole = v);
            Assert.That(ModelBridge.SetModelShine(model, "none", 0), Is.EqualTo(""));
            yield return Drawn(v => bare = v);
            Assert.That(masked, Is.EqualTo(bare).Within(0.01f), $"where the picture's alpha is 0 nothing shines: {masked} against {bare}");
            Assert.That(whole, Is.GreaterThan(bare + 0.1f), $"where it is 1 it all shines: {whole} against {bare}");
        }

        [Test]
        public void AModeOrStrengthItDoesNotTake_IsRefused() {
            Assert.That(ModelBridge.SetModelShine(12345, "add", 1), Is.EqualTo("there is no such model"));
            Assert.That(ModelBridge.SetMeshShine(12345, "add", 1), Does.Contain("no mesh model"));
            int mesh = ModelBridge.CreateMesh(true, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMeshShine(mesh, "gold", 1), Does.Contain("is \"add\" or \"multiply\""));
            Assert.That(ModelBridge.SetMeshShine(mesh, "add", 1.5f), Does.Contain("runs from 0 to 1"));
            Assert.That(ModelBridge.SetMeshShine(mesh, "add", float.NaN), Does.Contain("runs from 0 to 1"));
            Assert.That(ModelBridge.SetMeshShine(mesh, "multiply", 0.5f), Is.EqualTo(""));
        }
    }
}
