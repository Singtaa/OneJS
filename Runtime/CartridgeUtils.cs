using System;
using System.Collections.Generic;
using System.IO;
using OneJS;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared utility methods for cartridge operations used by JSRunner and JSPad.
/// </summary>
public static class CartridgeUtils {
    /// <summary>
    /// Escape a string for safe use in JavaScript string literals.
    /// </summary>
    public static string EscapeJsString(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    /// <summary>
    /// Get the path to a cartridge's extracted files.
    /// </summary>
    /// <param name="baseDir">Base directory (WorkingDir for JSRunner, TempDir for JSPad)</param>
    /// <param name="cartridge">The cartridge to get path for</param>
    /// <returns>Full path to cartridge folder, or null if invalid</returns>
    public static string GetCartridgePath(string baseDir, UICartridge cartridge) {
        if (string.IsNullOrEmpty(baseDir)) return null;
        if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) return null;
        return Path.Combine(baseDir, "@cartridges", cartridge.Slug);
    }

    /// <summary>
    /// Extract cartridge files to baseDir/@cartridges/{slug}/.
    /// </summary>
    /// <param name="baseDir">Base directory for extraction</param>
    /// <param name="cartridges">List of cartridges to extract</param>
    /// <param name="overwriteExisting">If true, deletes existing folders before extracting. If false, skips existing.</param>
    /// <param name="logPrefix">Prefix for log messages (e.g., "[JSRunner]" or "[JSPad]")</param>
    public static void ExtractCartridges(string baseDir, IReadOnlyList<UICartridge> cartridges, bool overwriteExisting, string logPrefix = null) {
        if (cartridges == null || cartridges.Count == 0) return;
        if (string.IsNullOrEmpty(baseDir)) return;

        foreach (var cartridge in cartridges) {
            if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

            var destPath = GetCartridgePath(baseDir, cartridge);
            if (string.IsNullOrEmpty(destPath)) continue;

            if (Directory.Exists(destPath)) {
                if (overwriteExisting) {
                    Directory.Delete(destPath, true);
                } else {
                    continue; // Skip if exists and not overwriting
                }
            }

            Directory.CreateDirectory(destPath);

            // Extract files
            foreach (var file in cartridge.Files) {
                if (string.IsNullOrEmpty(file.path) || file.content == null) continue;

                var filePath = Path.Combine(destPath, file.path);
                var fileDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                    Directory.CreateDirectory(fileDir);
                }
                File.WriteAllText(filePath, file.content.text);
            }

            // Generate TypeScript definitions
            var dts = CartridgeTypeGenerator.Generate(cartridge);
            File.WriteAllText(Path.Combine(destPath, $"{cartridge.Slug}.d.ts"), dts);

            if (!string.IsNullOrEmpty(logPrefix)) {
                Debug.Log($"{logPrefix} Extracted cartridge: {cartridge.Slug}");
            }
        }
    }

    /// <summary>
    /// Inject Unity objects from cartridges as JavaScript globals under __cartridges namespace.
    /// Access pattern: __cartridges.{slug}.{key}
    /// </summary>
    /// <param name="bridge">The QuickJS bridge to inject globals into</param>
    /// <param name="cartridges">List of cartridges to inject</param>
    public static void InjectCartridgeGlobals(QuickJSUIBridge bridge, IReadOnlyList<UICartridge> cartridges) {
        if (cartridges == null || cartridges.Count == 0) return;
        if (bridge == null) return;

        // Initialize __cartridges namespace
        bridge.Eval("globalThis.__cartridges = globalThis.__cartridges || {}");

        foreach (var cartridge in cartridges) {
            if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

            // Create cartridge namespace
            bridge.Eval($"__cartridges['{EscapeJsString(cartridge.Slug)}'] = {{}}");

            foreach (var entry in cartridge.Objects) {
                if (string.IsNullOrEmpty(entry.key) || entry.value == null) continue;

                var handle = QuickJSNative.RegisterObject(entry.value);
                var typeName = entry.value.GetType().FullName;
                bridge.Eval($"__cartridges['{EscapeJsString(cartridge.Slug)}']['{EscapeJsString(entry.key)}'] = __csHelpers.wrapObject('{typeName}', {handle})");
            }
        }
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
