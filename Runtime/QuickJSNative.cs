using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

public static class QuickJSNative {
    const string LibName =
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        "quickjs_unity";
#elif UNITY_IOS
        "__Internal";
#else
        "quickjs_unity";
#endif

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CsLogCallback(IntPtr msg);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern void qjs_set_cs_log_callback(CsLogCallback cb);

    // MARK: Imports
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr qjs_create();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern void qjs_destroy(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern int qjs_eval(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string code,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        int evalFlags,
        IntPtr outBuf,
        int outBufSize
    );

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern void qjs_run_gc(IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void CsInvokeCallback(
        InteropInvokeRequest* req,
        InteropInvokeResult* res
    );

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern void qjs_set_cs_invoke_callback(CsInvokeCallback cb);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe int qjs_invoke_callback(
        IntPtr ctx,
        int callbackHandle,
        InteropValue* args,
        int argCount,
        InteropValue* outResult
    );

    // Callback for releasing object handles from JS
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CsReleaseHandleCallback(int handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    static extern void qjs_set_cs_release_handle_callback(CsReleaseHandleCallback cb);

    // MARK: Interop

    public enum InteropType : int {
        Null = 0,
        Bool = 1,
        Int32 = 2,
        Double = 3,
        String = 4,
        ObjectHandle = 5,
        Int64 = 6,
        Float32 = 7
    }

    public enum InteropInvokeCallKind : int {
        Ctor = 0,
        Method = 1,
        GetProp = 2,
        SetProp = 3,
        GetField = 4,
        SetField = 5
    }

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

    // MARK: Handles
    static int _nextHandle = 1;
    static readonly Dictionary<int, object> _handleTable = new Dictionary<int, object>();
    static readonly Dictionary<object, int> _reverseHandleTable = new Dictionary<object, int>();
    static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
    static readonly object _handleLock = new object();

    static int RegisterObject(object obj) {
        if (obj == null) return 0;
        
        lock (_handleLock) {
            // Check if object already has a handle (avoid duplicates)
            if (_reverseHandleTable.TryGetValue(obj, out int existingHandle)) {
                return existingHandle;
            }
            
            int handle = _nextHandle++;
            _handleTable[handle] = obj;
            _reverseHandleTable[obj] = handle;
            return handle;
        }
    }

    static bool UnregisterObject(int handle) {
        if (handle == 0) return false;
        
        lock (_handleLock) {
            if (_handleTable.TryGetValue(handle, out var obj)) {
                _handleTable.Remove(handle);
                _reverseHandleTable.Remove(obj);
                return true;
            }
            return false;
        }
    }

    static object GetObjectByHandle(int handle) {
        if (handle == 0) return null;
        lock (_handleLock) {
            return _handleTable.TryGetValue(handle, out var obj) ? obj : null;
        }
    }
    
    /// <summary>
    /// Returns the number of currently registered object handles.
    /// Useful for debugging memory leaks.
    /// </summary>
    public static int GetHandleCount() {
        lock (_handleLock) {
            return _handleTable.Count;
        }
    }
    
    /// <summary>
    /// Clears all registered object handles.
    /// Call this when disposing all contexts to prevent memory leaks.
    /// </summary>
    public static void ClearAllHandles() {
        lock (_handleLock) {
            _handleTable.Clear();
            _reverseHandleTable.Clear();
            _nextHandle = 1;
        }
    }

    static Type ResolveType(string fullName) {
        if (string.IsNullOrEmpty(fullName)) return null;

        if (_typeCache.TryGetValue(fullName, out var cached)) {
            return cached;
        }

        Type type = Type.GetType(fullName);
        if (type == null) {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) {
                var asm = assemblies[i];
                try {
                    var t = asm.GetType(fullName);
                    if (t != null) {
                        type = t;
                        break;
                    }
                } catch {
                }
            }
        }

        if (type != null) {
            _typeCache[fullName] = type;
        }

        return type;
    }

    static MethodInfo FindMethod(Type type, string name, BindingFlags flags, object[] args) {
        while (type != null) {
            var methods = type.GetMethods(flags | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++) {
                var m = methods[i];
                if (m.Name != name) continue;

                var parameters = m.GetParameters();
                if (parameters.Length != args.Length) continue;

                bool match = true;
                for (int j = 0; j < parameters.Length; j++) {
                    var pType = parameters[j].ParameterType;
                    var arg = args[j];

                    if (arg == null) {
                        if (pType.IsValueType && Nullable.GetUnderlyingType(pType) == null) {
                            match = false;
                            break;
                        }
                    } else {
                        var aType = arg.GetType();
                        if (!pType.IsAssignableFrom(aType)) {
                            if (!(pType.IsPrimitive && aType.IsPrimitive)) {
                                match = false;
                                break;
                            }
                        }
                    }
                }

                if (match) return m;
            }

            type = type.BaseType;
        }

        return null;
    }

    static PropertyInfo FindProperty(Type type, string name, BindingFlags flags) {
        while (type != null) {
            var p = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (p != null) return p;
            type = type.BaseType;
        }
        return null;
    }

    static FieldInfo FindField(Type type, string name, BindingFlags flags) {
        while (type != null) {
            var f = type.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (f != null) return f;
            type = type.BaseType;
        }
        return null;
    }

    // MARK: Return
    static unsafe void SetReturnValue(InteropInvokeResult* resPtr, object value) {
        resPtr->returnValue = default;

        if (value == null) {
            resPtr->returnValue.type = InteropType.Null;
            return;
        }

        var t = value.GetType();
        switch (Type.GetTypeCode(t)) {
            case TypeCode.Boolean:
                resPtr->returnValue.type = InteropType.Bool;
                resPtr->returnValue.b = (bool)value ? 1 : 0;
                return;

            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
                resPtr->returnValue.type = InteropType.Int32;
                resPtr->returnValue.i32 = Convert.ToInt32(value);
                return;

            case TypeCode.UInt32:
            case TypeCode.Int64:
                resPtr->returnValue.type = InteropType.Int64;
                resPtr->returnValue.i64 = Convert.ToInt64(value);
                return;

            case TypeCode.UInt64:
                // UInt64 may overflow Int64, treat as double for safety
                resPtr->returnValue.type = InteropType.Double;
                resPtr->returnValue.f64 = Convert.ToDouble(value);
                return;

            case TypeCode.Single:
                resPtr->returnValue.type = InteropType.Float32;
                resPtr->returnValue.f32 = (float)value;
                return;

            case TypeCode.Double:
                resPtr->returnValue.type = InteropType.Double;
                resPtr->returnValue.f64 = (double)value;
                return;

            case TypeCode.String:
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8(value.ToString());
                return;
        }

        // Fallback: treat as object handle
        int handle = RegisterObject(value);
        resPtr->returnValue.type = InteropType.ObjectHandle;
        resPtr->returnValue.handle = handle;
    }

    static IntPtr StringToUtf8(string s) {
        if (s == null) return IntPtr.Zero;
        return Marshal.StringToCoTaskMemUTF8(s);
    }

    // MARK: Log / Dispatch
    static readonly CsLogCallback _logCallback = HandleLogFromJs;
    static readonly unsafe CsInvokeCallback _invokeCallback = DispatchFromJs;
    static readonly CsReleaseHandleCallback _releaseHandleCallback = HandleReleaseFromJs;

    static QuickJSNative() {
        // Register native -> C# callbacks once per process
        qjs_set_cs_log_callback(_logCallback);
        qjs_set_cs_invoke_callback(_invokeCallback);
        qjs_set_cs_release_handle_callback(_releaseHandleCallback);
    }

    static void HandleReleaseFromJs(int handle) {
        if (handle == 0) return;
        bool released = UnregisterObject(handle);
        #if UNITY_EDITOR
        if (released) {
            // Debug.Log($"[QuickJS] Released handle {handle}");
        }
        #endif
    }

    static void HandleLogFromJs(IntPtr msgPtr) {
        if (msgPtr == IntPtr.Zero) return;

        string msg = Marshal.PtrToStringUTF8(msgPtr);
        if (msg == null) return;

        Debug.Log("[QuickJS] " + msg);
    }

    // MARK: Invoke
    static unsafe void DispatchFromJs(InteropInvokeRequest* reqPtr, InteropInvokeResult* resPtr) {
        resPtr->errorCode = 0;
        resPtr->errorMsg = IntPtr.Zero;
        resPtr->returnValue = default;
        resPtr->returnValue.type = InteropType.Null;

        try {
            string typeName = PtrToStringUtf8(reqPtr->typeName);
            string memberName = PtrToStringUtf8(reqPtr->memberName);

            int argCount = reqPtr->argCount;
            object[] args = argCount > 0 ? new object[argCount] : Array.Empty<object>();

            if (argCount > 0 && reqPtr->args != IntPtr.Zero) {
                var nativeArgs = (InteropValue*)reqPtr->args;
                for (int i = 0; i < argCount; i++) {
                    args[i] = InteropValueToObject(nativeArgs[i]);
                }
            }

            // Keep the explicit Debug.Log shortcut
            if (typeName == "UnityEngine.Debug" &&
                memberName == "Log" &&
                reqPtr->callKind == InteropInvokeCallKind.Method) {
                if (args.Length > 0) {
                    Debug.Log(args[0]?.ToString());
                } else {
                    Debug.Log("(null)");
                }

                resPtr->returnValue.type = InteropType.Null;
                return;
            }

            bool isStatic = reqPtr->isStatic != 0;
            object target = null;
            Type type = null;

            if (!isStatic && reqPtr->targetHandle != 0) {
                target = GetObjectByHandle(reqPtr->targetHandle);
                if (target != null) {
                    type = target.GetType();
                }
            }

            if (type == null) {
                type = ResolveType(typeName);
            }

            if (reqPtr->callKind == InteropInvokeCallKind.Ctor) {
                if (type == null) {
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Type not found for ctor: " + typeName);
                    return;
                }

                object instance = Activator.CreateInstance(type, args);
                SetReturnValue(resPtr, instance);
                return;
            }

            if (type == null) {
                resPtr->errorCode = 1;
                Debug.LogError("[QuickJS] Type not found: " + typeName);
                return;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
            flags |= isStatic ? BindingFlags.Static : BindingFlags.Instance;

            switch (reqPtr->callKind) {
                case InteropInvokeCallKind.Method: {
                    MethodInfo method = FindMethod(type, memberName, flags, args);
                    if (method == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Method not found: " + type.FullName + "." + memberName);
                        return;
                    }

                    object result = method.Invoke(isStatic ? null : target, args);
                    SetReturnValue(resPtr, result);
                    return;
                }

                case InteropInvokeCallKind.GetProp: {
                    PropertyInfo prop = FindProperty(type, memberName, flags);
                    if (prop == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Property not found: " + type.FullName + "." + memberName);
                        return;
                    }

                    object result = prop.GetValue(isStatic ? null : target);
                    SetReturnValue(resPtr, result);
                    return;
                }

                case InteropInvokeCallKind.SetProp: {
                    PropertyInfo prop = FindProperty(type, memberName, flags);
                    if (prop == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Property not found (set): " + type.FullName + "." +
                                       memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    var pType = prop.PropertyType;
                    if (value != null && !pType.IsAssignableFrom(value.GetType())) {
                        try {
                            value = Convert.ChangeType(value, pType);
                        } catch {
                        }
                    }

                    prop.SetValue(isStatic ? null : target, value);
                    resPtr->returnValue.type = InteropType.Null;
                    return;
                }

                case InteropInvokeCallKind.GetField: {
                    FieldInfo field = FindField(type, memberName, flags);
                    if (field == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Field not found: " + type.FullName + "." + memberName);
                        return;
                    }

                    object result = field.GetValue(isStatic ? null : target);
                    SetReturnValue(resPtr, result);
                    return;
                }

                case InteropInvokeCallKind.SetField: {
                    FieldInfo field = FindField(type, memberName, flags);
                    if (field == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Field not found (set): " + type.FullName + "." +
                                       memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    var fType = field.FieldType;
                    if (value != null && !fType.IsAssignableFrom(value.GetType())) {
                        try {
                            value = Convert.ChangeType(value, fType);
                        } catch {
                        }
                    }

                    field.SetValue(isStatic ? null : target, value);
                    resPtr->returnValue.type = InteropType.Null;
                    return;
                }

                default:
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Unsupported call kind: " + reqPtr->callKind + " for " +
                                   typeName + "." +
                                   memberName);
                    return;
            }
        } catch (Exception ex) {
            resPtr->errorCode = 1;
            Debug.LogError("[QuickJS Invoke Error] " + ex);
        }
    }

    static string PtrToStringUtf8(IntPtr ptr) {
        if (ptr == IntPtr.Zero) return null;
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
    }

    static object InteropValueToObject(InteropValue v) {
        switch (v.type) {
            case InteropType.Null:
                return null;
            case InteropType.Bool:
                return v.b != 0;
            case InteropType.Int32:
                return v.i32;
            case InteropType.Int64:
                return v.i64;
            case InteropType.Float32:
                return v.f32;
            case InteropType.Double:
                return v.f64;
            case InteropType.String:
                return PtrToStringUtf8(v.str);
            case InteropType.ObjectHandle:
                return GetObjectByHandle(v.handle);
            default:
                return null;
        }
    }

    // MARK: Context class
    public sealed class Context : IDisposable {
        const string DefaultBootstrapResourcePath = "OneJS/QuickJSBootstrap.js";

        IntPtr _ptr;
        byte[] _buffer;
        bool _disposed;

        static string LoadBootstrapFromResources() {
            var asset = Resources.Load<TextAsset>(DefaultBootstrapResourcePath);
            if (!asset) {
                Debug.LogWarning("[QuickJS] Bootstrap script not found at Resources/" +
                                 DefaultBootstrapResourcePath);
                return null;
            }
            return asset.text;
        }

        public Context(int bufferSize = 16 * 1024) {
            _ptr = qjs_create();
            if (_ptr == IntPtr.Zero) {
                throw new Exception("qjs_create failed");
            }
            _buffer = new byte[bufferSize];

            // Install JS-side helpers (__cs, wrapObject, newObject, callMethod, callStatic)
            var bootstrap = LoadBootstrapFromResources();
            if (!string.IsNullOrEmpty(bootstrap)) {
                Eval(bootstrap, "quickjs_bootstrap.js");
            }
        }

        public string Eval(string code, string filename = "<input>", int evalFlags = 0) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(Context));
            }
            if (_ptr == IntPtr.Zero) {
                throw new InvalidOperationException("QuickJS context is null");
            }

            var handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            try {
                int result = qjs_eval(
                    _ptr,
                    code,
                    filename,
                    evalFlags,
                    handle.AddrOfPinnedObject(),
                    _buffer.Length
                );

                int len = 0;
                for (; len < _buffer.Length; len++) {
                    if (_buffer[len] == 0) break;
                }

                var str = System.Text.Encoding.UTF8.GetString(_buffer, 0, len);

                if (result != 0) {
                    throw new Exception("QuickJS error: " + str);
                }

                return str;
            } finally {
                handle.Free();
            }
        }

        public void RunGC() {
            if (_disposed || _ptr == IntPtr.Zero) return;
            qjs_run_gc(_ptr);
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            if (_ptr != IntPtr.Zero) {
                qjs_destroy(_ptr);
                _ptr = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ~Context() {
            // Last line of defense if somebody forgets Dispose
            if (_ptr != IntPtr.Zero) {
                qjs_destroy(_ptr);
                _ptr = IntPtr.Zero;
            }
        }

        // MARK: Callbacks
        public object InvokeCallback(int handle, params object[] args) {
            if (_disposed) throw new ObjectDisposedException(nameof(Context));
            if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

            unsafe {
                int argCount = args?.Length ?? 0;
                InteropValue* nativeArgs = null;
                IntPtr[] stringPtrs = null;

                try {
                    if (argCount > 0) {
                        nativeArgs = (InteropValue*)Marshal.AllocHGlobal(sizeof(InteropValue) * argCount);
                        stringPtrs = new IntPtr[argCount];

                        for (int i = 0; i < argCount; i++) {
                            stringPtrs[i] = IntPtr.Zero;
                            nativeArgs[i] = ObjectToInteropValue(args[i], ref stringPtrs[i]);
                        }
                    }

                    InteropValue result = default;
                    int code = qjs_invoke_callback(_ptr, handle, nativeArgs, argCount, &result);

                    if (code != 0) {
                        throw new Exception($"qjs_invoke_callback failed with code {code}");
                    }

                    object ret = InteropValueToObject(result);

                    // Free string result if allocated by native
                    if (result.type == InteropType.String && result.str != IntPtr.Zero) {
                        Marshal.FreeCoTaskMem(result.str);
                    }

                    return ret;
                } finally {
                    if (nativeArgs != null) {
                        Marshal.FreeHGlobal((IntPtr)nativeArgs);
                    }

                    if (stringPtrs != null) {
                        for (int i = 0; i < stringPtrs.Length; i++) {
                            if (stringPtrs[i] != IntPtr.Zero) {
                                Marshal.FreeCoTaskMem(stringPtrs[i]);
                            }
                        }
                    }
                }
            }
        }

        static unsafe InteropValue ObjectToInteropValue(object obj, ref IntPtr stringPtr) {
            InteropValue v = default;
            v.type = InteropType.Null;

            if (obj == null) return v;

            switch (obj) {
                case bool b:
                    v.type = InteropType.Bool;
                    v.b = b ? 1 : 0;
                    break;
                case int i:
                    v.type = InteropType.Int32;
                    v.i32 = i;
                    break;
                case long l:
                    v.type = InteropType.Int64;
                    v.i64 = l;
                    break;
                case float f:
                    v.type = InteropType.Float32;
                    v.f32 = f;
                    break;
                case double d:
                    v.type = InteropType.Double;
                    v.f64 = d;
                    break;
                case string s:
                    v.type = InteropType.String;
                    stringPtr = Marshal.StringToCoTaskMemUTF8(s);
                    v.str = stringPtr;
                    break;
                default:
                    // Check if it's already a registered handle
                    // For now, register new objects
                    int handle = RegisterObject(obj);
                    v.type = InteropType.ObjectHandle;
                    v.handle = handle;
                    break;
            }

            return v;
        }
    }
}