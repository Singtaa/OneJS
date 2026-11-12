using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// An actor's own light (ModelBridge.ActorLight.cs): an ambient and one directional light held
    /// as the actor's SH, in place of the scene's ambient, probes and baked light.
    /// </summary>
    public class ActorLightEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        // A square in the XY plane, its normal toward the camera at -z.
        static readonly Vector3[] Square = { new(-1, -1, 0), new(-1, 1, 0), new(1, 1, 0), new(1, -1, 0) };
        static readonly Vector3[] Toward = { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        static readonly uint[] TwoTriangles = { 0, 1, 2, 0, 2, 3 };

        // What a facing surface, one edge on and the far side take, through Unity's own evaluation of
        // the coefficients: the direct light whole, 3/34 and 1/17 of it, and the ambient everywhere.
        [Test]
        public void TheProbe_LightsAFacingSurfaceWithTheLightWhole_AndTheFarSideALittle() {
            var l = new Vector3(0.3f, 0.8f, -0.52f).normalized;
            var edge = Vector3.Cross(l, Vector3.right).normalized;
            var sh = ModelBridge.ActorLightProbe(new Color(0.1f, 0.2f, 0.3f), new Color(1f, 0.5f, 0.25f), l);
            var results = new Color[3];
            sh.Evaluate(new[] { l, edge, -l }, results);
            void Near(Color got, Color want, string what) {
                for (int c = 0; c < 3; c++) Assert.That(got[c], Is.EqualTo(want[c]).Within(1e-3f), $"{what}, channel {c}: {got}");
            }
            Near(results[0], new Color(1.1f, 0.7f, 0.55f), "facing the light");
            Near(results[1], new Color(0.1f, 0.2f, 0.3f) + new Color(1f, 0.5f, 0.25f) * (3f / 34f), "edge on");
            Near(results[2], new Color(0.1f, 0.2f, 0.3f) + new Color(1f, 0.5f, 0.25f) * (1f / 17f), "the far side");
        }

        [Test]
        public void AnActor_IsLitByItsOwnLight_AndGoesBackToTheScenesWhenCleared() {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) light.enabled = false;
            int model = ModelBridge.CreateMesh(true, false, false, 1, 1, 1, 1);
            Assert.That(ModelBridge.SetMesh(model, Square, TwoTriangles, Toward, null, null), Is.Empty);
            int actor = ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false);
            var renderer = ModelBridge.ActorObject(actor).GetComponentInChildren<Renderer>();

            // Grey light shining away from the camera, onto the square's face.
            Assert.That(ModelBridge.SetActorLight(actor, 0, 0, 0, 0.5f, 0.5f, 0.5f, 1, 0, 0, 1), Is.Empty);
            Assert.That(renderer.lightProbeUsage, Is.EqualTo(LightProbeUsage.CustomProvided));
            var facing = Middle();
            Assert.That(facing.g, Is.EqualTo(128).Within(10), $"lit facing the light, it takes the grey: {facing}");

            // The same light from behind leaves the face about a seventeenth: near black.
            Assert.That(ModelBridge.SetActorLight(actor, 0, 0, 0, 0.5f, 0.5f, 0.5f, 1, 0, 0, -1), Is.Empty);
            var behind = Middle();
            Assert.That(behind.g, Is.LessThan(40), $"lit from behind, it is dark: {behind}");

            // An ambient alone lights it from every side, in its own colour.
            Assert.That(ModelBridge.SetActorLight(actor, 0.5f, 0, 0, 0, 0, 0, 1, 0, -1, 0), Is.Empty);
            var ambient = Middle();
            Assert.That(ambient.r, Is.EqualTo(128).Within(10), $"under a red ambient, red: {ambient}");
            // A little of the sky's reflection, which URP adds to any surface, lifts the other two.
            Assert.That(ambient.g, Is.LessThan(25), $"under a red ambient, red: {ambient}");

            ModelBridge.ClearActorLight(actor);
            Assert.That(renderer.lightProbeUsage, Is.EqualTo(LightProbeUsage.BlendProbes), "back to the scene's light");

            Assert.That(ModelBridge.SetActorLight(actor, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0), Does.Contain("points nowhere"));
            Assert.That(ModelBridge.SetActorLight(actor, 0, 0, 0, 1, 1, 1, -1, 0, -1, 0), Does.Contain("intensity"));
            Assert.That(ModelBridge.SetActorLight(actor, float.NaN, 0, 0, 1, 1, 1, 1, 0, -1, 0), Does.Contain("ambient"));
        }

        // The middle pixel of the scene from the camera at -z, over black.
        static Color32 Middle() {
            var target = new RenderTexture(32, 32, 24);
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            var read = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try {
                camera.transform.position = new Vector3(0, 0, -2);
                camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                camera.Render();
                var was = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                RenderTexture.active = was;
                return read.GetPixel(16, 16);
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(read);
                target.Release();
            }
        }
    }
}
