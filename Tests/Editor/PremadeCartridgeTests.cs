using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Validates the UICartridge assets actually shipped in this project.
    ///
    /// CartridgeUtilsTests covers the extraction machinery with synthetic cartridges.
    /// This covers the data: a cartridge is authored by hand in a .asset file, and the
    /// failure modes are all silent. A file entry whose TextAsset reference broke
    /// extracts an empty folder; two cartridges sharing a slug overwrite each other; a
    /// slug with a space in it produces a folder no import statement can name. None of
    /// those surface until someone buys the package and tries to use it.
    ///
    /// Discovery is by type, not by a hardcoded list, so a new premade is covered the
    /// moment it is added.
    /// </summary>
    [TestFixture]
    public class PremadeCartridgeTests {
        // Used as a folder name and inside a JS string in __cart("..."), so it has to
        // be safe for both. Deliberately stricter than "no path separators".
        static readonly Regex kIdent = new Regex(@"^[A-Za-z][A-Za-z0-9_-]*$");

        // Only the shipped products. Cartridges elsewhere in the project (the
        // OneJSContainer/TestCartridge fixtures, say) are deliberately minimal test
        // data and would fail the store-listing requirements below for good reason.
        // CartridgeUtilsTests already covers the extraction machinery generically.
        const string kPremadeRoot = "Assets/Singtaa/Premade/";

        static string[] CartridgePaths() {
            var paths = AssetDatabase.FindAssets("t:UICartridge")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith(kPremadeRoot))
                .OrderBy(p => p)
                .ToArray();
            // A source returning nothing makes every parameterised test vanish and the
            // suite pass green, so surface it as a case instead.
            return paths.Length > 0 ? paths : new[] { "<none found>" };
        }

        static UICartridge Load(string path) {
            Assert.AreNotEqual("<none found>", path,
                $"No UICartridge assets under {kPremadeRoot}. Either the premades moved or FindAssets(\"t:UICartridge\") stopped matching.");
            var c = AssetDatabase.LoadAssetAtPath<UICartridge>(path);
            Assert.IsNotNull(c, $"{path} did not load as a UICartridge");
            return c;
        }

        // MARK: metadata

        [Test]
        public void AtLeastOnePremadeCartridgeExists() {
            var paths = CartridgePaths();
            Assert.AreNotEqual("<none found>", paths[0], $"expected cartridges under {kPremadeRoot}");
        }

        [Test]
        public void Metadata_IsComplete([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            Assert.IsNotEmpty(c.Slug ?? "", $"{path}: slug is required, it is the folder name and the __cart key");
            Assert.IsNotEmpty(c.Namespace ?? "", $"{path}: namespace is required for anything published");
            Assert.IsNotEmpty(c.Description ?? "", $"{path}: description is what the store listing shows");
            Assert.IsNotEmpty(c.DisplayName ?? "", $"{path}: display name falls back to slug, so this cannot be empty");
        }

        [Test]
        public void Metadata_SlugAndNamespaceArePathSafe([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            // Coalesced rather than passed straight in: Regex.IsMatch throws on null,
            // which would report as an NRE instead of naming the offending field.
            Assert.IsTrue(kIdent.IsMatch(c.Slug ?? ""), $"{path}: slug \"{c.Slug}\" is not a safe folder name / JS string");
            Assert.IsTrue(kIdent.IsMatch(c.Namespace ?? ""), $"{path}: namespace \"{c.Namespace}\" is not a safe folder name / JS string");
        }

        [Test]
        public void Metadata_AssetFileNameMatchesTheCartridgeName([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(path), c.name,
                $"{path}: renaming the asset file without renaming the object makes the two disagree in the inspector");
        }

        [Test]
        public void Identity_IsUniqueAcrossThePremades() {
            var paths = CartridgePaths();
            if (paths[0] == "<none found>") Assert.Ignore("no cartridges");
            var byId = new Dictionary<string, string>();
            foreach (var p in paths) {
                var c = AssetDatabase.LoadAssetAtPath<UICartridge>(p);
                if (c == null || string.IsNullOrEmpty(c.Slug)) continue;
                // Two cartridges with the same identity extract to one folder and the
                // second silently wins.
                if (byId.TryGetValue(c.RelativePath, out var first))
                    Assert.Fail($"duplicate cartridge identity \"{c.RelativePath}\": {first} and {p}");
                byId[c.RelativePath] = p;
            }
        }

        // MARK: payload

        [Test]
        public void Files_AllResolveToNonEmptyContent([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            Assert.IsNotEmpty(c.Files, $"{path}: a cartridge with no files extracts an empty folder");
            foreach (var f in c.Files) {
                Assert.IsNotEmpty(f.path ?? "", $"{path}: a file entry has no path");
                // The usual break: the .txt is renamed or deleted, the GUID no longer
                // resolves, and the entry silently becomes a null reference.
                Assert.IsNotNull(f.content, $"{path}: file entry \"{f.path}\" has a broken content reference");
                Assert.IsNotEmpty(f.content.text, $"{path}: file entry \"{f.path}\" resolves to an empty TextAsset");
            }
        }

        [Test]
        public void Files_PathsAreRelativeAndCannotEscape([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            foreach (var f in c.Files) {
                var p = f.path ?? "";
                // Extraction joins these onto the working dir and writes them, so a
                // rooted path or a .. segment writes outside the app entirely.
                Assert.IsFalse(Path.IsPathRooted(p), $"{path}: file path \"{p}\" is absolute");
                Assert.IsFalse(p.Split('/', '\\').Contains(".."), $"{path}: file path \"{p}\" escapes the cartridge folder");
                Assert.IsNotEmpty(Path.GetExtension(p), $"{path}: file path \"{p}\" has no extension");
            }
        }

        [Test]
        public void Objects_AllHaveKeyAndValue([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            foreach (var o in c.Objects) {
                Assert.IsNotEmpty(o.key ?? "", $"{path}: an object entry has no key");
                Assert.IsNotNull(o.value, $"{path}: object entry \"{o.key}\" has a broken reference");
            }
        }

        // MARK: extraction round-trip

        [Test]
        public void Extract_WritesEveryDeclaredFileVerbatim([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            var dir = Path.Combine(Path.GetTempPath(), "OneJSPremadeTest", System.Guid.NewGuid().ToString("N"));
            try {
                var written = CartridgeUtils.ExtractCartridges(dir, new List<UICartridge> { c }, true);
                var root = CartridgeUtils.GetCartridgePath(dir, c);

                foreach (var f in c.Files) {
                    var expected = Path.Combine(root, f.path.Replace('/', Path.DirectorySeparatorChar));
                    Assert.IsTrue(File.Exists(expected), $"{path}: \"{f.path}\" was not written to {expected}");
                    Assert.AreEqual(f.content.text, File.ReadAllText(expected),
                        $"{path}: \"{f.path}\" did not round-trip byte for byte");
                    Assert.Contains(expected, written, $"{path}: \"{f.path}\" was written but not reported");
                }

                // The generated .d.ts is what makes __cart() typed on the JS side.
                var dts = Path.Combine(root, $"{c.Slug}.d.ts");
                Assert.IsTrue(File.Exists(dts), $"{path}: no {c.Slug}.d.ts was generated");
                Assert.IsNotEmpty(File.ReadAllText(dts), $"{path}: {c.Slug}.d.ts is empty");
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Extract_LandsOnThePathTheImportStatementUses([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            var dir = Path.Combine(Path.GetTempPath(), "OneJSPremadeTest", System.Guid.NewGuid().ToString("N"));
            try {
                CartridgeUtils.ExtractCartridges(dir, new List<UICartridge> { c }, true);
                // Samples import "./@cartridges/@ns/slug/file", so this path is API.
                var expected = Path.Combine(dir, "@cartridges", $"@{c.Namespace}", c.Slug);
                Assert.IsTrue(Directory.Exists(expected),
                    $"{path}: expected extraction at {expected}, which is the path sample code imports");
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Extract_WithoutOverwrite_LeavesUserEditsAlone([ValueSource(nameof(CartridgePaths))] string path) {
            var c = Load(path);
            if (c.Files.Count == 0) Assert.Ignore("no files to probe with");
            var dir = Path.Combine(Path.GetTempPath(), "OneJSPremadeTest", System.Guid.NewGuid().ToString("N"));
            try {
                CartridgeUtils.ExtractCartridges(dir, new List<UICartridge> { c }, true);
                var root = CartridgeUtils.GetCartridgePath(dir, c);
                var probe = Path.Combine(root, c.Files[0].path.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(probe, "// edited by the user");

                CartridgeUtils.ExtractCartridges(dir, new List<UICartridge> { c }, false);

                Assert.AreEqual("// edited by the user", File.ReadAllText(probe),
                    $"{path}: a non-overwriting extract clobbered an existing folder");
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
