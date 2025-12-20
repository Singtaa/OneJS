using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Editor window for generating TypeScript declarations from C# types
    /// </summary>
    public class TypeGeneratorWindow : EditorWindow {
        // Assembly panel
        private Vector2 _assemblyScroll;
        private string _assemblyFilter = "";
        private List<AssemblyEntry> _assemblies = new();
        private HashSet<string> _selectedAssemblies = new();

        // Type panel
        private Vector2 _typeScroll;
        private string _typeFilter = "";
        private List<TypeEntry> _types = new();
        private HashSet<Type> _selectedTypes = new();

        // Preview panel
        private Vector2 _previewScroll;
        private string _previewContent = "";

        // Settings
        private string _outputPath = "Assets/Gen/Typings/csharp/index.d.ts";
        private bool _includeDocumentation = true;
        private bool _includeObsolete = false;
        private bool _autoRefreshPreview = true;

        // State
        private bool _needsRefresh = true;
        private bool _needsPreviewUpdate = true;

        [MenuItem("OneJS/Type Generator")]
        public static void ShowWindow() {
            var window = GetWindow<TypeGeneratorWindow>("Type Generator");
            window.minSize = new Vector2(900, 600);
            window.Show();
        }

        private void OnEnable() {
            RefreshAssemblies();
        }

        private void OnGUI() {
            // Toolbar
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();

            // Left panel - Assemblies
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            DrawAssemblyPanel();
            EditorGUILayout.EndVertical();

            // Middle panel - Types
            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            DrawTypePanel();
            EditorGUILayout.EndVertical();

            // Right panel - Preview
            EditorGUILayout.BeginVertical();
            DrawPreviewPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Bottom panel - Settings and actions
            DrawBottomPanel();

            // Handle deferred updates
            if (_needsRefresh) {
                _needsRefresh = false;
                RefreshTypes();
            }

            if (_needsPreviewUpdate && _autoRefreshPreview) {
                _needsPreviewUpdate = false;
                UpdatePreview();
            }
        }

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) {
                RefreshAssemblies();
            }

            GUILayout.FlexibleSpace();

            _autoRefreshPreview = GUILayout.Toggle(_autoRefreshPreview, "Auto Preview", EditorStyles.toolbarButton);

            if (!_autoRefreshPreview && GUILayout.Button("Update Preview", EditorStyles.toolbarButton)) {
                UpdatePreview();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssemblyPanel() {
            EditorGUILayout.LabelField("Assemblies", EditorStyles.boldLabel);

            // Filter
            EditorGUI.BeginChangeCheck();
            _assemblyFilter = EditorGUILayout.TextField(_assemblyFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) {
                // Filter changed
            }

            // Quick select buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Unity", EditorStyles.miniButtonLeft)) {
                SelectAssembliesByPrefix("UnityEngine");
            }
            if (GUILayout.Button("User", EditorStyles.miniButtonMid)) {
                SelectAssembliesByPrefix("Assembly-CSharp");
            }
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight)) {
                _selectedAssemblies.Clear();
                _needsRefresh = true;
            }
            EditorGUILayout.EndHorizontal();

            // Assembly list
            _assemblyScroll = EditorGUILayout.BeginScrollView(_assemblyScroll, GUILayout.ExpandHeight(true));

            var filter = _assemblyFilter.ToLowerInvariant();
            foreach (var asm in _assemblies) {
                if (!string.IsNullOrEmpty(filter) && !asm.Name.ToLowerInvariant().Contains(filter)) {
                    continue;
                }

                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.ToggleLeft(asm.Name, _selectedAssemblies.Contains(asm.Name));
                if (EditorGUI.EndChangeCheck()) {
                    if (selected) {
                        _selectedAssemblies.Add(asm.Name);
                    } else {
                        _selectedAssemblies.Remove(asm.Name);
                    }
                    _needsRefresh = true;
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"Selected: {_selectedAssemblies.Count}", EditorStyles.miniLabel);
        }

        private void DrawTypePanel() {
            EditorGUILayout.LabelField("Types", EditorStyles.boldLabel);

            // Filter
            EditorGUI.BeginChangeCheck();
            _typeFilter = EditorGUILayout.TextField(_typeFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) {
                // Filter changed
            }

            // Quick select buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft)) {
                foreach (var t in _types) {
                    if (PassesTypeFilter(t)) {
                        _selectedTypes.Add(t.Type);
                    }
                }
                _needsPreviewUpdate = true;
            }
            if (GUILayout.Button("None", EditorStyles.miniButtonMid)) {
                _selectedTypes.Clear();
                _needsPreviewUpdate = true;
            }
            if (GUILayout.Button("Classes", EditorStyles.miniButtonRight)) {
                foreach (var t in _types) {
                    if (t.Type.IsClass && !t.Type.IsAbstract) {
                        _selectedTypes.Add(t.Type);
                    }
                }
                _needsPreviewUpdate = true;
            }
            EditorGUILayout.EndHorizontal();

            // Type list
            _typeScroll = EditorGUILayout.BeginScrollView(_typeScroll, GUILayout.ExpandHeight(true));

            var filter = _typeFilter.ToLowerInvariant();
            foreach (var entry in _types) {
                if (!PassesTypeFilter(entry, filter)) {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.Toggle(_selectedTypes.Contains(entry.Type), GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck()) {
                    if (selected) {
                        _selectedTypes.Add(entry.Type);
                    } else {
                        _selectedTypes.Remove(entry.Type);
                    }
                    _needsPreviewUpdate = true;
                }

                // Type icon and name
                var icon = GetTypeIcon(entry.Type);
                GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(16));
                EditorGUILayout.LabelField(entry.DisplayName, GUILayout.ExpandWidth(true));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"Selected: {_selectedTypes.Count} / {_types.Count}", EditorStyles.miniLabel);
        }

        private void DrawPreviewPanel() {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));

            // Use a monospace style for code
            var style = new GUIStyle(EditorStyles.textArea) {
                font = Font.CreateDynamicFontFromOSFont("Menlo", 11),
                wordWrap = false
            };

            EditorGUILayout.TextArea(_previewContent, style, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.EndScrollView();

            // Preview stats
            var lines = string.IsNullOrEmpty(_previewContent) ? 0 : _previewContent.Split('\n').Length;
            var size = System.Text.Encoding.UTF8.GetByteCount(_previewContent);
            EditorGUILayout.LabelField($"Lines: {lines}, Size: {FormatBytes(size)}", EditorStyles.miniLabel);
        }

        private void DrawBottomPanel() {
            EditorGUILayout.Space(5);

            // Settings
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Path:", GUILayout.Width(80));
            _outputPath = EditorGUILayout.TextField(_outputPath);
            if (GUILayout.Button("...", GUILayout.Width(30))) {
                var path = EditorUtility.SaveFilePanel("Save TypeScript Declaration", "Assets", "index", "d.ts");
                if (!string.IsNullOrEmpty(path)) {
                    // Convert to relative path
                    if (path.StartsWith(Application.dataPath)) {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    _outputPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _includeDocumentation = EditorGUILayout.ToggleLeft("Include Documentation", _includeDocumentation, GUILayout.Width(150));
            _includeObsolete = EditorGUILayout.ToggleLeft("Include Obsolete", _includeObsolete, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Generate button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = _selectedTypes.Count > 0;
            if (GUILayout.Button("Generate", GUILayout.Width(120), GUILayout.Height(30))) {
                Generate();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }

        private void RefreshAssemblies() {
            _assemblies.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a => !a.FullName.StartsWith("System."))
                .Where(a => !a.FullName.StartsWith("mscorlib"))
                .Where(a => !a.FullName.StartsWith("netstandard"))
                .Where(a => !a.FullName.StartsWith("Microsoft."))
                .Where(a => !a.FullName.StartsWith("Mono."))
                .OrderBy(a => a.GetName().Name);

            foreach (var asm in assemblies) {
                try {
                    var name = asm.GetName().Name;
                    _assemblies.Add(new AssemblyEntry {
                        Name = name,
                        Assembly = asm
                    });
                } catch {
                    // Skip assemblies that can't be loaded
                }
            }

            _needsRefresh = true;
        }

        private void RefreshTypes() {
            _types.Clear();
            _selectedTypes.Clear();

            foreach (var asmName in _selectedAssemblies) {
                var asmEntry = _assemblies.FirstOrDefault(a => a.Name == asmName);
                if (asmEntry == null) continue;

                try {
                    var types = asmEntry.Assembly.GetTypes()
                        .Where(t => t.IsPublic)
                        .Where(t => !TypeMapper.ShouldSkipType(t))
                        .OrderBy(t => t.FullName);

                    foreach (var type in types) {
                        _types.Add(new TypeEntry {
                            Type = type,
                            DisplayName = type.FullName ?? type.Name
                        });
                    }
                } catch (ReflectionTypeLoadException ex) {
                    // Load what we can
                    foreach (var type in ex.Types.Where(t => t != null)) {
                        if (type.IsPublic && !TypeMapper.ShouldSkipType(type)) {
                            _types.Add(new TypeEntry {
                                Type = type,
                                DisplayName = type.FullName ?? type.Name
                            });
                        }
                    }
                } catch {
                    // Skip problematic assemblies
                }
            }

            _needsPreviewUpdate = true;
        }

        private void UpdatePreview() {
            if (_selectedTypes.Count == 0) {
                _previewContent = "// Select types to preview the generated TypeScript declarations";
                return;
            }

            try {
                var analyzer = new TypeAnalyzer(new AnalyzerOptions {
                    IncludeObsolete = _includeObsolete
                });

                var typeInfos = analyzer.AnalyzeTypes(_selectedTypes);

                var emitter = new TypeScriptEmitter(new EmitterOptions {
                    IncludeDocumentation = _includeDocumentation,
                    IncludeObsoleteWarnings = _includeObsolete
                });

                _previewContent = emitter.Emit(typeInfos);
            } catch (Exception ex) {
                _previewContent = $"// Error generating preview:\n// {ex.Message}\n// {ex.StackTrace}";
            }
        }

        private void Generate() {
            if (_selectedTypes.Count == 0) {
                EditorUtility.DisplayDialog("No Types Selected", "Please select at least one type to generate.", "OK");
                return;
            }

            try {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                var analyzer = new TypeAnalyzer(new AnalyzerOptions {
                    IncludeObsolete = _includeObsolete
                });

                var typeInfos = analyzer.AnalyzeTypes(_selectedTypes);

                var emitter = new TypeScriptEmitter(new EmitterOptions {
                    IncludeDocumentation = _includeDocumentation,
                    IncludeObsoleteWarnings = _includeObsolete
                });

                var content = emitter.Emit(typeInfos);

                File.WriteAllText(_outputPath, content);
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Generation Complete",
                    $"Generated {typeInfos.Count} types to:\n{_outputPath}", "OK");

            } catch (Exception ex) {
                EditorUtility.DisplayDialog("Generation Failed", ex.Message, "OK");
                Debug.LogException(ex);
            }
        }

        private void SelectAssembliesByPrefix(string prefix) {
            foreach (var asm in _assemblies) {
                if (asm.Name.StartsWith(prefix)) {
                    _selectedAssemblies.Add(asm.Name);
                }
            }
            _needsRefresh = true;
        }

        private bool PassesTypeFilter(TypeEntry entry, string filter = null) {
            if (string.IsNullOrEmpty(filter)) {
                filter = _typeFilter.ToLowerInvariant();
            }
            if (string.IsNullOrEmpty(filter)) return true;
            return entry.DisplayName.ToLowerInvariant().Contains(filter);
        }

        private GUIContent GetTypeIcon(Type type) {
            string iconName;
            if (type.IsInterface) iconName = "d_cs Script Icon";
            else if (type.IsEnum) iconName = "d_FilterByType";
            else if (type.IsValueType) iconName = "d_PreMatCube";
            else iconName = "d_cs Script Icon";

            var icon = EditorGUIUtility.IconContent(iconName);
            return icon ?? GUIContent.none;
        }

        private string FormatBytes(int bytes) {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private class AssemblyEntry {
            public string Name;
            public Assembly Assembly;
        }

        private class TypeEntry {
            public Type Type;
            public string DisplayName;
        }
    }
}
