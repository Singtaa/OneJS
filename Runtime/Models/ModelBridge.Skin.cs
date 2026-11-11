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

        // An actor is seen when any renderer under it is, which is Unity's rule for sampling its
        // clips, and what is attached to its joints is under it.
        static bool Seen(Actor a) {
            foreach (var r in a.renderers) if (r != null && r.isVisible) return true;
            if (a.attached != null) {
                foreach (var id in a.attached) if (_actors.TryGetValue(id, out var child) && Seen(child)) return true;
            }
            return false;
        }

        /// <summary>
        /// Fixes the bounds of a template's skinned renderers to cover every clip's reach, and
        /// stops them and its clips updating while unseen. Called once, when the model loads.
        /// </summary>
        static void PrepareSkins(GameObject template, Animation anim) {
            var skins = template.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) return;
            var reach = new SkinReach[skins.Length];
            for (int i = 0; i < skins.Length; i++) reach[i] = SkinReach.Of(skins[i]);

            void Grow() {
                for (int i = 0; i < reach.Length; i++) reach[i]?.Grow();
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
                var b = reach[i] != null && reach[i].Measured(out var reached) ? reached : (skin.sharedMesh != null ? skin.sharedMesh.bounds : new Bounds());
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

        // Where a skin's joints carry the vertices they hold, gathered over the poses its clips are
        // sampled in. What does not change between poses is read once: the joints, the space the
        // bounds are kept in (the root bone's, or the renderer's own without one), and each joint's
        // box as its eight corners in that joint's space.
        sealed class SkinReach {
            Transform _root;
            Transform[] _joints; // one per box
            Vector3[] _corners;  // eight per box
            Vector3 _min, _max;
            bool _any;

            public static SkinReach Of(SkinnedMeshRenderer skin) {
                var boxes = JointBounds(skin);
                if (boxes == null) return null;
                var bones = skin.bones;
                var joints = new List<Transform>();
                var corners = new List<Vector3>();
                for (int j = 0; j < boxes.Length; j++) {
                    if (!(boxes[j] is Bounds box) || bones[j] == null) continue;
                    joints.Add(bones[j]);
                    Vector3 min = box.min, max = box.max;
                    for (int c = 0; c < 8; c++) {
                        corners.Add(new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z));
                    }
                }
                return new SkinReach {
                    _root = skin.rootBone != null ? skin.rootBone : skin.transform,
                    _joints = joints.ToArray(), _corners = corners.ToArray(),
                };
            }

            // Grows the reach by where the joints carry their boxes now.
            public void Grow() {
                var toRoot = _root.worldToLocalMatrix;
                for (int j = 0, c = 0; j < _joints.Length; j++, c += 8) {
                    var m = toRoot * _joints[j].localToWorldMatrix;
                    for (int k = c; k < c + 8; k++) {
                        var p = m.MultiplyPoint3x4(_corners[k]);
                        if (_any) {
                            _min = Vector3.Min(_min, p);
                            _max = Vector3.Max(_max, p);
                        } else {
                            _min = _max = p;
                            _any = true;
                        }
                    }
                }
            }

            public bool Measured(out Bounds bounds) {
                bounds = default;
                if (!_any) return false;
                bounds.SetMinMax(_min, _max);
                return true;
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
