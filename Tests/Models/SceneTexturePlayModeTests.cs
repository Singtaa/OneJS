using System.Collections;
using System.Linq;
using NUnit.Framework;
using OneJS.Models;
using OneJS.ShaderFX;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelBridge.AcquireSceneTexture in play mode, where the bridge has switched the scene's own
    /// cameras off: while a program reads the scene, a camera that draws nothing clears the screen
    /// in the scene's background colour, and the last reader takes it away again.
    /// </summary>
    public class SceneTexturePlayModeTests {
        [TearDown]
        public void Clean() => ModelBridge.DisposeAll();

        static Camera[] Clears() => Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .Where(c => c.name == "OneJS Clear").ToArray();

        [UnityTest]
        public IEnumerator WhileRead_ACameraClearsTheScreen_InTheBackgroundColour() {
            ModelBridge.BeginScene();
            yield return null;
            Assert.That(Clears(), Is.Empty, "nothing reads the scene: its camera draws to the screen");

            ModelBridge.AcquireSceneTexture();
            ModelBridge.AcquireSceneTexture();
            var clears = Clears();
            Assert.That(clears, Has.Length.EqualTo(1), "one clear camera however many read");
            var clear = clears[0];
            Assert.That(clear.cullingMask, Is.EqualTo(0), "it draws nothing, so the scene is drawn once");
            Assert.That(clear.targetTexture, Is.Null, "it clears the screen");
            Assert.That(clear.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));

            ModelBridge.SetBackground(0, 1, 0);
            System.Threading.Thread.Sleep(2);
            ShaderEffectBridge.TickAll();
            Assert.That(clear.backgroundColor, Is.EqualTo(ModelBridge.SceneCamera.backgroundColor), "it follows the background");
            yield return null;

            ModelBridge.ReleaseSceneTexture();
            yield return null;
            Assert.That(Clears(), Has.Length.EqualTo(1), "one reader is left");

            ModelBridge.ReleaseSceneTexture();
            yield return null;
            Assert.That(Clears(), Is.Empty, "the last reader gone, the scene's camera clears the screen itself");
            Assert.That(ModelBridge.SceneCamera.enabled, Is.True);
            Assert.That(ModelBridge.SceneCamera.targetTexture, Is.Null);
        }

        [UnityTest]
        public IEnumerator TheSceneGoing_TakesTheClearCameraAlong() {
            ModelBridge.BeginScene();
            ModelBridge.AcquireSceneTexture();
            Assert.That(Clears(), Has.Length.EqualTo(1));
            ModelBridge.DisposeAll();
            yield return null;
            Assert.That(Clears(), Is.Empty);
        }
    }
}
