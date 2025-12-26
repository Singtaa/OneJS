using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Managed wrapper for a QuickJS JavaScript execution context.
/// Provides eval, GC control, and callback invocation for JS->C# interop.
/// </summary>
public sealed class QuickJSContext : IDisposable {
    const string DefaultBootstrapResourcePath = "OneJS/QuickJSBootstrap.js";
    const int GCInterval = 100; // Run GC every N evals
    const int HandleCountThreshold = 100; // Also run GC if handles exceed this count
    static string _cachedBootstrap;

    IntPtr _ptr;
    byte[] _buffer;
    bool _disposed;
    int _evalCount;
    bool _bufferOverflowWarned; // Only warn once per context to avoid log spam

    public IntPtr NativePtr => _ptr;

    static string LoadBootstrapFromResources() {
        // if (_cachedBootstrap != null) return _cachedBootstrap;

        var asset = Resources.Load<TextAsset>(DefaultBootstrapResourcePath);
        if (!asset) {
            Debug.LogWarning("[QuickJS] Bootstrap script not found at Resources/" +
                             DefaultBootstrapResourcePath);
            return null;
        }
        _cachedBootstrap = asset.text;
        return _cachedBootstrap;
    }

    public QuickJSContext(int bufferSize = 16 * 1024) {
        _ptr = QuickJSNative.qjs_create();
        if (_ptr == IntPtr.Zero) {
            throw new Exception("qjs_create failed");
        }
        _buffer = new byte[bufferSize];

        // Install JS-side helpers (__cs, wrapObject, newObject, callMethod, callStatic)
        var bootstrap = LoadBootstrapFromResources();
        if (!string.IsNullOrEmpty(bootstrap)) {
            Eval(bootstrap, "quickjs_bootstrap.js");
        }
    }

    public string Eval(string code, string filename = "<input>", int evalFlags = 0) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(QuickJSContext));
        }
        if (_ptr == IntPtr.Zero) {
            throw new InvalidOperationException("QuickJS context is null");
        }

        var handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        try {
            int result = QuickJSNative.qjs_eval(
                _ptr,
                code,
                filename,
                evalFlags,
                handle.AddrOfPinnedObject(),
                _buffer.Length
            );

            int len = 0;
            for (; len < _buffer.Length; len++) {
                if (_buffer[len] == 0) break;
            }

            var str = System.Text.Encoding.UTF8.GetString(_buffer, 0, len);

            if (result != 0) {
                throw new Exception("QuickJS error: " + str);
            }

            // Check for potential buffer overflow (output truncation)
            // If the result fills the entire buffer (or nearly so), it was likely truncated
            if (len >= _buffer.Length - 1 && !_bufferOverflowWarned) {
                _bufferOverflowWarned = true;
                Debug.LogWarning(
                    $"[QuickJSContext] Eval output may have been truncated at {_buffer.Length} bytes. " +
                    "Consider increasing buffer size or avoiding large return values from eval. " +
                    "File: {filename}");
            }

            // Run GC periodically to trigger FinalizationRegistry callbacks
            // Also run if handle count exceeds threshold to prevent leaks from chained property access
            // (e.g., go.transform.position creates intermediate handles that need cleanup)
            _evalCount++;
            if (_evalCount >= GCInterval || QuickJSNative.GetHandleCount() > HandleCountThreshold) {
                _evalCount = 0;
                QuickJSNative.qjs_run_gc(_ptr);
            }

            return str;
        } finally {
            handle.Free();
        }
    }

    public void RunGC() {
        if (_disposed || _ptr == IntPtr.Zero) return;
        QuickJSNative.qjs_run_gc(_ptr);
    }

    /// <summary>
    /// Execute all pending Promise jobs (microtasks).
    /// Must be called periodically to process Promise callbacks and React scheduler work.
    /// Returns the number of jobs executed, or -1 on error.
    /// </summary>
    public int ExecutePendingJobs() {
        if (_disposed || _ptr == IntPtr.Zero) return 0;
        return QuickJSNative.qjs_execute_pending_jobs(_ptr);
    }

    /// <summary>
    /// Runs GC if the handle table exceeds the given threshold.
    /// Call this from Update() if you need more aggressive cleanup.
    /// </summary>
    public void MaybeRunGC(int threshold = 50) {
        if (_disposed || _ptr == IntPtr.Zero) return;
        if (QuickJSNative.GetHandleCount() > threshold) {
            QuickJSNative.qjs_run_gc(_ptr);
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        if (_ptr != IntPtr.Zero) {
            QuickJSNative.qjs_destroy(_ptr);
            _ptr = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~QuickJSContext() {
        // Last line of defense if somebody forgets Dispose
        if (_ptr != IntPtr.Zero) {
            QuickJSNative.qjs_destroy(_ptr);
            _ptr = IntPtr.Zero;
        }
    }

    // MARK: Callbacks (Allocating)
    /// <summary>
    /// Invoke a JS callback with arbitrary arguments. ALLOCATES memory.
    /// For per-frame calls, use the zero-alloc overloads instead.
    /// </summary>
    public object InvokeCallback(int handle, params object[] args) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        unsafe {
            int argCount = args?.Length ?? 0;
            QuickJSNative.InteropValue* nativeArgs = null;
            IntPtr[] stringPtrs = null;

            try {
                if (argCount > 0) {
                    nativeArgs = (QuickJSNative.InteropValue*)Marshal.AllocHGlobal(
                        sizeof(QuickJSNative.InteropValue) * argCount);
                    stringPtrs = new IntPtr[argCount];

                    for (int i = 0; i < argCount; i++) {
                        stringPtrs[i] = IntPtr.Zero;
                        nativeArgs[i] = QuickJSNative.ObjectToInteropValue(args[i], ref stringPtrs[i]);
                    }
                }

                QuickJSNative.InteropValue result = default;
                int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, nativeArgs, argCount, &result);

                if (code != 0) {
                    throw new Exception($"qjs_invoke_callback failed with code {code}");
                }

                object ret = QuickJSNative.InteropValueToObject(result);

                // Free string result if allocated by native
                if (result.type == QuickJSNative.InteropType.String && result.str != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(result.str);
                }

                return ret;
            } finally {
                if (nativeArgs != null) {
                    Marshal.FreeHGlobal((IntPtr)nativeArgs);
                }

                if (stringPtrs != null) {
                    for (int i = 0; i < stringPtrs.Length; i++) {
                        if (stringPtrs[i] != IntPtr.Zero) {
                            Marshal.FreeCoTaskMem(stringPtrs[i]);
                        }
                    }
                }
            }
        }
    }

    // MARK: Zero-Alloc Callbacks
    // Common validation - inlined for performance
    void ThrowIfInvalid() {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");
    }

    unsafe void InvokeAndCheck(int handle, QuickJSNative.InteropValue* args, int count) {
        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, count, null);
        if (code != 0) throw new Exception($"qjs_invoke_callback failed with code {code}");
    }

    // InteropValue helpers for common types
    static QuickJSNative.InteropValue MakeFloat(float v) =>
        new() { type = QuickJSNative.InteropType.Float32, f32 = v };

    static QuickJSNative.InteropValue MakeInt(int v) =>
        new() { type = QuickJSNative.InteropType.Int32, i32 = v };

    static QuickJSNative.InteropValue MakeDouble(double v) =>
        new() { type = QuickJSNative.InteropType.Double, f64 = v };

    static QuickJSNative.InteropValue MakeBool(bool v) =>
        new() { type = QuickJSNative.InteropType.Bool, b = v ? 1 : 0 };

    static QuickJSNative.InteropValue MakeVec3(Vector3 v) =>
        new() { type = QuickJSNative.InteropType.Vector3, vecX = v.x, vecY = v.y, vecZ = v.z };

    static QuickJSNative.InteropValue MakeVec4(float x, float y, float z, float w) =>
        new() { type = QuickJSNative.InteropType.Vector4, vecX = x, vecY = y, vecZ = z, vecW = w };

    /// <summary>Invoke a callback with no arguments. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle) {
        ThrowIfInvalid();
        InvokeAndCheck(handle, null, 0);
    }

    /// <summary>Invoke a callback with 1 float argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeFloat(arg0) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with 2 float arguments. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, float arg1) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[2] { MakeFloat(arg0), MakeFloat(arg1) };
        InvokeAndCheck(handle, args, 2);
    }

    /// <summary>Invoke a callback with 3 float arguments. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, float arg1, float arg2) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[3] { MakeFloat(arg0), MakeFloat(arg1), MakeFloat(arg2) };
        InvokeAndCheck(handle, args, 3);
    }

    /// <summary>Invoke a callback with 1 int argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, int arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeInt(arg0) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with 2 int arguments. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, int arg0, int arg1) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[2] { MakeInt(arg0), MakeInt(arg1) };
        InvokeAndCheck(handle, args, 2);
    }

    /// <summary>Invoke a callback with 1 double argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, double arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeDouble(arg0) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with 1 bool argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, bool arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeBool(arg0) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with a Vector3 argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Vector3 arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeVec3(arg0) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with a Quaternion argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Quaternion arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeVec4(arg0.x, arg0.y, arg0.z, arg0.w) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with a Color argument. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Color arg0) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[1] { MakeVec4(arg0.r, arg0.g, arg0.b, arg0.a) };
        InvokeAndCheck(handle, args, 1);
    }

    /// <summary>Invoke a callback with float + Vector3 arguments. ZERO ALLOCATION.</summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, Vector3 arg1) {
        ThrowIfInvalid();
        var args = stackalloc QuickJSNative.InteropValue[2] { MakeFloat(arg0), MakeVec3(arg1) };
        InvokeAndCheck(handle, args, 2);
    }
}