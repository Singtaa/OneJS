using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// A font a game ships, made from its file's bytes, and applied with the
    /// style key a web author reaches for. On WebGL every file a game has is a
    /// URL, and Unity reads a font only from a path, so no font a cart shipped
    /// could reach a Text (PlaySite docs/engine-findings.md). `Fonts.FromBytes`
    /// is the step between the download and the style.
    /// </summary>
    [TestFixture]
    public class FontFromBytesTests {
        readonly List<Object> _made = new();

        PanelHost _panel;

        [TearDown]
        public void TearDown() {
            foreach (var o in _made) Fonts.Release(o as FontAsset);
            _made.Clear();
            _panel?.Destroy();
            _panel = null;
        }

        /// The bytes of the TrueType file OneJS ships, read as a game's download would arrive.
        static byte[] FontBytes([CallerFilePath] string here = "") =>
            File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(here), "..", "Resources", "OneJS", "Consolas-Regular.ttf"));

        FontAsset Made(byte[] bytes, string name) {
            var asset = Fonts.FromBytes(bytes, name);
            if (asset != null) _made.Add(asset);
            return asset;
        }

        [Test]
        public void Bytes_BecomeADynamicFont_ThatDrawsTheirGlyphs() {
            var asset = Made(FontBytes(), "Display.ttf");
            Assert.IsNotNull(asset);
            Assert.AreEqual("Display", asset.name);
            Assert.AreEqual(AtlasPopulationMode.Dynamic, asset.atlasPopulationMode);
            Assert.IsTrue(asset.HasCharacters("Ag7", out _, false, true), "the font made glyphs for the text asked of it");
        }

        /// Destroying a font that drew new glyphs in the frame threw
        /// MissingReferenceException (a destroyed Texture2D) from the next
        /// panel's text update: its atlas was still queued for applying. Found
        /// as three unrelated theme tests failing after this fixture.
        [UnityTest]
        public System.Collections.IEnumerator Release_TheFrameItDrewNewGlyphs_LeavesTextDrawing() {
            // The panel has no theme and says so; what this listens for is an exception.
            var thrown = new List<string>();
            void Heard(string text, string stack, LogType type) {
                if (type == LogType.Exception || type == LogType.Error) thrown.Add(text);
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Heard;
            var asset = Made(FontBytes(), "Display.ttf");
            _panel = PanelHost.Create(320, 80);
            var label = _panel.Add(new Label("Ag"), 320, 80);
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(asset);
            yield return null;
            yield return null;

            Assert.IsTrue(asset.TryAddCharacters("Qz9#", out _), "new glyphs, queued for the text system to apply");
            label.style.unityFontDefinition = StyleKeyword.Null;
            Fonts.Release(asset);
            label.text = "Plain again";
            yield return null;
            yield return null;

            Application.logMessageReceived -= Heard;
            LogAssert.ignoreFailingMessages = false;
            Assert.IsEmpty(thrown, "the next frames' text updates threw");
            Assert.LessOrEqual(asset.atlasTexture.width, 1, "the atlas, where the memory is, is gone");
        }

        [Test]
        public void TheSameBytesTwice_ShareOneFile() {
            var bytes = FontBytes();
            Made(bytes, "a.ttf");
            Made(bytes, "b.ttf");
            var files = Directory.GetFiles(Path.Combine(Application.temporaryCachePath, "onejs-fonts"));
            Assert.AreEqual(1, System.Array.FindAll(files, f => File.ReadAllBytes(f).Length == bytes.Length).Length);
        }

        [Test]
        public void BytesThatAreNotAFont_GiveNull() {
            LogAssert.ignoreFailingMessages = true;
            try {
                Assert.IsNull(Made(System.Text.Encoding.UTF8.GetBytes("<!doctype html><title>404</title>"), "missing.ttf"));
                Assert.IsNull(Made(new byte[0], "empty.ttf"));
            } finally {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void FontFamily_TakesTheFont_AndNullClearsIt() {
            var asset = Made(FontBytes(), "Display.ttf");
            var element = new Label("Ag");
            StyleBridge.ApplyStyles(element, new Dictionary<string, object> { { "fontFamily", asset } });
            Assert.AreEqual(asset, element.style.unityFontDefinition.value.fontAsset);

            StyleBridge.ApplyStyles(element, new Dictionary<string, object> { { "fontFamily", null } });
            Assert.AreEqual(StyleKeyword.Null, element.style.unityFontDefinition.keyword);
        }

        [Test]
        public void UnityFontDefinition_TakesTheFontAssetToo() {
            var asset = Made(FontBytes(), "Display.ttf");
            var element = new Label("Ag");
            StyleBridge.ApplyStyles(element, new Dictionary<string, object> { { "unityFontDefinition", asset } });
            Assert.AreEqual(asset, element.style.unityFontDefinition.value.fontAsset);
        }

        [Test]
        public void FontFamily_GivenAName_SaysWhatItTakes_AndSetsNothing() {
            var element = new Label("Ag");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"fontFamily takes a font a game loaded \(loadFont\), not the name ""Arial"""));
            StyleBridge.ApplyStyles(element, new Dictionary<string, object> { { "fontFamily", "Arial" } });
            Assert.AreEqual(StyleKeyword.Null, element.style.unityFontDefinition.keyword);
        }
    }
}
