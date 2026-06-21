using UnityEngine.UIElements;

namespace OneJS {
    /// <summary>
    /// Zero-alloc tree wiring. The reconciler attaches and detaches elements
    /// constantly; routing each through VisualElement.Add/Insert/RemoveFromHierarchy
    /// on the CS proxy means a reflection dispatch (object[] + invoke) per call.
    ///
    /// Those instance methods can't be FastPath-seeded directly because the fast
    /// path keys by the concrete element type (Button, Label, ScrollView, ...) while
    /// the methods live on the VisualElement base - one registration per concrete
    /// type would be needed. These static wrappers take element handles (ints) and
    /// are registered in the zero-alloc fast path by type name (like GPUBridge), so
    /// they are reflection-free and type-agnostic across every element type.
    ///
    /// Called from JS via:
    ///   CS.OneJS.NodeBridge.Add(parentHandle, childHandle)
    /// </summary>
    public static class NodeBridge {
        public static void Add(int parentHandle, int childHandle) {
            if (QuickJSNative.GetObjectByHandle(parentHandle) is VisualElement parent &&
                QuickJSNative.GetObjectByHandle(childHandle) is VisualElement child) {
                parent.Add(child);
            }
        }

        public static void Insert(int parentHandle, int index, int childHandle) {
            if (QuickJSNative.GetObjectByHandle(parentHandle) is VisualElement parent &&
                QuickJSNative.GetObjectByHandle(childHandle) is VisualElement child) {
                parent.Insert(index, child);
            }
        }

        public static void RemoveFromHierarchy(int childHandle) {
            if (QuickJSNative.GetObjectByHandle(childHandle) is VisualElement child) {
                child.RemoveFromHierarchy();
            }
        }
    }
}
