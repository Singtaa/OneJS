using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Serializes a Unity struct value to a JSON string for transfer to JS.
    /// JS will deserialize this back into a plain object.
    /// </summary>
    static string SerializeStructToJson(object value) {
        var t = value.GetType();

        // Vector2
        if (t == typeof(Vector2)) {
            var v = (Vector2)value;
            return $"{{\"__struct\":\"Vector2\",\"x\":{v.x},\"y\":{v.y}}}";
        }
        // Vector3
        if (t == typeof(Vector3)) {
            var v = (Vector3)value;
            return $"{{\"__struct\":\"Vector3\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        }
        // Vector4
        if (t == typeof(Vector4)) {
            var v = (Vector4)value;
            return $"{{\"__struct\":\"Vector4\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z},\"w\":{v.w}}}";
        }
        // Vector2Int
        if (t == typeof(Vector2Int)) {
            var v = (Vector2Int)value;
            return $"{{\"__struct\":\"Vector2Int\",\"x\":{v.x},\"y\":{v.y}}}";
        }
        // Vector3Int
        if (t == typeof(Vector3Int)) {
            var v = (Vector3Int)value;
            return $"{{\"__struct\":\"Vector3Int\",\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";
        }
        // Quaternion
        if (t == typeof(Quaternion)) {
            var q = (Quaternion)value;
            return $"{{\"__struct\":\"Quaternion\",\"x\":{q.x},\"y\":{q.y},\"z\":{q.z},\"w\":{q.w}}}";
        }
        // Color
        if (t == typeof(Color)) {
            var c = (Color)value;
            return $"{{\"__struct\":\"Color\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        }
        // Color32
        if (t == typeof(Color32)) {
            var c = (Color32)value;
            return $"{{\"__struct\":\"Color32\",\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";
        }
        // Rect
        if (t == typeof(Rect)) {
            var r = (Rect)value;
            return
                $"{{\"__struct\":\"Rect\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        }
        // RectInt
        if (t == typeof(RectInt)) {
            var r = (RectInt)value;
            return
                $"{{\"__struct\":\"RectInt\",\"x\":{r.x},\"y\":{r.y},\"width\":{r.width},\"height\":{r.height}}}";
        }
        // Bounds
        if (t == typeof(Bounds)) {
            var b = (Bounds)value;
            return
                $"{{\"__struct\":\"Bounds\",\"centerX\":{b.center.x},\"centerY\":{b.center.y},\"centerZ\":{b.center.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        }
        // BoundsInt
        if (t == typeof(BoundsInt)) {
            var b = (BoundsInt)value;
            return
                $"{{\"__struct\":\"BoundsInt\",\"positionX\":{b.position.x},\"positionY\":{b.position.y},\"positionZ\":{b.position.z},\"sizeX\":{b.size.x},\"sizeY\":{b.size.y},\"sizeZ\":{b.size.z}}}";
        }
        // Matrix4x4
        if (t == typeof(Matrix4x4)) {
            var m = (Matrix4x4)value;
            return
                $"{{\"__struct\":\"Matrix4x4\",\"m00\":{m.m00},\"m01\":{m.m01},\"m02\":{m.m02},\"m03\":{m.m03},\"m10\":{m.m10},\"m11\":{m.m11},\"m12\":{m.m12},\"m13\":{m.m13},\"m20\":{m.m20},\"m21\":{m.m21},\"m22\":{m.m22},\"m23\":{m.m23},\"m30\":{m.m30},\"m31\":{m.m31},\"m32\":{m.m32},\"m33\":{m.m33}}}";
        }
        // Ray
        if (t == typeof(Ray)) {
            var r = (Ray)value;
            return
                $"{{\"__struct\":\"Ray\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"originZ\":{r.origin.z},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y},\"directionZ\":{r.direction.z}}}";
        }
        // Ray2D
        if (t == typeof(Ray2D)) {
            var r = (Ray2D)value;
            return
                $"{{\"__struct\":\"Ray2D\",\"originX\":{r.origin.x},\"originY\":{r.origin.y},\"directionX\":{r.direction.x},\"directionY\":{r.direction.y}}}";
        }
        // Plane
        if (t == typeof(Plane)) {
            var p = (Plane)value;
            return
                $"{{\"__struct\":\"Plane\",\"normalX\":{p.normal.x},\"normalY\":{p.normal.y},\"normalZ\":{p.normal.z},\"distance\":{p.distance}}}";
        }

        // Fallback for unknown structs - use reflection
        return null;
    }

    /// <summary>
    /// Deserializes a JSON string with __struct marker back to a Unity struct.
    /// This is the inverse of SerializeStructToJson.
    /// </summary>
    static object DeserializeJsonToStruct(string json, Type targetType) {
        if (string.IsNullOrEmpty(json)) return null;

        try {
            // Simple JSON parsing without external dependencies
            // Extract values using string manipulation (faster than full JSON parser for simple structs)

            if (targetType == typeof(Vector2)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                return new Vector2(x, y);
            }
            if (targetType == typeof(Vector3)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                return new Vector3(x, y, z);
            }
            if (targetType == typeof(Vector4)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                float w = ExtractFloat(json, "\"w\":");
                return new Vector4(x, y, z, w);
            }
            if (targetType == typeof(Vector2Int)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                return new Vector2Int(x, y);
            }
            if (targetType == typeof(Vector3Int)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                int z = ExtractInt(json, "\"z\":");
                return new Vector3Int(x, y, z);
            }
            if (targetType == typeof(Quaternion)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float z = ExtractFloat(json, "\"z\":");
                float w = ExtractFloat(json, "\"w\":");
                return new Quaternion(x, y, z, w);
            }
            if (targetType == typeof(Color)) {
                float r = ExtractFloat(json, "\"r\":");
                float g = ExtractFloat(json, "\"g\":");
                float b = ExtractFloat(json, "\"b\":");
                float a = ExtractFloat(json, "\"a\":");
                return new Color(r, g, b, a);
            }
            if (targetType == typeof(Color32)) {
                byte r = (byte)ExtractInt(json, "\"r\":");
                byte g = (byte)ExtractInt(json, "\"g\":");
                byte b = (byte)ExtractInt(json, "\"b\":");
                byte a = (byte)ExtractInt(json, "\"a\":");
                return new Color32(r, g, b, a);
            }
            if (targetType == typeof(Rect)) {
                float x = ExtractFloat(json, "\"x\":");
                float y = ExtractFloat(json, "\"y\":");
                float w = ExtractFloat(json, "\"width\":");
                float h = ExtractFloat(json, "\"height\":");
                return new Rect(x, y, w, h);
            }
            if (targetType == typeof(RectInt)) {
                int x = ExtractInt(json, "\"x\":");
                int y = ExtractInt(json, "\"y\":");
                int w = ExtractInt(json, "\"width\":");
                int h = ExtractInt(json, "\"height\":");
                return new RectInt(x, y, w, h);
            }
            if (targetType == typeof(Bounds)) {
                float cx = ExtractFloat(json, "\"centerX\":");
                float cy = ExtractFloat(json, "\"centerY\":");
                float cz = ExtractFloat(json, "\"centerZ\":");
                float sx = ExtractFloat(json, "\"sizeX\":");
                float sy = ExtractFloat(json, "\"sizeY\":");
                float sz = ExtractFloat(json, "\"sizeZ\":");
                return new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
            }
            if (targetType == typeof(BoundsInt)) {
                int px = ExtractInt(json, "\"positionX\":");
                int py = ExtractInt(json, "\"positionY\":");
                int pz = ExtractInt(json, "\"positionZ\":");
                int sx = ExtractInt(json, "\"sizeX\":");
                int sy = ExtractInt(json, "\"sizeY\":");
                int sz = ExtractInt(json, "\"sizeZ\":");
                return new BoundsInt(new Vector3Int(px, py, pz), new Vector3Int(sx, sy, sz));
            }
            if (targetType == typeof(Ray)) {
                float ox = ExtractFloat(json, "\"originX\":");
                float oy = ExtractFloat(json, "\"originY\":");
                float oz = ExtractFloat(json, "\"originZ\":");
                float dx = ExtractFloat(json, "\"directionX\":");
                float dy = ExtractFloat(json, "\"directionY\":");
                float dz = ExtractFloat(json, "\"directionZ\":");
                return new Ray(new Vector3(ox, oy, oz), new Vector3(dx, dy, dz));
            }
            if (targetType == typeof(Ray2D)) {
                float ox = ExtractFloat(json, "\"originX\":");
                float oy = ExtractFloat(json, "\"originY\":");
                float dx = ExtractFloat(json, "\"directionX\":");
                float dy = ExtractFloat(json, "\"directionY\":");
                return new Ray2D(new Vector2(ox, oy), new Vector2(dx, dy));
            }
            if (targetType == typeof(Plane)) {
                float nx = ExtractFloat(json, "\"normalX\":");
                float ny = ExtractFloat(json, "\"normalY\":");
                float nz = ExtractFloat(json, "\"normalZ\":");
                float d = ExtractFloat(json, "\"distance\":");
                return new Plane(new Vector3(nx, ny, nz), d);
            }
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
        while (end < json.Length &&
               (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == 'e' ||
                json[end] == 'E' || json[end] == '+')) {
            end++;
        }
        if (float.TryParse(json.Substring(idx, end - idx), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result)) {
            return result;
        }
        return 0f;
    }

    static int ExtractInt(string json, string key) {
        int idx = json.IndexOf(key);
        if (idx < 0) return 0;
        idx += key.Length;
        int end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) {
            end++;
        }
        if (int.TryParse(json.Substring(idx, end - idx), out int result)) {
            return result;
        }
        return 0;
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

