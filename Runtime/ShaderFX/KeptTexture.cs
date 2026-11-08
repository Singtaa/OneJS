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

        /// <summary>Frees a kept texture.</summary>
        public static void Release(RenderTexture texture) {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}
