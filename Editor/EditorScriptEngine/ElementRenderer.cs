using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    public class RendererWrapper {
        public Type type;
        public Action<EditorWindow> render;
        public EditorScriptEngine engine;
        public EditorWindow window;
    }

    [InitializeOnLoad]
    public class ElementRenderer {
        static Dictionary<Type, RendererWrapper> renderers = new();

        static ElementRenderer() {
            Clear();
        }

        public static void Register(Type type, Action<EditorWindow> render) {
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
                if (renderer.window != null) {
                    renderer.window.rootVisualElement.Clear();
                    renderer.engine.ApplyStyleSheets(renderer.window.rootVisualElement);
                    renderer.render(renderer.window);
                }
            }
        }

        public void Refresh(Type type) {
            if (renderers.TryGetValue(type, out var renderer)) {
                renderer.window.rootVisualElement.Clear();
                renderer.render(renderer.window);
            }
        }

        public static bool TryRender<T>(T window, EditorScriptEngine engine) where T : EditorWindow {
            var type = typeof(T);
            if (renderers.TryGetValue(type, out var renderer)) {
                var root = window.rootVisualElement;
                root.Clear();
                engine.ApplyStyleSheets(root);
                renderer.render(window);

                renderer.window = window;
                renderer.engine = engine;
                return true;
            }
            return false;
        }
    }
}