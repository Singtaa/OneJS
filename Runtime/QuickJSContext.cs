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
    /// <summary>
    /// Invoke a callback with no arguments. ZERO ALLOCATION.
    /// Use this for per-frame tick callbacks.
    /// </summary>
    public void InvokeCallbackNoAlloc(int handle) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        unsafe {
            int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, null, 0, null);
            if (code != 0) {
                throw new Exception($"qjs_invoke_callback failed with code {code}");
            }
        }
    }

    /// <summary>
    /// Invoke a callback with 1 float argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Float32;
        args[0].f32 = arg0;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 2 float arguments. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, float arg1) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[2];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Float32;
        args[0].f32 = arg0;
        args[1] = default;
        args[1].type = QuickJSNative.InteropType.Float32;
        args[1].f32 = arg1;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 2, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 3 float arguments. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, float arg1, float arg2) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[3];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Float32;
        args[0].f32 = arg0;
        args[1] = default;
        args[1].type = QuickJSNative.InteropType.Float32;
        args[1].f32 = arg1;
        args[2] = default;
        args[2].type = QuickJSNative.InteropType.Float32;
        args[2].f32 = arg2;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 3, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 1 int argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, int arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Int32;
        args[0].i32 = arg0;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 2 int arguments. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, int arg0, int arg1) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[2];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Int32;
        args[0].i32 = arg0;
        args[1] = default;
        args[1].type = QuickJSNative.InteropType.Int32;
        args[1].i32 = arg1;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 2, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 1 double argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, double arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Double;
        args[0].f64 = arg0;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with 1 bool argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, bool arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Bool;
        args[0].b = arg0 ? 1 : 0;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with a Vector3 argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Vector3 arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Vector3;
        args[0].vecX = arg0.x;
        args[0].vecY = arg0.y;
        args[0].vecZ = arg0.z;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with a Quaternion argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Quaternion arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Vector4;
        args[0].vecX = arg0.x;
        args[0].vecY = arg0.y;
        args[0].vecZ = arg0.z;
        args[0].vecW = arg0.w;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with a Color argument. ZERO ALLOCATION.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, Color arg0) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[1];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Vector4;
        args[0].vecX = arg0.r;
        args[0].vecY = arg0.g;
        args[0].vecZ = arg0.b;
        args[0].vecW = arg0.a;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 1, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }

    /// <summary>
    /// Invoke a callback with float + Vector3 arguments. ZERO ALLOCATION.
    /// Useful for passing deltaTime + position in one call.
    /// </summary>
    public unsafe void InvokeCallbackNoAlloc(int handle, float arg0, Vector3 arg1) {
        if (_disposed) throw new ObjectDisposedException(nameof(QuickJSContext));
        if (_ptr == IntPtr.Zero) throw new InvalidOperationException("Context is null");

        QuickJSNative.InteropValue* args = stackalloc QuickJSNative.InteropValue[2];
        args[0] = default;
        args[0].type = QuickJSNative.InteropType.Float32;
        args[0].f32 = arg0;
        args[1] = default;
        args[1].type = QuickJSNative.InteropType.Vector3;
        args[1].vecX = arg1.x;
        args[1].vecY = arg1.y;
        args[1].vecZ = arg1.z;

        int code = QuickJSNative.qjs_invoke_callback(_ptr, handle, args, 2, null);
        if (code != 0) {
            throw new Exception($"qjs_invoke_callback failed with code {code}");
        }
    }
}