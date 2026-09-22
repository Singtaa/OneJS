using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace OneJS.Input {
    /// <summary>
    /// Gives the EventSystem JSRunner creates an InputSystemUIInputModule. Lives in this
    /// assembly, not beside JSRunner, so OneJS.Runtime never references the Input System
    /// package and a project without it compiles none of this.
    /// </summary>
    static class InputSystemUIModule {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() {
            JSRunner.AddInputSystemModule = Add;
        }

        static void Add(GameObject go) {
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            // Keep the panel selected when clicking non-focusable areas (most of a OneJS
            // layout is non-focusable Views). The default (true) deselects on such clicks,
            // which drops the navigation anchor and breaks keyboard/gamepad nav continuity.
            module.deselectOnBackgroundClick = false;
            // The default Submit action binds Enter + gamepad only; add Space so focused
            // controls (toggles, radios, buttons) activate with Space too (the convention).
            var submit = module.submit != null ? module.submit.action : null;
            if (submit != null) {
                bool wasEnabled = submit.enabled;
                if (wasEnabled) submit.Disable();
                submit.AddBinding("<Keyboard>/space");
                if (wasEnabled) submit.Enable();
            }
        }
    }
}
