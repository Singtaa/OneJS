using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        IntPtr ctx,
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

    // MARK: Handles
    static int _nextHandle = 1;
    static readonly Dictionary<int, object> _handleTable = new Dictionary<int, object>();
    static readonly Dictionary<object, int> _reverseHandleTable = new Dictionary<object, int>();
    static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
    static readonly object _handleLock = new object();

    // MARK: Callback GC Roots
    // These GCHandles prevent the GC from collecting the delegates while native code holds references
    static GCHandle _logCallbackHandle;
    static GCHandle _invokeCallbackHandle;
    static GCHandle _releaseCallbackHandle;

    // MARK: Cache
    // Using ConcurrentDictionary for thread-safe cache access without explicit locking
    // Method cache key includes argument type hash to handle overloads correctly
    static readonly ConcurrentDictionary<(Type, string, bool, int), MethodInfo> _methodCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), PropertyInfo> _propertyCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), FieldInfo> _fieldCache = new();

    static int RegisterObject(object obj) {
        if (obj == null) return 0;

        // Value types should not go through the handle table - they should be serialized directly.
        // Boxing creates new objects each time, breaking reverse lookup and causing handle leaks.
        var objType = obj.GetType();
        if (objType.IsValueType && !objType.IsPrimitive && !objType.IsEnum) {
            // This is a struct like Vector3, Color, Quaternion, etc.
            // These should be serialized specially, not registered as handles.
            throw new ArgumentException(
                $"Value types should not be registered as handles: {objType.FullName}. " +
                "Serialize them directly using SetReturnValueForStruct.");
        }

        lock (_handleLock) {
            if (_reverseHandleTable.TryGetValue(obj, out int existingHandle)) {
                return existingHandle;
            }

            // Find next available handle, wrapping safely
            int startHandle = _nextHandle;
            while (_handleTable.ContainsKey(_nextHandle)) {
                _nextHandle++;
                if (_nextHandle <= 0) _nextHandle = 1; // wrap around, skip 0
                if (_nextHandle == startHandle) {
                    throw new InvalidOperationException("Handle table exhausted");
                }
            }

            int handle = _nextHandle++;
            if (_nextHandle <= 0) _nextHandle = 1;

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

    /// <summary>
    /// Computes a hash based on argument types to distinguish method overloads.
    /// </summary>
    static int ComputeArgTypeHash(object[] args) {
        if (args == null || args.Length == 0) return 0;

        int hash = args.Length;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] != null) {
                hash = hash * 31 + args[i].GetType().GetHashCode();
            } else {
                hash = hash * 31; // null contributes 0 to distinguish from non-null
            }
        }
        return hash;
    }

    static MethodInfo FindMethodCached(Type type, string name, bool isStatic, object[] args) {
        var argHash = ComputeArgTypeHash(args);
        var key = (type, name, isStatic, argHash);

        if (_methodCache.TryGetValue(key, out var cached)) {
            // Verify arg count still matches as a sanity check
            var parms = cached.GetParameters();
            if (parms.Length == args.Length) return cached;
        }

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
        flags |= isStatic ? BindingFlags.Static : BindingFlags.Instance;

        var method = FindMethod(type, name, flags, args);
        if (method != null) {
            _methodCache[key] = method;
        }
        return method;
    }

    static PropertyInfo FindPropertyCached(Type type, string name, bool isStatic) {
        var key = (type, name, isStatic);
        if (_propertyCache.TryGetValue(key, out var cached)) return cached;

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
        flags |= isStatic ? BindingFlags.Static : BindingFlags.Instance;

        var prop = FindProperty(type, name, flags);
        if (prop != null) {
            _propertyCache[key] = prop;
        }
        return prop;
    }

    static FieldInfo FindFieldCached(Type type, string name, bool isStatic) {
        var key = (type, name, isStatic);
        if (_fieldCache.TryGetValue(key, out var cached)) return cached;

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
        flags |= isStatic ? BindingFlags.Static : BindingFlags.Instance;

        var field = FindField(type, name, flags);
        if (field != null) {
            _fieldCache[key] = field;
        }
        return field;
    }

    // MARK: Return

    /// <summary>
    /// Known Unity struct types that should be serialized directly instead of using handles.
    /// Handles for value types cause issues because boxing creates new objects each time.
    /// </summary>
    static readonly HashSet<Type> _serializableStructTypes = new HashSet<Type> {
        typeof(Vector2),
        typeof(Vector3),
        typeof(Vector4),
        typeof(Vector2Int),
        typeof(Vector3Int),
        typeof(Quaternion),
        typeof(Color),
        typeof(Color32),
        typeof(Rect),
        typeof(RectInt),
        typeof(Bounds),
        typeof(BoundsInt),
        typeof(Matrix4x4),
        typeof(Ray),
        typeof(Ray2D),
        typeof(Plane)
    };

    /// <summary>
    /// Serializes a Unity struct value to a JSON string for transfer to JS.
    /// JS will deserialize this back into a plain object.
    /// </summary>
    static string SerializeStructToJson(object value) {
        var t = value.GetType();

        // Vector2
        if (t == typeof(Vector2)) {
            var v = (Vector2)value;
            return $"{{\"__struct\":\"Vector2\",\"x\":{v.x},\"y\":{v.y}}}";
        }
        // Vector3
        if (t == typeof(Vector3)) {
            var v = (Vector3)value;
            return $"{{\"__struct\":\"Vector3\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        }
        // Vector4
        if (t == typeof(Vector4)) {
            var v = (Vector4)value;
            return $"{{\"__struct\":\"Vector4\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z},\"w\":{v.w}}}";
        }
        // Vector2Int
        if (t == typeof(Vector2Int)) {
            var v = (Vector2Int)value;
            return $"{{\"__struct\":\"Vector2Int\",\"x\":{v.x},\"y\":{v.y}}}";
        }
        // Vector3Int
        if (t == typeof(Vector3Int)) {
            var v = (Vector3Int)value;
            return $"{{\"__struct\":\"Vector3Int\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        }
        // Quaternion
        if (t == typeof(Quaternion)) {
            var q = (Quaternion)value;
            return $"{{\"__struct\":\"Quaternion\",\"x\":{q.x},\"y\":{q.y},\"z\":{q.z},\"w\":{q.w}}}";
        }
        // Color
        if (t == typeof(Color)) {
            var c = (Color)value;
            return $"{{\"__struct\":\"Color\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        }
        // Color32
        if (t == typeof(Color32)) {
            var c = (Color32)value;
            return $"{{\"__struct\":\"Color32\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        }
        // Rect
        if (t == typeof(Rect)) {
            var r = (Rect)value;
            return
                $"{{\"__struct\":\"Rect\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        }
        // RectInt
        if (t == typeof(RectInt)) {
            var r = (RectInt)value;
            return
                $"{{\"__struct\":\"RectInt\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        }
        // Bounds
        if (t == typeof(Bounds)) {
            var b = (Bounds)value;
            return
                $"{{\"__struct\":\"Bounds\",\"centerX\":{b.center.x},\"centerY\":{b.center.y},\"centerZ\":{b.center.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        }
        // BoundsInt
        if (t == typeof(BoundsInt)) {
            var b = (BoundsInt)value;
            return
                $"{{\"__struct\":\"BoundsInt\",\"positionX\":{b.position.x},\"positionY\":{b.position.y},\"positionZ\":{b.position.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        }
        // Matrix4x4
        if (t == typeof(Matrix4x4)) {
            var m = (Matrix4x4)value;
            return
                $"{{\"__struct\":\"Matrix4x4\",\"m00\":{m.m00},\"m01\":{m.m01},\"m02\":{m.m02},\"m03\":{m.m03},\"m10\":{m.m10},\"m11\":{m.m11},\"m12\":{m.m12},\"m13\":{m.m13},\"m20\":{m.m20},\"m21\":{m.m21},\"m22\":{m.m22},\"m23\":{m.m23},\"m30\":{m.m30},\"m31\":{m.m31},\"m32\":{m.m32},\"m33\":{m.m33}}}";
        }
        // Ray
        if (t == typeof(Ray)) {
            var r = (Ray)value;
            return
                $"{{\"__struct\":\"Ray\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"originZ\":{r.origin.z},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y},\"directionZ\":{r.direction.z}}}";
        }
        // Ray2D
        if (t == typeof(Ray2D)) {
            var r = (Ray2D)value;
            return
                $"{{\"__struct\":\"Ray2D\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y}}}";
        }
        // Plane
        if (t == typeof(Plane)) {
            var p = (Plane)value;
            return
                $"{{\"__struct\":\"Plane\",\"normalX\":{p.normal.x},\"normalY\":{p.normal.y},\"normalZ\":{p.normal.z},\"distance\":{p.distance}}}";
        }

        // Fallback for unknown structs - use reflection
        return null;
    }

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

        // Check for known serializable Unity structs (Vector3, Color, etc.)
        // These should be serialized directly to avoid handle table issues with value types
        if (_serializableStructTypes.Contains(t)) {
            string json = SerializeStructToJson(value);
            if (json != null) {
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8(json);
                return;
            }
        }

        // Fallback: treat as object handle (only for reference types)
        int handle = RegisterObject(value);
        resPtr->returnValue.type = InteropType.ObjectHandle;
        resPtr->returnValue.handle = handle;
        resPtr->returnValue.typeHint = StringToUtf8(t.FullName);
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
        // CRITICAL: Pin the delegates with GCHandle to prevent GC from collecting them.
        // The readonly modifier prevents reassignment but doesn't prevent GC collection.
        // Native code holds references to these delegates, but GC doesn't see that.
        _logCallbackHandle = GCHandle.Alloc(_logCallback);
        _invokeCallbackHandle = GCHandle.Alloc(_invokeCallback);
        _releaseCallbackHandle = GCHandle.Alloc(_releaseHandleCallback);

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
    static unsafe void DispatchFromJs(IntPtr ctxPtr, InteropInvokeRequest* reqPtr,
        InteropInvokeResult* resPtr) {
        // ctxPtr is the QjsContext* pointer - can be used for context-specific handling if needed
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

                // Find matching constructor and convert args
                var ctors = type.GetConstructors();
                foreach (var ctor in ctors) {
                    var parms = ctor.GetParameters();
                    if (parms.Length != args.Length) continue;

                    bool match = true;
                    object[] convertedArgs = new object[args.Length];
                    for (int i = 0; i < parms.Length; i++) {
                        try {
                            convertedArgs[i] = Convert.ChangeType(args[i], parms[i].ParameterType);
                        } catch {
                            match = false;
                            break;
                        }
                    }

                    if (match) {
                        object instance = ctor.Invoke(convertedArgs);
                        SetReturnValue(resPtr, instance);
                        return;
                    }
                }

                resPtr->errorCode = 1;
                Debug.LogError(
                    $"[QuickJS] No matching constructor found for {typeName} with {args.Length} args");
                return;
            }

            if (type == null) {
                resPtr->errorCode = 1;
                Debug.LogError("[QuickJS] Type not found: " + typeName);
                return;
            }

            switch (reqPtr->callKind) {
                case InteropInvokeCallKind.Method: {
                    MethodInfo method = FindMethodCached(type, memberName, isStatic, args);
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
                    PropertyInfo prop = FindPropertyCached(type, memberName, isStatic);
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
                    PropertyInfo prop = FindPropertyCached(type, memberName, isStatic);
                    if (prop == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Property not found (set): " + type.FullName + "." +
                                       memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    var pType = prop.PropertyType;
                    value = ConvertToTargetType(value, pType);

                    prop.SetValue(isStatic ? null : target, value);
                    resPtr->returnValue.type = InteropType.Null;
                    return;
                }

                case InteropInvokeCallKind.GetField: {
                    FieldInfo field = FindFieldCached(type, memberName, isStatic);
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
                    FieldInfo field = FindFieldCached(type, memberName, isStatic);
                    if (field == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Field not found (set): " + type.FullName + "." +
                                       memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    var fType = field.FieldType;
                    value = ConvertToTargetType(value, fType);

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

    /// <summary>
    /// Attempts to convert a value to the target type, with special handling for Unity structs.
    /// This is needed because JS may send plain objects that need to be converted to Vector3, Color, etc.
    /// </summary>
    static object ConvertToTargetType(object value, Type targetType) {
        if (value == null) return null;

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType)) {
            return value;
        }

        // Handle primitive type conversions
        if (targetType.IsPrimitive || targetType == typeof(decimal)) {
            try {
                return Convert.ChangeType(value, targetType);
            } catch {
                return value;
            }
        }

        // Special handling for Unity struct types - the value might already be the correct type
        // if it was created via new CS.UnityEngine.Vector3() in JS
        if (_serializableStructTypes.Contains(targetType) && targetType == valueType) {
            return value;
        }
        
        // Handle JSON struct strings - these come from JS when passing Unity structs back
        // Format: {"__struct":"Vector3","x":10,"y":20,"z":30}
        if (value is string jsonStr && jsonStr.StartsWith("{\"__struct\":")) {
            var deserialized = DeserializeJsonToStruct(jsonStr, targetType);
            if (deserialized != null) {
                return deserialized;
            }
        }

        return value;
    }
    
    /// <summary>
    /// Deserializes a JSON string with __struct marker back to a Unity struct.
    /// This is the inverse of SerializeStructToJson.
    /// </summary>
    static object DeserializeJsonToStruct(string json, Type targetType) {
        if (string.IsNullOrEmpty(json)) return null;
        
        try {
            // Simple JSON parsing without external dependencies
            // Extract values using string manipulation (faster than full JSON parser for simple structs)
            
            if (targetType == typeof(Vector2)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                return new Vector2(x, y);
            }
            if (targetType == typeof(Vector3)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                return new Vector3(x, y, z);
            }
            if (targetType == typeof(Vector4)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                float w = ExtractFloat(json, "\"w\":");
                return new Vector4(x, y, z, w);
            }
            if (targetType == typeof(Vector2Int)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                return new Vector2Int(x, y);
            }
            if (targetType == typeof(Vector3Int)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                int z = ExtractInt(json, "\"z\":");
                return new Vector3Int(x, y, z);
            }
            if (targetType == typeof(Quaternion)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                float w = ExtractFloat(json, "\"w\":");
                return new Quaternion(x, y, z, w);
            }
            if (targetType == typeof(Color)) {
                float r = ExtractFloat(json, "\"r\":");
                float g = ExtractFloat(json, "\"g\":");
                float b = ExtractFloat(json, "\"b\":");
                float a = ExtractFloat(json, "\"a\":");
                return new Color(r, g, b, a);
            }
            if (targetType == typeof(Color32)) {
                byte r = (byte)ExtractInt(json, "\"r\":");
                byte g = (byte)ExtractInt(json, "\"g\":");
                byte b = (byte)ExtractInt(json, "\"b\":");
                byte a = (byte)ExtractInt(json, "\"a\":");
                return new Color32(r, g, b, a);
            }
            if (targetType == typeof(Rect)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float w = ExtractFloat(json, "\"width\":");
                float h = ExtractFloat(json, "\"height\":");
                return new Rect(x, y, w, h);
            }
            if (targetType == typeof(RectInt)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                int w = ExtractInt(json, "\"width\":");
                int h = ExtractInt(json, "\"height\":");
                return new RectInt(x, y, w, h);
            }
            if (targetType == typeof(Bounds)) {
                float cx = ExtractFloat(json, "\"centerX\":");
                float cy = ExtractFloat(json, "\"centerY\":");
                float cz = ExtractFloat(json, "\"centerZ\":");
                float sx = ExtractFloat(json, "\"sizeX\":");
                float sy = ExtractFloat(json, "\"sizeY\":");
                float sz = ExtractFloat(json, "\"sizeZ\":");
                return new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
            }
            if (targetType == typeof(BoundsInt)) {
                int px = ExtractInt(json, "\"positionX\":");
                int py = ExtractInt(json, "\"positionY\":");
                int pz = ExtractInt(json, "\"positionZ\":");
                int sx = ExtractInt(json, "\"sizeX\":");
                int sy = ExtractInt(json, "\"sizeY\":");
                int sz = ExtractInt(json, "\"sizeZ\":");
                return new BoundsInt(new Vector3Int(px, py, pz), new Vector3Int(sx, sy, sz));
            }
            if (targetType == typeof(Ray)) {
                float ox = ExtractFloat(json, "\"originX\":");
                float oy = ExtractFloat(json, "\"originY\":");
                float oz = ExtractFloat(json, "\"originZ\":");
                float dx = ExtractFloat(json, "\"directionX\":");
                float dy = ExtractFloat(json, "\"directionY\":");
                float dz = ExtractFloat(json, "\"directionZ\":");
                return new Ray(new Vector3(ox, oy, oz), new Vector3(dx, dy, dz));
            }
            if (targetType == typeof(Ray2D)) {
                float ox = ExtractFloat(json, "\"originX\":");
                float oy = ExtractFloat(json, "\"originY\":");
                float dx = ExtractFloat(json, "\"directionX\":");
                float dy = ExtractFloat(json, "\"directionY\":");
                return new Ray2D(new Vector2(ox, oy), new Vector2(dx, dy));
            }
            if (targetType == typeof(Plane)) {
                float nx = ExtractFloat(json, "\"normalX\":");
                float ny = ExtractFloat(json, "\"normalY\":");
                float nz = ExtractFloat(json, "\"normalZ\":");
                float d = ExtractFloat(json, "\"distance\":");
                return new Plane(new Vector3(nx, ny, nz), d);
            }
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJS] Failed to deserialize struct JSON: {ex.Message}");
        }
        
        return null;
    }
    
    static float ExtractFloat(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0f;
        idx += key.Length;
        int end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == 'e' || json[end] == 'E' || json[end] == '+')) {
            end++;
        }
        if (float.TryParse(json.Substring(idx, end - idx), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result)) {
            return result;
        }
        return 0f;
    }
    
    static int ExtractInt(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0;
        idx += key.Length;
        int end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) {
            end++;
        }
        if (int.TryParse(json.Substring(idx, end - idx), out int result)) {
            return result;
        }
        return 0;
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
            case InteropType.Array:
                // For now, arrays are detected but full element serialization is not yet implemented.
                // The i32 field contains the array length. Full array support requires
                // element-by-element serialization in the JS bootstrap or C code.
                Debug.LogWarning("[QuickJS] Array argument detected (length=" + v.i32 +
                                 ") but full array serialization is not yet implemented.");
                return null;
            default:
                return null;
        }
    }

    // MARK: Context class
    public sealed class Context : IDisposable {
        const string DefaultBootstrapResourcePath = "OneJS/QuickJSBootstrap.js";
        const int GCInterval = 100; // Run GC every N evals
        const int HandleCountThreshold = 100; // Also run GC if handles exceed this count
        static string _cachedBootstrap;

        IntPtr _ptr;
        byte[] _buffer;
        bool _disposed;
        int _evalCount;

        static string LoadBootstrapFromResources() {
            if (_cachedBootstrap != null) return _cachedBootstrap;

            var asset = Resources.Load<TextAsset>(DefaultBootstrapResourcePath);
            if (!asset) {
                Debug.LogWarning("[QuickJS] Bootstrap script not found at Resources/" +
                                 DefaultBootstrapResourcePath);
                return null;
            }
            _cachedBootstrap = asset.text;
            return _cachedBootstrap;
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

                // Run GC periodically to trigger FinalizationRegistry callbacks
                // Also run if handle count exceeds threshold to prevent leaks from chained property access
                // (e.g., go.transform.position creates intermediate handles that need cleanup)
                _evalCount++;
                if (_evalCount >= GCInterval || GetHandleCount() > HandleCountThreshold) {
                    _evalCount = 0;
                    qjs_run_gc(_ptr);
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

        /// <summary>
        /// Runs GC if the handle table exceeds the given threshold.
        /// Call this from Update() if you need more aggressive cleanup.
        /// </summary>
        public void MaybeRunGC(int threshold = 50) {
            if (_disposed || _ptr == IntPtr.Zero) return;
            lock (_handleLock) {
                if (_handleTable.Count > threshold) {
                    qjs_run_gc(_ptr);
                }
            }
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