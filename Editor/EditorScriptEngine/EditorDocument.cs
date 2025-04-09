using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OneJS.Dom;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /**
     * Represents an editor-specific document, independent of UIDocument, which is used only at runtime.
     */
    public class EditorDocument : IDocument {
        Dictionary<VisualElement, Dom.Dom> _elementToDomLookup = new();
        Dictionary<string, Type> _tagCache = new();
        Type[] _tagTypes;

        IScriptEngine _scriptEngine;
        Dictionary<string, StyleSheet> _runtimeStyleSheets = new Dictionary<string, StyleSheet>();
        
        Dictionary<string, Texture2D> _imageCache = new();
        Dictionary<string, Font> _fontCache = new();
        Dictionary<string, FontDefinition> _fontDefinitionCache = new();
        WebApi _webApi = new WebApi(); // TODO May need a dedicated WebApi that uses Editor Coroutines

        public EditorDocument(IScriptEngine scriptEngine) {
            _scriptEngine = scriptEngine;
            _tagTypes = GetAllVisualElementTypes();
        }

        public void Dispose() {
            clearRuntimeStyleSheets();
        }

        public Dom.Dom createElement(string tagName) {
            Type type;
            // Try to lookup from tagCache, may still be null if not a VE type.
            if (!_tagCache.TryGetValue(tagName, out type)) {
                type = GetVisualElementType(tagName);
                _tagCache[tagName] = type;
            }

            if (type == null) {
                return new Dom.Dom(new VisualElement(), this);
            }
            return new Dom.Dom(Activator.CreateInstance(type) as VisualElement, this);
        }

        public Dom.Dom createElement(string tagName, ElementCreationOptions options) {
            return createElement(tagName);
        }

        public Dom.Dom createElementNS(string ns, string tagName, ElementCreationOptions options) { // namespace currently not used
            return createElement(tagName);
        }

        public Dom.Dom createTextNode(string text) {
            var tn = new TextElement();
            tn.text = text;
            return new Dom.Dom(tn, this);
        }

#if UNITY_2022_1_OR_NEWER
        public Dom.Dom createTextNode(string text, bool selectable) {
            var tn = new TextElement();
            tn.selection.isSelectable = selectable;
            tn.text = text;
            return new Dom.Dom(tn, this);
        }
#endif

        public void ApplyRuntimeStyleSheets(VisualElement ve) {
            // runtime stylesheets can potentially be null/destroy when entering/exiting play mode
            // so here we check and rebuild them if necessary
            foreach (var key in _runtimeStyleSheets.Keys.ToList()) {
                if (_runtimeStyleSheets[key] == null) {
                    var ss = BuildStyleSheet(key);
                    if (ss != null) {
                        _runtimeStyleSheets[key] = ss;
                    }
                }
            }

            foreach (var ss in _runtimeStyleSheets.Values.Where(v => v != null)) {
                if (!ve.styleSheets.Contains(ss)) {
                    ve.styleSheets.Add(ss);
                }
            }
        }

        public void addRuntimeUSS(string uss) {
            // var hash = GenerateShortHash(uss);
            if (_runtimeStyleSheets.ContainsKey(uss))
                return;
            var ss = BuildStyleSheet(uss);
            if (ss == null)
                return;
            _runtimeStyleSheets.Add(uss, ss);
        }

        public StyleSheet BuildStyleSheet(string uss) {
            var ss = ScriptableObject.CreateInstance<StyleSheet>();
            var builder = new CustomStyleSheets.CustomStyleSheetImporterImpl(_scriptEngine);
            builder.BuildStyleSheet(ss, uss);
            if (builder.importErrors.hasErrors) {
                Debug.LogError($"Runtime USS Error(s)");
                foreach (var error in builder.importErrors) {
                    Debug.LogError(error);
                }
                return null;
            }
            return ss;
        }

        public void clearRuntimeStyleSheets() {
            foreach (var sheet in _runtimeStyleSheets) {
                UnityEngine.Object.DestroyImmediate(sheet.Value);
            }
            _runtimeStyleSheets.Clear();
        }

        public void removeRuntimeStyleSheet(string uss) {
            // var hash = GenerateShortHash(uss);
            if (!_runtimeStyleSheets.ContainsKey(uss))
                return;
            var ss = _runtimeStyleSheets[uss];
            _runtimeStyleSheets.Remove(uss);
            UnityEngine.Object.DestroyImmediate(ss);
        }

        public void removeRuntimeStyleSheet(StyleSheet sheet) {
            // find sheet in _runtimeStyleSheets
            var uss = _runtimeStyleSheets.FirstOrDefault(x => x.Value == sheet).Key;
            if (uss == null)
                return;
            _runtimeStyleSheets.Remove(uss);
            UnityEngine.Object.Destroy(sheet);
        }
        
        public void clearCache() {
            _imageCache.Clear();
            _fontCache.Clear();
            _fontDefinitionCache.Clear();
        }
        
        public Coroutine loadRemoteImage(string url, Action<Texture2D> callback) {
            if (_imageCache.TryGetValue(url, out var tex)) {
                callback(tex);
                return null;
            }
            return _webApi.getImage(url, (texture) => {
                if (texture == null) {
                    Debug.LogError($"Failed to load image: {url}");
                    return;
                }
                _imageCache[url] = texture;
                callback(texture);
            });
        }

        /// <summary>
        /// Loads an image from the specified path and returns a Texture2D object.
        /// </summary>
        /// <param name="path">Relative to the WorkingDir</param>
        public Texture2D loadImage(string path, FilterMode filterMode = FilterMode.Bilinear) {
            if (_imageCache.TryGetValue(path, out var texture)) {
                return texture;
            }
            try {
                path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
                var rawData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2); // Create an empty Texture; size doesn't matter
                tex.LoadImage(rawData);
                tex.filterMode = filterMode;
                _imageCache[path] = tex;
                return tex;
            } catch (Exception) {
                Debug.LogError($"Failed to load image: {path}");
                // Debug.LogError(e);
                return null;
            }
        }

        /// <summary>
        /// Loads a font from the specified path and returns a Font object.
        /// </summary>
        /// <param name="path">Relative to the WorkingDir</param>
        public Font loadFont(string path) {
            if (_fontCache.TryGetValue(path, out var f)) {
                return f;
            }
            try {
                path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
                var font = new Font(path);
                _fontCache[path] = font;
                return font;
            } catch (Exception) {
                Debug.LogError($"Failed to load font: {path}");
                // Debug.LogError(e);
                return null;
            }
        }

        /// <summary>
        /// Loads a font from the specified path and returns a FontDefinition object.
        /// </summary>
        /// <param name="path">Relative to the WorkingDir</param>
        public FontDefinition loadFontDefinition(string path) {
            if (_fontDefinitionCache.TryGetValue(path, out var fd)) {
                return fd;
            }
            path = Path.IsPathRooted(path) ? path : Path.Combine(_scriptEngine.WorkingDir, path);
            var font = new Font(path);
            _fontCache[path] = font;
            return FontDefinition.FromFont(font);
        }

        Type[] GetAllVisualElementTypes() {
            List<Type> visualElementTypes = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies) {
                Type[] types = null;

                try {
                    types = assembly.GetTypes();
                } catch (ReflectionTypeLoadException ex) {
                    types = ex.Types.Where(t => t != null).ToArray(); // Handle partially loaded types
                }

                foreach (Type type in types) {
                    if (type.IsSubclassOf(typeof(VisualElement))) {
                        visualElementTypes.Add(type);
                    }
                }
            }

            return visualElementTypes.ToArray();
        }

        Type GetVisualElementType(string tagName) {
            Type foundType = null;
            var typeNameL = tagName.Replace("-", "").ToLower();
            foreach (var tagType in _tagTypes) {
                if (tagType.Name.ToLower() == typeNameL) {
                    foundType = tagType;
                    break;
                }
            }
            return foundType;
        }

        public void AddCachingDom(Dom.Dom dom) {
            _elementToDomLookup[dom.ve] = dom;
        }

        public void RemoveCachingDom(Dom.Dom dom) {
            _elementToDomLookup.Remove(dom.ve);
        }

        public static string GenerateShortHash(string input) {
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                string hash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8); // First 8 chars
                return hash;
            }
        }
    }
}