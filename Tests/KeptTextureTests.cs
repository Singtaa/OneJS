using System;
using NUnit.Framework;
using OneJS.Lighting;
using OneJS.ShaderFX;
using UnityEngine;

namespace OneJS.Tests {
    /// <summary>
    /// A kept texture's bytes, read with <see cref="KeptTexture.StoredBytes"/> and put back with
    /// <see cref="KeptTexture.Upload"/>, are the picture byte for byte: what onejs-play's tex cache
    /// relies on to hand a second load the picture the first one drew. Read back async and by
    /// ReadPixels, and bytes of another size refused with the texture left as it was.
    /// </summary>
    [TestFixture]
    public class KeptTextureTests {
        const int Width = 16, Height = 8;

        [TearDown]
        public void ReadAsTheGpuDoes() => TextureRead.UseReadPixels = false;

        /// <summary>A picture whose every byte differs from its neighbours', and whose rows differ, so a flip or a channel swap fails.</summary>
        static byte[] Picture() {
            var bytes = new byte[Width * Height * 4];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)((i * 37 + i / (Width * 4) * 11) & 0xff);
            return bytes;
        }

        static string Read(RenderTexture rt) => KeptTexture.StoredBytes(TextureRead.Start(rt).Wait());

        [TestCase(false)]
        [TestCase(true)]
        public void Upload_ThenStoredBytes_GivesTheBytesBack(bool readPixels) {
            TextureRead.UseReadPixels = readPixels;
            var into = KeptTexture.Create(Width, Height, true, "kept");
            try {
                var picture = Convert.ToBase64String(Picture());
                Assert.That(KeptTexture.Upload(picture, into), Is.True);
                Assert.That(Read(into), Is.EqualTo(picture));
            } finally {
                KeptTexture.Release(into);
            }
        }

        [Test]
        public void StoredBytes_OfADrawnPicture_UploadsIntoAnotherAsTheSame() {
            // Drawn as tex fills one: a linear float picture blitted in, encoded to sRGB on the way.
            var source = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            var colours = new Color[Width * Height];
            for (var i = 0; i < colours.Length; i++) colours[i] = new Color(i / (float)colours.Length, (i % 5) / 4f, 0.003f * i, 1 - i / (2f * colours.Length));
            source.SetPixels(colours);
            source.Apply();
            var drawn = KeptTexture.Create(Width, Height, false, "drawn");
            var again = KeptTexture.Create(Width, Height, false, "again");
            try {
                KeptTexture.Fill(source, drawn);
                var bytes = Read(drawn);
                Assert.That(KeptTexture.Upload(bytes, again), Is.True);
                Assert.That(Read(again), Is.EqualTo(bytes));
            } finally {
                UnityEngine.Object.DestroyImmediate(source);
                KeptTexture.Release(drawn);
                KeptTexture.Release(again);
            }
        }

        [Test]
        public void Upload_OfAnotherSize_IsRefusedAndLeavesTheTexture() {
            var into = KeptTexture.Create(Width, Height, false, "kept");
            try {
                var before = Read(into);
                Assert.That(KeptTexture.Upload(Convert.ToBase64String(new byte[Width * Height * 4 - 4]), into), Is.False);
                Assert.That(KeptTexture.Upload("not base64!", into), Is.False);
                Assert.That(Read(into), Is.EqualTo(before));
            } finally {
                KeptTexture.Release(into);
            }
        }
    }
}
