using System.Reflection;
using NUnit.Framework;
using OneJS.ShaderFX;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A pass's target at 16 or 32 bits a channel. A 32-bit one is point
    /// filtered: a float texture many WebGL2 and WebGPU devices cannot filter
    /// samples black there when a sampler asks to filter it.
    /// </summary>
    [TestFixture]
    [Category("RequiresGraphics")]
    public class ShaderPassBitsTests {
        static RenderTexture TargetOf(ShaderPass pass) {
            var m = typeof(ShaderPass).GetMethod("CreateTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "ShaderPass.CreateTarget is gone.");
            return (RenderTexture)m.Invoke(pass, new object[] { 8, 8 });
        }

        [Test]
        public void AFloatPassIsKeptAt32BitsUnfiltered() {
            Assume.That(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat), "This device cannot render floats.");
            var pass = new ShaderPass { Bits = 32 };
            var rt = TargetOf(pass);
            try {
                Assert.AreEqual(32, pass.Bits);
                Assert.AreEqual(RenderTextureFormat.ARGBFloat, rt.format);
                Assert.AreEqual(FilterMode.Point, rt.filterMode);
            } finally {
                Object.DestroyImmediate(rt);
                pass.Dispose();
            }
        }

        [Test]
        public void APassIsHalfFloatsBilinearUnlessAsked() {
            var pass = new ShaderPass { Bits = 24 };
            var rt = TargetOf(pass);
            try {
                Assert.AreEqual(16, pass.Bits, "Anything but 32 is 16.");
                Assert.AreEqual(RenderTextureFormat.ARGBHalf, rt.format);
                Assert.AreEqual(FilterMode.Bilinear, rt.filterMode);
            } finally {
                Object.DestroyImmediate(rt);
                pass.Dispose();
            }
        }
    }
}
