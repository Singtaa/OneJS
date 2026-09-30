using System.Collections;
using NUnit.Framework;
using OneJS.Fx;
using OneJS.ShaderFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Particle systems, physics worlds, shader effects and fx textures are held
    /// in C#, in registries every context shares. Tearing one context down (a hot
    /// reload, a disabled or destroyed JSRunner) must dispose what that context
    /// made and nothing else: with two runners live, a reload of one used to
    /// wipe the other's particles, physics and effects mid-frame.
    /// </summary>
    [TestFixture]
    public class PerContextResourcePlaymodeTests {
        GameObject _goA, _goB;
        PanelSettings _panelSettings;
        QuickJSUIBridge _a, _b;
        SimulationMode2D _simulationMode;

        const string MakeAll = @"
            (function () {
                const host = new CS.UnityEngine.UIElements.VisualElement()
                globalThis.__keep = [
                    CS.OneJS.ParticleBridge.Create(host, '{""v"":4,""max"":10,""emitters"":[{""rate"":1}]}', null),
                    CS.OneJS.Physics2DBridge.Create(host, '{""v"":1}'),
                    new CS.OneJS.ShaderFX.ShaderEffectElement(),
                ]
                return String(CS.OneJS.Fx.FxBridge.CreateTarget(4, 4))
            })()";

        [UnitySetUp]
        public IEnumerator SetUp() {
            _simulationMode = Physics2D.simulationMode;
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet =
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

            _goA = new GameObject("PerContextHostA");
            var docA = _goA.AddComponent<UIDocument>();
            docA.panelSettings = _panelSettings;
            _goB = new GameObject("PerContextHostB");
            var docB = _goB.AddComponent<UIDocument>();
            docB.panelSettings = _panelSettings;
            yield return null;

            _a = new QuickJSUIBridge(docA.rootVisualElement);
            _b = new QuickJSUIBridge(docB.rootVisualElement);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _b?.Dispose();
            _a?.Dispose();
            _a = _b = null;
            if (_goA != null) Object.Destroy(_goA);
            if (_goB != null) Object.Destroy(_goB);
            if (_panelSettings != null) Object.Destroy(_panelSettings);
            Physics2D.simulationMode = _simulationMode;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisposingOneContext_LeavesTheOthersResourcesLive() {
            int particles = ParticleBridge.LiveSystemCount;
            int worlds = Physics2DBridge.LiveWorldCount;
            int effects = ShaderEffectBridge.LiveEffectCount;
            int handles = FxBridge.LiveHandleCount;

            int fxA = int.Parse(_a.Eval(MakeAll));
            int fxB = int.Parse(_b.Eval(MakeAll));
            Assert.AreEqual(particles + 2, ParticleBridge.LiveSystemCount);
            Assert.AreEqual(worlds + 2, Physics2DBridge.LiveWorldCount);
            Assert.AreEqual(effects + 2, ShaderEffectBridge.LiveEffectCount);
            Assert.AreEqual(handles + 2, FxBridge.LiveHandleCount);

            _b.Dispose();
            _b = null;

            Assert.AreEqual(particles + 1, ParticleBridge.LiveSystemCount, "B's particle system goes, A's stays");
            Assert.AreEqual(worlds + 1, Physics2DBridge.LiveWorldCount, "B's physics world goes, A's stays");
            Assert.AreEqual(effects + 1, ShaderEffectBridge.LiveEffectCount, "B's shader effect goes, A's stays");
            Assert.AreEqual(handles + 1, FxBridge.LiveHandleCount, "B's fx target goes, A's stays");
            Assert.IsNull(FxBridge.GetTexture(fxB), "B's fx handle is released");
            Assert.IsNotNull(FxBridge.GetTexture(fxA), "A's fx handle still names a texture");

            _a.Dispose();
            _a = null;

            Assert.AreEqual(particles, ParticleBridge.LiveSystemCount);
            Assert.AreEqual(worlds, Physics2DBridge.LiveWorldCount);
            Assert.AreEqual(effects, ShaderEffectBridge.LiveEffectCount);
            Assert.AreEqual(handles, FxBridge.LiveHandleCount);
            yield return null;
        }
    }
}
