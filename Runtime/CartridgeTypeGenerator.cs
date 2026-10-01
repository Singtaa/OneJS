using System;

namespace OneJS {
    /// <summary>The old name of <see cref="PackTypeGenerator"/>, kept so code written against it still compiles.</summary>
    [Obsolete("CartridgeTypeGenerator is now PackTypeGenerator.")]
    public static class CartridgeTypeGenerator {
        public const string VersionLinePrefix = PackTypeGenerator.VersionLinePrefix;

        public static string Generate(Pack cartridge) => PackTypeGenerator.Generate(cartridge);

        public static string ParseVersion(string dtsText) => PackTypeGenerator.ParseVersion(dtsText);
    }
}
