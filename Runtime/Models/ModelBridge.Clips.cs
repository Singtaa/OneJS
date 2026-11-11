using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Models {
    /// <summary>
    /// One set of clips for many bodies: a loaded model plays another's clips, so the clips are
    /// held once however many files share a skeleton. A legacy clip moves transforms by their
    /// path from the object its Animation sits on, so the taker needs each of the giver's joints
    /// at the same path from its own.
    /// </summary>
    public static partial class ModelBridge {
        /// <summary>
        /// Gives `model` the clips of `from`, beside any of its own (one of the same name is
        /// replaced), and measures its skins' bounds again over them. Before it has actors, since
        /// an actor is a copy of the model as it was. "" when taken, or what was wrong.
        /// </summary>
        public static string TakeClips(int model, int from) {
            if (!_models.TryGetValue(model, out var m) || m.template == null) return "there is no such model";
            if (model == from) return "a model already plays its own clips";
            if (!_models.TryGetValue(from, out var giver) || giver.template == null) return "the model whose clips to take is not loaded";
            if (m.built != null || giver.built != null) return "a mesh model has no joints for a clip to move";
            foreach (var a in _actors.Values) if (a.model == model) return "this model already has actors, which were made without the clips";
            var theirs = giver.template.GetComponentInChildren<Animation>(true);
            if (theirs == null || theirs.GetClipCount() == 0) return "the model whose clips to take has no clips";

            var mine = m.template.GetComponentInChildren<Animation>(true);
            var root = mine != null ? mine.transform : Matching(m.template.transform, giver.template.transform, theirs.transform);
            if (root == null) return "this model's file is laid out differently from the one whose clips to take";
            foreach (var skin in giver.template.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                foreach (var bone in skin.bones) {
                    if (bone == null) continue;
                    var path = PathFrom(theirs.transform, bone);
                    if (path == null || path == "" || root.Find(path) != null) continue;
                    return $"this model has no joint {bone.name} at {path}, which the clips move";
                }
            }

            if (mine == null) {
                mine = root.gameObject.AddComponent<Animation>();
                mine.playAutomatically = false;
            }
            foreach (AnimationState state in theirs) {
                if (state.clip == null) continue;
                if (mine.GetClip(state.name) != null) mine.RemoveClip(state.name);
                mine.AddClip(state.clip, state.name);
            }
            var names = new List<string>();
            foreach (AnimationState state in mine) names.Add(state.name);
            m.clips = names.ToArray();
            m.clipsFrom = from;
            PrepareSkins(m.template, mine);
            return "";
        }

        // `t`'s path from `root` as Transform.Find takes it, "" for the root itself, null when it is not under it.
        static string PathFrom(Transform root, Transform t) {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            if (t == null) return null;
            parts.Reverse();
            return string.Join("/", parts);
        }

        // The object under `mine` where `at` is under `theirs`: by its path, or the only child where the files name their scenes differently.
        static Transform Matching(Transform mine, Transform theirs, Transform at) {
            var path = PathFrom(theirs, at);
            if (path == null) return null;
            if (path == "") return mine;
            var found = mine.Find(path);
            if (found != null) return found;
            return at.parent == theirs && mine.childCount == 1 ? mine.GetChild(0) : null;
        }
    }
}
