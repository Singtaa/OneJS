using System;
using System.Collections.Generic;
using System.Linq;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Maps C# types to TypeScript type representations
    /// </summary>
    public static class TypeMapper {
        /// <summary>
        /// Primitive type mappings from C# to TypeScript
        /// </summary>
        private static readonly Dictionary<Type, string> PrimitiveTypeMap = new() {
            // Numeric types -> number
            { typeof(int), "number" },
            { typeof(uint), "number" },
            { typeof(short), "number" },
            { typeof(ushort), "number" },
            { typeof(byte), "number" },
            { typeof(sbyte), "number" },
            { typeof(float), "number" },
            { typeof(double), "number" },
            { typeof(decimal), "number" },
            { typeof(char), "number" },

            // Long types -> bigint (64-bit)
            { typeof(long), "bigint" },
            { typeof(ulong), "bigint" },

            // Boolean
            { typeof(bool), "boolean" },

            // String
            { typeof(string), "string" },

            // Void
            { typeof(void), "void" },

            // Object/dynamic -> any
            { typeof(object), "any" },

            // IntPtr/UIntPtr
            { typeof(IntPtr), "number" },
            { typeof(UIntPtr), "number" },
        };

        /// <summary>
        /// Special type mappings that need custom handling
        /// </summary>
        private static readonly Dictionary<string, string> SpecialTypeMap = new() {
            { "System.Delegate", "Function" },
            { "System.MulticastDelegate", "Function" },
            { "System.Type", "System.Type" },
            { "System.DateTime", "System.DateTime" },
            { "System.TimeSpan", "System.TimeSpan" },
            { "System.Guid", "System.Guid" },
        };

        /// <summary>
        /// Creates a TsTypeRef from a C# Type
        /// </summary>
        public static TsTypeRef MapType(Type type) {
            if (type == null) {
                return new TsTypeRef { Name = "any", IsPrimitive = true, PrimitiveTypeName = "any" };
            }

            var typeRef = new TsTypeRef {
                OriginalType = type
            };

            // Handle by-ref types (ref, out, in)
            if (type.IsByRef) {
                var elementType = type.GetElementType();
                var innerRef = MapType(elementType);
                innerRef.IsByRef = true;
                return innerRef;
            }

            // Handle pointer types (not really supported in TS)
            if (type.IsPointer) {
                return new TsTypeRef {
                    Name = "any",
                    IsPrimitive = true,
                    PrimitiveTypeName = "any",
                    IsPointer = true,
                    OriginalType = type
                };
            }

            // Handle array types
            if (type.IsArray) {
                var elementType = type.GetElementType();
                typeRef.IsArray = true;
                typeRef.ArrayRank = type.GetArrayRank();
                typeRef.GenericArguments.Add(MapType(elementType));
                typeRef.Name = "Array";
                typeRef.Namespace = "System";
                return typeRef;
            }

            // Check primitive types first
            if (PrimitiveTypeMap.TryGetValue(type, out var primitiveTs)) {
                typeRef.IsPrimitive = true;
                typeRef.PrimitiveTypeName = primitiveTs;
                typeRef.Name = primitiveTs;
                return typeRef;
            }

            // Handle nullable types
            var underlyingNullable = Nullable.GetUnderlyingType(type);
            if (underlyingNullable != null) {
                var innerRef = MapType(underlyingNullable);
                innerRef.IsNullable = true;
                return innerRef;
            }

            // Handle generic types
            if (type.IsGenericType) {
                return MapGenericType(type);
            }

            // Handle generic parameters (T, U, etc.)
            if (type.IsGenericParameter) {
                typeRef.Name = type.Name;
                typeRef.Namespace = null;
                return typeRef;
            }

            // Check special type mappings
            var fullName = type.FullName ?? type.Name;
            if (SpecialTypeMap.TryGetValue(fullName, out var specialTs)) {
                typeRef.Name = specialTs;
                typeRef.IsPrimitive = true;
                typeRef.PrimitiveTypeName = specialTs;
                return typeRef;
            }

            // Handle nested types
            if (type.IsNested) {
                typeRef.Name = type.Name.Replace('`', '$');
                typeRef.Namespace = GetNestedTypeNamespace(type);
                return typeRef;
            }

            // Regular type
            typeRef.Name = type.Name.Replace('`', '$');
            typeRef.Namespace = type.Namespace;
            return typeRef;
        }

        /// <summary>
        /// Maps a generic type (like List<T> or Dictionary<K,V>)
        /// </summary>
        private static TsTypeRef MapGenericType(Type type) {
            var typeRef = new TsTypeRef {
                IsGeneric = true,
                OriginalType = type
            };

            var genericDef = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
            var genericArgs = type.GetGenericArguments();

            // Check for Task<T>
            if (genericDef.FullName?.StartsWith("System.Threading.Tasks.Task`") == true) {
                typeRef.Name = "$Task";
                typeRef.Namespace = null;
                typeRef.IsPrimitive = true;
                typeRef.PrimitiveTypeName = "$Task";
                foreach (var arg in genericArgs) {
                    typeRef.GenericArguments.Add(MapType(arg));
                }
                return typeRef;
            }

            // Check for Nullable<T>
            if (genericDef == typeof(Nullable<>)) {
                var innerType = MapType(genericArgs[0]);
                innerType.IsNullable = true;
                return innerType;
            }

            // Check for Action/Func delegates
            if (IsActionOrFunc(genericDef, out var delegateKind)) {
                return MapActionOrFunc(type, delegateKind);
            }

            // Regular generic type
            var baseName = genericDef.Name;
            var tickIndex = baseName.IndexOf('`');
            if (tickIndex > 0) {
                baseName = baseName.Substring(0, tickIndex);
            }

            typeRef.Name = baseName;
            typeRef.Namespace = type.IsNested ? GetNestedTypeNamespace(type) : type.Namespace;

            foreach (var arg in genericArgs) {
                typeRef.GenericArguments.Add(MapType(arg));
            }

            return typeRef;
        }

        /// <summary>
        /// Gets the namespace for a nested type, including parent type names
        /// </summary>
        private static string GetNestedTypeNamespace(Type type) {
            var parts = new List<string>();
            var current = type.DeclaringType;

            while (current != null) {
                parts.Insert(0, current.Name.Replace('`', '$'));
                current = current.DeclaringType;
            }

            var baseNamespace = type.DeclaringType?.Namespace;
            if (!string.IsNullOrEmpty(baseNamespace)) {
                parts.Insert(0, baseNamespace);
            }

            return string.Join(".", parts);
        }

        /// <summary>
        /// Checks if a type is Action or Func delegate
        /// </summary>
        private static bool IsActionOrFunc(Type genericDef, out string kind) {
            var fullName = genericDef.FullName ?? "";

            if (fullName.StartsWith("System.Action`") || fullName == "System.Action") {
                kind = "Action";
                return true;
            }
            if (fullName.StartsWith("System.Func`")) {
                kind = "Func";
                return true;
            }

            kind = null;
            return false;
        }

        /// <summary>
        /// Maps Action/Func to TypeScript function types
        /// For now, we still use the class representation, but could use inline function types
        /// </summary>
        private static TsTypeRef MapActionOrFunc(Type type, string kind) {
            var typeRef = new TsTypeRef {
                IsGeneric = true,
                OriginalType = type
            };

            var genericArgs = type.GetGenericArguments();
            typeRef.Name = kind;
            typeRef.Namespace = "System";

            foreach (var arg in genericArgs) {
                typeRef.GenericArguments.Add(MapType(arg));
            }

            return typeRef;
        }

        /// <summary>
        /// Checks if a type should be skipped during generation
        /// </summary>
        public static bool ShouldSkipType(Type type) {
            if (type == null) return true;

            // Skip pointer types (can't be represented in TS)
            if (type.IsPointer) return true;

            // Skip by-ref-like types (Span<T>, etc.) - they can't be used in JS
            // Note: This check requires reflection on newer framework features
            try {
                var isByRefLike = type.GetCustomAttributes(false)
                    .Any(a => a.GetType().Name == "IsByRefLikeAttribute");
                if (isByRefLike) return true;
            } catch {
                // Ignore if we can't check
            }

            return false;
        }

        /// <summary>
        /// Checks if a type should be emitted as 'any' instead of full type definition.
        /// This handles edge cases that would produce invalid TypeScript.
        /// </summary>
        public static bool ShouldEmitAsAny(Type type) {
            if (type == null) return true;

            var name = type.Name;

            // Compiler-generated types (lambdas, closures, etc.)
            if (name.StartsWith("<") || name.Contains("<>")) return true;

            // Display classes (closure captures)
            if (name.Contains("__DisplayClass")) return true;

            // State machines (async/iterator)
            if (name.Contains(">d__") || name.Contains(">c__")) return true;

            // Fixed buffers with invalid TS identifiers
            if (name.Contains("<") && name.Contains(">e__FixedBuffer")) return true;

            // Check for CompilerGeneratedAttribute
            try {
                var hasCompilerGenerated = type.GetCustomAttributes(false)
                    .Any(a => a.GetType().Name == "CompilerGeneratedAttribute");
                if (hasCompilerGenerated) return true;
            } catch {
                // Ignore if we can't check
            }

            // Check for invalid TypeScript identifier characters in the name
            // TypeScript identifiers cannot contain: < > + = ! @ # etc.
            if (ContainsInvalidTsIdentifierChars(name)) return true;

            return false;
        }

        /// <summary>
        /// Checks if a type name contains characters that are invalid in TypeScript identifiers.
        /// </summary>
        private static bool ContainsInvalidTsIdentifierChars(string name) {
            if (string.IsNullOrEmpty(name)) return false;

            // Characters that are valid in C# type names but not in TS identifiers
            char[] invalidChars = { '<', '>', '+', '=', '!', '@', '#', '&', '*', '(', ')', '[', ']', '{', '}', '|', '\\', '/', '?', ';', ':', '"', '\'' };

            foreach (var c in name) {
                if (Array.IndexOf(invalidChars, c) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Sanitizes a type name to be a valid TypeScript identifier.
        /// </summary>
        public static string SanitizeTypeName(string name) {
            if (string.IsNullOrEmpty(name)) return "Unknown";

            // Replace common patterns
            name = name.Replace('<', '_').Replace('>', '_');
            name = name.Replace('+', '_');
            name = name.Replace('`', '$');

            // Remove any remaining invalid characters
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++) {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '$') {
                    chars[i] = '_';
                }
            }

            name = new string(chars);

            // Ensure it doesn't start with a digit
            if (name.Length > 0 && char.IsDigit(name[0])) {
                name = "_" + name;
            }

            return name;
        }

        /// <summary>
        /// Checks if a member should be skipped
        /// </summary>
        public static bool ShouldSkipMember(System.Reflection.MemberInfo member) {
            if (member == null) return true;

            // Skip compiler-generated members
            if (member.Name.StartsWith("<")) return true;
            if (member.Name.Contains("$")) return true;

            // Check for obsolete with error
            var obsoleteAttr = member.GetCustomAttributes(typeof(ObsoleteAttribute), false)
                .FirstOrDefault() as ObsoleteAttribute;
            if (obsoleteAttr?.IsError == true) return true;

            return false;
        }

        /// <summary>
        /// Gets the TypeScript friendly name for a type (used in identifiers)
        /// </summary>
        public static string GetTsFriendlyName(Type type) {
            if (type == null) return "Unknown";

            var name = type.Name;

            // Remove generic arity suffix
            var tickIndex = name.IndexOf('`');
            if (tickIndex > 0) {
                name = name.Substring(0, tickIndex);
            }

            // Replace invalid characters
            name = name.Replace('+', '_');

            return name;
        }
    }
}
