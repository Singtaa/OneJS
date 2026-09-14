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
        /// Gets the TypeScript representation of this parameter
        /// </summary>
        public string ToTypeScript(bool useFullTypeName = true) {
            var name = IsParams ? $"...{Name}" : $"${Name}";
            var optional = IsOptional ? "?" : "";
            var typeName = Widen(Type) ?? Type?.ToTypeScript(useFullTypeName) ?? "any";

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
        /// `System.TypeLike` for a parameter that wants a `System.Type`, else null.
        ///
        /// JS hands C# a class reference (`CS.UnityEngine.Vector3`, a path proxy)
        /// where C# declares a `Type`, so a `Type` parameter typed as `System.Type`
        /// rejects the only thing a caller can actually pass. `TypeLike` is the
        /// union of both, declared in _system.d.ts.
        ///
        /// Parameters only. A member that RETURNS a `Type` returns a real one, and
        /// widening that would lie to the caller in the other direction. This was
        /// applied by hand to ~118 signatures in unity-types before it lived here.
        /// </summary>
        static string Widen(TsTypeRef type) {
            if (type == null) return null;
            if (type.OriginalType == typeof(System.Type)) return "System.TypeLike";
            if (type.IsArray && type.GenericArguments.Count == 1
                && type.GenericArguments[0].OriginalType == typeof(System.Type)) {
                return "System.Array$1<System.TypeLike>";
            }
            return null;
        }

        public override string ToString() => ToTypeScript();
    }
}
