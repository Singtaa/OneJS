using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the localStorage API implementation.
/// Uses Unity's PlayerPrefs under the hood.
/// </summary>
[TestFixture]
public class QuickJSStorageTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    // Use a unique prefix for test keys to avoid conflicts
    const string TestKeyPrefix = "__test_storage_";

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Clean up any existing test keys
        PlayerPrefs.DeleteKey(TestKeyPrefix + "key1");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "key2");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "theme");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "user");
        PlayerPrefs.Save();

        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        // Create GameObject with UIDocument
        _go = new GameObject("StorageTestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        // Wait a frame for UIDocument to initialize
        yield return null;

        var root = _uiDocument.rootVisualElement;
        _bridge = new QuickJSUIBridge(root);

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _bridge?.Dispose();
        _bridge = null;

        if (_go != null) Object.Destroy(_go);
        if (_panelSettings != null) Object.Destroy(_panelSettings);

        // Clean up test keys
        PlayerPrefs.DeleteKey(TestKeyPrefix + "key1");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "key2");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "theme");
        PlayerPrefs.DeleteKey(TestKeyPrefix + "user");
        PlayerPrefs.Save();

        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Existence Tests

    [UnityTest]
    public IEnumerator LocalStorage_GlobalExists() {
        var result = _bridge.Eval("typeof localStorage");
        Assert.AreEqual("object", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_GetItemExists() {
        var result = _bridge.Eval("typeof localStorage.getItem");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_SetItemExists() {
        var result = _bridge.Eval("typeof localStorage.setItem");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_RemoveItemExists() {
        var result = _bridge.Eval("typeof localStorage.removeItem");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_ClearExists() {
        var result = _bridge.Eval("typeof localStorage.clear");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SessionStorage_GlobalExists() {
        var result = _bridge.Eval("typeof sessionStorage");
        Assert.AreEqual("object", result);
        yield return null;
    }

    // MARK: Basic Operations

    [UnityTest]
    public IEnumerator LocalStorage_SetAndGetItem_Works() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}theme', 'dark')");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}theme')");
        Assert.AreEqual("dark", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_GetItem_ReturnsNullForMissing() {
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}nonexistent') === null ? 'null' : 'not null'");
        Assert.AreEqual("null", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_SetItem_OverwritesExisting() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'first')");
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'second')");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("second", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_RemoveItem_Works() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'value')");
        _bridge.Eval($"localStorage.removeItem('{TestKeyPrefix}key1')");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1') === null ? 'null' : 'not null'");
        Assert.AreEqual("null", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_RemoveItem_NonexistentKey_NoError() {
        // Should not throw when removing a key that doesn't exist
        _bridge.Eval($"localStorage.removeItem('{TestKeyPrefix}nonexistent')");
        var result = _bridge.Eval("'ok'");
        Assert.AreEqual("ok", result);
        yield return null;
    }

    // MARK: Value Type Handling

    [UnityTest]
    public IEnumerator LocalStorage_SetItem_ConvertsNumberToString() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 42)");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("42", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_SetItem_ConvertsBoolToString() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', true)");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("true", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_JSON_RoundTrip_Works() {
        _bridge.Eval($@"
            var user = {{ name: 'Alice', age: 30 }};
            localStorage.setItem('{TestKeyPrefix}user', JSON.stringify(user));
        ");
        var result = _bridge.Eval($@"
            var stored = localStorage.getItem('{TestKeyPrefix}user');
            var parsed = JSON.parse(stored);
            parsed.name + ':' + parsed.age;
        ");
        Assert.AreEqual("Alice:30", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_EmptyString_Works() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', '')");
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("", result);
        yield return null;
    }

    // MARK: Multiple Keys

    [UnityTest]
    public IEnumerator LocalStorage_MultipleKeys_Independent() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'value1')");
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key2', 'value2')");

        var result1 = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        var result2 = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key2')");

        Assert.AreEqual("value1", result1);
        Assert.AreEqual("value2", result2);
        yield return null;
    }

    // MARK: Persistence (via PlayerPrefs)

    [UnityTest]
    public IEnumerator LocalStorage_Persists_ToPlayerPrefs() {
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'persisted')");

        // Verify it's actually in PlayerPrefs
        var playerPrefsValue = PlayerPrefs.GetString(TestKeyPrefix + "key1");
        Assert.AreEqual("persisted", playerPrefsValue);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_Reads_FromPlayerPrefs() {
        // Set directly via PlayerPrefs
        PlayerPrefs.SetString(TestKeyPrefix + "key1", "from_prefs");
        PlayerPrefs.Save();

        // Read via localStorage
        var result = _bridge.Eval($"localStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("from_prefs", result);
        yield return null;
    }

    // MARK: API Completeness

    [UnityTest]
    public IEnumerator LocalStorage_Key_ReturnsNull() {
        // PlayerPrefs doesn't support key enumeration
        var result = _bridge.Eval("localStorage.key(0) === null ? 'null' : 'not null'");
        Assert.AreEqual("null", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator LocalStorage_Length_ReturnsZero() {
        // PlayerPrefs doesn't support counting keys
        var result = _bridge.Eval("localStorage.length.toString()");
        Assert.AreEqual("0", result);
        yield return null;
    }

    // MARK: SessionStorage

    [UnityTest]
    public IEnumerator SessionStorage_SetAndGetItem_Works() {
        _bridge.Eval($"sessionStorage.setItem('{TestKeyPrefix}theme', 'light')");
        var result = _bridge.Eval($"sessionStorage.getItem('{TestKeyPrefix}theme')");
        Assert.AreEqual("light", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SessionStorage_SharesData_WithLocalStorage() {
        // Since sessionStorage is an alias, data should be shared
        _bridge.Eval($"localStorage.setItem('{TestKeyPrefix}key1', 'shared')");
        var result = _bridge.Eval($"sessionStorage.getItem('{TestKeyPrefix}key1')");
        Assert.AreEqual("shared", result);
        yield return null;
    }
}
