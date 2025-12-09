using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Callback Delegates
    static readonly CsLogCallback _logCallback = HandleLogFromJs;
    static readonly unsafe CsInvokeCallback _invokeCallback = DispatchFromJs;
    static readonly CsReleaseHandleCallback _releaseHandleCallback = HandleReleaseFromJs;

    // MARK: Callback GC Roots
    // These GCHandles prevent the GC from collecting the delegates while native code holds references
    static GCHandle _logCallbackHandle;
    static GCHandle _invokeCallbackHandle;
    static GCHandle _releaseCallbackHandle;

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
        UnregisterObject(handle);
    }

    static void HandleLogFromJs(IntPtr msgPtr) {
        if (msgPtr == IntPtr.Zero) return;

        string msg = Marshal.PtrToStringUTF8(msgPtr);
        if (msg == null) return;

        Debug.Log("[QuickJS] " + msg);
    }

    // MARK: Dispatch
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

    // MARK: Value Conversion
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

    internal static object InteropValueToObject(InteropValue v) {
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

    internal static unsafe InteropValue ObjectToInteropValue(object obj, ref IntPtr stringPtr) {
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

