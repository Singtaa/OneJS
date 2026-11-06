using System;
using System.Runtime.InteropServices;

namespace OneJS {
    /// <summary>
    /// WebAssembly modules compiled to native code, for QuickJS, which has no
    /// WebAssembly. A module library (built from a .wasm by
    /// Auxiliary~/quickjs-unity/wasm) hands over a table describing the module,
    /// and every context then serves it to JavaScript as `__wasm`, so the
    /// browser glue that drives the module runs unchanged. In a WebGL build the
    /// browser's own WebAssembly serves instead and this does nothing.
    /// </summary>
    public static partial class QuickJSNative {
#if !UNITY_WEBGL || UNITY_EDITOR
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern int qjs_register_wasm_module(IntPtr module);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        static extern int qjs_unregister_wasm_module([MarshalAs(UnmanagedType.LPStr)] string name);
#endif

        /// <summary>
        /// Makes a native WebAssembly module available to every context, present
        /// and future. <paramref name="table"/> is what the module library's
        /// <c>onejs_wasm_&lt;name&gt;()</c> returns. Registering the same module
        /// again is harmless. False when the engine refused it: no table, a
        /// library built for another version of the table, or too many modules.
        /// </summary>
        public static bool RegisterWasmModule(IntPtr table) {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (table == IntPtr.Zero) return false;
            return qjs_register_wasm_module(table) == 0;
#endif
        }

        /// <summary>
        /// Forgets a module by name before the code behind its table goes away,
        /// as a module a test builds from managed delegates does. Instances
        /// already made keep the module; only later instantiates miss it.
        /// </summary>
        internal static bool UnregisterWasmModule(string name) {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return qjs_unregister_wasm_module(name) == 0;
#endif
        }
    }
}
