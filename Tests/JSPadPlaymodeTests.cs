using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// PlayMode tests for JSPad MonoBehaviour.
/// Tests temp directory lifecycle, source code handling, build state, and execution.
/// </summary>
[TestFixture]
public class JSPadPlaymodeTests {
    // Simple test source (no external dependencies, just validates basic structure)
    const string SimpleSource = @"console.log('JSPad test');";

    GameObject _go;
    JSPad _pad;
    PanelSettings _panelSettings;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create PanelSettings at runtime (required for UIDocument)
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        // Stop pad if running
        if (_pad != null) {
            _pad.Stop();
        }

        if (_go != null) UnityEngine.Object.Destroy(_go);
        if (_panelSettings != null) UnityEngine.Object.Destroy(_panelSettings);

        QuickJSNative.ClearAllHandles();

        yield return null;
    }

    /// <summary>
    /// Create a JSPad with preconfigured settings.
    /// </summary>
    JSPad CreateJSPad(string sourceCode = null) {
        _go = new GameObject("TestJSPad");

        // Add UIDocument with panel settings
        var uiDoc = _go.AddComponent<UIDocument>();
        uiDoc.panelSettings = _panelSettings;

        // Add JSPad
        _pad = _go.AddComponent<JSPad>();

        if (sourceCode != null) {
            _pad.SourceCode = sourceCode;
        }

        return _pad;
    }

    // MARK: Temp Directory Tests

    [UnityTest]
    public IEnumerator TempDir_CreatesUniqueInstanceId() {
        CreateJSPad();

        // Wait for Start()
        yield return null;

        // Access TempDir to trigger instance ID creation
        var tempDir1 = _pad.TempDir;
        Assert.IsNotNull(tempDir1, "TempDir should not be null");
        Assert.IsTrue(tempDir1.Contains("OneJSPad"), "TempDir should contain OneJSPad");

        // Create second pad
        var go2 = new GameObject("TestJSPad2");
        var uiDoc2 = go2.AddComponent<UIDocument>();
        uiDoc2.panelSettings = _panelSettings;
        var pad2 = go2.AddComponent<JSPad>();

        yield return null;

        var tempDir2 = pad2.TempDir;

        // Instance IDs should be different
        Assert.AreNotEqual(tempDir1, tempDir2, "Each JSPad should have unique temp directory");

        // Cleanup
        UnityEngine.Object.Destroy(go2);
    }

    [UnityTest]
    public IEnumerator TempDir_GeneratesPackageJson() {
        CreateJSPad(SimpleSource);

        yield return null;

        // Initialize temp directory
        _pad.EnsureTempDirectory();

        var packageJsonPath = Path.Combine(_pad.TempDir, "package.json");
        Assert.IsTrue(File.Exists(packageJsonPath), "package.json should be created");

        var content = File.ReadAllText(packageJsonPath);
        Assert.IsTrue(content.Contains("\"name\""), "package.json should have name field");
        Assert.IsTrue(content.Contains("onejs-react"), "package.json should reference onejs-react");
        Assert.IsTrue(content.Contains("react"), "package.json should reference react");
    }

    [UnityTest]
    public IEnumerator TempDir_GeneratesTsConfig() {
        CreateJSPad(SimpleSource);

        yield return null;

        _pad.EnsureTempDirectory();

        var tsconfigPath = Path.Combine(_pad.TempDir, "tsconfig.json");
        Assert.IsTrue(File.Exists(tsconfigPath), "tsconfig.json should be created");

        var content = File.ReadAllText(tsconfigPath);
        Assert.IsTrue(content.Contains("\"jsx\""), "tsconfig.json should have jsx config");
        Assert.IsTrue(content.Contains("react-jsx"), "tsconfig.json should use react-jsx");
    }

    [UnityTest]
    public IEnumerator TempDir_GeneratesEsbuildConfig() {
        CreateJSPad(SimpleSource);

        yield return null;

        _pad.EnsureTempDirectory();

        var esbuildConfigPath = Path.Combine(_pad.TempDir, "esbuild.config.mjs");
        Assert.IsTrue(File.Exists(esbuildConfigPath), "esbuild.config.mjs should be created");

        var content = File.ReadAllText(esbuildConfigPath);
        Assert.IsTrue(content.Contains("esbuild"), "esbuild config should import esbuild");
        Assert.IsTrue(content.Contains("@outputs/app.js"), "esbuild config should output to @outputs/app.js");
    }

    // MARK: Source Code Tests

    [UnityTest]
    public IEnumerator WriteSource_CreatesIndexTsx() {
        CreateJSPad(SimpleSource);

        yield return null;

        _pad.WriteSourceFile();

        var indexPath = Path.Combine(_pad.TempDir, "index.tsx");
        Assert.IsTrue(File.Exists(indexPath), "index.tsx should be created");
    }

    [UnityTest]
    public IEnumerator WriteSource_PreservesContent() {
        const string TestContent = "// Test content\nconsole.log('preserved');";

        CreateJSPad(TestContent);

        yield return null;

        _pad.WriteSourceFile();

        var indexPath = Path.Combine(_pad.TempDir, "index.tsx");
        var content = File.ReadAllText(indexPath);
        Assert.AreEqual(TestContent, content, "Source content should be preserved exactly");
    }

    [UnityTest]
    public IEnumerator SourceCode_CanBeModified() {
        CreateJSPad("original");

        yield return null;

        _pad.SourceCode = "modified";

        Assert.AreEqual("modified", _pad.SourceCode, "SourceCode property should be modifiable");
    }

    // MARK: Build State Tests

    [UnityTest]
    public IEnumerator BuildState_StartsIdle() {
        CreateJSPad();

        yield return null;

        Assert.AreEqual(JSPad.BuildState.Idle, _pad.CurrentBuildState, "Initial build state should be Idle");
    }

    [UnityTest]
    public IEnumerator BuildState_TransitionsCorrectly() {
        CreateJSPad();

        yield return null;

        // Test all state transitions
        _pad.SetBuildState(JSPad.BuildState.InstallingDeps);
        Assert.AreEqual(JSPad.BuildState.InstallingDeps, _pad.CurrentBuildState);

        _pad.SetBuildState(JSPad.BuildState.Building);
        Assert.AreEqual(JSPad.BuildState.Building, _pad.CurrentBuildState);

        _pad.SetBuildState(JSPad.BuildState.Ready);
        Assert.AreEqual(JSPad.BuildState.Ready, _pad.CurrentBuildState);

        _pad.SetBuildState(JSPad.BuildState.Error, error: "Test error");
        Assert.AreEqual(JSPad.BuildState.Error, _pad.CurrentBuildState);
        Assert.AreEqual("Test error", _pad.LastBuildError);
    }

    [UnityTest]
    public IEnumerator HasBuiltBundle_ReturnsFalse_WhenNoBundle() {
        CreateJSPad();

        yield return null;

        Assert.IsFalse(_pad.HasBuiltBundle, "HasBuiltBundle should be false when no bundle exists");
    }

    [UnityTest]
    public IEnumerator HasBuiltBundle_ReturnsTrue_AfterSaveBundleToSerializedFields() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Manually create output file to simulate build
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        if (!Directory.Exists(outputDir)) {
            Directory.CreateDirectory(outputDir);
        }
        File.WriteAllText(_pad.OutputFile, "console.log('test');");

        // Save to serialized field (this is what the editor does after building)
        _pad.SaveBundleToSerializedFields();

        Assert.IsTrue(_pad.HasBuiltBundle, "HasBuiltBundle should be true after saving bundle");
    }

    // MARK: Execution Tests

    [UnityTest]
    public IEnumerator Reload_ExecutesBuiltBundle() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Create mock built output
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(_pad.OutputFile, "globalThis.__jsPadRan = true;");

        // Save to serialized field (simulating editor Build button)
        _pad.SaveBundleToSerializedFields();

        // Reload the bundle
        _pad.Reload();

        // Wait a frame for execution
        yield return null;

        Assert.IsTrue(_pad.IsRunning, "JSPad should be running after Reload");

        var result = _pad.Bridge.Eval("globalThis.__jsPadRan");
        Assert.AreEqual("true", result, "Built bundle should execute");
    }

    [UnityTest]
    public IEnumerator Reload_ExposesRootAndBridge() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Create mock built output that checks for globals
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(_pad.OutputFile, @"
globalThis.__hasRoot = typeof __root !== 'undefined' && __root.__csHandle > 0;
globalThis.__hasBridge = typeof __bridge !== 'undefined' && __bridge.__csHandle > 0;
");

        _pad.SaveBundleToSerializedFields();
        _pad.Reload();
        yield return null;

        var hasRoot = _pad.Bridge.Eval("globalThis.__hasRoot");
        var hasBridge = _pad.Bridge.Eval("globalThis.__hasBridge");

        Assert.AreEqual("true", hasRoot, "__root should be exposed");
        Assert.AreEqual("true", hasBridge, "__bridge should be exposed");
    }

    [UnityTest]
    public IEnumerator Stop_ClearsUI() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Create mock built output that adds UI element
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(_pad.OutputFile, @"
var el = new CS.UnityEngine.UIElements.VisualElement();
el.name = 'TestElement';
__root.Add(el);
");

        _pad.SaveBundleToSerializedFields();
        _pad.Reload();
        yield return null;

        // Verify element was added
        var uiDoc = _go.GetComponent<UIDocument>();
        var element = uiDoc.rootVisualElement.Q("TestElement");
        Assert.IsNotNull(element, "Element should be added");

        // Stop
        _pad.Stop();
        yield return null;

        // Element should be gone
        var elementAfterStop = uiDoc.rootVisualElement.Q("TestElement");
        Assert.IsNull(elementAfterStop, "UI should be cleared after Stop");
    }

    [UnityTest]
    public IEnumerator Stop_DisposesBridge() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Create mock built output
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(_pad.OutputFile, "console.log('test');");

        _pad.SaveBundleToSerializedFields();
        _pad.Reload();
        yield return null;

        Assert.IsTrue(_pad.IsRunning, "Should be running");
        Assert.IsNotNull(_pad.Bridge, "Bridge should exist");

        _pad.Stop();
        yield return null;

        Assert.IsFalse(_pad.IsRunning, "Should not be running after Stop");
        Assert.IsNull(_pad.Bridge, "Bridge should be null after Stop");
    }

    // MARK: Initialization Tests

    [UnityTest]
    public IEnumerator Init_RequireComponent_EnsuresUIDocument() {
        // RequireComponent ensures UIDocument is automatically added when JSPad is added
        _go = new GameObject("TestJSPadRequireComponent");
        _pad = _go.AddComponent<JSPad>();

        // UIDocument should be added immediately by RequireComponent
        var uiDoc = _go.GetComponent<UIDocument>();
        Assert.IsNotNull(uiDoc, "UIDocument should be added automatically by RequireComponent");

        yield return null;
    }

    [UnityTest]
    public IEnumerator Init_EmbeddedPanelSettings_IsCreatedAndAssigned() {
        // Create JSPad - it will create UIDocument via RequireComponent
        _go = new GameObject("TestJSPadEmbeddedPanelSettings");
        _pad = _go.AddComponent<JSPad>();

        // Wait for OnEnable
        yield return null;

        // Should have embedded PanelSettings
        Assert.IsNotNull(_pad.EmbeddedPanelSettings, "EmbeddedPanelSettings should be created");

        // UIDocument should have PanelSettings assigned
        var uiDoc = _go.GetComponent<UIDocument>();
        Assert.IsNotNull(uiDoc.panelSettings, "UIDocument.panelSettings should be assigned");
        Assert.AreEqual(_pad.EmbeddedPanelSettings, uiDoc.panelSettings, "UIDocument should use the embedded PanelSettings");
    }

    // MARK: Node Modules Tests

    [UnityTest]
    public IEnumerator HasNodeModules_ReturnsFalse_WhenNotInstalled() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        Assert.IsFalse(_pad.HasNodeModules(), "HasNodeModules should be false before npm install");
    }

    [UnityTest]
    public IEnumerator HasNodeModules_ReturnsTrue_WhenDirectoryExists() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Manually create node_modules directory
        var nodeModulesPath = Path.Combine(_pad.TempDir, "node_modules");
        Directory.CreateDirectory(nodeModulesPath);

        Assert.IsTrue(_pad.HasNodeModules(), "HasNodeModules should be true when directory exists");
    }

    // MARK: Platform Defines Tests

    [UnityTest]
    public IEnumerator Reload_InjectsPlatformDefines() {
        CreateJSPad();

        yield return null;

        _pad.EnsureTempDirectory();

        // Create mock built output
        var outputDir = Path.GetDirectoryName(_pad.OutputFile);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(_pad.OutputFile, @"
globalThis.__platformTest = {
    hasEditor: typeof UNITY_EDITOR !== 'undefined',
    editorValue: UNITY_EDITOR
};
");

        _pad.SaveBundleToSerializedFields();
        _pad.Reload();
        yield return null;

        var hasEditor = _pad.Bridge.Eval("globalThis.__platformTest.hasEditor");
        Assert.AreEqual("true", hasEditor, "UNITY_EDITOR should be defined");

        var editorValue = _pad.Bridge.Eval("globalThis.__platformTest.editorValue");
        Assert.AreEqual("true", editorValue, "UNITY_EDITOR should be true in Editor");
    }
}
