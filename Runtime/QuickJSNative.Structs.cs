using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Struct Serialization

    /// <summary>
    /// Known Unity struct types that should be serialized directly instead of using handles.
    /// Handles for value types cause issues because boxing creates new objects each time.
    /// </summary>
    static readonly HashSet<Type> _serializableStructTypes = new HashSet<Type> {
        typeof(Vector2),
        typeof(Vector3),
        typeof(Vector4),
        typeof(Vector2Int),
        typeof(Vector3Int),
        typeof(Quaternion),
        typeof(Color),
        typeof(Color32),
        typeof(Rect),
        typeof(RectInt),
        typeof(Bounds),
        typeof(BoundsInt),
        typeof(Matrix4x4),
        typeof(Ray),
        typeof(Ray2D),
        typeof(Plane)
    };

    // MARK: Struct Registry
    static readonly Dictionary<Type, Func<object, string>> _structSerializers = new() {
        [typeof(Vector2)] = o => { var v = (Vector2)o; return $"{{\"__struct\":\"Vector2\",\"x\":{v.x},\"y\":{v.y}}}"; },
        [typeof(Vector3)] = o => { var v = (Vector3)o; return $"{{\"__struct\":\"Vector3\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}"; },
        [typeof(Vector4)] = o => { var v = (Vector4)o; return $"{{\"__struct\":\"Vector4\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z},\"w\":{v.w}}}"; },
        [typeof(Vector2Int)] = o => { var v = (Vector2Int)o; return $"{{\"__struct\":\"Vector2Int\",\"x\":{v.x},\"y\":{v.y}}}"; },
        [typeof(Vector3Int)] = o => { var v = (Vector3Int)o; return $"{{\"__struct\":\"Vector3Int\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}"; },
        [typeof(Quaternion)] = o => { var q = (Quaternion)o; return $"{{\"__struct\":\"Quaternion\",\"x\":{q.x},\"y\":{q.y},\"z\":{q.z},\"w\":{q.w}}}"; },
        [typeof(Color)] = o => { var c = (Color)o; return $"{{\"__struct\":\"Color\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}"; },
        [typeof(Color32)] = o => { var c = (Color32)o; return $"{{\"__struct\":\"Color32\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}"; },
        [typeof(Rect)] = o => { var r = (Rect)o; return $"{{\"__struct\":\"Rect\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}"; },
        [typeof(RectInt)] = o => { var r = (RectInt)o; return $"{{\"__struct\":\"RectInt\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}"; },
        [typeof(Bounds)] = o => { var b = (Bounds)o; return $"{{\"__struct\":\"Bounds\",\"centerX\":{b.center.x},\"centerY\":{b.center.y},\"centerZ\":{b.center.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}"; },
        [typeof(BoundsInt)] = o => { var b = (BoundsInt)o; return $"{{\"__struct\":\"BoundsInt\",\"positionX\":{b.position.x},\"positionY\":{b.position.y},\"positionZ\":{b.position.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}"; },
        [typeof(Matrix4x4)] = o => { var m = (Matrix4x4)o; return $"{{\"__struct\":\"Matrix4x4\",\"m00\":{m.m00},\"m01\":{m.m01},\"m02\":{m.m02},\"m03\":{m.m03},\"m10\":{m.m10},\"m11\":{m.m11},\"m12\":{m.m12},\"m13\":{m.m13},\"m20\":{m.m20},\"m21\":{m.m21},\"m22\":{m.m22},\"m23\":{m.m23},\"m30\":{m.m30},\"m31\":{m.m31},\"m32\":{m.m32},\"m33\":{m.m33}}}"; },
        [typeof(Ray)] = o => { var r = (Ray)o; return $"{{\"__struct\":\"Ray\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"originZ\":{r.origin.z},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y},\"directionZ\":{r.direction.z}}}"; },
        [typeof(Ray2D)] = o => { var r = (Ray2D)o; return $"{{\"__struct\":\"Ray2D\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y}}}"; },
        [typeof(Plane)] = o => { var p = (Plane)o; return $"{{\"__struct\":\"Plane\",\"normalX\":{p.normal.x},\"normalY\":{p.normal.y},\"normalZ\":{p.normal.z},\"distance\":{p.distance}}}"; }
    };

    static readonly Dictionary<Type, Func<string, object>> _structDeserializers = new() {
        [typeof(Vector2)] = json => new Vector2(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":")),
        [typeof(Vector3)] = json => new Vector3(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"), ExtractFloat(json, "\"z\":")),
        [typeof(Vector4)] = json => new Vector4(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"), ExtractFloat(json, "\"z\":"), ExtractFloat(json, "\"w\":")),
        [typeof(Vector2Int)] = json => new Vector2Int(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":")),
        [typeof(Vector3Int)] = json => new Vector3Int(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":"), ExtractInt(json, "\"z\":")),
        [typeof(Quaternion)] = json => new Quaternion(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"), ExtractFloat(json, "\"z\":"), ExtractFloat(json, "\"w\":")),
        [typeof(Color)] = json => new Color(ExtractFloat(json, "\"r\":"), ExtractFloat(json, "\"g\":"), ExtractFloat(json, "\"b\":"), ExtractFloat(json, "\"a\":")),
        [typeof(Color32)] = json => new Color32((byte)ExtractInt(json, "\"r\":"), (byte)ExtractInt(json, "\"g\":"), (byte)ExtractInt(json, "\"b\":"), (byte)ExtractInt(json, "\"a\":")),
        [typeof(Rect)] = json => new Rect(ExtractFloat(json, "\"x\":"), ExtractFloat(json, "\"y\":"), ExtractFloat(json, "\"width\":"), ExtractFloat(json, "\"height\":")),
        [typeof(RectInt)] = json => new RectInt(ExtractInt(json, "\"x\":"), ExtractInt(json, "\"y\":"), ExtractInt(json, "\"width\":"), ExtractInt(json, "\"height\":")),
        [typeof(Bounds)] = json => new Bounds(new Vector3(ExtractFloat(json, "\"centerX\":"), ExtractFloat(json, "\"centerY\":"), ExtractFloat(json, "\"centerZ\":")), new Vector3(ExtractFloat(json, "\"sizeX\":"), ExtractFloat(json, "\"sizeY\":"), ExtractFloat(json, "\"sizeZ\":"))),
        [typeof(BoundsInt)] = json => new BoundsInt(new Vector3Int(ExtractInt(json, "\"positionX\":"), ExtractInt(json, "\"positionY\":"), ExtractInt(json, "\"positionZ\":")), new Vector3Int(ExtractInt(json, "\"sizeX\":"), ExtractInt(json, "\"sizeY\":"), ExtractInt(json, "\"sizeZ\":"))),
        [typeof(Matrix4x4)] = json => {
            var m = new Matrix4x4();
            m.m00 = ExtractFloat(json, "\"m00\":"); m.m01 = ExtractFloat(json, "\"m01\":"); m.m02 = ExtractFloat(json, "\"m02\":"); m.m03 = ExtractFloat(json, "\"m03\":");
            m.m10 = ExtractFloat(json, "\"m10\":"); m.m11 = ExtractFloat(json, "\"m11\":"); m.m12 = ExtractFloat(json, "\"m12\":"); m.m13 = ExtractFloat(json, "\"m13\":");
            m.m20 = ExtractFloat(json, "\"m20\":"); m.m21 = ExtractFloat(json, "\"m21\":"); m.m22 = ExtractFloat(json, "\"m22\":"); m.m23 = ExtractFloat(json, "\"m23\":");
            m.m30 = ExtractFloat(json, "\"m30\":"); m.m31 = ExtractFloat(json, "\"m31\":"); m.m32 = ExtractFloat(json, "\"m32\":"); m.m33 = ExtractFloat(json, "\"m33\":");
            return m;
        },
        [typeof(Ray)] = json => new Ray(new Vector3(ExtractFloat(json, "\"originX\":"), ExtractFloat(json, "\"originY\":"), ExtractFloat(json, "\"originZ\":")), new Vector3(ExtractFloat(json, "\"directionX\":"), ExtractFloat(json, "\"directionY\":"), ExtractFloat(json, "\"directionZ\":"))),
        [typeof(Ray2D)] = json => new Ray2D(new Vector2(ExtractFloat(json, "\"originX\":"), ExtractFloat(json, "\"originY\":")), new Vector2(ExtractFloat(json, "\"directionX\":"), ExtractFloat(json, "\"directionY\":"))),
        [typeof(Plane)] = json => new Plane(new Vector3(ExtractFloat(json, "\"normalX\":"), ExtractFloat(json, "\"normalY\":"), ExtractFloat(json, "\"normalZ\":")), ExtractFloat(json, "\"distance\":"))
    };

    /// <summary>
    /// Serializes a Unity struct value to a JSON string for transfer to JS.
    /// JS will deserialize this back into a plain object.
    /// </summary>
    static string SerializeStructToJson(object value) {
        return _structSerializers.TryGetValue(value.GetType(), out var fn) ? fn(value) : null;
    }

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
        return float.TryParse(json.AsSpan(idx, end - idx), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) 
            ? result : 0f;
    }

    static int ExtractInt(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0;
        idx += key.Length;
        int end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) {
            end++;
        }
        return int.TryParse(json.AsSpan(idx, end - idx), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) 
            ? result : 0;
    }

    /// <summary>
    /// Attempts to convert a value to the target type, with special handling for Unity structs.
    /// This is needed because JS may send plain objects that need to be converted to Vector3, Color, etc.
    /// </summary>
    static object ConvertToTargetType(object value, Type targetType) {
        if (value == null) return null;

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType)) {
            return value;
        }

        // Handle primitive type conversions
        if (targetType.IsPrimitive || targetType == typeof(decimal)) {
            try {
                return Convert.ChangeType(value, targetType);
            } catch {
                return value;
            }
        }

        // Special handling for Unity struct types - the value might already be the correct type
        // if it was created via new CS.UnityEngine.Vector3() in JS
        if (_serializableStructTypes.Contains(targetType) && targetType == valueType) {
            return value;
        }

        // Handle JSON struct strings - these come from JS when passing Unity structs back
        // Format: {"__struct":"Vector3","x":10,"y":20,"z":30}
        if (value is string jsonStr && jsonStr.StartsWith("{\"__struct\":")) {
            var deserialized = DeserializeJsonToStruct(jsonStr, targetType);
            if (deserialized != null) {
                return deserialized;
            }
        }

        return value;
    }
}
