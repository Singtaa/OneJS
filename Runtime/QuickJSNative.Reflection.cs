using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

public static partial class QuickJSNative {
    // MARK: Type and Member Caches
    static readonly Dictionary<string, Type> _typeCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool, int), MethodInfo> _methodCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), PropertyInfo> _propertyCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), FieldInfo> _fieldCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), bool> _hasMethodCache = new();

    // MARK: Extension Method Registry
    // targetType → { methodName → List<MethodInfo> }
    static readonly Dictionary<Type, Dictionary<string, List<MethodInfo>>> _extensionMethodRegistry = new();
    // Quick existence check for HasExtensionMethodByName path
    static readonly HashSet<(Type, string)> _extensionMethodNames = new();
    // Prevent duplicate scanning
    static readonly HashSet<Type> _registeredExtensionTypes = new();

    // MARK: BindingFlags Helpers
    const BindingFlags PublicNonPublic = BindingFlags.Public | BindingFlags.NonPublic;
    static BindingFlags GetFlags(bool isStatic) =>
        PublicNonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

    // MARK: Type Resolution
    static Type ResolveType(string fullName) {
        if (string.IsNullOrEmpty(fullName)) return null;
        if (_typeCache.TryGetValue(fullName, out var cached)) return cached;

        var type = Type.GetType(fullName);
        if (type == null) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    type = asm.GetType(fullName);
                    if (type != null) break;
                } catch { }
            }
        }

        if (type != null) _typeCache[fullName] = type;
        return type;
    }

    // MARK: Member Finders
    /// <summary>
    /// Generic member finder that walks the type hierarchy.
    /// </summary>
    static T FindMember<T>(Type type, string name, BindingFlags flags,
        Func<Type, string, BindingFlags, T> getter) where T : class {
        while (type != null) {
            var member = getter(type, name, flags | BindingFlags.DeclaredOnly);
            if (member != null) return member;
            type = type.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Check if an argument matches a parameter type.
    /// </summary>
    static bool IsArgCompatible(Type paramType, object arg) {
        if (arg == null)
            return !paramType.IsValueType || Nullable.GetUnderlyingType(paramType) != null;

        var argType = arg.GetType();
        if (paramType.IsAssignableFrom(argType)) return true;
        if (paramType.IsPrimitive && argType.IsPrimitive) return true;

        // Type reference dict -> System.Type
        if (paramType == typeof(Type) &&
            arg is Dictionary<string, object> dict &&
            dict.ContainsKey("__csTypeRef")) {
            return true;
        }

        // Callback handle dict -> Delegate
        if (typeof(Delegate).IsAssignableFrom(paramType) &&
            arg is Dictionary<string, object> callbackDict &&
            callbackDict.ContainsKey("__csCallbackHandle")) {
            return true;
        }

        // Dictionary -> serializable struct (ConvertToTargetType will handle the conversion)
        // This handles structs like Angle, Length, etc. that are serialized as JSON objects
        if (arg is Dictionary<string, object> &&
            paramType.IsValueType && !paramType.IsPrimitive && !paramType.IsEnum) {
            return true;
        }

        // JSON string with __type marker -> serializable struct
        // When a serialized struct is passed through JS, it may come back as a JSON string
        // (due to try_convert_struct in native code returning STRING type for __type objects)
        if (arg is string jsonStr && jsonStr.StartsWith("{\"__type\":") &&
            paramType.IsValueType && !paramType.IsPrimitive && !paramType.IsEnum) {
            return true;
        }

        // Enum from numeric value
        if (paramType.IsEnum && IsNumericType(argType)) {
            return true;
        }

        // Vector3 -> Vector2 (C# returns Vector2 as Vector3 with z=0 for efficiency)
        if (paramType == typeof(UnityEngine.Vector2) && argType == typeof(UnityEngine.Vector3)) {
            return true;
        }

        return false;
    }

    static MethodInfo FindMethod(Type type, string name, BindingFlags flags, object[] args) {
        // Prefer an exact arity/type match. Fall back to a method that declares
        // additional trailing parameters where every extra has a C# default
        // value — this lets JS callers omit optional trailing arguments, e.g.
        //   audioManager.PlaySfx("UI/Submit")
        // binds to `PlaySfx(string key, float volumeScale = 1, float pitch = 1)`.
        // The invoke site (QuickJSNative.Dispatch) fills the missing slots with
        // the declared default values.
        MethodInfo optionalParamFallback = null;

        while (type != null) {
            foreach (var m in type.GetMethods(flags | BindingFlags.DeclaredOnly)) {
                if (m.Name != name) continue;
                var parameters = m.GetParameters();

                if (parameters.Length == args.Length) {
                    bool match = true;
                    for (int j = 0; j < parameters.Length && match; j++)
                        match = IsArgCompatible(parameters[j].ParameterType, args[j]);
                    if (match) return m;
                    continue;
                }

                if (parameters.Length > args.Length && optionalParamFallback == null) {
                    bool match = true;
                    for (int j = 0; j < args.Length && match; j++)
                        match = IsArgCompatible(parameters[j].ParameterType, args[j]);
                    if (match) {
                        for (int j = args.Length; j < parameters.Length && match; j++)
                            match = parameters[j].HasDefaultValue;
                        if (match) optionalParamFallback = m;
                    }
                }
            }
            type = type.BaseType;
        }

        return optionalParamFallback;
    }

    static PropertyInfo FindProperty(Type type, string name, BindingFlags flags) {
        var prop = FindMember(type, name, flags, (t, n, f) => t.GetProperty(n, f));
        if (prop != null) return prop;

        // Search interfaces (needed for IStyle, etc.)
        foreach (var iface in type.GetInterfaces()) {
            prop = iface.GetProperty(name);
            if (prop != null) return prop;
        }
        return null;
    }

    static FieldInfo FindField(Type type, string name, BindingFlags flags) =>
        FindMember(type, name, flags, (t, n, f) => t.GetField(n, f));

    /// <summary>
    /// Check if any method with the given name exists (ignores parameter count/types).
    /// Used for property-first access pattern to detect method references.
    /// </summary>
    static bool HasMethodByName(Type type, string name, bool isStatic) {
        var key = (type, name, isStatic);
        if (_hasMethodCache.TryGetValue(key, out var cached)) return cached;

        var flags = GetFlags(isStatic);
        var current = type;
        while (current != null) {
            foreach (var m in current.GetMethods(flags | BindingFlags.DeclaredOnly)) {
                if (m.Name == name) {
                    _hasMethodCache[key] = true;
                    return true;
                }
            }
            current = current.BaseType;
        }
        _hasMethodCache[key] = false;
        return false;
    }

    // MARK: Extension Methods

    /// <summary>
    /// Register all extension methods from a static class.
    /// Scans public static methods with [Extension] attribute and indexes them
    /// by the type of their first parameter (the 'this' target).
    /// Idempotent — skips if already registered.
    /// </summary>
    static void RegisterExtensionType(Type extensionClass) {
        if (!_registeredExtensionTypes.Add(extensionClass)) return;

        foreach (var method in extensionClass.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (!method.IsDefined(typeof(ExtensionAttribute), false)) continue;

            var parms = method.GetParameters();
            if (parms.Length == 0) continue;

            var targetType = parms[0].ParameterType;

            if (!_extensionMethodRegistry.TryGetValue(targetType, out var byName)) {
                byName = new Dictionary<string, List<MethodInfo>>();
                _extensionMethodRegistry[targetType] = byName;
            }

            if (!byName.TryGetValue(method.Name, out var overloads)) {
                overloads = new List<MethodInfo>();
                byName[method.Name] = overloads;
            }

            overloads.Add(method);
            _extensionMethodNames.Add((targetType, method.Name));
        }
    }

    /// <summary>
    /// Find an extension method for the given instance type and name.
    /// Walks the type hierarchy (type + base types + interfaces).
    /// Extension params are [thisParam, ...rest]; matches rest against args.
    /// </summary>
    static MethodInfo FindExtensionMethod(Type instanceType, string name, object[] args) {
        var current = instanceType;
        while (current != null) {
            var method = FindExtensionMethodForExactType(current, name, args);
            if (method != null) return method;
            current = current.BaseType;
        }

        foreach (var iface in instanceType.GetInterfaces()) {
            var method = FindExtensionMethodForExactType(iface, name, args);
            if (method != null) return method;
        }

        return null;
    }

    static MethodInfo FindExtensionMethodForExactType(Type targetType, string name, object[] args) {
        if (!_extensionMethodRegistry.TryGetValue(targetType, out var byName)) return null;
        if (!byName.TryGetValue(name, out var overloads)) return null;

        foreach (var method in overloads) {
            var parms = method.GetParameters();
            // First param is 'this', rest must match args
            if (parms.Length - 1 != args.Length) continue;

            bool match = true;
            for (int i = 0; i < args.Length && match; i++) {
                match = IsArgCompatible(parms[i + 1].ParameterType, args[i]);
            }

            if (match) return method;
        }

        return null;
    }

    /// <summary>
    /// Check if any extension method with the given name exists for the instance type.
    /// Walks the type hierarchy. Used by GetProp path to detect method references.
    /// </summary>
    static bool HasExtensionMethodByName(Type instanceType, string name) {
        var current = instanceType;
        while (current != null) {
            if (_extensionMethodNames.Contains((current, name))) return true;
            current = current.BaseType;
        }

        foreach (var iface in instanceType.GetInterfaces()) {
            if (_extensionMethodNames.Contains((iface, name))) return true;
        }

        return false;
    }

    // MARK: Argument Hash
    static int ComputeArgTypeHash(object[] args) {
        if (args == null || args.Length == 0) return 0;
        int hash = args.Length;
        foreach (var arg in args)
            hash = hash * 31 + (arg?.GetType().GetHashCode() ?? 0);
        return hash;
    }

    // MARK: Cached Lookups
    static MethodInfo FindMethodCached(Type type, string name, bool isStatic, object[] args) {
        var key = (type, name, isStatic, ComputeArgTypeHash(args));
        if (_methodCache.TryGetValue(key, out var cached) && cached.GetParameters().Length >= args.Length)
            return cached;

        var method = FindMethod(type, name, GetFlags(isStatic), args);
        if (method != null) _methodCache[key] = method;
        return method;
    }

    static PropertyInfo FindPropertyCached(Type type, string name, bool isStatic) {
        var key = (type, name, isStatic);
        if (_propertyCache.TryGetValue(key, out var cached)) return cached;

        var prop = FindProperty(type, name, GetFlags(isStatic));
        if (prop != null) _propertyCache[key] = prop;
        return prop;
    }

    static FieldInfo FindFieldCached(Type type, string name, bool isStatic) {
        var key = (type, name, isStatic);
        if (_fieldCache.TryGetValue(key, out var cached)) return cached;

        var field = FindField(type, name, GetFlags(isStatic));
        if (field != null) _fieldCache[key] = field;
        return field;
    }

    // MARK: Generic Type Helpers

    /// <summary>
    /// Extracts a string from an InteropValue. Used for type argument names in generic binding.
    /// </summary>
    static string InteropValueToString(InteropValue v) {
        // Use InteropValueToObject to handle all types that can be converted to string
        var obj = InteropValueToObject(v);
        if (obj == null) return null;
        return obj.ToString();
    }

    /// <summary>
    /// Generates a unique type name for a constructed generic type.
    /// Example: List`1[System.Int32] for List&lt;int&gt;
    /// </summary>
    static string GetGenericTypeName(Type constructedType) {
        if (constructedType == null) return null;

        // Use FullName which already has the proper format for generics
        // e.g. "System.Collections.Generic.List`1[[System.Int32, mscorlib, ...]]"
        // But we want a cleaner format for our cache key
        var genericDef = constructedType.GetGenericTypeDefinition();
        var typeArgs = constructedType.GetGenericArguments();

        var sb = new System.Text.StringBuilder();
        sb.Append(genericDef.FullName);
        sb.Append('[');
        for (int i = 0; i < typeArgs.Length; i++) {
            if (i > 0) sb.Append(',');
            sb.Append(typeArgs[i].FullName);
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Caches a type by name for future lookups.
    /// </summary>
    static void CacheType(string typeName, Type type) {
        if (string.IsNullOrEmpty(typeName) || type == null) return;
        _typeCache[typeName] = type;
    }
}