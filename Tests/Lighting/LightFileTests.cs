using System;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Lighting {
    /// <summary>
    /// The light file (Runtime/Lighting/LightFile.cs): a lightmap and its probes into one PNG and back,
    /// the probes exactly and the light to within RGBM's rounding, and a file for another atlas refused.
    /// </summary>
    public class LightFileTests {
        const int Width = 8, Height = 4;

        static Color[] Light() {
            var light = new Color[Width * Height];
            for (var i = 0; i < light.Length; i++) light[i] = new Color(i * 0.05f, 0.5f / (i + 1), i % 3 == 0 ? 0 : 1.7f, 1);
            light[5] = new Color(7.5f, 0.001f, 3, 1);
            light[6] = new Color(20, 0, 0, 1);
            return light;
        }

        static float[] Probes(int count) {
            var probes = new float[count * ProbeSolve.Floats];
            for (var i = 0; i < probes.Length; i++) probes[i] = (i % 7 - 3) * 0.123456789f + i * 1e-5f;
            probes[3] = float.Epsilon;
            return probes;
        }

        [Test]
        public void Encode_ThenDecode_GivesTheProbesExactlyAndTheLightToItsRounding() {
            var light = Light();
            var probes = Probes(3);
            var png = LightFile.Encode(light, Width, Height, probes);
            Assert.That(png[1], Is.EqualTo((byte)'P'));

            Assert.That(LightFile.Decode(png, Width, Height, 3, out var tex, out var read), Is.Empty);
            try {
                Assert.That(read, Is.EqualTo(probes), "every probe float, bit for bit");
                Assert.That(tex.width, Is.EqualTo(Width));
                Assert.That(tex.height, Is.EqualTo(Height));
                // The texture is the GPU's alone once made, so it is read back as ModelLit would see it.
                var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Color[] back;
                try {
                    Graphics.Blit(tex, rt);
                    back = LightFile.Read(rt);
                } finally {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                for (var i = 0; i < light.Length; i++) {
                    // Checked in place, so a file read upside down fails here.
                    if (i == 6) continue;
                    var want = light[i];
                    var max = Mathf.Max(want.r, Mathf.Max(want.g, want.b));
                    // RGBM rounds each channel to a 255th of the texel's multiplier, which is a 255th of the range at least.
                    var step = Mathf.Max(max, LightFile.Range / 255) / 255 + 2e-3f * max;
                    Assert.That(back[i].r, Is.EqualTo(want.r).Within(step), $"texel {i} red");
                    Assert.That(back[i].g, Is.EqualTo(want.g).Within(step), $"texel {i} green");
                    Assert.That(back[i].b, Is.EqualTo(want.b).Within(step), $"texel {i} blue");
                }
                Assert.That(back[6].r, Is.EqualTo(LightFile.Range).Within(0.01f), "light past the range is held at it");
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Decode_RefusesAFileForAnotherAtlasOrProbeCount() {
            var png = LightFile.Encode(Light(), Width, Height, Probes(3));
            Assert.That(LightFile.Decode(png, Width * 2, Height, 3, out _, out _), Does.Contain("needs 16 by"));
            Assert.That(LightFile.Decode(png, Width, Height, 300, out var tex, out _), Does.Contain("300 probes"));
            Assert.That(tex, Is.Null);
            Assert.That(LightFile.Decode(new byte[] { 1, 2, 3 }, Width, Height, 3, out _, out _), Does.Contain("not a picture"));
        }

        [Test]
        public void ProbeRows_IsEnoughRowsForEveryFloat() {
            Assert.That(LightFile.ProbeRows(0, 8), Is.EqualTo(0));
            Assert.That(LightFile.ProbeRows(1, 27), Is.EqualTo(1));
            Assert.That(LightFile.ProbeRows(1, 26), Is.EqualTo(2));
            Assert.That(LightFile.ProbeRows(486, 512), Is.EqualTo(26));
        }
    }
}
