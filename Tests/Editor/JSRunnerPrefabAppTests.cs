using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for apps a build reaches through a prefab rather than a scene,
    /// and for the Include In Build opt out.
    ///
    /// The rule these pin down is one sentence: every JSRunner in a scene or a
    /// prefab is built unless it opts out. The fixture writes real prefabs under
    /// Assets because the thing under test is a serialized TextAsset reference on a
    /// prefab asset, which only exists for files the AssetDatabase can see.
    /// </summary>
    [TestFixture]
    public class JSRunnerPrefabAppTests {
        const string FIXTURE_ROOT = "Assets/OneJSPrefabAppFixture";

        JSRunnerBuildProcessor _processor;
        MethodInfo _processPrefabs;
        MethodInfo _commitAssetsTo;
        HashSet<string> _processedRunners;
        IList _assetSources;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() {
            _processor = new JSRunnerBuildProcessor();

            _processPrefabs = typeof(JSRunnerBuildProcessor).GetMethod(
                "ProcessPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_processPrefabs,
                "JSRunnerBuildProcessor.ProcessPrefabs() was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            _commitAssetsTo = typeof(JSRunnerBuildProcessor).GetMethod(
                "CommitAssetsTo", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_commitAssetsTo, "JSRunnerBuildProcessor.CommitAssetsTo(string) was not found.");

            _processedRunners = (HashSet<string>)typeof(JSRunnerBuildProcessor)
                .GetField("_processedRunners", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            _assetSources = (IList)typeof(JSRunnerBuildProcessor)
                .GetField("_assetSources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Assert.IsNotNull(_processedRunners, "_processedRunners was not found via reflection.");
            Assert.IsNotNull(_assetSources, "_assetSources was not found via reflection.");
            _processedRunners.Clear();
            _assetSources.Clear();

            DeleteFixture();
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(FIXTURE_ROOT));
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _spawned) {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();
            _processedRunners.Clear();
            _assetSources.Clear();
            DeleteFixture();
        }

        static string ToFullPath(string assetPath) {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        }

        static void DeleteFixture() {
            if (AssetDatabase.IsValidFolder(FIXTURE_ROOT)) AssetDatabase.DeleteAsset(FIXTURE_ROOT);
            var full = ToFullPath(FIXTURE_ROOT);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            var meta = full + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// An app folder as JSRunner defines one: a PanelSettings asset with
        /// app.js.txt beside it, plus an optional ~/assets file so the app has
        /// something to ship.
        /// </summary>
        PanelSettings CreateApp(string appName, string assetFileName = null, string assetBody = "x") {
            var folder = FIXTURE_ROOT + "/" + appName;
            Directory.CreateDirectory(ToFullPath(folder));
            File.WriteAllText(ToFullPath(folder + "/app.js.txt"), $"// {appName} bundle");

            if (assetFileName != null) {
                var assetsDir = ToFullPath(folder + "/~/assets");
                Directory.CreateDirectory(assetsDir);
                File.WriteAllText(Path.Combine(assetsDir, assetFileName), assetBody);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, folder + "/PanelSettings.asset");
            AssetDatabase.ImportAsset(folder + "/PanelSettings.asset", ImportAssetOptions.ForceSynchronousImport);
            return ps;
        }

        /// <summary>
        /// Saves a prefab carrying a JSRunner and returns the PREFAB ASSET's runner,
        /// which is the object a build has to write the bundle onto. The scene object
        /// it was built from is destroyed, so nothing here is reachable from a scene.
        /// </summary>
        JSRunner CreatePrefabWithRunner(string prefabName, PanelSettings ps,
            bool includeInBuild = true, bool activeSelf = true) {
            // Kept inactive for its whole life in the scene. JSRunner's OnEnable
            // schedules an edit-mode preview, which would start a QuickJS context
            // behind the test, so the source object is never enabled.
            var go = new GameObject(prefabName);
            go.SetActive(false);
            _spawned.Add(go);
            var runner = go.AddComponent<JSRunner>();
            runner.SetPanelSettings(ps);
            if (!includeInBuild) SetIncludeInBuild(runner, false);

            var path = FIXTURE_ROOT + "/" + prefabName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            _spawned.Remove(go);
            UnityEngine.Object.DestroyImmediate(go);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(reloaded, $"Prefab {path} did not import.");

            // activeSelf is set on the prefab ASSET rather than on the source object,
            // for the same reason: a prefab asset belongs to no scene, so setting it
            // here enables nothing.
            if (reloaded.activeSelf != activeSelf) {
                reloaded.SetActive(activeSelf);
                PrefabUtility.SavePrefabAsset(reloaded);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return reloaded.GetComponent<JSRunner>();
        }

        static void SetIncludeInBuild(JSRunner runner, bool value) {
            var so = new SerializedObject(runner);
            var prop = so.FindProperty("_includeInBuild");
            Assert.IsNotNull(prop,
                "JSRunner._includeInBuild was not found. The Include In Build toggle is what these " +
                "tests are about, so a rename has to reach here too.");
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        void InvokeProcessPrefabs() {
            try {
                _processPrefabs.Invoke(_processor, null);
            } catch (TargetInvocationException e) {
                throw e.InnerException;
            }
        }

        void InvokeCommitAssets(string destDir) {
            try {
                _commitAssetsTo.Invoke(_processor, new object[] { destDir });
            } catch (TargetInvocationException e) {
                throw e.InnerException;
            }
        }

        static JSRunner Reload(JSRunner runner) {
            var path = AssetDatabase.GetAssetPath(runner.gameObject);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<JSRunner>();
        }

        // MARK: the app ships

        [Test]
        public void APrefabOnlyRunner_GetsItsBundle() {
            var ps = CreateApp("PrefabApp");
            var runner = CreatePrefabWithRunner("PrefabRunner", ps);
            Assert.IsNull(runner.BundleAsset, "Fixture is wrong: the prefab already had a bundle.");

            InvokeProcessPrefabs();

            Assert.IsNotNull(Reload(runner).BundleAsset,
                "A JSRunner that lives only in a prefab got no bundle, so it would run OneJS's " +
                "default placeholder app wherever the prefab ships.");
        }

        [Test]
        public void APrefabOnlyRunner_GetsItsAssets() {
            var ps = CreateApp("PrefabAssetApp", "logo.png");
            CreatePrefabWithRunner("PrefabAssetRunner", ps);

            InvokeProcessPrefabs();

            Assert.AreEqual(1, _assetSources.Count,
                "The prefab app's assets folder was not recorded, so nothing of it would reach " +
                "StreamingAssets/onejs/assets.");

            var dest = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSPrefabAppCommit");
            try {
                InvokeCommitAssets(dest);
                Assert.IsTrue(File.Exists(Path.Combine(dest, "logo.png")),
                    "The prefab app's asset did not reach the destination folder.");
            } finally {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        [Test]
        public void TwoPrefabsSharingAnApp_BothGetTheBundle() {
            // The bug fixed in "Assign the bundle to every runner sharing an app",
            // reached through the prefab walk this time: the file is written once,
            // the reference is per component.
            var ps = CreateApp("SharedApp");
            var first = CreatePrefabWithRunner("SharedOne", ps);
            var second = CreatePrefabWithRunner("SharedTwo", ps);

            InvokeProcessPrefabs();

            Assert.IsNotNull(Reload(first).BundleAsset, "The first prefab sharing the app has no bundle.");
            Assert.IsNotNull(Reload(second).BundleAsset, "The second prefab sharing the app has no bundle.");
        }

        // MARK: opting out

        [Test]
        public void AnExcludedPrefabRunner_GetsNothingAndWarnsNothing() {
            var ps = CreateApp("ExcludedApp", "shared.png");
            var runner = CreatePrefabWithRunner("ExcludedRunner", ps, includeInBuild: false);

            InvokeProcessPrefabs();

            Assert.IsNull(Reload(runner).BundleAsset,
                "A runner with Include In Build off was still given a bundle.");
            Assert.AreEqual(0, _assetSources.Count,
                "A runner with Include In Build off still contributed its assets, so it could still " +
                "collide with an app that does ship. Not colliding is the point of the toggle.");
        }

        [Test]
        public void AnExcludedRunnerCannotCollideWithOne_ThatShips() {
            // The whole reason the toggle exists: a prefab kept in the project but
            // never shipped must not be able to affect a build.
            var shipping = CreateApp("ShippingApp", "logo.png", "from shipping");
            CreatePrefabWithRunner("ShippingRunner", shipping);
            var parked = CreateApp("ParkedApp", "logo.png", "from parked");
            CreatePrefabWithRunner("ParkedRunner", parked, includeInBuild: false);

            InvokeProcessPrefabs();

            Assert.AreEqual(1, _assetSources.Count, "The excluded app should contribute nothing.");

            var dest = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSPrefabAppExcluded");
            try {
                InvokeCommitAssets(dest);
                Assert.AreEqual("from shipping", File.ReadAllText(Path.Combine(dest, "logo.png")),
                    "The shipping app did not get its own file.");
            } finally {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        [Test]
        public void ADisabledPrefabRunner_IsSkipped() {
            var ps = CreateApp("InactiveApp");
            var runner = CreatePrefabWithRunner("InactiveRunner", ps, activeSelf: false);

            InvokeProcessPrefabs();

            Assert.IsNull(Reload(runner).BundleAsset,
                "A runner on an inactive prefab GameObject was still built.");
        }

        // MARK: collision severity

        [Test]
        public void TwoPrefabAppsClaimingOnePath_WarnRatherThanFailTheBuild() {
            var a = CreateApp("PrefabCollideA", "logo.png", "from A");
            CreatePrefabWithRunner("PrefabCollideRunnerA", a);
            var b = CreateApp("PrefabCollideB", "logo.png", "from B");
            CreatePrefabWithRunner("PrefabCollideRunnerB", b);

            InvokeProcessPrefabs();
            Assert.AreEqual(2, _assetSources.Count, "Fixture is wrong: expected two prefab apps.");

            LogAssert.Expect(LogType.Warning, new Regex("Asset path collision.*logo\\.png"));

            var dest = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSPrefabAppCollide");
            try {
                // The point of the case: this must NOT throw. A prefab does not say
                // whether it ships, so it must not be able to fail somebody's build.
                Assert.DoesNotThrow(() => InvokeCommitAssets(dest));
                Assert.IsTrue(File.Exists(Path.Combine(dest, "logo.png")),
                    "The winning app's file should still have been written.");
            } finally {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        [Test]
        public void TwoSceneAppsClaimingOnePath_StillFailTheBuild() {
            // The 3.4.3 behaviour, unchanged: both certainly ship, so neither can be
            // allowed to silently read the other's file.
            AddSceneSource("SceneCollideA", "logo.png", "from A");
            AddSceneSource("SceneCollideB", "logo.png", "from B");

            var dest = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSSceneAppCollide");
            try {
                var e = Assert.Throws<BuildFailedException>(() => InvokeCommitAssets(dest));
                StringAssert.Contains("logo.png", e.Message);
            } finally {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        [Test]
        public void ASceneAppAndAPrefabApp_WarnAndTheSceneAppKeepsThePath() {
            var prefabApp = CreateApp("PrefabLoser", "logo.png", "from prefab");
            CreatePrefabWithRunner("PrefabLoserRunner", prefabApp);
            InvokeProcessPrefabs();
            AddSceneSource("SceneWinner", "logo.png", "from scene");

            LogAssert.Expect(LogType.Warning, new Regex("Asset path collision.*logo\\.png"));

            var dest = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSMixedCollide");
            try {
                Assert.DoesNotThrow(() => InvokeCommitAssets(dest));
                Assert.AreEqual("from scene", File.ReadAllText(Path.Combine(dest, "logo.png")),
                    "The scene app is the one that certainly ships, so it should keep the path " +
                    "regardless of which walk ran first.");
            } finally {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        /// <summary>
        /// Adds a source the way a build scene would, without needing a scene: the
        /// collision rules key off the fromScene flag, not off how it got there.
        /// </summary>
        void AddSceneSource(string appName, string fileName, string body) {
            var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Temp/OneJSSceneSource", appName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), body);

            var elem = _assetSources.GetType().GetGenericArguments()[0];
            _assetSources.Add(Activator.CreateInstance(elem, dir, appName, true));
        }
    }
}
