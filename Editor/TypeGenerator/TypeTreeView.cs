using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Virtualized TreeView for displaying and selecting types efficiently.
    /// Only renders visible rows, enabling smooth scrolling with 10,000+ types.
    /// </summary>
    internal class TypeTreeView : TreeView<int> {
        public event Action OnSelectionChanged;

        private List<TypeEntry> _entries = new();
        private HashSet<Type> _selectedTypes = new();
        private Dictionary<int, TypeEntry> _idToEntry = new();

        // Cached icons (static to share across instances)
        private static GUIContent s_classIcon;
        private static GUIContent s_interfaceIcon;
        private static GUIContent s_structIcon;
        private static GUIContent s_enumIcon;
        private static GUIContent s_delegateIcon;
        private static bool s_iconsInitialized;

        public TypeTreeView(TreeViewState<int> state) : base(state) {
            InitializeIcons();
            showBorder = true;
            showAlternatingRowBackgrounds = true;
            rowHeight = 18;
            Reload();
        }

        private static void InitializeIcons() {
            if (s_iconsInitialized) return;
            s_iconsInitialized = true;

            s_classIcon = EditorGUIUtility.IconContent("d_cs Script Icon");
            s_interfaceIcon = EditorGUIUtility.IconContent("d_cs Script Icon");  // Same icon, could customize
            s_structIcon = EditorGUIUtility.IconContent("d_PreMatCube");
            s_enumIcon = EditorGUIUtility.IconContent("d_FilterByType");
            s_delegateIcon = EditorGUIUtility.IconContent("d_cs Script Icon");
        }

        public void SetData(List<TypeEntry> entries, HashSet<Type> selectedTypes) {
            _entries = entries ?? new List<TypeEntry>();
            _selectedTypes = selectedTypes ?? new HashSet<Type>();

            // Build ID lookup
            _idToEntry.Clear();
            foreach (var entry in _entries) {
                _idToEntry[entry.Id] = entry;
            }

            Reload();
        }

        public HashSet<Type> GetSelectedTypes() => _selectedTypes;

        public void SelectAll() {
            foreach (var entry in _entries) {
                _selectedTypes.Add(entry.Type);
            }
            Reload();
            OnSelectionChanged?.Invoke();
        }

        public void SelectNone() {
            _selectedTypes.Clear();
            Reload();
            OnSelectionChanged?.Invoke();
        }

        public void SelectByPredicate(Func<TypeEntry, bool> predicate) {
            foreach (var entry in _entries) {
                if (predicate(entry)) {
                    _selectedTypes.Add(entry.Type);
                }
            }
            Reload();
            OnSelectionChanged?.Invoke();
        }

        protected override TreeViewItem<int> BuildRoot() {
            // Root is invisible, depth -1
            return new TreeViewItem<int>(-1, -1, "Root");
        }

        protected override IList<TreeViewItem<int>> BuildRows(TreeViewItem<int> root) {
            var rows = GetRows() ?? new List<TreeViewItem<int>>();
            rows.Clear();

            foreach (var entry in _entries) {
                var item = new TypeTreeViewItem(entry.Id, 0, entry.DisplayName, entry);
                rows.Add(item);
            }

            SetupParentsAndChildrenFromDepths(root, rows);
            return rows;
        }

        protected override void RowGUI(RowGUIArgs args) {
            var item = args.item as TypeTreeViewItem;
            if (item == null) {
                base.RowGUI(args);
                return;
            }

            var entry = item.Entry;
            var rect = args.rowRect;
            var indent = GetContentIndent(item);

            // Checkbox
            var checkRect = new Rect(rect.x + indent, rect.y, 18, rect.height);
            EditorGUI.BeginChangeCheck();
            var isSelected = _selectedTypes.Contains(entry.Type);
            var newSelected = EditorGUI.Toggle(checkRect, isSelected);
            if (EditorGUI.EndChangeCheck()) {
                if (newSelected) {
                    _selectedTypes.Add(entry.Type);
                } else {
                    _selectedTypes.Remove(entry.Type);
                }
                OnSelectionChanged?.Invoke();
            }

            // Icon
            var iconRect = new Rect(checkRect.xMax + 2, rect.y, 16, rect.height);
            var icon = GetIconForKind(entry.Kind);
            if (icon != null && icon.image != null) {
                GUI.DrawTexture(iconRect, icon.image, ScaleMode.ScaleToFit);
            }

            // Label
            var labelRect = new Rect(iconRect.xMax + 4, rect.y, rect.xMax - iconRect.xMax - 4, rect.height);
            GUI.Label(labelRect, entry.DisplayName);
        }

        private GUIContent GetIconForKind(TypeKind kind) {
            return kind switch {
                TypeKind.Interface => s_interfaceIcon,
                TypeKind.Struct => s_structIcon,
                TypeKind.Enum => s_enumIcon,
                TypeKind.Delegate => s_delegateIcon,
                _ => s_classIcon
            };
        }

        protected override bool DoesItemMatchSearch(TreeViewItem<int> item, string search) {
            if (string.IsNullOrEmpty(search)) return true;

            var treeItem = item as TypeTreeViewItem;
            if (treeItem == null) return false;

            // Use pre-lowercased name for fast comparison
            return treeItem.Entry.DisplayNameLower.Contains(search.ToLowerInvariant());
        }

        protected override void SelectionChanged(IList<int> selectedIds) {
            // TreeView row selection (not checkbox selection)
            // We use this for potential future features like context menus
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => true;
    }

    /// <summary>
    /// TreeViewItem wrapper that holds the TypeEntry data
    /// </summary>
    internal class TypeTreeViewItem : TreeViewItem<int> {
        public TypeEntry Entry { get; }

        public TypeTreeViewItem(int id, int depth, string displayName, TypeEntry entry)
            : base(id, depth, displayName) {
            Entry = entry;
        }
    }

    /// <summary>
    /// Type category for icon selection
    /// </summary>
    internal enum TypeKind {
        Class,
        Interface,
        Struct,
        Enum,
        Delegate
    }

    /// <summary>
    /// Type entry with pre-computed data for efficient display and filtering
    /// </summary>
    internal class TypeEntry {
        public Type Type;
        public string DisplayName;
        public string DisplayNameLower;  // Pre-computed for filtering
        public TypeKind Kind;
        public int Id;  // Unique ID for TreeView

        private static int s_nextId = 1;

        public static TypeEntry Create(Type type) {
            var displayName = type.FullName ?? type.Name;
            return new TypeEntry {
                Type = type,
                DisplayName = displayName,
                DisplayNameLower = displayName.ToLowerInvariant(),
                Kind = GetTypeKind(type),
                Id = s_nextId++
            };
        }

        public static void ResetIdCounter() {
            s_nextId = 1;
        }

        private static TypeKind GetTypeKind(Type type) {
            if (type.IsInterface) return TypeKind.Interface;
            if (type.IsEnum) return TypeKind.Enum;
            if (type.IsValueType) return TypeKind.Struct;
            if (typeof(Delegate).IsAssignableFrom(type)) return TypeKind.Delegate;
            return TypeKind.Class;
        }
    }
}
