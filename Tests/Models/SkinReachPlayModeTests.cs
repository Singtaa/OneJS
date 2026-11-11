using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using OneJS.Models;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Models {
    /// <summary>
    /// The fixed bounds a skinned model is given at load (ModelBridge.Skin.cs) are the ones the
    /// straightforward measure gives: every clip sampled at the same times, each joint's box
    /// carried through the joints' world matrices and gathered as Bounds. That measure is kept
    /// here, as it was written, and run on a spawned copy of each skinned fixture.
    /// </summary>
    public class SkinReachPlayModeTests {
        // As in ModelBridge.Skin.cs.
        const float ReachRate = 30;
        const int ReachSamples = 240;
        const float ReachMargin = 0.1f;

        static string Fixture(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here), "Fixtures~", name);

        static IEnumerator Load(string name, System.Action<int> done) {
            Task<int> task = ModelBridge.Load(Fixture(name));
            while (!task.IsCompleted) yield return null;
            Assert.That(task.Exception, Is.Null);
            done(task.Result);
        }

        [TearDown]
        public void Clean() => ModelBridge.DisposeAll();

        [UnityTest]
        public IEnumerator AModelsOwnClips_GiveTheMeasuredBounds() {
            int model = 0;
            yield return Load("reach.glb", m => model = m);
            AssertMeasured(model, 2);
        }

        [UnityTest]
        public IEnumerator AModelWithABlendShape_GivesTheMeasuredBounds() {
            int model = 0;
            yield return Load("skinned.glb", m => model = m);
            AssertMeasured(model, 1);
        }

        [UnityTest]
        public IEnumerator TakenClips_GiveTheMeasuredBounds() {
            int moves = 0, body = 0;
            yield return Load("reach.glb", m => moves = m);
            yield return Load("reach-body.glb", m => body = m);
            Assert.That(ModelBridge.TakeClips(body, moves), Is.EqualTo(""));
            AssertMeasured(body, 2);
        }

        static void AssertMeasured(int model, int clips) {
            var go = ModelBridge.ActorObject(ModelBridge.Spawn(model, 0, 0, 0, 0, 1, false, false));
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skins.Length, Is.GreaterThan(0));
            var given = new Bounds[skins.Length];
            for (int i = 0; i < skins.Length; i++) given[i] = skins[i].localBounds;
            var anim = go.GetComponentInChildren<Animation>(true);
            int sampled = 0;
            if (anim != null) foreach (AnimationState state in anim) if (state.clip != null) sampled++;
            Assert.That(sampled, Is.EqualTo(clips), "the fixture's clips");

            var measured = Measure(skins, anim);
            for (int i = 0; i < skins.Length; i++) {
                AssertNear(given[i].min, measured[i].min, $"{skins[i].name} min");
                AssertNear(given[i].max, measured[i].max, $"{skins[i].name} max");
            }
        }

        static void AssertNear(Vector3 a, Vector3 b, string what) {
            float tolerance = 1e-5f * Mathf.Max(1, b.magnitude);
            Assert.That(Vector3.Distance(a, b), Is.LessThanOrEqualTo(tolerance), $"{what}: given {a:F6}, measured {b:F6}");
        }

        // The measure as it was first written: Bounds? per skin, the joints and the root read on
        // every pose.
        static Bounds[] Measure(SkinnedMeshRenderer[] skins, Animation anim) {
            var parts = new Bounds?[skins.Length][];
            var reach = new Bounds?[skins.Length];
            for (int i = 0; i < skins.Length; i++) parts[i] = JointBounds(skins[i]);
            void Grow() {
                for (int i = 0; i < skins.Length; i++) Reach(skins[i], parts[i], ref reach[i]);
            }
            Grow();
            if (anim != null) {
                var transforms = anim.GetComponentsInChildren<Transform>(true);
                var rest = new (Vector3, Quaternion, Vector3)[transforms.Length];
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
                    (transforms[i].localPosition, transforms[i].localRotation, transforms[i].localScale) = rest[i];
                }
            }
            var result = new Bounds[skins.Length];
            for (int i = 0; i < skins.Length; i++) {
                var skin = skins[i];
                var b = reach[i] ?? (skin.sharedMesh != null ? skin.sharedMesh.bounds : new Bounds());
                b.Expand(b.size * (2 * ReachMargin) + Vector3.one * (2 * MorphReach(skin.sharedMesh)));
                result[i] = b;
            }
            return result;
        }

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
                    var corner = m.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z));
                    if (reach is Bounds b) {
                        b.Encapsulate(corner);
                        reach = b;
                    } else reach = new Bounds(corner, Vector3.zero);
                }
            }
        }

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
