using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OneJS {
    /// <summary>
    /// Extracting packs and exposing them to JavaScript, shared by JSRunner and JSPad.
    /// </summary>
    public static class PackUtils {
        /// <summary>The folder a runner extracts its packs to, below its working directory.</summary>
        public const string Folder = "@packs";

        /// <summary>
        /// The folder runners made before packs were renamed from cartridges extract to. Such a
        /// runner keeps it for every pack, so the imports its app already has keep resolving.
        /// </summary>
        public const string LegacyFolder = "@cartridges";

        /// <summary>
        /// Convert a '/'-separated logical path (Pack.RelativePath, PackFileEntry.path)
        /// into one using the platform's directory separator. No-op on Unix; on Windows it keeps
        /// Path.Combine from producing mixed separators like "C:\dir\@packs\@ns/slug".
        /// </summary>
        static string ToNativePath(string logicalPath) {
            if (string.IsNullOrEmpty(logicalPath)) return logicalPath;
            return logicalPath.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Get the path to a pack's extracted files.
        /// Uses pack.RelativePath which includes namespace when present.
        /// </summary>
        /// <param name="baseDir">Base directory (WorkingDir for JSRunner, TempDir for JSPad)</param>
        /// <param name="folder">The runner's pack folder, <see cref="Folder"/> or <see cref="LegacyFolder"/></param>
        /// <param name="pack">The pack to get path for</param>
        /// <returns>Full path to pack folder, or null if invalid</returns>
        public static string GetPackPath(string baseDir, string folder, Pack pack) {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(folder)) return null;
            if (pack == null || string.IsNullOrEmpty(pack.Slug)) return null;
            return Path.Combine(baseDir, folder, ToNativePath(pack.RelativePath));
        }

        /// <summary>
        /// Extract pack files to baseDir/{folder}/{relativePath}/.
        /// Without namespace: {folder}/{slug}/
        /// With namespace: {folder}/@{namespace}/{slug}/
        /// </summary>
        /// <param name="baseDir">Base directory for extraction</param>
        /// <param name="folder">The runner's pack folder, <see cref="Folder"/> or <see cref="LegacyFolder"/></param>
        /// <param name="packs">List of packs to extract</param>
        /// <param name="overwriteExisting">If true, deletes existing folders before extracting. If false, skips existing.</param>
        /// <param name="logPrefix">Prefix for log messages (e.g., "[JSRunner]" or "[JSPad]")</param>
        /// <returns>List of file paths that were created on disk.</returns>
        public static List<string> ExtractPacks(string baseDir, string folder, IReadOnlyList<Pack> packs, bool overwriteExisting, string logPrefix = null) {
            var createdFiles = new List<string>();
            if (packs == null || packs.Count == 0) return createdFiles;
            if (string.IsNullOrEmpty(baseDir)) return createdFiles;

            foreach (var pack in packs) {
                if (pack == null || string.IsNullOrEmpty(pack.Slug)) continue;

                var destPath = GetPackPath(baseDir, folder, pack);
                if (string.IsNullOrEmpty(destPath)) continue;

                if (Directory.Exists(destPath)) {
                    if (overwriteExisting) {
                        Directory.Delete(destPath, true);
                    } else {
                        WarnIfOutdated(baseDir, folder, pack, logPrefix);
                        continue; // Skip if exists and not overwriting
                    }
                }

                Directory.CreateDirectory(destPath);

                // Extract files
                foreach (var file in pack.Files) {
                    if (string.IsNullOrEmpty(file.path) || file.content == null) continue;

                    var filePath = Path.Combine(destPath, ToNativePath(file.path));
                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                        Directory.CreateDirectory(fileDir);
                    }
                    File.WriteAllText(filePath, file.content.text);
                    createdFiles.Add(filePath);
                }

                // Generate TypeScript definitions
                var dts = PackTypeGenerator.Generate(pack);
                var dtsPath = Path.Combine(destPath, $"{pack.Slug}.d.ts");
                File.WriteAllText(dtsPath, dts);
                createdFiles.Add(dtsPath);

                if (!string.IsNullOrEmpty(logPrefix)) {
                    Debug.Log($"{logPrefix} Extracted pack: {pack.RelativePath}");
                }
            }

            return createdFiles;
        }

        /// <summary>
        /// Read the version recorded in an extracted pack's generated .d.ts header,
        /// or null if the folder, the .d.ts, or the version line is missing.
        /// </summary>
        public static string GetExtractedVersion(string baseDir, string folder, Pack pack) {
            var destPath = GetPackPath(baseDir, folder, pack);
            if (string.IsNullOrEmpty(destPath)) return null;
            var dtsPath = Path.Combine(destPath, $"{pack.Slug}.d.ts");
            if (!File.Exists(dtsPath)) return null;
            try {
                return PackTypeGenerator.ParseVersion(File.ReadAllText(dtsPath));
            } catch (IOException) {
                return null;
            }
        }

        static void WarnIfOutdated(string baseDir, string folder, Pack pack, string logPrefix) {
            if (string.IsNullOrEmpty(pack.Version)) return;
            var extracted = GetExtractedVersion(baseDir, folder, pack);
            if (extracted == pack.Version) return;
            Debug.LogWarning(
                $"{logPrefix ?? "[OneJS]"} Pack '{pack.RelativePath}' is extracted as version " +
                $"{extracted ?? "unknown"} but the asset is {pack.Version}. " +
                "Use D then E on the Packs tab (delete extracted files, extract again) to update.");
        }

        /// <summary>
        /// Inject packs as JavaScript globals accessible via __pack(path) function.
        /// Access pattern: __pack("colorPicker").myTexture or __pack("@myCompany/colorPicker").config
        /// Each pack's object entries become properties on the returned JS object.
        /// __cart and __cartRegistry, the names from before the rename, are the same objects.
        /// </summary>
        /// <param name="bridge">The QuickJS bridge to inject globals into</param>
        /// <param name="packs">List of packs to inject</param>
        public static void InjectPackGlobals(QuickJSUIBridge bridge, IReadOnlyList<Pack> packs) {
            if (bridge == null) return;

            // Initialize __packRegistry (internal storage) and __pack function
            bridge.Eval(@"
    globalThis.__packRegistry = globalThis.__packRegistry || {};
    globalThis.__pack = function(path) {
        var pack = __packRegistry[path];
        if (!pack) throw new Error('Pack not found: ' + path);
        return pack;
    };
    globalThis.__cartRegistry = globalThis.__packRegistry;
    globalThis.__cart = globalThis.__pack;
    ", "__pack-init.js");

            if (packs == null || packs.Count == 0) return;

            foreach (var pack in packs) {
                if (pack == null || string.IsNullOrEmpty(pack.Slug)) continue;

                var path = RunnerUtils.EscapeJsString(pack.RelativePath);

                // Build a plain JS object with each PackObjectEntry as a property
                bridge.Eval($"__packRegistry['{path}'] = {{}}", $"__pack-{pack.Slug}.js");

                if (pack.Objects != null) {
                    foreach (var entry in pack.Objects) {
                        if (string.IsNullOrEmpty(entry.key) || entry.value == null) continue;

                        var handle = QuickJSNative.RegisterObject(entry.value);
                        var typeName = entry.value.GetType().FullName;
                        var escapedKey = RunnerUtils.EscapeJsString(entry.key);
                        bridge.Eval($"__packRegistry['{path}']['{escapedKey}'] = __csHelpers.wrapObject('{typeName}', {handle})");
                    }
                }
            }
        }
    }
}
