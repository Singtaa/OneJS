using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// `__wasm`, the native WebAssembly host QuickJS serves glue through. A real
    /// module library is generated C per platform; this one is built here from
    /// managed delegates against the same table (Auxiliary~/quickjs-unity/wasm/
    /// onejs_wasm.h), so the engine's half is tested on its own: argument and
    /// result conversion, imports into JavaScript, a JavaScript throw and a trap
    /// coming back out, memory read and written from both sides, and a memory
    /// that moves detaching the buffer JavaScript holds.
    /// </summary>
    [TestFixture]
    public class NativeWasmTests {
        const string Name = "onejs-test";
        const string Sha = "0000000000000000000000000000000000000000000000000000000000000001";
        const uint Abi = 1;
        const int Ok = 0, Trapped = 1, HostThrew = 2;
        const int PageSize = 65536;

        [StructLayout(LayoutKind.Sequential)]
        struct Import {
            public IntPtr Module, Name, Sig;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Export {
            public IntPtr Name, Sig, Thunk;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Module {
            public uint Abi;
            public IntPtr Name, Sha256;
            public uint ImportCount;
            public IntPtr Imports;
            public uint ExportCount;
            public IntPtr Exports;
            public IntPtr Instantiate, Free, Memory, Grow, Call;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr InstantiateFn(IntPtr host, IntPtr call, IntPtr error, UIntPtr errorSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void FreeFn(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr MemoryFn(IntPtr instance, IntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate ulong GrowFn(IntPtr instance, ulong pages);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int CallFn(IntPtr instance, IntPtr thunk, IntPtr args, IntPtr results, IntPtr error, UIntPtr errorSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void ThunkFn(IntPtr instance, IntPtr args, IntPtr results);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ImportCallFn(IntPtr host, uint index, IntPtr args, IntPtr results);

        class Trap : Exception {
            public Trap(string message) : base(message) { }
        }

        class HostThrewSignal : Exception { }

        // Everything native code points into, kept alive and pinned until TearDown.
        readonly List<object> _keep = new List<object>();
        readonly List<IntPtr> _allocated = new List<IntPtr>();
        readonly Dictionary<IntPtr, ThunkFn> _thunks = new Dictionary<IntPtr, ThunkFn>();
        IntPtr _table;
        QuickJSContext _ctx;

        byte[] _memory;
        GCHandle _memoryPin;
        IntPtr _host;
        ImportCallFn _importCall;
        int _instances, _freed;
        string _failStart;

        [SetUp]
        public void SetUp() {
            _instances = 0;
            _freed = 0;
            _failStart = null;
            SetMemory(new byte[PageSize]);
            _table = BuildModule();
            Assert.IsTrue(QuickJSNative.RegisterWasmModule(_table), "the engine refused the test module");
            _ctx = new QuickJSContext();
        }

        [TearDown]
        public void TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.UnregisterWasmModule(Name);
            foreach (var p in _allocated) Marshal.FreeHGlobal(p);
            _allocated.Clear();
            _thunks.Clear();
            _keep.Clear();
            if (_memoryPin.IsAllocated) _memoryPin.Free();
            _memory = null;
        }

        void SetMemory(byte[] bytes) {
            if (_memoryPin.IsAllocated) _memoryPin.Free();
            _memory = bytes;
            _memoryPin = GCHandle.Alloc(_memory, GCHandleType.Pinned);
        }

        IntPtr Str(string s) {
            var p = Marshal.StringToHGlobalAnsi(s);
            _allocated.Add(p);
            return p;
        }

        IntPtr Fn<T>(T fn) where T : Delegate {
            _keep.Add(fn);
            return Marshal.GetFunctionPointerForDelegate(fn);
        }

        IntPtr Array<T>(T[] items) where T : struct {
            int size = Marshal.SizeOf<T>();
            var p = Marshal.AllocHGlobal(Math.Max(1, size * items.Length));
            _allocated.Add(p);
            for (int i = 0; i < items.Length; i++) Marshal.StructureToPtr(items[i], p + i * size, false);
            return p;
        }

        static ulong Arg(IntPtr args, int i) => (ulong)Marshal.ReadInt64(args, i * 8);
        static void Result(IntPtr results, ulong value) => Marshal.WriteInt64(results, (long)value);

        IntPtr BuildModule() {
            var exports = new List<Export>();
            void Add(string name, string sig, ThunkFn thunk) {
                var p = Fn(thunk);
                _thunks[p] = thunk;
                exports.Add(new Export { Name = Str(name), Sig = Str(sig), Thunk = p });
            }

            Add("add", "ii>i", (inst, a, r) => Result(r, (uint)((int)Arg(a, 0) + (int)Arg(a, 1))));
            Add("half", "d>d", (inst, a, r) => Result(r, (ulong)BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble((long)Arg(a, 0)) / 2)));
            Add("poke", "ii>", (inst, a, r) => _memory[(uint)Arg(a, 0)] = (byte)Arg(a, 1));
            Add("peek", "i>i", (inst, a, r) => Result(r, _memory[(uint)Arg(a, 0)]));
            Add("move", ">", (inst, a, r) => {
                var bigger = new byte[_memory.Length + PageSize];
                Buffer.BlockCopy(_memory, 0, bigger, 0, _memory.Length);
                SetMemory(bigger);
            });
            Add("twice", "i>i", (inst, a, r) => {
                var args = Marshal.AllocHGlobal(8);
                var results = Marshal.AllocHGlobal(8);
                try {
                    Marshal.WriteInt64(args, (long)Arg(a, 0));
                    if (_importCall(_host, 0, args, results) != 0) throw new HostThrewSignal();
                    Result(r, (ulong)Marshal.ReadInt64(results) * 2);
                } finally {
                    Marshal.FreeHGlobal(args);
                    Marshal.FreeHGlobal(results);
                }
            });
            Add("boom", ">", (inst, a, r) => throw new Trap("unreachable executed"));

            var imports = new[] { new Import { Module = Str("env"), Name = Str("double"), Sig = Str("i>i") } };

            var module = new Module {
                Abi = Abi,
                Name = Str(Name),
                Sha256 = Str(Sha),
                ImportCount = (uint)imports.Length,
                Imports = Array(imports),
                ExportCount = (uint)exports.Count,
                Exports = Array(exports.ToArray()),
                Instantiate = Fn<InstantiateFn>((host, call, error, size) => {
                    if (_failStart != null) {
                        WriteError(error, size, _failStart);
                        return IntPtr.Zero;
                    }
                    _host = host;
                    _importCall = Marshal.GetDelegateForFunctionPointer<ImportCallFn>(call);
                    _instances++;
                    return new IntPtr(_instances);
                }),
                Free = Fn<FreeFn>(instance => _freed++),
                Memory = Fn<MemoryFn>((instance, size) => {
                    Marshal.WriteInt64(size, _memory.Length);
                    return _memoryPin.AddrOfPinnedObject();
                }),
                Grow = Fn<GrowFn>((instance, pages) => {
                    ulong old = (ulong)(_memory.Length / PageSize);
                    if (pages > 4) return ulong.MaxValue;
                    var bigger = new byte[_memory.Length + (int)pages * PageSize];
                    Buffer.BlockCopy(_memory, 0, bigger, 0, _memory.Length);
                    SetMemory(bigger);
                    return old;
                }),
                Call = Fn<CallFn>((instance, thunk, args, results, error, size) => {
                    try {
                        _thunks[thunk](instance, args, results);
                        return Ok;
                    } catch (Trap e) {
                        WriteError(error, size, e.Message);
                        return Trapped;
                    } catch (HostThrewSignal) {
                        return HostThrew;
                    }
                }),
            };
            return Array(new[] { module });
        }

        static void WriteError(IntPtr error, UIntPtr size, string message) {
            var bytes = Encoding.ASCII.GetBytes(message);
            int n = Math.Min(bytes.Length, (int)size.ToUInt32() - 1);
            Marshal.Copy(bytes, 0, error, n);
            Marshal.WriteByte(error, n, 0);
        }

        string Eval(string code) => _ctx.Eval(code);

        const string Imports = "({ env: { double: (x) => x * 2 } })";

        string Instantiate(string then) =>
            Eval($"(() => {{ const e = __wasm.instantiate(\"{Name}\", \"{Sha}\", {Imports}); {then} }})()");

        [Test]
        public void HasAnswersByNameAndHash() {
            Assert.AreEqual("true", Eval($"String(__wasm.has(\"{Name}\", \"{Sha}\"))"));
            Assert.AreEqual("false", Eval($"String(__wasm.has(\"{Name}\", \"{Sha.Replace('1', '2')}\"))"));
            Assert.AreEqual("false", Eval($"String(__wasm.has(\"no-such-module\", \"{Sha}\"))"));
        }

        [Test]
        public void ExportsConvertArgumentsAndResultsAsWebAssemblyDoes() {
            Assert.AreEqual("5", Instantiate("return e.add(2, 3)"));
            // An i32 comes back signed, and an argument wraps to 32 bits on the way in.
            Assert.AreEqual("-1", Instantiate("return e.add(2147483647, -2147483648)"));
            Assert.AreEqual("-2147483648", Instantiate("return e.add(4294967295, -2147483647)"));
            Assert.AreEqual("1.25", Instantiate("return e.half(2.5)"));
        }

        [Test]
        public void MemoryIsSharedBothWays() {
            Assert.AreEqual("65536 7 9", Instantiate(
                "const m = new Uint8Array(e.memory.buffer); e.poke(10, 7); m[11] = 9; " +
                "return `${e.memory.buffer.byteLength} ${m[10]} ${e.peek(11)}`"));
            Assert.AreEqual("true", Instantiate("return String(e.memory.buffer === e.memory.buffer)"));
        }

        [Test]
        public void MemoryThatMovesDetachesTheOldBuffer() {
            Assert.AreEqual("0 131072 42", Instantiate(
                "const before = e.memory.buffer; new Uint8Array(before)[5] = 42; e.move(); " +
                "return `${before.byteLength} ${e.memory.buffer.byteLength} ${new Uint8Array(e.memory.buffer)[5]}`"));
            Assert.AreEqual("2 0 196608", Instantiate(
                "const before = e.memory.buffer; const old = e.memory.grow(1); " +
                "return `${old} ${before.byteLength} ${e.memory.buffer.byteLength}`"));
            StringAssert.Contains("could not grow", Instantiate("try { e.memory.grow(5); return 'grew' } catch (x) { return String(x) }"));
        }

        [Test]
        public void ImportsCallIntoJavaScript() {
            Assert.AreEqual("28", Instantiate("return e.twice(7)"));
        }

        [Test]
        public void AJavaScriptThrowInAnImportReachesTheCaller() {
            Assert.AreEqual("Error: from the import", Eval(
                $"(() => {{ const e = __wasm.instantiate(\"{Name}\", \"{Sha}\", {{ env: {{ double() {{ throw new Error(\"from the import\") }} }} }}); " +
                "try { e.twice(1); return 'no throw' } catch (x) { return String(x) } })()"));
        }

        [Test]
        public void ATrapThrowsARuntimeError() {
            var text = Instantiate("try { e.boom(); return 'no trap' } catch (x) { return String(x) }");
            StringAssert.Contains("RuntimeError: unreachable executed", text);
            StringAssert.Contains($"{Name}.boom", text);
            Assert.AreEqual("5", Instantiate("try { e.boom() } catch (x) {} return e.add(2, 3)"), "the instance works after a trap");
        }

        [Test]
        public void AMismatchedHashIsRefusedByName() {
            var text = Eval($"(() => {{ try {{ __wasm.instantiate(\"{Name}\", \"abc\", {Imports}); return 'made' }} catch (x) {{ return String(x) }} }})()");
            StringAssert.Contains("TypeError", text);
            StringAssert.Contains(Sha, text);
            StringAssert.Contains("abc", text);
            StringAssert.Contains("no native nope module", Eval(
                $"(() => {{ try {{ __wasm.instantiate(\"nope\", \"{Sha}\", {Imports}); return 'made' }} catch (x) {{ return String(x) }} }})()"));
        }

        [Test]
        public void AMissingImportIsALinkError() {
            StringAssert.Contains("LinkError: import env.double is not a function", Eval(
                $"(() => {{ try {{ __wasm.instantiate(\"{Name}\", \"{Sha}\", {{ env: {{}} }}); return 'made' }} catch (x) {{ return String(x) }} }})()"));
            Assert.AreEqual(0, _instances, "nothing is started before every import resolves");
        }

        [Test]
        public void AFailedStartThrowsWithTheModulesWords() {
            _failStart = "out of memory";
            StringAssert.Contains("RuntimeError: out of memory (starting onejs-test)", Eval(
                $"(() => {{ try {{ __wasm.instantiate(\"{Name}\", \"{Sha}\", {Imports}); return 'made' }} catch (x) {{ return String(x) }} }})()"));
        }

        [Test]
        public void AnInstanceIsFreedWhenCollected() {
            Eval($"globalThis.held = __wasm.instantiate(\"{Name}\", \"{Sha}\", {Imports}); globalThis.held.add(1, 1)");
            Assert.AreEqual(0, _freed);
            Eval("globalThis.held = null");
            _ctx.RunGC();
            Assert.AreEqual(1, _freed);
        }

        [Test]
        public void TheEngineRefusesATableItCannotRead() {
            Assert.IsFalse(QuickJSNative.RegisterWasmModule(IntPtr.Zero));
            var other = Array(new[] { new Module { Abi = Abi + 1, Name = Str("other-abi"), Sha256 = Str(Sha) } });
            Assert.IsFalse(QuickJSNative.RegisterWasmModule(other));
            Assert.IsTrue(QuickJSNative.RegisterWasmModule(_table), "registering the same module again is harmless");
        }
    }
}
