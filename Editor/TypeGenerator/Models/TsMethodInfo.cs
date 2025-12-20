using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Represents a method, constructor, or delegate signature
    /// </summary>
    public class TsMethodInfo {
        public string Name { get; set; }
        public TsTypeRef ReturnType { get; set; }
        public List<TsParameterInfo> Parameters { get; set; } = new();

        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsConstructor { get; set; }
        public TsAccessibility Accessibility { get; set; } = TsAccessibility.Public;

        // Generic method support
        public bool IsGenericMethod { get; set; }
        public List<string> GenericParameters { get; set; } = new();
        public List<TsGenericConstraint> GenericConstraints { get; set; } = new();

        // Extension method support
        public bool IsExtensionMethod { get; set; }
        public TsTypeRef ExtendedType { get; set; }

        // Documentation
        public string Documentation { get; set; }
        public bool IsObsolete { get; set; }
        public string ObsoleteMessage { get; set; }

        public MethodBase OriginalMethod { get; set; }

        /// <summary>
        /// Gets the TypeScript representation of this method signature
        /// </summary>
        public string ToTypeScript(bool isInterface = false, bool useFullTypeName = true) {
            var sb = new StringBuilder();

            // Documentation (JSDoc)
            if (!string.IsNullOrEmpty(Documentation)) {
                sb.AppendLine(Documentation);
            }

            // Accessibility and modifiers
            if (!isInterface && Accessibility == TsAccessibility.Public) {
                sb.Append("public ");
            }
            if (IsStatic) {
                sb.Append("static ");
            }

            // Method name (or 'constructor')
            sb.Append(IsConstructor ? "constructor" : FormatMethodName(Name));

            // Generic parameters
            if (IsGenericMethod && GenericParameters.Count > 0) {
                sb.Append('<');
                for (int i = 0; i < GenericParameters.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append(GenericParameters[i]);
                    // Add constraints if any
                    var constraint = GenericConstraints?.Find(c => c.ParameterName == GenericParameters[i]);
                    if (constraint != null && constraint.HasConstraints) {
                        sb.Append(" extends ");
                        sb.Append(constraint.ToTypeScript());
                    }
                }
                sb.Append('>');
            }

            // Parameters
            sb.Append('(');
            for (int i = 0; i < Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append(Parameters[i].ToTypeScript(useFullTypeName));
            }
            sb.Append(')');

            // Return type (not for constructors)
            if (!IsConstructor) {
                var returnTypeName = ReturnType?.ToTypeScript(useFullTypeName) ?? "void";
                sb.Append($": {returnTypeName}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the TypeScript representation as a function type (for delegates)
        /// </summary>
        public string ToFunctionType(bool useFullTypeName = true) {
            var sb = new StringBuilder();
            sb.Append('(');
            for (int i = 0; i < Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append(Parameters[i].ToTypeScript(useFullTypeName));
            }
            sb.Append(") => ");
            sb.Append(ReturnType?.ToTypeScript(useFullTypeName) ?? "void");
            return sb.ToString();
        }

        private string FormatMethodName(string name) {
            // Handle explicit interface implementation (remove interface prefix)
            if (name.Contains(".")) {
                return name.Substring(name.LastIndexOf('.') + 1);
            }
            return name;
        }

        public override string ToString() => ToTypeScript();
    }

    public enum TsAccessibility {
        Public,
        Protected,
        Private,
        Internal
    }
}
