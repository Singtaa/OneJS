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
        /// one crossing for the whole picture, where reading <see cref="OneJS.Lighting.TextureRead.Texels"/>
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

        /// <summary>Frees a kept texture.</summary>
        public static void Release(RenderTexture texture) {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}
