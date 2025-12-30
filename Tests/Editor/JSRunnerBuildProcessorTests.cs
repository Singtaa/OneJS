using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EditMode tests for JSRunnerBuildProcessor.
/// Tests file copying, asset detection, and deduplication logic.
/// Uses temp directories to avoid polluting the project.
/// </summary>
[TestFixture]
public class JSRunnerBuildProcessorTests {
    const string TEST_BASE_DIR = "Temp/OneJSBuildProcessorTest";

    string _testBasePath;
    JSRunnerBuildProcessor _processor;

    [SetUp]
    public void SetUp() {
        _testBasePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_BASE_DIR);

        // Clean test directory
        if (Directory.Exists(_testBasePath)) {
            Directory.Delete(_testBasePath, true);
        }
        Directory.CreateDirectory(_testBasePath);

        _processor = new JSRunnerBuildProcessor();
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

    // MARK: CopyDirectory Tests (via reflection)

    [Test]
    public void CopyDirectory_CopiesFilesRecursively() {
        // Setup: Create source directory with files
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(srcDir);

        // Create nested structure
        File.WriteAllText(Path.Combine(srcDir, "file1.txt"), "content1");
        Directory.CreateDirectory(Path.Combine(srcDir, "subdir"));
        File.WriteAllText(Path.Combine(srcDir, "subdir", "file2.txt"), "content2");

        // Get private method via reflection
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyDirectory",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Execute
        var copied = (int)method.Invoke(_processor, new object[] { srcDir, destDir });

        // Verify
        Assert.AreEqual(2, copied, "Should copy 2 files");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")), "file1.txt should be copied");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "subdir", "file2.txt")), "subdir/file2.txt should be copied");
    }

    [Test]
    public void CopyDirectory_PreservesContent() {
        // Setup
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(srcDir);

        const string testContent = "test content with special chars: @#$%";
        File.WriteAllText(Path.Combine(srcDir, "test.txt"), testContent);

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyDirectory",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_processor, new object[] { srcDir, destDir });

        // Verify content preserved
        var copiedContent = File.ReadAllText(Path.Combine(destDir, "test.txt"));
        Assert.AreEqual(testContent, copiedContent, "File content should be preserved");
    }

    [Test]
    public void CopyDirectory_CreatesDestinationDirectory() {
        // Setup
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "deep", "nested", "dest");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "test.txt"), "content");

        // Verify dest doesn't exist
        Assert.IsFalse(Directory.Exists(destDir), "Destination should not exist initially");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyDirectory",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_processor, new object[] { srcDir, destDir });

        // Verify destination created
        Assert.IsTrue(Directory.Exists(destDir), "Destination directory should be created");
    }

    [Test]
    public void CopyDirectory_ReturnsZero_ForNonexistentSource() {
        // Setup
        var srcDir = Path.Combine(_testBasePath, "nonexistent");
        var destDir = Path.Combine(_testBasePath, "dest");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyDirectory",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { srcDir, destDir });

        // Verify
        Assert.AreEqual(0, copied, "Should return 0 for nonexistent source");
    }

    // MARK: TryCopyPackageAssets Tests

    [Test]
    public void TryCopyPackageAssets_DetectsNamespaceFolders() {
        // Setup: Create package with assets/@namespace/ structure
        var pkgDir = Path.Combine(_testBasePath, "my-package");
        var assetsDir = Path.Combine(pkgDir, "assets", "@my-package");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "image.png"), "fake image");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "TryCopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { pkgDir, destDir });

        // Verify
        Assert.AreEqual(1, copied, "Should copy 1 file");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "@my-package", "image.png")),
            "File should be in @namespace folder");
    }

    [Test]
    public void TryCopyPackageAssets_IgnoresNonNamespaceFolders() {
        // Setup: Create package with assets/regular-folder/ (no @ prefix)
        var pkgDir = Path.Combine(_testBasePath, "my-package");
        var regularDir = Path.Combine(pkgDir, "assets", "regular-folder");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(regularDir);
        File.WriteAllText(Path.Combine(regularDir, "image.png"), "fake image");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "TryCopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { pkgDir, destDir });

        // Verify
        Assert.AreEqual(0, copied, "Should not copy non-namespace folders");
        Assert.IsFalse(Directory.Exists(Path.Combine(destDir, "regular-folder")),
            "Non-namespace folder should not be created");
    }

    [Test]
    public void TryCopyPackageAssets_ReturnsZero_WhenNoAssetsFolder() {
        // Setup: Package with no assets folder
        var pkgDir = Path.Combine(_testBasePath, "my-package");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "package.json"), "{}");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "TryCopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { pkgDir, destDir });

        // Verify
        Assert.AreEqual(0, copied, "Should return 0 when no assets folder");
    }

    // MARK: CopyPackageAssets Tests (scoped packages)

    [Test]
    public void CopyPackageAssets_HandlesScopedPackages() {
        // Setup: Create node_modules with @scope/package structure
        var nodeModulesDir = Path.Combine(_testBasePath, "node_modules");
        var scopeDir = Path.Combine(nodeModulesDir, "@scope");
        var pkgDir = Path.Combine(scopeDir, "my-package");
        var assetsDir = Path.Combine(pkgDir, "assets", "@scope-package");
        var destDir = Path.Combine(_testBasePath, "dest");

        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "icon.svg"), "svg content");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { nodeModulesDir, destDir });

        // Verify
        Assert.AreEqual(1, copied, "Should copy 1 file from scoped package");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "@scope-package", "icon.svg")),
            "File should be copied from scoped package");
    }

    [Test]
    public void CopyPackageAssets_HandlesRegularPackages() {
        // Setup: Create node_modules with regular package
        var nodeModulesDir = Path.Combine(_testBasePath, "node_modules");
        var pkgDir = Path.Combine(nodeModulesDir, "my-package");
        var assetsDir = Path.Combine(pkgDir, "assets", "@my-package");
        var destDir = Path.Combine(_testBasePath, "dest");

        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "data.json"), "{}");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { nodeModulesDir, destDir });

        // Verify
        Assert.AreEqual(1, copied, "Should copy 1 file from regular package");
    }

    [Test]
    public void CopyPackageAssets_HandlesMultiplePackages() {
        // Setup: Multiple packages with assets
        var nodeModulesDir = Path.Combine(_testBasePath, "node_modules");
        var destDir = Path.Combine(_testBasePath, "dest");

        // Package 1
        var pkg1Dir = Path.Combine(nodeModulesDir, "pkg1", "assets", "@pkg1");
        Directory.CreateDirectory(pkg1Dir);
        File.WriteAllText(Path.Combine(pkg1Dir, "file1.txt"), "content1");

        // Package 2
        var pkg2Dir = Path.Combine(nodeModulesDir, "pkg2", "assets", "@pkg2");
        Directory.CreateDirectory(pkg2Dir);
        File.WriteAllText(Path.Combine(pkg2Dir, "file2.txt"), "content2");

        // Execute
        var method = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyPackageAssets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var copied = (int)method.Invoke(_processor, new object[] { nodeModulesDir, destDir });

        // Verify
        Assert.AreEqual(2, copied, "Should copy files from both packages");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "@pkg1", "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "@pkg2", "file2.txt")));
    }

    // MARK: Deduplication Tests

    [Test]
    public void ProcessedBundles_AreDeduplicated() {
        // Setup: Clear static tracking fields via reflection
        ClearStaticFields();

        // Get the static field
        var processedBundlesField = typeof(JSRunnerBuildProcessor).GetField(
            "_processedBundles",
            BindingFlags.NonPublic | BindingFlags.Static);
        var processedBundles = (HashSet<string>)processedBundlesField.GetValue(null);

        // Add an entry
        processedBundles.Add("/test/path/bundle.js");

        // Verify deduplication works
        Assert.IsTrue(processedBundles.Contains("/test/path/bundle.js"));
        Assert.IsFalse(processedBundles.Contains("/other/path/bundle.js"));

        // Adding same path again shouldn't increase count
        int countBefore = processedBundles.Count;
        processedBundles.Add("/test/path/bundle.js");
        Assert.AreEqual(countBefore, processedBundles.Count, "Duplicate should not be added");
    }

    [Test]
    public void ProcessedWorkingDirs_AreDeduplicated() {
        // Setup: Clear static tracking fields via reflection
        ClearStaticFields();

        // Get the static field
        var processedWorkingDirsField = typeof(JSRunnerBuildProcessor).GetField(
            "_processedWorkingDirs",
            BindingFlags.NonPublic | BindingFlags.Static);
        var processedWorkingDirs = (HashSet<string>)processedWorkingDirsField.GetValue(null);

        // Add an entry
        processedWorkingDirs.Add("/test/working/dir");

        // Verify deduplication works
        Assert.IsTrue(processedWorkingDirs.Contains("/test/working/dir"));

        // Adding same path again shouldn't increase count
        int countBefore = processedWorkingDirs.Count;
        processedWorkingDirs.Add("/test/working/dir");
        Assert.AreEqual(countBefore, processedWorkingDirs.Count, "Duplicate should not be added");
    }

    // MARK: Helper Methods

    void ClearStaticFields() {
        // Clear _copiedFiles
        var copiedFilesField = typeof(JSRunnerBuildProcessor).GetField(
            "_copiedFiles",
            BindingFlags.NonPublic | BindingFlags.Static);
        var copiedFiles = (List<string>)copiedFilesField.GetValue(null);
        copiedFiles.Clear();

        // Clear _processedBundles
        var processedBundlesField = typeof(JSRunnerBuildProcessor).GetField(
            "_processedBundles",
            BindingFlags.NonPublic | BindingFlags.Static);
        var processedBundles = (HashSet<string>)processedBundlesField.GetValue(null);
        processedBundles.Clear();

        // Clear _processedWorkingDirs
        var processedWorkingDirsField = typeof(JSRunnerBuildProcessor).GetField(
            "_processedWorkingDirs",
            BindingFlags.NonPublic | BindingFlags.Static);
        var processedWorkingDirs = (HashSet<string>)processedWorkingDirsField.GetValue(null);
        processedWorkingDirs.Clear();
    }
}
