using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor.Build;
using UnityEngine;

namespace OneJS.Tests.Editor {
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
        MethodInfo _commitAssets;
        IList _assetSources;

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

            _commitAssets = typeof(JSRunnerBuildProcessor).GetMethod(
                "CommitAssetsTo", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_commitAssets,
                "JSRunnerBuildProcessor.CommitAssetsTo(string) was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            // Static and shared across a whole build, so one test could otherwise
            // leave a source behind that makes the next one fail, or pass for free.
            _assetSources = (IList)typeof(JSRunnerBuildProcessor)
                .GetField("_assetSources", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            Assert.IsNotNull(_assetSources, "JSRunnerBuildProcessor._assetSources was not found via reflection.");
            _assetSources.Clear();
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

        // Reflection wraps a throw in TargetInvocationException, so unwrap it: a
        // test asserting BuildFailedException should see BuildFailedException.
        void InvokeCommitAssets(string destDir) {
            try {
                _commitAssets.Invoke(_processor, new object[] { destDir });
            } catch (TargetInvocationException e) {
                throw e.InnerException;
            }
        }

        // The processor reads _assetSources, a List of a private tuple type, so the
        // entries are built by reflection rather than named here.
        void AddSource(string srcDir, string runnerName) {
            var elem = _assetSources.GetType().GetGenericArguments()[0];
            _assetSources.Add(Activator.CreateInstance(elem, srcDir, runnerName));
        }

        static void Write(string dir, string relative, string content) {
            var full = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
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
        // MARK: CommitAssets Tests (the multi-app bug, Discord 2026-09-14)

        string Dest => Path.Combine(_testBasePath, "dest");

        /// <summary>
        /// Two apps in one build, both of their assets in the player.
        ///
        /// The copy used to delete the shared destination before every runner, so
        /// with more than one app only the last one's assets survived. Red against
        /// that code: app A's file is gone by the time this asserts.
        /// </summary>
        [Test]
        public void CommitAssets_TwoRunners_BothSurvive() {
            Write(Path.Combine(_testBasePath, "appA"), "a.png", "A");
            Write(Path.Combine(_testBasePath, "appB"), Path.Combine("nested", "b.png"), "B");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            AddSource(Path.Combine(_testBasePath, "appB"), "AppB");

            InvokeCommitAssets(Dest);

            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")), "AppA's asset was erased by AppB");
            Assert.IsTrue(File.Exists(Path.Combine(Dest, "nested", "b.png")), "AppB's asset is missing");
            Assert.AreEqual("A", File.ReadAllText(Path.Combine(Dest, "a.png")));
        }

        [Test]
        public void CommitAssets_SamePathFromTwoRunners_FailsNamingBoth() {
            Write(Path.Combine(_testBasePath, "appA"), Path.Combine("img", "logo.png"), "A");
            Write(Path.Combine(_testBasePath, "appB"), Path.Combine("img", "logo.png"), "B");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            AddSource(Path.Combine(_testBasePath, "appB"), "AppB");

            var e = Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(Dest));

            StringAssert.Contains("img/logo.png", e.Message);
            StringAssert.Contains("AppA", e.Message);
            StringAssert.Contains("AppB", e.Message);
        }

        /// <summary>
        /// Windows and macOS both resolve Logo.png and logo.png to one file, so a
        /// case-only difference is a collision there. Detecting it only on Linux
        /// would mean CI passing while every developer machine ships one app's
        /// texture under the other app's name.
        /// </summary>
        [Test]
        public void CommitAssets_CaseOnlyCollision_Fails() {
            Write(Path.Combine(_testBasePath, "appA"), "Logo.png", "A");
            Write(Path.Combine(_testBasePath, "appB"), "logo.png", "B");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            AddSource(Path.Combine(_testBasePath, "appB"), "AppB");

            var e = Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(Dest));

            StringAssert.Contains("differ only in case", e.Message);
        }

        /// <summary>
        /// An app deleted since the last build must take its files with it. A merge
        /// that never clears would keep shipping them in every build forever, which
        /// is the trap in simply removing the delete.
        /// </summary>
        [Test]
        public void CommitAssets_RunnerRemovedSinceLastBuild_LeavesNoStaleFile() {
            Write(Path.Combine(_testBasePath, "appA"), "a.png", "A");
            Write(Path.Combine(_testBasePath, "appB"), "b.png", "B");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            AddSource(Path.Combine(_testBasePath, "appB"), "AppB");
            InvokeCommitAssets(Dest);
            Assert.IsTrue(File.Exists(Path.Combine(Dest, "b.png")), "setup failed");

            // AppB is gone from the project; build again.
            _assetSources.Clear();
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            InvokeCommitAssets(Dest);

            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")));
            Assert.IsFalse(File.Exists(Path.Combine(Dest, "b.png")),
                "a removed app's asset is still being shipped");
        }

        /// <summary>
        /// Nothing is deleted until the replacement is complete on disk, so a build
        /// that fails leaves the previous one's folder intact rather than a partial
        /// one that the next build would treat as real.
        /// </summary>
        [Test]
        public void CommitAssets_FailedBuild_LeavesThePreviousFolderIntact() {
            Write(Path.Combine(_testBasePath, "appA"), "a.png", "A");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            InvokeCommitAssets(Dest);

            // Now a build that collides: it must not touch what is already there.
            Write(Path.Combine(_testBasePath, "appB"), "shared.png", "B");
            Write(Path.Combine(_testBasePath, "appC"), "shared.png", "C");
            _assetSources.Clear();
            AddSource(Path.Combine(_testBasePath, "appB"), "AppB");
            AddSource(Path.Combine(_testBasePath, "appC"), "AppC");
            Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(Dest));

            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")),
                "the failed build destroyed the previous build's assets");
            Assert.AreEqual("A", File.ReadAllText(Path.Combine(Dest, "a.png")));
            Assert.IsFalse(Directory.Exists(Dest + ".staging"), "staging folder was left behind");
        }

        /// <summary>
        /// One app reached twice is not two apps disagreeing: two JSRunners can
        /// share a project folder, and a runner can sit in more than one build
        /// scene. Keying the collision on the runner name alone failed those builds
        /// outright, which would be a worse bug than the one being fixed.
        /// </summary>
        [Test]
        public void CommitAssets_SameSourceTwice_IsNotACollision() {
            Write(Path.Combine(_testBasePath, "app"), "a.png", "A");
            AddSource(Path.Combine(_testBasePath, "app"), "AppInSceneOne");
            AddSource(Path.Combine(_testBasePath, "app"), "AppInSceneTwo");

            Assert.DoesNotThrow(() => InvokeCommitAssets(Dest));
            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")));
        }

        /// <summary>
        /// A runner with no assets folder records no source, so it cannot erase
        /// the apps that do. Under the old code the survivor was "the last runner
        /// that HAS assets", which made the bug look intermittent: adding an
        /// asset-less app changed nothing, adding one with assets lost the others.
        /// </summary>
        [Test]
        public void CommitAssets_RunnerWithoutAssets_ErasesNothing() {
            Write(Path.Combine(_testBasePath, "appA"), "a.png", "A");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");
            InvokeCommitAssets(Dest);

            // AppB ships no assets folder at all, so CopyAssets records no source
            // for it. The commit still runs, because the build had runners.
            InvokeCommitAssets(Dest);

            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")),
                "an app with no assets folder erased an app that has one");
        }

        /// <summary>
        /// Anything that goes wrong while replacing the folder has to stop the
        /// build, not just log.
        ///
        /// Unity aborts a build only for BuildFailedException; any other exception
        /// out of OnPreprocessBuild is an error line and the build carries on. On
        /// Windows a read only file in the destination made Directory.Delete throw,
        /// and the build then shipped the PREVIOUS build's assets under a green
        /// result. Found by a real Windows player build.
        ///
        /// The trigger is not portable, which is worth knowing: POSIX ties the
        /// right to delete a file to the DIRECTORY's permissions, so the same read
        /// only file is removed without complaint on macOS and Linux. So this
        /// asserts the thing that was actually changed, that a failure inside the
        /// commit surfaces as BuildFailedException, using a destination that
        /// cannot be created on any platform.
        /// </summary>
        [Test]
        public void CommitAssets_DestinationCannotBeReplaced_FailsTheBuild() {
            Write(Path.Combine(_testBasePath, "appA"), "a.png", "A");
            AddSource(Path.Combine(_testBasePath, "appA"), "AppA");

            // The destination's parent is a regular file, so neither it nor its
            // staging sibling can be made.
            var blocker = Path.Combine(_testBasePath, "blocker");
            File.WriteAllText(blocker, "not a directory");
            var impossible = Path.Combine(blocker, "assets");

            var e = Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(impossible),
                "a destination that cannot be replaced must stop the build, not be logged past");
            StringAssert.Contains("Could not rebuild StreamingAssets/onejs/assets", e.Message);
        }

        /// <summary>
        /// A read only source asset must not leave a read only file in the
        /// destination.
        ///
        /// File.Copy keeps the attribute, and Windows will not delete a read only
        /// file, so the copy poisoned the NEXT build: its delete threw, and the
        /// cleanup in the catch threw too and replaced the BuildFailedException
        /// with something Unity does not abort for. Green build, previous build's
        /// assets, staging folder left under Assets. A Perforce workspace hit it
        /// every second build.
        ///
        /// Asserted on the attribute rather than on the failure, because the
        /// failure is Windows only: POSIX ties the right to delete to the
        /// directory, so a Mac deletes a read only file without complaint and
        /// could never show the bug. The attribute is observable everywhere.
        /// </summary>
        [Test]
        public void CommitAssets_ReadOnlySource_LeavesAWritableDestination() {
            var app = Path.Combine(_testBasePath, "app");
            Write(app, "a.png", "A");
            File.SetAttributes(Path.Combine(app, "a.png"), FileAttributes.ReadOnly);
            AddSource(app, "App");

            try {
                InvokeCommitAssets(Dest);

                var copied = Path.Combine(Dest, "a.png");
                Assert.IsTrue(File.Exists(copied));
                Assert.IsFalse(File.GetAttributes(copied).HasFlag(FileAttributes.ReadOnly),
                    "the destination copy is read only, so the next build cannot replace it on Windows");

                // And the next build must go through rather than throw.
                Assert.DoesNotThrow(() => InvokeCommitAssets(Dest));
            } finally {
                File.SetAttributes(Path.Combine(app, "a.png"), FileAttributes.Normal);
            }
        }

        [Test]
        public void CommitAssets_SkipsMetaFiles() {
            Write(Path.Combine(_testBasePath, "app"), "a.png", "A");
            Write(Path.Combine(_testBasePath, "app"), "a.png.meta", "meta");
            AddSource(Path.Combine(_testBasePath, "app"), "App");

            InvokeCommitAssets(Dest);

            Assert.IsTrue(File.Exists(Path.Combine(Dest, "a.png")));
            Assert.IsFalse(File.Exists(Path.Combine(Dest, "a.png.meta")));
        }

    }
}
