using System.Collections.Generic;
using System.Text;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Represents a generic type constraint (e.g., where T : class, IDisposable, new())
    /// </summary>
    public class TsGenericConstraint {
        public string ParameterName { get; set; }

        // Constraint flags
        public bool HasValueTypeConstraint { get; set; }        // where T : struct
        public bool HasReferenceTypeConstraint { get; set; }    // where T : class
        public bool HasDefaultConstructorConstraint { get; set; } // where T : new()
        public bool HasNotNullConstraint { get; set; }          // where T : notnull

        // Type constraints (base class or interfaces)
        public List<TsTypeRef> TypeConstraints { get; set; } = new();

        public bool HasConstraints =>
            HasValueTypeConstraint ||
            HasReferenceTypeConstraint ||
            HasDefaultConstructorConstraint ||
            HasNotNullConstraint ||
            TypeConstraints.Count > 0;

        /// <summary>
        /// Gets the TypeScript representation of the constraint
        /// Note: TypeScript doesn't have all C# constraints, so we approximate
        /// </summary>
        public string ToTypeScript(bool useFullTypeName = true) {
            if (!HasConstraints) return null;

            var parts = new List<string>();

            // Type constraints become extends
            foreach (var tc in TypeConstraints) {
                parts.Add(tc.ToTypeScript(useFullTypeName));
            }

            // If no type constraints but has struct constraint, we can't really express this in TS
            // For reference type constraint, we could use 'object' but it's not quite the same

            if (parts.Count == 0) {
                // Fallback for constraints we can't express
                if (HasValueTypeConstraint) {
                    // Can't really express struct constraint in TS
                    return null;
                }
                if (HasReferenceTypeConstraint) {
                    parts.Add("object");
                }
            }

            if (parts.Count == 0) return null;

            // TypeScript only supports single 'extends', but we can use intersection for multiple
            if (parts.Count == 1) {
                return parts[0];
            }

            // Use intersection type for multiple constraints
            return string.Join(" & ", parts);
        }

        public override string ToString() => $"{ParameterName}: {ToTypeScript()}";
    }
}
