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
    /// A model playing another's clips (ModelBridge.TakeClips). reach-body.glb is reach.glb
    /// without its clips, and reach-other.glb is the same body with its upper joint renamed.
    /// </summary>
    public class TakenClipsPlayModeTests {
        static string Fixture(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", name);

        static IEnumerator Load(string name, System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture(name));
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        static Transform Find(Transform t, string name) {
            if (t.name == name) return t;
            foreach (Transform child in t) {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        [TearDown]
        public void Clean() => ModelBridge.DisposeAll();

        [UnityTest]
        public IEnumerator ABodyWithoutClips_PlaysAnothers_AndItsBoundsHoldTheirReach() {
            int moves = 0, body = 0;
            yield return Load("reach.glb", m => moves = m);
            yield return Load("reach-body.glb", m => body = m);
            Assert.That(ModelBridge.Clips(body), Is.EqualTo(""), "the control: the body has no clips of its own");
            Assert.That(ModelBridge.TakeClips(body, moves), Is.EqualTo(""));
            Assert.That(ModelBridge.Clips(body), Is.EqualTo(ModelBridge.Clips(moves)));

            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            var go = ModelBridge.ActorObject(actor);
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skin.localBounds.min.x, Is.LessThanOrEqualTo(-4.25f), "Reach carries the upper box to x = -4.25, which only the taken clips say");
            var arm = Find(go.transform, "arm");
            float before = arm.localPosition.x;
            Assert.That(ModelBridge.Play(actor, "Reach", false, 0), Is.True);
            go.GetComponentInChildren<Animation>(true)["Reach"].time = 1;
            go.GetComponentInChildren<Animation>(true).Sample();
            Assert.That(Mathf.Abs(arm.localPosition.x - before), Is.GreaterThan(3.5f), "the taken clip moves the body's own joint");

            // One AnimationClip, held once: the body's is the giver's very object.
            var given = ModelBridge.ActorObject(ModelBridge.Spawn(moves, 5, 0, 0, 0, 1, false, false)).GetComponentInChildren<Animation>(true);
            Assert.That(go.GetComponentInChildren<Animation>(true).GetClip("Reach"), Is.SameAs(given.GetClip("Reach")));
        }

        [UnityTest]
        public IEnumerator TheGiver_StaysLoaded_WhileAnotherPlaysItsClips() {
            int moves = 0, body = 0;
            yield return Load("reach.glb", m => moves = m);
            yield return Load("reach-body.glb", m => body = m);
            Assert.That(ModelBridge.TakeClips(body, moves), Is.EqualTo(""));
            Assert.That(ModelBridge.Unload(moves), Is.False);
            Assert.That(ModelBridge.Unload(body), Is.True);
            Assert.That(ModelBridge.Unload(moves), Is.True);
        }

        [UnityTest]
        public IEnumerator ABodyMissingAJoint_IsRefused_ByTheJointsName() {
            int moves = 0, other = 0, body = 0;
            yield return Load("reach.glb", m => moves = m);
            yield return Load("reach-other.glb", m => other = m);
            yield return Load("reach-body.glb", m => body = m);
            Assert.That(ModelBridge.TakeClips(other, moves), Does.Contain("no joint arm"));
            Assert.That(ModelBridge.Clips(other), Is.EqualTo(""));
            Assert.That(ModelBridge.TakeClips(moves, body), Does.Contain("has no clips"));
            Assert.That(ModelBridge.TakeClips(moves, moves), Does.Contain("its own clips"));
            ModelBridge.BeginScene();
            ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            Assert.That(ModelBridge.TakeClips(body, moves), Does.Contain("already has actors"));
        }
    }
}
