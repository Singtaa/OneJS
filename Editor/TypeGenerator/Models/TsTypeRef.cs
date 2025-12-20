using System;
using System.Collections.Generic;
using System.Text;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Represents a reference to a type (used for property types, method returns, base types, etc.)
    /// </summary>
    public class TsTypeRef {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

        public bool IsGeneric { get; set; }
        public List<TsTypeRef> GenericArguments { get; set; } = new();

        public bool IsNullable { get; set; }
        public bool IsByRef { get; set; }
        public bool IsOut { get; set; }
        public bool IsIn { get; set; }
        public bool IsArray { get; set; }
        public int ArrayRank { get; set; } = 1;
        public bool IsPointer { get; set; }

        // For primitives that map directly to TS types
        public bool IsPrimitive { get; set; }
        public string PrimitiveTypeName { get; set; }

        // The original C# type (for reference during analysis)
        public Type OriginalType { get; set; }

        public TsTypeRef() { }

        public TsTypeRef(string name, string ns = null) {
            Name = name;
            Namespace = ns;
        }

        /// <summary>
        /// Gets the TypeScript representation of this type reference
        /// </summary>
        public string ToTypeScript(bool useFullName = true) {
            var sb = new StringBuilder();

            if (IsByRef || IsOut) {
                sb.Append(IsOut ? "$Out<" : "$Ref<");
            }

            if (IsPrimitive && !string.IsNullOrEmpty(PrimitiveTypeName)) {
                sb.Append(PrimitiveTypeName);
            } else if (IsArray) {
                var elementType = GenericArguments.Count > 0
                    ? GenericArguments[0].ToTypeScript(useFullName)
                    : "any";
                sb.Append($"System.Array$1<{elementType}>");
            } else if (IsGeneric && GenericArguments.Count > 0) {
                var baseName = useFullName ? FullName : Name;
                // Remove backtick notation if present
                var tickIndex = baseName.IndexOf('`');
                if (tickIndex > 0) {
                    baseName = baseName.Substring(0, tickIndex);
                }
                sb.Append(baseName);
                sb.Append('$');
                sb.Append(GenericArguments.Count);
                sb.Append('<');
                for (int i = 0; i < GenericArguments.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append(GenericArguments[i].ToTypeScript(useFullName));
                }
                sb.Append('>');
            } else {
                sb.Append(useFullName ? FullName : Name);
            }

            if (IsByRef || IsOut) {
                sb.Append('>');
            }

            if (IsNullable) {
                sb.Append(" | null");
            }

            return sb.ToString();
        }

        public override string ToString() => ToTypeScript();
    }
}
