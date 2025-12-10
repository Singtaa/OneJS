using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Struct Registry
    static readonly Dictionary<Type, Func<object, string>> _structSerializers = new() {
        [typeof(Vector2)] = o => {
            var v = (Vector2)o;
            return $"{{\"__struct\":\"Vector2\",\"x\":{v.x},\"y\":{v.y}}}";
        },
        [typeof(Vector3)] = o => {
            var v = (Vector3)o;
            return $"{{\"__struct\":\"Vector3\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        },
        [typeof(Vector4)] = o => {
            var v = (Vector4)o;
            return $"{{\"__struct\":\"Vector4\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z},\"w\":{v.w}}}";
        },
        [typeof(Vector2Int)] = o => {
            var v = (Vector2Int)o;
            return $"{{\"__struct\":\"Vector2Int\",\"x\":{v.x},\"y\":{v.y}}}";
        },
        [typeof(Vector3Int)] = o => {
            var v = (Vector3Int)o;
            return $"{{\"__struct\":\"Vector3Int\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        },
        [typeof(Quaternion)] = o => {
            var q = (Quaternion)o;
            return $"{{\"__struct\":\"Quaternion\",\"x\":{q.x},\"y\":{q.y},\"z\":{q.z},\"w\":{q.w}}}";
        },
        [typeof(Color)] = o => {
            var c = (Color)o;
            return $"{{\"__struct\":\"Color\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        },
        [typeof(Color32)] = o => {
            var c = (Color32)o;
            return $"{{\"__struct\":\"Color32\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        },
        [typeof(Rect)] = o => {
            var r = (Rect)o;
            return
                $"{{\"__struct\":\"Rect\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        },
        [typeof(RectInt)] = o => {
            var r = (RectInt)o;
            return
                $"{{\"__struct\":\"RectInt\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        },
        [typeof(Bounds)] = o => {
            var b = (Bounds)o;
            return
                $"{{\"__struct\":\"Bounds\",\"centerX\":{b.center.x},\"centerY\":{b.center.y},\"centerZ\":{b.center.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        },
        [typeof(BoundsInt)] = o => {
            var b = (BoundsInt)o;
            return
                $"{{\"__struct\":\"BoundsInt\",\"positionX\":{b.position.x},\"positionY\":{b.position.y},\"positionZ\":{b.position.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        },
        [typeof(Matrix4x4)] = o => {
            var m = (Matrix4x4)o;
            return
                $"{{\"__struct\":\"Matrix4x4\",\"m00\":{m.m00},\"m01\":{m.m01},\"m02\":{m.m02},\"m03\":{m.m03},\"m10\":{m.m10},\"m11\":{m.m11},\"m12\":{m.m12},\"m13\":{m.m13},\"m20\":{m.m20},\"m21\":{m.m21},\"m22\":{m.m22},\"m23\":{m.m23},\"m30\":{m.m30},\"m31\":{m.m31},\"m32\":{m.m32},\"m33\":{m.m33}}}";
        },
        [typeof(Ray)] = o => {
            var r = (Ray)o;
            return
                $"{{\"__struct\":\"Ray\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"originZ\":{r.origin.z},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y},\"directionZ\":{r.direction.z}}}";
        },
        [typeof(Ray2D)] = o => {
            var r = (Ray2D)o;
            return
                $"{{\"__struct\":\"Ray2D\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y}}}";
        },
        [typeof(Plane)] = o => {
            var p = (Plane)o;
            return
                $"{{\"__struct\":\"Plane\",\"normalX\":{p.normal.x},\"normalY\":{p.normal.y},\"normalZ\":{p.normal.z},\"distance\":{p.distance}}}";
        },
        [typeof(UnityEngine.UIElements.Length)] = o => {
            var l = (UnityEngine.UIElements.Length)o;
            return $"{{\"__struct\":\"Length\",\"value\":{l.value},\"unit\":{(int)l.unit}}}";
        },
        [typeof(UnityEngine.UIElements.StyleLength)] = o => {
            var sl = (UnityEngine.UIElements.StyleLength)o;
            if (sl.keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                return $"{{\"__struct\":\"StyleLength\",\"keyword\":{(int)sl.keyword}}}";
            var l = sl.value;
            return $"{{\"__struct\":\"StyleLength\",\"value\":{l.value},\"unit\":{(int)l.unit}}}";
        },
        [typeof(UnityEngine.UIElements.StyleFloat)] = o => {
            var sf = (UnityEngine.UIElements.StyleFloat)o;
            return $"{{\"__struct\":\"StyleFloat\",\"value\":{sf.value},\"keyword\":{(int)sf.keyword}}}";
        },
        [typeof(UnityEngine.UIElements.StyleInt)] = o => {
            var si = (UnityEngine.UIElements.StyleInt)o;
            return $"{{\"__struct\":\"StyleInt\",\"value\":{si.value},\"keyword\":{(int)si.keyword}}}";
        },
        [typeof(UnityEngine.UIElements.StyleColor)] = o => {
            var sc = (UnityEngine.UIElements.StyleColor)o;
            var c = sc.value;
            return
                $"{{\"__struct\":\"StyleColor\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a},\"keyword\":{(int)sc.keyword}}}";
        },
    };

    static readonly Dictionary<Type, Func<string, object>> _structDeserializers = new() {
        [typeof(Vector2)] = json => new Vector2(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":")),
        [typeof(Vector3)] = json => new Vector3(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"),
            ExtractFloat(json, "\"z\":")),
        [typeof(Vector4)] = json => new Vector4(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"),
            ExtractFloat(json, "\"z\":"), ExtractFloat(json, "\"w\":")),
        [typeof(Vector2Int)] = json => new Vector2Int(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":")),
        [typeof(Vector3Int)] = json => new Vector3Int(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":"),
            ExtractInt(json, "\"z\":")),
        [typeof(Quaternion)] = json => new Quaternion(ExtractFloat(json, "\"x\":"),
            ExtractFloat(json, "\"y\":"), ExtractFloat(json, "\"z\":"), ExtractFloat(json, "\"w\":")),
        [typeof(Color)] = json => new Color(ExtractFloat(json, "\"r\":"), ExtractFloat(json, "\"g\":"),
            ExtractFloat(json, "\"b\":"), ExtractFloat(json, "\"a\":")),
        [typeof(Color32)] = json => new Color32((byte)ExtractInt(json, "\"r\":"),
            (byte)ExtractInt(json, "\"g\":"), (byte)ExtractInt(json, "\"b\":"),
            (byte)ExtractInt(json, "\"a\":")),
        [typeof(Rect)] = json => new Rect(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"),
            ExtractFloat(json, "\"width\":"), ExtractFloat(json, "\"height\":")),
        [typeof(RectInt)] = json => new RectInt(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":"),
            ExtractInt(json, "\"width\":"), ExtractInt(json, "\"height\":")),
        [typeof(Bounds)] = json =>
            new Bounds(
                new Vector3(ExtractFloat(json, "\"centerX\":"), ExtractFloat(json, "\"centerY\":"),
                    ExtractFloat(json, "\"centerZ\":")),
                new Vector3(ExtractFloat(json, "\"sizeX\":"), ExtractFloat(json, "\"sizeY\":"),
                    ExtractFloat(json, "\"sizeZ\":"))),
        [typeof(BoundsInt)] = json =>
            new BoundsInt(
                new Vector3Int(ExtractInt(json, "\"positionX\":"), ExtractInt(json, "\"positionY\":"),
                    ExtractInt(json, "\"positionZ\":")),
                new Vector3Int(ExtractInt(json, "\"sizeX\":"), ExtractInt(json, "\"sizeY\":"),
                    ExtractInt(json, "\"sizeZ\":"))),
        [typeof(Matrix4x4)] = json => {
            var m = new Matrix4x4();
            m.m00 = ExtractFloat(json, "\"m00\":");
            m.m01 = ExtractFloat(json, "\"m01\":");
            m.m02 = ExtractFloat(json, "\"m02\":");
            m.m03 = ExtractFloat(json, "\"m03\":");
            m.m10 = ExtractFloat(json, "\"m10\":");
            m.m11 = ExtractFloat(json, "\"m11\":");
            m.m12 = ExtractFloat(json, "\"m12\":");
            m.m13 = ExtractFloat(json, "\"m13\":");
            m.m20 = ExtractFloat(json, "\"m20\":");
            m.m21 = ExtractFloat(json, "\"m21\":");
            m.m22 = ExtractFloat(json, "\"m22\":");
            m.m23 = ExtractFloat(json, "\"m23\":");
            m.m30 = ExtractFloat(json, "\"m30\":");
            m.m31 = ExtractFloat(json, "\"m31\":");
            m.m32 = ExtractFloat(json, "\"m32\":");
            m.m33 = ExtractFloat(json, "\"m33\":");
            return m;
        },
        [typeof(Ray)] = json =>
            new Ray(
                new Vector3(ExtractFloat(json, "\"originX\":"), ExtractFloat(json, "\"originY\":"),
                    ExtractFloat(json, "\"originZ\":")),
                new Vector3(ExtractFloat(json, "\"directionX\":"), ExtractFloat(json, "\"directionY\":"),
                    ExtractFloat(json, "\"directionZ\":"))),
        [typeof(Ray2D)] = json =>
            new Ray2D(new Vector2(ExtractFloat(json, "\"originX\":"), ExtractFloat(json, "\"originY\":")),
                new Vector2(ExtractFloat(json, "\"directionX\":"), ExtractFloat(json, "\"directionY\":"))),
        [typeof(Plane)] = json =>
            new Plane(
                new Vector3(ExtractFloat(json, "\"normalX\":"), ExtractFloat(json, "\"normalY\":"),
                    ExtractFloat(json, "\"normalZ\":")), ExtractFloat(json, "\"distance\":")),
        [typeof(UnityEngine.UIElements.Length)] = json => {
            var value = ExtractFloat(json, "\"value\":");
            var unit = (UnityEngine.UIElements.LengthUnit)ExtractInt(json, "\"unit\":");
            return new UnityEngine.UIElements.Length(value, unit);
        },
        [typeof(UnityEngine.UIElements.StyleLength)] = json => {
            var keyword = (UnityEngine.UIElements.StyleKeyword)ExtractInt(json, "\"keyword\":");
            if (keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                return new UnityEngine.UIElements.StyleLength(keyword);
            var value = ExtractFloat(json, "\"value\":");
            var unit = (UnityEngine.UIElements.LengthUnit)ExtractInt(json, "\"unit\":");
            return new UnityEngine.UIElements.StyleLength(new UnityEngine.UIElements.Length(value, unit));
        },
        [typeof(UnityEngine.UIElements.StyleFloat)] = json => {
            var keyword = (UnityEngine.UIElements.StyleKeyword)ExtractInt(json, "\"keyword\":");
            if (keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                return new UnityEngine.UIElements.StyleFloat(keyword);
            return new UnityEngine.UIElements.StyleFloat(ExtractFloat(json, "\"value\":"));
        },
        [typeof(UnityEngine.UIElements.StyleInt)] = json => {
            var keyword = (UnityEngine.UIElements.StyleKeyword)ExtractInt(json, "\"keyword\":");
            if (keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                return new UnityEngine.UIElements.StyleInt(keyword);
            return new UnityEngine.UIElements.StyleInt(ExtractInt(json, "\"value\":"));
        },
        [typeof(UnityEngine.UIElements.StyleColor)] = json => {
            var keyword = (UnityEngine.UIElements.StyleKeyword)ExtractInt(json, "\"keyword\":");
            if (keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                return new UnityEngine.UIElements.StyleColor(keyword);
            var c = new Color(
                ExtractFloat(json, "\"r\":"),
                ExtractFloat(json, "\"g\":"),
                ExtractFloat(json, "\"b\":"),
                ExtractFloat(json, "\"a\":")
            );
            return new UnityEngine.UIElements.StyleColor(c);
        },
    };

    /// <summary>
    /// Deserializes a JSON string with __struct marker back to a Unity struct.
    /// This is the inverse of SerializeStructToJson.
    /// </summary>
    static object DeserializeJsonToStruct(string json, Type targetType) {
        if (string.IsNullOrEmpty(json)) return null;

        try {
            return _structDeserializers.TryGetValue(targetType, out var fn) ? fn(json) : null;
        } catch (Exception ex) {
            Debug.LogWarning($"[QuickJS] Failed to deserialize struct JSON: {ex.Message}");
        }

        return null;
    }

    static float ExtractFloat(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0f;
        idx += key.Length;
        int end = idx;
        while (end < json.Length) {
            char c = json[end];
            if (!char.IsDigit(c) && c != '.' && c != '-' && c != 'e' && c != 'E' && c != '+') break;
            end++;
        }
        return float.TryParse(json.AsSpan(idx, end - idx), NumberStyles.Float, CultureInfo.InvariantCulture,
            out float result)
            ? result
            : 0f;
    }

    static int ExtractInt(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0;
        idx += key.Length;
        int end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) {
            end++;
        }
        return int.TryParse(json.AsSpan(idx, end - idx), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int result)
            ? result
            : 0;
    }


    // MARK: Type Conversion
    static readonly ConcurrentDictionary<(Type, Type), MethodInfo> _implicitOpCache = new();

    static object ConvertToTargetType(object value, Type targetType) {
        if (value == null) return null;

        var sourceType = value.GetType();
        if (targetType.IsAssignableFrom(sourceType)) return value;

        // 1. Handle JSON struct strings first
        if (value is string jsonStr && jsonStr.StartsWith("{\"__struct\":")) {
            var deserialized = DeserializeJsonToStruct(jsonStr, targetType);
            if (deserialized != null) return deserialized;
        }

        // 2. Try implicit conversion operators (op_Implicit)
        var converted = TryImplicitConversion(value, sourceType, targetType);
        if (converted != null) return converted;

        // 3. Enum from numeric
        if (targetType.IsEnum && IsNumericType(sourceType)) {
            return Enum.ToObject(targetType, Convert.ToInt64(value));
        }

        // 4. StyleEnum<T> - convert int to enum, then wrap
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
            }
        }

        // 5. Primitive/numeric conversions
        if ((targetType.IsPrimitive || targetType == typeof(decimal)) && IsNumericType(sourceType)) {
            try {
                return Convert.ChangeType(value, targetType);
            } catch {
                // Fall through
            }
        }

        // 6. Single-parameter constructor fallback
        converted = TryConstructorConversion(value, sourceType, targetType);
        if (converted != null) return converted;

        return value;
    }

    static object TryImplicitConversion(object value, Type sourceType, Type targetType) {
        var cacheKey = (sourceType, targetType);

        if (!_implicitOpCache.TryGetValue(cacheKey, out var method)) {
            method = FindImplicitOperator(sourceType, targetType);
            _implicitOpCache[cacheKey] = method; // Cache even if null
        }

        if (method == null) return null;

        try {
            var parmType = method.GetParameters()[0].ParameterType;
            object arg;
            if (parmType.IsAssignableFrom(sourceType)) {
                arg = value;
            } else if (IsNumericType(sourceType) && IsNumericType(parmType)) {
                arg = Convert.ChangeType(value, parmType);
            } else {
                return null;
            }
            return method.Invoke(null, new[] { arg });
        } catch {
            return null;
        }
    }

    static MethodInfo FindImplicitOperator(Type sourceType, Type targetType) {
        // Check target type for op_Implicit(sourceType) -> targetType
        var method = FindOpImplicitIn(targetType, sourceType, targetType);
        if (method != null) return method;

        // Check source type for op_Implicit(sourceType) -> targetType
        method = FindOpImplicitIn(sourceType, sourceType, targetType);
        if (method != null) return method;

        // Try numeric widening: int -> float -> double
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
                    var converted = Convert.ChangeType(value, parmType);
                    return ctors[i].Invoke(new[] { converted });
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
}