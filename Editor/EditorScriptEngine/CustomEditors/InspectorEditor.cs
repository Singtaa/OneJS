using System;
using OneJS.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public abstract class InspectorEditor : UnityEditor.Editor {
        public EditorScriptEngine engine;
        public StyleSheet inspectorUSS;

        protected VisualElement _root;

        void OnDestroy() {
            if (engine == null)
                return;
            // Remove the render root to prevent memory leaks
            engine.RemoveRenderRoot(GetType(), _root);
            // Reload is necessary to get rid of stale events when the editor is closed.
            // Doing it on the EngineHost is more performant than doing it on _engine.
            if (engine.EngineHost != null)
                engine.EngineHost.DoReload();
        }

        public override VisualElement CreateInspectorGUI() {
            if (engine == null)
                return null;
            if (_root != null) {
                engine.RemoveRenderRoot(GetType(), _root);
                _root = null;
            }
            _root = new VisualElement();
            AddInspectorUSS(inspectorUSS);
            _root.name = "inspector-editor-root";
            engine.Render(this, _root);
            return _root;
        }

        protected void AddInspectorUSS(StyleSheet uss) {
            if (uss == null)
                return;
            // PropertyEditor is the base type. It's better to use this instead of InspectorWindow because
            // it's more general and certain 3rd party plugins might use that to create new tabs.
            var inspectorWindowType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PropertyEditor");
            var inspectorWindows = Resources.FindObjectsOfTypeAll(inspectorWindowType);
            foreach (var inspectorWindow in inspectorWindows) {
                var targetEle = (inspectorWindow as EditorWindow).rootVisualElement;
                if (targetEle != null && !targetEle.styleSheets.Contains(uss)) {
                    targetEle.styleSheets.Add(uss);
                }
            }
        }
    }
}