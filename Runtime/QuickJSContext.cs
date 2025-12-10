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

    // MARK: Callbacks
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
}

