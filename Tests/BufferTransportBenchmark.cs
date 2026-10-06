using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;

namespace OneJS.Tests {
    /// <summary>The C# half of <see cref="BufferTransportBenchmark"/>: each transport's receive and send.</summary>
    public static class BufferTransportFixture {
        public static float[] Source = new float[0];

        static readonly MethodInfo s_Parse = typeof(OneJS.GPU.GPUBridge).GetMethod("ParseFloatArray", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo s_Format = typeof(OneJS.GPU.GPUBridge).GetMethod("FloatArrayToJson", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>What GPUBridge.SetBufferData does with its argument today.</summary>
        public static int TakeJson(string json) => ((float[])s_Parse.Invoke(null, new object[] { json })).Length;

        /// <summary>What GPUBridge.GetReadbackData returns today.</summary>
        public static string GiveJson() => (string)s_Format.Invoke(null, new object[] { Source });

        public static int Length(string s) => s.Length;

        /// <summary>What GPUBridge.SetBufferBits does with its argument.</summary>
        public static int TakeBits(string bits) => OneJS.GPU.BufferBits.Parse(bits).Length;

        /// <summary>What GPUBridge.GetReadbackBits returns.</summary>
        public static string GiveBits() => OneJS.GPU.BufferBits.Format(Source);

        /// <summary>Keeps what JS sent as the buffer GiveJson and GiveBits send back.</summary>
        public static void StoreBits(string bits) => Source = OneJS.GPU.BufferBits.Parse(bits);

        /// <summary>C#-side cost alone: the parse GPUBridge does, timed on a string it already has.</summary>
        public static double ParseUs(string json, int times) {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < times; i++) s_Parse.Invoke(null, new object[] { json });
            return sw.Elapsed.TotalMilliseconds * 1000.0 / times;
        }

        public static double FormatUs(int times) {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < times; i++) s_Format.Invoke(null, new object[] { Source });
            return sw.Elapsed.TotalMilliseconds * 1000.0 / times;
        }
    }

    /// <summary>
    /// #107: a float buffer crossing the bridge as JSON (today: Array.from and
    /// JSON.stringify in JS, a string split and a float.Parse per element in
    /// C#, and the reverse for a readback) against the same words carried as
    /// their int32 bit patterns (<see cref="OneJS.GPU.BufferBits"/>), which is
    /// what GPUBridge.SetBufferBits and GetReadbackBits take and give.
    ///
    /// Explicit: a benchmark, not a check. Read the "[Bench107]" lines.
    /// </summary>
    [TestFixture, Explicit("benchmark"), Category("Benchmark")]
    public class BufferTransportBenchmark {
        const int Floats = 4096;
        const int Calls = 200;
        const int Warmup = 20;

        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            _ctx = new QuickJSContext();
            _ctx.Eval($@"
                globalThis.F = CS.OneJS.Tests.BufferTransportFixture
                globalThis.f32 = new Float32Array({Floats})
                for (let i = 0; i < f32.length; i++) f32[i] = Math.sin(i * 0.01) * 123.456
                globalThis.toBits = function (a) {{ return Array.prototype.join.call(new Int32Array(a.buffer, a.byteOffset, a.length), ',') }}
                globalThis.fromBits = function (s) {{ return new Float32Array(new Int32Array(JSON.parse('[' + s + ']')).buffer) }}
                globalThis.check = 0
                F.StoreBits(toBits(f32))
            ");
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
        }

        /// <summary>Microseconds per call of the JS loop body <paramref name="body"/>, run inside one Eval.</summary>
        double UsPerCall(string body) {
            _ctx.Eval($"for (let k = 0; k < {Warmup}; k++) {{ {body} }}");
            var sw = Stopwatch.StartNew();
            _ctx.Eval($"for (let k = 0; k < {Calls}; k++) {{ {body} }}");
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1000.0 / Calls;
        }

        /// <summary>Where today's JSON write and read spend their time, phase by phase.</summary>
        [Test]
        public void JsonPhases() {
            _ctx.Eval("globalThis.json = JSON.stringify(Array.from(f32))");
            double stringify = UsPerCall("check += JSON.stringify(Array.from(f32)).length");
            double cross = UsPerCall("check += F.Length(json)");
            double parse = double.Parse(_ctx.Eval($"F.ParseUs(json, {Calls})"), System.Globalization.CultureInfo.InvariantCulture);
            double whole = UsPerCall("check += F.TakeJson(JSON.stringify(Array.from(f32)))");
            var line = $"[Bench107] JSON write phases, us/call: JS stringify {stringify:F0}, crossing the string {cross:F0}, C# parse {parse:F0}, whole write {whole:F0}; " +
                       $"NUL survives crossing: {_ctx.Eval("F.Length('a\\u0000b') === 3")}";
            UnityEngine.Debug.Log(line);
            TestContext.WriteLine(line);
        }

        /// <summary>
        /// The same floats as their int32 bit patterns, joined by commas: JS
        /// formats integers and C# parses them by hand, both far cheaper than
        /// float text, and the round trip is exact. Text because a string
        /// crossing the bridge ends at its first NUL (JsonPhases checks it),
        /// which raw float bytes are full of.
        /// </summary>
        [Test]
        public void JsonAgainstBits() {
            Assert.AreEqual(Floats, int.Parse(_ctx.Eval("F.TakeBits(toBits(f32))")), "the bits did not all arrive");
            Assert.AreEqual("true", _ctx.Eval("(function () { const r = fromBits(F.GiveBits()); if (r.length !== f32.length) return false; for (let i = 0; i < r.length; i++) if (r[i] !== f32[i]) return false; return true })()"),
                "the bits did not come back exactly");

            double writeJson = UsPerCall("check += F.TakeJson(JSON.stringify(Array.from(f32)))");
            double writeBits = UsPerCall("check += F.TakeBits(toBits(f32))");
            double readJson = UsPerCall("check += new Float32Array(JSON.parse(F.GiveJson())).length");
            double readBits = UsPerCall("check += fromBits(F.GiveBits()).length");
            var line = $"[Bench107] {Floats} floats, us/call: write json {writeJson:F0}, write bits {writeBits:F0}; read json {readJson:F0}, read bits {readBits:F0}";
            UnityEngine.Debug.Log(line);
            TestContext.WriteLine(line);
        }
    }
}
