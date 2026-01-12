using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Utils
{
    /// <summary>
    /// Runtime UI Toolkit debugger utility for dumping the visual tree structure.
    /// Useful for debugging USS selector issues.
    /// </summary>
    public static class UIDebugger
    {
        /// <summary>
        /// Dumps all UI elements in all UIDocuments (parameterless for MCP).
        /// </summary>
        public static string Dump()
        {
            return DumpAll(5, true);
        }

        /// <summary>
        /// Dumps with deeper traversal (parameterless for MCP).
        /// </summary>
        public static string DumpDeep()
        {
            return DumpAll(15, true);
        }

        /// <summary>
        /// Dumps all UI elements in all UIDocuments in the scene.
        /// </summary>
        /// <param name="maxDepth">Maximum depth to traverse (default: 5)</param>
        /// <param name="includeStyles">Include computed style values (default: true)</param>
        public static string DumpAll(int maxDepth, bool includeStyles)
        {
            var sb = new StringBuilder();
            var uiDocs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

            sb.AppendLine($"Found {uiDocs.Length} UIDocument(s) in scene:");
            sb.AppendLine();

            foreach (var doc in uiDocs)
            {
                if (doc.rootVisualElement == null) continue;

                sb.AppendLine($"=== UIDocument: {doc.gameObject.name} ===");
                sb.AppendLine();
                sb.Append(DumpTree(doc.rootVisualElement, maxDepth, includeStyles));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Finds elements by type name across all UIDocuments.
        /// Can be called via MCP reflection.
        /// </summary>
        /// <param name="typeName">Element type name (e.g., "Button", "TextField")</param>
        public static string FindAll(string typeName)
        {
            var sb = new StringBuilder();
            var uiDocs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

            foreach (var doc in uiDocs)
            {
                if (doc.rootVisualElement == null) continue;
                sb.Append(FindByType(doc.rootVisualElement, typeName));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Finds elements by USS class name across all UIDocuments.
        /// Can be called via MCP reflection.
        /// </summary>
        /// <param name="className">USS class name to search for</param>
        public static string FindAllByClass(string className)
        {
            var sb = new StringBuilder();
            var uiDocs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);

            foreach (var doc in uiDocs)
            {
                if (doc.rootVisualElement == null) continue;
                sb.Append(FindByClass(doc.rootVisualElement, className));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Dumps the visual tree starting from a given element.
        /// </summary>
        /// <param name="root">The root element to start from</param>
        /// <param name="maxDepth">Maximum depth to traverse (default: 10)</param>
        /// <param name="includeStyles">Include computed style values (default: false)</param>
        /// <returns>A string representation of the visual tree</returns>
        public static string DumpTree(VisualElement root, int maxDepth = 10, bool includeStyles = false)
        {
            if (root == null) return "null";

            var sb = new StringBuilder();
            DumpElement(sb, root, 0, maxDepth, includeStyles);
            return sb.ToString();
        }

        /// <summary>
        /// Dumps a single element and its children.
        /// </summary>
        private static void DumpElement(StringBuilder sb, VisualElement element, int depth, int maxDepth, bool includeStyles)
        {
            if (depth > maxDepth) return;

            var indent = new string(' ', depth * 2);
            var typeName = element.GetType().Name;
            var name = string.IsNullOrEmpty(element.name) ? "" : $" name=\"{element.name}\"";

            // Get USS classes
            var classes = new List<string>();
            foreach (var cls in element.GetClasses())
            {
                classes.Add(cls);
            }
            var classAttr = classes.Count > 0 ? $" class=\"{string.Join(" ", classes)}\"" : "";

            // Start element
            sb.AppendLine($"{indent}<{typeName}{name}{classAttr}>");

            // Include style information if requested
            if (includeStyles)
            {
                DumpStyles(sb, element, depth + 1);
            }

            // Recurse children
            foreach (var child in element.Children())
            {
                DumpElement(sb, child, depth + 1, maxDepth, includeStyles);
            }

            sb.AppendLine($"{indent}</{typeName}>");
        }

        /// <summary>
        /// Dumps key computed styles for an element.
        /// </summary>
        private static void DumpStyles(StringBuilder sb, VisualElement element, int depth)
        {
            var indent = new string(' ', depth * 2);
            var style = element.resolvedStyle;

            sb.AppendLine($"{indent}<!-- Computed Styles:");
            sb.AppendLine($"{indent}  backgroundColor: {FormatColor(style.backgroundColor)}");
            sb.AppendLine($"{indent}  borderTopColor: {FormatColor(style.borderTopColor)}");
            sb.AppendLine($"{indent}  color: {FormatColor(style.color)}");
            sb.AppendLine($"{indent}  display: {style.display}");
            sb.AppendLine($"{indent}  visibility: {style.visibility}");
            sb.AppendLine($"{indent}-->");
        }

        private static string FormatColor(Color c)
        {
            return $"rgba({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
        }

        /// <summary>
        /// Finds elements by USS class name and dumps their info.
        /// </summary>
        /// <param name="root">The root element to search from</param>
        /// <param name="className">USS class name to search for</param>
        /// <returns>Info about matching elements</returns>
        public static string FindByClass(VisualElement root, string className)
        {
            if (root == null) return "null";

            var sb = new StringBuilder();
            var matches = root.Query(className: className).ToList();

            sb.AppendLine($"Found {matches.Count} elements with class '{className}':");
            sb.AppendLine();

            foreach (var element in matches)
            {
                DumpElementInfo(sb, element);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Finds elements by type and dumps their info.
        /// </summary>
        /// <param name="root">The root element to search from</param>
        /// <param name="typeName">Element type name (e.g., "Button", "TextField")</param>
        /// <returns>Info about matching elements</returns>
        public static string FindByType(VisualElement root, string typeName)
        {
            if (root == null) return "null";

            var sb = new StringBuilder();
            var allElements = new List<VisualElement>();
            CollectAllElements(root, allElements);

            var matches = allElements.FindAll(e => e.GetType().Name == typeName);

            sb.AppendLine($"Found {matches.Count} elements of type '{typeName}':");
            sb.AppendLine();

            foreach (var element in matches)
            {
                DumpElementInfo(sb, element);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Dumps detailed info for a single element including all USS classes and key styles.
        /// </summary>
        private static void DumpElementInfo(StringBuilder sb, VisualElement element)
        {
            var typeName = element.GetType().Name;
            var name = string.IsNullOrEmpty(element.name) ? "(no name)" : element.name;

            sb.AppendLine($"  Type: {typeName}");
            sb.AppendLine($"  Name: {name}");

            // USS classes
            var classes = new List<string>();
            foreach (var cls in element.GetClasses())
            {
                classes.Add(cls);
            }
            sb.AppendLine($"  Classes: [{string.Join(", ", classes)}]");

            // Pseudo states
            var pseudoStates = new List<string>();
            if (element.IsHover()) pseudoStates.Add(":hover");
            if (element.HasFocus()) pseudoStates.Add(":focus");
            // Note: Can't easily check :active or :checked without reflection
            sb.AppendLine($"  Pseudo States: [{string.Join(", ", pseudoStates)}]");

            // Key computed styles
            var style = element.resolvedStyle;
            sb.AppendLine($"  Styles:");
            sb.AppendLine($"    backgroundColor: {FormatColor(style.backgroundColor)}");
            sb.AppendLine($"    borderTopColor: {FormatColor(style.borderTopColor)}");
            sb.AppendLine($"    borderRightColor: {FormatColor(style.borderRightColor)}");
            sb.AppendLine($"    borderBottomColor: {FormatColor(style.borderBottomColor)}");
            sb.AppendLine($"    borderLeftColor: {FormatColor(style.borderLeftColor)}");
            sb.AppendLine($"    color: {FormatColor(style.color)}");
        }

        private static void CollectAllElements(VisualElement root, List<VisualElement> list)
        {
            list.Add(root);
            foreach (var child in root.Children())
            {
                CollectAllElements(child, list);
            }
        }

        /// <summary>
        /// Extension to check if element is being hovered.
        /// </summary>
        private static bool IsHover(this VisualElement element)
        {
            // Use reflection to check pseudo state flags
            try
            {
                var pseudoStates = element.GetType().GetProperty("pseudoStates",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pseudoStates != null)
                {
                    var value = (int)pseudoStates.GetValue(element);
                    return (value & 1) != 0; // Hover is typically bit 0
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Extension to check if element has focus.
        /// </summary>
        private static bool HasFocus(this VisualElement element)
        {
            return element.focusController?.focusedElement == element;
        }
    }
}
