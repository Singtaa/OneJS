using System.Collections;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The 3D scene is one per player, made by whichever context first adds to
    /// it. Tearing that context down (a hot reload, a stopped JSRunner) must
    /// take the scene with it, as onejs-play's unmount does when it runs;
    /// tearing down any other context must leave it (architecture review A1).
    /// </summary>
    [TestFixture]
    public class ModelTeardownPlaymodeTests {
        QuickJSUIBridge _owner, _other;

        [UnitySetUp]
        public IEnumerator SetUp() {
            ModelBridge.DisposeAll();
            _owner = new QuickJSUIBridge(new VisualElement());
            _other = new QuickJSUIBridge(new VisualElement());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _other?.Dispose();
            _owner?.Dispose();
            _owner = _other = null;
            ModelBridge.DisposeAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSceneGoesWithTheContextThatMadeIt() {
            _owner.Eval("CS.OneJS.Models.ModelBridge.AddPointLight(0, 2, 0, 1, 1, 1, 1, 5)");
            Assert.AreEqual(1, ModelBridge.LightCount);
            Assert.IsTrue(ModelBridge.HasScene);

            _other.Dispose();
            _other = null;
            Assert.AreEqual(1, ModelBridge.LightCount, "another context's teardown leaves the scene");

            _owner.Dispose();
            _owner = null;
            Assert.AreEqual(0, ModelBridge.LightCount, "the light goes with its context");
            Assert.IsFalse(ModelBridge.HasScene, "and so do the scene's root and camera");
            yield return null;
        }
    }
}
