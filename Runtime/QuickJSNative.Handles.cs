using System;
using System.Collections.Generic;

public static partial class QuickJSNative {
    // MARK: Handle Table
    static int _nextHandle = 1;
    internal static readonly Dictionary<int, object> _handleTable = new Dictionary<int, object>();
    static readonly Dictionary<object, int> _reverseHandleTable = new Dictionary<object, int>();
    internal static readonly object _handleLock = new object();

    internal static int RegisterObject(object obj) {
        if (obj == null) return 0;

        // Value types should not go through the handle table - they should be serialized directly.
        // Boxing creates new objects each time, breaking reverse lookup and causing handle leaks.
        var objType = obj.GetType();
        if (objType.IsValueType && !objType.IsPrimitive && !objType.IsEnum) {
            // This is a struct like Vector3, Color, Quaternion, etc.
            // These should be serialized specially, not registered as handles.
            throw new ArgumentException(
                $"Value types should not be registered as handles: {objType.FullName}. " +
                "Serialize them directly using SetReturnValueForStruct.");
        }

        lock (_handleLock) {
            if (_reverseHandleTable.TryGetValue(obj, out int existingHandle)) {
                return existingHandle;
            }

            // Find next available handle, wrapping safely
            int startHandle = _nextHandle;
            while (_handleTable.ContainsKey(_nextHandle)) {
                _nextHandle++;
                if (_nextHandle <= 0) _nextHandle = 1; // wrap around, skip 0
                if (_nextHandle == startHandle) {
                    throw new InvalidOperationException("Handle table exhausted");
                }
            }

            int handle = _nextHandle++;
            if (_nextHandle <= 0) _nextHandle = 1;

            _handleTable[handle] = obj;
            _reverseHandleTable[obj] = handle;
            return handle;
        }
    }

    static bool UnregisterObject(int handle) {
        if (handle == 0) return false;

        lock (_handleLock) {
            if (_handleTable.TryGetValue(handle, out var obj)) {
                _handleTable.Remove(handle);
                _reverseHandleTable.Remove(obj);
                return true;
            }
            return false;
        }
    }

    internal static object GetObjectByHandle(int handle) {
        if (handle == 0) return null;
        lock (_handleLock) {
            return _handleTable.TryGetValue(handle, out var obj) ? obj : null;
        }
    }

    public static int GetHandleForObject(object obj) {
        if (obj == null) return 0;
        lock (_handleLock) {
            return _reverseHandleTable.TryGetValue(obj, out int handle) ? handle : 0;
        }
    }

    /// <summary>
    /// Returns the number of currently registered object handles.
    /// Useful for debugging memory leaks.
    /// </summary>
    public static int GetHandleCount() {
        lock (_handleLock) {
            return _handleTable.Count;
        }
    }

    /// <summary>
    /// Clears all registered object handles.
    /// Call this when disposing all contexts to prevent memory leaks.
    /// </summary>
    public static void ClearAllHandles() {
        lock (_handleLock) {
            _handleTable.Clear();
            _reverseHandleTable.Clear();
            _nextHandle = 1;
        }
    }
}