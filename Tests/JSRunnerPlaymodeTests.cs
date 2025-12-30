using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// PlayMode tests for JSRunner MonoBehaviour.
/// Tests scaffolding, initialization, script execution, reload, and globals injection.
/// Uses a temp directory to avoid polluting the project.
/// </summary>
[TestFixture]
public class JSRunnerPlaymodeTests {
    const string TEST_WORKING_DIR = "Temp/OneJSTestRunner";

    // Simple test scripts (inline, no GUID dependencies)
    const string SimpleConsoleLog = "console.log('test output');";
    const string GlobalAccessTest = "globalThis.__testResult = 'success';";
    const string RootElementTest = "globalThis.__hasRoot = typeof __root !== 'undefined' && __root.__csHandle > 0;";
    const string PlatformDefinesTest = @"
globalThis.__platformTest = {
    hasEditor: typeof UNITY_EDITOR !== 'undefined',
    hasWebGL: typeof UNITY_WEBGL !== 'undefined',
    hasStandalone: typeof UNITY_STANDALONE !== 'undefined',
    editorValue: UNITY_EDITOR
};";

    GameObject _go;
    JSRunner _runner;
    PanelSettings _panelSettings;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Clean test directory
        var fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_WORKING_DIR);
        if (Directory.Exists(fullPath)) {
            Directory.Delete(fullPath, true);
        }
        Directory.CreateDirectory(fullPath);

        // Create PanelSettings at runtime (required for UIDocument)
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        // Stop runner if running
        if (_runner != null && _runner.Bridge != null) {
            _runner.Bridge.Dispose();
        }

        if (_go != null) UnityEngine.Object.Destroy(_go);
        if (_panelSettings != null) UnityEngine.Object.Destroy(_panelSettings);

        QuickJSNative.ClearAllHandles();

        // Cleanup test directory
        var fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_WORKING_DIR);
        if (Directory.Exists(fullPath)) {
            try {
                Directory.Delete(fullPath, true);
            } catch (IOException) {
                // File might be locked, ignore in teardown
            }
        }

        yield return null;
    }

    /// <summary>
    /// Create a JSRunner with preconfigured settings.
    /// Does NOT trigger Start() - caller must yield a frame for that.
    /// </summary>
    JSRunner CreateJSRunner(string entryContent = null) {
        _go = new GameObject("TestJSRunner");

        // Add UIDocument first with panel settings
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        // Add JSRunner
        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Create entry file if content provided
        if (entryContent != null) {
            var entryPath = Path.Combine(_runner.WorkingDirFullPath, "test.js");
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!Directory.Exists(entryDir)) {
                Directory.CreateDirectory(entryDir);
            }
            File.WriteAllText(entryPath, entryContent);
        }

        return _runner;
    }

    // MARK: Initialization Tests

    [UnityTest]
    public IEnumerator Init_CreatesUIDocument_WhenMissing() {
        // Create WITHOUT UIDocument pre-added
        _go = new GameObject("TestJSRunnerNoUIDoc");
        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Pre-create entry file
        var entryPath = _runner.EntryFileFullPath;
        var entryDir = Path.GetDirectoryName(entryPath);
        Directory.CreateDirectory(entryDir);
        File.WriteAllText(entryPath, SimpleConsoleLog);

        // Wait for Start() and deferred initialization
        yield return null;
        yield return null;

        // Should have added UIDocument
        var uiDoc = _go.GetComponent<UIDocument>();
        Assert.IsNotNull(uiDoc, "UIDocument should be created automatically");
    }

    [UnityTest]
    public IEnumerator Init_CreatesPanelSettings_WhenMissing() {
        // Create WITHOUT panel settings
        _go = new GameObject("TestJSRunnerNoPanelSettings");
        var uiDoc = _go.AddComponent<UIDocument>();
        // Don't assign panel settings

        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Pre-create entry file
        var entryPath = _runner.EntryFileFullPath;
        var entryDir = Path.GetDirectoryName(entryPath);
        Directory.CreateDirectory(entryDir);
        File.WriteAllText(entryPath, SimpleConsoleLog);

        // Wait for Start() and deferred initialization
        yield return null;
        yield return null;

        // Should have created panel settings
        Assert.IsNotNull(uiDoc.panelSettings, "PanelSettings should be created automatically");
    }

    [UnityTest]
    public IEnumerator Init_UsesAssignedPanelSettings_WhenProvided() {
        // Pre-create entry file
        var fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_WORKING_DIR);
        File.WriteAllText(Path.Combine(fullPath, "test.js"), SimpleConsoleLog);

        CreateJSRunner(SimpleConsoleLog);

        // Wait for Start()
        yield return null;

        // Should use our panel settings
        var uiDoc = _go.GetComponent<UIDocument>();
        Assert.AreEqual(_panelSettings, uiDoc.panelSettings, "Should use assigned PanelSettings");
    }

    // MARK: Script Execution Tests

    [UnityTest]
    public IEnumerator Run_ExecutesEntryFile_WhenExists() {
        CreateJSRunner(GlobalAccessTest);

        // Wait for Start() to run
        yield return null;
        yield return null;

        // Check script executed
        Assert.IsTrue(_runner.IsRunning, "Runner should be running");

        var result = _runner.Bridge.Eval("globalThis.__testResult");
        Assert.AreEqual("success", result, "Script should have executed and set global");
    }

    [UnityTest]
    public IEnumerator Run_ExposesRootElement_AsGlobal() {
        CreateJSRunner(RootElementTest);

        // Wait for Start() to run
        yield return null;
        yield return null;

        var result = _runner.Bridge.Eval("globalThis.__hasRoot");
        Assert.AreEqual("true", result, "__root should be exposed with valid handle");
    }

    [UnityTest]
    public IEnumerator Run_InjectsPlatformDefines() {
        CreateJSRunner(PlatformDefinesTest);

        // Wait for Start()
        yield return null;
        yield return null;

        // Check platform defines are injected
        var hasEditor = _runner.Bridge.Eval("globalThis.__platformTest.hasEditor");
        Assert.AreEqual("true", hasEditor, "UNITY_EDITOR should be defined");

        var editorValue = _runner.Bridge.Eval("globalThis.__platformTest.editorValue");
        Assert.AreEqual("true", editorValue, "UNITY_EDITOR should be true in Editor");
    }

    [UnityTest]
    public IEnumerator Run_InjectsCustomGlobals() {
        // Create runner first
        CreateJSRunner("globalThis.__textureExists = typeof myTexture !== 'undefined';");

        // Inject a custom global via reflection (since _globals is private)
        var globalsField = typeof(JSRunner).GetField("_globals", BindingFlags.NonPublic | BindingFlags.Instance);
        var globals = new List<GlobalEntry> {
            new GlobalEntry { key = "myTexture", value = Texture2D.whiteTexture }
        };
        globalsField.SetValue(_runner, globals);

        // Wait for Start()
        yield return null;
        yield return null;

        // Custom global should be available
        var result = _runner.Bridge.Eval("globalThis.__textureExists");
        Assert.AreEqual("true", result, "Custom globals should be injected");
    }

    // MARK: Reload Tests

    [UnityTest]
    public IEnumerator Reload_IncrementsReloadCount() {
        CreateJSRunner(GlobalAccessTest);

        // Wait for initial load
        yield return null;
        yield return null;

        Assert.AreEqual(0, _runner.ReloadCount, "Initial reload count should be 0");

        // Force reload
        _runner.ForceReload();
        yield return null;

        Assert.AreEqual(1, _runner.ReloadCount, "Reload count should increment");

        // Reload again
        _runner.ForceReload();
        yield return null;

        Assert.AreEqual(2, _runner.ReloadCount, "Reload count should increment again");
    }

    [UnityTest]
    public IEnumerator Reload_ClearsUITree() {
        const string AddElementScript = @"
var el = new CS.UnityEngine.UIElements.VisualElement();
el.name = 'TestElement';
__root.Add(el);
globalThis.__elementAdded = true;
";

        CreateJSRunner(AddElementScript);

        // Wait for initial load
        yield return null;
        yield return null;

        // Verify element was added
        var uiDoc = _go.GetComponent<UIDocument>();
        var element = uiDoc.rootVisualElement.Q("TestElement");
        Assert.IsNotNull(element, "Element should be added");

        // Modify the script to add a different element
        var entryPath = _runner.EntryFileFullPath;
        File.WriteAllText(entryPath, @"
var el = new CS.UnityEngine.UIElements.VisualElement();
el.name = 'NewElement';
__root.Add(el);
");

        // Force reload
        _runner.ForceReload();
        yield return null;

        // Old element should be gone
        var oldElement = uiDoc.rootVisualElement.Q("TestElement");
        Assert.IsNull(oldElement, "Old element should be cleared after reload");

        // New element should exist
        var newElement = uiDoc.rootVisualElement.Q("NewElement");
        Assert.IsNotNull(newElement, "New element should exist after reload");
    }

    [UnityTest]
    public IEnumerator Reload_PreservesGlobalConfiguration() {
        // Create runner with custom global
        CreateJSRunner("globalThis.__textureHandle = typeof myTexture !== 'undefined' ? myTexture.__csHandle : -1;");

        var globalsField = typeof(JSRunner).GetField("_globals", BindingFlags.NonPublic | BindingFlags.Instance);
        var globals = new List<GlobalEntry> {
            new GlobalEntry { key = "myTexture", value = Texture2D.whiteTexture }
        };
        globalsField.SetValue(_runner, globals);

        // Wait for initial load
        yield return null;
        yield return null;

        var initialHandle = _runner.Bridge.Eval("globalThis.__textureHandle");
        Assert.AreNotEqual("-1", initialHandle, "Global should be injected initially");

        // Force reload
        _runner.ForceReload();
        yield return null;

        // Global should still be available after reload
        var reloadHandle = _runner.Bridge.Eval("globalThis.__textureHandle");
        Assert.AreNotEqual("-1", reloadHandle, "Global should be re-injected after reload");
    }

    // MARK: Scaffolding Tests

    [UnityTest]
    public IEnumerator Scaffolding_CreatesWorkingDirectory() {
        var workingDirPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_WORKING_DIR);

        // Delete if exists
        if (Directory.Exists(workingDirPath)) {
            Directory.Delete(workingDirPath, true);
        }

        // Create runner - working dir should be created
        _go = new GameObject("TestJSRunner");
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Wait for Start()
        yield return null;
        yield return null;

        Assert.IsTrue(Directory.Exists(workingDirPath), "Working directory should be created");
    }

    [UnityTest]
    public IEnumerator Scaffolding_CreatesDefaultFiles_WhenConfigured() {
        // Create runner with default files configured
        _go = new GameObject("TestJSRunner");
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Create a TextAsset for default file content
        var testContent = new TextAsset();
        var textField = typeof(TextAsset).GetField("m_Script", BindingFlags.NonPublic | BindingFlags.Instance);
        textField?.SetValue(testContent, "// Default content");

        // Set default files via reflection
        var defaultFilesField = typeof(JSRunner).GetField("_defaultFiles", BindingFlags.NonPublic | BindingFlags.Instance);
        var defaultFiles = new List<DefaultFileEntry> {
            new DefaultFileEntry { path = "config/settings.json", content = testContent }
        };
        defaultFilesField.SetValue(_runner, defaultFiles);

        // Wait for Start()
        yield return null;
        yield return null;

        // Check that default file was created
        var expectedPath = Path.Combine(_runner.WorkingDirFullPath, "config", "settings.json");
        Assert.IsTrue(File.Exists(expectedPath), "Default file should be created");

        // Cleanup TextAsset
        UnityEngine.Object.Destroy(testContent);
    }

    [UnityTest]
    public IEnumerator Scaffolding_DoesNotOverwrite_ExistingFiles() {
        // Pre-create a file with custom content
        var workingDirPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_WORKING_DIR);
        Directory.CreateDirectory(workingDirPath);
        var existingFilePath = Path.Combine(workingDirPath, "existing.txt");
        File.WriteAllText(existingFilePath, "original content");

        // Create runner with default file that matches existing file
        _go = new GameObject("TestJSRunner");
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "test.js";

        // Create TextAsset with different content
        var testContent = new TextAsset();
        var textField = typeof(TextAsset).GetField("m_Script", BindingFlags.NonPublic | BindingFlags.Instance);
        textField?.SetValue(testContent, "new content that should not overwrite");

        // Set default files via reflection
        var defaultFilesField = typeof(JSRunner).GetField("_defaultFiles", BindingFlags.NonPublic | BindingFlags.Instance);
        var defaultFiles = new List<DefaultFileEntry> {
            new DefaultFileEntry { path = "existing.txt", content = testContent }
        };
        defaultFilesField.SetValue(_runner, defaultFiles);

        // Also create entry file so runner can start
        File.WriteAllText(Path.Combine(workingDirPath, "test.js"), SimpleConsoleLog);

        // Wait for Start()
        yield return null;
        yield return null;

        // Verify original content preserved
        var content = File.ReadAllText(existingFilePath);
        Assert.AreEqual("original content", content, "Existing files should not be overwritten");

        // Cleanup TextAsset
        UnityEngine.Object.Destroy(testContent);
    }

    // MARK: Bridge Exposure Tests

    [UnityTest]
    public IEnumerator Bridge_ExposedToJS_AsGlobal() {
        const string BridgeTest = "globalThis.__hasBridge = typeof __bridge !== 'undefined' && __bridge.__csHandle > 0;";

        CreateJSRunner(BridgeTest);

        // Wait for Start()
        yield return null;
        yield return null;

        var result = _runner.Bridge.Eval("globalThis.__hasBridge");
        Assert.AreEqual("true", result, "__bridge should be exposed with valid handle");
    }

    // MARK: Error Handling Tests

    [UnityTest]
    public IEnumerator Run_LogsWarning_WhenEntryFileMissing() {
        // Create runner but don't create entry file
        _go = new GameObject("TestJSRunner");
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        _runner = _go.AddComponent<JSRunner>();
        _runner.WorkingDir = TEST_WORKING_DIR;
        _runner.EntryFile = "nonexistent.js";

        // Wait for Start() - should create default entry file
        yield return null;
        yield return null;

        // JSRunner creates a default entry file if missing, so it should still run
        Assert.IsTrue(_runner.IsRunning, "Runner should run with default entry file");
    }
}
