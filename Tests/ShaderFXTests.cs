using System.Collections;
using NUnit.Framework;
using OneJS.ShaderFX;
using OneJS.Tests;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for the ShaderFX element: render-target lifecycle against real layout,
    /// the flat-array uniform path that JS uses to send a whole layer stack in one
    /// crossing, and ramp/registry behaviour.
    ///
    /// The layout tests deliberately never call ShaderEffectBridge.TickAll. An
    /// element that only paints once a tick happens to land after layout looks fine
    /// in play mode and blank in a throttled editor, so "paints from layout alone"
    /// is the property worth pinning.
    /// </summary>
    [TestFixture]
    [Category("RequiresGraphics")]
    public class ShaderFXTests {
        const string kShader = "OneJS/TextureFX";

        static ShaderEffectElement MakeEffect() {
            var fx = new ShaderEffectElement();
            fx.SetShader(kShader);
            return fx;
        }

        // PanelSettings created without a theme logs a warning we do not control, so
        // the panel-backed tests opt out of log failures individually. It is not set
        // in a [SetUp]: the LogAssert.Expect tests below need failures left on.

        // MARK: Render target lifecycle

        [UnityTest]
        public IEnumerator Layout_PaintsOnFirstRectWithoutWaitingForATick() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(64, 64);
            var fx = MakeEffect();
            ph.Add(fx, 64, 64);

            Assert.IsFalse(fx.IsReady, "nothing to render into before layout has run");

            yield return null;
            yield return null;

            // No TickAll above: layout alone has to be enough. Before this behaviour
            // existed the element sat blank until the next bridge tick, which in
            // edit-mode preview can be seconds away whenever the editor is unfocused.
            Assert.IsTrue(fx.IsReady, "should have painted from the layout pass that gave it a rect");
            Assert.AreEqual(64, fx.RenderWidth);
            Assert.AreEqual(64, fx.RenderHeight);

            fx.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Layout_ResizeRebuildsTheTargetWithoutATick() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(128, 128);
            var fx = MakeEffect();
            ph.Add(fx, 64, 64);
            yield return null;
            yield return null;
            Assert.AreEqual(64, fx.RenderWidth);

            fx.style.width = 96;
            fx.style.height = 32;
            yield return null;
            yield return null;

            Assert.AreEqual(96, fx.RenderWidth, "a resize should re-size the render target");
            Assert.AreEqual(32, fx.RenderHeight);

            fx.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Resolution_ExplicitSizeIgnoresLayout() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(128, 128);
            var fx = MakeEffect();
            fx.SetResolution(16, 24);
            ph.Add(fx, 128, 128);
            yield return null;
            yield return null;

            Assert.AreEqual(16, fx.RenderWidth, "an explicit resolution should win over the element's rect");
            Assert.AreEqual(24, fx.RenderHeight);

            fx.Dispose();
            ph.Destroy();
        }

        [UnityTest]
        public IEnumerator Dispose_UnregistersFromTheBridge() {
            LogAssert.ignoreFailingMessages = true;
            var ph = PanelHost.Create(32, 32);
            var before = ShaderEffectBridge.LiveEffectCount;
            var fx = MakeEffect();
            Assert.AreEqual(before + 1, ShaderEffectBridge.LiveEffectCount);
            ph.Add(fx, 32, 32);
            yield return null;

            fx.Dispose();
            Assert.AreEqual(before, ShaderEffectBridge.LiveEffectCount);
            fx.Dispose(); // idempotent
            Assert.AreEqual(before, ShaderEffectBridge.LiveEffectCount);

            ph.Destroy();
        }

        // MARK: Uniform marshalling
        // SetVectorArray takes object rather than float[] on purpose: a JS array
        // arrives as the {__csArray, __csArrayType} marker, which does not bind to a
        // float[] parameter and makes the method invisible to reflection entirely
        // ("Method not found: ...SetVectorArray"). These cover the C#-side contract;
        // the packing itself is pinned by onejs-react's texturefx.test.ts.

        [Test]
        public void SetVectorArray_RejectsRaggedInput() {
            var fx = MakeEffect();
            // 4 floats per float4, so a length that is not a multiple of 4 is a bug in
            // the caller, not something to pad or truncate silently.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("_LScale"));
            fx.SetVectorArray("_LScale", new float[] { 1f, 2f, 3f });
            fx.Dispose();
        }

        [Test]
        public void SetVectorArray_AcceptsAFlatFloatArray() {
            var fx = MakeEffect();
            Assert.DoesNotThrow(() => fx.SetVectorArray("_LScale", new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }));
            fx.Dispose();
        }

        // MARK: Shared resources
        // These are all uploaded with Apply(makeNoLongerReadable: true), so there is no
        // CPU copy to read back: the assertions are about identity, caching and shape
        // rather than texels. Ramp and noise *content* is exercised end to end by the
        // render tests instead.

        [Test]
        public void Ramp_IsCachedByContent() {
            var a = ShaderEffectBridge.BuildRamp(new float[] { 0, 0, 0, 0, 1, 1, 1, 1 });
            var b = ShaderEffectBridge.BuildRamp(new float[] { 0, 0, 0, 0, 1, 1, 1, 1 });
            var c = ShaderEffectBridge.BuildRamp(new float[] { 0, 0, 0, 0, 1, 0, 0, 1 });
            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "identical stops should reuse one texture, not allocate per render");
            Assert.AreNotSame(a, c, "different stops are a different ramp");
            Assert.AreEqual(256, a.width);
            Assert.AreEqual(1, a.height);
        }

        [Test]
        public void Ramp_RejectsFewerThanTwoStops() {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ramp"));
            Assert.IsNull(ShaderEffectBridge.BuildRamp(new float[] { 1, 1, 1, 1 }));
        }

        [Test]
        public void Ramp_RejectsRaggedStops() {
            // Long enough to clear the two-stop minimum, so this exercises the
            // 4-floats-per-stop check rather than the length one.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ramp"));
            Assert.IsNull(ShaderEffectBridge.BuildRamp(new float[] { 1, 1, 1, 1, 0, 0, 0, 1, 0, 0 }));
        }

        [Test]
        public void BuiltinTexture_UnknownNameWarnsAndReturnsNull() {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not-a-texture"));
            Assert.IsNull(ShaderEffectBridge.GetBuiltinTexture("not-a-texture"));
        }

        [Test]
        public void BuiltinTexture_NoiseIsCachedPerSeed() {
            var a = ShaderEffectBridge.GetBuiltinTexture("noise:1");
            var b = ShaderEffectBridge.GetBuiltinTexture("noise:1");
            var c = ShaderEffectBridge.GetBuiltinTexture("noise:2");
            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "same seed should hit the cache");
            Assert.AreNotSame(a, c, "a different seed is a different field");
            // Repeat wrap is what lets the shader scroll it forever without a seam.
            Assert.AreEqual(TextureWrapMode.Repeat, a.wrapMode);
        }

        [Test]
        public void BuiltinTexture_MasksClampSoScrollingCannotWrapThem() {
            foreach (var name in new[] { "flame-mask", "radial-mask" }) {
                var tex = ShaderEffectBridge.GetBuiltinTexture(name);
                Assert.IsNotNull(tex, name);
                Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode, name);
            }
        }
    }
}
