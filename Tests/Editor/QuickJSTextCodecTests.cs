using NUnit.Framework;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// TextDecoder and TextEncoder, UTF-8 only, native beside the engine. Glue
    /// that WebAssembly toolchains generate makes a decoder when it loads, and
    /// most npm code that handles bytes reaches for one.
    /// </summary>
    [TestFixture]
    public class QuickJSTextCodecTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() => _ctx = new QuickJSContext();

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
        }

        string Eval(string code) => _ctx.Eval(code);

        string Thrown(string code) => Eval($"(() => {{ try {{ {code}; return 'no throw' }} catch (x) {{ return String(x) }} }})()");

        [Test]
        public void DecodesUtf8FromEveryKindOfView() {
            Assert.AreEqual("€A", Eval("new TextDecoder().decode(new Uint8Array([0xe2, 0x82, 0xac, 0x41]))"));
            Assert.AreEqual("€A", Eval("new TextDecoder('utf-8').decode(new Uint8Array([0xe2, 0x82, 0xac, 0x41]).buffer)"));
            Assert.AreEqual("AB", Eval("new TextDecoder().decode(new Uint8Array([0x78, 0x41, 0x42, 0x79]).subarray(1, 3))"));
            Assert.AreEqual("AB", Eval("new TextDecoder().decode(new DataView(new Uint8Array([0x78, 0x41, 0x42, 0x79]).buffer, 1, 2))"));
            Assert.AreEqual("", Eval("new TextDecoder().decode()"));
        }

        [Test]
        public void ReplacesBadBytesUnlessFatal() {
            Assert.AreEqual("a�b", Eval("new TextDecoder().decode(new Uint8Array([0x61, 0xff, 0x62]))"));
            StringAssert.StartsWith("TypeError", Thrown("new TextDecoder('utf-8', { fatal: true }).decode(new Uint8Array([0xff]))"));
        }

        [Test]
        public void DropsTheByteOrderMarkUnlessAsked() {
            Assert.AreEqual("1", Eval("new TextDecoder().decode(new Uint8Array([0xef, 0xbb, 0xbf, 0x68])).length"));
            Assert.AreEqual("2", Eval("new TextDecoder('utf-8', { ignoreBOM: true }).decode(new Uint8Array([0xef, 0xbb, 0xbf, 0x68])).length"));
        }

        [Test]
        public void RefusesOtherEncodings() {
            StringAssert.StartsWith("RangeError", Thrown("new TextDecoder('latin1')"));
        }

        [Test]
        public void EncodesAndRoundTrips() {
            Assert.AreEqual("226,130,172,97", Eval("Array.from(new TextEncoder().encode('€a')).join(',')"));
            Assert.AreEqual("4 😀", Eval("(() => { const b = new TextEncoder().encode('😀'); return `${b.length} ${new TextDecoder().decode(b)}` })()"));
            Assert.AreEqual("true", Eval("String(new TextEncoder().encode('x') instanceof Uint8Array)"));
        }

        [Test]
        public void EncodeIntoWritesWholeCharactersOnly() {
            Assert.AreEqual("2 4 97,226,130,172", Eval(
                "(() => { const b = new Uint8Array(4); const r = new TextEncoder().encodeInto('a€b', b); return `${r.read} ${r.written} ${Array.from(b).join(',')}` })()"));
            Assert.AreEqual("1 1", Eval(
                "(() => { const r = new TextEncoder().encodeInto('a€', new Uint8Array(3)); return `${r.read} ${r.written}` })()"));
        }

        [Test]
        public void ReportsItsSettings() {
            Assert.AreEqual("utf-8 true false utf-8", Eval(
                "(() => { const d = new TextDecoder('utf8', { fatal: true }); return `${d.encoding} ${d.fatal} ${d.ignoreBOM} ${new TextEncoder().encoding}` })()"));
        }
    }
}
