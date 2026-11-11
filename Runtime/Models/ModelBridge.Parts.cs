using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// A model's parts, shown and hidden an actor at a time: a part is a node of the file that
    /// carries a mesh, by the node's name. Hiding one switches its renderer off, so it is not
    /// drawn, casts no shadow and, where it is skinned, is not deformed.
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>A loaded model's parts by name, one a line, in the file's order.</summary>
        public static string Parts(int model) {
            if (!_models.TryGetValue(model, out var m) || m.template == null) return "";
            var names = new List<string>();
            foreach (var r in m.template.GetComponentsInChildren<Renderer>(true)) {
                if (!names.Contains(r.gameObject.name)) names.Add(r.gameObject.name);
            }
            return string.Join("\n", names);
        }

        /// <summary>Shows or hides the actor's part of that name. False when it has none.</summary>
        public static bool ShowPart(int actor, string part, bool show) {
            if (!_actors.TryGetValue(actor, out var a)) return false;
            bool found = false;
            // Its own renderers, as it was spawned: what is attached to it has parts of its own.
            foreach (var r in a.renderers) {
                if (r == null || r.gameObject.name != part) continue;
                r.enabled = show;
                found = true;
            }
            return found;
        }
    }
}
