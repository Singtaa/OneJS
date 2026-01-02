using System;
using System.Runtime.InteropServices;

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
    // WebGL uses __Internal which routes to OneJSWebGL.jslib
    // iOS uses __Internal for static linking
    // All other platforms use the native QuickJS library
    const string LibName =
#if UNITY_WEBGL && !UNITY_EDITOR
        "__Internal";
#elif UNITY_IOS
        "__Internal";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        "quickjs_unity";
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

#if UNITY_WEBGL && !UNITY_EDITOR
    // Fast event dispatch for WebGL - avoids eval overhead
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void qjs_dispatch_event(
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
        TaskHandle = 12 // Pending C# Task - JS receives Promise
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
        MakeGenericType = 8
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

        // Union members - all start at offset 8
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

        // Vector components - for Vector3/Vector4/Color/Quaternion
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