using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// An actor on another's joint: a hat on a head, a tool in a hand. The attached actor is made
    /// a child of the joint, so Unity carries it with the pose and nothing runs each frame. Its
    /// place, turn and scale are then measured from the joint (Move, SetRotation and PlaceMany
    /// set them from the parent). It is destroyed with what it is attached to.
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>A loaded model's joints by name, one a line: the bones its skins are bound to, in the file's order.</summary>
        public static string Joints(int model) {
            if (!_models.TryGetValue(model, out var m) || m.template == null) return "";
            var names = new List<string>();
            foreach (var skin in m.template.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                foreach (var bone in skin.bones) if (bone != null && !names.Contains(bone.name)) names.Add(bone.name);
            }
            return string.Join("\n", names);
        }

        /// <summary>
        /// Attaches `actor` to the joint of `to` named `joint`, at a place, a turn (degrees) and a
        /// scale measured from the joint. "" when attached, or what was wrong.
        /// </summary>
        public static string Attach(int actor, int to, string joint, float x, float y, float z, float yaw, float pitch, float roll, float scale) {
            if (!_actors.TryGetValue(actor, out var a) || a.go == null) return "the actor to attach is gone";
            if (!_actors.TryGetValue(to, out var b) || b.go == null) return "the actor to attach it to is gone";
            if (actor == to) return "an actor cannot be attached to itself";
            for (int up = to; up != 0; up = _actors.TryGetValue(up, out var above) ? above.attachedTo : 0) {
                if (up == actor) return "the actor it would be attached to is attached to it";
            }
            Transform bone = null;
            foreach (var r in b.renderers) {
                if (r is not SkinnedMeshRenderer skin) continue;
                foreach (var candidate in skin.bones) {
                    if (candidate != null && candidate.name == joint) { bone = candidate; break; }
                }
                if (bone != null) break;
            }
            if (bone == null) return $"it has no joint named {joint}";
            Unhang(actor, a);
            a.attachedTo = to;
            (b.attached ??= new List<int>()).Add(actor);
            a.go.transform.SetParent(bone, false);
            a.go.transform.SetLocalPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(pitch, yaw, roll));
            a.go.transform.localScale = Vector3.one * scale;
            return "";
        }

        /// <summary>
        /// Takes an actor off the joint it is attached to and leaves it where it was in the world.
        /// Returns where that is, since its place is measured from the world again: x, y, z, its
        /// turn as a quaternion, and its scale. Null for an actor that was not attached.
        /// </summary>
        public static float[] Detach(int actor) {
            if (!_actors.TryGetValue(actor, out var a) || a.go == null || a.attachedTo == 0) return null;
            Unhang(actor, a);
            var t = a.go.transform;
            t.SetParent(Root, true);
            // A joint a clip has scaled unevenly leaves a shear a transform cannot hold; its x is the answer then.
            t.localScale = Vector3.one * t.localScale.x;
            var p = t.localPosition;
            var q = t.localRotation;
            return new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, t.localScale.x };
        }

        /// <summary>The actor an actor is attached to, 0 for none.</summary>
        internal static int AttachedTo(int actor) => _actors.TryGetValue(actor, out var a) ? a.attachedTo : 0;

        // Forgets that `a` hangs on another actor; its transform is the caller's to move.
        static void Unhang(int actor, Actor a) {
            if (a.attachedTo == 0) return;
            if (_actors.TryGetValue(a.attachedTo, out var on)) on.attached?.Remove(actor);
            a.attachedTo = 0;
        }
    }
}
