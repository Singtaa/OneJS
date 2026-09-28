using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for the Scaffolding list in JSRunner's inspector: its remove
    /// button, what EnsureProjectSetup does with the list afterwards, and undo.
    ///
    /// Each test builds the real inspector and clicks the real X button, because
    /// the bug these guard against lived in the row, not in JSRunner: every row
    /// kept the index it was built with, and nothing rebuilt the rows after an
    /// undo, so removing B from [A, B, C], undoing, then clicking X on C removed
    /// the wrong entry.
    ///
    /// ShowTab and Clickable.SimulateSingleClick are not public, so they are
    /// reached by reflection and asserted in SetUp, where a rename fails loudly
    /// rather than as a NullReferenceException inside each test.
    /// </summary>
    [TestFixture]
    public class JSRunnerScaffoldingListTests {
        const string TEST_FOLDER = "Assets/OneJSScaffoldingListTests";
        const string TabPrefKey = "JSRunner.ActiveTab";
        const int BuildTab = 3;
        static readonly string[] Paths = { "a.txt", "b.txt", "c.txt" };

        GameObject _go;
        JSRunner _runner;
        UnityEditor.Editor _editor;
        VisualElement _root;
        readonly List<TextAsset> _templates = new List<TextAsset>();
        MethodInfo _showTab;
        MethodInfo _simulateClick;
        int _tabBefore;
        bool _hadTabPref;

        // In a preview scene, never whoever's scene is open: a GameObject there
        // dirties it, and the next run blocks on a save dialog.
        Scene _previewScene;

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        string WorkingFile(string path) => Path.Combine(_runner.WorkingDirFullPath, path);

        [SetUp]
        public void SetUp() {
            _showTab = typeof(JSRunnerEditor).GetMethod("ShowTab", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_showTab, "JSRunnerEditor.ShowTab(int) was not found: these tests are out of sync with the inspector.");
            _simulateClick = typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_simulateClick, "Clickable.SimulateSingleClick was not found: Unity renamed it, so these tests cannot click.");

            _hadTabPref = EditorPrefs.HasKey(TabPrefKey);
            _tabBefore = EditorPrefs.GetInt(TabPrefKey, 0);
            _previewScene = EditorSceneManager.NewPreviewScene();

            // A folder holding a "~" is a project folder, so the runner resolves
            // a working directory and EnsureProjectSetup has somewhere to write.
            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            Directory.CreateDirectory(Path.Combine(AbsolutePath(TEST_FOLDER), "~"));
            AssetDatabase.Refresh();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panelSettings, TEST_FOLDER + "/PanelSettings.asset");

            _go = new GameObject(nameof(JSRunnerScaffoldingListTests));
            SceneManager.MoveGameObjectToScene(_go, _previewScene);
            _runner = _go.AddComponent<JSRunner>();
            _runner.SetPanelSettings(panelSettings);
            Assert.IsNotNull(_runner.WorkingDirFullPath, "Test setup is wrong: the folder was supposed to be a project folder.");

            var so = new SerializedObject(_runner);
            var list = so.FindProperty("_defaultFiles");
            list.arraySize = Paths.Length;
            for (int i = 0; i < Paths.Length; i++) {
                var template = new TextAsset($"template for {Paths[i]}");
                _templates.Add(template);
                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("path").stringValue = Paths[i];
                entry.FindPropertyRelative("content").objectReferenceValue = template;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            _editor = UnityEditor.Editor.CreateEditor(_runner, typeof(JSRunnerEditor));
            _root = _editor.CreateInspectorGUI();
            _showTab.Invoke(_editor, new object[] { BuildTab });
            CollectionAssert.AreEqual(Paths, Rows(), "Test setup is wrong: the inspector does not list the three entries.");
        }

        [TearDown]
        public void TearDown() {
            if (_editor != null) Object.DestroyImmediate(_editor);
            if (_runner != null) Undo.ClearUndo(_runner);
            if (_go != null) Object.DestroyImmediate(_go);
            foreach (var t in _templates) Object.DestroyImmediate(t);
            _templates.Clear();
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
            if (_hadTabPref) EditorPrefs.SetInt(TabPrefKey, _tabBefore);
            else EditorPrefs.DeleteKey(TabPrefKey);
        }

        /// <summary>The paths the runner lists now, read from the component itself.</summary>
        string[] Listed() {
            var list = new SerializedObject(_runner).FindProperty("_defaultFiles");
            return Enumerable.Range(0, list.arraySize)
                .Select(i => list.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue)
                .ToArray();
        }

        /// <summary>The rows the inspector shows, top to bottom, each by its path label.</summary>
        string[] Rows() => XButtons().Select(b => b.parent.Q<Label>().text).ToArray();

        List<Button> XButtons() => _root.Query<Button>().Where(b => b.text == "X").ToList();

        /// <summary>Clicks X on the row the inspector shows for `path`, as it is drawn now.</summary>
        void ClickRemove(string path) {
            var button = XButtons().FirstOrDefault(b => b.parent.Q<Label>().text == path);
            Assert.IsNotNull(button, $"The inspector shows no row for {path}. Rows: {string.Join(", ", Rows())}");
            var args = _simulateClick.GetParameters()
                .Select(p => p.ParameterType == typeof(int) ? (object)0 : null)
                .ToArray();
            _simulateClick.Invoke(button.clickable, args);
        }

        [Test]
        public void RemoveTakesOutOnlyTheRowClicked() {
            Undo.IncrementCurrentGroup();
            ClickRemove("b.txt");

            CollectionAssert.AreEqual(new[] { "a.txt", "c.txt" }, Listed(), "X on b.txt removed something other than b.txt.");
            CollectionAssert.AreEqual(new[] { "a.txt", "c.txt" }, Rows(), "The inspector still draws the list as it was.");
        }

        [Test]
        public void ARemovedFileIsNotRecreatedWhileTheOthersAre() {
            _runner.EnsureProjectSetup();
            foreach (var p in Paths) Assert.IsTrue(File.Exists(WorkingFile(p)), $"Test setup is wrong: {p} was not scaffolded.");

            Undo.IncrementCurrentGroup();
            ClickRemove("b.txt");
            File.Delete(WorkingFile("b.txt"));
            File.Delete(WorkingFile("c.txt"));
            _runner.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(WorkingFile("b.txt")), "b.txt was recreated after it was removed from the list.");
            Assert.IsTrue(File.Exists(WorkingFile("c.txt")), "c.txt is still listed, so a missing c.txt should have been recreated.");
            Assert.IsTrue(File.Exists(WorkingFile("a.txt")), "a.txt, which nobody deleted, is gone.");
        }

        [Test]
        public void AfterUndoTheRemovedRowIsBackAndRemoveStillTakesTheRowClicked() {
            Undo.IncrementCurrentGroup();
            ClickRemove("b.txt");
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            CollectionAssert.AreEqual(Paths, Listed(), "Undo did not bring b.txt back.");

            // Read now, asserted after the click: a missing b.txt row would
            // otherwise hide what X on the still drawn c.txt row does.
            var rowsAfterUndo = Rows();

            Undo.IncrementCurrentGroup();
            ClickRemove("c.txt");

            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, Listed(), "After an undo, X on c.txt removed something other than c.txt.");
            CollectionAssert.AreEqual(Paths, rowsAfterUndo, "Undo brought b.txt back, but the inspector did not draw its row.");
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, Rows());
        }

        [Test]
        public void ARowDrawnBeforeTheListChangedRemovesItsOwnEntry() {
            // The list changes under rows that are already drawn, as it does when
            // a second inspector on the same runner removes an entry. The rows
            // here are not rebuilt, so C's row still holds index 2.
            var so = new SerializedObject(_runner);
            so.FindProperty("_defaultFiles").DeleteArrayElementAtIndex(0);
            so.ApplyModifiedPropertiesWithoutUndo();
            CollectionAssert.AreEqual(Paths, Rows(), "Test setup is wrong: the rows were rebuilt.");

            ClickRemove("c.txt");

            CollectionAssert.AreEqual(new[] { "b.txt" }, Listed(), "A row drawn before the list changed removed the wrong entry.");
        }
    }
}
