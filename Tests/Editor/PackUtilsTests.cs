using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for PackUtils static methods.
    /// Tests string escaping, path calculation, file extraction, and stylesheet application.
    /// </summary>
    [TestFixture]
    public class PackUtilsTests {
        const string TEST_BASE_DIR = "Temp/PackUtilsTest";

        string _testBasePath;

        [SetUp]
        public void SetUp() {
            _testBasePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_BASE_DIR);

            // Clean test directory
            if (Directory.Exists(_testBasePath)) {
                Directory.Delete(_testBasePath, true);
            }
            Directory.CreateDirectory(_testBasePath);
        }

        [TearDown]
        public void TearDown() {
            // Cleanup test directory
            if (Directory.Exists(_testBasePath)) {
                try {
                    Directory.Delete(_testBasePath, true);
                } catch (IOException) {
                    // File might be locked, ignore in teardown
                }
            }
        }

        // MARK: EscapeJsString Tests

        [Test]
        public void EscapeJsString_NullInput_ReturnsNull() {
            var result = RunnerUtils.EscapeJsString(null);
            Assert.IsNull(result);
        }

        [Test]
        public void EscapeJsString_EmptyString_ReturnsEmpty() {
            var result = RunnerUtils.EscapeJsString("");
            Assert.AreEqual("", result);
        }

        [Test]
        public void EscapeJsString_SimpleString_ReturnsUnchanged() {
            var result = RunnerUtils.EscapeJsString("hello world");
            Assert.AreEqual("hello world", result);
        }

        [Test]
        public void EscapeJsString_SingleQuotes_AreEscaped() {
            var result = RunnerUtils.EscapeJsString("it's a test");
            Assert.AreEqual("it\\'s a test", result);
        }

        [Test]
        public void EscapeJsString_Backslashes_AreEscaped() {
            var result = RunnerUtils.EscapeJsString("path\\to\\file");
            Assert.AreEqual("path\\\\to\\\\file", result);
        }

        [Test]
        public void EscapeJsString_Newlines_AreEscaped() {
            var result = RunnerUtils.EscapeJsString("line1\nline2");
            Assert.AreEqual("line1\\nline2", result);
        }

        [Test]
        public void EscapeJsString_CarriageReturns_AreEscaped() {
            var result = RunnerUtils.EscapeJsString("line1\rline2");
            Assert.AreEqual("line1\\rline2", result);
        }

        [Test]
        public void EscapeJsString_MixedSpecialChars_AllEscaped() {
            var result = RunnerUtils.EscapeJsString("it's a\\path\nwith\rmixed");
            Assert.AreEqual("it\\'s a\\\\path\\nwith\\rmixed", result);
        }

        // MARK: Pack.RelativePath Tests

        [Test]
        public void RelativePath_WithoutNamespace_ReturnsSlug() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");

            Assert.AreEqual("myPack", pack.RelativePath);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void RelativePath_WithNamespace_ReturnsNamespacedPath() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");
            SetPackNamespace(pack, "myCompany");

            Assert.AreEqual("@myCompany/myPack", pack.RelativePath);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void RelativePath_EmptyNamespace_ReturnsSlug() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");
            SetPackNamespace(pack, "");

            Assert.AreEqual("myPack", pack.RelativePath);
            Object.DestroyImmediate(pack);
        }

        // MARK: GetPackPath Tests

        [Test]
        public void GetPackPath_NullBaseDir_ReturnsNull() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "test");

            var result = PackUtils.GetPackPath(null, PackUtils.Folder, pack);

            Assert.IsNull(result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_EmptyBaseDir_ReturnsNull() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "test");

            var result = PackUtils.GetPackPath("", PackUtils.Folder, pack);

            Assert.IsNull(result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_NullPack_ReturnsNull() {
            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, null);
            Assert.IsNull(result);
        }

        [Test]
        public void GetPackPath_PackWithNullSlug_ReturnsNull() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            // Slug is null by default

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            Assert.IsNull(result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_PackWithEmptySlug_ReturnsNull() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "");

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            Assert.IsNull(result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_ValidInputs_ReturnsCorrectPath() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            var expected = Path.Combine(_testBasePath, PackUtils.Folder, "myPack");
            Assert.AreEqual(expected, result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_WithNamespace_ReturnsNamespacedPath() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");
            SetPackNamespace(pack, "myCompany");

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            var expected = Path.Combine(_testBasePath, PackUtils.Folder, "@myCompany", "myPack");
            Assert.AreEqual(expected, result);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_WithNamespace_UsesPlatformSeparators() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");
            SetPackNamespace(pack, "myCompany");

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            // RelativePath is always '/'-separated (it doubles as the __pack() key), so the
            // filesystem path must not inherit those separators on Windows.
            var expectedTail = Path.Combine(PackUtils.Folder, "@myCompany", "myPack");
            Assert.IsTrue(result.EndsWith(expectedTail),
                $"Path should end with '{expectedTail}' using platform separators, got '{result}'");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetPackPath_EmptyNamespace_ReturnsNonNamespacedPath() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "myPack");
            SetPackNamespace(pack, "");

            var result = PackUtils.GetPackPath(_testBasePath, PackUtils.Folder, pack);

            var expected = Path.Combine(_testBasePath, PackUtils.Folder, "myPack");
            Assert.AreEqual(expected, result);
            Object.DestroyImmediate(pack);
        }

        // MARK: ExtractPacks Tests

        [Test]
        public void ExtractPacks_NullPacks_DoesNotThrow() {
            Assert.DoesNotThrow(() => {
                PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, null, false);
            });
        }

        [Test]
        public void ExtractPacks_EmptyPacks_DoesNotThrow() {
            var packs = new List<Pack>();
            Assert.DoesNotThrow(() => {
                PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);
            });
        }

        [Test]
        public void ExtractPacks_NullBaseDir_DoesNotThrow() {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, "test");
            var packs = new List<Pack> { pack };

            Assert.DoesNotThrow(() => {
                PackUtils.ExtractPacks(null, PackUtils.Folder, packs, false);
            });

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_CreatesPackFolder() {
            var pack = CreateTestPack("testSlug");
            var packs = new List<Pack> { pack };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            var expectedPath = Path.Combine(_testBasePath, PackUtils.Folder, "testSlug");
            Assert.IsTrue(Directory.Exists(expectedPath), "Pack folder should be created");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_GeneratesTypeDefinitions() {
            var pack = CreateTestPack("myCart");
            var packs = new List<Pack> { pack };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            var dtsPath = Path.Combine(_testBasePath, PackUtils.Folder, "myCart", "myCart.d.ts");
            Assert.IsTrue(File.Exists(dtsPath), "TypeScript definition file should be created");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_OverwriteFalse_SkipsExisting() {
            var pack = CreateTestPack("existingCart");
            var packs = new List<Pack> { pack };

            // Create folder with a marker file
            var packPath = Path.Combine(_testBasePath, PackUtils.Folder, "existingCart");
            Directory.CreateDirectory(packPath);
            var markerFile = Path.Combine(packPath, "marker.txt");
            File.WriteAllText(markerFile, "original");

            // Extract with overwrite=false
            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, overwriteExisting: false);

            // Marker file should still exist (folder wasn't deleted)
            Assert.IsTrue(File.Exists(markerFile), "Existing folder should not be deleted when overwrite=false");
            Assert.AreEqual("original", File.ReadAllText(markerFile));

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_OverwriteTrue_ReplacesExisting() {
            var pack = CreateTestPack("replaceCart");
            var packs = new List<Pack> { pack };

            // Create folder with a marker file
            var packPath = Path.Combine(_testBasePath, PackUtils.Folder, "replaceCart");
            Directory.CreateDirectory(packPath);
            var markerFile = Path.Combine(packPath, "marker.txt");
            File.WriteAllText(markerFile, "original");

            // Extract with overwrite=true
            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, overwriteExisting: true);

            // Marker file should be gone (folder was deleted and recreated)
            Assert.IsFalse(File.Exists(markerFile), "Marker file should be deleted when overwrite=true");

            // But the .d.ts file should exist
            var dtsPath = Path.Combine(packPath, "replaceCart.d.ts");
            Assert.IsTrue(File.Exists(dtsPath), "New files should be created after overwrite");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_NestedFilePath_UsesPlatformSeparators() {
            var pack = CreateTestPack("nestedCart");
            AddPackFile(pack, "components/Button.tsx", "export const Button = () => null");
            var packs = new List<Pack> { pack };

            var created = PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            // PackFileEntry.path is authored with '/', same as RelativePath.
            var expected = Path.Combine(_testBasePath, PackUtils.Folder, "nestedCart", "components", "Button.tsx");
            Assert.IsTrue(File.Exists(expected), "Nested pack file should be written");
            Assert.IsTrue(created.Contains(expected),
                $"Returned path should use platform separators, got: {string.Join(", ", created)}");

            DestroyPack(pack);
        }

        [Test]
        public void ExtractPacks_SkipsNullPacksInList() {
            var validPack = CreateTestPack("validCart");
            var packs = new List<Pack> { null, validPack, null };

            Assert.DoesNotThrow(() => {
                PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);
            });

            // Valid pack should still be extracted
            var expectedPath = Path.Combine(_testBasePath, PackUtils.Folder, "validCart");
            Assert.IsTrue(Directory.Exists(expectedPath));

            Object.DestroyImmediate(validPack);
        }

        // MARK: ApplyStylesheets Tests

        [Test]
        public void ApplyStylesheets_NullStylesheets_DoesNotThrow() {
            var root = new VisualElement();
            Assert.DoesNotThrow(() => {
                RunnerUtils.ApplyStylesheets(root, null);
            });
        }

        [Test]
        public void ApplyStylesheets_EmptyStylesheets_DoesNotThrow() {
            var root = new VisualElement();
            var stylesheets = new List<StyleSheet>();

            Assert.DoesNotThrow(() => {
                RunnerUtils.ApplyStylesheets(root, stylesheets);
            });
        }

        [Test]
        public void ApplyStylesheets_NullRoot_DoesNotThrow() {
            var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();
            var stylesheets = new List<StyleSheet> { stylesheet };

            Assert.DoesNotThrow(() => {
                RunnerUtils.ApplyStylesheets(null, stylesheets);
            });

            Object.DestroyImmediate(stylesheet);
        }

        [Test]
        public void ApplyStylesheets_ValidStylesheet_IsApplied() {
            var root = new VisualElement();
            var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();
            var stylesheets = new List<StyleSheet> { stylesheet };

            RunnerUtils.ApplyStylesheets(root, stylesheets);

            Assert.IsTrue(root.styleSheets.Contains(stylesheet), "Stylesheet should be applied to root");

            Object.DestroyImmediate(stylesheet);
        }

        [Test]
        public void ApplyStylesheets_SkipsNullStylesheetsInList() {
            var root = new VisualElement();
            var validStylesheet = ScriptableObject.CreateInstance<StyleSheet>();
            var stylesheets = new List<StyleSheet> { null, validStylesheet, null };

            Assert.DoesNotThrow(() => {
                RunnerUtils.ApplyStylesheets(root, stylesheets);
            });

            Assert.IsTrue(root.styleSheets.Contains(validStylesheet));

            Object.DestroyImmediate(validStylesheet);
        }

        [Test]
        public void ApplyStylesheets_MultipleStylesheets_AllApplied() {
            var root = new VisualElement();
            var ss1 = ScriptableObject.CreateInstance<StyleSheet>();
            var ss2 = ScriptableObject.CreateInstance<StyleSheet>();
            var stylesheets = new List<StyleSheet> { ss1, ss2 };

            RunnerUtils.ApplyStylesheets(root, stylesheets);

            Assert.AreEqual(2, root.styleSheets.count);
            Assert.IsTrue(root.styleSheets.Contains(ss1));
            Assert.IsTrue(root.styleSheets.Contains(ss2));

            Object.DestroyImmediate(ss1);
            Object.DestroyImmediate(ss2);
        }

        [Test]
        public void ExtractPacks_WithNamespace_CreatesNamespacedFolder() {
            var pack = CreateTestPackWithNamespace("myCompany", "testSlug");
            var packs = new List<Pack> { pack };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            var expectedPath = Path.Combine(_testBasePath, PackUtils.Folder, "@myCompany", "testSlug");
            Assert.IsTrue(Directory.Exists(expectedPath), "Namespaced pack folder should be created");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_MixedNamespaces_CreatesBothFolderStructures() {
            var cartWithNs = CreateTestPackWithNamespace("myCompany", "cart1");
            var cartWithoutNs = CreateTestPack("cart2");
            var packs = new List<Pack> { cartWithNs, cartWithoutNs };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            var namespacedPath = Path.Combine(_testBasePath, PackUtils.Folder, "@myCompany", "cart1");
            var regularPath = Path.Combine(_testBasePath, PackUtils.Folder, "cart2");

            Assert.IsTrue(Directory.Exists(namespacedPath), "Namespaced folder should exist");
            Assert.IsTrue(Directory.Exists(regularPath), "Non-namespaced folder should exist");

            Object.DestroyImmediate(cartWithNs);
            Object.DestroyImmediate(cartWithoutNs);
        }

        // MARK: ExtractPacks Return Value Tests

        [Test]
        public void ExtractPacks_ReturnsCreatedFilePaths() {
            var pack = CreateTestPack("returnTest");
            var packs = new List<Pack> { pack };

            var created = PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            Assert.IsNotNull(created);
            // Should contain at least the .d.ts file
            Assert.IsTrue(created.Count > 0, "Should return at least the .d.ts path");
            Assert.IsTrue(created.Exists(p => p.EndsWith(".d.ts")), "Should include .d.ts file");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_SkipsExisting_ReturnsEmptyList() {
            var pack = CreateTestPack("skipReturn");
            var packs = new List<Pack> { pack };

            // First extraction
            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            // Second extraction with overwrite=false should skip
            var created = PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            Assert.IsNotNull(created);
            Assert.AreEqual(0, created.Count, "Should return empty list when skipping existing");

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_NullInputs_ReturnsEmptyList() {
            var result1 = PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, null, false);
            Assert.IsNotNull(result1);
            Assert.AreEqual(0, result1.Count);

            var result2 = PackUtils.ExtractPacks(null, PackUtils.Folder, new List<Pack>(), false);
            Assert.IsNotNull(result2);
            Assert.AreEqual(0, result2.Count);
        }

        // MARK: Old Names

        [Test]
        public void Generate_DeclaresPackAndTheDeprecatedCartridgeNames() {
            var pack = CreateTestPack("my-hud");
            var objects = (List<PackObjectEntry>)typeof(Pack).GetField("_objects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(pack);
            objects.Add(new PackObjectEntry { key = "config", value = pack });

            var dts = PackTypeGenerator.Generate(pack);

            StringAssert.Contains("interface MyHudPack {", dts);
            StringAssert.Contains("function __pack(path: 'my-hud'): MyHudPack;", dts);
            StringAssert.Contains("/** @deprecated Use __pack. */\n    function __cart(path: 'my-hud'): MyHudPack;", dts.Replace("\r\n", "\n"));
            StringAssert.Contains("type MyHudCartridge = MyHudPack;", dts);
            Object.DestroyImmediate(pack);
        }

#pragma warning disable 0618 // the obsolete forwarders are what these test
        [Test]
        public void CartridgeUtils_ForwardsToTheFolderItAlwaysUsed() {
            var pack = CreateTestPack("oldCart");
            SetPackVersion(pack, "1.0.0");
            AddPackFile(pack, "index.tsx", "export {}");
            var packs = new List<Pack> { pack };

            var expected = Path.Combine(_testBasePath, PackUtils.LegacyFolder, "oldCart");
            Assert.AreEqual(expected, CartridgeUtils.GetCartridgePath(_testBasePath, pack));
            CartridgeUtils.ExtractCartridges(_testBasePath, packs, false);
            Assert.IsTrue(File.Exists(Path.Combine(expected, "index.tsx")));
            Assert.AreEqual("1.0.0", CartridgeUtils.GetExtractedVersion(_testBasePath, pack));
            Assert.AreEqual("it\\'s", CartridgeUtils.EscapeJsString("it's"));
            Assert.AreEqual(PackTypeGenerator.Generate(pack).Split('\n')[0], CartridgeTypeGenerator.Generate(pack).Split('\n')[0]);
            DestroyPack(pack);
        }
#pragma warning restore 0618

        // MARK: Version Tests

        [Test]
        public void Generate_WithVersion_WritesVersionLineInHeader() {
            var pack = CreateTestPack("versionedCart");
            SetPackVersion(pack, "1.2.0");

            var dts = PackTypeGenerator.Generate(pack);

            StringAssert.Contains($"{PackTypeGenerator.VersionLinePrefix}1.2.0", dts);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void Generate_WithoutVersion_OmitsVersionLine() {
            var pack = CreateTestPack("unversionedCart");

            var dts = PackTypeGenerator.Generate(pack);

            StringAssert.DoesNotContain(PackTypeGenerator.VersionLinePrefix, dts);
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ParseVersion_RoundTripsThroughGenerate() {
            var pack = CreateTestPack("roundTripCart");
            SetPackVersion(pack, "2.0.1");

            var dts = PackTypeGenerator.Generate(pack);

            Assert.AreEqual("2.0.1", PackTypeGenerator.ParseVersion(dts));
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ParseVersion_OnlyScansTheCommentHeader() {
            // A version-looking line below the header (i.e., after any non-comment line)
            // must not count: it could be user-authored content.
            var text = "// Auto-generated by OneJS Pack: x\n\n// Version: 9.9.9\nexport {};";
            Assert.IsNull(PackTypeGenerator.ParseVersion(text));
            Assert.IsNull(PackTypeGenerator.ParseVersion(null));
            Assert.IsNull(PackTypeGenerator.ParseVersion(""));
        }

        [Test]
        public void GetExtractedVersion_AfterExtract_MatchesAssetVersion() {
            var pack = CreateTestPack("extractVersion");
            SetPackVersion(pack, "1.0.0");

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, new List<Pack> { pack }, false);

            Assert.AreEqual("1.0.0", PackUtils.GetExtractedVersion(_testBasePath, PackUtils.Folder, pack));
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void GetExtractedVersion_UnversionedExtraction_ReturnsNull() {
            var pack = CreateTestPack("noVersion");

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, new List<Pack> { pack }, false);

            Assert.IsNull(PackUtils.GetExtractedVersion(_testBasePath, PackUtils.Folder, pack));
            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_SkipExisting_WarnsWhenVersionDiffers() {
            var pack = CreateTestPack("staleCart");
            SetPackVersion(pack, "1.0.0");
            var packs = new List<Pack> { pack };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);
            SetPackVersion(pack, "1.1.0");

            LogAssert.Expect(LogType.Warning, new Regex("staleCart.*1\\.0\\.0.*1\\.1\\.0"));
            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            Object.DestroyImmediate(pack);
        }

        [Test]
        public void ExtractPacks_SkipExisting_NoWarningWhenVersionMatches() {
            var pack = CreateTestPack("freshCart");
            SetPackVersion(pack, "1.0.0");
            var packs = new List<Pack> { pack };

            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);
            PackUtils.ExtractPacks(_testBasePath, PackUtils.Folder, packs, false);

            LogAssert.NoUnexpectedReceived();
            Object.DestroyImmediate(pack);
        }

        // MARK: Helper Methods

        /// <summary>
        /// Sets the slug field on a Pack via reflection (since it's private).
        /// </summary>
        void SetPackSlug(Pack pack, string slug) {
            var field = typeof(Pack).GetField("_slug",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(pack, slug);
        }

        /// <summary>
        /// Sets the namespace field on a Pack via reflection.
        /// </summary>
        void SetPackNamespace(Pack pack, string ns) {
            var field = typeof(Pack).GetField("_namespace",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(pack, ns);
        }

        /// <summary>
        /// Sets the version field on a Pack via reflection.
        /// </summary>
        void SetPackVersion(Pack pack, string version) {
            var field = typeof(Pack).GetField("_version",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(pack, version);
        }

        /// <summary>
        /// Adds a file entry to a Pack via reflection (backed by an in-memory TextAsset).
        /// </summary>
        void AddPackFile(Pack pack, string path, string content) {
            var field = typeof(Pack).GetField("_files",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var files = (List<PackFileEntry>)field.GetValue(pack);
            files.Add(new PackFileEntry { path = path, content = new TextAsset(content) });
        }

        /// <summary>
        /// Destroys a pack along with any TextAssets created by AddPackFile.
        /// </summary>
        void DestroyPack(Pack pack) {
            foreach (var file in pack.Files) {
                if (file?.content != null) Object.DestroyImmediate(file.content);
            }
            Object.DestroyImmediate(pack);
        }

        /// <summary>
        /// Creates a test Pack with the given slug.
        /// </summary>
        Pack CreateTestPack(string slug) {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, slug);
            return pack;
        }

        /// <summary>
        /// Creates a test Pack with the given namespace and slug.
        /// </summary>
        Pack CreateTestPackWithNamespace(string ns, string slug) {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackNamespace(pack, ns);
            SetPackSlug(pack, slug);
            return pack;
        }
    }
}
