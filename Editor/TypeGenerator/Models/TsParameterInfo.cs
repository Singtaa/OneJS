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
            var typeName = Type?.ToTypeScript(useFullTypeName) ?? "any";

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

        public override string ToString() => ToTypeScript();
    }
}
