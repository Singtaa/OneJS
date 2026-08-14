using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Data plumbing for UI Toolkit tree views. BaseTreeView's data entry point
    /// is the generic SetRootItems&lt;T&gt;, and the CS proxy does not support
    /// generic methods, so this wraps it in a plain method JS can call.
    ///
    /// The tree crosses as two parallel int arrays in pre-order (parents before
    /// children, siblings in display order); parentIds[i] is the parent of
    /// ids[i], with -1 marking a root. Item data itself never crosses: items are
    /// TreeViewItemData&lt;int&gt; whose payload is the id, and the JS wrapper
    /// resolves id -> data on its own side when binding rows. That keeps this a
    /// single structure-only crossing per data set, in the same spirit as
    /// StyleBridge and PainterBridge.
    ///
    /// Called from JS via:
    ///   CS.OneJS.TreeViewBridge.SetRootItems(treeView, ids, parentIds)
    /// The array contract is produced by onejs-react's treeview.ts; keep the two
    /// in sync.
    /// </summary>
    public static class TreeViewBridge {
        public static void SetRootItems(BaseTreeView treeView, int[] ids, int[] parentIds) {
            if (treeView == null) {
                Debug.LogError("[TreeViewBridge] SetRootItems called with a null TreeView");
                return;
            }
            if (ids == null || parentIds == null || ids.Length != parentIds.Length) {
                Debug.LogError($"[TreeViewBridge] ids and parentIds must be parallel arrays " +
                    $"(got {ids?.Length.ToString() ?? "null"} ids, {parentIds?.Length.ToString() ?? "null"} parents)");
                return;
            }

            int n = ids.Length;

            // Validate up front and fail loudly: TreeView requires unique ids, and
            // reverse-order construction below relies on pre-order input.
            var seen = new HashSet<int>();
            for (int i = 0; i < n; i++) {
                if (!seen.Add(ids[i])) {
                    Debug.LogError($"[TreeViewBridge] Duplicate item id {ids[i]} at index {i}");
                    return;
                }
                if (parentIds[i] != -1 && !seen.Contains(parentIds[i])) {
                    Debug.LogError($"[TreeViewBridge] Item {ids[i]} at index {i} references parent " +
                        $"{parentIds[i]}, which does not appear before it (input must be pre-order)");
                    return;
                }
            }

            // Pre-order input means every node's descendants come after it, so
            // walking backwards constructs children before their parent needs
            // them. Sibling lists accumulate reversed and are flipped on use.
            var childLists = new Dictionary<int, List<TreeViewItemData<int>>>();
            var roots = new List<TreeViewItemData<int>>();
            for (int i = n - 1; i >= 0; i--) {
                List<TreeViewItemData<int>> children = null;
                if (childLists.TryGetValue(ids[i], out var accumulated)) {
                    accumulated.Reverse();
                    children = accumulated;
                }
                var item = new TreeViewItemData<int>(ids[i], ids[i], children);
                if (parentIds[i] == -1) {
                    roots.Add(item);
                } else {
                    if (!childLists.TryGetValue(parentIds[i], out var siblings)) {
                        siblings = new List<TreeViewItemData<int>>();
                        childLists[parentIds[i]] = siblings;
                    }
                    siblings.Add(item);
                }
            }
            roots.Reverse();

            treeView.SetRootItems(roots);
            treeView.RefreshItems();
        }

        /// <summary>
        /// Selected item ids, resolved from the view's selected indices. The
        /// selection APIs expose IEnumerable&lt;int&gt;, which JS cannot iterate
        /// through the proxy; a plain int[] marshals cleanly.
        /// </summary>
        public static int[] GetSelectedIds(BaseTreeView treeView) {
            if (treeView == null) return new int[0];
            var result = new List<int>();
            foreach (var index in treeView.selectedIndices) {
                result.Add(treeView.GetIdForIndex(index));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Selected indices as a plain int[], for the same reason as
        /// GetSelectedIds. Takes the shared base type so ListView's selection
        /// events can use it too.
        /// </summary>
        public static int[] GetSelectedIndices(BaseVerticalCollectionView view) {
            if (view == null) return new int[0];
            var result = new List<int>();
            foreach (var index in view.selectedIndices) {
                result.Add(index);
            }
            return result.ToArray();
        }
    }
}
