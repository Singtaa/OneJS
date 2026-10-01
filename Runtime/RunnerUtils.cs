using UnityEngine.UIElements;
using System.Collections.Generic;

namespace OneJS {
    /// <summary>
    /// Helpers JSRunner and JSPad share for setting up a context, unrelated to packs.
    /// </summary>
    public static class RunnerUtils {
        /// <summary>
        /// Escape a string for safe use in JavaScript string literals.
        /// </summary>
        public static string EscapeJsString(string s) {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        /// <summary>
        /// Apply USS stylesheets to a root visual element.
        /// </summary>
        /// <param name="root">Root element to apply stylesheets to</param>
        /// <param name="stylesheets">List of stylesheets to apply</param>
        public static void ApplyStylesheets(VisualElement root, IReadOnlyList<StyleSheet> stylesheets) {
            if (stylesheets == null || stylesheets.Count == 0) return;
            if (root == null) return;

            foreach (var stylesheet in stylesheets) {
                if (stylesheet != null) {
                    root.styleSheets.Add(stylesheet);
                }
            }
        }

        /// <summary>
        /// Inject Unity platform defines as JavaScript globals.
        /// These can be used for conditional code: if (UNITY_WEBGL) { ... }
        /// </summary>
        /// <param name="bridge">The QuickJS bridge to inject defines into</param>
        public static void InjectPlatformDefines(QuickJSUIBridge bridge) {
            if (bridge == null) return;

            // Compile-time platform flags (cannot be simplified further due to preprocessor requirements)
            const bool isEditor =
#if UNITY_EDITOR
                true;
#else
                false;
#endif
            const bool isWebGL =
#if UNITY_WEBGL
                true;
#else
                false;
#endif
            const bool isStandalone =
#if UNITY_STANDALONE
                true;
#else
                false;
#endif
            const bool isOSX =
#if UNITY_STANDALONE_OSX
                true;
#else
                false;
#endif
            const bool isWindows =
#if UNITY_STANDALONE_WIN
                true;
#else
                false;
#endif
            const bool isLinux =
#if UNITY_STANDALONE_LINUX
                true;
#else
                false;
#endif
            const bool isIOS =
#if UNITY_IOS
                true;
#else
                false;
#endif
            const bool isAndroid =
#if UNITY_ANDROID
                true;
#else
                false;
#endif
            const bool isDebug =
#if DEBUG || DEVELOPMENT_BUILD
                true;
#else
                false;
#endif

            // Single eval with all defines
            bridge.Eval($@"Object.assign(globalThis, {{
        UNITY_EDITOR: {(isEditor ? "true" : "false")},
        UNITY_WEBGL: {(isWebGL ? "true" : "false")},
        UNITY_STANDALONE: {(isStandalone ? "true" : "false")},
        UNITY_STANDALONE_OSX: {(isOSX ? "true" : "false")},
        UNITY_STANDALONE_WIN: {(isWindows ? "true" : "false")},
        UNITY_STANDALONE_LINUX: {(isLinux ? "true" : "false")},
        UNITY_IOS: {(isIOS ? "true" : "false")},
        UNITY_ANDROID: {(isAndroid ? "true" : "false")},
        DEBUG: {(isDebug ? "true" : "false")}
    }});", "platform-defines.js");
        }
    }
}
