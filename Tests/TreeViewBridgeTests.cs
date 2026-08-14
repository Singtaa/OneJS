using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// Tests for TreeViewBridge, the non-generic wrapper around
    /// BaseTreeView.SetRootItems&lt;T&gt;. The parallel-array contract
    /// (pre-order, parentId -1 = root) is produced by onejs-react's
    /// treeview.ts; the shape fixtures here mirror treeview.test.ts.
    /// </summary>
    [TestFixture]
    public class TreeViewBridgeTests {
        static DefaultTreeViewController<int> Controller(TreeView tv) =>
            (DefaultTreeViewController<int>)tv.viewController;

        static int[] ChildIds(TreeView tv, int id) =>
            Controller(tv).GetTreeViewItemDataForId(id).children.Select(c => c.id).ToArray();

        [Test]
        public void SetRootItems_BuildsNestedStructure() {
            var tv = new TreeView();
            // 1            10
            // ├─ 2         (root)
            // └─ 3
            //    └─ 4
            TreeViewBridge.SetRootItems(tv,
                new[] { 1, 2, 3, 4, 10 },
                new[] { -1, 1, 1, 3, -1 });

            Assert.AreEqual(5, tv.GetTreeCount());
            CollectionAssert.AreEqual(new[] { 1, 10 }, tv.GetRootIds().ToArray());
            CollectionAssert.AreEqual(new[] { 2, 3 }, ChildIds(tv, 1));
            CollectionAssert.AreEqual(new[] { 4 }, ChildIds(tv, 3));
            Assert.IsEmpty(ChildIds(tv, 10));
        }

        [Test]
        public void SetRootItems_PreservesSiblingOrder() {
            var tv = new TreeView();
            TreeViewBridge.SetRootItems(tv,
                new[] { 7, 3, 9, 5, 1 },
                new[] { -1, 7, 7, 7, 7 });

            CollectionAssert.AreEqual(new[] { 3, 9, 5, 1 }, ChildIds(tv, 7));
        }

        [Test]
        public void SetRootItems_FlatListIsAllRoots() {
            var tv = new TreeView();
            TreeViewBridge.SetRootItems(tv,
                new[] { 4, 2, 8 },
                new[] { -1, -1, -1 });

            CollectionAssert.AreEqual(new[] { 4, 2, 8 }, tv.GetRootIds().ToArray());
            Assert.AreEqual(3, tv.GetTreeCount());
        }

        [Test]
        public void SetRootItems_EmptyArraysClearTree() {
            var tv = new TreeView();
            TreeViewBridge.SetRootItems(tv, new[] { 1, 2 }, new[] { -1, 1 });
            Assert.AreEqual(2, tv.GetTreeCount());

            TreeViewBridge.SetRootItems(tv, new int[0], new int[0]);
            Assert.AreEqual(0, tv.GetTreeCount());
        }

        [Test]
        public void SetRootItems_RejectsDuplicateIds() {
            var tv = new TreeView();
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("Duplicate item id"));
            TreeViewBridge.SetRootItems(tv, new[] { 1, 1 }, new[] { -1, -1 });
        }

        [Test]
        public void SetRootItems_RejectsParentAppearingAfterChild() {
            var tv = new TreeView();
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("pre-order"));
            TreeViewBridge.SetRootItems(tv, new[] { 2, 1 }, new[] { 1, -1 });
        }

        [Test]
        public void SetRootItems_RejectsMismatchedLengths() {
            var tv = new TreeView();
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("parallel arrays"));
            TreeViewBridge.SetRootItems(tv, new[] { 1, 2 }, new[] { -1 });
        }
    }
}
