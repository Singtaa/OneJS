using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// EditMode tests for CartridgeUtils static methods.
/// Tests string escaping, path calculation, file extraction, and stylesheet application.
/// </summary>
[TestFixture]
public class CartridgeUtilsTests {
    const string TEST_BASE_DIR = "Temp/CartridgeUtilsTest";

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
        var result = CartridgeUtils.EscapeJsString(null);
        Assert.IsNull(result);
    }

    [Test]
    public void EscapeJsString_EmptyString_ReturnsEmpty() {
        var result = CartridgeUtils.EscapeJsString("");
        Assert.AreEqual("", result);
    }

    [Test]
    public void EscapeJsString_SimpleString_ReturnsUnchanged() {
        var result = CartridgeUtils.EscapeJsString("hello world");
        Assert.AreEqual("hello world", result);
    }

    [Test]
    public void EscapeJsString_SingleQuotes_AreEscaped() {
        var result = CartridgeUtils.EscapeJsString("it's a test");
        Assert.AreEqual("it\\'s a test", result);
    }

    [Test]
    public void EscapeJsString_Backslashes_AreEscaped() {
        var result = CartridgeUtils.EscapeJsString("path\\to\\file");
        Assert.AreEqual("path\\\\to\\\\file", result);
    }

    [Test]
    public void EscapeJsString_Newlines_AreEscaped() {
        var result = CartridgeUtils.EscapeJsString("line1\nline2");
        Assert.AreEqual("line1\\nline2", result);
    }

    [Test]
    public void EscapeJsString_CarriageReturns_AreEscaped() {
        var result = CartridgeUtils.EscapeJsString("line1\rline2");
        Assert.AreEqual("line1\\rline2", result);
    }

    [Test]
    public void EscapeJsString_MixedSpecialChars_AllEscaped() {
        var result = CartridgeUtils.EscapeJsString("it's a\\path\nwith\rmixed");
        Assert.AreEqual("it\\'s a\\\\path\\nwith\\rmixed", result);
    }

    // MARK: GetCartridgePath Tests

    [Test]
    public void GetCartridgePath_NullBaseDir_ReturnsNull() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, "test");

        var result = CartridgeUtils.GetCartridgePath(null, cartridge);

        Assert.IsNull(result);
        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void GetCartridgePath_EmptyBaseDir_ReturnsNull() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, "test");

        var result = CartridgeUtils.GetCartridgePath("", cartridge);

        Assert.IsNull(result);
        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void GetCartridgePath_NullCartridge_ReturnsNull() {
        var result = CartridgeUtils.GetCartridgePath(_testBasePath, null);
        Assert.IsNull(result);
    }

    [Test]
    public void GetCartridgePath_CartridgeWithNullSlug_ReturnsNull() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        // Slug is null by default

        var result = CartridgeUtils.GetCartridgePath(_testBasePath, cartridge);

        Assert.IsNull(result);
        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void GetCartridgePath_CartridgeWithEmptySlug_ReturnsNull() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, "");

        var result = CartridgeUtils.GetCartridgePath(_testBasePath, cartridge);

        Assert.IsNull(result);
        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void GetCartridgePath_ValidInputs_ReturnsCorrectPath() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, "myCartridge");

        var result = CartridgeUtils.GetCartridgePath(_testBasePath, cartridge);

        var expected = Path.Combine(_testBasePath, "@cartridges", "myCartridge");
        Assert.AreEqual(expected, result);
        Object.DestroyImmediate(cartridge);
    }

    // MARK: ExtractCartridges Tests

    [Test]
    public void ExtractCartridges_NullCartridges_DoesNotThrow() {
        Assert.DoesNotThrow(() => {
            CartridgeUtils.ExtractCartridges(_testBasePath, null, false);
        });
    }

    [Test]
    public void ExtractCartridges_EmptyCartridges_DoesNotThrow() {
        var cartridges = new List<UICartridge>();
        Assert.DoesNotThrow(() => {
            CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, false);
        });
    }

    [Test]
    public void ExtractCartridges_NullBaseDir_DoesNotThrow() {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, "test");
        var cartridges = new List<UICartridge> { cartridge };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.ExtractCartridges(null, cartridges, false);
        });

        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void ExtractCartridges_CreatesCartridgeFolder() {
        var cartridge = CreateTestCartridge("testSlug");
        var cartridges = new List<UICartridge> { cartridge };

        CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, false);

        var expectedPath = Path.Combine(_testBasePath, "@cartridges", "testSlug");
        Assert.IsTrue(Directory.Exists(expectedPath), "Cartridge folder should be created");

        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void ExtractCartridges_GeneratesTypeDefinitions() {
        var cartridge = CreateTestCartridge("myCart");
        var cartridges = new List<UICartridge> { cartridge };

        CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, false);

        var dtsPath = Path.Combine(_testBasePath, "@cartridges", "myCart", "myCart.d.ts");
        Assert.IsTrue(File.Exists(dtsPath), "TypeScript definition file should be created");

        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void ExtractCartridges_OverwriteFalse_SkipsExisting() {
        var cartridge = CreateTestCartridge("existingCart");
        var cartridges = new List<UICartridge> { cartridge };

        // Create folder with a marker file
        var cartPath = Path.Combine(_testBasePath, "@cartridges", "existingCart");
        Directory.CreateDirectory(cartPath);
        var markerFile = Path.Combine(cartPath, "marker.txt");
        File.WriteAllText(markerFile, "original");

        // Extract with overwrite=false
        CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, overwriteExisting: false);

        // Marker file should still exist (folder wasn't deleted)
        Assert.IsTrue(File.Exists(markerFile), "Existing folder should not be deleted when overwrite=false");
        Assert.AreEqual("original", File.ReadAllText(markerFile));

        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void ExtractCartridges_OverwriteTrue_ReplacesExisting() {
        var cartridge = CreateTestCartridge("replaceCart");
        var cartridges = new List<UICartridge> { cartridge };

        // Create folder with a marker file
        var cartPath = Path.Combine(_testBasePath, "@cartridges", "replaceCart");
        Directory.CreateDirectory(cartPath);
        var markerFile = Path.Combine(cartPath, "marker.txt");
        File.WriteAllText(markerFile, "original");

        // Extract with overwrite=true
        CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, overwriteExisting: true);

        // Marker file should be gone (folder was deleted and recreated)
        Assert.IsFalse(File.Exists(markerFile), "Marker file should be deleted when overwrite=true");

        // But the .d.ts file should exist
        var dtsPath = Path.Combine(cartPath, "replaceCart.d.ts");
        Assert.IsTrue(File.Exists(dtsPath), "New files should be created after overwrite");

        Object.DestroyImmediate(cartridge);
    }

    [Test]
    public void ExtractCartridges_SkipsNullCartridgesInList() {
        var validCartridge = CreateTestCartridge("validCart");
        var cartridges = new List<UICartridge> { null, validCartridge, null };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.ExtractCartridges(_testBasePath, cartridges, false);
        });

        // Valid cartridge should still be extracted
        var expectedPath = Path.Combine(_testBasePath, "@cartridges", "validCart");
        Assert.IsTrue(Directory.Exists(expectedPath));

        Object.DestroyImmediate(validCartridge);
    }

    // MARK: ApplyStylesheets Tests

    [Test]
    public void ApplyStylesheets_NullStylesheets_DoesNotThrow() {
        var root = new VisualElement();
        Assert.DoesNotThrow(() => {
            CartridgeUtils.ApplyStylesheets(root, null);
        });
    }

    [Test]
    public void ApplyStylesheets_EmptyStylesheets_DoesNotThrow() {
        var root = new VisualElement();
        var stylesheets = new List<StyleSheet>();

        Assert.DoesNotThrow(() => {
            CartridgeUtils.ApplyStylesheets(root, stylesheets);
        });
    }

    [Test]
    public void ApplyStylesheets_NullRoot_DoesNotThrow() {
        var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();
        var stylesheets = new List<StyleSheet> { stylesheet };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.ApplyStylesheets(null, stylesheets);
        });

        Object.DestroyImmediate(stylesheet);
    }

    [Test]
    public void ApplyStylesheets_ValidStylesheet_IsApplied() {
        var root = new VisualElement();
        var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();
        var stylesheets = new List<StyleSheet> { stylesheet };

        CartridgeUtils.ApplyStylesheets(root, stylesheets);

        Assert.IsTrue(root.styleSheets.Contains(stylesheet), "Stylesheet should be applied to root");

        Object.DestroyImmediate(stylesheet);
    }

    [Test]
    public void ApplyStylesheets_SkipsNullStylesheetsInList() {
        var root = new VisualElement();
        var validStylesheet = ScriptableObject.CreateInstance<StyleSheet>();
        var stylesheets = new List<StyleSheet> { null, validStylesheet, null };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.ApplyStylesheets(root, stylesheets);
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

        CartridgeUtils.ApplyStylesheets(root, stylesheets);

        Assert.AreEqual(2, root.styleSheets.count);
        Assert.IsTrue(root.styleSheets.Contains(ss1));
        Assert.IsTrue(root.styleSheets.Contains(ss2));

        Object.DestroyImmediate(ss1);
        Object.DestroyImmediate(ss2);
    }

    // MARK: Helper Methods

    /// <summary>
    /// Sets the slug field on a UICartridge via reflection (since it's private).
    /// </summary>
    void SetCartridgeSlug(UICartridge cartridge, string slug) {
        var field = typeof(UICartridge).GetField("_slug",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(cartridge, slug);
    }

    /// <summary>
    /// Creates a test UICartridge with the given slug.
    /// </summary>
    UICartridge CreateTestCartridge(string slug) {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, slug);
        return cartridge;
    }
}
