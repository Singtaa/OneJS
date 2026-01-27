using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Current Context (for delegate wrapper creation during dispatch)
    [ThreadStatic]
    static IntPtr _currentContextPtr;

    /// <summary>
    /// The native context pointer for the currently executing dispatch.
    /// Only valid during a dispatch call. Used by delegate wrappers.
    /// </summary>
    internal static IntPtr CurrentContextPtr => _currentContextPtr;

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

    [MonoPInvokeCallback(typeof(CsReleaseHandleCallback))]
    static void HandleReleaseFromJs(int handle) {
        if (handle == 0) return;
        UnregisterObject(handle);
    }

    [MonoPInvokeCallback(typeof(CsLogCallback))]
    static void HandleLogFromJs(IntPtr msgPtr) {
        if (msgPtr == IntPtr.Zero) return;
        string msg = Marshal.PtrToStringUTF8(msgPtr);
        if (msg == null) return;
        Debug.Log("[QuickJS] " + msg);
    }

    // MARK: Dispatch
    [MonoPInvokeCallback(typeof(CsInvokeCallback))]
    static unsafe void DispatchFromJs(IntPtr ctxPtr, InteropInvokeRequest* reqPtr,
        InteropInvokeResult* resPtr) {
        resPtr->errorCode = 0;
        resPtr->errorMsg = IntPtr.Zero;
        resPtr->returnValue = default;
        resPtr->returnValue.type = InteropType.Null;

        // Store context pointer for delegate wrapper creation
        var prevContext = _currentContextPtr;
        _currentContextPtr = ctxPtr;

        try {
            bool isStatic = reqPtr->isStatic != 0;
            int argCount = reqPtr->argCount;
            var argsPtr = (InteropValue*)reqPtr->args;

            // Get target/type from handle (no string allocation needed)
            object target = null;
            Type type = null;

            if (!isStatic && reqPtr->targetHandle != 0) {
                target = GetObjectByHandle(reqPtr->targetHandle);
                if (target != null) type = target.GetType();
            }

            // ============================================================
            // ZERO-ALLOC FAST PATH - checked BEFORE any string allocation
            // ============================================================
            if (TryFastPathZeroAlloc(
                    type,
                    reqPtr->typeName,
                    reqPtr->memberName,
                    reqPtr->callKind,
                    isStatic,
                    target,
                    argsPtr,
                    argCount,
                    &resPtr->returnValue)) {
                return; // Handled without any managed allocation!
            }

            // ============================================================
            // SLOW PATH - now we allocate strings for reflection
            // ============================================================
            string typeName = PtrToStringUtf8(reqPtr->typeName);
            string memberName = PtrToStringUtf8(reqPtr->memberName);

            // Resolve type if not already resolved from handle
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

            // MakeGenericType: List`1 + [Int32] => List<Int32>
            if (reqPtr->callKind == InteropInvokeCallKind.MakeGenericType) {
                if (type == null) {
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Generic type definition not found: " + typeName);
                    return;
                }

                if (!type.IsGenericTypeDefinition) {
                    resPtr->errorCode = 1;
                    Debug.LogError("[QuickJS] Type is not a generic definition: " + typeName);
                    return;
                }

                // Parse type arguments from args
                var typeArgs = new Type[argCount];
                for (int i = 0; i < argCount; i++) {
                    string typeArgName = InteropValueToString(argsPtr[i]);
                    if (string.IsNullOrEmpty(typeArgName)) {
                        resPtr->errorCode = 1;
                        Debug.LogError($"[QuickJS] Invalid type argument at index {i}");
                        return;
                    }

                    Type typeArg = ResolveType(typeArgName);
                    if (typeArg == null) {
                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Type argument not found: " + typeArgName);
                        return;
                    }
                    typeArgs[i] = typeArg;
                }

                try {
                    Type constructedType = type.MakeGenericType(typeArgs);

                    // Cache the constructed type for future lookups
                    string constructedTypeName = GetGenericTypeName(constructedType);
                    CacheType(constructedTypeName, constructedType);

                    // Return the type name as a string
                    resPtr->returnValue.type = InteropType.String;
                    resPtr->returnValue.str = StringToUtf8(constructedTypeName);
                    return;
                } catch (Exception ex) {
                    resPtr->errorCode = 1;
                    Debug.LogError($"[QuickJS] Failed to make generic type: {ex.Message}");
                    return;
                }
            }

            // Convert args to object[] (allocates)
            object[] args = argCount > 0 ? new object[argCount] : Array.Empty<object>();

            if (argCount > 0 && argsPtr != null) {
                for (int i = 0; i < argCount; i++) {
                    args[i] = InteropValueToObject(argsPtr[i]);
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
                        // Fallback: try as property getter if no method found
                        // This allows JS to access C# properties with PascalCase names naturally
                        PropertyInfo prop = FindPropertyCached(type, memberName, isStatic);
                        if (prop != null && prop.CanRead) {
                            object propResult = prop.GetValue(isStatic ? null : target);
                            SetReturnValue(resPtr, propResult);
                            return;
                        }

                        resPtr->errorCode = 1;
                        Debug.LogError("[QuickJS] Method not found: " + type.FullName + "." + memberName);
                        return;
                    }

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
                        // Fallback: check if there's a method with this name (any signature)
                        // Return magic string so JS can create a function wrapper
                        if (HasMethodByName(type, memberName, isStatic)) {
                            SetReturnValue(resPtr, "__oneJS_methodRef__");
                            return;
                        }

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
                        Debug.LogError("[QuickJS] Field not found (set): " + type.FullName + "." +
                                       memberName);
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
        } catch (TargetInvocationException tie) {
            // Unwrap reflection exceptions to get the actual error
            // TargetInvocationException wraps the real exception from reflected method calls
            var innerEx = tie.InnerException ?? tie;
            resPtr->errorCode = 1;

            string typeName = PtrToStringUtf8(reqPtr->typeName) ?? "<unknown>";
            string memberName = PtrToStringUtf8(reqPtr->memberName) ?? "<unknown>";

            Debug.LogError(
                $"[QuickJS Invoke Error] {reqPtr->callKind} on {typeName}.{memberName} failed:\n" +
                $"  Exception: {innerEx.GetType().Name}: {innerEx.Message}\n" +
                $"  Stack trace:\n{innerEx.StackTrace}");
        } catch (Exception ex) {
            resPtr->errorCode = 1;

            // Preserve full exception context
            string typeName = PtrToStringUtf8(reqPtr->typeName) ?? "<unknown>";
            string memberName = PtrToStringUtf8(reqPtr->memberName) ?? "<unknown>";

            Debug.LogError(
                $"[QuickJS Invoke Error] {reqPtr->callKind} on {typeName}.{memberName} failed:\n" +
                $"  Exception: {ex.GetType().Name}: {ex.Message}\n" +
                $"  Stack trace:\n{ex.StackTrace}");
        } finally {
            // Restore previous context pointer
            _currentContextPtr = prevContext;
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

        // Unity structs (zero-alloc path)
        if (TrySetReturnValueForUnityStruct(resPtr, value, t)) return;

        // Primitives
        if (TrySetReturnValueForPrimitive(resPtr, value, t)) return;

        // Serializable structs
        if (IsSerializableStruct(t)) {
            var json = SerializeStruct(value);
            if (json != null) {
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8(json);
                return;
            }
        }

        // Tasks (async)
        if (value is Task task) {
            SetReturnValueForTask(resPtr, task);
            return;
        }

        // Reference type - register as handle
        int handle = RegisterObject(value);
        resPtr->returnValue.type = InteropType.ObjectHandle;
        resPtr->returnValue.handle = handle;
        resPtr->returnValue.typeHint = StringToUtf8(t.FullName);
    }

    /// <summary>
    /// Handle Unity vector/color structs. Returns true if handled.
    /// </summary>
    static unsafe bool TrySetReturnValueForUnityStruct(InteropInvokeResult* resPtr, object value, Type t) {
        if (t == typeof(Vector2)) {
            var v = (Vector2)value;
            resPtr->returnValue.type = InteropType.Vector3;
            resPtr->returnValue.vecX = v.x;
            resPtr->returnValue.vecY = v.y;
            resPtr->returnValue.vecZ = 0;
            return true;
        }

        if (t == typeof(Vector3)) {
            var v = (Vector3)value;
            resPtr->returnValue.type = InteropType.Vector3;
            resPtr->returnValue.vecX = v.x;
            resPtr->returnValue.vecY = v.y;
            resPtr->returnValue.vecZ = v.z;
            return true;
        }

        if (t == typeof(Vector4)) {
            var v = (Vector4)value;
            resPtr->returnValue.type = InteropType.Vector4;
            resPtr->returnValue.vecX = v.x;
            resPtr->returnValue.vecY = v.y;
            resPtr->returnValue.vecZ = v.z;
            resPtr->returnValue.vecW = v.w;
            return true;
        }

        if (t == typeof(Quaternion)) {
            var q = (Quaternion)value;
            resPtr->returnValue.type = InteropType.Vector4;
            resPtr->returnValue.vecX = q.x;
            resPtr->returnValue.vecY = q.y;
            resPtr->returnValue.vecZ = q.z;
            resPtr->returnValue.vecW = q.w;
            return true;
        }

        if (t == typeof(Color)) {
            var c = (Color)value;
            resPtr->returnValue.type = InteropType.Vector4;
            resPtr->returnValue.vecX = c.r;
            resPtr->returnValue.vecY = c.g;
            resPtr->returnValue.vecZ = c.b;
            resPtr->returnValue.vecW = c.a;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handle primitive types. Returns true if handled.
    /// </summary>
    static unsafe bool TrySetReturnValueForPrimitive(InteropInvokeResult* resPtr, object value, Type t) {
        switch (Type.GetTypeCode(t)) {
            case TypeCode.Boolean:
                resPtr->returnValue.type = InteropType.Bool;
                resPtr->returnValue.b = (bool)value ? 1 : 0;
                return true;

            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
                resPtr->returnValue.type = InteropType.Int32;
                resPtr->returnValue.i32 = Convert.ToInt32(value);
                return true;

            case TypeCode.UInt32:
            case TypeCode.Int64:
                resPtr->returnValue.type = InteropType.Int64;
                resPtr->returnValue.i64 = Convert.ToInt64(value);
                return true;

            case TypeCode.UInt64:
                resPtr->returnValue.type = InteropType.Double;
                resPtr->returnValue.f64 = Convert.ToDouble(value);
                return true;

            case TypeCode.Single:
                resPtr->returnValue.type = InteropType.Float32;
                resPtr->returnValue.f32 = (float)value;
                return true;

            case TypeCode.Double:
                resPtr->returnValue.type = InteropType.Double;
                resPtr->returnValue.f64 = (double)value;
                return true;

            case TypeCode.String:
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8(value.ToString());
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Handle Task/Task&lt;T&gt; for async completion.
    /// </summary>
    static unsafe void SetReturnValueForTask(InteropInvokeResult* resPtr, Task task) {
        if (task.IsCompleted) {
            if (task.IsFaulted) {
                string errorMsg = task.Exception?.InnerException?.Message ??
                                  task.Exception?.Message ?? "Task faulted";
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8($"{{\"__csError\":\"{EscapeJsString(errorMsg)}\"}}");
                return;
            }

            if (task.IsCanceled) {
                resPtr->returnValue.type = InteropType.String;
                resPtr->returnValue.str = StringToUtf8("{\"__csError\":\"Task was canceled\"}");
                return;
            }

            // Task succeeded - return the result
            object result = GetTaskResultDirect(task);
            if (result == null) {
                resPtr->returnValue.type = InteropType.Null;
                return;
            }

            // Recursively set the return value with the actual result
            SetReturnValue(resPtr, result);
            return;
        }

        // Task is still pending - register for async completion
        int taskId = RegisterTask(task);
        resPtr->returnValue.type = InteropType.String;
        resPtr->returnValue.str = StringToUtf8($"{{\"__csTaskId\":{taskId}}}");
    }

    // MARK: Task Result Extraction
    /// <summary>
    /// Extract result from a completed Task for direct return (not async).
    /// Returns null for void tasks or non-generic tasks.
    /// </summary>
    static object GetTaskResultDirect(Task task) {
        var taskType = task.GetType();
        if (!taskType.IsGenericType) return null;

        var typeArgs = taskType.GetGenericArguments();
        if (typeArgs.Length == 0) return null;

        var resultType = typeArgs[0];
        // VoidTaskResult indicates a void async method
        if (resultType.Name == "VoidTaskResult") return null;

        var resultProp = taskType.GetProperty("Result");
        if (resultProp == null) return null;

        try {
            return resultProp.GetValue(task);
        } catch {
            return null;
        }
    }

    static string EscapeJsString(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
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
            case InteropType.Vector3:
                return new Vector3(v.vecX, v.vecY, v.vecZ);
            case InteropType.Vector4:
                var hint = PtrToStringUtf8(v.typeHint);
                if (hint == "color") {
                    return new Color(v.vecX, v.vecY, v.vecZ, v.vecW);
                }
                return new Vector4(v.vecX, v.vecY, v.vecZ, v.vecW);
            case InteropType.JsonObject:
                var json = PtrToStringUtf8(v.str);
                if (!string.IsNullOrEmpty(json)) {
                    var dict = ParseSimpleJson(json);
                    if (dict != null) {
                        // Check for array marker from JS
                        if (dict.TryGetValue("__csArray", out var arrayData)) {
                            string arrayType = null;
                            if (dict.TryGetValue("__csArrayType", out var at) && at is string ats) {
                                arrayType = ats;
                            }
                            return ConvertJsArrayToCs(arrayData, arrayType);
                        }
                        if (dict.TryGetValue("__csTypeRef", out var typeRefName) &&
                            typeRefName is string tn) {
                            return ResolveType(tn);
                        }
                        return dict;
                    }
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
            case Vector3 vec:
                v.type = InteropType.Vector3;
                v.vecX = vec.x;
                v.vecY = vec.y;
                v.vecZ = vec.z;
                break;
            case Quaternion q:
                v.type = InteropType.Vector4;
                v.vecX = q.x;
                v.vecY = q.y;
                v.vecZ = q.z;
                v.vecW = q.w;
                break;
            case Color c:
                v.type = InteropType.Vector4;
                v.vecX = c.r;
                v.vecY = c.g;
                v.vecZ = c.b;
                v.vecW = c.a;
                break;
            default:
                int handle = RegisterObject(obj);
                v.type = InteropType.ObjectHandle;
                v.handle = handle;
                break;
        }

        return v;
    }

    // MARK: Array Conversion
    /// <summary>
    /// Convert JS array data to C# array based on type hint.
    /// </summary>
    static object ConvertJsArrayToCs(object arrayData, string typeHint) {
        if (arrayData == null) return null;

        // arrayData should be a List<object> from JSON parsing
        var list = arrayData as List<object>;
        if (list == null) {
            // Try to get it as a dictionary array (shouldn't happen, but defensive)
            if (arrayData is System.Collections.IEnumerable enumerable) {
                list = new List<object>();
                foreach (var item in enumerable) {
                    list.Add(item);
                }
            } else {
                return null;
            }
        }

        int count = list.Count;
        if (count == 0) {
            // Return empty array of appropriate type
            return typeHint switch {
                "float" => Array.Empty<float>(),
                "double" => Array.Empty<double>(),
                "int" => Array.Empty<int>(),
                "short" => Array.Empty<short>(),
                "byte" => Array.Empty<byte>(),
                "sbyte" => Array.Empty<sbyte>(),
                "uint" => Array.Empty<uint>(),
                "ushort" => Array.Empty<ushort>(),
                "long" => Array.Empty<long>(),
                "ulong" => Array.Empty<ulong>(),
                "bool" => Array.Empty<bool>(),
                "string" => Array.Empty<string>(),
                "Vector2" => Array.Empty<Vector2>(),
                "Vector3" => Array.Empty<Vector3>(),
                "Vector4" => Array.Empty<Vector4>(),
                "Color" => Array.Empty<Color>(),
                _ => Array.Empty<object>()
            };
        }

        // Convert based on type hint
        switch (typeHint) {
            case "float":
                var floatArr = new float[count];
                for (int i = 0; i < count; i++) {
                    floatArr[i] = Convert.ToSingle(list[i]);
                }
                return floatArr;

            case "double":
                var doubleArr = new double[count];
                for (int i = 0; i < count; i++) {
                    doubleArr[i] = Convert.ToDouble(list[i]);
                }
                return doubleArr;

            case "int":
                var intArr = new int[count];
                for (int i = 0; i < count; i++) {
                    intArr[i] = Convert.ToInt32(list[i]);
                }
                return intArr;

            case "short":
                var shortArr = new short[count];
                for (int i = 0; i < count; i++) {
                    shortArr[i] = Convert.ToInt16(list[i]);
                }
                return shortArr;

            case "byte":
                var byteArr = new byte[count];
                for (int i = 0; i < count; i++) {
                    byteArr[i] = Convert.ToByte(list[i]);
                }
                return byteArr;

            case "sbyte":
                var sbyteArr = new sbyte[count];
                for (int i = 0; i < count; i++) {
                    sbyteArr[i] = Convert.ToSByte(list[i]);
                }
                return sbyteArr;

            case "uint":
                var uintArr = new uint[count];
                for (int i = 0; i < count; i++) {
                    uintArr[i] = Convert.ToUInt32(list[i]);
                }
                return uintArr;

            case "ushort":
                var ushortArr = new ushort[count];
                for (int i = 0; i < count; i++) {
                    ushortArr[i] = Convert.ToUInt16(list[i]);
                }
                return ushortArr;

            case "long":
                var longArr = new long[count];
                for (int i = 0; i < count; i++) {
                    longArr[i] = Convert.ToInt64(list[i]);
                }
                return longArr;

            case "ulong":
                var ulongArr = new ulong[count];
                for (int i = 0; i < count; i++) {
                    ulongArr[i] = Convert.ToUInt64(list[i]);
                }
                return ulongArr;

            case "bool":
                var boolArr = new bool[count];
                for (int i = 0; i < count; i++) {
                    boolArr[i] = Convert.ToBoolean(list[i]);
                }
                return boolArr;

            case "string":
                var strArr = new string[count];
                for (int i = 0; i < count; i++) {
                    strArr[i] = list[i]?.ToString();
                }
                return strArr;

            case "Vector2":
                var vec2Arr = new Vector2[count];
                for (int i = 0; i < count; i++) {
                    vec2Arr[i] = ConvertToVector2(list[i]);
                }
                return vec2Arr;

            case "Vector3":
                var vec3Arr = new Vector3[count];
                for (int i = 0; i < count; i++) {
                    vec3Arr[i] = ConvertToVector3(list[i]);
                }
                return vec3Arr;

            case "Vector4":
                var vec4Arr = new Vector4[count];
                for (int i = 0; i < count; i++) {
                    vec4Arr[i] = ConvertToVector4(list[i]);
                }
                return vec4Arr;

            case "Color":
                var colorArr = new Color[count];
                for (int i = 0; i < count; i++) {
                    colorArr[i] = ConvertToColor(list[i]);
                }
                return colorArr;

            default:
                // Return as object array
                return list.ToArray();
        }
    }

    static Vector2 ConvertToVector2(object obj) {
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            return new Vector2(x, y);
        }
        if (obj is List<object> list && list.Count >= 2) {
            return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
        }
        if (obj is Vector2 v2) return v2;
        return Vector2.zero;
    }

    static Vector3 ConvertToVector3(object obj) {
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            float z = dict.TryGetValue("z", out var zv) ? Convert.ToSingle(zv) : 0f;
            return new Vector3(x, y, z);
        }
        if (obj is List<object> list && list.Count >= 3) {
            return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
        }
        if (obj is Vector3 v3) return v3;
        return Vector3.zero;
    }

    static Vector4 ConvertToVector4(object obj) {
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            float z = dict.TryGetValue("z", out var zv) ? Convert.ToSingle(zv) : 0f;
            float w = dict.TryGetValue("w", out var wv) ? Convert.ToSingle(wv) : 0f;
            return new Vector4(x, y, z, w);
        }
        if (obj is List<object> list && list.Count >= 4) {
            return new Vector4(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2]), Convert.ToSingle(list[3]));
        }
        if (obj is Vector4 v4) return v4;
        return Vector4.zero;
    }

    static Color ConvertToColor(object obj) {
        if (obj is Dictionary<string, object> dict) {
            float r = dict.TryGetValue("r", out var rv) ? Convert.ToSingle(rv) : 0f;
            float g = dict.TryGetValue("g", out var gv) ? Convert.ToSingle(gv) : 0f;
            float b = dict.TryGetValue("b", out var bv) ? Convert.ToSingle(bv) : 0f;
            float a = dict.TryGetValue("a", out var av) ? Convert.ToSingle(av) : 1f;
            return new Color(r, g, b, a);
        }
        if (obj is List<object> list && list.Count >= 3) {
            float r = Convert.ToSingle(list[0]);
            float g = Convert.ToSingle(list[1]);
            float b = Convert.ToSingle(list[2]);
            float a = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;
            return new Color(r, g, b, a);
        }
        if (obj is Color c) return c;
        return Color.white;
    }
}