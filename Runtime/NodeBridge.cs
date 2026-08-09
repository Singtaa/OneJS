using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Zero-alloc tree wiring. The reconciler attaches and detaches elements
    /// constantly; routing each through VisualElement.Add/Insert/RemoveFromHierarchy
    /// on the CS proxy means a reflection dispatch (object[] + invoke) per call.
    ///
    /// Those instance methods can't be FastPath-seeded directly because the fast
    /// path keys by the concrete element type (Button, Label, ScrollView, ...) while
    /// the methods live on the VisualElement base: one registration per concrete
    /// type would be needed. These static wrappers take element handles (ints) and
    /// are registered in the zero-alloc fast path by type name (like GPUBridge), so
    /// they are reflection-free and type-agnostic across every element type.
    ///
    /// Called from JS via:
    ///   CS.OneJS.NodeBridge.Add(parentHandle, childHandle)
    ///
    /// Unresolvable handles are reported rather than swallowed. Attach and detach
    /// are deliberately asymmetric:
    ///
    /// - Add/Insert must be loud. The proxy path these replaced threw on an
    ///   unresolvable target; a silent no-op instead leaves an element constructed,
    ///   styled and event-wired but never parented, so a subtree just stops painting
    ///   with nothing in the console to explain it.
    /// - RemoveFromHierarchy tolerates a handle that is already gone: the reconciler
    ///   relies on detach being a safe no-op when the root was cleared before React
    ///   tore the tree down (hot reload). A handle that resolves to a non-element is
    ///   still an error: that means handle reuse, not an already-detached element.
    /// </summary>
    public static class NodeBridge {
        public static void Add(int parentHandle, int childHandle) {
            var parent = Resolve(parentHandle, nameof(Add), "parent", reportMissing: true);
            var child = Resolve(childHandle, nameof(Add), "child", reportMissing: true);
            if (parent == null || child == null) return;
            parent.Add(child);
        }

        public static void Insert(int parentHandle, int index, int childHandle) {
            var parent = Resolve(parentHandle, nameof(Insert), "parent", reportMissing: true);
            var child = Resolve(childHandle, nameof(Insert), "child", reportMissing: true);
            if (parent == null || child == null) return;
            parent.Insert(index, child);
        }

        public static void RemoveFromHierarchy(int childHandle) {
            var child = Resolve(childHandle, nameof(RemoveFromHierarchy), "child", reportMissing: false);
            if (child == null) return;
            child.RemoveFromHierarchy();
        }

        /// <summary>
        /// Resolve a handle to a VisualElement, reporting why it failed. Nothing is
        /// allocated on the success path: the message is only built on failure.
        /// </summary>
        static VisualElement Resolve(int handle, string op, string role, bool reportMissing) {
            var obj = QuickJSNative.GetObjectByHandle(handle);
            if (obj is VisualElement el) return el;

            if (obj == null) {
                if (reportMissing) {
                    Debug.LogError(
                        $"[OneJS] NodeBridge.{op}: {role} handle {handle} is not in the handle table " +
                        "(already released, or stale across a reload). The element was not attached.");
                }
            } else {
                Debug.LogError(
                    $"[OneJS] NodeBridge.{op}: {role} handle {handle} resolved to " +
                    $"{obj.GetType().FullName}, not a VisualElement. Handle reuse or a stale handle.");
            }
            return null;
        }
    }
}
