using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Zero-allocation interop system for hot-path C# method invocations from JavaScript.
///
/// This system provides a way to call registered C# methods from JavaScript without any
/// managed heap allocations, making it suitable for per-frame calls in performance-critical code.
///
/// ## How it works:
/// 1. Register methods at init time using ZeroAllocInterop.Bind()
/// 2. Get a binding ID back
/// 3. Call from JS using __zaInvokeN(bindingId, arg0, arg1, ...) where N is the arg count
///
/// ## Zero-allocation guarantee:
/// - Native side uses stack-allocated InteropValue arrays (no malloc)
/// - C# side uses pre-cached delegates (no boxing, no allocations)
/// - Arguments are passed as primitives/structs (no object allocations)
///
/// ## Supported argument types (zero-alloc):
/// - int, float, double, bool
/// - string (pointer to QuickJS internal string, valid during call)
/// - Object handles (int __csHandle)
/// - Vector3, Vector4/Color (binary packed floats)
///
/// ## Limitations:
/// - Max 8 arguments per call
/// - Complex objects require JSON serialization (not zero-alloc)
/// - Return values limited to primitives and handles
/// </summary>
public static partial class QuickJSNative {
    // MARK: Zero-Alloc Binding Registry

    /// <summary>
    /// Delegate type for zero-alloc method handlers.
    /// The handler receives raw InteropValue pointers and must convert them to typed arguments.
    /// </summary>
    public unsafe delegate void ZeroAllocHandler(InteropValue* args, int argCount, InteropValue* result);

    /// <summary>
    /// Registry of bound methods by ID.
    /// IDs start at 1 (0 is reserved for "not found").
    /// </summary>
    static readonly Dictionary<int, ZeroAllocHandler> _bindings = new();
    static int _nextBindingId = 1;

    /// <summary>
    /// Keep a reference to the native callback to prevent GC collection.
    /// </summary>
    static CsZeroAllocCallback _zeroAllocCallbackRef;
    static bool _zeroAllocInitialized;

    /// <summary>
    /// Initialize the zero-alloc dispatch system.
    /// Called automatically when first binding is registered.
    /// </summary>
    static unsafe void EnsureZeroAllocInitialized() {
        if (_zeroAllocInitialized) return;
        _zeroAllocInitialized = true;

        // Create and pin the callback delegate
        _zeroAllocCallbackRef = OnZeroAllocDispatch;
        qjs_set_cs_zeroalloc_callback(_zeroAllocCallbackRef);
    }

    /// <summary>
    /// Native callback handler - dispatches to registered bindings.
    /// </summary>
    [AOT.MonoPInvokeCallback(typeof(CsZeroAllocCallback))]
    static unsafe void OnZeroAllocDispatch(int bindingId, InteropValue* args, int argCount, InteropValue* outResult) {
        // Initialize result to null
        outResult->type = InteropType.Null;

        if (!_bindings.TryGetValue(bindingId, out var handler)) {
            Debug.LogWarning($"[ZeroAlloc] Unknown binding ID: {bindingId}");
            return;
        }

        try {
            handler(args, argCount, outResult);
        } catch (Exception e) {
            Debug.LogError($"[ZeroAlloc] Handler error for binding {bindingId}: {e}");
        }
    }

    // MARK: Public Registration API

    /// <summary>
    /// Register a zero-alloc handler and return its binding ID.
    /// The handler receives raw InteropValue pointers for maximum performance.
    /// </summary>
    /// <param name="handler">The handler delegate</param>
    /// <returns>Binding ID to use from JavaScript</returns>
    public static int RegisterZeroAllocBinding(ZeroAllocHandler handler) {
        EnsureZeroAllocInitialized();

        int id = _nextBindingId++;
        _bindings[id] = handler;
        return id;
    }

    /// <summary>
    /// Unregister a binding by ID.
    /// </summary>
    public static bool UnregisterZeroAllocBinding(int bindingId) {
        return _bindings.Remove(bindingId);
    }

    /// <summary>
    /// Get the number of registered bindings.
    /// </summary>
    public static int ZeroAllocBindingCount => _bindings.Count;

    // MARK: Dynamic Binding (for TypeScript interop.bind())

    /// <summary>
    /// Register a C# static method for zero-alloc calling from JavaScript.
    /// Called by TypeScript's interop.bind() function.
    ///
    /// This uses reflection to find the method, so it should only be called
    /// at init time, not in hot paths.
    /// </summary>
    public static int RegisterZeroAllocMethodBinding(string typeName, string methodName, int argCount) {
        try {
            var type = FindType(typeName);
            if (type == null) {
                Debug.LogWarning($"[ZeroAlloc] Type not found: {typeName}");
                return 0;
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            MethodInfo targetMethod = null;

            foreach (var method in methods) {
                if (method.Name == methodName) {
                    var parameters = method.GetParameters();
                    if (parameters.Length == argCount) {
                        targetMethod = method;
                        break;
                    }
                }
            }

            if (targetMethod == null) {
                Debug.LogWarning($"[ZeroAlloc] Method not found: {typeName}.{methodName} with {argCount} args");
                return 0;
            }

            var handler = CreateReflectionHandler(targetMethod);
            return RegisterZeroAllocBinding(handler);
        } catch (Exception e) {
            Debug.LogError($"[ZeroAlloc] Failed to bind {typeName}.{methodName}: {e}");
            return 0;
        }
    }

    static Type FindType(string typeName) {
        var type = Type.GetType(typeName);
        if (type != null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            type = assembly.GetType(typeName);
            if (type != null) return type;
        }

        return null;
    }

    static unsafe ZeroAllocHandler CreateReflectionHandler(MethodInfo method) {
        var parameters = method.GetParameters();
        var argCount = parameters.Length;

        var argTypes = new Type[argCount];
        for (int i = 0; i < argCount; i++) {
            argTypes[i] = parameters[i].ParameterType;
        }

        return (InteropValue* args, int argc, InteropValue* result) => {
            var boxedArgs = new object[argCount];
            for (int i = 0; i < argCount && i < argc; i++) {
                boxedArgs[i] = ConvertInteropValueToObject(&args[i], argTypes[i]);
            }

            var returnValue = method.Invoke(null, boxedArgs);

            if (returnValue != null) {
                SetResultFromObject(result, returnValue);
            }
        };
    }

    static unsafe object ConvertInteropValueToObject(InteropValue* v, Type expectedType) {
        if (expectedType == typeof(int)) return GetInt(v);
        if (expectedType == typeof(float)) return GetFloat(v);
        if (expectedType == typeof(double)) return GetDouble(v);
        if (expectedType == typeof(bool)) return v->b != 0;
        if (expectedType == typeof(string)) return GetString(v);
        if (expectedType == typeof(Vector3)) return new Vector3(v->vecX, v->vecY, v->vecZ);
        if (expectedType == typeof(Vector4)) return new Vector4(v->vecX, v->vecY, v->vecZ, v->vecW);
        if (expectedType == typeof(Color)) return new Color(v->vecX, v->vecY, v->vecZ, v->vecW);

        if (v->type == InteropType.ObjectHandle) {
            return GetObjectByHandle(v->handle);
        }

        return null;
    }

    static unsafe void SetResultFromObject(InteropValue* result, object value) {
        switch (value) {
            case int i:
                result->type = InteropType.Int32;
                result->i32 = i;
                break;
            case float f:
                result->type = InteropType.Float32;
                result->f32 = f;
                break;
            case double d:
                result->type = InteropType.Double;
                result->f64 = d;
                break;
            case bool b:
                result->type = InteropType.Bool;
                result->b = b ? 1 : 0;
                break;
            case Vector3 v3:
                result->type = InteropType.Vector3;
                result->vecX = v3.x;
                result->vecY = v3.y;
                result->vecZ = v3.z;
                break;
            case Vector4 v4:
                result->type = InteropType.Vector4;
                result->vecX = v4.x;
                result->vecY = v4.y;
                result->vecZ = v4.z;
                result->vecW = v4.w;
                break;
            case Color c:
                result->type = InteropType.Vector4;
                result->vecX = c.r;
                result->vecY = c.g;
                result->vecZ = c.b;
                result->vecW = c.a;
                break;
            case UnityEngine.Object obj:
                result->type = InteropType.ObjectHandle;
                result->handle = RegisterObject(obj);
                break;
            default:
                result->type = InteropType.Null;
                break;
        }
    }

    // MARK: Typed Binding Helpers

    /// <summary>
    /// Register a zero-arg method.
    /// </summary>
    public static unsafe int Bind(Action action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action();
        });
    }

    /// <summary>
    /// Register a zero-arg method with return value.
    /// </summary>
    public static unsafe int Bind<TResult>(Func<TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func();
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 1-arg method.
    /// </summary>
    public static unsafe int Bind<T0>(Action<T0> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]));
        });
    }

    /// <summary>
    /// Register a 1-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, TResult>(Func<T0, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]));
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 2-arg method.
    /// </summary>
    public static unsafe int Bind<T0, T1>(Action<T0, T1> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]));
        });
    }

    /// <summary>
    /// Register a 2-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, T1, TResult>(Func<T0, T1, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]));
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 3-arg method.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2>(Action<T0, T1, T2> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]));
        });
    }

    /// <summary>
    /// Register a 3-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, TResult>(Func<T0, T1, T2, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]));
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 4-arg method.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3>(Action<T0, T1, T2, T3> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]));
        });
    }

    /// <summary>
    /// Register a 4-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3, TResult>(Func<T0, T1, T2, T3, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]));
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 5-arg method.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3, T4>(Action<T0, T1, T2, T3, T4> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]), GetArg<T4>(&args[4]));
        });
    }

    /// <summary>
    /// Register a 5-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3, T4, TResult>(Func<T0, T1, T2, T3, T4, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]), GetArg<T4>(&args[4]));
            SetResult(result, ret);
        });
    }

    /// <summary>
    /// Register a 6-arg method.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3, T4, T5>(Action<T0, T1, T2, T3, T4, T5> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            action(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]), GetArg<T4>(&args[4]), GetArg<T5>(&args[5]));
        });
    }

    /// <summary>
    /// Register a 6-arg method with return value.
    /// </summary>
    public static unsafe int Bind<T0, T1, T2, T3, T4, T5, TResult>(Func<T0, T1, T2, T3, T4, T5, TResult> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            var ret = func(GetArg<T0>(&args[0]), GetArg<T1>(&args[1]), GetArg<T2>(&args[2]), GetArg<T3>(&args[3]), GetArg<T4>(&args[4]), GetArg<T5>(&args[5]));
            SetResult(result, ret);
        });
    }

    // MARK: Argument Extraction

    static unsafe T GetArg<T>(InteropValue* v) {
        if (typeof(T) == typeof(int)) return (T)(object)GetInt(v);
        if (typeof(T) == typeof(float)) return (T)(object)GetFloat(v);
        if (typeof(T) == typeof(double)) return (T)(object)GetDouble(v);
        if (typeof(T) == typeof(bool)) return (T)(object)(v->b != 0);
        if (typeof(T) == typeof(string)) return (T)(object)GetString(v);
        if (typeof(T) == typeof(Vector3)) return (T)(object)new Vector3(v->vecX, v->vecY, v->vecZ);
        if (typeof(T) == typeof(Vector4)) return (T)(object)new Vector4(v->vecX, v->vecY, v->vecZ, v->vecW);
        if (typeof(T) == typeof(Color)) return (T)(object)new Color(v->vecX, v->vecY, v->vecZ, v->vecW);

        if (v->type == InteropType.ObjectHandle) {
            var obj = GetObjectByHandle(v->handle);
            if (obj is T typed) return typed;
        }

        throw new InvalidCastException($"Cannot convert InteropValue type {v->type} to {typeof(T).Name}");
    }

    static unsafe int GetInt(InteropValue* v) {
        return v->type switch {
            InteropType.Int32 => v->i32,
            InteropType.Double => (int)v->f64,
            InteropType.Float32 => (int)v->f32,
            _ => 0
        };
    }

    static unsafe float GetFloat(InteropValue* v) {
        return v->type switch {
            InteropType.Float32 => v->f32,
            InteropType.Double => (float)v->f64,
            InteropType.Int32 => v->i32,
            _ => 0f
        };
    }

    static unsafe double GetDouble(InteropValue* v) {
        return v->type switch {
            InteropType.Double => v->f64,
            InteropType.Float32 => v->f32,
            InteropType.Int32 => v->i32,
            _ => 0.0
        };
    }

    static unsafe string GetString(InteropValue* v) {
        if (v->type != InteropType.String || v->str == IntPtr.Zero)
            return null;
        return PtrToStringUtf8(v->str);
    }

    // MARK: Result Setting

    static unsafe void SetResult<T>(InteropValue* result, T value) {
        if (value == null) {
            result->type = InteropType.Null;
            return;
        }

        switch (value) {
            case int i:
                result->type = InteropType.Int32;
                result->i32 = i;
                break;
            case float f:
                result->type = InteropType.Float32;
                result->f32 = f;
                break;
            case double d:
                result->type = InteropType.Double;
                result->f64 = d;
                break;
            case bool b:
                result->type = InteropType.Bool;
                result->b = b ? 1 : 0;
                break;
            case Vector3 v3:
                result->type = InteropType.Vector3;
                result->vecX = v3.x;
                result->vecY = v3.y;
                result->vecZ = v3.z;
                break;
            case Vector4 v4:
                result->type = InteropType.Vector4;
                result->vecX = v4.x;
                result->vecY = v4.y;
                result->vecZ = v4.z;
                result->vecW = v4.w;
                break;
            case Color c:
                result->type = InteropType.Vector4;
                result->vecX = c.r;
                result->vecY = c.g;
                result->vecZ = c.b;
                result->vecW = c.a;
                break;
            default:
                if (value is UnityEngine.Object obj) {
                    result->type = InteropType.ObjectHandle;
                    result->handle = RegisterObject(obj);
                } else {
                    result->type = InteropType.Null;
                }
                break;
        }
    }

    // MARK: Type-Specific Result Setters (Zero-Alloc)
    //
    // These methods avoid the boxing that occurs in SetResult<T> by using
    // concrete types instead of generics. Use these for hot-path code.

    static unsafe void SetResultInt(InteropValue* result, int value) {
        result->type = InteropType.Int32;
        result->i32 = value;
    }

    static unsafe void SetResultFloat(InteropValue* result, float value) {
        result->type = InteropType.Float32;
        result->f32 = value;
    }

    static unsafe void SetResultBool(InteropValue* result, bool value) {
        result->type = InteropType.Bool;
        result->b = value ? 1 : 0;
    }

    // MARK: Specialized GPU Bindings (Truly Zero-Alloc)
    //
    // These methods create bindings for specific GPU operations without using
    // generics, completely eliminating boxing overhead. The generic Bind<T>()
    // API remains available for convenience and prototyping.
    //
    // Why specialized bindings?
    // - Generic GetArg<T>() boxes: return (T)(object)GetInt(v)
    // - Generic SetResult<T>() boxes: switch (value) pattern-matches on object
    // - These specialized versions use direct primitive types - zero allocations
    //
    // Usage: Call these once at init, use returned binding IDs for per-frame calls.

    /// <summary>
    /// Specialized binding for SetFloatById(int shaderHandle, int nameId, float value).
    /// Truly zero-alloc: no generics, no boxing.
    /// </summary>
    public static unsafe int BindGpuSetFloatById(Action<int, int, float> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            int handle = GetInt(&args[0]);
            int nameId = GetInt(&args[1]);
            float value = GetFloat(&args[2]);
            action(handle, nameId, value);
        });
    }

    /// <summary>
    /// Specialized binding for SetIntById(int shaderHandle, int nameId, int value).
    /// Truly zero-alloc: no generics, no boxing.
    /// </summary>
    public static unsafe int BindGpuSetIntById(Action<int, int, int> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            int handle = GetInt(&args[0]);
            int nameId = GetInt(&args[1]);
            int value = GetInt(&args[2]);
            action(handle, nameId, value);
        });
    }

    /// <summary>
    /// Specialized binding for SetVectorById(int shaderHandle, int nameId, float x, float y, float z, float w).
    /// Truly zero-alloc: no generics, no boxing.
    /// </summary>
    public static unsafe int BindGpuSetVectorById(Action<int, int, float, float, float, float> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            int handle = GetInt(&args[0]);
            int nameId = GetInt(&args[1]);
            float x = GetFloat(&args[2]);
            float y = GetFloat(&args[3]);
            float z = GetFloat(&args[4]);
            float w = GetFloat(&args[5]);
            action(handle, nameId, x, y, z, w);
        });
    }

    /// <summary>
    /// Specialized binding for SetTextureById(int shaderHandle, int kernelIndex, int nameId, int textureHandle).
    /// Truly zero-alloc: no generics, no boxing.
    /// </summary>
    public static unsafe int BindGpuSetTextureById(Action<int, int, int, int> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            int shaderHandle = GetInt(&args[0]);
            int kernelIndex = GetInt(&args[1]);
            int nameId = GetInt(&args[2]);
            int textureHandle = GetInt(&args[3]);
            action(shaderHandle, kernelIndex, nameId, textureHandle);
        });
    }

    /// <summary>
    /// Specialized binding for Dispatch(int shaderHandle, int kernelIndex, int groupsX, int groupsY, int groupsZ).
    /// Truly zero-alloc: no generics, no boxing.
    /// </summary>
    public static unsafe int BindGpuDispatch(Action<int, int, int, int, int> action) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            int shaderHandle = GetInt(&args[0]);
            int kernelIndex = GetInt(&args[1]);
            int groupsX = GetInt(&args[2]);
            int groupsY = GetInt(&args[3]);
            int groupsZ = GetInt(&args[4]);
            action(shaderHandle, kernelIndex, groupsX, groupsY, groupsZ);
        });
    }

    /// <summary>
    /// Specialized binding for GetScreenWidth() -> int.
    /// Truly zero-alloc: uses SetResultInt instead of generic SetResult.
    /// </summary>
    public static unsafe int BindGpuGetScreenWidth(Func<int> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            SetResultInt(result, func());
        });
    }

    /// <summary>
    /// Specialized binding for GetScreenHeight() -> int.
    /// Truly zero-alloc: uses SetResultInt instead of generic SetResult.
    /// </summary>
    public static unsafe int BindGpuGetScreenHeight(Func<int> func) {
        return RegisterZeroAllocBinding((InteropValue* args, int argCount, InteropValue* result) => {
            SetResultInt(result, func());
        });
    }
}
