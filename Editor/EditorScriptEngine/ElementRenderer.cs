using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class RendererWrapper {
        public Type type;

        /// <summary>
        /// Will be set from JS
        /// </summary>
        public Action<VisualElement> render;

        public VisualElement root;
        public EditorScriptEngine engine;
    }

    [InitializeOnLoad]
    public class ElementRenderer {
        static Dictionary<Type, RendererWrapper> renderers = new();

        static ElementRenderer() {
            Clear();
        }

        public static void Register(Type type, Action<VisualElement> render) {
            if (renderers.TryGetValue(type, out var renderer)) {
                renderer.render = render;
            } else {
                // root and engine will be null here (until first render)
                renderers[type] = new RendererWrapper {
                    type = type,
                    render = render
                };
            }
        }

        public static void Clear() {
            renderers.Clear();
        }

        public static void RefreshAll() {
            foreach (var renderer in renderers.Values) {
                if (renderer.root != null) {
                    renderer.root.Clear();
                    renderer.engine.ApplyStyleSheets(renderer.root);
                    renderer.render(renderer.root);
                }
            }
        }

        public void Refresh(Type type) {
            if (renderers.TryGetValue(type, out var renderer)) {
                renderer.root.Clear();
                renderer.render(renderer.root);
            }
        }

        public static bool TryRender(Type type, VisualElement root, EditorScriptEngine engine) {
            if (renderers.TryGetValue(type, out var renderer)) {
                root.Clear();
                engine.ApplyStyleSheets(root);
                renderer.render(root);

                renderer.root = root;
                renderer.engine = engine;
                return true;
            }
            return false;
        }
    }
}