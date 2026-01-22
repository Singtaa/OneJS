using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Struct Registry
    // Custom serializers take priority over reflection-based generic handling
    static readonly Dictionary<Type, Func<object, string>> _customSerializers = new();
    static readonly Dictionary<Type, Func<Dictionary<string, object>, object>> _customDeserializers = new();

    // Types registered for generic reflection-based handling
    static readonly HashSet<Type> _registeredStructTypes = new();

    // Cache for struct field/property info (performance optimization)
    static readonly ConcurrentDictionary<Type, StructFieldInfo[]> _structFieldCache = new();

    struct StructFieldInfo {
        public string Name;
        public Func<object, object> Getter;
        public Func<object, object, object> Setter; // Returns modified instance for struct copy semantics
        public Type FieldType;
    }

    // MARK: Registration API
    /// <summary>
    /// Registers a struct type for automatic reflection-based serialization.
    /// Call this for any custom struct types you want to pass between JS and C#.
    /// </summary>
    public static void RegisterStructType<T>() where T : struct {
        RegisterStructType(typeof(T));
    }

    public static void RegisterStructType(Type type) {
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum) {
            Debug.LogWarning($"[QuickJS] RegisterStructType: {type.FullName} is not a struct");
            return;
        }
        _registeredStructTypes.Add(type);
        // Pre-cache field info for performance
        GetStructFields(type);
    }

    /// <summary>
    /// Registers a struct type with custom serialization/deserialization handlers.
    /// Use this for structs that need special handling (e.g., private fields, computed properties).
    /// </summary>
    public static void RegisterStructType<T>(
        Func<T, string> serializer,
        Func<Dictionary<string, object>, T> deserializer
    ) where T : struct {
        var type = typeof(T);
        _registeredStructTypes.Add(type);
        _customSerializers[type] = obj => serializer((T)obj);
        _customDeserializers[type] = dict => deserializer(dict);
    }

    /// <summary>
    /// Checks if a type should be serialized as a struct (vs object handle).
    /// Auto-registers unknown struct types on first encounter.
    /// </summary>
    public static bool IsSerializableStruct(Type type) {
        if (type == null || !type.IsValueType || type.IsPrimitive || type.IsEnum) return false;
        EnsureStructsInitialized();

        // Already registered?
        if (_registeredStructTypes.Contains(type) || _customSerializers.ContainsKey(type)) return true;

        // Auto-register any struct type on first encounter
        // This allows user structs to work without explicit registration
        if (ShouldAutoRegister(type)) {
            RegisterStructType(type);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a struct type should be auto-registered.
    /// Filters out types that shouldn't be serialized (internal Unity types, etc.)
    /// </summary>
    static bool ShouldAutoRegister(Type type) {
        // Must be a struct
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum) return false;

        // Skip Nullable<T>
        if (Nullable.GetUnderlyingType(type) != null) return false;

        // Skip compiler-generated types
        if (type.Name.StartsWith("<")) return false;

        // Skip types without public fields/properties (nothing to serialize)
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (fields.Length == 0 && props.Length == 0) return false;

        return true;
    }

    // MARK: Init
    static bool _structsInitialized;

    static void EnsureStructsInitialized() {
        if (_structsInitialized) return;
        _structsInitialized = true;

        // Register common Unity structs
        RegisterStructType<Vector2>();
        RegisterStructType<Vector3>();
        RegisterStructType<Vector4>();
        RegisterStructType<Vector2Int>();
        RegisterStructType<Vector3Int>();
        RegisterStructType<Quaternion>();
        RegisterStructType<Color>();
        RegisterStructType<Color32>();
        RegisterStructType<Rect>();
        RegisterStructType<RectInt>();
        RegisterStructType<Bounds>();
        RegisterStructType<BoundsInt>();
        RegisterStructType<Matrix4x4>();
        RegisterStructType<Ray>();
        RegisterStructType<Ray2D>();
        RegisterStructType<Plane>();
        RegisterStructType<UnityEngine.UIElements.Length>();
        RegisterStructType<UnityEngine.UIElements.Angle>();

        // Style types need custom handling due to keyword field
        RegisterStyleTypes();
    }

    static void RegisterStyleTypes() {
        // StyleLength - has keyword that changes semantics
        RegisterStructType<UnityEngine.UIElements.StyleLength>(
            sl => {
                if (sl.keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                    return
                        $"{{\"__type\":\"UnityEngine.UIElements.StyleLength\",\"keyword\":{(int)sl.keyword}}}";
                var l = sl.value;
                return
                    $"{{\"__type\":\"UnityEngine.UIElements.StyleLength\",\"value\":{F(l.value)},\"unit\":{(int)l.unit}}}";
            },
            dict => {
                if (dict.TryGetValue("keyword", out var kw) && Convert.ToInt32(kw) != 0)
                    return new UnityEngine.UIElements.StyleLength(
                        (UnityEngine.UIElements.StyleKeyword)Convert.ToInt32(kw));
                var val = dict.TryGetValue("value", out var v) ? Convert.ToSingle(v) : 0f;
                var unit = dict.TryGetValue("unit", out var u)
                    ? (UnityEngine.UIElements.LengthUnit)Convert.ToInt32(u)
                    : default;
                return new UnityEngine.UIElements.StyleLength(new UnityEngine.UIElements.Length(val, unit));
            }
        );

        RegisterStructType<UnityEngine.UIElements.StyleFloat>(
            sf =>
                $"{{\"__type\":\"UnityEngine.UIElements.StyleFloat\",\"value\":{F(sf.value)},\"keyword\":{(int)sf.keyword}}}",
            dict => {
                var val = dict.TryGetValue("value", out var v) ? Convert.ToSingle(v) : 0f;
                var kw = dict.TryGetValue("keyword", out var k)
                    ? (UnityEngine.UIElements.StyleKeyword)Convert.ToInt32(k)
                    : default;
                if (kw != UnityEngine.UIElements.StyleKeyword.Undefined)
                    return new UnityEngine.UIElements.StyleFloat(kw);
                return new UnityEngine.UIElements.StyleFloat(val);
            }
        );

        RegisterStructType<UnityEngine.UIElements.StyleInt>(
            si =>
                $"{{\"__type\":\"UnityEngine.UIElements.StyleInt\",\"value\":{si.value},\"keyword\":{(int)si.keyword}}}",
            dict => {
                var val = dict.TryGetValue("value", out var v) ? Convert.ToInt32(v) : 0;
                var kw = dict.TryGetValue("keyword", out var k)
                    ? (UnityEngine.UIElements.StyleKeyword)Convert.ToInt32(k)
                    : default;
                if (kw != UnityEngine.UIElements.StyleKeyword.Undefined)
                    return new UnityEngine.UIElements.StyleInt(kw);
                return new UnityEngine.UIElements.StyleInt(val);
            }
        );

        RegisterStructType<UnityEngine.UIElements.StyleColor>(
            sc => {
                var c = sc.value;
                return
                    $"{{\"__type\":\"UnityEngine.UIElements.StyleColor\",\"r\":{F(c.r)},\"g\":{F(c.g)},\"b\":{F(c.b)},\"a\":{F(c.a)},\"keyword\":{(int)sc.keyword}}}";
            },
            dict => {
                var kw = dict.TryGetValue("keyword", out var k)
                    ? (UnityEngine.UIElements.StyleKeyword)Convert.ToInt32(k)
                    : default;
                if (kw != UnityEngine.UIElements.StyleKeyword.Undefined)
                    return new UnityEngine.UIElements.StyleColor(kw);
                var r = dict.TryGetValue("r", out var rv) ? Convert.ToSingle(rv) : 0f;
                var g = dict.TryGetValue("g", out var gv) ? Convert.ToSingle(gv) : 0f;
                var b = dict.TryGetValue("b", out var bv) ? Convert.ToSingle(bv) : 0f;
                var a = dict.TryGetValue("a", out var av) ? Convert.ToSingle(av) : 1f;
                return new UnityEngine.UIElements.StyleColor(new Color(r, g, b, a));
            }
        );
    }

    // Float formatting helper - invariant culture, no trailing zeros
    static string F(float v) => v.ToString("G", CultureInfo.InvariantCulture);
    static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    // MARK: Serialization (C# -> JS)
    /// <summary>
    /// Serializes a struct to JSON string for transfer to JS.
    /// Returns null if the type is not a serializable struct type.
    /// </summary>
    public static string SerializeStruct(object value) {
        if (value == null) return null;

        var type = value.GetType();

        // This call handles initialization and auto-registration
        if (!IsSerializableStruct(type)) return null;

        // Custom serializer takes priority
        if (_customSerializers.TryGetValue(type, out var customSerializer)) {
            return customSerializer(value);
        }

        // Generic reflection-based serialization
        return SerializeStructGeneric(value, type);
    }

    static string SerializeStructGeneric(object value, Type type) {
        var fields = GetStructFields(type);
        var sb = new StringBuilder(128);
        sb.Append("{\"__type\":\"");
        sb.Append(type.FullName);
        sb.Append('"');

        for (int i = 0; i < fields.Length; i++) {
            var field = fields[i];
            var fieldValue = field.Getter(value);
            sb.Append(",\"");
            sb.Append(char.ToLowerInvariant(field.Name[0]));
            if (field.Name.Length > 1) sb.Append(field.Name, 1, field.Name.Length - 1);
            sb.Append("\":");
            AppendJsonValue(sb, fieldValue, field.FieldType);
        }

        sb.Append('}');
        return sb.ToString();
    }

    static void AppendJsonValue(StringBuilder sb, object value, Type type) {
        if (value == null) {
            sb.Append("null");
            return;
        }

        if (type == typeof(float)) {
            sb.Append(F((float)value));
        } else if (type == typeof(double)) {
            sb.Append(F((double)value));
        } else if (type == typeof(int) || type == typeof(short) || type == typeof(byte) ||
                   type == typeof(sbyte) || type == typeof(ushort)) {
            sb.Append(Convert.ToInt32(value));
        } else if (type == typeof(long) || type == typeof(uint)) {
            sb.Append(Convert.ToInt64(value));
        } else if (type == typeof(bool)) {
            sb.Append((bool)value ? "true" : "false");
        } else if (type == typeof(string)) {
            sb.Append('"');
            sb.Append(EscapeJsonString((string)value));
            sb.Append('"');
        } else if (type.IsEnum) {
            sb.Append(Convert.ToInt32(value));
        } else if (IsSerializableStruct(type)) {
            // Nested struct
            sb.Append(SerializeStruct(value));
        } else {
            // Fallback to ToString
            sb.Append('"');
            sb.Append(EscapeJsonString(value.ToString()));
            sb.Append('"');
        }
    }

    static string EscapeJsonString(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        // Simple escape - extend if needed
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    // MARK: Deserialization (JS -> C#)
    /// <summary>
    /// Deserializes a JSON string to a struct of the target type.
    /// Used when JS passes struct data back to C# and we know the expected type.
    /// </summary>
    public static object DeserializeStruct(string json, Type targetType) {
        if (string.IsNullOrEmpty(json) || targetType == null) return null;

        var dict = ParseSimpleJson(json);
        if (dict == null) return null;

        return DeserializeFromDict(dict, targetType);
    }

    /// <summary>
    /// Deserializes from a pre-parsed dictionary to target type.
    /// This is the main entry point when C# receives a plain object from JS.
    /// </summary>
    public static object DeserializeFromDict(Dictionary<string, object> dict, Type targetType) {
        if (dict == null || targetType == null) return null;

        // This call handles initialization and auto-registration
        if (!IsSerializableStruct(targetType)) return null;

        // Custom deserializer takes priority
        if (_customDeserializers.TryGetValue(targetType, out var customDeserializer)) {
            return customDeserializer(dict);
        }

        // If dict specifies __type and it's a registered type, use that
        if (dict.TryGetValue("__type", out var typeNameObj) && typeNameObj is string typeName) {
            var specifiedType = ResolveType(typeName);
            if (specifiedType != null && targetType.IsAssignableFrom(specifiedType)) {
                targetType = specifiedType;
            }
        }

        // Generic reflection-based deserialization
        return DeserializeStructGeneric(dict, targetType);
    }

    static object DeserializeStructGeneric(Dictionary<string, object> dict, Type type) {
        var fields = GetStructFields(type);
        object instance = Activator.CreateInstance(type);

        for (int i = 0; i < fields.Length; i++) {
            var field = fields[i];
            // Try lowercase first (JS convention), then original name
            var lowerName = char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);

            object rawValue = null;
            if (!dict.TryGetValue(lowerName, out rawValue) && !dict.TryGetValue(field.Name, out rawValue)) {
                continue;
            }

            var convertedValue = ConvertJsonValue(rawValue, field.FieldType);
            if (convertedValue != null) {
                instance = field.Setter(instance, convertedValue);
            }
        }

        return instance;
    }

    static object ConvertJsonValue(object value, Type targetType) {
        if (value == null) return null;

        var sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType)) return value;

        // Handle nested dictionary (nested struct from JS)
        if (value is Dictionary<string, object> nestedDict && IsSerializableStruct(targetType)) {
            return DeserializeFromDict(nestedDict, targetType);
        }

        // Numeric conversions
        if (IsNumericType(sourceType) && (IsNumericType(targetType) || targetType.IsEnum)) {
            if (targetType.IsEnum) {
                return Enum.ToObject(targetType, Convert.ToInt64(value));
            }
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        // String to enum
        if (sourceType == typeof(string) && targetType.IsEnum) {
            return Enum.Parse(targetType, (string)value);
        }

        return value;
    }

    // MARK: Field Cache
    static StructFieldInfo[] GetStructFields(Type type) {
        if (_structFieldCache.TryGetValue(type, out var cached)) return cached;

        var list = new List<StructFieldInfo>();

        // Get public instance fields
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var f in fields) {
            if (f.IsStatic || f.IsLiteral) continue;
            var fieldCopy = f;
            list.Add(new StructFieldInfo {
                Name = f.Name,
                FieldType = f.FieldType,
                Getter = obj => fieldCopy.GetValue(obj),
                Setter = (obj, val) => {
                    // For structs: unbox, set, return modified copy
                    fieldCopy.SetValue(obj, val);
                    return obj;
                }
            });
        }

        // Get public instance properties with both getter and setter
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var p in props) {
            if (!p.CanRead || !p.CanWrite) continue;
            if (p.GetIndexParameters().Length > 0) continue;
            if (list.Exists(f => string.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase))) continue;

            var propCopy = p;
            list.Add(new StructFieldInfo {
                Name = p.Name,
                FieldType = p.PropertyType,
                Getter = obj => propCopy.GetValue(obj),
                Setter = (obj, val) => {
                    propCopy.SetValue(obj, val);
                    return obj;
                }
            });
        }

        var result = list.ToArray();
        _structFieldCache[type] = result;
        return result;
    }

    // MARK: JSON Parsing
    // Simple JSON parser for struct data - handles numbers, strings, bools, nested objects
    static Dictionary<string, object> ParseSimpleJson(string json) {
        if (string.IsNullOrEmpty(json) || json[0] != '{') return null;

        var dict = new Dictionary<string, object>();
        int i = 1; // Skip opening brace

        while (i < json.Length) {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) break;
            if (json[i] == '}') break;
            if (json[i] == ',') {
                i++;
                continue;
            }

            // Parse key
            if (json[i] != '"') break;
            var key = ParseJsonString(json, ref i);
            if (key == null) break;

            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != ':') break;
            i++; // Skip colon

            SkipWhitespace(json, ref i);
            var value = ParseJsonValue(json, ref i);
            dict[key] = value;
        }

        return dict;
    }

    static object ParseJsonValue(string json, ref int i) {
        if (i >= json.Length) return null;

        char c = json[i];

        if (c == '"') return ParseJsonString(json, ref i);
        if (c == '{') {
            int end = FindMatchingBrace(json, i);
            var subJson = json.Substring(i, end - i + 1);
            i = end + 1;
            return ParseSimpleJson(subJson);
        }
        if (c == '[') {
            // Parse JSON array
            return ParseJsonArray(json, ref i);
        }
        if (c == 't' && i + 4 <= json.Length && json.Substring(i, 4) == "true") {
            i += 4;
            return true;
        }
        if (c == 'f' && i + 5 <= json.Length && json.Substring(i, 5) == "false") {
            i += 5;
            return false;
        }
        if (c == 'n' && i + 4 <= json.Length && json.Substring(i, 4) == "null") {
            i += 4;
            return null;
        }

        // Number
        int start = i;
        bool hasDecimal = false;
        if (json[i] == '-') i++;
        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' ||
                                   json[i] == 'E' || json[i] == '+' || json[i] == '-')) {
            if (json[i] == '.') hasDecimal = true;
            i++;
        }
        var numStr = json.Substring(start, i - start);
        if (hasDecimal || numStr.Contains("e") || numStr.Contains("E")) {
            return double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : 0.0;
        }
        return int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>
    /// Parse a JSON array: [value, value, ...]
    /// </summary>
    static List<object> ParseJsonArray(string json, ref int i) {
        if (i >= json.Length || json[i] != '[') return null;
        i++; // Skip opening bracket

        var result = new List<object>();

        while (i < json.Length) {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) break;
            if (json[i] == ']') {
                i++; // Skip closing bracket
                break;
            }
            if (json[i] == ',') {
                i++;
                continue;
            }

            var value = ParseJsonValue(json, ref i);
            result.Add(value);
        }

        return result;
    }

    static string ParseJsonString(string json, ref int i) {
        if (json[i] != '"') return null;
        i++; // Skip opening quote

        var sb = new StringBuilder();
        while (i < json.Length && json[i] != '"') {
            if (json[i] == '\\' && i + 1 < json.Length) {
                i++;
                switch (json[i]) {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append(json[i]); break;
                }
            } else {
                sb.Append(json[i]);
            }
            i++;
        }
        if (i < json.Length) i++; // Skip closing quote
        return sb.ToString();
    }

    static int FindMatchingBrace(string json, int start) {
        int depth = 0;
        for (int i = start; i < json.Length; i++) {
            if (json[i] == '{') depth++;
            else if (json[i] == '}') {
                depth--;
                if (depth == 0) return i;
            }
        }
        return json.Length - 1;
    }

    static void SkipWhitespace(string json, ref int i) {
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
    }

    // MARK: Type Conversion (updated)
    static readonly ConcurrentDictionary<(Type, Type), MethodInfo> _implicitOpCache = new();

    internal static object ConvertToTargetType(object value, Type targetType) {
        if (value == null) return null;
        EnsureStructsInitialized();

        var sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType)) return value;

        // Vector4 -> Quaternion (from binary packed {x,y,z,w})
        if (sourceType == typeof(Vector4) && targetType == typeof(Quaternion)) {
            var v = (Vector4)value;
            return new Quaternion(v.x, v.y, v.z, v.w);
        }

        // Vector4 -> Color (from binary packed {r,g,b,a})
        if (sourceType == typeof(Vector4) && targetType == typeof(Color)) {
            var v = (Vector4)value;
            return new Color(v.x, v.y, v.z, v.w);
        }

        // Vector4 -> StyleColor (convert via Color first)
        if (sourceType == typeof(Vector4) && targetType.FullName == "UnityEngine.UIElements.StyleColor") {
            var v = (Vector4)value;
            var color = new Color(v.x, v.y, v.z, v.w);
            return Activator.CreateInstance(targetType, color);
        }

        // Color -> StyleColor (StyleColor has implicit conversion from Color)
        // if (sourceType == typeof(Color) && targetType.FullName == "UnityEngine.UIElements.StyleColor") {
        //     return Activator.CreateInstance(targetType, value);
        // }

        // Vector3 -> Vector3 (already correct type, but handle any edge cases)
        if (sourceType == typeof(Vector3) && targetType == typeof(Vector3)) {
            return value;
        }

        // Vector3 -> Vector2 (C# returns Vector2 as Vector3 for efficiency, convert back)
        if (sourceType == typeof(Vector3) && targetType == typeof(Vector2)) {
            var v = (Vector3)value;
            return new Vector2(v.x, v.y);
        }

        // 1. Array conversions
        if (targetType.IsArray) {
            return ConvertToArray(value, targetType);
        }

        // 2. Dictionary from JS -> System.Type or delegate
        if (value is Dictionary<string, object> dict) {
            // Handle __csTypeRef for AddComponent(MeshFilter) style calls
            if (targetType == typeof(Type) &&
                dict.TryGetValue("__csTypeRef", out var typeRefName) &&
                typeRefName is string tn) {
                return ResolveType(tn);
            }

            // Handle __csCallbackHandle for JS function -> C# delegate conversion
            if (dict.TryGetValue("__csCallbackHandle", out var handleObj) &&
                typeof(Delegate).IsAssignableFrom(targetType)) {
                int callbackHandle = Convert.ToInt32(handleObj);
                return CreateDelegateWrapper(targetType, callbackHandle);
            }

            // Dictionary -> struct
            if (IsSerializableStruct(targetType)) {
                return DeserializeFromDict(dict, targetType);
            }
        }

        // 2. JSON string -> struct (legacy support)
        if (value is string jsonStr && jsonStr.StartsWith("{\"__")) {
            var parsed = ParseSimpleJson(jsonStr);
            if (parsed != null) {
                var deserialized = DeserializeFromDict(parsed, targetType);
                if (deserialized != null && targetType.IsAssignableFrom(deserialized.GetType())) {
                    return deserialized;
                }
            }
        }

        // 3. Try implicit conversion operators
        var converted = TryImplicitConversion(value, sourceType, targetType);
        if (converted != null) return converted;

        // 4. Enum from numeric or string
        if (targetType.IsEnum) {
            if (IsNumericType(sourceType)) {
                return Enum.ToObject(targetType, Convert.ToInt64(value));
            }
            if (sourceType == typeof(string)) {
                var strValue = (string)value;
                try {
                    return Enum.Parse(targetType, strValue, ignoreCase: true);
                } catch {
                    var pascalCase = ConvertToPascalCase(strValue);
                    if (pascalCase != strValue) {
                        try {
                            return Enum.Parse(targetType, pascalCase, ignoreCase: true);
                        } catch { }
                    }
                }
            }
        }

        // 5. StyleEnum<T>
        if (targetType.IsGenericType) {
            var genDef = targetType.GetGenericTypeDefinition();
            if (genDef.FullName == "UnityEngine.UIElements.StyleEnum`1") {
                var enumType = targetType.GetGenericArguments()[0];
                if (IsNumericType(sourceType)) {
                    var enumVal = Enum.ToObject(enumType, Convert.ToInt64(value));
                    return Activator.CreateInstance(targetType, enumVal);
                }
                if (sourceType == enumType) {
                    return Activator.CreateInstance(targetType, value);
                }
                // Handle string -> enum parsing (e.g., "row" -> FlexDirection.Row)
                if (sourceType == typeof(string)) {
                    var strValue = (string)value;
                    try {
                        // Try case-insensitive parse
                        var enumVal = Enum.Parse(enumType, strValue, ignoreCase: true);
                        return Activator.CreateInstance(targetType, enumVal);
                    } catch {
                        // Try PascalCase conversion (e.g., "flex-start" -> "FlexStart")
                        var pascalCase = ConvertToPascalCase(strValue);
                        if (pascalCase != strValue) {
                            try {
                                var enumVal = Enum.Parse(enumType, pascalCase, ignoreCase: true);
                                return Activator.CreateInstance(targetType, enumVal);
                            } catch { }
                        }
                    }
                }
            }
        }

        // 6. Primitive/numeric conversions
        if ((targetType.IsPrimitive || targetType == typeof(decimal)) && IsNumericType(sourceType)) {
            try {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            } catch {
            }
        }

        // 7. Single-parameter constructor fallback
        converted = TryConstructorConversion(value, sourceType, targetType);
        if (converted != null) return converted;

        return value;
    }

    static object TryImplicitConversion(object value, Type sourceType, Type targetType) {
        var cacheKey = (sourceType, targetType);

        if (!_implicitOpCache.TryGetValue(cacheKey, out var method)) {
            method = FindImplicitOperator(sourceType, targetType);
            _implicitOpCache[cacheKey] = method;
        }

        if (method == null) return null;

        try {
            var parmType = method.GetParameters()[0].ParameterType;
            object arg;
            if (parmType.IsAssignableFrom(sourceType)) {
                arg = value;
            } else if (IsNumericType(sourceType) && IsNumericType(parmType)) {
                arg = Convert.ChangeType(value, parmType, CultureInfo.InvariantCulture);
            } else {
                return null;
            }
            return method.Invoke(null, new[] { arg });
        } catch {
            return null;
        }
    }

    static MethodInfo FindImplicitOperator(Type sourceType, Type targetType) {
        var method = FindOpImplicitIn(targetType, sourceType, targetType);
        if (method != null) return method;

        method = FindOpImplicitIn(sourceType, sourceType, targetType);
        if (method != null) return method;

        if (IsNumericType(sourceType)) {
            foreach (var wideType in new[] { typeof(float), typeof(double), typeof(long) }) {
                if (wideType == sourceType) continue;
                method = FindOpImplicitIn(targetType, wideType, targetType);
                if (method != null) return method;
            }
        }

        return null;
    }

    static MethodInfo FindOpImplicitIn(Type searchType, Type paramType, Type returnType) {
        var methods = searchType.GetMethods(BindingFlags.Static | BindingFlags.Public);
        for (int i = 0; i < methods.Length; i++) {
            var m = methods[i];
            if (m.Name != "op_Implicit") continue;
            if (m.ReturnType != returnType) continue;
            var parms = m.GetParameters();
            if (parms.Length != 1) continue;
            if (parms[0].ParameterType == paramType) return m;
        }
        return null;
    }

    static object TryConstructorConversion(object value, Type sourceType, Type targetType) {
        var ctors = targetType.GetConstructors();
        for (int i = 0; i < ctors.Length; i++) {
            var parms = ctors[i].GetParameters();
            if (parms.Length != 1) continue;
            var parmType = parms[0].ParameterType;

            if (parmType.IsAssignableFrom(sourceType)) {
                try {
                    return ctors[i].Invoke(new[] { value });
                } catch {
                    continue;
                }
            }

            if (IsNumericType(sourceType) && IsNumericType(parmType)) {
                try {
                    var conv = Convert.ChangeType(value, parmType, CultureInfo.InvariantCulture);
                    return ctors[i].Invoke(new[] { conv });
                } catch {
                    continue;
                }
            }
        }
        return null;
    }

    static bool IsNumericType(Type t) {
        return t == typeof(int) || t == typeof(float) || t == typeof(double) ||
               t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
               t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) ||
               t == typeof(sbyte) || t == typeof(decimal);
    }

    /// <summary>
    /// Converts kebab-case or lowercase strings to PascalCase for enum parsing.
    /// E.g., "flex-start" -> "FlexStart", "row" -> "Row"
    /// </summary>
    static string ConvertToPascalCase(string s) {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new System.Text.StringBuilder(s.Length);
        bool capitalizeNext = true;

        foreach (char c in s) {
            if (c == '-' || c == '_') {
                capitalizeNext = true;
            } else if (capitalizeNext) {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            } else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // MARK: Array Conversion
    /// <summary>
    /// Convert value to target array type.
    /// Handles conversion from object[], typed arrays, and IEnumerable.
    /// </summary>
    static object ConvertToArray(object value, Type targetArrayType) {
        if (value == null) return null;

        var elementType = targetArrayType.GetElementType();
        if (elementType == null) return value;

        // If already correct type, return as-is
        if (targetArrayType.IsAssignableFrom(value.GetType())) {
            return value;
        }

        // Convert from IEnumerable
        if (value is System.Collections.IEnumerable enumerable) {
            var list = new List<object>();
            foreach (var item in enumerable) {
                list.Add(item);
            }

            var result = Array.CreateInstance(elementType, list.Count);
            for (int i = 0; i < list.Count; i++) {
                var converted = ConvertArrayElement(list[i], elementType);
                result.SetValue(converted, i);
            }
            return result;
        }

        return value;
    }

    /// <summary>
    /// Convert a single array element to the target type.
    /// </summary>
    static object ConvertArrayElement(object value, Type targetType) {
        if (value == null) return null;

        var sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType)) return value;

        // Vector conversions from dictionary or list
        if (targetType == typeof(Vector2)) {
            return ConvertToVector2Element(value);
        }
        if (targetType == typeof(Vector3)) {
            return ConvertToVector3Element(value);
        }
        if (targetType == typeof(Vector4)) {
            return ConvertToVector4Element(value);
        }
        if (targetType == typeof(Color)) {
            return ConvertToColorElement(value);
        }
        if (targetType == typeof(Quaternion)) {
            return ConvertToQuaternionElement(value);
        }

        // Numeric conversions
        if (IsNumericType(targetType) && IsNumericType(sourceType)) {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        // String conversion
        if (targetType == typeof(string)) {
            return value?.ToString();
        }

        // Struct deserialization
        if (value is Dictionary<string, object> dict && IsSerializableStruct(targetType)) {
            return DeserializeFromDict(dict, targetType);
        }

        // Fallback: try generic conversion
        return ConvertToTargetType(value, targetType);
    }

    static Vector2 ConvertToVector2Element(object obj) {
        if (obj is Vector2 v2) return v2;
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            return new Vector2(x, y);
        }
        if (obj is List<object> list && list.Count >= 2) {
            return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
        }
        return Vector2.zero;
    }

    static Vector3 ConvertToVector3Element(object obj) {
        if (obj is Vector3 v3) return v3;
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            float z = dict.TryGetValue("z", out var zv) ? Convert.ToSingle(zv) : 0f;
            return new Vector3(x, y, z);
        }
        if (obj is List<object> list && list.Count >= 3) {
            return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
        }
        return Vector3.zero;
    }

    static Vector4 ConvertToVector4Element(object obj) {
        if (obj is Vector4 v4) return v4;
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            float z = dict.TryGetValue("z", out var zv) ? Convert.ToSingle(zv) : 0f;
            float w = dict.TryGetValue("w", out var wv) ? Convert.ToSingle(wv) : 0f;
            return new Vector4(x, y, z, w);
        }
        if (obj is List<object> list && list.Count >= 4) {
            return new Vector4(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2]), Convert.ToSingle(list[3]));
        }
        return Vector4.zero;
    }

    static Color ConvertToColorElement(object obj) {
        if (obj is Color c) return c;
        if (obj is Dictionary<string, object> dict) {
            float r = dict.TryGetValue("r", out var rv) ? Convert.ToSingle(rv) : 0f;
            float g = dict.TryGetValue("g", out var gv) ? Convert.ToSingle(gv) : 0f;
            float b = dict.TryGetValue("b", out var bv) ? Convert.ToSingle(bv) : 0f;
            float a = dict.TryGetValue("a", out var av) ? Convert.ToSingle(av) : 1f;
            return new Color(r, g, b, a);
        }
        if (obj is List<object> list && list.Count >= 3) {
            float r = Convert.ToSingle(list[0]);
            float g = Convert.ToSingle(list[1]);
            float b = Convert.ToSingle(list[2]);
            float a = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;
            return new Color(r, g, b, a);
        }
        return Color.white;
    }

    static Quaternion ConvertToQuaternionElement(object obj) {
        if (obj is Quaternion q) return q;
        if (obj is Dictionary<string, object> dict) {
            float x = dict.TryGetValue("x", out var xv) ? Convert.ToSingle(xv) : 0f;
            float y = dict.TryGetValue("y", out var yv) ? Convert.ToSingle(yv) : 0f;
            float z = dict.TryGetValue("z", out var zv) ? Convert.ToSingle(zv) : 0f;
            float w = dict.TryGetValue("w", out var wv) ? Convert.ToSingle(wv) : 1f;
            return new Quaternion(x, y, z, w);
        }
        if (obj is List<object> list && list.Count >= 4) {
            return new Quaternion(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2]), Convert.ToSingle(list[3]));
        }
        return Quaternion.identity;
    }

    // MARK: Delegate Wrapper Creation

    /// <summary>
    /// Creates a C# delegate that wraps a JS callback function.
    /// When the delegate is invoked, it calls the JS callback via qjs_invoke_callback.
    /// </summary>
    /// <param name="delegateType">The target delegate type (e.g., Action&lt;T&gt;, Func&lt;T&gt;)</param>
    /// <param name="callbackHandle">The JS callback handle from __registerCallback</param>
    /// <returns>A delegate that invokes the JS callback</returns>
    internal static Delegate CreateDelegateWrapper(Type delegateType, int callbackHandle) {
        if (delegateType == null || callbackHandle < 0) return null;

        // Capture the current context pointer for later invocation
        IntPtr ctxPtr = CurrentContextPtr;
        if (ctxPtr == IntPtr.Zero) {
            Debug.LogWarning("[QuickJS] Cannot create delegate wrapper: no active context");
            return null;
        }

        // Get the delegate's Invoke method to understand the signature
        var invokeMethod = delegateType.GetMethod("Invoke");
        if (invokeMethod == null) return null;

        var parameters = invokeMethod.GetParameters();
        var returnType = invokeMethod.ReturnType;

        // Create wrapper based on delegate signature
        // For Action<T> delegates (no return value)
        if (returnType == typeof(void)) {
            return CreateActionWrapper(delegateType, ctxPtr, callbackHandle, parameters);
        }

        // For Func<T, TResult> delegates (with return value)
        return CreateFuncWrapper(delegateType, ctxPtr, callbackHandle, parameters, returnType);
    }

    static Delegate CreateActionWrapper(Type delegateType, IntPtr ctxPtr, int callbackHandle, ParameterInfo[] parameters) {
        // Special case: Action<MeshGenerationContext> for generateVisualContent
        if (parameters.Length == 1 &&
            parameters[0].ParameterType.FullName == "UnityEngine.UIElements.MeshGenerationContext") {
            return CreateMeshGenerationContextActionWrapper(delegateType, ctxPtr, callbackHandle);
        }

        // Special case: Action<VisualElement, int> for bindItem/unbindItem
        if (parameters.Length == 2 &&
            parameters[0].ParameterType.FullName == "UnityEngine.UIElements.VisualElement" &&
            parameters[1].ParameterType == typeof(int)) {
            return CreateBindItemActionWrapper(ctxPtr, callbackHandle);
        }

        // Special case: Action<VisualElement> for destroyItem
        if (parameters.Length == 1 &&
            parameters[0].ParameterType.FullName == "UnityEngine.UIElements.VisualElement") {
            return CreateDestroyItemActionWrapper(ctxPtr, callbackHandle);
        }

        // Special case: Action (no parameters)
        if (parameters.Length == 0) {
            Action action = () => {
                unsafe {
                    int code = qjs_invoke_callback(ctxPtr, callbackHandle, null, 0, null);
                    if (code != 0) {
                        Debug.LogError($"[QuickJS] Callback invocation failed with code {code}");
                    }
                }
            };
            return action;
        }

        // Generic case: Action<T> with arbitrary parameter
        // For simplicity, we create a generic wrapper that marshals all parameters
        return CreateGenericActionWrapper(delegateType, ctxPtr, callbackHandle, parameters);
    }

    static Delegate CreateMeshGenerationContextActionWrapper(Type delegateType, IntPtr ctxPtr, int callbackHandle) {
        // Create Action<MeshGenerationContext> wrapper
        // The MeshGenerationContext is passed as an object handle to JS
        Action<UnityEngine.UIElements.MeshGenerationContext> action = (mgc) => {
            unsafe {
                // Register the MeshGenerationContext as a handle so JS can access it
                int mgcHandle = RegisterObject(mgc);

                // Create interop value with the handle and type hint
                // Type hint allows JS to know what type this object is for proper proxying
                var arg = new InteropValue {
                    type = InteropType.ObjectHandle,
                    handle = mgcHandle,
                    typeHint = StringToUtf8("UnityEngine.UIElements.MeshGenerationContext")
                };

                InteropValue* args = &arg;
                int code = qjs_invoke_callback(ctxPtr, callbackHandle, args, 1, null);

                if (code != 0) {
                    Debug.LogError($"[QuickJS] generateVisualContent callback failed with code {code}");
                }

                // Note: We don't unregister the handle immediately as JS might still reference it
                // The handle will be released when JS releases it or context is destroyed
            }
        };
        return action;
    }

    static Delegate CreateBindItemActionWrapper(IntPtr ctxPtr, int callbackHandle) {
        // Create Action<VisualElement, int> wrapper for ListView bindItem/unbindItem
        Action<UnityEngine.UIElements.VisualElement, int> action = (element, index) => {
            unsafe {
                // Register the VisualElement as a handle
                int elementHandle = RegisterObject(element);

                // Create interop values for both parameters
                var args = stackalloc InteropValue[2];
                args[0] = new InteropValue {
                    type = InteropType.ObjectHandle,
                    handle = elementHandle,
                    typeHint = StringToUtf8("UnityEngine.UIElements.VisualElement")
                };
                args[1] = new InteropValue {
                    type = InteropType.Int32,
                    i32 = index
                };

                int code = qjs_invoke_callback(ctxPtr, callbackHandle, args, 2, null);

                if (code != 0) {
                    Debug.LogError($"[QuickJS] bindItem callback failed with code {code}");
                }
            }
        };
        return action;
    }

    static Delegate CreateDestroyItemActionWrapper(IntPtr ctxPtr, int callbackHandle) {
        // Create Action<VisualElement> wrapper for ListView destroyItem
        Action<UnityEngine.UIElements.VisualElement> action = (element) => {
            unsafe {
                // Register the VisualElement as a handle
                int elementHandle = RegisterObject(element);

                var arg = new InteropValue {
                    type = InteropType.ObjectHandle,
                    handle = elementHandle,
                    typeHint = StringToUtf8("UnityEngine.UIElements.VisualElement")
                };

                InteropValue* args = &arg;
                int code = qjs_invoke_callback(ctxPtr, callbackHandle, args, 1, null);

                if (code != 0) {
                    Debug.LogError($"[QuickJS] destroyItem callback failed with code {code}");
                }
            }
        };
        return action;
    }

    static Delegate CreateGenericActionWrapper(Type delegateType, IntPtr ctxPtr, int callbackHandle, ParameterInfo[] parameters) {
        // For now, return null for unsupported delegate types
        // This can be extended to support more delegate signatures
        Debug.LogWarning($"[QuickJS] Unsupported delegate type for callback wrapper: {delegateType.Name}");
        return null;
    }

    static Delegate CreateFuncWrapper(Type delegateType, IntPtr ctxPtr, int callbackHandle, ParameterInfo[] parameters, Type returnType) {
        // Special case: Func<VisualElement> for makeItem
        if (parameters.Length == 0 &&
            returnType.FullName == "UnityEngine.UIElements.VisualElement") {
            Func<UnityEngine.UIElements.VisualElement> func = () => {
                unsafe {
                    var result = new InteropValue();
                    int code = qjs_invoke_callback(ctxPtr, callbackHandle, null, 0, &result);

                    if (code != 0) {
                        Debug.LogError($"[QuickJS] makeItem callback failed with code {code}");
                        return null;
                    }

                    // Convert result to VisualElement
                    object resultObj = InteropValueToObject(result);
                    return resultObj as UnityEngine.UIElements.VisualElement;
                }
            };
            return func;
        }

        // For now, return null for unsupported delegate types
        Debug.LogWarning($"[QuickJS] Unsupported Func delegate type for callback wrapper: {delegateType.Name}");
        return null;
    }
}