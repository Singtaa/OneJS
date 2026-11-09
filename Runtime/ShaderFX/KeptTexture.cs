using UnityEngine;

namespace OneJS.ShaderFX {
    /// <summary>
    /// A picture drawn once and kept: an 8-bit sRGB texture made before it is drawn, so
    /// whoever asked for it can hold it at once, and filled from a pass's output when the
    /// pass has drawn. onejs-play's `tex` keeps what it bakes in one of these and frees
    /// the passes that made it.
    ///
    /// Made here rather than from JS because RenderTexture's constructors differ only in
    /// enum types, which a call by reflection cannot tell apart: from JS, `ARGB32` reached
    /// the GraphicsFormat overload as `None` and the texture was never created.
    /// </summary>
    public static class KeptTexture {
        /// <summary>A `width` by `height` sRGB texture, repeating or clamped, bilinear, transparent until filled.</summary>
        public static RenderTexture Create(int width, int height, bool repeat, string name) {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = name,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            if (!Application.isPlaying) rt.hideFlags = HideFlags.DontSave;
            rt.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
            return rt;
        }

        /// <summary>Copies `source` into `into`, converting linear values to sRGB on the way.</summary>
        public static void Fill(Texture source, RenderTexture into) {
            Graphics.Blit(source, into);
        }

        /// <summary>
        /// A finished read's texels as 8-bit stored values, RGBA, row 0 at the top, in base64:
        /// one crossing for the whole picture (a kept texture's own bytes too, which <see cref="Upload"/>
        /// takes back), where reading <see cref="OneJS.Lighting.TextureRead.Texels"/>
        /// from JS is one crossing a float, and ran a 512 picture out of memory.
        /// </summary>
        public static string StoredBytes(OneJS.Lighting.TextureRead read) {
            var texels = read.Texels;
            int w = read.Width, h = read.Height, row = w * 4;
            var bytes = new byte[row * h];
            for (int y = 0; y < h; y++) {
                // Unity's row 0 is the bottom.
                int from = (h - 1 - y) * row, to = y * row;
                for (int i = 0; i < row; i++) {
                    var v = Mathf.Floor(texels[from + i] * 255f + 0.5f);
                    bytes[to + i] = (byte)(v < 0f ? 0f : v > 255f ? 255f : v);
                }
            }
            return System.Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// A finished read's texels as stored, unrounded: RGBA float32, little-endian,
        /// row 0 at the top, in base64. For a 32-bit picture, which 8 bits would round.
        /// </summary>
        public static string StoredFloats(OneJS.Lighting.TextureRead read) {
            var texels = read.Texels;
            int w = read.Width, h = read.Height, row = w * 4;
            var bytes = new byte[row * h * 4];
            for (int y = 0; y < h; y++) {
                // Unity's row 0 is the bottom.
                System.Buffer.BlockCopy(texels, (h - 1 - y) * row * 4, bytes, y * row * 4, row * 4);
            }
            if (!System.BitConverter.IsLittleEndian) {
                for (int i = 0; i < bytes.Length; i += 4) System.Array.Reverse(bytes, i, 4);
            }
            return System.Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Puts bytes <see cref="StoredBytes"/> read from a kept texture the same size back into
        /// `into`, as stored: 8-bit sRGB RGBA, row 0 at the top, in base64. How onejs-play's tex
        /// hands back a picture it kept across loads without drawing it. False, and `into`
        /// untouched, when the bytes are not `into`'s size.
        /// </summary>
        public static bool Upload(string base64, RenderTexture into) {
            if (into == null || string.IsNullOrEmpty(base64)) return false;
            byte[] bytes;
            try {
                bytes = System.Convert.FromBase64String(base64);
            } catch (System.FormatException) {
                return false;
            }
            int w = into.width, h = into.height, row = w * 4;
            if (bytes.Length != row * h) return false;
            var flipped = new byte[bytes.Length];
            // Unity's row 0 is the bottom.
            for (int y = 0; y < h; y++) System.Buffer.BlockCopy(bytes, y * row, flipped, (h - 1 - y) * row, row);
            var staging = new Texture2D(w, h, TextureFormat.RGBA32, false, !into.sRGB);
            try {
                staging.LoadRawTextureData(flipped);
                staging.Apply(false, false);
                // A copy moves the bytes unchanged; a blit decodes and encodes sRGB on the way, which
                // returns the same bytes where the GPU's conversion is exact, and is the fallback.
                if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.TextureToRT) != 0) {
                    Graphics.CopyTexture(staging, into);
                } else {
                    Graphics.Blit(staging, into);
                }
            } finally {
                if (Application.isPlaying) Object.Destroy(staging);
                else Object.DestroyImmediate(staging);
            }
            return true;
        }

        /// <summary>Frees a kept texture.</summary>
        public static void Release(RenderTexture texture) {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}
