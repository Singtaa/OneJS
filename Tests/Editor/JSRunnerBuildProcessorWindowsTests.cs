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
    /// The half of the asset commit that only Windows can test.
    ///
    /// POSIX ties the right to delete a file to the DIRECTORY's permissions, so a
    /// read only file deletes without complaint on macOS and Linux and neither of
    /// these tests can fail there. That asymmetry is exactly how the bug they
    /// cover reached a release: the copy kept the read only attribute, Windows
    /// refused to delete the result, and the failure was invisible to everyone
    /// developing on a Mac.
    ///
    /// A separate fixture rather than more cases in JSRunnerBuildProcessorTests,
    /// so the platform gate sits on the fixture and there is one place to look
    /// when something here is skipped.
    /// </summary>
    [TestFixture]
    [Platform("Win", Reason = "Windows refuses to delete a read only file; POSIX does not, so these cannot fail there.")]
    public class JSRunnerBuildProcessorWindowsTests {
        const string TEST_BASE_DIR = "Temp/OneJSBuildProcessorWinTest";

        string _testBasePath;
        JSRunnerBuildProcessor _processor;
        MethodInfo _commitAssets;
        IList _assetSources;

        string Dest => Path.Combine(_testBasePath, "dest");
        string App => Path.Combine(_testBasePath, "app");

        [SetUp]
        public void SetUp() {
            _testBasePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), TEST_BASE_DIR);
            DeleteTree(_testBasePath);
            Directory.CreateDirectory(_testBasePath);

            _processor = new JSRunnerBuildProcessor();
            _commitAssets = typeof(JSRunnerBuildProcessor).GetMethod(
                "CommitAssetsTo", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_commitAssets,
                "JSRunnerBuildProcessor.CommitAssetsTo(string) was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            _assetSources = (IList)typeof(JSRunnerBuildProcessor)
                .GetField("_assetSources", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            Assert.IsNotNull(_assetSources, "JSRunnerBuildProcessor._assetSources was not found via reflection.");
            _assetSources.Clear();
        }

        [TearDown]
        public void TearDown() {
            // Clears read only as it goes, or a test that left some behind would
            // fail the NEXT test's SetUp rather than itself.
            DeleteTree(_testBasePath);
            _assetSources?.Clear();
        }

        /// <summary>
        /// A destination left read only by an older build is replaced, not refused.
        ///
        /// This is the state a Perforce workspace is in, and the state every build
        /// before the attribute was cleared on copy left behind. Asserts the
        /// CONTENT changed rather than only that the call returned: a commit that
        /// silently kept the old file would otherwise read as a pass.
        /// </summary>
        [Test]
        public void CommitAssets_ReadOnlyDestination_IsReplacedRatherThanRefused() {
            Write(App, "a.png", "first");
            AddSource(App, "App");
            InvokeCommitAssets(Dest);

            var copied = Path.Combine(Dest, "a.png");
            Assert.IsTrue(File.Exists(copied));
            File.SetAttributes(copied, FileAttributes.ReadOnly);

            File.WriteAllText(Path.Combine(App, "a.png"), "second");

            Assert.DoesNotThrow(() => InvokeCommitAssets(Dest),
                "a read only destination should be cleared and replaced, not refused");
            Assert.AreEqual("second", File.ReadAllText(copied),
                "the destination still holds the previous build's bytes, so the commit did not replace it");
            Assert.IsFalse(File.GetAttributes(copied).HasFlag(FileAttributes.ReadOnly),
                "the destination copy is read only again, so the NEXT build cannot replace it");
        }

        /// <summary>
        /// A destination that genuinely cannot be replaced still stops the build.
        ///
        /// An open handle with no sharing is the one thing clearing an attribute
        /// cannot get past, and it is what an editor, an importer or a scanner
        /// holding a file looks like. The type matters more than the failure:
        /// Unity only ABORTS a build for BuildFailedException, and anything else
        /// out of OnPreprocessBuild is an error line the build carries on past,
        /// which is how a green build shipped the previous build's assets twice
        /// during this fix.
        /// </summary>
        [Test]
        public void CommitAssets_UnreplaceableDestination_RaisesBuildFailedException() {
            Write(App, "a.png", "first");
            AddSource(App, "App");
            InvokeCommitAssets(Dest);

            var copied = Path.Combine(Dest, "a.png");
            using (File.Open(copied, FileMode.Open, FileAccess.Read, FileShare.None)) {
                var e = Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(Dest),
                    "an unreplaceable destination must raise BuildFailedException, the only type Unity aborts a build for");
                StringAssert.Contains("StreamingAssets/onejs/assets", e.Message,
                    "the message should name the folder the user has to deal with");
            }

            Assert.IsFalse(Directory.Exists(Dest + ".staging"),
                "the staging folder was left under Assets, where Unity will import it and ship it in the player");
        }

        // ---------------------------------------------------------------- helpers

        void InvokeCommitAssets(string destDir) {
            try {
                _commitAssets.Invoke(_processor, new object[] { destDir });
            } catch (TargetInvocationException e) {
                throw e.InnerException;
            }
        }

        /// <summary>_assetSources is a List of a private tuple type, so it is built by reflection.</summary>
        void AddSource(string srcDir, string runnerName) {
            var elem = _assetSources.GetType().GetGenericArguments()[0];
            _assetSources.Add(Activator.CreateInstance(elem, srcDir, runnerName));
        }

        static void Write(string dir, string relative, string content) {
            var full = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        static void DeleteTree(string dir) {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(dir, true);
        }
    }
}
