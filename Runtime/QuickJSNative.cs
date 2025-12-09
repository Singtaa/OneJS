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
/// </summary>
public static partial class QuickJSNative {
    // MARK: Native Library Name
    const string LibName =
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        "quickjs_unity";
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
        Array = 8
    }

    public enum InteropInvokeCallKind : int {
        Ctor = 0,
        Method = 1,
        GetProp = 2,
        SetProp = 3,
        GetField = 4,
        SetField = 5
    }

    // MARK: Interop Structs
    [StructLayout(LayoutKind.Explicit)]
    public struct InteropValue {
        // type + padding (matches C: int32 type; int32 _pad;)
        [FieldOffset(0)]
        public InteropType type;

        [FieldOffset(4)]
        public int pad; // not used directly, just keeps alignment

        // Union starts at offset 8
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
        public IntPtr str; // UTF-8 char* from native

        // typeHint is at offset 16 (after the 8-byte union)
        [FieldOffset(16)]
        public IntPtr typeHint; // for OBJECT_HANDLE, nullable type name
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InteropInvokeRequest {
        public IntPtr typeName; // const char* utf8
        public IntPtr memberName; // const char* utf8
        public InteropInvokeCallKind callKind;
        public int isStatic;
        public int targetHandle;
        public int argCount;
        public IntPtr args; // InteropValue* (we'll cast this in unsafe code)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InteropInvokeResult {
        public InteropValue returnValue;
        public int errorCode;
        public IntPtr errorMsg; // const char* utf8 (optional)
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