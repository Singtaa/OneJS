using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for which UIDocument JSRunner is allowed to remove.
    ///
    /// JSRunner clears the UIDocument when Panel Settings is cleared or points outside a project
    /// folder. That is right for a UIDocument JSRunner added itself and wrong for one the user had
    /// on the GameObject first: the removal is deferred through EditorApplication.delayCall, so it
    /// lands detached from the change that caused it and reads as the component vanishing on its own.
    ///
    /// These drive RemoveUIDocumentIfOwned directly rather than waiting on delayCall. That is the
    /// real production method and the only place the policy lives, so nothing is reimplemented here,
    /// and the tests do not depend on the editor scheduler running, which a background editor may
    /// never do. The third test asserts a removal still happens, so the first two cannot pass by the
    /// removal being broken outright.
    /// </summary>
    [TestFixture]
    public class JSRunnerUIDocumentOwnershipTests {
        const string TEST_FOLDER = "Assets/OneJSUIDocumentOwnershipTests";

        GameObject _go;
        MethodInfo _removeUIDocumentIfOwned;
        MethodInfo _ensureUIDocumentInEditor;

        // Test objects live in a preview scene, never in whoever's scene is open. A GameObject created
        // in the open scene dirties it, and the EditMode runner then prompts to save modified scenes
        // when the NEXT run starts, which blocks the editor on a modal dialog until somebody clicks it.
        Scene _previewScene;

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        [SetUp]
        public void SetUp() {
            _removeUIDocumentIfOwned = typeof(JSRunner).GetMethod(
                "RemoveUIDocumentIfOwned", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_removeUIDocumentIfOwned,
                "JSRunner.RemoveUIDocumentIfOwned() was not found via reflection: " +
                "these tests are out of sync with the implementation.");
            _ensureUIDocumentInEditor = typeof(JSRunner).GetMethod(
                "EnsureUIDocumentInEditor", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_ensureUIDocumentInEditor,
                "JSRunner.EnsureUIDocumentInEditor() was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            _previewScene = EditorSceneManager.NewPreviewScene();

            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            Directory.CreateDirectory(AbsolutePath(TEST_FOLDER));
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        /// <summary>A GameObject in this fixture's preview scene, so the open scene is never touched.</summary>
        GameObject NewTestObject() {
            var go = new GameObject(nameof(JSRunnerUIDocumentOwnershipTests));
            SceneManager.MoveGameObjectToScene(go, _previewScene);
            return go;
        }

        /// <summary>A PanelSettings in a folder that is NOT a project folder (no ~, no app.js.txt).</summary>
        PanelSettings PanelSettingsInInvalidFolder() {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, TEST_FOLDER + "/Invalid.asset");
            AssetDatabase.SaveAssets();
            return ps;
        }

        /// <summary>A PanelSettings in a real project folder, made valid by the "~" directory beside it.</summary>
        PanelSettings PanelSettingsInValidFolder() {
            const string valid = TEST_FOLDER + "/Valid";
            Directory.CreateDirectory(Path.Combine(AbsolutePath(valid), "~"));
            AssetDatabase.Refresh();
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, valid + "/PanelSettings.asset");
            AssetDatabase.SaveAssets();
            return ps;
        }

        /// <summary>The user's own UIDocument, already on the GameObject before JSRunner arrives.</summary>
        JSRunner RunnerOnTopOfAUserUIDocument() {
            _go = NewTestObject();
            _go.AddComponent<UIDocument>();
            return _go.AddComponent<JSRunner>();
        }

        [Test]
        public void ClearingPanelSettings_KeepsAUIDocumentTheUserAdded() {
            var runner = RunnerOnTopOfAUserUIDocument();
            runner.SetPanelSettings(null);

            _removeUIDocumentIfOwned.Invoke(runner, null);

            Assert.IsNotNull(_go.GetComponent<UIDocument>(),
                "Clearing Panel Settings destroyed a UIDocument the user added, not one JSRunner added.");
        }

        [Test]
        public void InvalidPanelSettingsFolder_KeepsAUIDocumentTheUserAdded() {
            var runner = RunnerOnTopOfAUserUIDocument();
            runner.SetPanelSettings(PanelSettingsInInvalidFolder());
            Assert.IsFalse(runner.IsPanelSettingsInValidProjectFolder(),
                "Test setup is wrong: the folder was supposed to be an invalid project folder.");

            _removeUIDocumentIfOwned.Invoke(runner, null);

            Assert.IsNotNull(_go.GetComponent<UIDocument>(),
                "An invalid Panel Settings destroyed a UIDocument the user added, not one JSRunner added.");
        }

        [Test]
        public void ClearingPanelSettings_StillRemovesAUIDocumentJSRunnerAdded() {
            _go = NewTestObject();
            var runner = _go.AddComponent<JSRunner>();
            runner.SetPanelSettings(PanelSettingsInValidFolder());

            // JSRunner adding the UIDocument here is what makes it JSRunner's to remove.
            _ensureUIDocumentInEditor.Invoke(runner, null);
            Assert.IsNotNull(_go.GetComponent<UIDocument>(),
                "Test setup is wrong: JSRunner was supposed to add a UIDocument for a valid Panel Settings.");

            runner.SetPanelSettings(null);
            _removeUIDocumentIfOwned.Invoke(runner, null);

            Assert.IsNull(_go.GetComponent<UIDocument>(),
                "Clearing Panel Settings left behind a UIDocument that JSRunner added. The point is to " +
                "narrow this cleanup to JSRunner's own component, not to stop cleaning up.");
        }
    }
}
