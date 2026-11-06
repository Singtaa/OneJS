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
        static string Fixture(string name = "cubes.glb", [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here)), "Fixtures~", name);

        static IEnumerator Load(System.Action<int> done, string fixture = "cubes.glb") {
            Task<int> task = ModelBridge.Load(Fixture(fixture));
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
        public IEnumerator OnlyAnActorWithValuesOfItsOwn_CarriesAPropertyBlock() {
            // A renderer with a property block leaves URP's SRP Batcher and draws alone in every
            // shadow cascade, so the common actor must have none.
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int plain = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int fading = ModelBridge.Spawn(model, 2, 0, 0, 0, 1, true, true);
            int shadowless = ModelBridge.Spawn(model, 4, 0, 0, 0, 1, true, false);
            Renderer[] Of(int actor) => ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>();

            ModelBridge.SetDissolve(fading, 0.4f);
            Assert.That(Of(plain).Any(r => r.HasPropertyBlock()), Is.False, "a plain actor");
            Assert.That(Of(shadowless).All(r => r.HasPropertyBlock()), Is.True, "an actor that keeps no shadow on itself");
            foreach (var r in Of(fading)) {
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_ActorDissolve"), Is.EqualTo(0.4f).Within(1e-6));
            }

            // Back to plain, the block goes, so the actor batches again.
            ModelBridge.SetDissolve(fading, 0f);
            ModelBridge.SetShadows(shadowless, true, true);
            Assert.That(Of(fading).Concat(Of(shadowless)).Any(r => r.HasPropertyBlock()), Is.False);

            // And every material instances, for the actors that do carry one.
            Assert.That(Of(plain).SelectMany(r => r.sharedMaterials).All(m => m.enableInstancing), Is.True);
        }

        [UnityTest]
        public IEnumerator Actors_MoveTurnAndScaleOneThingAtATime() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            var t = ModelBridge.ActorObject(actor).transform;

            ModelBridge.SetRotation(actor, 90, 30, 10);
            Assert.That(Quaternion.Angle(t.rotation, Quaternion.Euler(30, 90, 10)), Is.LessThan(0.01f));

            // Moving must not undo a pitch or a roll, which Place, carrying only a yaw, would.
            ModelBridge.Move(actor, 1, 2, 3);
            Assert.That(t.position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(Quaternion.Angle(t.rotation, Quaternion.Euler(30, 90, 10)), Is.LessThan(0.01f));

            ModelBridge.SetScale(actor, 2);
            Assert.That(t.localScale, Is.EqualTo(Vector3.one * 2));
        }

        [UnityTest]
        public IEnumerator PlaceMany_MovesAndTurnsEveryActorInOneCall() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int a = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int b = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            // b's quaternion is what onejs-play's physics3d/rotation.ts toQuat gives for
            // { yaw: 90, pitch: 30, roll: 10 }: the two sides must mean the same turn.
            ModelBridge.PlaceMany(new[] {
                a, 1f, 2f, 3f, 0f, 0f, 0f, 1f,
                b, -1f, 0.5f, 4f, 0.24184476f, 0.66446302f, -0.12278780f, 0.69636424f,
                999f, 9f, 9f, 9f, 0f, 0f, 0f, 1f,
            });
            var ta = ModelBridge.ActorObject(a).transform;
            var tb = ModelBridge.ActorObject(b).transform;
            Assert.That(ta.position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(Quaternion.Angle(ta.rotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(tb.position, Is.EqualTo(new Vector3(-1, 0.5f, 4)));
            Assert.That(Quaternion.Angle(tb.rotation, Quaternion.Euler(30, 90, 10)), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator GltfX_IsMirroredIntoUnitysX() {
            // onejs-play builds mesh colliders from the same .glb in JavaScript and mirrors
            // glTF's x the way glTFast does. If glTFast ever stops, colliders stand on the
            // wrong side of every model, and this is where it shows.
            int model = 0;
            yield return Load(m => model = m, "offset-wall.glb");
            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            var bounds = ModelBridge.ActorObject(actor).GetComponentInChildren<Renderer>().bounds;
            Assert.That(bounds.center.x, Is.EqualTo(-3f).Within(1e-3), "glTF x = +3 should stand at Unity x = -3");
            Assert.That(bounds.size.x, Is.EqualTo(2f).Within(1e-3));
            Assert.That(bounds.center.y, Is.EqualTo(1f).Within(1e-3));
        }

        [UnityTest]
        public IEnumerator Speed_ScalesAnActorsClipAndZeroFreezesIt() {
            int model = 0;
            yield return Load(m => model = m);
            ModelBridge.BeginScene();
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            int other = ModelBridge.Spawn(model, 2, 0, 0, 0, 1, true, true);
            var spin = ModelBridge.ActorObject(actor).GetComponentInChildren<Animation>()["spin"];
            var otherSpin = ModelBridge.ActorObject(other).GetComponentInChildren<Animation>()["spin"];

            // Set before the clip starts, so a clip played later takes the actor's speed too.
            ModelBridge.SetSpeed(actor, 0);
            ModelBridge.Play(actor, "spin", true, 0);
            ModelBridge.Play(other, "spin", true, 0);
            ModelBridge.Step(0.25f);
            Assert.That(spin.time, Is.EqualTo(0f), "a speed of 0 is paused where it is");
            Assert.That(otherSpin.time, Is.EqualTo(0.25f).Within(1e-4), "one actor's speed is its own");

            ModelBridge.SetSpeed(actor, 2);
            ModelBridge.Step(0.25f);
            Assert.That(spin.time, Is.EqualTo(0.5f).Within(1e-4));
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

        // onejs-play's unload: a level's models go when the level does, not only with the scene.
        [UnityTest]
        public IEnumerator Unload_FreesAModelNothingShows_AndKeepsOneAnActorStillShows() {
            int model = 0;
            yield return Load(m => model = m);
            var actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
            Assert.That(ModelBridge.Unload(model), Is.False, "an actor of it is still in the world");
            Assert.That(ModelBridge.ModelCount, Is.EqualTo(1));
            ModelBridge.Destroy(actor);
            Assert.That(ModelBridge.Unload(model), Is.True);
            Assert.That(ModelBridge.ModelCount, Is.EqualTo(0));
            Assert.That(ModelBridge.Clips(model), Is.Empty);
            Assert.Throws<System.ArgumentException>(() => ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true));
            Assert.That(ModelBridge.Unload(model), Is.True, "unloading twice is no harm");
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

        // glTFast gives a skinned mesh with vertex colours four streams (colours and UVs apart, bones
        // in the fourth), and Unity warns "Skinned mesh attributes use wrong streams" as it builds
        // the mesh: an orange line in every cart's console on every Run. Unity skins from position,
        // normal and tangent in stream 0, colours and UVs in 1, bones in 2. ModelLit reads no vertex
        // colours, so they are not loaded, and the rest keeps its data.
        [UnityTest]
        public IEnumerator SkinnedModels_UseTheStreamsUnitySkinsFrom() {
            var warned = new System.Collections.Generic.List<string>();
            void Collect(string message, string stack, LogType type) { if (message.Contains("wrong streams")) warned.Add(message); }
            Application.logMessageReceived += Collect;
            var target = new RenderTexture(64, 64, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            try {
                int model = 0;
                yield return Load(m => model = m, "skinned.glb");
                var actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
                var mesh = ModelBridge.ActorObject(actor).GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                foreach (var a in mesh.GetVertexAttributes()) {
                    var stream = a.attribute <= VertexAttribute.Tangent ? 0 : a.attribute >= VertexAttribute.BlendWeight ? 2 : 1;
                    Assert.That(a.stream, Is.EqualTo(stream), a.attribute.ToString());
                }
                Assert.That(mesh.HasVertexAttribute(VertexAttribute.Color), Is.False);
                // The fixture's four UV corners, and the top half on the second joint.
                Assert.That(mesh.uv.Distinct().OrderBy(v => v.x).ThenBy(v => v.y),
                    Is.EqualTo(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) }));
                Assert.That(mesh.boneWeights.Count(w => w.boneIndex0 == 1), Is.EqualTo(mesh.vertices.Count(v => v.y > 0.5f)));
                Assert.That(mesh.vertices.Count(v => v.y > 0.5f), Is.GreaterThan(0));
                Assert.That(mesh.blendShapeCount, Is.EqualTo(1));
                var deltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(0, 0, deltas, null, null);
                Assert.That(deltas[0].magnitude, Is.EqualTo(0.25f).Within(1e-5f));

                camera.transform.position = new Vector3(0, 0.5f, -3);
                camera.targetTexture = target;
                camera.Render();
                Assert.That(warned, Is.Empty);
            } finally {
                Application.logMessageReceived -= Collect;
                Object.DestroyImmediate(camera.gameObject);
                target.Release();
            }
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

        // A per-actor value declared where URP does not expect it takes every model off the SRP
        // Batcher, and nothing else would say so: the models still draw, one by one.
        // A depth-only draw (Gunyard's shadow fan, Tachi 5f9c606d): _ColorMask 0 leaves the target's colour alone,
        // and the default, 15, draws. Without ColorMask [_ColorMask] in the forward pass both draw.
        [UnityTest]
        public IEnumerator ColorMaskZero_DrawsNoColour() {
            var target = new RenderTexture(64, 64, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            try {
                int model = 0;
                yield return Load(m => model = m, "skinned.glb");
                var actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, true, true);
                var materials = ModelBridge.ActorObject(actor).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
                Assert.That(materials, Is.Not.Empty);
                Assert.That(materials.All(m => m.GetFloat("_ColorMask") == 15), "every channel by default");
                camera.transform.position = new Vector3(0, 0.5f, -3);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.targetTexture = target;
                Color Centre() {
                    camera.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    RenderTexture.active = was;
                    return read.GetPixel(32, 32);
                }
                Assert.That(Centre(), Is.Not.EqualTo(Color.magenta), "the model covers the centre");
                foreach (var m in materials) m.SetFloat("_ColorMask", 0);
                Assert.That(Centre(), Is.EqualTo(Color.magenta), "a depth-only draw leaves the colour");
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }

        [Test]
        public void UrpSubShader_StaysSrpBatcherCompatible() {
            var shader = Resources.Load<Shader>("OneJS/ModelLit");
            var pipeline = new ShaderTagId("RenderPipeline");
            var urp = Enumerable.Range(0, shader.subshaderCount).Single(i => shader.FindSubshaderTagValue(i, pipeline) != ShaderTagId.none);
            var code = typeof(UnityEditor.ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (code == null) Assert.Ignore("this editor has no ShaderUtil.GetSRPBatcherCompatibilityCode");
            var reason = typeof(UnityEditor.ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            int result = (int)code.Invoke(null, new object[] { shader, urp });
            string why = result == 0 || reason == null ? "" : (string)reason.Invoke(null, new object[] { shader, urp, result });
            Assert.That(result, Is.EqualTo(0), why);
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

        // What ModelLit reads: an actor with no property block receives shadows.
        static float ReceiveShadows(Renderer r) {
            if (!r.HasPropertyBlock()) return 1f;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return 1f - block.GetFloat("_ActorShadowless");
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
