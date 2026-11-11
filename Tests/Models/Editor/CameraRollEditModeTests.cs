using NUnit.Framework;
using OneJS.Models;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The scene camera's roll (ModelBridge.SetCameraRolled): turned about the line it
    /// looks along after it looks at its point, so it still looks there, and SetCamera, which
    /// every older cart sends, rolls nothing.
    /// </summary>
    public class CameraRollEditModeTests {
        [SetUp]
        public void NewScene() {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ModelBridge.BeginScene();
        }

        [TearDown]
        public void Dispose() => ModelBridge.DisposeAll();

        static void Near(Vector3 a, Vector3 b, string what) =>
            Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-4f), $"{what}: {a:F4}, wanted {b:F4}");

        [Test]
        public void Roll_TurnsTheCameraAboutWhereItLooks_AnticlockwiseFromBehind() {
            ModelBridge.SetCamera(0, 0, -5, 0, 0, 0, 50);
            var t = ModelBridge.SceneCamera.transform;
            Near(t.up, Vector3.up, "unrolled");
            ModelBridge.SetCameraRolled(0, 0, -5, 0, 0, 0, 50, 90);
            Near(t.forward, Vector3.forward, "rolled, it still looks at its point");
            Near(t.up, Vector3.left, "rolled 90, its top leans to its left");
            ModelBridge.SetCamera(0, 0, -5, 0, 0, 0, 50);
            Near(t.up, Vector3.up, "SetCamera rolls it back");
            ModelBridge.SetCameraRolled(0, 0, -5, 0, 0, 0, 50, float.NaN);
            Near(t.up, Vector3.up, "a roll that is no number rolls nothing");
        }
    }
}
