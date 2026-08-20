using System;
using System.Runtime.InteropServices;

namespace OneJS {
    /// <summary>
    /// Core native interop layer for QuickJS integration.
    /// This partial class contains only DllImports, enums, structs, and string conversion helpers.
    /// See other partial files for specific functionality:
    /// - QuickJSNative.Handles.cs: Handle table management
    /// - QuickJSNative.Reflection.cs: Type resolution and member caching
    /// - QuickJSNative.Structs.cs: Unity struct serialization/deserialization
    /// - QuickJSNative.Dispatch.cs: JS->C# dispatch and value conversion
    /// - QuickJSNative.FastPath.cs: Zero-allocation fast path for hot methods
    ///
    /// Platform behavior:
    /// - Editor/Standalone: Uses native QuickJS library (quickjs_unity.dylib/.dll/.so)
    /// - iOS: Uses statically linked QuickJS (__Internal)
    /// - WebGL: Uses browser's JS engine via OneJSWebGL.jslib (__Internal)
    ///   In WebGL builds, JavaScript runs directly in the browser with JIT optimization
    ///   rather than in QuickJS compiled to WASM.
    /// </summary>
    public static partial class QuickJSNative {
        // MARK: Native Library Name
        // Editor always uses the native library (even when build target is iOS/WebGL)
        // WebGL builds use __Internal which routes to OneJSWebGL.jslib
        // iOS builds use __Internal for static linking
        // All other platforms use the native QuickJS library
        const string LibName =
#if UNITY_EDITOR
            "quickjs_unity";
#elif UNITY_WEBGL
            "__Internal";
#elif UNITY_IOS
            "__Internal";
#else
            "quickjs_unity";
#endif

        // MARK: Delegate Types
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CsLogCallback(IntPtr msg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void CsInvokeCallback(
            IntPtr ctx,
            InteropInvokeRequest* req,
            InteropInvokeResult* res
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CsReleaseHandleCallback(int handle);

        /// <summary>
        /// Frees a buffer the C# side allocated (StringToCoTaskMemUTF8) once the
        /// native side has consumed it. Each side frees foreign memory through the
        /// owner's allocator: on Windows the CRT heap and the CoTaskMem heap are
        /// distinct, so native free() on a C# buffer (or FreeCoTaskMem on a native
        /// malloc) is undefined behavior. The reverse direction is qjs_free.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CsFreeCallback(IntPtr ptr);

        /// <summary>
        /// Zero-allocation dispatch callback.
        /// Called from fixed-arity __zaInvoke functions with stack-allocated args.
        /// </summary>
        /// <param name="bindingId">Pre-registered binding ID</param>
        /// <param name="args">Pointer to stack-allocated InteropValue array</param>
        /// <param name="argCount">Number of arguments (0-8)</param>
        /// <param name="outResult">Output result (caller-allocated)</param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void CsZeroAllocCallback(
            int bindingId,
            InteropValue* args,
            int argCount,
            InteropValue* outResult
        );

        // MARK: DllImports
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern void qjs_set_cs_log_callback(CsLogCallback cb);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr qjs_create();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void qjs_destroy(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int qjs_eval(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string code,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            int evalFlags,
            IntPtr outBuf,
            int outBufSize
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void qjs_run_gc(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int qjs_execute_pending_jobs(IntPtr ctx);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern void qjs_set_cs_invoke_callback(CsInvokeCallback cb);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int qjs_invoke_callback(
            IntPtr ctx,
            int callbackHandle,
            InteropValue* args,
            int argCount,
            InteropValue* outResult
        );

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern void qjs_set_cs_release_handle_callback(CsReleaseHandleCallback cb);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void qjs_set_cs_zeroalloc_callback(CsZeroAllocCallback cb);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern void qjs_set_cs_free_callback(CsFreeCallback cb);

        // Frees native-allocated buffers returned in InteropValue (str/typeHint
        // from qjs_invoke_callback results) through the native allocator.
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void qjs_free(IntPtr ptr);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int qjs_abi_version();

        /// <summary>
        /// The native ABI version this C# runtime was built against. Checked once
        /// at context creation; a mismatch (e.g. the editor still has an older
        /// dylib loaded after a package update) fails loudly instead of
        /// misbehaving at the first interop call.
        /// </summary>
        internal const int ExpectedAbiVersion = 2;

        // MARK: Error Codes (mirror QjsError in quickjs_unity.c)
        internal const int ErrOk = 0;
        internal const int ErrInvalidCtx = -1;
        internal const int ErrInvalidHandle = -2;
        internal const int ErrNotFunction = -3;
        internal const int ErrOutOfMemory = -4;
        internal const int ErrException = -5;
        internal const int ErrStaleHandle = -6;

        /// <summary>
        /// Frees the native-allocated buffers of a callback result (string/JSON
        /// payload and typeHint) through the native allocator. Safe to call for
        /// any result value; clears the pointers so a double free is impossible.
        /// </summary>
        internal static void FreeNativeResultBuffers(ref InteropValue result) {
            if ((result.type == InteropType.String || result.type == InteropType.JsonObject) &&
                result.str != IntPtr.Zero) {
                qjs_free(result.str);
                result.str = IntPtr.Zero;
            }
            if (result.typeHint != IntPtr.Zero) {
                qjs_free(result.typeHint);
                result.typeHint = IntPtr.Zero;
            }
        }

        internal static string DescribeError(int code) {
            switch (code) {
                case ErrInvalidCtx: return "invalid or destroyed JS context";
                case ErrInvalidHandle: return "invalid callback handle";
                case ErrNotFunction: return "callback handle does not refer to a function";
                case ErrOutOfMemory: return "native out of memory";
                case ErrException: return "the JS callback threw an exception (see log)";
                case ErrStaleHandle: return "stale callback handle from a previous JS context (re-register after reload)";
                default: return $"native error {code}";
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Fast event dispatch for WebGL: avoids eval overhead. Returns the suppression-flags
        // bitmask from __dispatchEvent (bit0=propagationStopped, bit1=defaultPrevented), or 0.
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int qjs_dispatch_event(
            int elementHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string eventType,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string eventDataJson
        );
#endif

        // MARK: Interop Enums
        public enum InteropType : int {
            Null = 0,
            Bool = 1,
            Int32 = 2,
            Double = 3,
            String = 4,
            ObjectHandle = 5,
            Int64 = 6,
            Float32 = 7,
            Array = 8,
            JsonObject = 9,
            Vector3 = 10,  // Binary packed x,y,z floats - zero alloc!
            Vector4 = 11,  // Binary packed x,y,z,w floats (Quaternion, Color) - zero alloc!
            TaskHandle = 12, // Pending C# Task - JS receives Promise
            MethodRef = 13  // Signals "this is a method, not a property" - JS creates function wrapper
        }

        public enum InteropInvokeCallKind : int {
            Ctor = 0,
            Method = 1,
            GetProp = 2,
            SetProp = 3,
            GetField = 4,
            SetField = 5,
            TypeExists = 6,
            IsEnumType = 7,
            MakeGenericType = 8,
            RegisterExtensionType = 9,
            TryGetProp = 10
        }

        // MARK: Interop Structs
        // Layout must match C struct exactly:
        // - type (4) + pad (4) + union (16) + typeHint (8) = 32 bytes
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        public struct InteropValue {
            [FieldOffset(0)]
            public InteropType type;

            [FieldOffset(4)]
            public int pad;

            // Union members: all start at offset 8
            [FieldOffset(8)]
            public int i32;

            [FieldOffset(8)]
            public int b;

            [FieldOffset(8)]
            public int handle;

            [FieldOffset(8)]
            public long i64;

            [FieldOffset(8)]
            public float f32;

            [FieldOffset(8)]
            public double f64;

            [FieldOffset(8)]
            public IntPtr str;

            // Vector components: for Vector3/Vector4/Color/Quaternion
            [FieldOffset(8)]
            public float vecX;

            [FieldOffset(12)]
            public float vecY;

            [FieldOffset(16)]
            public float vecZ;

            [FieldOffset(20)]
            public float vecW;

            // typeHint now at offset 24 (after 16-byte union)
            [FieldOffset(24)]
            public IntPtr typeHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InteropInvokeRequest {
            public IntPtr typeName;
            public IntPtr memberName;
            public InteropInvokeCallKind callKind;
            public int isStatic;
            public int targetHandle;
            public int argCount;
            public IntPtr args;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InteropInvokeResult {
            public InteropValue returnValue;
            public int errorCode;
            public IntPtr errorMsg;
        }

        // MARK: String Helpers
        internal static IntPtr StringToUtf8(string s) {
            if (s == null) return IntPtr.Zero;
            return Marshal.StringToCoTaskMemUTF8(s);
        }

        internal static string PtrToStringUtf8(IntPtr ptr) {
            if (ptr == IntPtr.Zero) return null;
            return Marshal.PtrToStringUTF8(ptr);
        }
    }
}