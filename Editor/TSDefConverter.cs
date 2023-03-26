using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OneJS.Editor {
    public class TSDefConverter {
        readonly Dictionary<string, string> _typeMapping = new Dictionary<string, string>() {
            { "Void", "void" },

            { "Boolean", "boolean" },
            { "Boolean[]", "boolean[]" },

            { "Double", "number" },
            { "Int32", "number" },
            { "UInt32", "number" },
            { "Int64", "number" },
            { "UInt64", "number" },
            { "Int16", "number" },
            { "UInt16", "number" },
            { "Single", "number" },

            { "Double[]", "number[]" },
            { "Int32[]", "number[]" },
            { "UInt32[]", "number[]" },
            { "Int64[]", "number[]" },
            { "UInt64[]", "number[]" },
            { "Int16[]", "number[]" },
            { "UInt16[]", "number[]" },
            { "Single[]", "number[]" },

            { "String", "string" },
            { "String[]", "string[]" },

            { "Object", "any" },
            { "Object[]", "any[]" },

            { "Action", "() => void" },
        };

        bool _jintSyntaxForEvents;

        Type _type;
        ConstructorInfo[] _ctors;
        FieldInfo[] _fields;
        EventInfo[] _events;
        MethodInfo[] _methods;
        PropertyInfo[] _properties;
        FieldInfo[] _staticFields;
        EventInfo[] _staticEvents;
        MethodInfo[] _staticMethods;
        PropertyInfo[] _staticProperties;

        List<Type> _referencedTypes;
        string _output;
        int _indentSpaces = 0;

        public TSDefConverter(Type type, bool jintSyntaxForEvents = false) {
            this._type = type;
            this._jintSyntaxForEvents = jintSyntaxForEvents;
            DoMembers();
        }

        public string Convert() {
            var lines = new List<string>();
            lines.Add($"{ClassDecStr()} {{");
            Indent();
            foreach (var p in _staticProperties) {
                lines.Add(PropToStr(p, true));
            }
            foreach (var e in _staticEvents) {
                DoEventLines(e, lines, true);
            }
            foreach (var f in _staticFields) {
                lines.Add(FieldToStr(f, true));
            }
            foreach (var m in _staticMethods) {
                lines.Add(MethodToStr(m, true));
            }
            foreach (var p in _properties) {
                lines.Add(PropToStr(p));
            }
            foreach (var e in _events) {
                DoEventLines(e, lines);
            }
            foreach (var f in _fields) {
                lines.Add(FieldToStr(f));
            }
            foreach (var c in _ctors) {
                lines.Add(ConstructorToStr(c));
            }
            foreach (var m in _methods) {
                lines.Add(MethodToStr(m));
            }
            Unindent();
            lines.Add("}");
            return String.Join("\n", lines.Where(l => l != null));
        }

        void DoEventLines(EventInfo e, List<string> lines, bool isStatic = false) {
            var staticStr = isStatic ? "static " : "";
            if (_jintSyntaxForEvents) {
                var str = $"{e.Name}(handler: {CleanTypeName(e.EventHandlerType)}): void";
                lines.Add(new String(' ', _indentSpaces) + $"{staticStr}add_" + str);
                lines.Add(new String(' ', _indentSpaces) + $"{staticStr}remove_" + str);
            } else {
                var str = $"{e.Name}: {CleanTypeName(e.EventHandlerType)}";
                lines.Add(new String(' ', _indentSpaces) + str);
            }
        }

        public void Debug() {
            DoMembers();
            UnityEngine.Debug.Log($"{_type.Name} has {_fields.Length} fields, " +
                                  $"{_methods.Length} methods, " + $"{_properties.Length} properties, " +
                                  $"{_staticFields.Length} static fields, " +
                                  $"{_staticMethods.Length} static methods, " +
                                  $"{_staticProperties.Length} static properties");
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

        void DoMembers() {
            var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.DeclaredOnly;
            _ctors = _type.GetConstructors(flags | BindingFlags.Instance);
            _fields = _type.GetFields(flags | BindingFlags.Instance);
            _events = _type.GetEvents(flags | BindingFlags.Instance);
            _methods = _type.GetMethods(flags | BindingFlags.Instance);
            _properties = _type.GetProperties(flags | BindingFlags.Instance);
            _staticFields = _type.GetFields(flags | BindingFlags.Static);
            _staticEvents = _type.GetEvents(flags | BindingFlags.Static);
            _staticMethods = _type.GetMethods(flags | BindingFlags.Static);
            _staticProperties = _type.GetProperties(flags | BindingFlags.Static);
        }

        string ClassDecStr() {
            var type = "class";
            if (_type.IsInterface)
                type = "interface";
            if (_type.IsEnum)
                type = "enum";

            var str = $"export {type} {CleanTypeName(_type)}";
            if (!_type.IsEnum) {
                if (_type.BaseType != null && _type.BaseType != typeof(System.Object) && !_type.IsValueType) {
                    str += $" extends {CleanTypeName(_type.BaseType)}";
                }
                var interfaces = _type.GetInterfaces();
                if (interfaces.Length > 0) {
                    str += $" implements";
                    var facesStr = String.Join(", ", interfaces.Select(i => CleanTypeName(i)));
                    str += $" {facesStr}";
                }
            }
            return new String(' ', _indentSpaces) + str;
        }

        string PropToStr(PropertyInfo propInfo, bool isStatic = false) {
            if (propInfo.CustomAttributes.Where(a => a.AttributeType == typeof(ObsoleteAttribute)).Count() > 0)
                return null;
            var str = isStatic ? "static " : "";
            str += $"{propInfo.Name}: {CleanTypeName(propInfo.PropertyType)}";

            return new String(' ', _indentSpaces) + str;
        }

        string FieldToStr(FieldInfo fieldInfo, bool isStatic = false) {
            if (fieldInfo.CustomAttributes.Where(a => a.AttributeType == typeof(ObsoleteAttribute)).Count() > 0)
                return null;
            if (_type.IsEnum) {
                if (fieldInfo.Name == "value__")
                    return null;
                return new String(' ', _indentSpaces) + fieldInfo.Name + ",";
            }
            var str = isStatic ? "static " : "";
            str += $"{fieldInfo.Name}: {CleanTypeName(fieldInfo.FieldType)}";

            return new String(' ', _indentSpaces) + str;
        }

        string EventToStr(EventInfo eventInfo) {
            if (eventInfo.CustomAttributes.Where(a => a.AttributeType == typeof(ObsoleteAttribute)).Count() > 0)
                return null;
            var str = $"{eventInfo.Name}: {CleanTypeName(eventInfo.EventHandlerType)}";

            return new String(' ', _indentSpaces) + str;
        }

        string MethodToStr(MethodInfo methodInfo, bool isStatic = false) {
            if (methodInfo.CustomAttributes.Where(a => a.AttributeType == typeof(ObsoleteAttribute)).Count() > 0)
                return null;
            if (methodInfo.IsSpecialName)
                return null;
            var builder = new StringBuilder();
            builder.Append(isStatic ? "static " : "");
            builder.Append(methodInfo.Name);
            if (methodInfo.IsGenericMethod) {
                builder.Append("<");
                var argTypes = methodInfo.GetGenericArguments();
                var typeStrs = argTypes.Select(t => CleanTypeName(t));
                builder.Append(String.Join(", ", typeStrs));
                builder.Append(">");
            }
            builder.Append("(");

            var parameters = methodInfo.GetParameters();
            var parameterStrs = parameters.Select(p => $"{p.Name}: {CleanTypeName(p.ParameterType)}");
            builder.Append(String.Join(", ", parameterStrs));

            builder.Append($"): {CleanTypeName(methodInfo.ReturnType)}");
            return new String(' ', _indentSpaces) + builder.ToString();
        }

        string ConstructorToStr(ConstructorInfo ctorInfo, bool isStatic = false) {
            if (ctorInfo.IsGenericMethod)
                return null;
            var str = isStatic ? "static " : "";
            str += $"constructor(";

            var parameters = ctorInfo.GetParameters();
            str += String.Join(", ", parameters.Select(p => $"{p.Name}: {CleanTypeName(p.ParameterType)}"));

            str += $")";
            return new String(' ', _indentSpaces) + str;
        }


        string CleanTypeName(Type t) {
            // Need to watch out for things like `Span<T>.Enumerator` because it is generic
            // but type.Name only returns "Enumerator"
            if ((!t.IsGenericType || !t.Name.Contains("`")) && !t.IsByRef)
                return MapName(t.Name.Replace("&", ""));

            StringBuilder sb = new StringBuilder();

            if (t.FullName.StartsWith("System.Action`")) {
                var str = String.Join(", ",
                    t.GetGenericArguments().Select((t, i) => $"{(char)(i + 97)}: " + CleanTypeName(t)));
                return $"({str}) => void";
            }

            if (t.FullName.StartsWith("System.Func`")) {
                var gts = t.GetGenericArguments();
                var str = String.Join(", ",
                    gts.Take(gts.Length - 1).Select((t, i) => $"{(char)(i + 97)}: " + CleanTypeName(t)));
                return $"({str}) => {CleanTypeName(gts.Last())}";
            }

            sb.Append(t.Name.Substring(0, t.Name.LastIndexOf("`")));
            sb.Append(t.GetGenericArguments().Aggregate("<",
                delegate(string aggregate, Type type) {
                    return aggregate + (aggregate == "<" ? "" : ",") + CleanTypeName(type);
                }
            ));
            sb.Append(">");

            return sb.ToString();
        }

        string MapName(string typeName) {
            if (_typeMapping.ContainsKey(typeName))
                return _typeMapping[typeName];
            return typeName;
        }
    }
}