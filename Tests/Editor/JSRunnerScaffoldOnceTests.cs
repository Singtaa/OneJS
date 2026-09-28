using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// EditMode tests for scaffolding once: JSRunner writes each default file a single
    /// time, records it in <c>~/.onejs/scaffold</c>, and never writes it again, so a file the
    /// user deletes stays deleted.
    ///
    /// One test per state the record can be in (present; absent with no package.json, a new
    /// app; absent with a package.json, an app from before the record existed), then the
    /// cases that break the obvious shortcuts: a deleted package.json while the record exists,
    /// a deleted record, and two runners sharing one working directory.
    ///
    /// These use only EnsureProjectSetup, PopulateDefaultFiles and the files on disk, so they
    /// read the same against a JSRunner that has never heard of the record.
    ///
    /// Then Restore and the Scaffolding list's Status column, which the inspector draws from
    /// ScaffoldingPaths, GetDefaultFileStatus and RestoreDefaultFile: every file the missing-file
    /// warning names has a row Restore works on, Restore records what it wrote, and the status
    /// says "Template newer" or "Modified" only when OneJS can tell which.
    /// </summary>
    [TestFixture]
    public class JSRunnerScaffoldOnceTests {
        const string TEST_FOLDER = "Assets/OneJSScaffoldOnceTests";
        const string RecordPath = ".onejs/scaffold";

        PanelSettings _panelSettings;
        Scene _previewScene;

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        static string WorkingDir => Path.Combine(AbsolutePath(TEST_FOLDER), "~");
        static string InWorkingDir(string path) => Path.Combine(WorkingDir, path);
        static string Record => InWorkingDir(RecordPath);

        [SetUp]
        public void SetUp() {
            _previewScene = EditorSceneManager.NewPreviewScene();
            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            // A folder holding a "~" is a project folder.
            Directory.CreateDirectory(WorkingDir);
            AssetDatabase.Refresh();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(_panelSettings, TEST_FOLDER + "/PanelSettings.asset");
        }

        [TearDown]
        public void TearDown() {
            foreach (var go in _previewScene.GetRootGameObjects()) Object.DestroyImmediate(go);
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        /// <summary>A runner on the test folder with the OneJS templates as its list.</summary>
        JSRunner MakeRunner(string name = "Runner") {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _previewScene);
            var runner = go.AddComponent<JSRunner>();
            runner.SetPanelSettings(_panelSettings);
            runner.PopulateDefaultFiles();
            Assert.AreEqual(WorkingDir, runner.WorkingDirFullPath, "Test setup is wrong: the runner resolved another working directory.");
            return runner;
        }

        static string[] ListedPaths(JSRunner runner) {
            var list = new SerializedObject(runner).FindProperty("_defaultFiles");
            return Enumerable.Range(0, list.arraySize)
                .Select(i => list.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue)
                .ToArray();
        }

        static void Unlist(JSRunner runner, string path) {
            var so = new SerializedObject(runner);
            var list = so.FindProperty("_defaultFiles");
            for (int i = 0; i < list.arraySize; i++) {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("path").stringValue != path) continue;
                list.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            Assert.Fail($"Test setup is wrong: {path} is not in the list.");
        }

        static string[] RecordedPaths() =>
            File.ReadAllLines(Record).Where(l => l.Length > 0).Select(l => l.Split('\t')[0]).ToArray();

        [Test]
        public void ANewAppGetsEveryDefaultFileAndARecordOfEach() {
            var runner = MakeRunner();
            // Something the user put there first is kept, not replaced.
            File.WriteAllText(InWorkingDir("index.tsx"), "// mine");

            runner.EnsureProjectSetup();

            foreach (var path in ListedPaths(runner)) Assert.IsTrue(File.Exists(InWorkingDir(path)), $"{path} was not written.");
            Assert.AreEqual("// mine", File.ReadAllText(InWorkingDir("index.tsx")), "A file that was already there was overwritten.");
            Assert.IsTrue(File.Exists(Record), $"A new app got no {RecordPath}.");
            CollectionAssert.IsSubsetOf(ListedPaths(runner), RecordedPaths(), "The record leaves out a default file the app was given.");
            var package = File.ReadAllLines(Record).Single(l => l.StartsWith("package.json"));
            StringAssert.Contains("\t", package, "A file the app was given is recorded without the hash of what it was given.");
        }

        [Test]
        public void ADeletedDefaultFileStaysDeleted() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            File.Delete(InWorkingDir("index.tsx"));
            File.Delete(InWorkingDir("styles/main.uss"));

            runner.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "index.tsx was deleted and came back.");
            Assert.IsFalse(File.Exists(InWorkingDir("styles/main.uss")), "styles/main.uss was deleted and came back.");
            Assert.IsTrue(File.Exists(InWorkingDir("package.json")), "A file nobody deleted is gone.");
        }

        [Test]
        public void AnAppWithARecordGetsOnlyTheDefaultFilesItHasNotHadOnce() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            // AGENTS.md stands for a template a newer OneJS added: on disk nowhere, recorded nowhere.
            File.Delete(InWorkingDir("AGENTS.md"));
            File.WriteAllLines(Record, File.ReadAllLines(Record).Where(l => !l.StartsWith("AGENTS.md")));
            File.Delete(InWorkingDir("index.tsx"));

            LogAssert.Expect(LogType.Log, new Regex("AGENTS\\.md"));
            runner.EnsureProjectSetup();

            Assert.IsTrue(File.Exists(InWorkingDir("AGENTS.md")), "A default file the app never had was not written.");
            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "index.tsx is recorded and was deleted, but came back.");
            CollectionAssert.Contains(RecordedPaths(), "AGENTS.md", "The new default file was written but not recorded.");

            File.Delete(InWorkingDir("AGENTS.md"));
            runner.EnsureProjectSetup();
            Assert.IsFalse(File.Exists(InWorkingDir("AGENTS.md")), "The new default file was written a second time.");
        }

        [Test]
        public void AnAppFromBeforeTheRecordIsSeededFromItsDiskAndItsList() {
            var runner = MakeRunner();
            // An app scaffolded by an older OneJS: its files, no record, and a list from
            // before AGENTS.md was a template. The user has since deleted index.tsx.
            foreach (var path in ListedPaths(runner)) {
                var full = InWorkingDir(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, "// scaffolded long ago");
            }
            File.Delete(InWorkingDir("index.tsx"));
            File.Delete(InWorkingDir("AGENTS.md"));
            Unlist(runner, "AGENTS.md");

            runner.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "index.tsx was listed and deleted before the upgrade, and came back.");
            Assert.IsTrue(File.Exists(InWorkingDir("AGENTS.md")), "A template the old list never had was not written once.");
            Assert.AreEqual("// scaffolded long ago", File.ReadAllText(InWorkingDir("package.json")), "An existing file was overwritten.");
            Assert.IsTrue(File.Exists(Record), "The record was not seeded.");
            CollectionAssert.IsSubsetOf(new[] { "index.tsx", "AGENTS.md", "package.json" }, RecordedPaths());
        }

        [Test]
        public void ADeletedPackageJsonDoesNotMakeAnAppWithARecordNew() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            File.Delete(InWorkingDir("package.json"));
            File.Delete(InWorkingDir("index.tsx"));

            runner.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(InWorkingDir("package.json")), "package.json was deleted and came back.");
            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "Without package.json the app was treated as new, and index.tsx came back.");
        }

        [Test]
        public void DeletingTheRecordBringsNoDeletedFileBack() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            File.Delete(InWorkingDir("index.tsx"));
            File.Delete(Record);

            runner.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "With the record deleted, index.tsx came back.");
            Assert.IsTrue(File.Exists(Record), "The record was not written again.");
            CollectionAssert.Contains(RecordedPaths(), "index.tsx", "The new record forgot index.tsx, so a later Play could write it.");
        }

        [Test]
        public void TwoRunnersOnOneFolderShareTheRecord() {
            var first = MakeRunner("First");
            var second = MakeRunner("Second");
            first.EnsureProjectSetup();
            File.Delete(InWorkingDir("index.tsx"));

            second.EnsureProjectSetup();

            Assert.IsFalse(File.Exists(InWorkingDir("index.tsx")), "The second runner brought back a file the first had already given the folder.");
        }

        // MARK: Restore and status

        /// <summary>A OneJS template's text, from a fresh runner's list.</summary>
        string TemplateText(string path) {
            var go = new GameObject("Templates");
            SceneManager.MoveGameObjectToScene(go, _previewScene);
            var runner = go.AddComponent<JSRunner>();
            runner.PopulateDefaultFiles();
            var list = new SerializedObject(runner).FindProperty("_defaultFiles");
            for (int i = 0; i < list.arraySize; i++) {
                var entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("path").stringValue == path)
                    return ((TextAsset)entry.FindPropertyRelative("content").objectReferenceValue).text;
            }
            Assert.Fail($"Test setup is wrong: OneJS has no template for {path}.");
            return null;
        }

        /// <summary>Gives the runner's entry for `path` new content, as an upgrade that changes a template does.</summary>
        static void ChangeTemplate(JSRunner runner, string path, string text) {
            var so = new SerializedObject(runner);
            var list = so.FindProperty("_defaultFiles");
            for (int i = 0; i < list.arraySize; i++) {
                var entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("path").stringValue != path) continue;
                entry.FindPropertyRelative("content").objectReferenceValue = new TextAsset(text);
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            Assert.Fail($"Test setup is wrong: {path} is not in the list.");
        }

        /// <summary>An app scaffolded by a OneJS from before the record: every listed file on disk, no record.</summary>
        void MakeAppFromBeforeTheRecord(JSRunner runner, System.Func<string, string> content) {
            foreach (var path in ListedPaths(runner)) {
                var full = InWorkingDir(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, content(path));
            }
            Assert.IsFalse(File.Exists(Record), "Test setup is wrong: the app already has a record.");
        }

        static string RecordedHash(string path) {
            var line = File.ReadAllLines(Record).SingleOrDefault(l => l.Split('\t')[0] == path);
            Assert.IsNotNull(line, $"{path} is not recorded.");
            var parts = line.Split('\t');
            return parts.Length > 1 ? parts[1] : null;
        }

        [Test]
        public void EveryMissingFileTheWarningNamesHasARowRestoreWorksOn() {
            var runner = MakeRunner();
            // A runner from before AGENTS.md was a template: the template reaches it, its list does not.
            Unlist(runner, "AGENTS.md");
            runner.EnsureProjectSetup();
            File.Delete(InWorkingDir("AGENTS.md"));

            StringAssert.Contains("AGENTS.md", runner.DescribeMissingDefaultFiles(), "Test setup is wrong: the warning does not name AGENTS.md.");
            CollectionAssert.Contains(runner.ScaffoldingPaths, "AGENTS.md",
                "The warning says to Restore AGENTS.md in the Scaffolding list, which has no row for it.");
            Assert.AreEqual(DefaultFileStatus.Missing, runner.GetDefaultFileStatus("AGENTS.md"));

            Assert.IsTrue(runner.RestoreDefaultFile("AGENTS.md"), "Restore refused a path the warning names.");
            Assert.AreEqual(TemplateText("AGENTS.md"), File.ReadAllText(InWorkingDir("AGENTS.md")), "Restore did not write OneJS's template.");
            Assert.AreEqual(ScaffoldRecord.Hash(TemplateText("AGENTS.md")), RecordedHash("AGENTS.md"), "Restore did not record what it wrote.");
            Assert.AreEqual(DefaultFileStatus.UpToDate, runner.GetDefaultFileStatus("AGENTS.md"));
        }

        [Test]
        public void RestoreBringsBackADeletedFileAsItsTemplate() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            File.Delete(InWorkingDir("index.tsx"));
            Assert.AreEqual(DefaultFileStatus.Missing, runner.GetDefaultFileStatus("index.tsx"));

            Assert.IsTrue(runner.RestoreDefaultFile("index.tsx"));

            Assert.AreEqual(TemplateText("index.tsx"), File.ReadAllText(InWorkingDir("index.tsx")));
            Assert.AreEqual(DefaultFileStatus.UpToDate, runner.GetDefaultFileStatus("index.tsx"));
            runner.EnsureProjectSetup();
            Assert.AreEqual(TemplateText("index.tsx"), File.ReadAllText(InWorkingDir("index.tsx")), "The next Play changed a restored file.");
        }

        [Test]
        public void RestoreRecordsWhatItWroteSoALaterTemplateChangeShows() {
            var runner = MakeRunner();
            MakeAppFromBeforeTheRecord(runner, _ => "// scaffolded long ago");
            runner.EnsureProjectSetup();

            Assert.IsTrue(runner.RestoreDefaultFile("index.tsx"));

            Assert.AreEqual(ScaffoldRecord.Hash(TemplateText("index.tsx")), RecordedHash("index.tsx"),
                "Restore wrote the template and left the record saying nothing about it.");
            ChangeTemplate(runner, "index.tsx", "// a newer template");
            Assert.AreEqual(DefaultFileStatus.TemplateUpdated, runner.GetDefaultFileStatus("index.tsx"),
                "A restored file nobody touched since reads as changed by the user after the template moved on.");
        }

        [Test]
        public void ASeededFileThatIsTheTemplateShowsTemplateNewerWhenTheTemplateMovesOn() {
            var runner = MakeRunner();
            MakeAppFromBeforeTheRecord(runner, TemplateText);
            runner.EnsureProjectSetup();

            ChangeTemplate(runner, "index.tsx", "// a newer template");

            Assert.AreEqual(DefaultFileStatus.TemplateUpdated, runner.GetDefaultFileStatus("index.tsx"),
                "An untouched copy of the template reads as changed by the user after an upgrade.");
        }

        [Test]
        public void ASeededFileOfUnknownOriginIsNotCalledModified() {
            var runner = MakeRunner();
            MakeAppFromBeforeTheRecord(runner, _ => "// scaffolded long ago");
            runner.EnsureProjectSetup();

            // No record of what it was written with: the user may have changed it, or the template may have.
            Assert.AreEqual(DefaultFileStatus.Differs, runner.GetDefaultFileStatus("package.json"));
        }

        [Test]
        public void TheTemplateWithOtherLineEndingsIsUpToDate() {
            var runner = MakeRunner();
            runner.EnsureProjectSetup();
            // What a Windows checkout with autocrlf makes of it.
            File.WriteAllText(InWorkingDir("index.tsx"), TemplateText("index.tsx").Replace("\r\n", "\n").Replace("\n", "\r\n"));

            Assert.AreEqual(DefaultFileStatus.UpToDate, runner.GetDefaultFileStatus("index.tsx"));
        }

        [Test]
        public void ARestoreBeforeTheFirstPlayLeavesTheAppNew() {
            var runner = MakeRunner();
            Assert.IsTrue(runner.RestoreDefaultFile("index.tsx"));

            runner.EnsureProjectSetup();

            foreach (var path in ListedPaths(runner)) Assert.IsTrue(File.Exists(InWorkingDir(path)), $"{path} was not written: one Restore made the app look old.");
        }
    }
}
