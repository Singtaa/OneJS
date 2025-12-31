using System;
using System.Collections.Generic;
using System.Text;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// The kind of TypeScript type being generated
    /// </summary>
    public enum TsTypeKind {
        Class,
        Interface,
        Enum,
        Delegate,
        Struct,
        TypeAlias
    }

    /// <summary>
    /// Represents a complete type to be emitted as TypeScript
    /// </summary>
    public class TsTypeInfo {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

        public TsTypeKind Kind { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public bool IsStatic { get; set; }

        // Generic type support
        public bool IsGenericTypeDefinition { get; set; }
        public List<string> GenericParameters { get; set; } = new();
        public List<TsGenericConstraint> GenericConstraints { get; set; } = new();

        // Inheritance
        public TsTypeRef BaseType { get; set; }
        public List<TsTypeRef> Interfaces { get; set; } = new();

        // Members
        public List<TsFieldInfo> Fields { get; set; } = new();
        public List<TsPropertyInfo> Properties { get; set; } = new();
        public List<TsMethodInfo> Methods { get; set; } = new();
        public List<TsMethodInfo> Constructors { get; set; } = new();
        public List<TsEventInfo> Events { get; set; } = new();
        public List<TsIndexerInfo> Indexers { get; set; } = new();

        // Extension methods that extend this type
        public List<TsMethodInfo> ExtensionMethods { get; set; } = new();

        // Nested types
        public List<TsTypeInfo> NestedTypes { get; set; } = new();

        // For delegates
        public TsMethodInfo DelegateSignature { get; set; }

        // For enums
        public List<TsEnumMember> EnumMembers { get; set; } = new();
        public bool IsEnumFlags { get; set; }

        // For type aliases (e.g., problematic types emitted as 'any')
        public TsTypeRef AliasedType { get; set; }

        // Documentation
        public string Documentation { get; set; }
        public bool IsObsolete { get; set; }
        public string ObsoleteMessage { get; set; }

        // Original type reference
        public Type OriginalType { get; set; }

        /// <summary>
        /// Gets the type declaration line (e.g., "class Foo<T> extends Bar implements IBaz")
        /// </summary>
        public string GetDeclarationLine(bool useFullBaseTypeName = true) {
            var sb = new StringBuilder();

            // Type keyword
            sb.Append(GetTypeKeyword());
            sb.Append(' ');

            // Type name
            sb.Append(Name.Replace('`', '$'));

            // Generic parameters
            if (IsGenericTypeDefinition && GenericParameters.Count > 0) {
                sb.Append('<');
                for (int i = 0; i < GenericParameters.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append(GenericParameters[i]);

                    // Add constraints
                    var constraint = GenericConstraints?.Find(c => c.ParameterName == GenericParameters[i]);
                    if (constraint != null && constraint.HasConstraints) {
                        var constraintStr = constraint.ToTypeScript(useFullBaseTypeName);
                        if (!string.IsNullOrEmpty(constraintStr)) {
                            sb.Append(" extends ");
                            sb.Append(constraintStr);
                        }
                    }
                }
                sb.Append('>');
            }

            // Base type (for classes)
            if (Kind == TsTypeKind.Class && BaseType != null) {
                sb.Append(" extends ");
                sb.Append(FormatBaseTypeName(BaseType, useFullBaseTypeName));
            }

            // Interfaces
            if (Interfaces.Count > 0 && Kind != TsTypeKind.Enum && Kind != TsTypeKind.Delegate) {
                sb.Append(Kind == TsTypeKind.Interface ? " extends " : " implements ");
                for (int i = 0; i < Interfaces.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatBaseTypeName(Interfaces[i], useFullBaseTypeName));
                }
            }

            return sb.ToString();
        }

        private string GetTypeKeyword() {
            return Kind switch {
                TsTypeKind.Class => "class",
                TsTypeKind.Struct => "class", // Structs are classes in TS
                TsTypeKind.Interface => "interface",
                TsTypeKind.Enum => "enum",
                TsTypeKind.Delegate => "interface", // Delegates become interfaces
                _ => "class"
            };
        }

        private string FormatBaseTypeName(TsTypeRef typeRef, bool useFullName) {
            if (typeRef == null) return "";

            var name = useFullName ? typeRef.FullName : typeRef.Name;

            // Handle generic types
            if (typeRef.IsGeneric && typeRef.GenericArguments.Count > 0) {
                var baseName = name;
                var tickIndex = baseName.IndexOf('`');
                if (tickIndex > 0) {
                    baseName = baseName.Substring(0, tickIndex);
                }

                var sb = new StringBuilder(baseName);
                sb.Append('$');
                sb.Append(typeRef.GenericArguments.Count);
                sb.Append('<');
                for (int i = 0; i < typeRef.GenericArguments.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append(typeRef.GenericArguments[i].ToTypeScript(useFullName));
                }
                sb.Append('>');
                return sb.ToString();
            }

            // Replace backtick with $ for generic type names
            return name.Replace('`', '$');
        }

        public override string ToString() => FullName;
    }

    /// <summary>
    /// Represents an event
    /// </summary>
    public class TsEventInfo {
        public string Name { get; set; }
        public TsTypeRef EventHandlerType { get; set; }
        public bool IsStatic { get; set; }

        public string Documentation { get; set; }
        public bool IsObsolete { get; set; }

        /// <summary>
        /// Gets the add/remove method signatures for this event
        /// </summary>
        public string ToTypeScript(bool isInterface = false, bool useFullTypeName = true) {
            var sb = new StringBuilder();
            var handlerType = EventHandlerType?.ToTypeScript(useFullTypeName) ?? "Function";

            // add_EventName
            if (!isInterface) sb.Append("public ");
            if (IsStatic) sb.Append("static ");
            sb.Append($"add_{Name}(handler: {handlerType}): void;");
            sb.AppendLine();

            // remove_EventName
            if (!isInterface) sb.Append("public ");
            if (IsStatic) sb.Append("static ");
            sb.Append($"remove_{Name}(handler: {handlerType}): void;");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents an indexer (this[])
    /// </summary>
    public class TsIndexerInfo {
        public List<TsParameterInfo> Parameters { get; set; } = new();
        public TsTypeRef ReturnType { get; set; }
        public bool HasGetter { get; set; }
        public bool HasSetter { get; set; }

        public string Documentation { get; set; }

        /// <summary>
        /// Gets the get_Item/set_Item method signatures
        /// </summary>
        public string ToTypeScript(bool isInterface = false, bool useFullTypeName = true) {
            var sb = new StringBuilder();
            var returnTypeName = ReturnType?.ToTypeScript(useFullTypeName) ?? "any";

            // Build parameter list
            var paramList = new StringBuilder();
            for (int i = 0; i < Parameters.Count; i++) {
                if (i > 0) paramList.Append(", ");
                paramList.Append(Parameters[i].ToTypeScript(useFullTypeName));
            }

            if (HasGetter) {
                if (!isInterface) sb.Append("public ");
                sb.Append($"get_Item({paramList}): {returnTypeName};");
            }

            if (HasSetter) {
                if (HasGetter) sb.AppendLine();
                if (!isInterface) sb.Append("public ");
                sb.Append($"set_Item({paramList}, value: {returnTypeName}): void;");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents an enum member
    /// </summary>
    public class TsEnumMember {
        public string Name { get; set; }
        public object Value { get; set; }
        public string Documentation { get; set; }

        public string ToTypeScript() {
            if (Value != null) {
                return $"{Name} = {Value}";
            }
            return Name;
        }
    }
}
