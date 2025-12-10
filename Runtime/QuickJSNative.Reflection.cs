using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

public static partial class QuickJSNative {
    // MARK: Type and Member Caches
    static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
    
    // Using ConcurrentDictionary for thread-safe cache access without explicit locking
    // Method cache key includes argument type hash to handle overloads correctly
    static readonly ConcurrentDictionary<(Type, string, bool, int), MethodInfo> _methodCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), PropertyInfo> _propertyCache = new();
    static readonly ConcurrentDictionary<(Type, string, bool), FieldInfo> _fieldCache = new();

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

    /// <summary>
    /// Generic member finder that walks the type hierarchy with a common pattern.
    /// </summary>
    static T FindMember<T>(Type type, string name, BindingFlags flags, Func<Type, string, BindingFlags, T> getter) where T : class {
        while (type != null) {
            var member = getter(type, name, flags | BindingFlags.DeclaredOnly);
            if (member != null) return member;
            type = type.BaseType;
        }
        return null;
    }

    static PropertyInfo FindProperty(Type type, string name, BindingFlags flags) => 
        FindMember(type, name, flags, (t, n, f) => t.GetProperty(n, f));

    static FieldInfo FindField(Type type, string name, BindingFlags flags) => 
        FindMember(type, name, flags, (t, n, f) => t.GetField(n, f));

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
}

