using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class ScratchpadWindow : UnityEditor.EditorWindow {
        // [MenuItem("Tmp/Foo")]
        // public static void ShowFoo() {
        //     GetWindow("Foo", w => {
        //         var root = new VisualElement();
        //         root.Add(new Label("Hello, This is Foo!"));
        //         return root;
        //     });
        // }
        //
        // [MenuItem("Tmp/Bar")]
        // public static void ShowBar() {
        //     GetWindow("Bar", w => {
        //         var root = new VisualElement();
        //         root.Add(new Label("Hello, This is Bar!"));
        //         return root;
        //     });
        // }

        public static ScratchpadWindow Get(string name, bool focus = false) {
            var scratchWindows = Resources.FindObjectsOfTypeAll<ScratchpadWindow>();
            var foundWindow = Array.Find(scratchWindows, w => w.name == name);
            if (foundWindow != null) {
                if (focus)
                    foundWindow.Focus();
                foundWindow.rootVisualElement.Clear();
                return foundWindow;
            }
            var window = CreateInstance<ScratchpadWindow>();
            window.name = name;
            var icon = Resources.Load("onejs/icons/fizzing-flask") as Texture2D;
            window.titleContent = new GUIContent($"{name}", icon);
            window.Show();
            window.rootVisualElement.Clear();
            return window;
        }

        void CreateGUI() {
            rootVisualElement.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            rootVisualElement.style.height = new StyleLength(new Length(100, LengthUnit.Percent));

            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            rootVisualElement.AddToClassList("root");
        }

        void OnGeometryChanged(GeometryChangedEvent evt) {
            if (rootVisualElement.childCount == 0)
                return;
            var child = rootVisualElement.ElementAt(0);
            if (child != null) {
                var myHeight = rootVisualElement.resolvedStyle.height;
                child.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                child.style.height = new StyleLength(new Length(myHeight - 24, LengthUnit.Pixel));
            }
        }
    }
}