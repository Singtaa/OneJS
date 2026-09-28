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
    /// EditMode tests for what surrounds scaffolding once: the list Initialize Project keeps,
    /// the message a missing default file gets, the status of a file whose template changed,
    /// the Scaffolding list without its remove button, and the state folder staying committed.
    /// </summary>
    [TestFixture]
    public class JSRunnerDefaultFilesTests {
        const string TEST_FOLDER = "Assets/OneJSDefaultFilesTests";

        PanelSettings _panelSettings;
        Scene _previewScene;
        JSRunner _runner;
        readonly System.Collections.Generic.List<Object> _made = new System.Collections.Generic.List<Object>();

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        static string WorkingDir => Path.Combine(AbsolutePath(TEST_FOLDER), "~");
        static string InWorkingDir(string path) => Path.Combine(WorkingDir, path);

        [SetUp]
        public void SetUp() {
            _previewScene = EditorSceneManager.NewPreviewScene();
            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            Directory.CreateDirectory(WorkingDir);
            AssetDatabase.Refresh();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(_panelSettings, TEST_FOLDER + "/PanelSettings.asset");

            var go = new GameObject(nameof(JSRunnerDefaultFilesTests));
            SceneManager.MoveGameObjectToScene(go, _previewScene);
            _runner = go.AddComponent<JSRunner>();
            _runner.SetPanelSettings(_panelSettings);
            _runner.PopulateDefaultFiles();
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _previewScene.GetRootGameObjects()) Object.DestroyImmediate(go);
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        TextAsset Text(string content) {
            var t = new TextAsset(content);
            _made.Add(t);
            return t;
        }

        SerializedProperty Entry(SerializedObject so, string path) {
            var list = so.FindProperty("_defaultFiles");
            for (int i = 0; i < list.arraySize; i++) {
                var e = list.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("path").stringValue == path) return e;
            }
            return null;
        }

        int IndexOf(string path) {
            var list = new SerializedObject(_runner).FindProperty("_defaultFiles");
            for (int i = 0; i < list.arraySize; i++) {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue == path) return i;
            }
            return -1;
        }

        [Test]
        public void InitializingKeepsACustomizedList() {
            // The Premade demo's shape: its own index.tsx, and a file the templates do not have.
            var so = new SerializedObject(_runner);
            var index = Text("// the demo");
            Entry(so, "index.tsx").FindPropertyRelative("content").objectReferenceValue = index;
            var list = so.FindProperty("_defaultFiles");
            list.arraySize++;
            var extra = list.GetArrayElementAtIndex(list.arraySize - 1);
            extra.FindPropertyRelative("path").stringValue = "Settings.module.uss";
            extra.FindPropertyRelative("content").objectReferenceValue = Text(".settings {}");
            // And a template it lacks, which initializing should add.
            for (int i = 0; i < list.arraySize; i++) {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue != "AGENTS.md") continue;
                list.DeleteArrayElementAtIndex(i);
                break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            _runner.AddMissingDefaultFiles();

            so = new SerializedObject(_runner);
            Assert.AreSame(index, Entry(so, "index.tsx").FindPropertyRelative("content").objectReferenceValue,
                "The list's own index.tsx was replaced by the template.");
            Assert.IsNotNull(Entry(so, "Settings.module.uss"), "An entry the templates do not cover was dropped.");
            Assert.IsNotNull(Entry(so, "AGENTS.md"), "A template the list lacked was not added.");
            var paths = Enumerable.Range(0, so.FindProperty("_defaultFiles").arraySize)
                .Select(i => so.FindProperty("_defaultFiles").GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue);
            CollectionAssert.AllItemsAreUnique(paths, "A path is listed twice.");
        }

        [Test]
        public void AMissingDefaultFileIsNamedWithHowToGetItBack() {
            _runner.EnsureProjectSetup();
            Assert.IsNull(_runner.DescribeMissingDefaultFiles(), "Nothing is missing, yet something was reported.");

            File.Delete(InWorkingDir("package.json"));
            File.Delete(InWorkingDir("esbuild.config.mjs"));
            var message = _runner.DescribeMissingDefaultFiles();

            Assert.IsNotNull(message, "Two default files are missing and nothing says so.");
            StringAssert.Contains("package.json", message);
            StringAssert.Contains("esbuild.config.mjs", message);
            StringAssert.Contains("Restore", message, "The message does not say how to get a file back.");
            StringAssert.DoesNotContain("tsconfig.json", message, "A file that is there was reported missing.");
        }

        [Test]
        public void AFileNobodyChangedReadsTemplateNewerWhenItsTemplateChanges() {
            _runner.EnsureProjectSetup();
            int at = IndexOf("styles/main.uss");
            Assert.AreEqual(DefaultFileStatus.UpToDate, _runner.GetDefaultFileStatus(at));

            var so = new SerializedObject(_runner);
            Entry(so, "styles/main.uss").FindPropertyRelative("content").objectReferenceValue = Text(".root { color: red; }");
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(DefaultFileStatus.TemplateUpdated, _runner.GetDefaultFileStatus(at),
                "An untouched file whose template changed reads as if the user had changed it.");

            File.AppendAllText(InWorkingDir("styles/main.uss"), "\n.mine {}");
            Assert.AreEqual(DefaultFileStatus.Modified, _runner.GetDefaultFileStatus(at));

            File.Delete(InWorkingDir("styles/main.uss"));
            Assert.AreEqual(DefaultFileStatus.Missing, _runner.GetDefaultFileStatus(at));
        }

        [Test]
        public void TheScaffoldingListHasRestoreAndNoRemoveButton() {
            _runner.EnsureProjectSetup();
            var editor = UnityEditor.Editor.CreateEditor(_runner, typeof(JSRunnerEditor));
            bool hadTab = EditorPrefs.HasKey("JSRunner.ActiveTab");
            int tab = EditorPrefs.GetInt("JSRunner.ActiveTab", 0);
            try {
                var root = editor.CreateInspectorGUI();
                typeof(JSRunnerEditor).GetMethod("ShowTab", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(editor, new object[] { 3 });
                var buttons = root.Query<Button>().ToList();
                int listed = new SerializedObject(_runner).FindProperty("_defaultFiles").arraySize;
                Assert.AreEqual(listed, buttons.Count(b => b.text == OneJSEditorDesign.Texts.Restore),
                    "Every scaffolded file should have a Restore button.");
                Assert.IsFalse(buttons.Any(b => b.tooltip != null && b.tooltip.StartsWith("Stop scaffolding")),
                    "The Scaffolding list still has its remove button.");
            } finally {
                Object.DestroyImmediate(editor);
                if (hadTab) EditorPrefs.SetInt("JSRunner.ActiveTab", tab);
                else EditorPrefs.DeleteKey("JSRunner.ActiveTab");
            }
        }

        [Test]
        public void TheScaffoldedGitignoreKeepsTheStateFolder() {
            _runner.EnsureProjectSetup();
            var patterns = File.ReadAllLines(InWorkingDir(".gitignore"))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToArray();
            foreach (var p in patterns) {
                Assert.IsFalse(p.TrimStart('/').StartsWith(".onejs") || p == ".*" || p == ".*/" || p.StartsWith("scaffold"),
                    $"The scaffolded .gitignore pattern '{p}' would keep {ScaffoldRecord.StateFolder}/{ScaffoldRecord.FileName} out of git.");
            }
            Assert.IsTrue(File.Exists(Path.Combine(WorkingDir, ScaffoldRecord.StateFolder, ScaffoldRecord.FileName)),
                "The record is not where the .gitignore check assumes.");
        }
    }
}
