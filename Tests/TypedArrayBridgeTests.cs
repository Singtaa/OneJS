using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>The C# half of <see cref="TypedArrayBridgeTests"/>: keeps what JS sent.</summary>
    public static class TypedArrayFixture {
        public static object Last;

        public static void TakeFloats(float[] values) => Last = values;
        public static void TakeDoubles(double[] values) => Last = values;
        public static void TakeInts(int[] values) => Last = values;
        public static void TakeUInts(uint[] values) => Last = values;
        public static void TakeShorts(short[] values) => Last = values;
        public static void TakeUShorts(ushort[] values) => Last = values;
        public static void TakeBytes(byte[] values) => Last = values;
        public static void TakeSBytes(sbyte[] values) => Last = values;
        public static void TakeLongs(long[] values) => Last = values;
        public static void TakeULongs(ulong[] values) => Last = values;

        /// <summary>The shape PainterBridge, FxBridge and ShaderPass take: object, converted to float[].</summary>
        public static void TakeObject(object value) => Last = value;

        public static int Count(float[] values) => values.Length;

        public static void TakeVectors(Vector3[] values) => Last = values;
    }

    /// <summary>
    /// Typed arrays cross the bridge as their bytes (InteropType.TypedArray):
    /// each kind arrives as the managed array of its element type, a view
    /// brings only its own elements, and a result or a zero-alloc argument
    /// carries them as well as a call argument does.
    /// </summary>
    public class TypedArrayBridgeTests {
        QuickJSContext _ctx;

        [SetUp]
        public void SetUp() {
            TypedArrayFixture.Last = null;
            _ctx = new QuickJSContext();
            _ctx.Eval("globalThis.F = CS.OneJS.Tests.TypedArrayFixture");
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            TypedArrayFixture.Last = null;
            QuickJSNative.ClearAllHandles();
        }

        T Sent<T>(string js) {
            _ctx.Eval(js);
            Assert.IsInstanceOf<T>(TypedArrayFixture.Last, $"after: {js}");
            return (T)TypedArrayFixture.Last;
        }

        [Test]
        public void Float32Array_ArrivesAsFloatArray_BitForBit() {
            var got = Sent<float[]>("F.TakeFloats(new Float32Array([1.5, -0.25, 3.4e38, 1e-7, -0]))");
            CollectionAssert.AreEqual(new[] { 1.5f, -0.25f, 3.4e38f, 1e-7f, -0f }, got);
            Assert.IsTrue(float.IsNegative(got[4]), "-0 keeps its sign: the bytes cross, not a number's text");
        }

        [Test]
        public void EveryKind_ArrivesAsItsElementType() {
            CollectionAssert.AreEqual(new[] { 0.1, -2.5 }, Sent<double[]>("F.TakeDoubles(new Float64Array([0.1, -2.5]))"));
            CollectionAssert.AreEqual(new[] { int.MinValue, 7 }, Sent<int[]>("F.TakeInts(new Int32Array([-2147483648, 7]))"));
            CollectionAssert.AreEqual(new[] { uint.MaxValue, 1u }, Sent<uint[]>("F.TakeUInts(new Uint32Array([4294967295, 1]))"));
            CollectionAssert.AreEqual(new short[] { -32768, 9 }, Sent<short[]>("F.TakeShorts(new Int16Array([-32768, 9]))"));
            CollectionAssert.AreEqual(new ushort[] { 65535, 2 }, Sent<ushort[]>("F.TakeUShorts(new Uint16Array([65535, 2]))"));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 3 }, Sent<byte[]>("F.TakeBytes(new Uint8Array([255, 0, 3]))"));
            CollectionAssert.AreEqual(new byte[] { 255, 0 }, Sent<byte[]>("F.TakeBytes(new Uint8ClampedArray([300, -4]))"));
            CollectionAssert.AreEqual(new sbyte[] { -128, 127 }, Sent<sbyte[]>("F.TakeSBytes(new Int8Array([-128, 127]))"));
            CollectionAssert.AreEqual(new[] { long.MinValue, 5L }, Sent<long[]>("F.TakeLongs(new BigInt64Array([-9223372036854775808n, 5n]))"));
            CollectionAssert.AreEqual(new[] { ulong.MaxValue }, Sent<ulong[]>("F.TakeULongs(new BigUint64Array([18446744073709551615n]))"));
        }

        [Test]
        public void View_BringsOnlyItsOwnElements() {
            var got = Sent<float[]>(@"
                const all = new Float32Array([0, 1, 2, 3, 4, 5, 6, 7])
                F.TakeFloats(all.subarray(3, 6))
            ");
            CollectionAssert.AreEqual(new[] { 3f, 4f, 5f }, got);
        }

        [Test]
        public void Empty_ArrivesAsEmptyArray() {
            Assert.AreEqual(0, Sent<float[]>("F.TakeFloats(new Float32Array(0))").Length);
        }

        [Test]
        public void Detached_ArrivesEmpty_AsJavaScriptReadsIt() {
            var got = Sent<float[]>(@"
                const b = new ArrayBuffer(16)
                const a = new Float32Array(b)
                b.transfer()
                F.TakeFloats(a)
            ");
            Assert.AreEqual(0, got.Length);
        }

        [Test]
        public void ObjectParameter_ReceivesTheTypedArray() {
            var got = Sent<float[]>("F.TakeObject(new Float32Array([4, 5]))");
            CollectionAssert.AreEqual(new[] { 4f, 5f }, got);
        }

        [Test]
        public void PlainArray_StillCrosses() {
            // A plain array keeps the JSON path and its element inference: any
            // fraction makes it float[] (all integers would make it int[]).
            CollectionAssert.AreEqual(new[] { 1.5f, 2f, 3f }, Sent<float[]>("F.TakeFloats([1.5, 2, 3])"));
        }

        [Test]
        public void DataView_IsNotATypedArray() {
            _ctx.Eval("F.TakeObject(new DataView(new ArrayBuffer(8)))");
            Assert.IsNotInstanceOf<Array>(TypedArrayFixture.Last);
        }

        [Test]
        public void CallbackResult_CarriesTheBytes() {
            var handle = int.Parse(_ctx.Eval("__registerCallback(function() { return new Int32Array([10, -20, 30]).subarray(1); })"));
            var result = _ctx.InvokeCallback(handle);
            CollectionAssert.AreEqual(new[] { -20, 30 }, (int[])result);
        }

        [Test]
        public void ZeroAllocArgument_CarriesTheBytes() {
            float[] got = null;
            int id = QuickJSNative.Bind<float[]>(values => got = values);
            _ctx.Eval($"__zaInvoke1({id}, new Float32Array([7, 8, 9]).subarray(1))");
            CollectionAssert.AreEqual(new[] { 8f, 9f }, got);
        }

        [Test]
        public void ManyCalls_Agree() {
            // A thousand crossings of a 1600-float buffer (PlaceMany at 200
            // actors): every one arrives whole, so nothing is read from a
            // buffer freed before C# copied it.
            var result = _ctx.Eval(@"
                const buf = new Float32Array(1600)
                let ok = 0
                for (let i = 0; i < 1000; i++) {
                    buf[i % 1600] = i
                    if (F.Count(buf) === 1600) ok++
                }
                ok
            ");
            Assert.AreEqual("1000", result);
        }

        [Test]
        public void Float32Array_PacksIntoVector3Parameter() {
            var got = Sent<Vector3[]>("F.TakeVectors(new Float32Array([1, 2, 3, -4, 5.5, 6]))");
            CollectionAssert.AreEqual(new[] { new Vector3(1, 2, 3), new Vector3(-4, 5.5f, 6) }, got);
        }

        [Test]
        public void MeshFromTypedArrays_TakesTheVerticesAndTriangles() {
            // A mesh redrawn every frame, its corners and triangles given as
            // typed arrays instead of an array of [x, y, z] tuples.
            var mesh = Sent<Mesh>(@"
                const m = new CS.UnityEngine.Mesh()
                m.vertices = new Float32Array([0, 0, 0, 1, 0, 0, 0, 0, 1])
                m.triangles = new Int32Array([0, 2, 1])
                F.TakeObject(m)
            ");
            try {
                CollectionAssert.AreEqual(new[] { Vector3.zero, Vector3.right, Vector3.forward }, mesh.vertices);
                CollectionAssert.AreEqual(new[] { 0, 2, 1 }, mesh.triangles);
            } finally {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FloatsThatAreNotWholeVectors_AreRefused() {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("4 floats do not make whole Vector3s"));
            _ctx.Eval("F.TakeVectors(new Float32Array(4))");
            Assert.IsNull(TypedArrayFixture.Last);
        }

        // Malformed values: what the bridge refuses rather than read past a buffer

        static QuickJSNative.InteropValue Malformed(int kind, int byteLength, IntPtr data) => new QuickJSNative.InteropValue {
            type = QuickJSNative.InteropType.TypedArray,
            taKind = (QuickJSNative.TypedArrayKind)kind,
            taByteLength = byteLength,
            taData = data,
        };

        [Test]
        public unsafe void Malformed_IsRefusedWithAnError() {
            var bytes = new byte[16];
            fixed (byte* p = bytes) {
                var data = (IntPtr)p;
                var cases = new[] {
                    Malformed(42, 4, data),   // no such kind
                    Malformed(10, 6, data),   // 6 bytes is not a whole number of floats
                    Malformed(10, -4, data),  // negative length
                    Malformed(10, 8, IntPtr.Zero), // bytes promised, none given
                };
                foreach (var v in cases) {
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Malformed typed array"));
                    Assert.IsNull(QuickJSNative.InteropValueToObject(v), $"kind {(int)v.taKind}, {v.taByteLength} bytes");
                }
            }
        }

        [Test]
        public unsafe void WellFormed_IsRead() {
            // The control for Malformed_IsRefusedWithAnError: the same reader
            // given a value it should accept reads it.
            var floats = new[] { 1f, 2f };
            fixed (float* p = floats) {
                var got = QuickJSNative.InteropValueToObject(Malformed(10, 8, (IntPtr)p));
                CollectionAssert.AreEqual(floats, (float[])got);
            }
        }
    }
}
