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
    /// An actor attached to another's joint (ModelBridge.Attach.cs): it rides the joint through a
    /// clip, comes off where it stood, goes when what carries it goes, and seeing it counts as
    /// seeing what carries it, so the pose under it is live and its clip's time is not run twice.
    /// </summary>
    public class AttachedActorPlayModeTests {
        static string Fixture(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", name);

        static IEnumerator Load(string name, System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture(name));
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        RenderTexture _screen;

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            if (_screen != null) Object.Destroy(_screen);
            _screen = null;
        }

        [UnityTest]
        public IEnumerator AnAttachedActor_RidesTheJoint_AndComesOffWhereItStood() {
            int body = 0, hatModel = 0;
            yield return Load("reach.glb", m => body = m);
            yield return Load("reach-body.glb", m => hatModel = m);
            Assert.That(ModelBridge.Joints(body), Is.EqualTo("root\narm"));
            ModelBridge.BeginScene();
            int tom = ModelBridge.Spawn(body, 10, 0, 0, 0, 1, false, false);
            int hat = ModelBridge.Spawn(hatModel, 0, 0, 0, 0, 1, false, false);
            Assert.That(ModelBridge.Attach(hat, tom, "elbow", 0, 0, 0, 0, 0, 0, 1), Does.Contain("no joint named elbow"));
            Assert.That(ModelBridge.Attach(tom, tom, "arm", 0, 0, 0, 0, 0, 0, 1), Does.Contain("itself"));
            Assert.That(ModelBridge.Attach(hat, tom, "arm", 0, 0.5f, 0, 0, 0, 0, 0.5f), Is.EqualTo(""));
            Assert.That(ModelBridge.Attach(tom, hat, "arm", 0, 0, 0, 0, 0, 0, 1), Does.Contain("attached to it"));
            var t = ModelBridge.ActorObject(hat).transform;
            // The arm stands at y = 1 on a body at x = 10.
            Assert.That(Vector3.Distance(t.position, new Vector3(10, 1.5f, 0)), Is.LessThan(0.001f));
            Assert.That(t.lossyScale.x, Is.EqualTo(0.5f).Within(0.001f));

            var anim = ModelBridge.ActorObject(tom).GetComponentInChildren<Animation>(true);
            Assert.That(ModelBridge.Play(tom, "Reach", false, 0), Is.True);
            anim["Reach"].time = 1;
            anim.Sample();
            // Reach carries the arm 4 to glTF's +x, Unity's -x.
            Assert.That(Vector3.Distance(t.position, new Vector3(6, 1.5f, 0)), Is.LessThan(0.01f), $"the hat is at {t.position}");

            // Its place is measured from the joint while it is attached.
            ModelBridge.Move(hat, 0, 1, 0);
            Assert.That(Vector3.Distance(t.position, new Vector3(6, 2, 0)), Is.LessThan(0.01f));

            var where = ModelBridge.Detach(hat);
            Assert.That(ModelBridge.AttachedTo(hat), Is.EqualTo(0));
            Assert.That(Vector3.Distance(t.position, new Vector3(6, 2, 0)), Is.LessThan(0.01f), "detached, it stays where it was");
            Assert.That(new Vector3(where[0], where[1], where[2]), Is.EqualTo(t.localPosition));
            Assert.That(where[7], Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ModelBridge.Detach(hat), Is.Null);
            anim["Reach"].time = 0;
            anim.Sample();
            Assert.That(Vector3.Distance(t.position, new Vector3(6, 2, 0)), Is.LessThan(0.01f), "and no longer rides the joint");
        }

        [UnityTest]
        public IEnumerator WhatIsAttached_GoesWithWhatCarriesIt() {
            int body = 0;
            yield return Load("reach.glb", m => body = m);
            ModelBridge.BeginScene();
            int tom = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            int hat = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            int feather = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            int loose = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            ModelBridge.Attach(hat, tom, "arm", 0, 0, 0, 0, 0, 0, 1);
            ModelBridge.Attach(feather, hat, "arm", 0, 0, 0, 0, 0, 0, 1);
            ModelBridge.Attach(loose, tom, "arm", 0, 0, 0, 0, 0, 0, 1);
            ModelBridge.Detach(loose);
            ModelBridge.Destroy(tom);
            yield return null;
            Assert.That(ModelBridge.ActorObject(hat), Is.Null);
            Assert.That(ModelBridge.ActorObject(feather), Is.Null);
            Assert.That(ModelBridge.ActorObject(loose) != null, "one detached first is its own again");
        }

        [UnityTest]
        public IEnumerator APart_IsHiddenAndShownByItsNodesName_OnOneActorAlone() {
            int cubes = 0;
            yield return Load("cubes.glb", m => cubes = m);
            Assert.That(ModelBridge.Parts(cubes), Is.EqualTo("A\nB"));
            ModelBridge.BeginScene();
            int one = ModelBridge.Spawn(cubes, 0, 0, 0, 0, 1, true, true);
            int two = ModelBridge.Spawn(cubes, 3, 0, 0, 0, 1, true, true);
            Renderer Part(int actor, string name) {
                foreach (var r in ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name == name) return r;
                return null;
            }
            Assert.That(ModelBridge.ShowPart(one, "B", false), Is.True);
            Assert.That(Part(one, "B").enabled, Is.False);
            Assert.That(Part(one, "A").enabled, Is.True);
            Assert.That(Part(two, "B").enabled, Is.True, "another actor of the model keeps its part");
            Assert.That(ModelBridge.ShowPart(one, "B", true), Is.True);
            Assert.That(Part(one, "B").enabled, Is.True);
            Assert.That(ModelBridge.ShowPart(one, "C", false), Is.False);

            // What is attached keeps its own parts: hiding the wearer's B leaves the hat's.
            int body = 0;
            yield return Load("reach.glb", m => body = m);
            int tom = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            Assert.That(ModelBridge.Attach(one, tom, "arm", 0, 0, 0, 0, 0, 0, 1), Is.EqualTo(""));
            Assert.That(ModelBridge.ShowPart(tom, "B", false), Is.False);
            Assert.That(Part(one, "B").enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator SeeingWhatIsAttached_IsSeeingWhatCarriesIt() {
            int body = 0;
            yield return Load("reach.glb", m => body = m);
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(0, 1, 0, 0, 1, 10, 60);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 1, 1, 1, false);
            _screen = new RenderTexture(320, 180, 24);
            ModelBridge.SceneCamera.targetTexture = _screen;
            // Tom stands 60 behind the camera, with a hat on his arm 70 in front of him.
            int tom = ModelBridge.Spawn(body, 0, 0, -60, 0, 1, false, false);
            int hat = ModelBridge.Spawn(body, 0, 0, 0, 0, 1, false, false);
            // Taken before the hat is under him, after which a search of his children finds the hat's too.
            var tomSkin = ModelBridge.ActorObject(tom).GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(ModelBridge.Attach(hat, tom, "arm", 0, 0, 70, 0, 0, 0, 1), Is.EqualTo(""));
            Assert.That(ModelBridge.Play(tom, "Turn", false, 0), Is.True);
            var go = ModelBridge.ActorObject(tom);
            var anim = go.GetComponentInChildren<Animation>(true);
            var arm = ModelBridge.ActorObject(hat).transform.parent;
            for (int i = 0; i < 3; i++) { yield return null; ModelBridge.SceneCamera.Render(); }
            Assert.That(tomSkin.isVisible, Is.False, "the control: Tom himself is not seen");
            Assert.That(ModelBridge.ActorObject(hat).GetComponentInChildren<SkinnedMeshRenderer>(true).isVisible, Is.True, "the control: the hat is");

            var turn = arm.localRotation;
            float time = anim["Turn"].time, clock = Time.time;
            while (Time.time < clock + 0.4f) { yield return null; ModelBridge.SceneCamera.Render(); }
            float ran = anim["Turn"].time - time, passed = Time.time - clock;
            Assert.That(Quaternion.Angle(turn, arm.localRotation), Is.GreaterThan(5f), "the joint the hat hangs on holds a stale pose while the hat is on screen");
            Assert.That(ran, Is.EqualTo(passed).Within(passed * 0.3f), $"the clip ran {ran:0.000} s in {passed:0.000} s");
        }
    }
}
