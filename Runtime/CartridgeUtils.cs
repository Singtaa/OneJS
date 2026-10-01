using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// The old home of <see cref="PackUtils"/> and <see cref="RunnerUtils"/>, kept so code written
    /// against it still compiles. Its paths are the folder it always used, @cartridges.
    /// </summary>
    [Obsolete("CartridgeUtils is now PackUtils (packs) and RunnerUtils (EscapeJsString, ApplyStylesheets, InjectPlatformDefines).")]
    public static class CartridgeUtils {
        public static string EscapeJsString(string s) => RunnerUtils.EscapeJsString(s);

        public static string GetCartridgePath(string baseDir, Pack cartridge) =>
            PackUtils.GetPackPath(baseDir, PackUtils.LegacyFolder, cartridge);

        public static List<string> ExtractCartridges(string baseDir, IReadOnlyList<Pack> cartridges, bool overwriteExisting, string logPrefix = null) =>
            PackUtils.ExtractPacks(baseDir, PackUtils.LegacyFolder, cartridges, overwriteExisting, logPrefix);

        public static string GetExtractedVersion(string baseDir, Pack cartridge) =>
            PackUtils.GetExtractedVersion(baseDir, PackUtils.LegacyFolder, cartridge);

        public static void InjectCartridgeGlobals(QuickJSUIBridge bridge, IReadOnlyList<Pack> cartridges) =>
            PackUtils.InjectPackGlobals(bridge, cartridges);

        public static void ApplyStylesheets(VisualElement root, IReadOnlyList<StyleSheet> stylesheets) =>
            RunnerUtils.ApplyStylesheets(root, stylesheets);

        public static void InjectPlatformDefines(QuickJSUIBridge bridge) => RunnerUtils.InjectPlatformDefines(bridge);
    }
}
