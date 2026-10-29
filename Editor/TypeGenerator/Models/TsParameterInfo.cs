using System.Reflection;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Represents a method or constructor parameter
    /// </summary>
    public class TsParameterInfo {
        public string Name { get; set; }
        public TsTypeRef Type { get; set; }
        public bool IsOptional { get; set; }
        public bool IsParams { get; set; }      // params keyword (variadic)
        public bool IsOut { get; set; }         // out keyword
        public bool IsRef { get; set; }         // ref keyword
        public bool IsIn { get; set; }          // in keyword (C# 7.2+)
        public string DefaultValue { get; set; } // String representation of default value

        public ParameterInfo OriginalParameter { get; set; }

        /// <summary>
        /// Gets the TypeScript representation of this parameter. `widen: false`
        /// declares the C# type itself, for a parameter C# fills, as in the call
        /// signature of a delegate that C# invokes on a JS function.
        /// </summary>
        public string ToTypeScript(bool useFullTypeName = true, bool widen = true) {
            var name = IsParams ? $"...{Name}" : $"${Name}";
            var optional = IsOptional ? "?" : "";
            var typeName = (widen ? Widen(Type, useFullTypeName, IsParams) : null) ?? Type?.ToTypeScript(useFullTypeName) ?? "any";

            // For params, use array syntax
            if (IsParams && !typeName.EndsWith("[]")) {
                // Convert System.Array$1<T> to T[] for params
                if (typeName.StartsWith("System.Array$1<") && typeName.EndsWith(">")) {
                    var inner = typeName.Substring(15, typeName.Length - 16);
                    typeName = $"{inner}[]";
                } else {
                    typeName = $"{typeName}[]";
                }
            }

            return $"{name}{optional}: {typeName}";
        }

        /// <summary>
        /// What a parameter accepts when that is wider than the C# type it
        /// declares, else null.
        ///
        /// `System.TypeLike` for a parameter that wants a `System.Type`.
        ///
        /// JS hands C# a class reference (`CS.UnityEngine.Vector3`, a path proxy)
        /// where C# declares a `Type`, so a `Type` parameter typed as `System.Type`
        /// rejects the only thing a caller can actually pass. `TypeLike` is the
        /// union of both, declared in _system.d.ts.
        ///
        /// Parameters only. A member that RETURNS a `Type` returns a real one, and
        /// widening that would lie to the caller in the other direction. This was
        /// applied by hand to ~118 signatures in unity-types before it lived here.
        ///
        /// `System.ArrayInput<T>` for a one-dimensional array passed in (#150).
        /// The bootstrap sends a JS array or typed array as an `__csArray` marker
        /// that the runtime converts to the parameter's `T[]`, so `System.Array$1`
        /// alone rejected a `Float32Array` the call accepts. A `ref` or `out` array
        /// is written back as a C# array, a multidimensional one is not carried by
        /// the marker, and a `params` array is spread, so those keep their type.
        ///
        /// The list itself or `System.ArrayInput<T>` for a `List<T>` passed in,
        /// which the runtime fills from a JS array element by element (#151). A
        /// `ref` or `out` list is written back as the list, so it keeps its type.
        /// </summary>
        static string Widen(TsTypeRef type, bool useFullTypeName, bool isParams) {
            if (type == null) return null;
            if (type.OriginalType == typeof(System.Type)) return "System.TypeLike";
            if (type.IsArray && type.GenericArguments.Count == 1) {
                var element = type.GenericArguments[0];
                var isType = element.OriginalType == typeof(System.Type);
                if (!isParams && type.ArrayRank == 1 && !type.IsByRef && !type.IsOut) {
                    var elementName = isType ? "System.TypeLike" : element.ToTypeScript(useFullTypeName);
                    return $"System.ArrayInput<{elementName}>";
                }
                if (isType) return "System.Array$1<System.TypeLike>";
            }
            var original = type.OriginalType;
            if (original != null && original.IsGenericType && original.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)
                && type.GenericArguments.Count == 1 && !type.IsByRef && !type.IsOut) {
                return $"{type.ToTypeScript(useFullTypeName)} | System.ArrayInput<{type.GenericArguments[0].ToTypeScript(useFullTypeName)}>";
            }
            return null;
        }

        public override string ToString() => ToTypeScript();
    }
}
