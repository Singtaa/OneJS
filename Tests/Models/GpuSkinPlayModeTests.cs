using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// A skinned actor skinned on the graphics card (ModelBridge.GpuSkin.cs): its renderer is a
    /// MeshRenderer that ModelLit's skin variant poses from the joint palette, drawn where the same
    /// actor skinned on the CPU is drawn, culled by bounds that hold its clips' reach, and an actor
    /// whose pose does not move writes nothing to the palette. The fixture is reach.glb: `Reach`
    /// carries the upper box 4 units to the side (glTF's +x, which is Unity's -x).
    /// </summary>
    public class GpuSkinPlayModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", "reach.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        RenderTexture _screen;

        // Off is the default (TheDefault_IsTheCpu), so every case here turns it on.
        [SetUp]
        public void OnTheGpu() => ModelBridge.GpuSkinning = true;

        [TearDown]
        public void Clean() {
            ModelBridge.DisposeAll();
            ModelBridge.GpuSkinning = false;
            if (_screen != null) Object.Destroy(_screen);
            _screen = null;
        }

        // The graphics card path needs URP and a float texture: say so rather than pass on nothing.
        static void RequireGpuSkins() {
            if (GraphicsSettings.currentRenderPipeline == null) Assert.Ignore("the built-in pipeline keeps skins on the CPU");
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)) Assert.Ignore("no float textures: skins stay on the CPU");
        }

        Camera Screen(float x, float y, float z) {
            ModelBridge.SetCamera(x, y, z, x, y, z + 10, 60);
            ModelBridge.SetSun(false, 0, -1, 1, 1, 1, 1, 1, false);
            _screen = new RenderTexture(320, 180, 24);
            ModelBridge.SceneCamera.targetTexture = _screen;
            return ModelBridge.SceneCamera;
        }

        static IEnumerator Draw(int frames) {
            for (int i = 0; i < frames; i++) {
                yield return null;
                ModelBridge.SceneCamera.Render();
            }
        }

        // Which pixels differ from the corner's, the camera's clear colour.
        bool[] Covered() {
            var was = RenderTexture.active;
            RenderTexture.active = _screen;
            var read = new Texture2D(_screen.width, _screen.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, _screen.width, _screen.height), 0, 0);
            read.Apply();
            RenderTexture.active = was;
            var pixels = read.GetPixels32();
            Object.Destroy(read);
            var clear = pixels[0];
            var covered = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) {
                var p = pixels[i];
                covered[i] = Mathf.Abs(p.r - clear.r) + Mathf.Abs(p.g - clear.g) + Mathf.Abs(p.b - clear.b) > 12;
            }
            return covered;
        }

        static (int differ, int either) Compare(bool[] a, bool[] b) {
            int differ = 0, either = 0;
            for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) differ++;
                if (a[i] || b[i]) either++;
            }
            return (differ, either);
        }

        // An actor held at the end of Reach, the arm 4 units to the side, drawn for a few frames.
        IEnumerator Reached(int model, bool gpu, System.Action<int> made) {
            ModelBridge.GpuSkinning = gpu;
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 180, 1, false, false);
            Assert.That(ModelBridge.Play(actor, "Reach", false, 0), Is.True);
            var anim = ModelBridge.ActorObject(actor).GetComponentInChildren<Animation>(true);
            ModelBridge.SetSpeed(actor, 0);
            anim["Reach"].time = anim["Reach"].length;
            yield return Draw(4);
            made(actor);
        }

        [UnityTest]
        public IEnumerator ASkinnedActor_IsPosedOnTheGraphicsCard_WhereTheCpuPosesIt() {
            RequireGpuSkins();
            int model = 0;
            yield return Load(m => model = m);
            Screen(0, 1, -10);

            int gpu = 0;
            yield return Reached(model, true, a => gpu = a);
            var go = ModelBridge.ActorObject(gpu);
            Assert.That(go.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Null, "the skin stayed on the CPU");
            Assert.That(go.GetComponentInChildren<MeshRenderer>(true).sharedMaterial.IsKeywordEnabled("_OJ_SKIN"), Is.True);
            Assert.That(ModelBridge.GpuSkinCount, Is.EqualTo(1));
            var onGpu = Covered();
            ModelBridge.Destroy(gpu);
            Assert.That(ModelBridge.GpuSkinCount, Is.EqualTo(0));

            int cpu = 0;
            yield return Reached(model, false, a => cpu = a);
            Assert.That(ModelBridge.ActorObject(cpu).GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null, "the control: this one is skinned on the CPU");
            var onCpu = Covered();
            ModelBridge.Destroy(cpu);

            // The control: the same actor standing as its model stands covers other pixels, so
            // the comparison can tell a posed skin from one left in its bind pose.
            ModelBridge.GpuSkinning = true;
            ModelBridge.Spawn(model, 0, 0, 0, 180, 1, false, false);
            yield return Draw(4);
            var standing = Covered();

            var (differ, either) = Compare(onGpu, onCpu);
            var (apart, _) = Compare(standing, onCpu);
            Assert.That(either, Is.GreaterThan(200), "nothing was drawn, so nothing was compared");
            Assert.That(apart, Is.GreaterThan(either / 5), "a skin in its bind pose covers what the posed one does: the fixture proves nothing");
            Assert.That(differ, Is.LessThanOrEqualTo(either / 50), $"{differ} of {either} pixels differ between the graphics card's pose and the CPU's");
        }

        [UnityTest]
        public IEnumerator AnActorItsClipCarriesIntoView_IsDrawn() {
            RequireGpuSkins();
            int model = 0;
            yield return Load(m => model = m);
            var camera = Screen(0, 1, -10);
            float half = Mathf.Tan(30 * Mathf.Deg2Rad) * 10 * camera.aspect;
            // As it stands the actor is 2 units outside the right edge; Reach's end is 2 inside it.
            int actor = ModelBridge.Spawn(model, half + 2, 0, 0, 0, 1, false, false);
            var r = ModelBridge.ActorObject(actor).GetComponentInChildren<MeshRenderer>(true);
            Assert.That(r, Is.Not.Null);
            var kept = r.localBounds;
            r.localBounds = r.GetComponent<MeshFilter>().sharedMesh.bounds;
            yield return Draw(3);
            Assert.That(r.isVisible, Is.False, "the mesh's own box is on screen, so this placement proves nothing");

            r.localBounds = kept;
            Assert.That(ModelBridge.Play(actor, "Reach", false, 0), Is.True);
            float until = Time.time + 1.2f;
            while (Time.time < until) yield return Draw(1);
            Assert.That(r.isVisible, Is.True, "its clip carried it into view and it was culled by its bounds");
        }

        [UnityTest]
        public IEnumerator AStillActor_WritesNoPose_AndAPlayingOneDoes() {
            RequireGpuSkins();
            int model = 0;
            yield return Load(m => model = m);
            Screen(0, 1, -10);
            int still = ModelBridge.Spawn(model, -2, 0, 0, 180, 1, false, false);
            int held = ModelBridge.Spawn(model, 0, 0, 0, 180, 1, false, false);
            ModelBridge.Play(held, "Turn", true, 0);
            ModelBridge.SetSpeed(held, 0);
            yield return Draw(3);

            int before = ModelBridge.PosesWritten;
            // Moving and turning an actor moves its renderer and its joints alike.
            for (int i = 0; i < 10; i++) {
                ModelBridge.Place(still, -2 + i * 0.1f, 0, 0, 180 + i);
                yield return Draw(1);
            }
            Assert.That(ModelBridge.PosesWritten - before, Is.EqualTo(0), "a pose that did not move was written to the palette");

            // The control: the counter counts. A playing clip moves the pose every frame it is seen.
            ModelBridge.SetSpeed(held, 1);
            before = ModelBridge.PosesWritten;
            yield return Draw(10);
            Assert.That(ModelBridge.PosesWritten - before, Is.GreaterThanOrEqualTo(8), "a playing clip's pose was not written");
        }

        [UnityTest]
        public IEnumerator AJointOfASkinOnTheGraphicsCard_CarriesWhatIsAttached() {
            RequireGpuSkins();
            int model = 0;
            yield return Load(m => model = m);
            Screen(0, 1, -10);
            int tom = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            int hat = ModelBridge.Spawn(model, 0, 0, 0, 0, 0.25f, false, false);
            Assert.That(ModelBridge.GpuSkinCount, Is.EqualTo(2));
            Assert.That(ModelBridge.Attach(hat, tom, "arm", 0, 0, 0, 0, 0, 0, 0.25f), Is.EqualTo(""));
            Assert.That(ModelBridge.ActorObject(hat).transform.parent.name, Is.EqualTo("arm"));
        }
    }
}
