using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Analyzes C# types using reflection and produces TsTypeInfo structures
    /// </summary>
    public class TypeAnalyzer {
        private const BindingFlags DefaultFlags =
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        private readonly AnalyzerOptions _options;
        private readonly HashSet<Type> _analyzedTypes = new();
        private readonly Dictionary<Type, List<MethodInfo>> _extensionMethodCache = new();

        public TypeAnalyzer(AnalyzerOptions options = null) {
            _options = options ?? new AnalyzerOptions();
        }

        /// <summary>
        /// Analyzes a single type and returns its TypeScript representation
        /// </summary>
        public TsTypeInfo AnalyzeType(Type type) {
            if (type == null || TypeMapper.ShouldSkipType(type)) {
                return null;
            }

            var info = new TsTypeInfo {
                Name = GetTypeName(type),
                Namespace = GetTypeNamespace(type),
                Kind = GetTypeKind(type),
                OriginalType = type,
                IsAbstract = type.IsAbstract && !type.IsInterface,
                IsSealed = type.IsSealed,
                IsStatic = type.IsAbstract && type.IsSealed, // Static classes are abstract sealed
            };

            // Handle generic type definitions
            if (type.IsGenericTypeDefinition) {
                info.IsGenericTypeDefinition = true;
                info.GenericParameters = type.GetGenericArguments()
                    .Select(t => t.Name)
                    .ToList();
                info.GenericConstraints = AnalyzeGenericConstraints(type.GetGenericArguments());
            }

            // Analyze based on type kind
            switch (info.Kind) {
                case TsTypeKind.Enum:
                    AnalyzeEnum(type, info);
                    break;
                case TsTypeKind.Delegate:
                    AnalyzeDelegate(type, info);
                    break;
                default:
                    AnalyzeClassOrInterface(type, info);
                    break;
            }

            // Check for obsolete attribute
            var obsoleteAttr = type.GetCustomAttribute<ObsoleteAttribute>();
            if (obsoleteAttr != null) {
                info.IsObsolete = true;
                info.ObsoleteMessage = obsoleteAttr.Message;
            }

            _analyzedTypes.Add(type);
            return info;
        }

        /// <summary>
        /// Analyzes multiple types
        /// </summary>
        public List<TsTypeInfo> AnalyzeTypes(IEnumerable<Type> types) {
            var results = new List<TsTypeInfo>();
            foreach (var type in types) {
                var info = AnalyzeType(type);
                if (info != null) {
                    results.Add(info);
                }
            }
            return results;
        }

        private string GetTypeName(Type type) {
            var name = type.Name;
            // Replace backtick for generic types
            var tickIndex = name.IndexOf('`');
            if (tickIndex > 0) {
                name = name.Substring(0, tickIndex);
            }
            return name;
        }

        private string GetTypeNamespace(Type type) {
            if (type.IsNested) {
                var parts = new List<string>();
                var current = type.DeclaringType;
                while (current != null) {
                    parts.Insert(0, GetTypeName(current));
                    current = current.DeclaringType;
                }
                var baseNs = type.DeclaringType?.Namespace;
                if (!string.IsNullOrEmpty(baseNs)) {
                    parts.Insert(0, baseNs);
                }
                return string.Join(".", parts);
            }
            return type.Namespace;
        }

        private TsTypeKind GetTypeKind(Type type) {
            if (type.IsEnum) return TsTypeKind.Enum;
            if (type.IsInterface) return TsTypeKind.Interface;
            if (IsDelegate(type)) return TsTypeKind.Delegate;
            if (type.IsValueType && !type.IsPrimitive) return TsTypeKind.Struct;
            return TsTypeKind.Class;
        }

        private bool IsDelegate(Type type) {
            return typeof(Delegate).IsAssignableFrom(type) &&
                   type != typeof(Delegate) &&
                   type != typeof(MulticastDelegate);
        }

        private void AnalyzeClassOrInterface(Type type, TsTypeInfo info) {
            // Base type
            if (type.BaseType != null &&
                type.BaseType != typeof(object) &&
                type.BaseType != typeof(ValueType)) {
                info.BaseType = TypeMapper.MapType(type.BaseType);
            }

            // Interfaces
            var interfaces = type.GetInterfaces();
            // Filter to only directly implemented interfaces (not inherited)
            if (type.BaseType != null) {
                var baseInterfaces = type.BaseType.GetInterfaces();
                interfaces = interfaces.Except(baseInterfaces).ToArray();
            }
            info.Interfaces = interfaces.Select(TypeMapper.MapType).ToList();

            // Fields
            info.Fields = AnalyzeFields(type);

            // Properties
            info.Properties = AnalyzeProperties(type);

            // Methods (excluding constructors, property accessors, etc.)
            info.Methods = AnalyzeMethods(type);

            // Constructors (for non-abstract, non-static classes)
            if (!type.IsAbstract && !type.IsInterface) {
                info.Constructors = AnalyzeConstructors(type);
            }

            // Events
            info.Events = AnalyzeEvents(type);

            // Indexers
            info.Indexers = AnalyzeIndexers(type);

            // Nested types
            if (_options.IncludeNestedTypes) {
                info.NestedTypes = type.GetNestedTypes(DefaultFlags)
                    .Where(t => !TypeMapper.ShouldSkipType(t))
                    .Select(AnalyzeType)
                    .Where(t => t != null)
                    .ToList();
            }
        }

        private void AnalyzeEnum(Type type, TsTypeInfo info) {
            info.IsEnumFlags = type.GetCustomAttribute<FlagsAttribute>() != null;

            var underlyingType = Enum.GetUnderlyingType(type);
            var values = Enum.GetValues(type);
            var names = Enum.GetNames(type);

            info.EnumMembers = new List<TsEnumMember>();
            for (int i = 0; i < names.Length; i++) {
                var value = Convert.ChangeType(values.GetValue(i), underlyingType);
                info.EnumMembers.Add(new TsEnumMember {
                    Name = names[i],
                    Value = value
                });
            }
        }

        private void AnalyzeDelegate(Type type, TsTypeInfo info) {
            var invokeMethod = type.GetMethod("Invoke");
            if (invokeMethod != null) {
                info.DelegateSignature = AnalyzeMethod(invokeMethod, isDelegate: true);
            }
        }

        private List<TsFieldInfo> AnalyzeFields(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            return type.GetFields(flags)
                .Where(f => !TypeMapper.ShouldSkipMember(f))
                .Where(f => !f.IsSpecialName) // Skip backing fields
                .Where(f => !IsPointerType(f.FieldType))
                .Select(f => new TsFieldInfo {
                    Name = f.Name,
                    Type = TypeMapper.MapType(f.FieldType),
                    IsStatic = f.IsStatic,
                    IsReadOnly = f.IsInitOnly,
                    IsConst = f.IsLiteral,
                    Accessibility = GetAccessibility(f),
                    OriginalField = f,
                    IsObsolete = f.GetCustomAttribute<ObsoleteAttribute>() != null,
                    ObsoleteMessage = f.GetCustomAttribute<ObsoleteAttribute>()?.Message
                })
                .ToList();
        }

        private List<TsPropertyInfo> AnalyzeProperties(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            return type.GetProperties(flags)
                .Where(p => !TypeMapper.ShouldSkipMember(p))
                .Where(p => p.Name != "Item") // Indexers are handled separately
                .Where(p => !IsPointerType(p.PropertyType))
                .Select(p => {
                    var getter = p.GetGetMethod(true);
                    var setter = p.GetSetMethod(true);
                    return new TsPropertyInfo {
                        Name = p.Name,
                        Type = TypeMapper.MapType(p.PropertyType),
                        IsStatic = (getter?.IsStatic ?? setter?.IsStatic) == true,
                        HasGetter = getter != null && (getter.IsPublic || _options.IncludeNonPublic),
                        HasSetter = setter != null && (setter.IsPublic || _options.IncludeNonPublic),
                        Accessibility = GetAccessibility(getter ?? setter),
                        OriginalProperty = p,
                        IsObsolete = p.GetCustomAttribute<ObsoleteAttribute>() != null,
                        ObsoleteMessage = p.GetCustomAttribute<ObsoleteAttribute>()?.Message
                    };
                })
                .Where(p => p.HasGetter || p.HasSetter)
                .ToList();
        }

        private List<TsMethodInfo> AnalyzeMethods(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            return type.GetMethods(flags)
                .Where(m => !TypeMapper.ShouldSkipMember(m))
                .Where(m => !m.IsSpecialName) // Skip property accessors, event methods, etc.
                .Where(m => !IsUnsupportedMethod(m))
                .Select(m => AnalyzeMethod(m))
                .Where(m => m != null)
                .ToList();
        }

        private List<TsMethodInfo> AnalyzeConstructors(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            return type.GetConstructors(flags)
                .Where(c => !TypeMapper.ShouldSkipMember(c))
                .Where(c => !IsUnsupportedConstructor(c))
                .Select(c => AnalyzeConstructor(c))
                .Where(c => c != null)
                .ToList();
        }

        private TsMethodInfo AnalyzeMethod(MethodInfo method, bool isDelegate = false) {
            var info = new TsMethodInfo {
                Name = method.Name,
                ReturnType = TypeMapper.MapType(method.ReturnType),
                IsStatic = method.IsStatic,
                IsAbstract = method.IsAbstract,
                IsVirtual = method.IsVirtual && !method.IsFinal,
                Accessibility = GetAccessibility(method),
                OriginalMethod = method,
                IsObsolete = method.GetCustomAttribute<ObsoleteAttribute>() != null,
                ObsoleteMessage = method.GetCustomAttribute<ObsoleteAttribute>()?.Message
            };

            // Check for extension method
            if (method.IsDefined(typeof(ExtensionAttribute), false)) {
                info.IsExtensionMethod = true;
                var firstParam = method.GetParameters().FirstOrDefault();
                if (firstParam != null) {
                    info.ExtendedType = TypeMapper.MapType(firstParam.ParameterType);
                }
            }

            // Generic method parameters
            if (method.IsGenericMethodDefinition) {
                info.IsGenericMethod = true;
                info.GenericParameters = method.GetGenericArguments()
                    .Select(t => t.Name)
                    .ToList();
                info.GenericConstraints = AnalyzeGenericConstraints(method.GetGenericArguments());
            }

            // Parameters (skip 'this' for extension methods when analyzing for the extended type)
            var parameters = method.GetParameters();
            if (isDelegate || !info.IsExtensionMethod) {
                info.Parameters = parameters.Select(AnalyzeParameter).ToList();
            } else {
                // For extension methods, skip the first 'this' parameter
                info.Parameters = parameters.Skip(1).Select(AnalyzeParameter).ToList();
            }

            return info;
        }

        private TsMethodInfo AnalyzeConstructor(ConstructorInfo constructor) {
            var info = new TsMethodInfo {
                Name = "constructor",
                IsConstructor = true,
                Accessibility = GetAccessibility(constructor),
                OriginalMethod = constructor
            };

            info.Parameters = constructor.GetParameters()
                .Select(AnalyzeParameter)
                .ToList();

            return info;
        }

        private TsParameterInfo AnalyzeParameter(ParameterInfo param) {
            var info = new TsParameterInfo {
                Name = param.Name ?? $"arg{param.Position}",
                Type = TypeMapper.MapType(param.ParameterType),
                IsOptional = param.IsOptional,
                IsParams = param.IsDefined(typeof(ParamArrayAttribute), false),
                IsOut = param.IsOut,
                IsRef = param.ParameterType.IsByRef && !param.IsOut,
                IsIn = param.IsDefined(typeof(InAttribute), false),
                OriginalParameter = param
            };

            // Handle default value
            if (param.HasDefaultValue && param.DefaultValue != null) {
                info.DefaultValue = FormatDefaultValue(param.DefaultValue);
            }

            // Update type ref for out/ref
            if (info.IsOut) {
                info.Type.IsOut = true;
            } else if (info.IsRef) {
                info.Type.IsByRef = true;
            }

            return info;
        }

        private List<TsEventInfo> AnalyzeEvents(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            return type.GetEvents(flags)
                .Where(e => !TypeMapper.ShouldSkipMember(e))
                .Select(e => new TsEventInfo {
                    Name = e.Name,
                    EventHandlerType = TypeMapper.MapType(e.EventHandlerType),
                    IsStatic = e.AddMethod?.IsStatic == true,
                    IsObsolete = e.GetCustomAttribute<ObsoleteAttribute>() != null
                })
                .ToList();
        }

        private List<TsIndexerInfo> AnalyzeIndexers(Type type) {
            var flags = DefaultFlags;
            if (_options.IncludeNonPublic) {
                flags |= BindingFlags.NonPublic;
            }

            // Indexers are properties named "Item" with parameters
            return type.GetProperties(flags)
                .Where(p => p.Name == "Item" && p.GetIndexParameters().Length > 0)
                .Where(p => !TypeMapper.ShouldSkipMember(p))
                .Select(p => {
                    var getter = p.GetGetMethod(true);
                    var setter = p.GetSetMethod(true);
                    return new TsIndexerInfo {
                        Parameters = p.GetIndexParameters().Select(AnalyzeParameter).ToList(),
                        ReturnType = TypeMapper.MapType(p.PropertyType),
                        HasGetter = getter != null && (getter.IsPublic || _options.IncludeNonPublic),
                        HasSetter = setter != null && (setter.IsPublic || _options.IncludeNonPublic)
                    };
                })
                .ToList();
        }

        private List<TsGenericConstraint> AnalyzeGenericConstraints(Type[] genericArgs) {
            var constraints = new List<TsGenericConstraint>();

            foreach (var arg in genericArgs) {
                var constraint = new TsGenericConstraint {
                    ParameterName = arg.Name
                };

                var attrs = arg.GenericParameterAttributes;

                constraint.HasReferenceTypeConstraint =
                    (attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
                constraint.HasValueTypeConstraint =
                    (attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
                constraint.HasDefaultConstructorConstraint =
                    (attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

                // Type constraints (base class or interfaces)
                var typeConstraints = arg.GetGenericParameterConstraints();
                constraint.TypeConstraints = typeConstraints
                    .Where(t => t != typeof(ValueType)) // Skip ValueType for struct constraint
                    .Select(TypeMapper.MapType)
                    .ToList();

                if (constraint.HasConstraints) {
                    constraints.Add(constraint);
                }
            }

            return constraints;
        }

        private TsAccessibility GetAccessibility(MethodBase method) {
            if (method == null) return TsAccessibility.Public;
            if (method.IsPublic) return TsAccessibility.Public;
            if (method.IsFamily) return TsAccessibility.Protected;
            if (method.IsPrivate) return TsAccessibility.Private;
            return TsAccessibility.Internal;
        }

        private TsAccessibility GetAccessibility(FieldInfo field) {
            if (field == null) return TsAccessibility.Public;
            if (field.IsPublic) return TsAccessibility.Public;
            if (field.IsFamily) return TsAccessibility.Protected;
            if (field.IsPrivate) return TsAccessibility.Private;
            return TsAccessibility.Internal;
        }

        private bool IsPointerType(Type type) {
            if (type == null) return false;
            if (type.IsPointer) return true;
            if (type.IsByRef) {
                return IsPointerType(type.GetElementType());
            }
            return false;
        }

        private bool IsUnsupportedMethod(MethodInfo method) {
            // Skip methods with pointer parameters or return types
            if (IsPointerType(method.ReturnType)) return true;
            if (method.GetParameters().Any(p => IsPointerType(p.ParameterType))) return true;

            // Skip generic methods that are not definitions (instantiated generics)
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition) return true;

            return false;
        }

        private bool IsUnsupportedConstructor(ConstructorInfo constructor) {
            // Skip constructors with pointer parameters
            return constructor.GetParameters().Any(p => IsPointerType(p.ParameterType));
        }

        private string FormatDefaultValue(object value) {
            if (value == null) return "null";
            if (value is string s) return $"\"{s}\"";
            if (value is bool b) return b ? "true" : "false";
            if (value is char c) return $"'{c}'";
            return value.ToString();
        }
    }

    /// <summary>
    /// Options for type analysis
    /// </summary>
    public class AnalyzerOptions {
        public bool IncludeNonPublic { get; set; } = false;
        public bool IncludeObsolete { get; set; } = false;
        public bool IncludeNestedTypes { get; set; } = true;
        public bool ResolveExtensionMethods { get; set; } = true;
    }
}
