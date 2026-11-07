using System;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Lighting {
    /// <summary>
    /// The light file (Runtime/Lighting/LightFile.cs): a lightmap's pages and its probes into one PNG and
    /// back, the probes exactly and the light to within RGBM's rounding, a one-page file laid out as
    /// files were before pages, and a file for another atlas refused.
    /// </summary>
    public class LightFileTests {
        const int Width = 8, Height = 4;

        static Color[] Light(int pages = 1) {
            var light = new Color[Width * Height * pages];
            for (var i = 0; i < light.Length; i++) light[i] = new Color(i * 0.05f, 0.5f / (i + 1), i % 3 == 0 ? 0 : 1.7f, 1);
            light[5] = new Color(7.5f, 0.001f, 3, 1);
            light[6] = new Color(20, 0, 0, 1);
            // Each later page its own, so a page read from another's rows fails.
            for (var i = Width * Height; i < light.Length; i++) light[i] = new Color(0.1f * (i / (Width * Height)), (i % Width) * 0.2f, 0.3f, 1);
            return light;
        }

        static float[] Probes(int count) {
            var probes = new float[count * ProbeSolve.Floats];
            for (var i = 0; i < probes.Length; i++) probes[i] = (i % 7 - 3) * 0.123456789f + i * 1e-5f;
            probes[3] = float.Epsilon;
            return probes;
        }

        [TearDown]
        public void ReadAsTheGpuDoes() => TextureRead.UseReadPixels = false;

        // Read back async, as both browser backends do, and by ReadPixels, as a GPU without async readback does.
        [TestCase(false)]
        [TestCase(true)]
        public void Encode_ThenDecode_GivesTheProbesExactlyAndTheLightToItsRounding(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            var light = Light();
            var probes = Probes(3);
            var png = LightFile.Encode(light, Width, Height, 1, probes);
            Assert.That(png[1], Is.EqualTo((byte)'P'));

            Assert.That(LightFile.Decode(png, Width, Height, 1, 3, out var tex, out var read), Is.Empty);
            try {
                Assert.That(read, Is.EqualTo(probes), "every probe float, bit for bit");
                Assert.That(tex.width, Is.EqualTo(Width));
                Assert.That(tex.height, Is.EqualTo(Height));
                Assert.That(tex.depth, Is.EqualTo(1));
                var back = Slice(tex, 0);
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

        // The texture is the GPU's alone once made, so a slice is read back as ModelLit would see it.
        static Color[] Slice(Texture2DArray tex, int slice) {
            var rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try {
                rt.Create();
                Graphics.CopyTexture(tex, slice, 0, rt, 0, 0);
                return TextureRead.Start(rt).Wait().Colors();
            } finally {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // The file's own pixels, row 0 at the picture's bottom as Unity reads a PNG.
        static Color32[] Pixels(byte[] png, out int width, out int height) {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try {
                Assert.That(tex.LoadImage(png, false), Is.True);
                width = tex.width;
                height = tex.height;
                return tex.GetPixels32();
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void AOnePageFile_IsTheLightOverTheProbes_AsBeforePages() {
            var light = Light();
            var probes = Probes(3);
            var raw = Pixels(LightFile.Encode(light, Width, Height, 1, probes), out var w, out var h);
            var rows = LightFile.ProbeRows(3, Width);
            Assert.That((w, h), Is.EqualTo((Width, rows + Height)));
            // The probes' bytes from the bottom row, the light's RGBM from the row above the last of them.
            var first = BitConverter.GetBytes(probes[0]);
            Assert.That(new[] { raw[0].r, raw[0].g, raw[0].b, raw[0].a }, Is.EqualTo(first));
            for (var i = 0; i < light.Length; i++) {
                var p = raw[Width * rows + i];
                var m = p.a / 255f * LightFile.Range / 255f;
                Assert.That(p.g * m, Is.EqualTo(Mathf.Min(light[i].g, LightFile.Range)).Within(0.04f), $"texel {i}");
            }
        }

        [Test]
        public void Pages_StackTopToBottom_TheFirstPageAtTheTop_AndEachReadsBackToItsSlice() {
            const int pages = 3;
            var light = Light(pages);
            var probes = Probes(3);
            var png = LightFile.Encode(light, Width, Height, pages, probes);
            var raw = Pixels(png, out var w, out var h);
            var rows = LightFile.ProbeRows(3, Width);
            Assert.That((w, h), Is.EqualTo((Width, rows + Height * pages)));
            // The first page's rows are the top of the picture, as a one-page file of it alone has them.
            var alone = Pixels(LightFile.Encode(light[..(Width * Height)], Width, Height, 1, probes), out _, out _);
            for (var i = 0; i < Width * Height; i++) Assert.That(raw[Width * (rows + Height * (pages - 1)) + i], Is.EqualTo(alone[Width * rows + i]), $"page 0 texel {i}");

            Assert.That(LightFile.Decode(png, Width, Height, pages, 3, out var tex, out var read), Is.Empty);
            try {
                Assert.That(read, Is.EqualTo(probes));
                Assert.That(tex.depth, Is.EqualTo(pages));
                for (var page = 0; page < pages; page++) {
                    var back = Slice(tex, page);
                    for (var i = 0; i < back.Length; i++) {
                        if (page == 0 && i == 6) continue;
                        var want = light[page * Width * Height + i];
                        Assert.That(back[i].g, Is.EqualTo(want.g).Within(0.04f), $"page {page} texel {i}");
                        Assert.That(back[i].r, Is.EqualTo(want.r).Within(0.04f), $"page {page} texel {i}");
                    }
                }
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Decode_RefusesAFileForAnotherAtlasOrProbeCount() {
            var png = LightFile.Encode(Light(), Width, Height, 1, Probes(3));
            Assert.That(LightFile.Decode(png, Width * 2, Height, 1, 3, out _, out _), Does.Contain("needs 16 by"));
            Assert.That(LightFile.Decode(png, Width, Height, 1, 300, out var tex, out _), Does.Contain("300 probes"));
            Assert.That(tex, Is.Null);
            Assert.That(LightFile.Decode(png, Width, Height, 2, 3, out _, out _), Does.Contain("2 pages"));
            Assert.That(LightFile.Decode(new byte[] { 1, 2, 3 }, Width, Height, 1, 3, out _, out _), Does.Contain("not a picture"));
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
