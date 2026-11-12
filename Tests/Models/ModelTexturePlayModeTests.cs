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
    /// What a cart dressing a body reads off its model (socket.glb): the picture its file carries
    /// (ModelBridge.ModelTexture), which an actor wearing a picture of its own leaves alone; and
    /// which of the file's nodes an actor can be hung on, which is the bones of its skin, a bone
    /// with no weights included, and no other node.
    /// </summary>
    public class ModelTexturePlayModeTests {
        static string Fixture(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", name);

        static IEnumerator Load(string name, System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture(name));
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        [TearDown]
        public void Clean() => ModelBridge.DisposeAll();

        // A texture's texel (0, 0) as stored, read through a copy, since a loaded model's picture
        // keeps no copy a script can read.
        static Color32 Texel(Texture t) {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var texel = read.GetPixels32()[0];
            Object.Destroy(read);
            return texel;
        }

        [UnityTest]
        public IEnumerator AModel_HandsBackThePictureItsFileCarries() {
            int model = 0, plain = 0;
            yield return Load("socket.glb", m => model = m);
            yield return Load("reach.glb", m => plain = m);
            var own = ModelBridge.ModelTexture(model);
            Assert.That(own, Is.Not.Null, "socket.glb carries a picture");
            Assert.That(own.width, Is.EqualTo(8));
            Assert.That(own.height, Is.EqualTo(8));
            var texel = Texel(own);
            // socket.mjs paints every texel (40, 200, 90).
            Assert.That(texel.r, Is.EqualTo(40).Within(1), $"read {texel}");
            Assert.That(texel.g, Is.EqualTo(200).Within(1), $"read {texel}");
            Assert.That(texel.b, Is.EqualTo(90).Within(1), $"read {texel}");

            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            var red = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Assert.That(ModelBridge.SetTexture(actor, red), Is.EqualTo(""));
            Assert.That(ModelBridge.ModelTexture(model), Is.SameAs(own), "an actor wearing its own picture leaves the model's");
            Object.Destroy(red);

            Assert.That(ModelBridge.ModelTexture(plain), Is.Null, "reach.glb is coloured by a factor and carries no picture");
            Assert.That(ModelBridge.ModelTexture(-1), Is.Null, "no model");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnActor_HangsOnABoneOfTheSkin_WeightedOrNot_AndOnNoOtherNode() {
            int model = 0, hatModel = 0;
            yield return Load("socket.glb", m => model = m);
            yield return Load("reach.glb", m => hatModel = m);
            Assert.That(ModelBridge.Joints(model), Is.EqualTo("root\narm\ngrip"), "grip carries no weights and is a joint; back is no bone of the skin");
            ModelBridge.BeginScene();
            int body = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            int held = ModelBridge.Spawn(hatModel, 0, 0, 0, 0, 1, false, false);
            Assert.That(ModelBridge.Attach(held, body, "back", 0, 0, 0, 0, 0, 0, 1), Does.Contain("no joint named back"));
            Assert.That(ModelBridge.Attach(held, body, "grip", 0, 0, 0, 0, 0, 0, 1), Is.EqualTo(""));
            // grip stands 0.5 to glTF's +x of arm, which is Unity's -x, and 1.5 up.
            var at = ModelBridge.ActorObject(held).transform.position;
            Assert.That(Vector3.Distance(at, new Vector3(-0.5f, 1.5f, 0)), Is.LessThan(0.001f), $"held at {at}");
            yield return null;
        }
    }
}
