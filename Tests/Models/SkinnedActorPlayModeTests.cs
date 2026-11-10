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
    /// What a skinned actor costs while nobody sees it (ModelBridge.Skin.cs): its mesh is not
    /// deformed off screen, its clips run their time without moving its joints, and the fixed
    /// bounds Unity culls it by hold every pose its clips reach, so one whose clip carries it
    /// into view from outside is still drawn. The fixture is reach.glb: `Reach` carries the upper
    /// box 4 units to the side (glTF's +x, which is Unity's -x), `Turn` turns it.
    /// </summary>
    public class SkinnedActorPlayModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", "reach.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
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

        RenderTexture _screen;

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            if (_screen != null) Object.Destroy(_screen);
            _screen = null;
        }

        // The scene's camera, drawing into a texture of its own: a test run in batch mode draws no
        // frame by itself, and what is seen is only known from a camera's culling.
        Camera Screen(float x, float y, float z) {
            ModelBridge.SetCamera(x, y, z, x, y, z + 10, 60);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 1, 1, 1, false);
            _screen = new RenderTexture(320, 180, 24);
            ModelBridge.SceneCamera.targetTexture = _screen;
            return ModelBridge.SceneCamera;
        }

        // Frames for `seconds`, each drawn.
        static IEnumerator Draw(float seconds) {
            float until = Time.time + seconds;
            do {
                yield return null;
                ModelBridge.SceneCamera.Render();
            } while (Time.time < until);
        }

        [UnityTest]
        public IEnumerator TheBounds_HoldEveryPoseOfEveryClip() {
            int model = 0;
            yield return Load(m => model = m);
            var go = ModelBridge.ActorObject(ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false));
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var anim = go.GetComponentInChildren<Animation>(true);
            Assert.That(skin.updateWhenOffscreen, Is.False, "an actor off screen would be deformed and uploaded every frame");
            Assert.That(anim.cullingType, Is.EqualTo(AnimationCullingType.BasedOnRenderers));

            var root = skin.rootBone != null ? skin.rootBone : skin.transform;
            var bounds = skin.localBounds;
            // The control: the mesh as it stands ends at x = -0.25, so bounds taken from it would fail below.
            Assert.That(skin.sharedMesh.bounds.min.x, Is.GreaterThan(-1f));
            Assert.That(bounds.min.x, Is.LessThanOrEqualTo(-4.25f), "Reach carries the upper box to x = -4.25");

            var baked = new Mesh();
            int clips = 0;
            float furthest = 0;
            foreach (AnimationState state in anim) {
                clips++;
                for (int k = 0; k <= 20; k++) {
                    state.clip.SampleAnimation(anim.gameObject, state.clip.length * k / 20);
                    skin.BakeMesh(baked, true);
                    foreach (var v in baked.vertices) {
                        var p = root.InverseTransformPoint(skin.transform.TransformPoint(v));
                        furthest = Mathf.Max(furthest, -p.x);
                        Assert.That(bounds.Contains(p), $"{state.name} at {k / 20f:0.00} puts a vertex at {p}, outside {bounds}");
                    }
                }
            }
            Object.Destroy(baked);
            Assert.That(clips, Is.EqualTo(2));
            Assert.That(furthest, Is.GreaterThan(4f), "no pose left the mesh's own box, so nothing was checked");
        }

        [UnityTest]
        public IEnumerator AnActorItsClipCarriesIntoView_IsDrawn() {
            int model = 0;
            yield return Load(m => model = m);
            var camera = Screen(0, 1, -10);
            float half = Mathf.Tan(30 * Mathf.Deg2Rad) * 10 * camera.aspect;
            // As it stands the actor is 2 units outside the right edge; Reach's end is 2 inside it.
            int actor = ModelBridge.Spawn(model, half + 2, 0, 0, 0, 1, false, false);
            var skin = ModelBridge.ActorObject(actor).GetComponentInChildren<SkinnedMeshRenderer>(true);
            var kept = skin.localBounds;

            // With bounds no bigger than the mesh as it stands, it is culled: this is what the fixed
            // bounds prevent, and it shows the camera and the placement can tell the two apart.
            skin.localBounds = skin.sharedMesh.bounds;
            yield return Draw(0.05f);
            Assert.That(skin.isVisible, Is.False, "the mesh's own box is on screen, so this placement proves nothing");

            skin.localBounds = kept;
            Assert.That(ModelBridge.Play(actor, "Reach", false, 0), Is.True);
            yield return Draw(1.2f);
            Assert.That(skin.isVisible, Is.True, "its clip carried it into view and it was culled by its bounds");
            var arm = Find(ModelBridge.ActorObject(actor).transform, "arm");
            Assert.That(arm.localPosition.x, Is.EqualTo(-4f).Within(0.01f), "seen, its clip was sampled to the end");
        }

        [UnityTest]
        public IEnumerator AnUnseenActor_KeepsItsJointsWhileItsClipRuns() {
            int model = 0;
            yield return Load(m => model = m);
            Screen(0, 1, -10);
            // Behind the camera.
            int actor = ModelBridge.Spawn(model, 0, 0, -40, 0, 1, false, false);
            var go = ModelBridge.ActorObject(actor);
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var anim = go.GetComponentInChildren<Animation>(true);
            var arm = Find(go.transform, "arm");
            Assert.That(ModelBridge.Play(actor, "Turn", true, 0), Is.True);
            var began = arm.localRotation;

            yield return Draw(0.4f);
            Assert.That(skin.isVisible, Is.False);
            Assert.That(anim["Turn"].time, Is.GreaterThan(0.2f), "an unseen actor's clip keeps its time");
            Assert.That(Quaternion.Angle(arm.localRotation, began), Is.LessThan(0.01f), "an unseen actor's joints were sampled: it costs a frame again");

            // Seen, it takes the pose its clip has reached, not the one it was left in.
            ModelBridge.Move(actor, 0, 0, 0);
            float angle = 0;
            for (int i = 0; i < 10 && angle < 1; i++) {
                yield return Draw(0);
                angle = Quaternion.Angle(arm.localRotation, began);
            }
            Assert.That(skin.isVisible, Is.True);
            Assert.That(angle, Is.GreaterThan(1f), "seen again, its joints follow its clip");
        }
    }
}
