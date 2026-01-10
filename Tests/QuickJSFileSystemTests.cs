using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the FileSystem API implementation.
/// Tests readTextFile, writeTextFile, fileExists, directoryExists, deleteFile, listFiles.
/// </summary>
[TestFixture]
public class QuickJSFileSystemTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    // Use a unique subdirectory for test files
    string _testDir;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create a test directory in temp
        _testDir = Path.Combine(Application.temporaryCachePath, "OneJSFileSystemTests");
        if (Directory.Exists(_testDir)) {
            Directory.Delete(_testDir, true);
        }
        Directory.CreateDirectory(_testDir);

        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        // Create GameObject with UIDocument
        _go = new GameObject("FileSystemTestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        // Wait a frame for UIDocument to initialize
        yield return null;

        var root = _uiDocument.rootVisualElement;
        _bridge = new QuickJSUIBridge(root);

        // Expose __bridge globally like JSRunner does (needed for compileStyleSheet, etc.)
        int bridgeHandle = QuickJSNative.RegisterObject(_bridge);
        _bridge.Eval($"globalThis.__bridge = __csHelpers.wrapObject('QuickJSUIBridge', {bridgeHandle})");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _bridge?.Dispose();
        _bridge = null;

        if (_go != null) Object.Destroy(_go);
        if (_panelSettings != null) Object.Destroy(_panelSettings);

        // Clean up test directory
        if (Directory.Exists(_testDir)) {
            Directory.Delete(_testDir, true);
        }

        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Path Globals Tests

    [UnityTest]
    public IEnumerator PathGlobals_PersistentDataPath_Exists() {
        var result = _bridge.Eval("typeof __persistentDataPath");
        Assert.AreEqual("string", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PathGlobals_StreamingAssetsPath_Exists() {
        var result = _bridge.Eval("typeof __streamingAssetsPath");
        Assert.AreEqual("string", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PathGlobals_DataPath_Exists() {
        var result = _bridge.Eval("typeof __dataPath");
        Assert.AreEqual("string", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PathGlobals_TemporaryCachePath_Exists() {
        var result = _bridge.Eval("typeof __temporaryCachePath");
        Assert.AreEqual("string", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PathGlobals_PersistentDataPath_MatchesUnity() {
        var result = _bridge.Eval("__persistentDataPath");
        Assert.AreEqual(Application.persistentDataPath, result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PathGlobals_StreamingAssetsPath_MatchesUnity() {
        var result = _bridge.Eval("__streamingAssetsPath");
        Assert.AreEqual(Application.streamingAssetsPath, result);
        yield return null;
    }

    // MARK: Function Existence Tests

    [UnityTest]
    public IEnumerator ReadTextFile_FunctionExists() {
        var result = _bridge.Eval("typeof readTextFile");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator WriteTextFile_FunctionExists() {
        var result = _bridge.Eval("typeof writeTextFile");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FileExists_FunctionExists() {
        var result = _bridge.Eval("typeof fileExists");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DirectoryExists_FunctionExists() {
        var result = _bridge.Eval("typeof directoryExists");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DeleteFile_FunctionExists() {
        var result = _bridge.Eval("typeof deleteFile");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ListFiles_FunctionExists() {
        var result = _bridge.Eval("typeof listFiles");
        Assert.AreEqual("function", result);
        yield return null;
    }

    // MARK: FileExists Tests

    [UnityTest]
    public IEnumerator FileExists_ReturnsFalse_ForNonexistent() {
        var testPath = Path.Combine(_testDir, "nonexistent.txt").Replace("\\", "/");
        var result = _bridge.Eval($"fileExists('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("false", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator FileExists_ReturnsTrue_ForExisting() {
        var testPath = Path.Combine(_testDir, "exists.txt").Replace("\\", "/");
        File.WriteAllText(testPath, "test content");

        var result = _bridge.Eval($"fileExists('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("true", result);
        yield return null;
    }

    // MARK: DirectoryExists Tests

    [UnityTest]
    public IEnumerator DirectoryExists_ReturnsFalse_ForNonexistent() {
        var testPath = Path.Combine(_testDir, "nonexistent_dir").Replace("\\", "/");
        var result = _bridge.Eval($"directoryExists('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("false", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DirectoryExists_ReturnsTrue_ForExisting() {
        var testPath = _testDir.Replace("\\", "/");
        var result = _bridge.Eval($"directoryExists('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("true", result);
        yield return null;
    }

    // MARK: ReadTextFile Tests (Async)

    [UnityTest]
    public IEnumerator ReadTextFile_ReadsContent_Successfully() {
        var testPath = Path.Combine(_testDir, "read_test.txt").Replace("\\", "/");
        File.WriteAllText(testPath, "Hello OneJS!");

        // Set up async test with Promise
        _bridge.Eval($@"
            globalThis.__testResult = null;
            globalThis.__testError = null;
            readTextFile('{testPath}')
                .then(function(content) {{ globalThis.__testResult = content; }})
                .catch(function(err) {{ globalThis.__testError = err.message; }});
        ");

        // Wait for async operation and tick
        for (int i = 0; i < 10; i++) {
            _bridge.Tick();
            yield return null;
        }

        var result = _bridge.Eval("globalThis.__testResult");
        var error = _bridge.Eval("globalThis.__testError");

        // JS null returns as string "null" from Eval
        Assert.AreEqual("null", error, $"Unexpected error: {error}");
        Assert.AreEqual("Hello OneJS!", result);
    }

    [UnityTest]
    public IEnumerator ReadTextFile_ThrowsError_ForNonexistent() {
        var testPath = Path.Combine(_testDir, "nonexistent.txt").Replace("\\", "/");

        // Test that reading a non-existent file results in an error
        // The error may come as a JS exception or as a __csError object
        _bridge.Eval($@"
            globalThis.__testResult = 'not_set';
            globalThis.__testError = null;
            (async function() {{
                try {{
                    var content = await readTextFile('{testPath}');
                    // Check if result contains error indicator
                    if (content && typeof content === 'object' && content.__csError) {{
                        globalThis.__testError = 'error_object';
                        globalThis.__testResult = content.__csError;
                    }} else {{
                        globalThis.__testResult = content;
                    }}
                }} catch(err) {{
                    globalThis.__testError = 'caught';
                    globalThis.__testResult = err.message || String(err);
                }}
            }})();
        ");

        // Wait for async operation
        for (int i = 0; i < 30; i++) {
            _bridge.Tick();
            yield return null;
        }

        var result = _bridge.Eval("globalThis.__testResult");
        var error = _bridge.Eval("globalThis.__testError");

        // Should have an error (either caught as exception or returned as error object)
        bool hasError = error == "caught" || error == "error_object" ||
                        (result != null && result != "not_set" && result.Contains("not found"));

        Assert.IsTrue(hasError,
            $"Expected error for non-existent file. result={result}, error={error}");
    }

    // MARK: WriteTextFile Tests (Async)

    [UnityTest]
    public IEnumerator WriteTextFile_WritesContent_Successfully() {
        var testPath = Path.Combine(_testDir, "write_test.txt").Replace("\\", "/");

        _bridge.Eval($@"
            globalThis.__testDone = false;
            globalThis.__testError = null;
            writeTextFile('{testPath}', 'Written from JS!')
                .then(function() {{ globalThis.__testDone = true; }})
                .catch(function(err) {{ globalThis.__testError = err.message; }});
        ");

        // Wait for async operation
        for (int i = 0; i < 10; i++) {
            _bridge.Tick();
            yield return null;
        }

        var done = _bridge.Eval("globalThis.__testDone ? 'true' : 'false'");
        var error = _bridge.Eval("globalThis.__testError");

        Assert.AreEqual("true", done);
        Assert.IsTrue(File.Exists(testPath), "File should exist");
        Assert.AreEqual("Written from JS!", File.ReadAllText(testPath));
    }

    [UnityTest]
    public IEnumerator WriteTextFile_CreatesDirectories_Automatically() {
        var testPath = Path.Combine(_testDir, "subdir", "nested", "file.txt").Replace("\\", "/");

        _bridge.Eval($@"
            globalThis.__testDone = false;
            writeTextFile('{testPath}', 'Nested content')
                .then(function() {{ globalThis.__testDone = true; }});
        ");

        // Wait for async operation
        for (int i = 0; i < 10; i++) {
            _bridge.Tick();
            yield return null;
        }

        var done = _bridge.Eval("globalThis.__testDone ? 'true' : 'false'");
        Assert.AreEqual("true", done);
        Assert.IsTrue(File.Exists(testPath), "File should exist in nested directory");
    }

    // MARK: DeleteFile Tests

    [UnityTest]
    public IEnumerator DeleteFile_ReturnsTrue_WhenDeleted() {
        var testPath = Path.Combine(_testDir, "delete_test.txt").Replace("\\", "/");
        File.WriteAllText(testPath, "to be deleted");

        var result = _bridge.Eval($"deleteFile('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("true", result);
        Assert.IsFalse(File.Exists(testPath), "File should be deleted");
        yield return null;
    }

    [UnityTest]
    public IEnumerator DeleteFile_ReturnsFalse_ForNonexistent() {
        var testPath = Path.Combine(_testDir, "nonexistent.txt").Replace("\\", "/");

        var result = _bridge.Eval($"deleteFile('{testPath}') ? 'true' : 'false'");
        Assert.AreEqual("false", result);
        yield return null;
    }

    // MARK: ListFiles Tests

    [UnityTest]
    public IEnumerator ListFiles_ReturnsEmptyArray_ForEmptyDir() {
        var emptyDir = Path.Combine(_testDir, "empty").Replace("\\", "/");
        Directory.CreateDirectory(emptyDir);

        var result = _bridge.Eval($"listFiles('{emptyDir}').length.toString()");
        Assert.AreEqual("0", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ListFiles_ReturnsFiles_WithPattern() {
        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "file1.txt"), "");
        File.WriteAllText(Path.Combine(_testDir, "file2.txt"), "");
        File.WriteAllText(Path.Combine(_testDir, "file3.json"), "");

        var testPath = _testDir.Replace("\\", "/");
        var result = _bridge.Eval($"listFiles('{testPath}', '*.txt').length.toString()");
        Assert.AreEqual("2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ListFiles_ReturnsAllFiles_WithStar() {
        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "a.txt"), "");
        File.WriteAllText(Path.Combine(_testDir, "b.json"), "");
        File.WriteAllText(Path.Combine(_testDir, "c.uss"), "");

        var testPath = _testDir.Replace("\\", "/");
        var result = _bridge.Eval($"listFiles('{testPath}', '*').length.toString()");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ListFiles_ReturnsRecursive_WhenEnabled() {
        // Create nested files
        var subDir = Path.Combine(_testDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_testDir, "root.txt"), "");
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "");

        var testPath = _testDir.Replace("\\", "/");

        // Non-recursive should only find root file
        var nonRecursive = _bridge.Eval($"listFiles('{testPath}', '*.txt', false).length.toString()");
        Assert.AreEqual("1", nonRecursive);

        // Recursive should find both
        var recursive = _bridge.Eval($"listFiles('{testPath}', '*.txt', true).length.toString()");
        Assert.AreEqual("2", recursive);
        yield return null;
    }

    // MARK: Integration Test - USS Loading from File

    [UnityTest]
    public IEnumerator Integration_LoadUssFromFile_Works() {
        var ussPath = Path.Combine(_testDir, "theme.uss").Replace("\\", "/");
        File.WriteAllText(ussPath, ".test-class { background-color: red; }");

        _bridge.Eval($@"
            globalThis.__testDone = false;
            readTextFile('{ussPath}')
                .then(function(content) {{
                    compileStyleSheet(content, 'test-theme');
                    globalThis.__testDone = true;
                }});
        ");

        // Wait for async operation
        for (int i = 0; i < 10; i++) {
            _bridge.Tick();
            yield return null;
        }

        var done = _bridge.Eval("globalThis.__testDone ? 'true' : 'false'");
        Assert.AreEqual("true", done);

        // Verify stylesheet was loaded by checking it can be removed
        var removed = _bridge.Eval("removeStyleSheet('test-theme') ? 'true' : 'false'");
        Assert.AreEqual("true", removed);
    }
}
