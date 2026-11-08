using System;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Lighting {
    /// <summary>
    /// The light file (Runtime/Lighting/LightFile.cs): a lightmap's pages and its probes into one PNG and
    /// back, the probes to a byte against their own size and the light to within RGBM's dithered
    /// rounding, a one-page file laid out as files were before pages, a file whose probes are floats
    /// (from before 1.4.1) still read exactly, and a file for another atlas refused.
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
        public void Encode_ThenDecode_GivesTheProbesToAByteAndTheLightToItsRounding(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            var light = Light();
            var probes = Probes(3);
            var png = LightFile.Encode(light, Width, Height, 1, probes);
            Assert.That(png[1], Is.EqualTo((byte)'P'));

            Assert.That(LightFile.Decode(png, Width, Height, 1, 3, out var tex, out var read), Is.Empty);
            try {
                AssertProbes(read, probes);
                Assert.That(tex.width, Is.EqualTo(Width));
                Assert.That(tex.height, Is.EqualTo(Height));
                Assert.That(tex.depth, Is.EqualTo(1));
                var back = Slice(tex, 0);
                for (var i = 0; i < light.Length; i++) {
                    // Checked in place, so a file read upside down fails here.
                    if (i == 6) continue;
                    var want = light[i];
                    var max = Mathf.Max(want.r, Mathf.Max(want.g, want.b));
                    // RGBM rounds each channel to a 255th of the texel's multiplier, which is a 255th of the range at
                    // least, and the dither's half step can carry the rounding to the next one.
                    var step = 1.01f * Mathf.Max(max, LightFile.Range / 255) / 255 + 2e-3f * max;
                    Assert.That(back[i].r, Is.EqualTo(want.r).Within(step), $"texel {i} red");
                    Assert.That(back[i].g, Is.EqualTo(want.g).Within(step), $"texel {i} green");
                    Assert.That(back[i].b, Is.EqualTo(want.b).Within(step), $"texel {i} blue");
                }
                Assert.That(back[6].r, Is.EqualTo(LightFile.Range).Within(0.01f), "light past the range is held at it");
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        // Each probe's floats to within half a step of a byte against its largest: 127 steps a side.
        static void AssertProbes(float[] read, float[] want) {
            Assert.That(read.Length, Is.EqualTo(want.Length));
            for (var p = 0; p < want.Length / ProbeSolve.Floats; p++) {
                var size = 0f;
                for (var k = 0; k < ProbeSolve.Floats; k++) size = Mathf.Max(size, Mathf.Abs(want[p * ProbeSolve.Floats + k]));
                for (var k = 0; k < ProbeSolve.Floats; k++) {
                    var i = p * ProbeSolve.Floats + k;
                    Assert.That(read[i], Is.EqualTo(want[i]).Within(size / 254 * 1.001f), $"probe {p} coefficient {k}");
                }
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
            // The first probe's size from the bottom row's first pixel, the light's RGBM from the row above the last probe's.
            var size = 0f;
            for (var k = 0; k < ProbeSolve.Floats; k++) size = Mathf.Max(size, Mathf.Abs(probes[k]));
            Assert.That(new[] { raw[0].r, raw[0].g, raw[0].b, raw[0].a }, Is.EqualTo(BitConverter.GetBytes(size)));
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
                AssertProbes(read, probes);
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
        public void ProbeRows_IsEnoughRowsForEveryProbesEightPixels() {
            Assert.That(LightFile.ProbePixels, Is.EqualTo(8));
            Assert.That(LightFile.ProbeRows(0, 8), Is.EqualTo(0));
            Assert.That(LightFile.ProbeRows(1, 8), Is.EqualTo(1));
            Assert.That(LightFile.ProbeRows(1, 7), Is.EqualTo(2));
            Assert.That(LightFile.ProbeRows(3888, 496), Is.EqualTo(63), "Crate Yard's probes: 63 rows, where they took 212 as floats");
        }

        // A file as 1.4.0 wrote it: the probes' 27 floats a pixel each, under the pages' RGBM rounded to the nearest step.
        static byte[] FloatFile(Color[] light, int pages, float[] probes) {
            var rows = (probes.Length + Width - 1) / Width;
            var raw = new byte[Width * (rows + Height * pages) * 4];
            Buffer.BlockCopy(probes, 0, raw, 0, probes.Length * 4);
            var at = Width * rows * 4;
            for (var page = pages - 1; page >= 0; page--) {
                for (int i = page * Width * Height, end = i + Width * Height; i < end; i++) {
                    var c = light[i];
                    var max = Mathf.Max(Mathf.Max(c.r, c.g), Mathf.Max(c.b, 1e-6f));
                    var m = Mathf.Clamp(Mathf.CeilToInt(max / LightFile.Range * 255), 1, 255);
                    var scale = 255 / (m / 255f * LightFile.Range);
                    raw[at++] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * scale), 0, 255);
                    raw[at++] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * scale), 0, 255);
                    raw[at++] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * scale), 0, 255);
                    raw[at++] = (byte)m;
                }
            }
            var tex = new Texture2D(Width, rows + Height * pages, TextureFormat.RGBA32, false, true);
            try {
                tex.LoadRawTextureData(raw);
                return tex.EncodeToPNG();
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Decode_ReadsAFileWhoseProbesAreFloats_AsBefore141_Exactly() {
            var light = Light(2);
            var probes = Probes(3);
            Assert.That(LightFile.Decode(FloatFile(light, 2, probes), Width, Height, 2, 3, out var tex, out var read), Is.Empty);
            try {
                Assert.That(read, Is.EqualTo(probes), "every probe float, bit for bit");
                var back = Slice(tex, 1);
                for (var i = 0; i < back.Length; i++) Assert.That(back[i].g, Is.EqualTo(light[Width * Height + i].g).Within(0.04f), $"page 1 texel {i}");
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        // A dim slow gradient lands on a few 8-bit steps: rounded to the nearest, a run of texels takes one and the
        // light shows rings; dithered, the run's texels mix the steps either side, and their mean is the light's.
        // One flat light 0.44 of a step past a step is the run: rounded, every texel is the next step up.
        [Test]
        public void Encode_DithersASlowGradientRatherThanBandingIt() {
            const int wide = 64, high = 16;
            var light = new Color[wide * high];
            for (var i = 0; i < light.Length; i++) light[i] = new Color(0.02f, 0.02f, 0.02f, 1);
            Assert.That(LightFile.Decode(LightFile.Encode(light, wide, high, 1, Array.Empty<float>()), wide, high, 1, 0, out var tex, out _), Is.Empty);
            try {
                var rt = new RenderTexture(wide, high, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                rt.Create();
                Graphics.CopyTexture(tex, 0, 0, rt, 0, 0);
                var back = TextureRead.Start(rt).Wait().Colors();
                rt.Release();
                Object.DestroyImmediate(rt);
                var levels = new System.Collections.Generic.HashSet<float>();
                float sum = 0, want = 0;
                for (var i = 0; i < back.Length; i++) {
                    levels.Add(back[i].r);
                    sum += back[i].r;
                    want += light[i].r;
                }
                Assert.That(levels.Count, Is.GreaterThanOrEqualTo(2), "a gradient under a step dithers over the steps either side");
                // The step here: the multiplier for 0.02 is 1, so a step is Range / 255 / 255.
                var step = LightFile.Range / 255 / 255;
                Assert.That(sum / back.Length, Is.EqualTo(want / back.Length).Within(step * 0.1f), "and its mean is the light's, to a tenth of a step");
            } finally {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
