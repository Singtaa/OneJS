using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OneJS.Lighting {
    /// <summary>
    /// A finished bake as one PNG, the file onejs-play's `scene.bake(name)` saves and loads
    /// (PlaySite docs/lightmapping.md in the container, "Where the file lives").
    ///
    /// The image is the atlas's width wide. Its top rows, as a picture shows it, are the lightmap's
    /// pages as RGBM in 8 bits, the first at the top and each under the last: a texel's light is its
    /// rgb times its alpha times <see cref="Range"/>, rounded to the nearest step. Not dithered: a
    /// texel's brightest channel takes at least 128 of its 256 steps wherever the light is above a
    /// 255th of the range, so a step is under 1% of the light, and noise of half a step made Crate
    /// Yard's pages 34% larger (review L5). Under the last page are the probes, in grid order, <see cref="ProbePixels"/>
    /// pixels each: the largest of its 27 coefficients' sizes (<see cref="ProbeSolve.Floats"/>) as a
    /// little-endian float, then each coefficient as a byte, 128 for 0 and 1 and 255 for minus and
    /// plus that size, then a byte of 0 (review L4: a quarter of the 27 floats before, which were
    /// most of a file). A blend of eight probes is off by under half a percent of a probe's
    /// largest term. A file from before, its probes as 27 floats a probe, still reads: its height
    /// says which. onejs-play adds its record (the fingerprint and the layout) as a text chunk,
    /// which this skips.
    /// </summary>
    public static class LightFile {
        /// <summary>The brightest light a texel can hold: RGBM's multiplier at its full 255.</summary>
        public const float Range = 8;

        /// <summary>Pixels a probe takes: its scale, its 27 bytes and one of 0.</summary>
        public const int ProbePixels = 8;

        /// <summary>How many rows of the image the probes take, at `width` pixels a row.</summary>
        public static int ProbeRows(int probeCount, int width) => Rows(probeCount * ProbePixels, width);

        // Files before ProbePixels held each coefficient as a float, a pixel each.
        static int FloatProbeRows(int probeCount, int width) => Rows(probeCount * ProbeSolve.Floats, width);

        static int Rows(int pixels, int width) => width <= 0 ? 0 : (pixels + width - 1) / width;

        /// <summary>
        /// The file for `pages` pages of a `width` by `height` lightmap and its probes: `light` holds
        /// each page in turn, linear rgb, row 0 at the bottom, as Unity reads a texture.
        /// </summary>
        public static byte[] Encode(Color[] light, int width, int height, int pages, float[] probes) {
            if (pages < 1) throw new ArgumentException($"a lightmap has a page at least, not {pages}");
            if (light == null || light.Length != width * height * pages) throw new ArgumentException($"{pages} pages of a {width} by {height} lightmap are {width * height * pages} texels");
            probes ??= Array.Empty<float>();
            if (probes.Length % ProbeSolve.Floats != 0) throw new ArgumentException($"{ProbeSolve.Floats} floats a probe");
            var count = probes.Length / ProbeSolve.Floats;
            var rows = ProbeRows(count, width);
            // A texture's row 0 is the picture's bottom, so the probes go in first and sit under the light,
            // then the last page, and the first page last, at the top.
            var raw = new byte[width * (rows + height * pages) * 4];
            for (var p = 0; p < count; p++) {
                var at0 = p * ProbePixels * 4;
                var size = 0f;
                for (var k = 0; k < ProbeSolve.Floats; k++) size = Mathf.Max(size, Mathf.Abs(probes[p * ProbeSolve.Floats + k]));
                var bytes = BitConverter.GetBytes(size);
                if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                Array.Copy(bytes, 0, raw, at0, 4);
                for (var k = 0; k < ProbeSolve.Floats; k++) {
                    var c = size > 0 ? probes[p * ProbeSolve.Floats + k] / size : 0;
                    raw[at0 + 4 + k] = (byte)Mathf.Clamp(128 + Mathf.RoundToInt(c * 127), 1, 255);
                }
            }
            var at = width * rows * 4;
            for (var page = pages - 1; page >= 0; page--) {
                for (int i = page * width * height, end = i + width * height; i < end; i++) {
                    var c = light[i];
                    var max = Mathf.Max(Mathf.Max(c.r, c.g), Mathf.Max(c.b, 1e-6f));
                    // The multiplier rounded up, so the rgb under it never passes 1.
                    var m = Mathf.Clamp(Mathf.CeilToInt(max / Range * 255), 1, 255);
                    var scale = 255 / (m / 255f * Range);
                    raw[at++] = Byte(c.r * scale);
                    raw[at++] = Byte(c.g * scale);
                    raw[at++] = Byte(c.b * scale);
                    raw[at++] = (byte)m;
                }
            }
            var tex = new Texture2D(width, rows + height * pages, TextureFormat.RGBA32, false, true);
            try {
                tex.LoadRawTextureData(raw);
                return tex.EncodeToPNG();
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        static byte Byte(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x), 0, 255);

        /// <summary>
        /// Reads a file made for `pages` pages of a `width` by `height` atlas and `probeCount` probes:
        /// the lightmap as the half-float texture array ModelLit samples, a slice a page, and the
        /// probes' floats. "" when it read, or why not.
        /// </summary>
        public static string Decode(byte[] png, int width, int height, int pages, int probeCount, out Texture2DArray light, out float[] probes) {
            light = null;
            probes = null;
            if (png == null || png.Length == 0) return "the light file is empty";
            if (pages < 1) return $"a lightmap has a page at least, not {pages}";
            var rows = ProbeRows(probeCount, width);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try {
                if (!tex.LoadImage(png, false)) return "the light file is not a picture Unity can read";
                var floats = tex.height != rows + height * pages && tex.height == FloatProbeRows(probeCount, width) + height * pages;
                if (floats) rows = FloatProbeRows(probeCount, width);
                if (tex.width != width || tex.height != rows + height * pages) {
                    var atlas = pages == 1 ? $"a {width} by {height} atlas" : $"{pages} pages of a {width} by {height} atlas";
                    return $"the light file is {tex.width} by {tex.height}, and {atlas} with {probeCount} probes needs {width} by {rows + height * pages}";
                }
                // Whatever format LoadImage chose (ARGB32 on some editors), GetPixels32 is r, g, b, a a pixel.
                var raw = tex.GetPixels32();
                probes = floats ? FloatProbes(raw, probeCount) : ByteProbes(raw, probeCount);

                light = new Texture2DArray(width, height, pages, TextureFormat.RGBAHalf, false, true) {
                    name = "OneJS Light File",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color[width * height];
                for (var page = 0; page < pages; page++) {
                    // The last page sits on the probes, the first at the top.
                    var at = width * (rows + height * (pages - 1 - page));
                    for (var i = 0; i < pixels.Length; i++, at++) {
                        var p = raw[at];
                        var m = p.a / 255f * Range / 255f;
                        pixels[i] = new Color(p.r * m, p.g * m, p.b * m, 1);
                    }
                    light.SetPixels(pixels, page);
                }
                light.Apply(false, true);
                return "";
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        static float[] ByteProbes(Color32[] raw, int count) {
            var probes = new float[count * ProbeSolve.Floats];
            var size = new byte[4];
            for (var p = 0; p < count; p++) {
                var px = p * ProbePixels;
                var first = raw[px];
                size[0] = first.r; size[1] = first.g; size[2] = first.b; size[3] = first.a;
                if (!BitConverter.IsLittleEndian) Array.Reverse(size);
                var scale = BitConverter.ToSingle(size, 0) / 127f;
                for (var k = 0; k < ProbeSolve.Floats; k++) {
                    var b = 4 + k;
                    var pixel = raw[px + b / 4];
                    var q = (b % 4) switch { 0 => pixel.r, 1 => pixel.g, 2 => pixel.b, _ => pixel.a };
                    probes[p * ProbeSolve.Floats + k] = (q - 128) * scale;
                }
            }
            return probes;
        }

        static float[] FloatProbes(Color32[] raw, int count) {
            var probes = new float[count * ProbeSolve.Floats];
            var bytes = new byte[probes.Length * 4];
            for (var i = 0; i < bytes.Length; i += 4) {
                var p = raw[i / 4];
                bytes[i] = p.r;
                bytes[i + 1] = p.g;
                bytes[i + 2] = p.b;
                bytes[i + 3] = p.a;
            }
            if (!BitConverter.IsLittleEndian) {
                for (var i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);
            }
            Buffer.BlockCopy(bytes, 0, probes, 0, bytes.Length);
            return probes;
        }
    }
}
