using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Fast Path Key
    readonly struct FastPathKey : IEquatable<FastPathKey> {
        readonly int _typeHash;
        readonly string _memberName;
        readonly InteropInvokeCallKind _callKind;
        readonly bool _isStatic;

        public FastPathKey(Type type, string memberName, InteropInvokeCallKind callKind, bool isStatic) {
            _typeHash = type.GetHashCode();
            _memberName = memberName;
            _callKind = callKind;
            _isStatic = isStatic;
        }

        public bool Equals(FastPathKey other) =>
            _typeHash == other._typeHash &&
            _memberName == other._memberName &&
            _callKind == other._callKind &&
            _isStatic == other._isStatic;

        public override bool Equals(object obj) => obj is FastPathKey k && Equals(k);

        public override int GetHashCode() =>
            HashCode.Combine(_typeHash, _memberName, (int)_callKind, _isStatic);
    }

    // MARK: Delegate Types
    // These operate directly on InteropValue pointers - no object[] allocation
    unsafe delegate void FastInstanceHandler(object target, InteropValue* args, int argCount,
        InteropValue* result);

    unsafe delegate void FastStaticHandler(InteropValue* args, int argCount, InteropValue* result);

    // MARK: Registry
    static readonly Dictionary<FastPathKey, Delegate> _fastPathRegistry = new();
    static bool _fastPathInitialized;

    // MARK: Public API
    public static class FastPath {
        /// <summary>
        /// Register an instance property (getter and/or setter).
        /// </summary>
        public static void Property<TTarget, TValue>(string name,
            Func<TTarget, TValue> getter,
            Action<TTarget, TValue> setter = null) where TTarget : class {
            var type = typeof(TTarget);

            if (getter != null) {
                var key = new FastPathKey(type, name, InteropInvokeCallKind.GetProp, false);
                unsafe {
                    _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                        var value = getter((TTarget)target);
                        WriteToInterop(value, result);
                    });
                }
            }

            if (setter != null) {
                var key = new FastPathKey(type, name, InteropInvokeCallKind.SetProp, false);
                unsafe {
                    _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                        var value = ReadFromInterop<TValue>(args);
                        setter((TTarget)target, value);
                    });
                }
            }
        }

        /// <summary>
        /// Register a static property (getter and/or setter).
        /// </summary>
        public static void StaticProperty<TOwner, TValue>(string name,
            Func<TValue> getter,
            Action<TValue> setter = null) {
            var type = typeof(TOwner);

            if (getter != null) {
                var key = new FastPathKey(type, name, InteropInvokeCallKind.GetProp, true);
                unsafe {
                    _fastPathRegistry[key] = new FastStaticHandler((args, argCount, result) => {
                        var value = getter();
                        WriteToInterop(value, result);
                    });
                }
            }

            if (setter != null) {
                var key = new FastPathKey(type, name, InteropInvokeCallKind.SetProp, true);
                unsafe {
                    _fastPathRegistry[key] = new FastStaticHandler((args, argCount, result) => {
                        var value = ReadFromInterop<TValue>(args);
                        setter(value);
                    });
                }
            }
        }

        /// <summary>
        /// Register an instance method with no arguments.
        /// </summary>
        public static void Method<TTarget>(string name, Action<TTarget> method) where TTarget : class {
            var type = typeof(TTarget);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, false);
            unsafe {
                _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                    method((TTarget)target);
                    result->type = InteropType.Null;
                });
            }
        }

        /// <summary>
        /// Register an instance method with no arguments, returning a value.
        /// </summary>
        public static void Method<TTarget, TResult>(string name, Func<TTarget, TResult> method)
            where TTarget : class {
            var type = typeof(TTarget);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, false);
            unsafe {
                _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                    var ret = method((TTarget)target);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register an instance method with 1 argument.
        /// </summary>
        public static void Method<TTarget, TArg0, TResult>(string name, Func<TTarget, TArg0, TResult> method)
            where TTarget : class {
            var type = typeof(TTarget);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, false);
            unsafe {
                _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var ret = method((TTarget)target, arg0);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register an instance method with 1 argument, no return.
        /// </summary>
        public static void Method<TTarget, TArg0>(string name, Action<TTarget, TArg0> method)
            where TTarget : class {
            var type = typeof(TTarget);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, false);
            unsafe {
                _fastPathRegistry[key] = new FastInstanceHandler((target, args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    method((TTarget)target, arg0);
                    result->type = InteropType.Null;
                });
            }
        }

        /// <summary>
        /// Register a static method with no arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TResult>(string name, Func<TResult> method) {
            var type = typeof(TOwner);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistry[key] = new FastStaticHandler((args, argCount, result) => {
                    var ret = method();
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 1 argument.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TResult>(string name, Func<TArg0, TResult> method) {
            var type = typeof(TOwner);
            var key = new FastPathKey(type, name, InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistry[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var ret = method(arg0);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Clear all fast path registrations.
        /// </summary>
        public static void Clear() {
            _fastPathRegistry.Clear();
            _fastPathInitialized = false;
        }

        /// <summary>
        /// Number of registered fast paths.
        /// </summary>
        public static int Count => _fastPathRegistry.Count;
    }

    // MARK: Init
    static void EnsureFastPathInitialized() {
        if (_fastPathInitialized) return;
        _fastPathInitialized = true;

        // Register commonly used Unity properties for zero-alloc access
        // These are the hot paths that benefit most from optimization

        // Time - accessed every frame
        FastPath.StaticProperty<Time, float>("deltaTime", () => Time.deltaTime);
        FastPath.StaticProperty<Time, float>("unscaledDeltaTime", () => Time.unscaledDeltaTime);
        FastPath.StaticProperty<Time, float>("time", () => Time.time);
        FastPath.StaticProperty<Time, float>("unscaledTime", () => Time.unscaledTime);
        FastPath.StaticProperty<Time, float>("fixedDeltaTime", () => Time.fixedDeltaTime);
        FastPath.StaticProperty<Time, float>("timeScale", () => Time.timeScale, v => Time.timeScale = v);
        FastPath.StaticProperty<Time, int>("frameCount", () => Time.frameCount);

        // Transform - most common component
        FastPath.Property<Transform, Vector3>("position", t => t.position, (t, v) => t.position = v);
        FastPath.Property<Transform, Vector3>("localPosition", t => t.localPosition,
            (t, v) => t.localPosition = v);
        FastPath.Property<Transform, Quaternion>("rotation", t => t.rotation, (t, v) => t.rotation = v);
        FastPath.Property<Transform, Quaternion>("localRotation", t => t.localRotation,
            (t, v) => t.localRotation = v);
        FastPath.Property<Transform, Vector3>("localScale", t => t.localScale, (t, v) => t.localScale = v);
        FastPath.Property<Transform, Vector3>("eulerAngles", t => t.eulerAngles, (t, v) => t.eulerAngles = v);
        FastPath.Property<Transform, Vector3>("localEulerAngles", t => t.localEulerAngles,
            (t, v) => t.localEulerAngles = v);
        FastPath.Property<Transform, Vector3>("forward", t => t.forward, (t, v) => t.forward = v);
        FastPath.Property<Transform, Vector3>("right", t => t.right, (t, v) => t.right = v);
        FastPath.Property<Transform, Vector3>("up", t => t.up, (t, v) => t.up = v);

        // Transform methods
        FastPath.Method<Transform, Vector3, Vector3>("TransformPoint", (t, v) => t.TransformPoint(v));
        FastPath.Method<Transform, Vector3, Vector3>("InverseTransformPoint",
            (t, v) => t.InverseTransformPoint(v));
        FastPath.Method<Transform, Vector3, Vector3>("TransformDirection", (t, v) => t.TransformDirection(v));
        FastPath.Method<Transform, Vector3, Vector3>("InverseTransformDirection",
            (t, v) => t.InverseTransformDirection(v));

        // GameObject basics
        FastPath.Property<GameObject, bool>("activeSelf", g => g.activeSelf);
        FastPath.Property<GameObject, bool>("activeInHierarchy", g => g.activeInHierarchy);
        FastPath.Property<GameObject, string>("name", g => g.name, (g, v) => g.name = v);
        FastPath.Property<GameObject, string>("tag", g => g.tag, (g, v) => g.tag = v);
        FastPath.Property<GameObject, int>("layer", g => g.layer, (g, v) => g.layer = v);
        FastPath.Method<GameObject, bool>("SetActive", (g, v) => g.SetActive(v));

        // Input (legacy) - only register if legacy input is enabled
        // Check at runtime to avoid errors when Input System package is active
#if ENABLE_LEGACY_INPUT_MANAGER
        FastPath.StaticProperty<Input, Vector3>("mousePosition", () => Input.mousePosition);
        FastPath.StaticProperty<Input, bool>("anyKey", () => Input.anyKey);
        FastPath.StaticProperty<Input, bool>("anyKeyDown", () => Input.anyKeyDown);
        FastPath.StaticMethod<Input, string, bool>("GetKey", Input.GetKey);
        FastPath.StaticMethod<Input, string, bool>("GetKeyDown", Input.GetKeyDown);
        FastPath.StaticMethod<Input, string, bool>("GetKeyUp", Input.GetKeyUp);
        FastPath.StaticMethod<Input, int, bool>("GetMouseButton", Input.GetMouseButton);
        FastPath.StaticMethod<Input, int, bool>("GetMouseButtonDown", Input.GetMouseButtonDown);
        FastPath.StaticMethod<Input, int, bool>("GetMouseButtonUp", Input.GetMouseButtonUp);
        FastPath.StaticMethod<Input, string, float>("GetAxis", Input.GetAxis);
        FastPath.StaticMethod<Input, string, float>("GetAxisRaw", Input.GetAxisRaw);
#endif

        // Screen
        FastPath.StaticProperty<Screen, int>("width", () => Screen.width);
        FastPath.StaticProperty<Screen, int>("height", () => Screen.height);
        FastPath.StaticProperty<Screen, float>("dpi", () => Screen.dpi);

        // Mathf common operations
        FastPath.StaticMethod<Mathf, float, float>("Abs", Mathf.Abs);
        FastPath.StaticMethod<Mathf, float, float>("Sqrt", Mathf.Sqrt);
        FastPath.StaticMethod<Mathf, float, float>("Sin", Mathf.Sin);
        FastPath.StaticMethod<Mathf, float, float>("Cos", Mathf.Cos);
        FastPath.StaticMethod<Mathf, float, float>("Floor", Mathf.Floor);
        FastPath.StaticMethod<Mathf, float, float>("Ceil", Mathf.Ceil);
        FastPath.StaticMethod<Mathf, float, float>("Round", Mathf.Round);
    }

    // MARK: Read/Write
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void WriteToInterop<T>(T value, InteropValue* result) {
        result->type = InteropType.Null;
        result->typeHint = IntPtr.Zero;

        // Primitives - use UnsafeUtility to avoid boxing
        // JIT eliminates dead branches when T is known at compile time
        if (typeof(T) == typeof(float)) {
            result->type = InteropType.Float32;
            result->f32 = UnsafeUtility.As<T, float>(ref value);
            return;
        }
        if (typeof(T) == typeof(int)) {
            result->type = InteropType.Int32;
            result->i32 = UnsafeUtility.As<T, int>(ref value);
            return;
        }
        if (typeof(T) == typeof(double)) {
            result->type = InteropType.Double;
            result->f64 = UnsafeUtility.As<T, double>(ref value);
            return;
        }
        if (typeof(T) == typeof(bool)) {
            result->type = InteropType.Bool;
            result->b = UnsafeUtility.As<T, bool>(ref value) ? 1 : 0;
            return;
        }
        if (typeof(T) == typeof(long)) {
            result->type = InteropType.Int64;
            result->i64 = UnsafeUtility.As<T, long>(ref value);
            return;
        }

        // Null check for reference types
        if (value == null) return;

        if (typeof(T) == typeof(string)) {
            result->type = InteropType.String;
            result->str = StringToUtf8(UnsafeUtility.As<T, string>(ref value));
            return;
        }

        var type = typeof(T);

        // Structs - serialize (still allocates for JSON, but that's expected)
        if (type.IsValueType) {
            var json = SerializeStruct(value);
            if (json != null) {
                result->type = InteropType.String;
                result->str = StringToUtf8(json);
                return;
            }
        }

        // Reference type - register handle
        var handle = RegisterObject(value);
        result->type = InteropType.ObjectHandle;
        result->handle = handle;
        result->typeHint = StringToUtf8(type.FullName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe T ReadFromInterop<T>(InteropValue* v) {
        // Primitives - direct reads with type reinterpretation
        if (typeof(T) == typeof(float)) {
            float f = v->type switch {
                InteropType.Float32 => v->f32,
                InteropType.Double => (float)v->f64,
                InteropType.Int32 => v->i32,
                _ => 0f
            };
            return UnsafeUtility.As<float, T>(ref f);
        }
        if (typeof(T) == typeof(int)) {
            int i = v->type switch {
                InteropType.Int32 => v->i32,
                InteropType.Double => (int)v->f64,
                InteropType.Float32 => (int)v->f32,
                _ => 0
            };
            return UnsafeUtility.As<int, T>(ref i);
        }
        if (typeof(T) == typeof(double)) {
            double d = v->type switch {
                InteropType.Double => v->f64,
                InteropType.Float32 => v->f32,
                InteropType.Int32 => v->i32,
                _ => 0.0
            };
            return UnsafeUtility.As<double, T>(ref d);
        }
        if (typeof(T) == typeof(bool)) {
            bool b = v->b != 0;
            return UnsafeUtility.As<bool, T>(ref b);
        }
        if (typeof(T) == typeof(long)) {
            long l = v->type switch {
                InteropType.Int64 => v->i64,
                InteropType.Int32 => v->i32,
                InteropType.Double => (long)v->f64,
                _ => 0L
            };
            return UnsafeUtility.As<long, T>(ref l);
        }
        if (typeof(T) == typeof(string)) {
            if (v->type == InteropType.String) {
                string s = PtrToStringUtf8(v->str);
                return UnsafeUtility.As<string, T>(ref s);
            }
            return default;
        }

        // Object handle
        if (v->type == InteropType.ObjectHandle) {
            var obj = GetObjectByHandle(v->handle);
            if (obj is T t) return t;
        }

        var type = typeof(T);

        // Struct from JSON
        if (type.IsValueType && !type.IsPrimitive) {
            if (v->type == InteropType.String || v->type == InteropType.JsonObject) {
                var json = PtrToStringUtf8(v->str);
                if (!string.IsNullOrEmpty(json)) {
                    var dict = ParseSimpleJson(json);
                    if (dict != null) {
                        var deserialized = DeserializeFromDict(dict, type);
                        if (deserialized is T result) return result;
                    }
                }
            }
        }

        return default;
    }

    // MARK: Dispatch Hook
    /// <summary>
    /// Try to handle the call via fast path. Returns true if handled.
    /// </summary>
    static unsafe bool TryFastPath(Type type, string memberName, InteropInvokeCallKind callKind,
        bool isStatic, object target, InteropValue* args, int argCount, InteropValue* result) {
        EnsureFastPathInitialized();

        if (type == null) return false;

        var key = new FastPathKey(type, memberName, callKind, isStatic);
        if (!_fastPathRegistry.TryGetValue(key, out var handler)) return false;

        if (isStatic) {
            ((FastStaticHandler)handler)(args, argCount, result);
        } else {
            ((FastInstanceHandler)handler)(target, args, argCount, result);
        }

        return true;
    }
}