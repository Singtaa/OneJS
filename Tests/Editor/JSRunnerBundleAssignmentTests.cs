using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using OneJS;
using OneJS.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for which runners come out of a build with a bundle.
    ///
    /// The bundle FILE is written once per app, but the reference to it is
    /// serialized on each JSRunner component, so a build has to assign it once
    /// per runner. Two runners share an app whenever they share a PanelSettings
    /// asset, which is what the instance folder is keyed off, and the everyday
    /// way into that is one prefab dropped into several build scenes.
    ///
    /// Unlike the sibling JSRunnerBuildProcessorTests, these need a real
    /// AssetDatabase: the thing under test is a TextAsset reference, which only
    /// exists for a file under Assets. So the fixture builds a throwaway project
    /// folder there and deletes it again.
    /// </summary>
    [TestFixture]
    public class JSRunnerBundleAssignmentTests {
        const string FIXTURE_ROOT = "Assets/OneJSBundleAssignmentFixture";

        string _appFolder;
        PanelSettings _panelSettings;
        MethodInfo _processJSRunner;
        HashSet<string> _processedRunners;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() {
            _processJSRunner = typeof(JSRunnerBuildProcessor).GetMethod(
                "ProcessJSRunner", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_processJSRunner,
                "JSRunnerBuildProcessor.ProcessJSRunner(JSRunner) was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            // Static and shared across a whole build, so one test would otherwise
            // leave a path behind that makes the next one take the already-written
            // branch for free, which is the exact branch under test.
            _processedRunners = (HashSet<string>)typeof(JSRunnerBuildProcessor)
                .GetField("_processedRunners", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            Assert.IsNotNull(_processedRunners,
                "JSRunnerBuildProcessor._processedRunners was not found via reflection.");
            _processedRunners.Clear();

            DeleteFixture();

            // A project folder as JSRunner defines one: a PanelSettings asset,
            // with app.js.txt beside it. IsPanelSettingsInValidProjectFolder wants
            // the second, so InstanceFolder is null without it and the processor
            // bails long before the branch these tests are about.
            _appFolder = FIXTURE_ROOT + "/App";
            Directory.CreateDirectory(ToFullPath(_appFolder));
            File.WriteAllText(ToFullPath(_appFolder + "/app.js.txt"), "// fixture bundle");
            File.WriteAllText(ToFullPath(_appFolder + "/app.js.map.txt"), "{\"version\":3}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(_panelSettings, _appFolder + "/PanelSettings.asset");
            AssetDatabase.ImportAsset(_appFolder + "/PanelSettings.asset",
                ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _spawned) {
                if (go != null) Object.DestroyImmediate(go);
            }
            _spawned.Clear();
            _processedRunners.Clear();
            DeleteFixture();
        }

        static string ToFullPath(string assetPath) {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        }

        static void DeleteFixture() {
            if (AssetDatabase.IsValidFolder(FIXTURE_ROOT)) {
                AssetDatabase.DeleteAsset(FIXTURE_ROOT);
            }
            var full = ToFullPath(FIXTURE_ROOT);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            var meta = full + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// A runner that is never enabled, so [ExecuteAlways] does not start an
        /// edit-mode preview (and a QuickJS context with it) behind the test.
        /// SetActive(false) precedes AddComponent because OnEnable fires during
        /// AddComponent on an active GameObject.
        /// </summary>
        JSRunner SpawnRunner(string name) {
            var go = new GameObject(name);
            go.SetActive(false);
            go.hideFlags = HideFlags.HideAndDontSave;
            _spawned.Add(go);
            var runner = go.AddComponent<JSRunner>();
            runner.SetPanelSettings(_panelSettings);
            return runner;
        }

        bool InvokeProcess(JSRunnerBuildProcessor processor, JSRunner runner) {
            return (bool)_processJSRunner.Invoke(processor, new object[] { runner });
        }

        [Test]
        public void TwoRunnersSharingAnApp_BothGetTheBundle() {
            var processor = new JSRunnerBuildProcessor();
            var first = SpawnRunner("FirstRunner");
            var second = SpawnRunner("SecondRunner");

            Assert.IsTrue(InvokeProcess(processor, first),
                "The first runner should have had its bundle built and assigned.");
            InvokeProcess(processor, second);

            Assert.IsNotNull(first.BundleAsset, "The first runner has no bundle.");
            Assert.IsNotNull(second.BundleAsset,
                "The second runner sharing this app has no bundle, so it would run OneJS's " +
                "default placeholder app in the player. The file was already written by the " +
                "first runner; the reference is per component and still has to be assigned.");
            Assert.AreSame(first.BundleAsset, second.BundleAsset,
                "Both runners resolve to one app, so both should point at the same TextAsset.");
        }

        [Test]
        public void ThirdRunnerSharingAnApp_AlsoGetsTheBundle() {
            // Two is the case that was broken; three guards the fix being written
            // as "assign the second one too" rather than "assign every one".
            var processor = new JSRunnerBuildProcessor();
            var runners = new[] { SpawnRunner("A"), SpawnRunner("B"), SpawnRunner("C") };

            foreach (var runner in runners) InvokeProcess(processor, runner);

            foreach (var runner in runners) {
                Assert.IsNotNull(runner.BundleAsset, $"Runner {runner.gameObject.name} has no bundle.");
            }
        }

        [Test]
        public void ASingleRunner_StillGetsTheBundleAndSourceMap() {
            var processor = new JSRunnerBuildProcessor();
            var runner = SpawnRunner("OnlyRunner");

            Assert.IsTrue(InvokeProcess(processor, runner));

            Assert.IsNotNull(runner.BundleAsset, "The only runner in the build has no bundle.");
            Assert.AreEqual("// fixture bundle", runner.BundleAsset.text,
                "The assigned TextAsset is not the app's bundle.");
        }

        [Test]
        public void ARunnerWithAPreassignedBundle_IsLeftAlone() {
            // The early return for an already-assigned bundle is how a user pins a
            // bundle by hand, so the fix must not start overwriting it.
            var processor = new JSRunnerBuildProcessor();
            var runner = SpawnRunner("PinnedRunner");
            var pinned = new TextAsset("pinned by hand");
            runner.SetBundleAsset(pinned);

            Assert.IsFalse(InvokeProcess(processor, runner),
                "A runner with a bundle already assigned should report no work done.");
            Assert.AreSame(pinned, runner.BundleAsset, "A hand-assigned bundle was overwritten.");

            runner.SetBundleAsset(null);
            Object.DestroyImmediate(pinned);
        }
    }
}
