using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for the lists in JSRunner's inspector that have a remove
    /// button: Stylesheets, Preloads, Globals and Cartridges. Each test builds the
    /// real inspector and clicks the real X button. (Scaffolding has no remove
    /// button: a default file is written once, see JSRunnerScaffoldOnceTests.)
    ///
    /// The bug these guard against lived in the rows, not in JSRunner. Every row
    /// kept the index it was built with and removed through the list as it was
    /// when drawn, and nothing redrew the rows after an undo. So removing B from
    /// [A, B, C], undoing, then clicking X on C wrote a stale list back, and a
    /// row drawn before another inspector changed the list removed the wrong
    /// entry, or brought a removed one back.
    ///
    /// ShowTab, JSRunnerEditor.s_Confirm and Clickable.SimulateSingleClick are
    /// not public, so they are reached by reflection and asserted in SetUp,
    /// where a rename fails loudly rather than inside each test.
    /// </summary>
    [TestFixture]
    public class JSRunnerInspectorListTests {
        const string TEST_FOLDER = "Assets/OneJSInspectorListTests";
        const string TabPrefKey = "JSRunner.ActiveTab";
        static readonly string[] Names = { "a", "b", "c" };

        /// <summary>Where each list is drawn, and how its X button says what it removes.</summary>
        static readonly Dictionary<string, (int tab, string tooltip)> Lists = new Dictionary<string, (int, string)> {
            ["_stylesheets"] = (1, "Remove this stylesheet"),
            ["_preloads"] = (0, "Remove this preload"),
            ["_globals"] = (0, "Remove this global"),
            ["_cartridges"] = (2, "Remove from list"),
        };

        GameObject _go;
        JSRunner _runner;
        UnityEditor.Editor _editor;
        VisualElement _root;
        readonly List<Object> _made = new List<Object>();
        MethodInfo _showTab;
        MethodInfo _simulateClick;
        FieldInfo _confirm;
        object _confirmBefore;
        int _tabBefore;
        bool _hadTabPref;

        // In a preview scene, never whoever's scene is open: a GameObject there
        // dirties it, and the next run blocks on a save dialog.
        Scene _previewScene;

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        T Make<T>(T obj, string name) where T : Object {
            obj.name = name;
            _made.Add(obj);
            return obj;
        }

        [SetUp]
        public void SetUp() {
            _showTab = typeof(JSRunnerEditor).GetMethod("ShowTab", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_showTab, "JSRunnerEditor.ShowTab(int) was not found: these tests are out of sync with the inspector.");
            _confirm = typeof(JSRunnerEditor).GetField("s_Confirm", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(_confirm, "JSRunnerEditor.s_Confirm was not found, so the Cartridges X would open a modal dialog.");
            _simulateClick = typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_simulateClick, "Clickable.SimulateSingleClick was not found: Unity renamed it, so these tests cannot click.");

            _confirmBefore = _confirm.GetValue(null);
            _confirm.SetValue(null, (Func<string, string, string, string, bool>)((_, _, _, _) => true));
            _hadTabPref = EditorPrefs.HasKey(TabPrefKey);
            _tabBefore = EditorPrefs.GetInt(TabPrefKey, 0);
            _previewScene = EditorSceneManager.NewPreviewScene();

            // A folder holding a "~" is a project folder, so the runner resolves
            // a working directory and the inspector draws its tabs.
            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            Directory.CreateDirectory(Path.Combine(AbsolutePath(TEST_FOLDER), "~"));
            AssetDatabase.Refresh();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panelSettings, TEST_FOLDER + "/PanelSettings.asset");

            _go = new GameObject(nameof(JSRunnerInspectorListTests));
            SceneManager.MoveGameObjectToScene(_go, _previewScene);
            _runner = _go.AddComponent<JSRunner>();
            _runner.SetPanelSettings(panelSettings);
            Assert.IsNotNull(_runner.WorkingDirFullPath, "Test setup is wrong: the folder was supposed to be a project folder.");

            // Every list holds a, b and c, each named so a row and an entry can
            // be matched by what they show.
            var so = new SerializedObject(_runner);
            foreach (var prop in Lists.Keys) so.FindProperty(prop).arraySize = Names.Length;
            for (int i = 0; i < Names.Length; i++) {
                var n = Names[i];
                so.FindProperty("_stylesheets").GetArrayElementAtIndex(i).objectReferenceValue =
                    Make(ScriptableObject.CreateInstance<StyleSheet>(), n);
                so.FindProperty("_preloads").GetArrayElementAtIndex(i).objectReferenceValue = Make(new TextAsset(n), n);
                so.FindProperty("_globals").GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue = n;
                so.FindProperty("_cartridges").GetArrayElementAtIndex(i).objectReferenceValue =
                    Make(ScriptableObject.CreateInstance<UICartridge>(), n);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            _editor = UnityEditor.Editor.CreateEditor(_runner, typeof(JSRunnerEditor));
            _root = _editor.CreateInspectorGUI();
        }

        [TearDown]
        public void TearDown() {
            if (_editor != null) Object.DestroyImmediate(_editor);
            if (_runner != null) Undo.ClearUndo(_runner);
            if (_go != null) Object.DestroyImmediate(_go);
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
            if (_confirm != null) _confirm.SetValue(null, _confirmBefore);
            if (_hadTabPref) EditorPrefs.SetInt(TabPrefKey, _tabBefore);
            else EditorPrefs.DeleteKey(TabPrefKey);
        }

        /// <summary>Draws the tab `list` lives on and checks it shows a, b and c.</summary>
        void Show(string list) {
            _showTab.Invoke(_editor, new object[] { Lists[list].tab });
            CollectionAssert.AreEqual(Names, Rows(list), $"Test setup is wrong: the inspector does not draw {list} as a, b, c.");
        }

        /// <summary>What each entry of `list` on the runner is now, by name.</summary>
        string[] Listed(string list) {
            var prop = new SerializedObject(_runner).FindProperty(list);
            return Enumerable.Range(0, prop.arraySize).Select(i => {
                var e = prop.GetArrayElementAtIndex(i);
                if (list == "_globals") return e.FindPropertyRelative("key").stringValue;
                return e.objectReferenceValue != null ? e.objectReferenceValue.name : "(none)";
            }).ToArray();
        }

        List<Button> XButtons(string list) =>
            _root.Query<Button>().Where(b => b.text == "X" && b.tooltip != null && b.tooltip.StartsWith(Lists[list].tooltip)).ToList();

        /// <summary>What a row shows: a global's key, or an object's name.</summary>
        static string RowName(VisualElement row) {
            var key = row.Q<TextField>();
            if (key != null) return key.value;
            var field = row.Q<ObjectField>();
            return field.value != null ? field.value.name : "(none)";
        }

        /// <summary>The rows the inspector draws for `list`, top to bottom.</summary>
        string[] Rows(string list) => XButtons(list).Select(b => RowName(b.parent)).ToArray();

        /// <summary>Clicks X on the row drawn for `name` in `list`, as it is drawn now.</summary>
        void ClickRemove(string list, string name) {
            var button = XButtons(list).FirstOrDefault(b => RowName(b.parent) == name);
            Assert.IsNotNull(button, $"The inspector draws no {list} row for {name}. Rows: {string.Join(", ", Rows(list))}");
            var args = _simulateClick.GetParameters()
                .Select(p => p.ParameterType == typeof(int) ? (object)0 : null)
                .ToArray();
            _simulateClick.Invoke(button.clickable, args);
        }

        [TestCase("_stylesheets")]
        [TestCase("_preloads")]
        [TestCase("_globals")]
        [TestCase("_cartridges")]
        public void RemoveTakesOutOnlyTheRowClicked(string list) {
            Show(list);
            Undo.IncrementCurrentGroup();
            ClickRemove(list, "b");

            CollectionAssert.AreEqual(new[] { "a", "c" }, Listed(list), "X on b removed something other than b.");
            CollectionAssert.AreEqual(new[] { "a", "c" }, Rows(list), "The inspector still draws the list as it was.");
        }

        [TestCase("_stylesheets")]
        [TestCase("_preloads")]
        [TestCase("_globals")]
        [TestCase("_cartridges")]
        public void AfterUndoTheRemovedRowIsBackAndRemoveStillTakesTheRowClicked(string list) {
            Show(list);
            Undo.IncrementCurrentGroup();
            ClickRemove(list, "b");
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            CollectionAssert.AreEqual(Names, Listed(list), "Undo did not bring b back.");

            // Read now, asserted after the click: a missing b row would otherwise
            // hide what X on the still drawn c row does.
            var rowsAfterUndo = Rows(list);

            Undo.IncrementCurrentGroup();
            ClickRemove(list, "c");

            CollectionAssert.AreEqual(new[] { "a", "b" }, Listed(list), "After an undo, X on c removed something other than c.");
            CollectionAssert.AreEqual(Names, rowsAfterUndo, "Undo brought b back, but the inspector did not draw its row.");
            CollectionAssert.AreEqual(new[] { "a", "b" }, Rows(list));
        }

        [TestCase("_stylesheets")]
        [TestCase("_preloads")]
        [TestCase("_globals")]
        [TestCase("_cartridges")]
        public void ARowDrawnBeforeTheListChangedRemovesItsOwnEntry(string list) {
            Show(list);
            // The list changes under rows that are already drawn, as it does when
            // a second inspector on the same runner removes an entry. The rows
            // here are not rebuilt, so c's row still holds index 2.
            var so = new SerializedObject(_runner);
            var prop = so.FindProperty(list);
            if (prop.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.ObjectReference) {
                prop.GetArrayElementAtIndex(0).objectReferenceValue = null;
            }
            prop.DeleteArrayElementAtIndex(0);
            so.ApplyModifiedPropertiesWithoutUndo();
            CollectionAssert.AreEqual(Names, Rows(list), "Test setup is wrong: the rows were rebuilt.");

            ClickRemove(list, "c");

            CollectionAssert.AreEqual(new[] { "b" }, Listed(list), "A row drawn before the list changed removed the wrong entry.");
        }
    }
}
