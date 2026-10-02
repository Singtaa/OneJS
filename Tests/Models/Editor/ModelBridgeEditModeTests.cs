using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge in the edit-mode preview, where a cart runs in the Game view of somebody's open
    /// scene. The rule there: the bridge adds objects of its own, marked not to save, and changes
    /// nothing that was already in the scene. Also the glTF material mapping and the defaults a
    /// spawned model gets, which do not depend on the mode.
    /// </summary>
    public class ModelBridgeEditModeTests {
        static string Fixture([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here)), "Fixtures~", "cubes.glb");

        static IEnumerator Load(System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture());
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            done(task.Result);
        }

        static Material MaterialNamed(int actor, string name) =>
            ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).First(m => m.name == name);

        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        [UnityTest]
        public IEnumerator GltfMaterials_KeepMetallicRoughnessEmissionAndAlpha() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);

            var metal = MaterialNamed(actor, "Metal");
            Assert.That(metal.shader.name, Is.EqualTo("OneJS/ModelLit"));
            Assert.That(metal.shader.isSupported, Is.True, "ModelLit has no SubShader for this pipeline");
            AssertColor(metal.GetColor("_BaseColor").linear, new Color(0.8f, 0.2f, 0.1f));
            Assert.That(metal.GetFloat("_Metallic"), Is.EqualTo(0.25f).Within(1e-4));
            Assert.That(metal.GetFloat("_Roughness"), Is.EqualTo(0.75f).Within(1e-4));
            AssertColor(metal.GetColor("_EmissionColor").linear, new Color(1f, 0.5f, 0f));
            Assert.That(metal.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            Assert.That(metal.GetFloat("_Cutoff"), Is.EqualTo(0.3f).Within(1e-4));
            Assert.That(metal.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off), "doubleSided draws both faces");
            Assert.That(metal.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));

            var glass = MaterialNamed(actor, "Glass");
            Assert.That(glass.GetFloat("_Surface"), Is.EqualTo(1f));
            Assert.That(glass.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(glass.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
            Assert.That(glass.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
            Assert.That(glass.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(glass.GetColor("_BaseColor").a, Is.EqualTo(0.5f).Within(1e-3));
        }

        [UnityTest]
        public IEnumerator Clips_AreTheFilesOwnNames() {
            int model = 0;
            yield return Load(m => model = m);
            Assert.That(ModelBridge.Clips(model), Is.EqualTo("spin"));
        }

        [UnityTest]
        public IEnumerator Spawned_CastAndReceiveShadowsUnlessTold() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int on = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int off = ModelBridge.Spawn(model, 2, 0, 0, 0, 1, false, false);

            foreach (var r in ModelBridge.ActorObject(on).GetComponentsInChildren<Renderer>()) {
                Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                Assert.That(ReceiveShadows(r), Is.EqualTo(1f));
            }
            foreach (var r in ModelBridge.ActorObject(off).GetComponentsInChildren<Renderer>()) {
                Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(ReceiveShadows(r), Is.EqualTo(0f));
            }

            ModelBridge.SetShadows(off, true, true);
            foreach (var r in ModelBridge.ActorObject(off).GetComponentsInChildren<Renderer>()) {
                Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                Assert.That(ReceiveShadows(r), Is.EqualTo(1f));
            }
        }

        [UnityTest]
        public IEnumerator EditMode_LeavesTheScenesCameraLightAndLightingAlone() {
            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1, -10);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var before = Snapshot.Of(camera, light);

            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            ModelBridge.SetCamera(3, 4, 5, 0, 0, 0, 40);
            ModelBridge.SetSun(true, 1, -1, 0, 1, 0, 0, 2, true);
            ModelBridge.SetAmbient(1, 0, 0, 0, 0, 1, 1);
            ModelBridge.SetBackground(1, 1, 0);
            ModelBridge.SetFog(true, 1, 1, 1, 1, 2);
            ModelBridge.AddPointLight(0, 1, 0, 1, 1, 1, 2, 5);
            ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);

            Assert.That(Snapshot.Of(camera, light), Is.EqualTo(before), "the preview changed something already in the scene");
            var ours = InScene<Transform>().Where(t => t != camera.transform && t != light.transform).ToArray();
            Assert.That(ours, Is.Not.Empty);
            foreach (var t in ours) Assert.That(t.gameObject.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave), t.name);
            Assert.That(ModelBridge.SceneCamera, Is.Not.EqualTo(camera), "the bridge draws through a camera of its own");
            Assert.That(ModelBridge.SceneCamera.depth, Is.GreaterThan(camera.depth));
        }

        [Test]
        public void EditMode_WithNoSceneLight_MakesItsOwnSunThatCastsSoftShadows() {
            ModelBridge.BeginScene();
            ModelBridge.SetSun(true, 0, -1, 0, 1, 1, 1, 1.5f, true);
            var suns = InScene<Light>().Where(l => l.type == LightType.Directional).ToArray();
            Assert.That(suns, Has.Length.EqualTo(1));
            Assert.That(suns[0].shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(suns[0].intensity, Is.EqualTo(1.5f));
            Assert.That(suns[0].gameObject.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
        }

        [Test]
        public void Dispose_RemovesEverythingTheBridgeMade() {
            ModelBridge.BeginScene();
            ModelBridge.SetSun(true, 0, -1, 0, 1, 1, 1, 1, true);
            ModelBridge.AddPointLight(0, 1, 0, 1, 1, 1, 1, 5);
            ModelBridge.DisposeAll();
            Assert.That(InScene<Transform>(), Is.Empty);
        }

        // Pressing Play or saving a file while a .glb is still loading tears the scene down under
        // the load; a load that then finished anyway left a template behind that outlived it.
        [UnityTest]
        public IEnumerator Dispose_DuringALoad_LeavesNothingWhenTheLoadFinishes() {
            Task<int> task = ModelBridge.Load(Fixture());
            Assume.That(task.IsCompleted, Is.False, "the load finished before it could be interrupted");
            ModelBridge.DisposeAll();
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsCanceled, Is.True, "a load the scene did not outlive must not hand back a model");
            Assert.That(InScene<Transform>(), Is.Empty);
        }

        [Test]
        public void HemisphereProbe_IsTheSkyAboveAndTheGroundBelow() {
            var sky = new Color(0.6f, 0.7f, 0.9f);
            var ground = new Color(0.3f, 0.25f, 0.2f);
            var sh = ModelBridge.HemisphereProbe(sky, ground, 1f);
            var dirs = new[] { Vector3.up, Vector3.down, Vector3.right };
            var got = new Color[3];
            sh.Evaluate(dirs, got);
            AssertColor(got[0], sky.linear);
            AssertColor(got[1], ground.linear);
            AssertColor(got[2], (sky.linear + ground.linear) * 0.5f);
        }

        [Test]
        public void Shader_CompilesOnlyWhereGltfastIs() {
            var source = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Fixture()))),
                "..", "Runtime", "Models", "Resources", "OneJS", "ModelLit.shader"));
            int subShaders = source.Split("SubShader {").Length - 1;
            int gated = source.Split("\"com.unity.cloud.gltfast\"").Length - 1;
            Assert.That(subShaders, Is.GreaterThan(0));
            Assert.That(gated, Is.EqualTo(subShaders), "every SubShader needs PackageRequirements on glTFast, or it ships in builds without models");
        }

        // URP compiled the built-in SubShader's 48 variants into every build and never drew one.
        [Test]
        public void ScriptablePipelineBuilds_LeaveOutTheBuiltInSubShader() {
            var shader = Resources.Load<Shader>("OneJS/ModelLit");
            var pipeline = new ShaderTagId("RenderPipeline");
            var all = Enumerable.Range(0, shader.subshaderCount).ToArray();
            var builtIn = all.Single(i => shader.FindSubshaderTagValue(i, pipeline) == ShaderTagId.none);
            var urp = all.Single(i => i != builtIn);
            Assert.That(OneJS.Editor.ModelShaderStripper.Strips(shader, (uint)builtIn, scriptableOnly: true), Is.True);
            Assert.That(OneJS.Editor.ModelShaderStripper.Strips(shader, (uint)urp, scriptableOnly: true), Is.False);
            Assert.That(OneJS.Editor.ModelShaderStripper.Strips(shader, (uint)builtIn, scriptableOnly: false), Is.False, "a project that uses the built-in pipeline anywhere keeps it");
            Assert.That(OneJS.Editor.ModelShaderStripper.Strips(Shader.Find("Hidden/InternalErrorShader"), 0, scriptableOnly: true), Is.False, "only ModelLit");
        }

        // FindObjectsByType skips DontSave objects, which is everything the bridge makes in edit mode.
        static T[] InScene<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();

        static float ReceiveShadows(Renderer r) {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetFloat("_ReceiveShadows");
        }

        static void AssertColor(Color got, Color want) {
            Assert.That(got.r, Is.EqualTo(want.r).Within(2e-3), "r");
            Assert.That(got.g, Is.EqualTo(want.g).Within(2e-3), "g");
            Assert.That(got.b, Is.EqualTo(want.b).Within(2e-3), "b");
        }

        /// <summary>Everything about the scene's own camera, light and lighting that a preview could disturb.</summary>
        struct Snapshot : System.IEquatable<Snapshot> {
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public bool cameraEnabled;
            public CameraClearFlags clear;
            public Color background;
            public int cullingMask;
            public float fov;
            public Quaternion lightRotation;
            public bool lightEnabled;
            public float intensity;
            public Color lightColor;
            public LightShadows shadows;
            public AmbientMode ambientMode;
            public SphericalHarmonicsL2 probe;
            public Color ambientLight;
            public bool fog;
            public Color fogColor;
            public Light sun;

            public static Snapshot Of(Camera c, Light l) => new Snapshot {
                cameraPosition = c.transform.position, cameraRotation = c.transform.rotation, cameraEnabled = c.enabled,
                clear = c.clearFlags, background = c.backgroundColor, cullingMask = c.cullingMask, fov = c.fieldOfView,
                lightRotation = l.transform.rotation, lightEnabled = l.enabled, intensity = l.intensity, lightColor = l.color,
                shadows = l.shadows, ambientMode = RenderSettings.ambientMode, probe = RenderSettings.ambientProbe,
                ambientLight = RenderSettings.ambientLight, fog = RenderSettings.fog, fogColor = RenderSettings.fogColor,
                sun = RenderSettings.sun,
            };

            public bool Equals(Snapshot o) =>
                cameraPosition == o.cameraPosition && cameraRotation == o.cameraRotation && cameraEnabled == o.cameraEnabled
                && clear == o.clear && background == o.background && cullingMask == o.cullingMask && fov == o.fov
                && lightRotation == o.lightRotation && lightEnabled == o.lightEnabled && intensity == o.intensity
                && lightColor == o.lightColor && shadows == o.shadows && ambientMode == o.ambientMode && probe == o.probe
                && ambientLight == o.ambientLight && fog == o.fog && fogColor == o.fogColor && sun == o.sun;

            public override bool Equals(object obj) => obj is Snapshot s && Equals(s);
            public override int GetHashCode() => cameraPosition.GetHashCode();
            public override string ToString() => JsonUtility.ToJson(this);
        }
    }
}
