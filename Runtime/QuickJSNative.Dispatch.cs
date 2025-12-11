using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Callback Delegates
    static readonly CsLogCallback _logCallback = HandleLogFromJs;
    static readonly unsafe CsInvokeCallback _invokeCallback = DispatchFromJs;
    static readonly CsReleaseHandleCallback _releaseHandleCallback = HandleReleaseFromJs;

    // MARK: Callback GC Roots
    static GCHandle _logCallbackHandle;
    static GCHandle _invokeCallbackHandle;
    static GCHandle _releaseCallbackHandle;

    static QuickJSNative() {
        _logCallbackHandle = GCHandle.Alloc(_logCallback);
        _invokeCallbackHandle = GCHandle.Alloc(_invokeCallback);
        _releaseCallbackHandle = GCHandle.Alloc(_releaseHandleCallback);

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

            // Debug.Log shortcut
            if (typeName == "UnityEngine.Debug" &&
                memberName == "Log" &&
                reqPtr->callKind == InteropInvokeCallKind.Method) {
                Debug.Log(args.Length > 0 ? args[0]?.ToString() : "(null)");
                resPtr->returnValue.type = InteropType.Null;
                return;
            }

            bool isStatic = reqPtr->isStatic != 0;
            object target = null;
            Type type = null;

            if (!isStatic && reqPtr->targetHandle != 0) {
                target = GetObjectByHandle(reqPtr->targetHandle);
                if (target != null) type = target.GetType();
            }

            if (type == null) type = ResolveType(typeName);

            // Type queries
            if (reqPtr->callKind == InteropInvokeCallKind.TypeExists) {
                resPtr->returnValue.type = InteropType.Bool;
                resPtr->returnValue.b = type != null ? 1 : 0;
                return;
            }

            if (reqPtr->callKind == InteropInvokeCallKind.IsEnumType) {
                resPtr->returnValue.type = InteropType.Bool;
                resPtr->returnValue.b = type != null && type.IsEnum ? 1 : 0;
                return;
            }

            // Constructor
            if (reqPtr->callKind == InteropInvokeCallKind.Ctor) {
                if (type == null) {
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Type not found for ctor: " + typeName);
                    return;
                }

                var ctors = type.GetConstructors();
                foreach (var ctor in ctors) {
                    var parms = ctor.GetParameters();
                    if (parms.Length != args.Length) continue;

                    bool match = true;
                    object[] convertedArgs = new object[args.Length];
                    for (int i = 0; i < parms.Length; i++) {
                        var pType = parms[i].ParameterType;
                        var converted = ConvertToTargetType(args[i], pType);

                        if (converted == null) {
                            if (pType.IsValueType && Nullable.GetUnderlyingType(pType) == null) {
                                match = false;
                                break;
                            }
                        } else if (!pType.IsAssignableFrom(converted.GetType())) {
                            match = false;
                            break;
                        }

                        convertedArgs[i] = converted;
                    }

                    if (match) {
                        object instance = ctor.Invoke(convertedArgs);
                        SetReturnValue(resPtr, instance);
                        return;
                    }
                }

                resPtr->errorCode = 1;
                Debug.LogError($"[QuickJS] No matching ctor for {typeName} with {args.Length} args");
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

                    // Convert args to match parameter types
                    var parms = method.GetParameters();
                    for (int i = 0; i < parms.Length; i++) {
                        args[i] = ConvertToTargetType(args[i], parms[i].ParameterType);
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
                        Debug.LogError("[QuickJS] Property not found (set): " + type.FullName + "." + memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    value = ConvertToTargetType(value, prop.PropertyType);
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
                        Debug.LogError("[QuickJS] Field not found (set): " + type.FullName + "." + memberName);
                        return;
                    }

                    object value = args.Length > 0 ? args[0] : null;
                    value = ConvertToTargetType(value, field.FieldType);
                    field.SetValue(isStatic ? null : target, value);
                    resPtr->returnValue.type = InteropType.Null;
                    return;
                }

                default:
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Unsupported call kind: " + reqPtr->callKind);
                    return;
            }
        } catch (Exception ex) {
            resPtr->errorCode = 1;
            Debug.LogError("[QuickJS Invoke Error] " + ex);
        }
    }

    // MARK: Return Value
    static unsafe void SetReturnValue(InteropInvokeResult* resPtr, object value) {
        resPtr->returnValue = default;

        if (value == null) {
            resPtr->returnValue.type = InteropType.Null;
            return;
        }

        var t = value.GetType();

        // Primitives
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

        // Serializable struct - use the new generic system
        if (IsSerializableStruct(t)) {
            var json = SerializeStruct(value);
            if (json != null) {
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8(json);
                return;
            }
        }

        // Reference type - register as handle
        int handle = RegisterObject(value);
        resPtr->returnValue.type = InteropType.ObjectHandle;
        resPtr->returnValue.handle = handle;
        resPtr->returnValue.typeHint = StringToUtf8(t.FullName);
    }

    // MARK: Value Conversion
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
            case InteropType.JsonObject:
                // Plain JS object serialized as JSON - parse to dictionary
                // ConvertToTargetType will convert to proper struct type
                var json = PtrToStringUtf8(v.str);
                if (!string.IsNullOrEmpty(json)) {
                    return ParseSimpleJson(json);
                }
                return null;
            case InteropType.Array:
                Debug.LogWarning("[QuickJS] Array deserialization not yet implemented");
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
                int handle = RegisterObject(obj);
                v.type = InteropType.ObjectHandle;
                v.handle = handle;
                break;
        }

        return v;
    }
}