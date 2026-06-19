using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for JSRunnerBuildProcessor's asset-copy logic.
/// Exercises the private CopyDirectoryRecursive helper via reflection, using temp
/// directories so the project is never touched. The MethodInfo is resolved (and
/// asserted) in SetUp, so a future rename surfaces as a clear message here rather
/// than a cryptic NullReferenceException inside each test.
/// </summary>
[TestFixture]
public class JSRunnerBuildProcessorTests {
    const string TEST_BASE_DIR = "Temp/OneJSBuildProcessorTest";

    string _testBasePath;
    JSRunnerBuildProcessor _processor;
    MethodInfo _copyDirectoryRecursive;

    [SetUp]
    public void SetUp() {
        _testBasePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_BASE_DIR);

        // Clean test directory
        if (Directory.Exists(_testBasePath)) {
            Directory.Delete(_testBasePath, true);
        }
        Directory.CreateDirectory(_testBasePath);

        _processor = new JSRunnerBuildProcessor();
        _copyDirectoryRecursive = typeof(JSRunnerBuildProcessor).GetMethod(
            "CopyDirectoryRecursive",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(_copyDirectoryRecursive,
            "JSRunnerBuildProcessor.CopyDirectoryRecursive(string, string) was not found via reflection - " +
            "these tests are out of sync with the implementation.");
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

    int InvokeCopyDirectoryRecursive(string src, string dest) {
        return (int)_copyDirectoryRecursive.Invoke(_processor, new object[] { src, dest });
    }

    // MARK: CopyDirectoryRecursive Tests (via reflection)

    [Test]
    public void CopyDirectoryRecursive_CopiesFilesRecursively() {
        // Setup: Create source directory with a nested structure
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(srcDir, "file1.txt"), "content1");
        Directory.CreateDirectory(Path.Combine(srcDir, "subdir"));
        File.WriteAllText(Path.Combine(srcDir, "subdir", "file2.txt"), "content2");

        var copied = InvokeCopyDirectoryRecursive(srcDir, destDir);

        Assert.AreEqual(2, copied, "Should copy 2 files");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "file1.txt")), "file1.txt should be copied");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "subdir", "file2.txt")), "subdir/file2.txt should be copied");
    }

    [Test]
    public void CopyDirectoryRecursive_PreservesContent() {
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(srcDir);

        const string testContent = "test content with special chars: @#$%";
        File.WriteAllText(Path.Combine(srcDir, "test.txt"), testContent);

        InvokeCopyDirectoryRecursive(srcDir, destDir);

        var copiedContent = File.ReadAllText(Path.Combine(destDir, "test.txt"));
        Assert.AreEqual(testContent, copiedContent, "File content should be preserved");
    }

    [Test]
    public void CopyDirectoryRecursive_CreatesDestinationDirectory() {
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "deep", "nested", "dest");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "test.txt"), "content");

        Assert.IsFalse(Directory.Exists(destDir), "Destination should not exist initially");

        InvokeCopyDirectoryRecursive(srcDir, destDir);

        Assert.IsTrue(Directory.Exists(destDir), "Destination directory should be created");
    }

    [Test]
    public void CopyDirectoryRecursive_SkipsMetaFiles() {
        // .meta files are Unity import sidecars and must not be copied into StreamingAssets.
        var srcDir = Path.Combine(_testBasePath, "src");
        var destDir = Path.Combine(_testBasePath, "dest");
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(srcDir, "keep.png"), "image");
        File.WriteAllText(Path.Combine(srcDir, "keep.png.meta"), "meta");

        var copied = InvokeCopyDirectoryRecursive(srcDir, destDir);

        Assert.AreEqual(1, copied, "Should copy only the non-.meta file");
        Assert.IsTrue(File.Exists(Path.Combine(destDir, "keep.png")), "Asset file should be copied");
        Assert.IsFalse(File.Exists(Path.Combine(destDir, "keep.png.meta")), ".meta sidecar should be skipped");
    }
}
