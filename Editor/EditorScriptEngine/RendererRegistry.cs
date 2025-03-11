using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Editor {
    public class RendererInfo {
        public Type type;
        public Action<Object, VisualElement> render;
        public Object target;
        public HashSet<VisualElement> roots;
    }

    public class RendererRegistry {
        public int RenderersCount => _renderers.Count;
        public string RenderersTypesString => string.Join(", ", _renderers.Keys);
        
        Dictionary<Type, RendererInfo> _renderers = new();
        EditorScriptEngine _engine;

        public RendererRegistry(EditorScriptEngine engine) {
            _engine = engine;
        }

        public void Register(Type type, Action<Object, VisualElement> render) {
            if (_renderers.TryGetValue(type, out var renderer)) {
                renderer.render = render;
            } else {
                _renderers[type] = new RendererInfo {
                    type = type,
                    render = render,
                    roots = new HashSet<VisualElement>()
                };
            }
        }
        
        public RendererInfo GetRendererInfo(Type type) {
            return _renderers[type];
        }

        public bool TryGetRendererInfo(Type type, out RendererInfo renderer) {
            return _renderers.TryGetValue(type, out renderer);
        }
        
        public void ReRenderAll() {
            foreach (var renderer in _renderers.Values) {
                foreach (var root in renderer.roots) {
                    root.Clear();
                    _engine.ApplyStyleSheets(root);
                    renderer.render(renderer.target, root);
                }
            }
        }
    }
}