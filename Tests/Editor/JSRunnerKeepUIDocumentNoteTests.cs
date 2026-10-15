using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Unity 6.5's UIDocument inspector suggests migrating to Panel Renderer, which cannot draw
    /// the edit-mode preview. JSRunner's inspector answers it with one line, shown while the
    /// GameObject has a UIDocument. Builds the real inspector and reads the line's display.
    /// </summary>
    [TestFixture]
    public class JSRunnerKeepUIDocumentNoteTests {
        GameObject _go;
        JSRunner _runner;
        UnityEditor.Editor _editor;
        Scene _previewScene;

        [SetUp]
        public void SetUp() {
            // A preview scene, so the open scene is never dirtied.
            _previewScene = EditorSceneManager.NewPreviewScene();
            _go = new GameObject(nameof(JSRunnerKeepUIDocumentNoteTests));
            SceneManager.MoveGameObjectToScene(_go, _previewScene);
            _runner = _go.AddComponent<JSRunner>();
        }

        [TearDown]
        public void TearDown() {
            if (_editor != null) Object.DestroyImmediate(_editor);
            if (_go != null) Object.DestroyImmediate(_go);
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
        }

        VisualElement BuildInspector() {
            _editor = UnityEditor.Editor.CreateEditor(_runner, typeof(JSRunnerEditor));
            return _editor.CreateInspectorGUI();
        }

        static Label Note(VisualElement root) =>
            root.Query<Label>().ToList().FirstOrDefault(l => l.text == OneJSEditorDesign.Texts.KeepUIDocument);

        /// <summary>The note's box, the element whose display the inspector switches.</summary>
        static DisplayStyle Shown(Label note) => note.parent.style.display.value;

        void Update() {
            var update = typeof(JSRunnerEditor).GetMethod("UpdateDynamicUI", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(update, "JSRunnerEditor.UpdateDynamicUI was not found: this test is out of sync with the inspector.");
            update.Invoke(_editor, null);
        }

#if UNITY_6000_5_OR_NEWER
        [Test]
        public void WithAUIDocument_TheInspectorSaysToKeepIt_AndHidesTheLineOnceItIsGone() {
            var doc = _go.AddComponent<UIDocument>();
            var note = Note(BuildInspector());
            Assert.IsNotNull(note, "JSRunner's inspector should answer UIDocument's Panel Renderer suggestion on Unity 6.5+.");
            Assert.AreEqual(DisplayStyle.Flex, Shown(note), "The line should show while the GameObject has a UIDocument.");

            Object.DestroyImmediate(doc);
            Update();
            Assert.AreEqual(DisplayStyle.None, Shown(note), "The line should hide once the UIDocument it is about is gone.");
        }

        [Test]
        public void WithNoUIDocument_TheLineIsHidden() {
            var note = Note(BuildInspector());
            Assert.IsNotNull(note, "JSRunner's inspector should build the Panel Renderer line on Unity 6.5+.");
            Assert.AreEqual(DisplayStyle.None, Shown(note), "With no UIDocument there is nothing to keep, so the line should be hidden.");
        }
#else
        [Test]
        public void BeforeUnity65_ThereIsNoLine() {
            _go.AddComponent<UIDocument>();
            Assert.IsNull(Note(BuildInspector()), "Before Unity 6.5 UIDocument suggests nothing, so the inspector should say nothing.");
        }
#endif
    }
}
