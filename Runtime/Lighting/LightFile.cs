using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OneJS.Lighting {
    /// <summary>
    /// A finished bake as one PNG, the file onejs-play's `scene.bake(name)` saves and loads
    /// (PlaySite docs/lightmapping.md in the container, "Where the file lives").
    ///
    /// The image is the atlas's width wide. Its top rows, as a picture shows it, are the lightmap as
    /// RGBM in 8 bits: a texel's light is its rgb times its alpha times <see cref="Range"/>. Under them
    /// are the probes, each of the 27 floats a probe holds (<see cref="ProbeSolve.Floats"/>) written
    /// as its four little-endian bytes into one pixel's r, g, b and a, the probes in grid order. So a
    /// file is read back exactly as the baker wrote its probes, and to within RGBM's rounding its light.
    /// onejs-play adds its record (the fingerprint and the layout) as a text chunk, which this skips.
    /// </summary>
    public static class LightFile {
        /// <summary>The brightest light a texel can hold: RGBM's multiplier at its full 255.</summary>
        public const float Range = 8;

        /// <summary>How many rows of the image the probes take, at `width` pixels a row.</summary>
        public static int ProbeRows(int probeCount, int width) =>
            width <= 0 ? 0 : (probeCount * ProbeSolve.Floats + width - 1) / width;

        /// <summary>The file for a `width` by `height` lightmap (linear rgb, row 0 at the bottom, as Unity reads a texture) and its probes.</summary>
        public static byte[] Encode(Color[] light, int width, int height, float[] probes) {
            if (light == null || light.Length != width * height) throw new ArgumentException($"a {width} by {height} lightmap is {width * height} texels");
            probes ??= Array.Empty<float>();
            if (probes.Length % ProbeSolve.Floats != 0) throw new ArgumentException($"{ProbeSolve.Floats} floats a probe");
            var rows = ProbeRows(probes.Length / ProbeSolve.Floats, width);
            // A texture's row 0 is the picture's bottom, so the probes go in first and sit under the light.
            var raw = new byte[width * (rows + height) * 4];
            Buffer.BlockCopy(probes, 0, raw, 0, probes.Length * 4);
            if (!BitConverter.IsLittleEndian) {
                for (var i = 0; i < probes.Length * 4; i += 4) Array.Reverse(raw, i, 4);
            }
            var at = width * rows * 4;
            foreach (var c in light) {
                var max = Mathf.Max(Mathf.Max(c.r, c.g), Mathf.Max(c.b, 1e-6f));
                // The multiplier rounded up, so the rgb under it never passes 1.
                var m = Mathf.Clamp(Mathf.CeilToInt(max / Range * 255), 1, 255);
                var scale = 255 / (m / 255f * Range);
                raw[at++] = Byte(c.r * scale);
                raw[at++] = Byte(c.g * scale);
                raw[at++] = Byte(c.b * scale);
                raw[at++] = (byte)m;
            }
            var tex = new Texture2D(width, rows + height, TextureFormat.RGBA32, false, true);
            try {
                tex.LoadRawTextureData(raw);
                return tex.EncodeToPNG();
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        static byte Byte(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x), 0, 255);

        /// <summary>
        /// Reads a file made for a `width` by `height` atlas and `probeCount` probes: the lightmap as a
        /// half-float texture ModelLit samples, and the probes' floats. "" when it read, or why not.
        /// </summary>
        public static string Decode(byte[] png, int width, int height, int probeCount, out Texture2D light, out float[] probes) {
            light = null;
            probes = null;
            if (png == null || png.Length == 0) return "the light file is empty";
            var rows = ProbeRows(probeCount, width);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try {
                if (!tex.LoadImage(png, false)) return "the light file is not a picture Unity can read";
                if (tex.width != width || tex.height != rows + height) {
                    return $"the light file is {tex.width} by {tex.height}, and a {width} by {height} atlas with {probeCount} probes needs {width} by {rows + height}";
                }
                // Whatever format LoadImage chose (ARGB32 on some editors), GetPixels32 is r, g, b, a a pixel.
                var raw = tex.GetPixels32();
                probes = new float[probeCount * ProbeSolve.Floats];
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

                var pixels = new Color[width * height];
                for (int i = 0, at = width * rows; i < pixels.Length; i++, at++) {
                    var p = raw[at];
                    var m = p.a / 255f * Range / 255f;
                    pixels[i] = new Color(p.r * m, p.g * m, p.b * m, 1);
                }
                light = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true) {
                    name = "OneJS Light File",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                light.SetPixels(pixels);
                light.Apply(false, true);
                return "";
            } finally {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
