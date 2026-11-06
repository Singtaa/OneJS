using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// A font made at runtime from a file's bytes, for text to draw with.
    ///
    /// Unity reads a font only from a path (<c>new Font(path)</c>), and a font a
    /// game downloads, which is every font on WebGL, has bytes and no path. So the
    /// bytes are written once to the temporary cache (the in-memory file system on
    /// WebGL) under their own hash, and the font is read from there. The file stays
    /// for as long as the font may need it: a dynamic font reads its glyphs from
    /// the file as text asks for them.
    /// </summary>
    public static class Fonts {
        /// <summary>
        /// A dynamic SDF font asset from a TrueType or OpenType file's bytes, or
        /// null when they are not a font Unity can read. <paramref name="name"/>
        /// names the asset and keeps the file's extension.
        /// </summary>
        public static FontAsset FromBytes(byte[] bytes, string name) {
            if (bytes == null || bytes.Length == 0) return null;
            var path = CachedFile(bytes, name);
            var font = new Font(path);
            var asset = FontAsset.CreateFontAsset(font);
            if (asset == null) {
                if (Application.isPlaying) UnityEngine.Object.Destroy(font);
                else UnityEngine.Object.DestroyImmediate(font);
                return null;
            }
            asset.name = Path.GetFileNameWithoutExtension(name ?? "font");
            return asset;
        }

        /// <summary>
        /// Lets go of a font's memory: its atlas, where nearly all of it is,
        /// shrinks to nothing and its glyphs are dropped. Text still naming it
        /// draws nothing new; asked again, it would fill its atlas afresh.
        ///
        /// Not Destroy. A font that drew new glyphs this frame has its atlas
        /// queued for the text system to apply, and destroying it before that
        /// throws MissingReferenceException from every panel's next text
        /// update, in code nobody can see. The object left behind is small.
        /// </summary>
        public static void Release(FontAsset asset) {
            if (asset == null) return;
            asset.ClearFontAssetData(true);
        }

        /// <summary>
        /// What a style's font takes: a font asset, a legacy font, or a font
        /// definition as it is, so `fontFamily` and `unityFontDefinition` accept the
        /// same values. Null for anything else.
        /// </summary>
        public static FontDefinition? DefinitionOf(object value) {
            switch (value) {
                case FontAsset asset: return FontDefinition.FromSDFFont(asset);
                case Font font: return FontDefinition.FromFont(font);
                case FontDefinition definition: return definition;
                case StyleFontDefinition style: return style.value;
                default: return null;
            }
        }

        static string CachedFile(byte[] bytes, string name) {
            var dir = Path.Combine(Application.temporaryCachePath, "onejs-fonts");
            Directory.CreateDirectory(dir);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes), 0, 12).Replace("-", "").ToLowerInvariant();
            var extension = Path.GetExtension(name ?? "");
            var path = Path.Combine(dir, hash + (string.IsNullOrEmpty(extension) ? ".ttf" : extension.ToLowerInvariant()));
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
