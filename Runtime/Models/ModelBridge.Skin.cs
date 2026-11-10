using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    // What a loaded model's skinned meshes cost while nobody sees them.
    //
    // glTFast makes every SkinnedMeshRenderer with updateWhenOffscreen on, and Unity then deforms
    // the mesh on the CPU, uploads its vertices and remeasures its bounds from the joints every
    // frame, drawn or not: 12 of the 16 ms a thousand people cost a frame were paid with all of
    // them behind the camera (PlaySite docs/engine-findings.md, "A skinned actor nobody sees").
    // So it is turned off, and since Unity then culls by the bounds the renderer was given rather
    // than ones it measures, the bounds are fixed here to hold every pose the model's clips reach.
    // The clips follow the same rule (BasedOnRenderers): unseen, the joints are left where they
    // were, and they are sampled again once any renderer under the actor is seen, by the camera
    // or by a light that casts its shadow. Unity also stops an unseen clip's time, which a player
    // would see as a villager who sat down out of view still sitting down a minute later, so the
    // clip an actor was last told to play has its time run here while the actor is unseen.
    public static partial class ModelBridge {
        // How often a clip is sampled for its reach, and the most samples one clip gets.
        const float ReachRate = 30;
        const int ReachSamples = 240;
        // Added on every side, as a share of the size: a crossfade passes through poses between
        // two clips, which no clip's own samples hold.
        const float ReachMargin = 0.1f;

        static readonly Vector3[] _corners = new Vector3[8];

        // The actors with a clip playing, and the frame their unseen clocks last ran in.
        static readonly HashSet<Actor> _clocked = new();
        static int _clockedFrame = -1;

        static void Clock(Actor a, AnimationState clip) {
            a.clip = clip;
            if (!Playing || !_clocked.Add(a) || _clocked.Count > 1) return;
            _clockedFrame = -1;
            RenderPipelineManager.beginContextRendering += ClocksFor;
            Camera.onPreCull += ClocksFor;
        }

        static void Unclock(Actor a) {
            if (_clocked.Remove(a) && _clocked.Count == 0) StopClocks();
        }

        // Only while an actor plays a clip, so a scene without one has no callback at all.
        static void StopClocks() {
            _clocked.Clear();
            RenderPipelineManager.beginContextRendering -= ClocksFor;
            Camera.onPreCull -= ClocksFor;
        }

        static void ClocksFor(ScriptableRenderContext _, List<Camera> __) => RunUnseenClocks();
        static void ClocksFor(Camera _) => RunUnseenClocks();

        // Once a frame, however many cameras draw it (see ProbesOnce). Whether a renderer is seen
        // is what the last frame's culling found, the same answer Unity's own clip culling uses.
        static void RunUnseenClocks() {
            if (Time.frameCount == _clockedFrame) return;
            _clockedFrame = Time.frameCount;
            float dt = Time.deltaTime;
            foreach (var a in _clocked) {
                if (a.go == null || !a.clip.enabled || Seen(a)) continue;
                a.clip.time += dt * a.clip.speed;
            }
        }

        static bool Seen(Actor a) {
            foreach (var r in a.renderers) if (r != null && r.isVisible) return true;
            return false;
        }

        /// <summary>
        /// Fixes the bounds of a template's skinned renderers to cover every clip's reach, and
        /// stops them and its clips updating while unseen. Called once, when the model loads.
        /// </summary>
        static void PrepareSkins(GameObject template, Animation anim) {
            var skins = template.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) return;
            var parts = new Bounds?[skins.Length][];
            var reach = new Bounds?[skins.Length];
            for (int i = 0; i < skins.Length; i++) parts[i] = JointBounds(skins[i]);

            void Grow() {
                for (int i = 0; i < skins.Length; i++) Reach(skins[i], parts[i], ref reach[i]);
            }

            Grow();
            if (anim != null) {
                var transforms = template.GetComponentsInChildren<Transform>(true);
                var rest = new (Vector3 position, Quaternion rotation, Vector3 scale)[transforms.Length];
                for (int i = 0; i < transforms.Length; i++) rest[i] = (transforms[i].localPosition, transforms[i].localRotation, transforms[i].localScale);
                foreach (AnimationState state in anim) {
                    var clip = state.clip;
                    if (clip == null) continue;
                    int steps = Mathf.Clamp(Mathf.CeilToInt(clip.length * ReachRate), 1, ReachSamples);
                    for (int k = 0; k <= steps; k++) {
                        clip.SampleAnimation(anim.gameObject, clip.length * k / steps);
                        Grow();
                    }
                }
                for (int i = 0; i < transforms.Length; i++) {
                    transforms[i].localPosition = rest[i].position;
                    transforms[i].localRotation = rest[i].rotation;
                    transforms[i].localScale = rest[i].scale;
                }
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
            }

            for (int i = 0; i < skins.Length; i++) {
                var skin = skins[i];
                var b = reach[i] ?? (skin.sharedMesh != null ? skin.sharedMesh.bounds : new Bounds());
                b.Expand(b.size * (2 * ReachMargin) + Vector3.one * (2 * MorphReach(skin.sharedMesh)));
                skin.localBounds = b;
                skin.updateWhenOffscreen = false;
            }
        }

        // The box round the vertices each joint carries, in that joint's own space, or null for a
        // joint that carries none. Null for a mesh without joints (blend shapes alone).
        static Bounds?[] JointBounds(SkinnedMeshRenderer skin) {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            if (mesh == null || bones == null || bones.Length == 0) return null;
            var poses = mesh.bindposes;
            var weights = mesh.boneWeights;
            var vertices = mesh.vertices;
            if (poses.Length < bones.Length || weights.Length != vertices.Length) return null;
            var boxes = new Bounds?[bones.Length];
            void Add(int bone, float weight, Vector3 vertex) {
                if (weight <= 0 || bone < 0 || bone >= boxes.Length) return;
                var p = poses[bone].MultiplyPoint3x4(vertex);
                if (boxes[bone] is Bounds b) {
                    b.Encapsulate(p);
                    boxes[bone] = b;
                } else boxes[bone] = new Bounds(p, Vector3.zero);
            }
            for (int v = 0; v < vertices.Length; v++) {
                var w = weights[v];
                Add(w.boneIndex0, w.weight0, vertices[v]);
                Add(w.boneIndex1, w.weight1, vertices[v]);
                Add(w.boneIndex2, w.weight2, vertices[v]);
                Add(w.boneIndex3, w.weight3, vertices[v]);
            }
            return boxes;
        }

        // Grows `reach` by where the joints carry their boxes now, in the space Unity keeps a
        // skinned renderer's bounds in: its root bone's, or its own without one.
        static void Reach(SkinnedMeshRenderer skin, Bounds?[] boxes, ref Bounds? reach) {
            if (boxes == null) return;
            var root = skin.rootBone != null ? skin.rootBone : skin.transform;
            var toRoot = root.worldToLocalMatrix;
            var bones = skin.bones;
            for (int j = 0; j < boxes.Length; j++) {
                if (!(boxes[j] is Bounds box) || bones[j] == null) continue;
                var m = toRoot * bones[j].localToWorldMatrix;
                Vector3 min = box.min, max = box.max;
                for (int c = 0; c < 8; c++) {
                    _corners[c] = m.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z));
                }
                foreach (var corner in _corners) {
                    if (reach is Bounds b) {
                        b.Encapsulate(corner);
                        reach = b;
                    } else reach = new Bounds(corner, Vector3.zero);
                }
            }
        }

        // The furthest a blend shape moves any vertex, which no joint's box knows about.
        static float MorphReach(Mesh mesh) {
            if (mesh == null || mesh.blendShapeCount == 0) return 0;
            var deltas = new Vector3[mesh.vertexCount];
            float most = 0;
            for (int s = 0; s < mesh.blendShapeCount; s++) {
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++) {
                    mesh.GetBlendShapeFrameVertices(s, f, deltas, null, null);
                    foreach (var d in deltas) most = Mathf.Max(most, d.magnitude);
                }
            }
            return most;
        }
    }
}
