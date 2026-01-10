using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

public static partial class QuickJSNative {
    // MARK: Type and Member Caches
    static readonly Dictionary<string, Type> _typeCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool, int), MethodInfo> _methodCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), PropertyInfo> _propertyCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), FieldInfo> _fieldCache = new();

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

        return false;
    }

    static MethodInfo FindMethod(Type type, string name, BindingFlags flags, object[] args) {
        while (type != null) {
            foreach (var m in type.GetMethods(flags | BindingFlags.DeclaredOnly)) {
                if (m.Name != name) continue;
                var parameters = m.GetParameters();
                if (parameters.Length != args.Length) continue;

                bool match = true;
                for (int j = 0; j < parameters.Length && match; j++)
                    match = IsArgCompatible(parameters[j].ParameterType, args[j]);

                if (match) return m;
            }
            type = type.BaseType;
        }
        return null;
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
        if (_methodCache.TryGetValue(key, out var cached) && cached.GetParameters().Length == args.Length)
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