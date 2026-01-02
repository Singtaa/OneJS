using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using OneJS.GPU;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Hash Utils
    /// <summary>
    /// Compute hash from UTF8 string pointer WITHOUT allocating a managed string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe int HashUtf8Ptr(IntPtr ptr) {
        if (ptr == IntPtr.Zero) return 0;
        byte* p = (byte*)ptr;
        int hash = 17;
        while (*p != 0) {
            hash = hash * 31 + *p;
            p++;
        }
        return hash;
    }

    /// <summary>
    /// Compute hash from managed string (used at registration time).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int HashString(string s) {
        if (string.IsNullOrEmpty(s)) return 0;
        int hash = 17;
        for (int i = 0; i < s.Length; i++) {
            // Only hash ASCII portion to match UTF8 bytes
            hash = hash * 31 + (s[i] & 0x7F);
        }
        return hash;
    }

    // MARK: Delegate Types
    unsafe delegate void FastInstanceHandler(object target, InteropValue* args, int argCount,
        InteropValue* result);

    unsafe delegate void FastStaticHandler(InteropValue* args, int argCount, InteropValue* result);

    // MARK: Struct Handler Delegates
    unsafe delegate void StructPackerRaw(object value, InteropValue* result);
    unsafe delegate object StructUnpackerRaw(InteropValue* source);
    delegate object DictConverterRaw(Dictionary<string, object> dict);

    public unsafe delegate void StructPacker<T>(in T value, InteropValue* result) where T : struct;
    public unsafe delegate void StructUnpacker<T>(InteropValue* source, out T value) where T : struct;
    public delegate T DictConverter<T>(Dictionary<string, object> dict) where T : struct;

    // MARK: Registry
    static readonly Dictionary<Type, Delegate> _structPackers = new();
    static readonly Dictionary<Type, Delegate> _structUnpackers = new();
    static readonly Dictionary<Type, Delegate> _dictConverters = new();

    // Static type hash cache - maps hash of type name to Type
    static readonly Dictionary<int, Type> _staticTypeHashCache = new();

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
            var typeHash = type.GetHashCode();
            var memberHash = HashString(name);

            if (getter != null) {
                var key = MakeFastPathKey(typeHash, memberHash, InteropInvokeCallKind.GetProp, false);
                unsafe {
                    _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
                        var value = getter((TTarget)target);
                        WriteToInterop(value, result);
                    });
                }
            }

            if (setter != null) {
                var key = MakeFastPathKey(typeHash, memberHash, InteropInvokeCallKind.SetProp, false);
                unsafe {
                    _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
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
            CacheStaticTypeHash(type);
            var typeHash = type.GetHashCode();
            var memberHash = HashString(name);

            if (getter != null) {
                var key = MakeFastPathKey(typeHash, memberHash, InteropInvokeCallKind.GetProp, true);
                unsafe {
                    _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                        var value = getter();
                        WriteToInterop(value, result);
                    });
                }
            }

            if (setter != null) {
                var key = MakeFastPathKey(typeHash, memberHash, InteropInvokeCallKind.SetProp, true);
                unsafe {
                    _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
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
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                false);
            unsafe {
                _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
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
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                false);
            unsafe {
                _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
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
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                false);
            unsafe {
                _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
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
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                false);
            unsafe {
                _fastPathRegistryLong[key] = new FastInstanceHandler((target, args, argCount, result) => {
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
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
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
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var ret = method(arg0);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 2 arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TArg1, TResult>(string name, Func<TArg0, TArg1, TResult> method) {
            var type = typeof(TOwner);
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var ret = method(arg0, arg1);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 3 arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TArg1, TArg2, TResult>(string name, Func<TArg0, TArg1, TArg2, TResult> method) {
            var type = typeof(TOwner);
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var ret = method(arg0, arg1, arg2);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 4 arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TArg1, TArg2, TArg3, TResult>(string name, Func<TArg0, TArg1, TArg2, TArg3, TResult> method) {
            var type = typeof(TOwner);
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var ret = method(arg0, arg1, arg2, arg3);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 5 arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TArg1, TArg2, TArg3, TArg4, TResult>(string name, Func<TArg0, TArg1, TArg2, TArg3, TArg4, TResult> method) {
            var type = typeof(TOwner);
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var arg4 = ReadFromInterop<TArg4>(&args[4]);
                    var ret = method(arg0, arg1, arg2, arg3, arg4);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 6 arguments.
        /// </summary>
        public static void StaticMethod<TOwner, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TResult>(string name, Func<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TResult> method) {
            var type = typeof(TOwner);
            CacheStaticTypeHash(type);
            var key = MakeFastPathKey(type.GetHashCode(), HashString(name), InteropInvokeCallKind.Method,
                true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var arg4 = ReadFromInterop<TArg4>(&args[4]);
                    var arg5 = ReadFromInterop<TArg5>(&args[5]);
                    var ret = method(arg0, arg1, arg2, arg3, arg4, arg5);
                    WriteToInterop(ret, result);
                });
            }
        }

        // MARK: Static methods by type name (for static classes that can't be type arguments)

        /// <summary>
        /// Register a static method with 2 arguments using explicit type name (for static classes).
        /// </summary>
        public static void StaticMethodByTypeName<TArg0, TArg1, TResult>(string typeName, string methodName, Func<TArg0, TArg1, TResult> method) {
            int typeNameHash = HashString(typeName);
            CacheStaticTypeHashByName(typeName, typeNameHash);
            var key = MakeFastPathKey(typeNameHash, HashString(methodName), InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var ret = method(arg0, arg1);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 3 arguments using explicit type name (for static classes).
        /// </summary>
        public static void StaticMethodByTypeName<TArg0, TArg1, TArg2, TResult>(string typeName, string methodName, Func<TArg0, TArg1, TArg2, TResult> method) {
            int typeNameHash = HashString(typeName);
            CacheStaticTypeHashByName(typeName, typeNameHash);
            var key = MakeFastPathKey(typeNameHash, HashString(methodName), InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var ret = method(arg0, arg1, arg2);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 4 arguments using explicit type name (for static classes).
        /// </summary>
        public static void StaticMethodByTypeName<TArg0, TArg1, TArg2, TArg3, TResult>(string typeName, string methodName, Func<TArg0, TArg1, TArg2, TArg3, TResult> method) {
            int typeNameHash = HashString(typeName);
            CacheStaticTypeHashByName(typeName, typeNameHash);
            var key = MakeFastPathKey(typeNameHash, HashString(methodName), InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var ret = method(arg0, arg1, arg2, arg3);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 5 arguments using explicit type name (for static classes).
        /// </summary>
        public static void StaticMethodByTypeName<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>(string typeName, string methodName, Func<TArg0, TArg1, TArg2, TArg3, TArg4, TResult> method) {
            int typeNameHash = HashString(typeName);
            CacheStaticTypeHashByName(typeName, typeNameHash);
            var key = MakeFastPathKey(typeNameHash, HashString(methodName), InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var arg4 = ReadFromInterop<TArg4>(&args[4]);
                    var ret = method(arg0, arg1, arg2, arg3, arg4);
                    WriteToInterop(ret, result);
                });
            }
        }

        /// <summary>
        /// Register a static method with 6 arguments using explicit type name (for static classes).
        /// </summary>
        public static void StaticMethodByTypeName<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TResult>(string typeName, string methodName, Func<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TResult> method) {
            int typeNameHash = HashString(typeName);
            CacheStaticTypeHashByName(typeName, typeNameHash);
            var key = MakeFastPathKey(typeNameHash, HashString(methodName), InteropInvokeCallKind.Method, true);
            unsafe {
                _fastPathRegistryLong[key] = new FastStaticHandler((args, argCount, result) => {
                    var arg0 = ReadFromInterop<TArg0>(&args[0]);
                    var arg1 = ReadFromInterop<TArg1>(&args[1]);
                    var arg2 = ReadFromInterop<TArg2>(&args[2]);
                    var arg3 = ReadFromInterop<TArg3>(&args[3]);
                    var arg4 = ReadFromInterop<TArg4>(&args[4]);
                    var arg5 = ReadFromInterop<TArg5>(&args[5]);
                    var ret = method(arg0, arg1, arg2, arg3, arg4, arg5);
                    WriteToInterop(ret, result);
                });
            }
        }

        // MARK: Struct Registration
        public static unsafe void Struct<T>(
            StructPacker<T> pack,
            StructUnpacker<T> unpack = null,
            DictConverter<T> fromDict = null
        ) where T : struct {
            var type = typeof(T);

            if (pack != null) {
                _structPackers[type] = new StructPackerRaw((obj, result) => {
                    var typed = (T)obj;
                    pack(in typed, result);
                });
            }
            if (unpack != null) {
                _structUnpackers[type] = new StructUnpackerRaw((source) => {
                    unpack(source, out var val);
                    return val;
                });
            }
            if (fromDict != null) {
                _dictConverters[type] = new DictConverterRaw(dict => fromDict(dict));
            }
        }

        public static void BlittableStruct<T>() where T : struct {
            var type = typeof(T);
            var fields = GetNumericFields(type);

            if (fields.Length == 0) {
                Debug.LogWarning($"[QuickJS] BlittableStruct<{type.Name}>: No numeric fields found");
                return;
            }
            if (fields.Length > 4) {
                Debug.LogWarning($"[QuickJS] BlittableStruct<{type.Name}>: Max 4 fields, using first 4.");
                fields = fields[..4];
            }

            var camelNames = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                camelNames[i] = char.ToLowerInvariant(fields[i].Name[0]) + fields[i].Name[1..];

            RegisterBlittablePacker(type, fields);
            RegisterBlittableUnpacker(type, fields);
            RegisterBlittableDictConverter(type, fields, camelNames);
        }

        static FieldInfo[] GetNumericFields(Type type) {
            var allFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var numeric = new List<FieldInfo>();
            foreach (var f in allFields) {
                if (f.FieldType == typeof(float) || f.FieldType == typeof(int) ||
                    f.FieldType == typeof(double) || f.FieldType == typeof(bool))
                    numeric.Add(f);
            }
            return numeric.ToArray();
        }

        static unsafe void RegisterBlittablePacker(Type type, FieldInfo[] fields) {
            _structPackers[type] = new StructPackerRaw((obj, result) => {
                result->type = fields.Length <= 3 ? InteropType.Vector3 : InteropType.Vector4;
                result->vecX = fields.Length > 0 ? Convert.ToSingle(fields[0].GetValue(obj)) : 0;
                result->vecY = fields.Length > 1 ? Convert.ToSingle(fields[1].GetValue(obj)) : 0;
                result->vecZ = fields.Length > 2 ? Convert.ToSingle(fields[2].GetValue(obj)) : 0;
                result->vecW = fields.Length > 3 ? Convert.ToSingle(fields[3].GetValue(obj)) : 0;
            });
        }

        static unsafe void RegisterBlittableUnpacker(Type type, FieldInfo[] fields) {
            float[] vecComponents = null; // Captured for lambda
            _structUnpackers[type] = new StructUnpackerRaw((source) => {
                vecComponents ??= new float[4];
                vecComponents[0] = source->vecX;
                vecComponents[1] = source->vecY;
                vecComponents[2] = source->vecZ;
                vecComponents[3] = source->vecW;

                object boxed = Activator.CreateInstance(type);
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetValue(boxed, ConvertToFieldType(vecComponents[i], fields[i].FieldType));
                return boxed;
            });
        }

        static void RegisterBlittableDictConverter(Type type, FieldInfo[] fields, string[] camelNames) {
            _dictConverters[type] = new DictConverterRaw((dict) => {
                object boxed = Activator.CreateInstance(type);
                for (int i = 0; i < fields.Length; i++) {
                    if (dict.TryGetValue(camelNames[i], out var val) ||
                        dict.TryGetValue(fields[i].Name, out val))
                        fields[i].SetValue(boxed, ConvertToFieldType(val, fields[i].FieldType));
                }
                return boxed;
            });
        }

        static object ConvertToFieldType(object value, Type fieldType) =>
            fieldType == typeof(float) ? Convert.ToSingle(value) :
            fieldType == typeof(int) ? Convert.ToInt32(value) :
            fieldType == typeof(double) ? Convert.ToDouble(value) :
            fieldType == typeof(bool) ? Convert.ToBoolean(value) : value;

        public static bool HasStructHandler<T>() where T : struct => _structPackers.ContainsKey(typeof(T));
        public static bool HasStructHandler(Type type) => _structPackers.ContainsKey(type);

        public static void Clear() {
            _fastPathRegistryLong.Clear();
            _structPackers.Clear();
            _structUnpackers.Clear();
            _dictConverters.Clear();
            _staticTypeHashCache.Clear();
            _staticClassTypeNameHashes.Clear();
            _fastPathInitialized = false;
        }

        public static int Count => _fastPathRegistryLong.Count;
        public static int StructCount => _structPackers.Count;
    }

    // MARK: Static Type Cache
    static void CacheStaticTypeHash(Type type) {
        // Cache mapping from typeName hash -> Type for static lookups
        var fullName = type.FullName;
        var hash = HashString(fullName);
        _staticTypeHashCache[hash] = type;
    }

    // Registry for static class type name hashes (for StaticMethodByTypeName)
    // Maps typeNameHash -> typeNameHash (identity mapping, to signal we should use hash directly as typeHash)
    static readonly HashSet<int> _staticClassTypeNameHashes = new HashSet<int>();

    /// <summary>
    /// Register a type name for static method lookup (for static classes that can't be Type).
    /// This marks the hash so TryFastPathZeroAlloc uses it directly as the typeHash.
    /// </summary>
    static void CacheStaticTypeHashByName(string typeName, int typeNameHash) {
        _staticClassTypeNameHashes.Add(typeNameHash);
        // Also add to cache so lookup doesn't fail early
        if (!_staticTypeHashCache.ContainsKey(typeNameHash)) {
            _staticTypeHashCache[typeNameHash] = typeof(object);
        }
    }

    // MARK: Init
    static void EnsureFastPathInitialized() {
        if (_fastPathInitialized) return;
        _fastPathInitialized = true;

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
        FastPath.Property<GameObject, Transform>("transform", g => g.transform);

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

        // GPU - zero-alloc compute shader dispatch
        // Uses StaticMethodByTypeName since GPUBridge is a static class (can't be type argument)
        const string gpuBridgeTypeName = "OneJS.GPU.GPUBridge";
        FastPath.StaticMethodByTypeName<int, string, int>(gpuBridgeTypeName, "FindKernel", GPUBridge.FindKernel);
        FastPath.StaticMethodByTypeName<int, string, float, bool>(gpuBridgeTypeName, "SetFloat", (h, n, v) => {
            GPUBridge.SetFloat(h, n, v);
            return true;
        });
        FastPath.StaticMethodByTypeName<int, string, int, bool>(gpuBridgeTypeName, "SetInt", (h, n, v) => {
            GPUBridge.SetInt(h, n, v);
            return true;
        });
        FastPath.StaticMethodByTypeName<int, string, bool, bool>(gpuBridgeTypeName, "SetBool", (h, n, v) => {
            GPUBridge.SetBool(h, n, v);
            return true;
        });
        FastPath.StaticMethodByTypeName<int, string, float, float, float, float, bool>(gpuBridgeTypeName, "SetVector", (h, n, x, y, z, w) => {
            GPUBridge.SetVector(h, n, x, y, z, w);
            return true;
        });
        FastPath.StaticMethodByTypeName<int, int, string, int, bool>(gpuBridgeTypeName, "SetTexture", (sh, ki, n, th) => {
            GPUBridge.SetTexture(sh, ki, n, th);
            return true;
        });
        FastPath.StaticMethodByTypeName<int, int, int, int, int, bool>(gpuBridgeTypeName, "Dispatch", (h, k, x, y, z) => {
            GPUBridge.Dispatch(h, k, x, y, z);
            return true;
        });
    }

    // MARK: Zero-Alloc Lookup

    // Use long key instead of struct to avoid potential boxing in Dictionary
    static readonly Dictionary<long, Delegate> _fastPathRegistryLong = new();

    static long MakeFastPathKey(int typeHash, int memberHash, InteropInvokeCallKind callKind, bool isStatic) {
        // Pack into 64 bits: typeHash(32) | memberHash(24) | callKind(4) | isStatic(1)
        return ((long)typeHash << 32) |
               ((long)(memberHash & 0xFFFFFF) << 8) |
               ((long)callKind << 1) |
               (isStatic ? 1L : 0L);
    }

    // DEBUG: Set to true to see fast path hit/miss logging
    public static bool DebugFastPath = false;

    /// <summary>
    /// Try fast path lookup using raw pointers - ZERO ALLOCATION.
    /// </summary>
    static unsafe bool TryFastPathZeroAlloc(
        Type typeFromHandle, // Already resolved from handle (for instance calls)
        IntPtr typeNamePtr, // Raw pointer (for static calls)
        IntPtr memberNamePtr, // Raw pointer
        InteropInvokeCallKind callKind,
        bool isStatic,
        object target,
        InteropValue* args,
        int argCount,
        InteropValue* result) {
        EnsureFastPathInitialized();

        // Compute member hash from raw UTF8 bytes
        int memberHash = HashUtf8Ptr(memberNamePtr);

        int typeHash;
        if (isStatic) {
            // For static calls, compute type hash from typeName pointer
            int typeNameHash = HashUtf8Ptr(typeNamePtr);

            // Check if this is a registered static class type name (use hash directly)
            if (_staticClassTypeNameHashes.Contains(typeNameHash)) {
                typeHash = typeNameHash;
            } else {
                // Regular type: lookup cached type and use its GetHashCode()
                if (!_staticTypeHashCache.TryGetValue(typeNameHash, out var cachedType)) {
                    if (DebugFastPath) {
                        var tn = PtrToStringUtf8(typeNamePtr);
                        Debug.Log($"[FastPath MISS] Static type not cached: {tn}, hash={typeNameHash}");
                    }
                    return false; // Not a registered static path
                }
                typeHash = cachedType.GetHashCode();
            }
        } else {
            // For instance calls, use the Type directly
            if (typeFromHandle == null) return false;
            typeHash = typeFromHandle.GetHashCode();
        }

        var key = MakeFastPathKey(typeHash, memberHash, callKind, isStatic);
        if (!_fastPathRegistryLong.TryGetValue(key, out var handler)) {
            if (DebugFastPath) {
                var mn = PtrToStringUtf8(memberNamePtr);
                Debug.Log(
                    $"[FastPath MISS] Key not found: member={mn}, memberHash={memberHash}, typeHash={typeHash}, kind={callKind}, static={isStatic}");
            }
            return false;
        }

        if (DebugFastPath) {
            Debug.Log($"[FastPath HIT] typeHash={typeHash}, memberHash={memberHash}");
        }

        if (isStatic) {
            ((FastStaticHandler)handler)(args, argCount, result);
        } else {
            ((FastInstanceHandler)handler)(target, args, argCount, result);
        }

        return true;
    }

    // MARK: Read/Write
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void WriteToInterop<T>(T value, InteropValue* result) {
        result->type = InteropType.Null;
        result->typeHint = IntPtr.Zero;

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

        if (typeof(T) == typeof(Vector3)) {
            var vec = UnsafeUtility.As<T, Vector3>(ref value);
            result->type = InteropType.Vector3;
            result->vecX = vec.x;
            result->vecY = vec.y;
            result->vecZ = vec.z;
            return;
        }

        if (typeof(T) == typeof(Quaternion)) {
            var quat = UnsafeUtility.As<T, Quaternion>(ref value);
            result->type = InteropType.Vector4;
            result->vecX = quat.x;
            result->vecY = quat.y;
            result->vecZ = quat.z;
            result->vecW = quat.w;
            return;
        }

        if (typeof(T) == typeof(Color)) {
            var col = UnsafeUtility.As<T, Color>(ref value);
            result->type = InteropType.Vector4;
            result->vecX = col.r;
            result->vecY = col.g;
            result->vecZ = col.b;
            result->vecW = col.a;
            return;
        }

        if (typeof(T) == typeof(Vector4)) {
            var vec4 = UnsafeUtility.As<T, Vector4>(ref value);
            result->type = InteropType.Vector4;
            result->vecX = vec4.x;
            result->vecY = vec4.y;
            result->vecZ = vec4.z;
            result->vecW = vec4.w;
            return;
        }

        if (typeof(T) == typeof(Vector2)) {
            var vec2 = UnsafeUtility.As<T, Vector2>(ref value);
            result->type = InteropType.Vector3;
            result->vecX = vec2.x;
            result->vecY = vec2.y;
            result->vecZ = 0;
            return;
        }

        if (value == null) return;

        if (typeof(T) == typeof(string)) {
            result->type = InteropType.String;
            result->str = StringToUtf8(UnsafeUtility.As<T, string>(ref value));
            return;
        }

        var type = typeof(T);

        if (type.IsValueType && _structPackers.TryGetValue(type, out var packerDelegate)) {
            var packer = (StructPackerRaw)packerDelegate;
            packer(value, result);
            return;
        }

        if (type.IsValueType) {
            var json = SerializeStruct(value);
            if (json != null) {
                result->type = InteropType.String;
                result->str = StringToUtf8(json);
                return;
            }
        }

        var handle = RegisterObject(value);
        result->type = InteropType.ObjectHandle;
        result->handle = handle;
        result->typeHint = StringToUtf8(type.FullName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe T ReadFromInterop<T>(InteropValue* v) {
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

        if (typeof(T) == typeof(Vector3)) {
            Vector3 vec;
            if (v->type == InteropType.Vector3 || v->type == InteropType.Vector4) {
                vec = new Vector3(v->vecX, v->vecY, v->vecZ);
            } else if (v->type == InteropType.JsonObject || v->type == InteropType.String) {
                vec = ReadStructFromJson<Vector3>(v);
            } else {
                vec = Vector3.zero;
            }
            return UnsafeUtility.As<Vector3, T>(ref vec);
        }

        if (typeof(T) == typeof(Quaternion)) {
            Quaternion quat;
            if (v->type == InteropType.Vector4) {
                quat = new Quaternion(v->vecX, v->vecY, v->vecZ, v->vecW);
            } else if (v->type == InteropType.JsonObject || v->type == InteropType.String) {
                quat = ReadStructFromJson<Quaternion>(v);
            } else {
                quat = Quaternion.identity;
            }
            return UnsafeUtility.As<Quaternion, T>(ref quat);
        }

        if (typeof(T) == typeof(Color)) {
            Color col;
            if (v->type == InteropType.Vector4) {
                col = new Color(v->vecX, v->vecY, v->vecZ, v->vecW);
            } else if (v->type == InteropType.JsonObject || v->type == InteropType.String) {
                col = ReadStructFromJson<Color>(v);
            } else {
                col = Color.white;
            }
            return UnsafeUtility.As<Color, T>(ref col);
        }

        if (typeof(T) == typeof(Vector4)) {
            Vector4 vec4;
            if (v->type == InteropType.Vector4) {
                vec4 = new Vector4(v->vecX, v->vecY, v->vecZ, v->vecW);
            } else if (v->type == InteropType.Vector3) {
                vec4 = new Vector4(v->vecX, v->vecY, v->vecZ, 0);
            } else {
                vec4 = Vector4.zero;
            }
            return UnsafeUtility.As<Vector4, T>(ref vec4);
        }

        if (typeof(T) == typeof(Vector2)) {
            Vector2 vec2;
            if (v->type == InteropType.Vector3 || v->type == InteropType.Vector4) {
                vec2 = new Vector2(v->vecX, v->vecY);
            } else {
                vec2 = Vector2.zero;
            }
            return UnsafeUtility.As<Vector2, T>(ref vec2);
        }

        if (v->type == InteropType.ObjectHandle) {
            var obj = GetObjectByHandle(v->handle);
            if (obj is T t) return t;
        }

        var type = typeof(T);

        if (type.IsValueType && (v->type == InteropType.Vector3 || v->type == InteropType.Vector4)) {
            if (_structUnpackers.TryGetValue(type, out var unpackerDelegate)) {
                var unpacker = (StructUnpackerRaw)unpackerDelegate;
                var obj = unpacker(v);
                if (obj is T result) return result;
            }
        }

        if (type.IsValueType && (v->type == InteropType.String || v->type == InteropType.JsonObject)) {
            if (_dictConverters.TryGetValue(type, out var converterDelegate)) {
                var json = PtrToStringUtf8(v->str);
                if (!string.IsNullOrEmpty(json)) {
                    var dict = ParseSimpleJson(json);
                    if (dict != null) {
                        var converter = (DictConverterRaw)converterDelegate;
                        var obj = converter(dict);
                        if (obj is T result) return result;
                    }
                }
            }
        }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe T ReadStructFromJson<T>(InteropValue* v) where T : struct {
        var json = PtrToStringUtf8(v->str);
        if (!string.IsNullOrEmpty(json)) {
            var dict = ParseSimpleJson(json);
            if (dict != null) {
                var deserialized = DeserializeFromDict(dict, typeof(T));
                if (deserialized is T result) return result;
            }
        }
        return default;
    }
}