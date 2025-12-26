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
    /// Editor window for generating TypeScript declarations from C# types.
    /// Uses virtualized TreeView for efficient display of large type lists.
    /// </summary>
    public class TypeGeneratorWindow : EditorWindow {
        // Assembly panel
        private Vector2 _assemblyScroll;
        private string _assemblyFilter = "";
        private List<AssemblyEntry> _assemblies = new();
        private HashSet<string> _selectedAssemblies = new();

        // Type panel - TreeView based
        private TypeTreeView _typeTreeView;
        private TreeViewState _typeTreeViewState;
        private SearchField _typeSearchField;
        private List<TypeEntry> _allTypes = new();
        private List<TypeEntry> _filteredTypes = new();
        private string _typeFilter = "";
        private string _lastAppliedFilter = "";

        // Preview panel
        private Vector2 _previewScroll;
        private string _previewContent = "";

        // Settings
        private string _outputPath = "Assets/Gen/Typings/csharp/index.d.ts";
        private bool _includeDocumentation = true;
        private bool _includeObsolete = false;
        private bool _autoRefreshPreview = true;

        // State flags
        private bool _needsTypeRefresh = true;
        private bool _needsPreviewUpdate = true;

        // Debouncing
        private double _filterDebounceTime;
        private double _previewDebounceTime;
        private const double FilterDebounceDelay = 0.15;   // 150ms
        private const double PreviewDebounceDelay = 0.3;   // 300ms

        // Cached GUI styles
        private static GUIStyle s_monoStyle;
        private static bool s_stylesInitialized;

        [MenuItem("OneJS/Type Generator")]
        public static void ShowWindow() {
            var window = GetWindow<TypeGeneratorWindow>("Type Generator");
            window.minSize = new Vector2(900, 600);
            window.Show();
        }

        private void OnEnable() {
            _typeTreeViewState = new TreeViewState();
            _typeSearchField = new SearchField();
            RefreshAssemblies();
        }

        private void OnDisable() {
            // Clean up TreeView
            _typeTreeView = null;
        }

        private static void InitializeStyles() {
            if (s_stylesInitialized) return;
            s_stylesInitialized = true;

            s_monoStyle = new GUIStyle(EditorStyles.textArea) {
                wordWrap = false,
                richText = false
            };

            // Try to use a monospace font
            var monoFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            if (monoFont == null) {
                monoFont = Font.CreateDynamicFontFromOSFont("Menlo", 11);
            }
            if (monoFont == null) {
                monoFont = Font.CreateDynamicFontFromOSFont("Consolas", 11);
            }
            if (monoFont != null) {
                s_monoStyle.font = monoFont;
            }
        }

        private void OnGUI() {
            InitializeStyles();

            // Toolbar
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();

            // Left panel - Assemblies
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            DrawAssemblyPanel();
            EditorGUILayout.EndVertical();

            // Middle panel - Types (TreeView)
            EditorGUILayout.BeginVertical(GUILayout.Width(350));
            DrawTypePanel();
            EditorGUILayout.EndVertical();

            // Right panel - Preview
            EditorGUILayout.BeginVertical();
            DrawPreviewPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Bottom panel - Settings and actions
            DrawBottomPanel();

            // Handle deferred updates with debouncing
            ProcessDeferredUpdates();
        }

        private void ProcessDeferredUpdates() {
            var now = EditorApplication.timeSinceStartup;

            // Type refresh (immediate, no debounce)
            if (_needsTypeRefresh) {
                _needsTypeRefresh = false;
                RefreshTypes();
            }

            // Filter debounce
            if (_typeFilter != _lastAppliedFilter) {
                if (_filterDebounceTime == 0) {
                    _filterDebounceTime = now + FilterDebounceDelay;
                    Repaint();  // Ensure we get called again
                }

                if (now >= _filterDebounceTime) {
                    _filterDebounceTime = 0;
                    ApplyTypeFilter();
                }
            }

            // Preview debounce
            if (_needsPreviewUpdate && _autoRefreshPreview) {
                if (_previewDebounceTime == 0) {
                    _previewDebounceTime = now + PreviewDebounceDelay;
                    Repaint();
                }

                if (now >= _previewDebounceTime) {
                    _previewDebounceTime = 0;
                    _needsPreviewUpdate = false;
                    UpdatePreview();
                }
            }

            // Request repaint if we're waiting for debounce
            if (_filterDebounceTime > 0 || _previewDebounceTime > 0) {
                Repaint();
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
                // Filter changed - will be applied inline
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
                _needsTypeRefresh = true;
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
                    _needsTypeRefresh = true;
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"Selected: {_selectedAssemblies.Count}", EditorStyles.miniLabel);
        }

        private void DrawTypePanel() {
            EditorGUILayout.LabelField("Types", EditorStyles.boldLabel);

            // Search field with debouncing
            var searchRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            var newFilter = _typeSearchField.OnGUI(searchRect, _typeFilter);
            if (newFilter != _typeFilter) {
                _typeFilter = newFilter;
                // Will be applied after debounce
            }

            // Quick select buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft)) {
                _typeTreeView?.SelectAll();
            }
            if (GUILayout.Button("None", EditorStyles.miniButtonMid)) {
                _typeTreeView?.SelectNone();
            }
            if (GUILayout.Button("Classes", EditorStyles.miniButtonRight)) {
                _typeTreeView?.SelectByPredicate(e => e.Kind == TypeKind.Class && !e.Type.IsAbstract);
            }
            EditorGUILayout.EndHorizontal();

            // TreeView
            EnsureTreeViewInitialized();

            var treeRect = GUILayoutUtility.GetRect(0, 10000, 0, 10000);
            _typeTreeView?.OnGUI(treeRect);

            // Stats
            var selectedCount = _typeTreeView?.GetSelectedTypes().Count ?? 0;
            EditorGUILayout.LabelField($"Selected: {selectedCount} / {_filteredTypes.Count}", EditorStyles.miniLabel);
        }

        private void EnsureTreeViewInitialized() {
            if (_typeTreeView == null) {
                _typeTreeView = new TypeTreeView(_typeTreeViewState);
                _typeTreeView.OnSelectionChanged += () => {
                    _needsPreviewUpdate = true;
                    _previewDebounceTime = 0;  // Reset debounce timer
                };
                UpdateTreeViewData();
            }
        }

        private void UpdateTreeViewData() {
            if (_typeTreeView == null) return;

            var selectedTypes = _typeTreeView.GetSelectedTypes();
            _typeTreeView.SetData(_filteredTypes, selectedTypes);
        }

        private void ApplyTypeFilter() {
            _lastAppliedFilter = _typeFilter;

            if (string.IsNullOrEmpty(_typeFilter)) {
                _filteredTypes = new List<TypeEntry>(_allTypes);
            } else {
                var filterLower = _typeFilter.ToLowerInvariant();
                _filteredTypes = _allTypes
                    .Where(e => e.DisplayNameLower.Contains(filterLower))
                    .ToList();
            }

            UpdateTreeViewData();
        }

        private void DrawPreviewPanel() {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.TextArea(_previewContent, s_monoStyle ?? EditorStyles.textArea,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

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

            var selectedCount = _typeTreeView?.GetSelectedTypes().Count ?? 0;
            GUI.enabled = selectedCount > 0;
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

            _needsTypeRefresh = true;
        }

        private void RefreshTypes() {
            TypeEntry.ResetIdCounter();
            _allTypes.Clear();
            var failedAssemblies = new List<string>();

            foreach (var asmName in _selectedAssemblies) {
                var asmEntry = _assemblies.FirstOrDefault(a => a.Name == asmName);
                if (asmEntry == null) continue;

                try {
                    var types = asmEntry.Assembly.GetTypes()
                        .Where(t => t.IsPublic)
                        .Where(t => !TypeMapper.ShouldSkipType(t))
                        .OrderBy(t => t.FullName);

                    foreach (var type in types) {
                        _allTypes.Add(TypeEntry.Create(type));
                    }
                } catch (ReflectionTypeLoadException ex) {
                    failedAssemblies.Add($"{asmName} (partial load)");
                    // Load what we can
                    foreach (var type in ex.Types.Where(t => t != null)) {
                        if (type.IsPublic && !TypeMapper.ShouldSkipType(type)) {
                            _allTypes.Add(TypeEntry.Create(type));
                        }
                    }
                } catch (Exception ex) {
                    failedAssemblies.Add($"{asmName}: {ex.Message}");
                }
            }

            // Log failed assemblies
            if (failedAssemblies.Count > 0) {
                Debug.LogWarning($"[TypeGenerator] Some assemblies had loading issues:\n• {string.Join("\n• ", failedAssemblies)}");
            }

            // Apply current filter to new types
            ApplyTypeFilter();

            _needsPreviewUpdate = true;
        }

        private void UpdatePreview() {
            var selectedTypes = _typeTreeView?.GetSelectedTypes();
            if (selectedTypes == null || selectedTypes.Count == 0) {
                _previewContent = "// Select types to preview the generated TypeScript declarations";
                return;
            }

            try {
                var analyzer = new TypeAnalyzer(new AnalyzerOptions {
                    IncludeObsolete = _includeObsolete
                });

                var typeInfos = analyzer.AnalyzeTypes(selectedTypes);

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
            var selectedTypes = _typeTreeView?.GetSelectedTypes();
            if (selectedTypes == null || selectedTypes.Count == 0) {
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

                var typeInfos = analyzer.AnalyzeTypes(selectedTypes);

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
            _needsTypeRefresh = true;
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
    }
}
