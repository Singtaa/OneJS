using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// The old home of <see cref="PackUtils"/> and <see cref="RunnerUtils"/>, kept so code written
    /// against it still compiles. Its paths are the folder it always used, @cartridges.
    /// </summary>
    [Obsolete("CartridgeUtils is now PackUtils (packs) and RunnerUtils (EscapeJsString, ApplyStylesheets, InjectPlatformDefines).")]
    public static class CartridgeUtils {
        public static string EscapeJsString(string s) => RunnerUtils.EscapeJsString(s);

        public static string GetCartridgePath(string baseDir, UICartridge cartridge) =>
            PackUtils.GetPackPath(baseDir, PackUtils.LegacyFolder, cartridge as Pack);

        public static List<string> ExtractCartridges(string baseDir, IReadOnlyList<UICartridge> cartridges, bool overwriteExisting, string logPrefix = null) =>
            PackUtils.ExtractPacks(baseDir, PackUtils.LegacyFolder, AsPacks(cartridges), overwriteExisting, logPrefix);

        public static string GetExtractedVersion(string baseDir, UICartridge cartridge) =>
            PackUtils.GetExtractedVersion(baseDir, PackUtils.LegacyFolder, cartridge as Pack);

        public static void InjectCartridgeGlobals(QuickJSUIBridge bridge, IReadOnlyList<UICartridge> cartridges) =>
            PackUtils.InjectPackGlobals(bridge, AsPacks(cartridges));

        public static void ApplyStylesheets(VisualElement root, IReadOnlyList<StyleSheet> stylesheets) =>
            RunnerUtils.ApplyStylesheets(root, stylesheets);

        public static void InjectPlatformDefines(QuickJSUIBridge bridge) => RunnerUtils.InjectPlatformDefines(bridge);

        static List<Pack> AsPacks(IReadOnlyList<UICartridge> cartridges) => cartridges?.OfType<Pack>().ToList();
    }
}
