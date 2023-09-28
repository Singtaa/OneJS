using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OneJS.Editor.TSDefinitions
{
    public class TSDefConverter
    {
        private readonly Dictionary<string, string> _typeMapping = new() {
            {"Void", "void"},

            {"Boolean", "boolean"},
            {"Boolean[]", "boolean[]"},

            {"Double", "number"},
            {"Int32", "number"},
            {"UInt32", "number"},
            {"Int64", "number"},
            {"UInt64", "number"},
            {"Int16", "number"},
            {"UInt16", "number"},
            {"Single", "number"},

            {"Double[]", "number[]"},
            {"Int32[]", "number[]"},
            {"UInt32[]", "number[]"},
            {"Int64[]", "number[]"},
            {"UInt64[]", "number[]"},
            {"Int16[]", "number[]"},
            {"UInt16[]", "number[]"},
            {"Single[]", "number[]"},

            {"String", "string"},
            {"String[]", "string[]"},

            {"Object", "any"},
            {"Object[]", "any[]"},

            {"Action", "() => void"},
        };

        public TSDefConverterOptions Options { get; set; }
        public TsDefConverterContext Ctx     { get; set; }

        int _indentSpaces;

        public TSDefConverter(TsDefConverterContext ctx) {
            Ctx     = ctx;
            Options = ctx.Options;

            Ctx.LoadObjectContext();
        }

        public void Indent() {
            _indentSpaces += 4;
        }

        public void Unindent() {
            _indentSpaces -= 4;
        }

        public void ResetIndent() {
            _indentSpaces = 0;
        }

        public string Convert() {
            var objs = Ctx.ObjectContexts.Values.OrderBy(c => c.UnwrapOrder).ToList();

            var results = objs
               .GroupBy(c => c.Type.Namespace)
               .Select(ConvertNamespaceGroups)
               .ToList();

            return string.Join("\n\n", results);
        }

        private string ConvertNamespaceGroups(IGrouping<string, TsDefConverterObjectContext> kv) {
            var lines = new List<string>();
            var ns    = kv.Key!;

            if (Options.IncludeDeclare) {
                lines.Add($"declare module \"{ns.Replace(".", "/")}\" {{");
                Indent();
            }

            lines.AddRange(kv.Select(Convert));

            if (Options.IncludeDeclare) {
                Unindent();
                lines.Add(new string(' ', _indentSpaces) + "}");
            }

            return string.Join("\n", lines.Where(l => l != null));
        }

        public string Convert(TsDefConverterObjectContext ctx) {
            var lines = new List<string>();

            lines.Add($"{ClassDecStr(ctx)} {{");
            Indent();

            lines.AddRange(ctx.StaticProperties.Select(p => PropToStr(ctx, p, true)));

            foreach (var e in ctx.StaticEvents) {
                DoEventLines(ctx, e, lines, true);
            }

            lines.AddRange(ctx.StaticFields.Select(f => FieldToStr(ctx, f, true)));

            lines.AddRange(ctx.StaticMethods.Select(m => MethodToStr(ctx, m, true)));

            lines.AddRange(ctx.Properties.Select(p => PropToStr(ctx, p)));

            foreach (var e in ctx.Events) {
                DoEventLines(ctx, e, lines);
            }

            lines.AddRange(ctx.Fields.Select(f => FieldToStr(ctx, f)));

            lines.AddRange(ctx.Ctors.Select(c => ConstructorToStr(ctx, c)));

            lines.AddRange(ctx.Methods.Select(m => MethodToStr(ctx, m)));

            Unindent();
            lines.Add(new string(' ', _indentSpaces) + "}");

            return string.Join("\n", lines.Where(l => l != null));
        }

        private void DoEventLines(TsDefConverterObjectContext ctx, EventInfo e, List<string> lines, bool isStatic = false) {
            var staticStr = isStatic ? "static " : "";
            if (Options.JintSyntaxForEvents) {
                var str = $"{e.Name}(handler: {TSName(e.EventHandlerType)}): void";
                lines.Add(new string(' ', _indentSpaces) + $"{staticStr}add_" + str);
                lines.Add(new string(' ', _indentSpaces) + $"{staticStr}remove_" + str);
            } else {
                var str = $"{e.Name}: {TSName(e.EventHandlerType)}";
                lines.Add(new string(' ', _indentSpaces) + str);
            }
        }

        private string ClassDecStr(TsDefConverterObjectContext ctx) {
            var type = "class";
            if (ctx.Type.IsInterface)
                type = "interface";
            if (ctx.Type.IsEnum)
                type = "enum";

            var str = $"export {type} {TSName(ctx.Type)}";

            if (!ctx.Type.IsEnum) {
                if (ctx.Type.BaseType != null && ctx.Type.BaseType != typeof(object) && !ctx.Type.IsValueType) {
                    str += $" extends {TSName(ctx.Type.BaseType)}";
                }

                var interfaces = ctx.Type.GetInterfaces();
                if (interfaces.Length > 0) {
                    str += $" implements";
                    var facesStr = string.Join(", ", interfaces.Select(TSName));
                    str += $" {facesStr}";
                }
            }

            return new string(' ', _indentSpaces) + str;
        }

        private string PropToStr(TsDefConverterObjectContext ctx, PropertyInfo propInfo, bool isStatic = false) {
            if (propInfo.CustomAttributes.Any(a => a.AttributeType == typeof(ObsoleteAttribute)))
                return null;
            var str = isStatic ? "static " : "";
            str += $"{propInfo.Name}: {TSName(propInfo.PropertyType)}";

            return new string(' ', _indentSpaces) + str;
        }

        private string FieldToStr(TsDefConverterObjectContext ctx, FieldInfo fieldInfo, bool isStatic = false) {
            if (fieldInfo.CustomAttributes.Any(a => a.AttributeType == typeof(ObsoleteAttribute)))
                return null;
            if (ctx.Type.IsEnum) {
                if (fieldInfo.Name == "value__")
                    return null;
                return new string(' ', _indentSpaces) + fieldInfo.Name + ",";
            }

            var str = isStatic ? "static " : "";
            str += $"{fieldInfo.Name}: {TSName(fieldInfo.FieldType)}";

            return new string(' ', _indentSpaces) + str;
        }

        // string EventToStr(EventInfo eventInfo) {
        //     if (eventInfo.CustomAttributes.Where(a => a.AttributeType == typeof(ObsoleteAttribute)).Count() > 0)
        //         return null;
        //     var str = $"{eventInfo.Name}: {TSName(eventInfo.EventHandlerType)}";
        //
        //     return new String(' ', _indentSpaces) + str;
        // }

        private string MethodToStr(TsDefConverterObjectContext ctx, MethodInfo methodInfo, bool isStatic = false) {
            if (methodInfo.CustomAttributes.Any(a => a.AttributeType == typeof(ObsoleteAttribute)))
                return null;
            if (methodInfo.IsSpecialName)
                return null;
            var builder = new StringBuilder();
            builder.Append(isStatic ? "static " : "");
            builder.Append(methodInfo.Name);
            if (methodInfo.IsGenericMethod) {
                builder.Append("<");
                var argTypes = methodInfo.GetGenericArguments();
                var typeStrs = argTypes.Select(t =>
                {
                    var typeName        = TSName(t);
                    var constraintTypes = t.GetGenericParameterConstraints();

                    if (constraintTypes.Length > 0) {
                        return $"{typeName} extends {string.Join(", ", constraintTypes.Select(TSName))}";
                    }

                    return typeName;
                });
                builder.Append(string.Join(", ", typeStrs));
                builder.Append(">");
            }

            builder.Append("(");

            var parameters    = methodInfo.GetParameters();
            var parameterStrs = parameters.Select(p => $"{p.Name}: {TSName(p.ParameterType)}");
            builder.Append(string.Join(", ", parameterStrs));

            builder.Append($"): {TSName(methodInfo.ReturnType)}");
            return new string(' ', _indentSpaces) + builder;
        }

        private string ConstructorToStr(TsDefConverterObjectContext ctx, ConstructorInfo ctorInfo, bool isStatic = false) {
            if (ctorInfo.IsGenericMethod)
                return null;
            var str = isStatic ? "static " : "";
            str += $"constructor(";

            var parameters = ctorInfo.GetParameters();
            str += string.Join(", ", parameters.Select(p => $"{p.Name}: {TSName(p.ParameterType)}"));

            str += $")";
            return new string(' ', _indentSpaces) + str;
        }


        private string TSName(Type t) {
            if (t.IsGenericParameter) return t.Name;

            // Need to watch out for things like `Span<T>.Enumerator` because it is generic
            // but type.Name only returns "Enumerator"
            var tName = t.Name.Replace("&", "");
            if (!t.IsGenericType || !t.Name.Contains("`"))
                return MapName(tName);

            var genericArgTypes = t.GetGenericArguments().Select(TSName).ToList();

            if (t.Namespace == "System") {
                if (t.Name.StartsWith("Action`")) {
                    return TsFunctionSignature(genericArgTypes, "void");
                }

                if (t.Name.StartsWith("Func`")) {
                    return TsFunctionSignature(genericArgTypes.SkipLast(1), genericArgTypes.Last());
                }

                if (t.Name.StartsWith("Predicate`")) {
                    return TsFunctionSignature(genericArgTypes, "boolean");
                }

                static string TsFunctionSignature(IEnumerable<string> argTypes, string returnType) {
                    var args = string.Join(", ", argTypes.Select((t, i) => $"{(char) (i + 'a')}: {t}"));
                    return $"({args}) => {returnType}";
                }

                if (t.Name.StartsWith("Nullable`")) {
                    return $"{genericArgTypes.First()} | null";
                }

                if (t.Name.StartsWith("ValueTuple`")) {
                    return $"[{string.Join(", ", genericArgTypes)}]";
                }
            }

            tName = tName[..tName.LastIndexOf("`", StringComparison.Ordinal)];
            return $"{tName}<{string.Join(", ", genericArgTypes)}>";
        }

        private string MapName(string typeName) {
            if (_typeMapping.TryGetValue(typeName, out var mappedType))
                return mappedType;
            return typeName;
        }

        public void Debug(TsDefConverterObjectContext ctx) {
            UnityEngine.Debug.Log($"{ctx.Type.Name} has {ctx.Fields.Length} fields, " +
                                  $"{ctx.Methods.Length} methods, " + $"{ctx.Properties.Length} properties, " +
                                  $"{ctx.StaticFields.Length} static fields, " +
                                  $"{ctx.StaticMethods.Length} static methods, " +
                                  $"{ctx.StaticProperties.Length} static properties");
        }

    }
}