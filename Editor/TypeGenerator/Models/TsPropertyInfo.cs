using System.Reflection;
using System.Text;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Represents a property (with getter/setter)
    /// </summary>
    public class TsPropertyInfo {
        public string Name { get; set; }
        public TsTypeRef Type { get; set; }

        public bool IsStatic { get; set; }
        public bool HasGetter { get; set; }
        public bool HasSetter { get; set; }
        public TsAccessibility Accessibility { get; set; } = TsAccessibility.Public;

        // Documentation
        public string Documentation { get; set; }
        public bool IsObsolete { get; set; }
        public string ObsoleteMessage { get; set; }

        public PropertyInfo OriginalProperty { get; set; }

        /// <summary>
        /// Gets the TypeScript representation of this property
        /// </summary>
        public string ToTypeScript(bool isInterface = false, bool useFullTypeName = true, bool useAccessors = true) {
            var sb = new StringBuilder();

            // Documentation
            if (!string.IsNullOrEmpty(Documentation)) {
                sb.AppendLine(Documentation);
            }

            var typeName = Type?.ToTypeScript(useFullTypeName) ?? "any";

            if (useAccessors && (HasGetter != HasSetter || !HasGetter)) {
                // Use get/set accessors when only one is available
                if (HasGetter) {
                    if (!isInterface) sb.Append("public ");
                    if (IsStatic) sb.Append("static ");
                    sb.Append($"get {Name}(): {typeName};");
                }
                if (HasSetter) {
                    if (HasGetter) sb.AppendLine();
                    if (!isInterface) sb.Append("public ");
                    if (IsStatic) sb.Append("static ");
                    sb.Append($"set {Name}(value: {typeName});");
                }
            } else {
                // Simple property declaration
                if (!isInterface) sb.Append("public ");
                if (IsStatic) sb.Append("static ");
                sb.Append($"{Name}: {typeName};");
            }

            return sb.ToString();
        }

        public override string ToString() => ToTypeScript();
    }

    /// <summary>
    /// Represents a field
    /// </summary>
    public class TsFieldInfo {
        public string Name { get; set; }
        public TsTypeRef Type { get; set; }

        public bool IsStatic { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsConst { get; set; }
        public TsAccessibility Accessibility { get; set; } = TsAccessibility.Public;

        // For const/static readonly, we might want the actual value
        public string ConstValue { get; set; }

        // Documentation
        public string Documentation { get; set; }
        public bool IsObsolete { get; set; }
        public string ObsoleteMessage { get; set; }

        public FieldInfo OriginalField { get; set; }

        /// <summary>
        /// Gets the TypeScript representation of this field
        /// </summary>
        public string ToTypeScript(bool isInterface = false, bool useFullTypeName = true) {
            var sb = new StringBuilder();

            // Documentation
            if (!string.IsNullOrEmpty(Documentation)) {
                sb.AppendLine(Documentation);
            }

            if (!isInterface) sb.Append("public ");
            if (IsStatic) sb.Append("static ");
            if (IsReadOnly || IsConst) sb.Append("readonly ");

            var typeName = Type?.ToTypeScript(useFullTypeName) ?? "any";
            sb.Append($"{Name}: {typeName};");

            return sb.ToString();
        }

        public override string ToString() => ToTypeScript();
    }
}
