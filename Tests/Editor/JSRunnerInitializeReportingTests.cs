using System;
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
    /// EditMode tests for what Initialize Project reports when it cannot create anything.
    ///
    /// The case that matters is a PanelSettings assigned from a folder that is not a project
    /// folder (no "~" and no app.js.txt). JSRunner then resolves no working directory, so
    /// nothing is scaffolded, and the inspector hides its tab bar. For a long time the button
    /// still logged "Project initialized. Working directory and default files created.", which
    /// is how issue 114 ended up hunting for a folder-naming rule that does not exist.
    ///
    /// RunInitializeProject is private, so it is reached by reflection; the MethodInfo is
    /// resolved and asserted in SetUp so a rename surfaces here rather than as a
    /// NullReferenceException inside each test.
    /// </summary>
    [TestFixture]
    public class JSRunnerInitializeReportingTests {
        const string TEST_FOLDER = "Assets/OneJSInitializeReportingTests";

        GameObject _go;
        MethodInfo _runInitializeProject;

        // The test object lives in a preview scene, never in whoever's scene is open. A GameObject
        // created in the open scene dirties it, and the EditMode runner then prompts to save modified
        // scenes when the NEXT run starts, which blocks the editor on a modal dialog.
        Scene _previewScene;

        static string AbsolutePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath.Replace('/', Path.DirectorySeparatorChar));

        [SetUp]
        public void SetUp() {
            _runInitializeProject = typeof(JSRunnerEditor).GetMethod(
                "RunInitializeProject", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_runInitializeProject,
                "JSRunnerEditor.RunInitializeProject() was not found via reflection: " +
                "these tests are out of sync with the implementation.");

            _previewScene = EditorSceneManager.NewPreviewScene();

            if (AssetDatabase.IsValidFolder(TEST_FOLDER)) AssetDatabase.DeleteAsset(TEST_FOLDER);
            Directory.CreateDirectory(AbsolutePath(TEST_FOLDER));
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            AssetDatabase.DeleteAsset(TEST_FOLDER);
            AssetDatabase.Refresh();
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        [Test]
        public void InitializeProject_WhenPanelSettingsFolderIsNotAProject_WarnsInsteadOfClaimingSuccess() {
            // A PanelSettings made the ordinary Unity way, sitting in a folder that holds no
            // "~" working directory and no app.js.txt. This is the state issue 114 was in.
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panelSettings, TEST_FOLDER + "/PanelSettings.asset");
            AssetDatabase.SaveAssets();

            _go = new GameObject(nameof(JSRunnerInitializeReportingTests));
            SceneManager.MoveGameObjectToScene(_go, _previewScene);
            var runner = _go.AddComponent<JSRunner>();
            runner.SetPanelSettings(panelSettings);
            Assert.IsFalse(runner.IsPanelSettingsInValidProjectFolder(),
                "Test setup is wrong: the folder was supposed to be an invalid project folder.");

            var logs = new List<string>();
            var warnings = new List<string>();
            Application.LogCallback capture = (condition, stack, type) =>
                (type == LogType.Warning ? warnings : logs).Add(condition);

            Application.logMessageReceived += capture;
            try {
                var editor = UnityEditor.Editor.CreateEditor(runner, typeof(JSRunnerEditor));
                try { _runInitializeProject.Invoke(editor, null); }
                finally { UnityEngine.Object.DestroyImmediate(editor); }
            } finally {
                Application.logMessageReceived -= capture;
            }

            // It created nothing, so it must not say it created anything.
            Assert.IsFalse(logs.Any(l => l.Contains("Project initialized")),
                "Initialize Project reported success while creating nothing. Logged: " +
                string.Join(" | ", logs));

            // And it must say what is actually wrong, naming the folder so the user can go look.
            Assert.IsTrue(warnings.Any(w => w.Contains(TEST_FOLDER)),
                "Expected a warning naming '" + TEST_FOLDER + "'. Warnings: " +
                (warnings.Count == 0 ? "(none)" : string.Join(" | ", warnings)));

            // The report has to match reality: no working directory was scaffolded.
            Assert.IsFalse(Directory.Exists(Path.Combine(AbsolutePath(TEST_FOLDER), "~")),
                "Initialize Project scaffolded into a folder it was not supposed to touch.");
        }
    }
}
