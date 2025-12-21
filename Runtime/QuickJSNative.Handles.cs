using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class QuickJSNative {
    // MARK: Handle Table
    static int _nextHandle = 1;
    internal static readonly Dictionary<int, object> _handleTable = new Dictionary<int, object>();
    static readonly Dictionary<object, int> _reverseHandleTable = new Dictionary<object, int>();
    internal static readonly object _handleLock = new object();

    // Handle monitoring thresholds
    const int HandleWarningThreshold = 10000;      // Warn when handles exceed this
    const int HandleCriticalThreshold = 100000;    // Critical warning at this level
    static bool _warningLogged;
    static bool _criticalWarningLogged;
    static int _peakHandleCount;

    public static int RegisterObject(object obj) {
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

            // Track peak and check thresholds
            int count = _handleTable.Count;
            if (count > _peakHandleCount) _peakHandleCount = count;

            // Log warnings at thresholds (only once per threshold to avoid spam)
            if (count >= HandleCriticalThreshold && !_criticalWarningLogged) {
                _criticalWarningLogged = true;
                Debug.LogError(
                    $"[QuickJSNative] CRITICAL: Handle count ({count}) exceeded {HandleCriticalThreshold}. " +
                    "This indicates a severe memory leak. Check for missing Dispose() calls or " +
                    "unreleased C# object references from JavaScript.");
            } else if (count >= HandleWarningThreshold && !_warningLogged) {
                _warningLogged = true;
                Debug.LogWarning(
                    $"[QuickJSNative] Handle count ({count}) exceeded {HandleWarningThreshold}. " +
                    "This may indicate a memory leak. Consider calling RunGC() more frequently " +
                    "or checking for unreleased object references.");
            }

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
            // Reset warning flags so they can trigger again if handles grow again
            _warningLogged = false;
            _criticalWarningLogged = false;
        }
    }

    /// <summary>
    /// Returns the peak handle count since last reset.
    /// Useful for debugging memory usage patterns.
    /// </summary>
    public static int GetPeakHandleCount() {
        lock (_handleLock) {
            return _peakHandleCount;
        }
    }

    /// <summary>
    /// Resets handle monitoring statistics.
    /// Call this after fixing a memory leak to re-enable warnings.
    /// </summary>
    public static void ResetHandleMonitoring() {
        lock (_handleLock) {
            _peakHandleCount = _handleTable.Count;
            _warningLogged = false;
            _criticalWarningLogged = false;
        }
    }
}