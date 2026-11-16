using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OneJS.Models {
    // Skinning on the graphics card.
    //
    // A SkinnedMeshRenderer on the web player is skinned on the CPU, one thread of wasm, and
    // every seen vertex's position and normal is written to the graphics card each frame, whether
    // the pose moved or not: 13 to 16 us a seen 1,600 triangle person on an M4 Pro, the same
    // standing, held or walking (PrimoEden's crowd finding, 16 Nov 2026). So an actor's skinned
    // renderers are made MeshRenderers over the same mesh at spawn, and ModelLit's _OJ_SKIN
    // variant skins each vertex from a palette: one texture of every such actor's joints, three
    // texels a joint (its 3x4 matrix, from the renderer's space to the renderer's space posed).
    // The palette is a texture rather than a storage buffer so the same variant runs on WebGL 2,
    // which has no storage buffers, and on a WebGPU device that allows none in the vertex stage.
    //
    // A joint's matrix is written only when the actor's pose moved: a clip's time or weight
    // changed while it is seen. Moving, turning or scaling the actor moves its renderer and its
    // joints alike, so it leaves the palette as it was. A still actor then costs what a rigid one
    // does. Its joints stay Transforms, played by its Animation as before, so attachTo, the clips'
    // culling and everything that reads a joint are unchanged.
    //
    // Where a skin cannot be drawn this way it stays a SkinnedMeshRenderer: blend shapes, more
    // than four joints a vertex, a material that is not ModelLit, the built-in pipeline, edit mode,
    // and a palette that is full.
    public static partial class ModelBridge {
        // Texels a row of the palette. ModelSkin.hlsl has the same number.
        internal const int PaletteWide = 1024;
        // Where a renderer's first joint sits in the palette rides in its user value above the
        // shadow bit and the colour row (13 bits for MostColorRows). An instanced draw carries
        // the user value as a float's bits, so the base stays under where those bits would read
        // as a NaN, which a graphics card may rewrite: 130,000 joints, 3,900 people of 33.
        internal const int SkinBaseShift = 14;
        internal const int MostPaletteJoints = 130000;
        // Joint 0 of the palette is never given out, so a renderer's base is never 0.
        const int FirstPaletteJoint = 1;
        const string SkinKeyword = "_OJ_SKIN";
        static readonly int SkinPaletteId = Shader.PropertyToID("_OjSkinPalette");

        /// <summary>
        /// Whether actors spawned from now skin on the graphics card where they can. On by default;
        /// off, every skin stays a SkinnedMeshRenderer, which is how a run measures the two apart.
        /// Actors already spawned keep the way they were made with.
        /// </summary>
        public static bool GpuSkinning { get; set; } = true;

        /// <summary>How many actors' renderers are skinned on the graphics card now.</summary>
        public static int GpuSkinCount => _gpuSkinned.Count;

        // How many times an actor's pose has been written to the palette, for the tests.
        internal static int PosesWritten { get; private set; }

        /// <summary>One renderer of an actor skinned on the graphics card, and its place in the palette.</summary>
        internal sealed class GpuSkin {
            public MeshRenderer renderer;
            public Transform space;
            public Transform[] bones;
            public Matrix4x4[] bindposes;
            public int baseJoint;
        }

        /// <summary>A model's material as its skins on the graphics card draw it: a copy with _OJ_SKIN on.</summary>
        internal sealed class SkinMaterial {
            public Material copy;
            public int crc;
        }

        static readonly HashSet<Actor> _gpuSkinned = new();
        static Vector4[] _palette = new Vector4[0];
        static Texture2D _paletteTexture;
        static int _paletteJoints = FirstPaletteJoint;
        // Freed ranges, by their length in joints.
        static readonly Dictionary<int, Stack<int>> _freePalette = new();
        static bool _paletteChanged;
        static int _posedFrame = -1;

        // Whether this model's skins can all be drawn by ModelLit's skin variant. Asked once.
        static bool GpuSkinnable(Model m) {
            if (m.gpuSkinned is bool known) return known;
            bool can = m.built == null && m.template != null && m.skinSpaces != null && ModelMaterialGenerator.ModelShader != null;
            if (can) {
                var skins = m.template.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                can = skins.Length == m.skinSpaces.Length;
                foreach (var skin in skins) {
                    if (!can) break;
                    can = SkinnableOnGpu(skin);
                }
            }
            m.gpuSkinned = can;
            return can;
        }

        static bool SkinnableOnGpu(SkinnedMeshRenderer skin) {
            var mesh = skin.sharedMesh;
            var bones = skin.bones;
            if (mesh == null || bones == null || bones.Length == 0 || mesh.blendShapeCount > 0) return false;
            if (mesh.bindposeCount < bones.Length) return false;
            if (!mesh.HasVertexAttribute(VertexAttribute.BlendIndices) || !mesh.HasVertexAttribute(VertexAttribute.BlendWeight)) return false;
            if (mesh.GetVertexAttributeDimension(VertexAttribute.BlendIndices) > 4) return false;
            foreach (var material in skin.sharedMaterials) {
                if (material == null || material.shader != ModelMaterialGenerator.ModelShader) return false;
            }
            return true;
        }

        static bool GpuSkinsHere =>
            GpuSkinning && Playing && !BuiltInPipeline && SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat);

        /// <summary>
        /// Makes a new actor's skinned renderers MeshRenderers that ModelLit skins, before it is
        /// active. Null where it keeps them (see the top of this file).
        /// </summary>
        static GpuSkin[] SkinOnGpu(GameObject go, Model m) {
            if (!GpuSkinsHere || !GpuSkinnable(m)) return null;
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int joints = 0;
            foreach (var skin in skins) joints += skin.bones.Length;
            if (_paletteJoints + joints > MostPaletteJoints && !FreeRangesFor(skins)) return null;
            var made = new GpuSkin[skins.Length];
            for (int i = 0; i < skins.Length; i++) {
                var skin = skins[i];
                var host = skin.gameObject;
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                var materials = SkinMaterials(m, skin.sharedMaterials);
                var cast = skin.shadowCastingMode;
                bool receive = skin.receiveShadows, enabled = skin.enabled;
                var probes = skin.lightProbeUsage;
                var reflections = skin.reflectionProbeUsage;
                uint layers = skin.renderingLayerMask;
                Object.DestroyImmediate(skin);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = host.AddComponent<MeshRenderer>();
                r.sharedMaterials = materials;
                r.shadowCastingMode = cast;
                r.receiveShadows = receive;
                r.lightProbeUsage = probes;
                r.reflectionProbeUsage = reflections;
                r.renderingLayerMask = layers;
                r.enabled = enabled;
                r.localBounds = m.skinSpaces[i];
                var bindposes = new Matrix4x4[bones.Length];
                var all = mesh.GetBindposes();
                for (int j = 0; j < bones.Length; j++) bindposes[j] = all[j];
                made[i] = new GpuSkin { renderer = r, space = host.transform, bones = bones, bindposes = bindposes };
            }
            return made;
        }

        // Whether freed ranges alone can hold every skin of a spawn, when the palette is full.
        static bool FreeRangesFor(SkinnedMeshRenderer[] skins) {
            var wanted = new Dictionary<int, int>();
            foreach (var skin in skins) wanted[skin.bones.Length] = wanted.TryGetValue(skin.bones.Length, out var n) ? n + 1 : 1;
            foreach (var pair in wanted) {
                if (!_freePalette.TryGetValue(pair.Key, out var free) || free.Count < pair.Value) return false;
            }
            return true;
        }

        // The copies of a skin's materials with the skin variant on, one a material a model.
        static Material[] SkinMaterials(Model m, Material[] sources) {
            m.skinMaterials ??= new Dictionary<Material, SkinMaterial>();
            var copies = new Material[sources.Length];
            for (int i = 0; i < sources.Length; i++) {
                var source = sources[i];
                if (!m.skinMaterials.TryGetValue(source, out var held)) {
                    var copy = new Material(source) { name = source.name + " (skinned on the graphics card)" };
                    copy.EnableKeyword(SkinKeyword);
                    held = new SkinMaterial { copy = copy, crc = source.ComputeCRC() };
                    m.skinMaterials[source] = held;
                }
                copies[i] = held.copy;
            }
            return copies;
        }

        static void FreeSkinMaterials(Model m) {
            if (m.skinMaterials == null) return;
            foreach (var held in m.skinMaterials.Values) Kill(held.copy);
            m.skinMaterials = null;
        }

        // A model's material changed after its skins were copied: the copy takes it again.
        static void FollowSkinMaterials() {
            foreach (var m in _models.Values) {
                if (m.skinMaterials == null) continue;
                foreach (var pair in m.skinMaterials) {
                    if (pair.Key == null) continue;
                    int crc = pair.Key.ComputeCRC();
                    if (crc == pair.Value.crc) continue;
                    pair.Value.crc = crc;
                    pair.Value.copy.CopyPropertiesFromMaterial(pair.Key);
                    pair.Value.copy.EnableKeyword(SkinKeyword);
                }
            }
        }

        /// <summary>Gives a new actor's skins their places in the palette and writes its pose there.</summary>
        static void StartGpuSkins(Actor a) {
            if (a.gpuSkins == null) return;
            foreach (var s in a.gpuSkins) s.baseJoint = TakePalette(s.bones.Length);
            if (a.anim != null) {
                var states = new List<AnimationState>();
                foreach (AnimationState state in a.anim) states.Add(state);
                a.states = states.ToArray();
                a.pose = new float[a.states.Length * 3];
            }
            WritePose(a);
            if (_gpuSkinned.Add(a) && _gpuSkinned.Count == 1) {
                _posedFrame = -1;
                RenderPipelineManager.beginContextRendering += PosesFor;
            }
        }

        static void FreeGpuSkins(Actor a) {
            if (a.gpuSkins == null) return;
            foreach (var s in a.gpuSkins) GivePalette(s.baseJoint, s.bones.Length);
            a.gpuSkins = null;
            if (_gpuSkinned.Remove(a) && _gpuSkinned.Count == 0) RenderPipelineManager.beginContextRendering -= PosesFor;
        }

        static void StopGpuSkins() {
            if (_gpuSkinned.Count > 0) RenderPipelineManager.beginContextRendering -= PosesFor;
            _gpuSkinned.Clear();
            _freePalette.Clear();
            _paletteJoints = FirstPaletteJoint;
            _palette = new Vector4[0];
            _paletteChanged = false;
            if (_paletteTexture != null) Kill(_paletteTexture);
            _paletteTexture = null;
            Shader.SetGlobalTexture(SkinPaletteId, null);
        }

        static int TakePalette(int joints) {
            if (_freePalette.TryGetValue(joints, out var free) && free.Count > 0) return free.Pop();
            int at = _paletteJoints;
            _paletteJoints += joints;
            int texels = _paletteJoints * 3;
            if (_palette.Length < texels) {
                int rows = Mathf.NextPowerOfTwo((texels + PaletteWide - 1) / PaletteWide);
                System.Array.Resize(ref _palette, rows * PaletteWide);
            }
            return at;
        }

        static void GivePalette(int at, int joints) {
            if (!_freePalette.TryGetValue(joints, out var free)) _freePalette[joints] = free = new Stack<int>();
            free.Push(at);
        }

        // The bits of a renderer's user value that say where its palette starts, 0 for a renderer
        // that is not skinned on the graphics card.
        static uint SkinBaseBits(Actor a, Renderer r) {
            if (a.gpuSkins == null) return 0;
            foreach (var s in a.gpuSkins) if (s.renderer == r) return (uint)s.baseJoint << SkinBaseShift;
            return 0;
        }

        static void PosesFor(ScriptableRenderContext _, List<Camera> __) => PoseGpuSkins();

        /// <summary>
        /// Once a frame, before anything draws: writes the joints of every actor whose pose moved,
        /// then sends the palette when any did.
        /// </summary>
        internal static void PoseGpuSkins() {
            if (Time.frameCount == _posedFrame) return;
            _posedFrame = Time.frameCount;
            FollowSkinMaterials();
            foreach (var a in _gpuSkinned) {
                if (a.go == null) continue;
                if (a.attached != null) CullByWhatItCarries(a);
                if (Moved(a)) WritePose(a);
            }
            if (!_paletteChanged) return;
            _paletteChanged = false;
            int rows = _palette.Length / PaletteWide;
            if (_paletteTexture == null || _paletteTexture.height != rows) {
                if (_paletteTexture != null) Kill(_paletteTexture);
                _paletteTexture = new Texture2D(PaletteWide, rows, TextureFormat.RGBAFloat, false, true) {
                    name = "joint palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                };
                Shader.SetGlobalTexture(SkinPaletteId, _paletteTexture);
            }
            _paletteTexture.SetPixelData(_palette, 0);
            _paletteTexture.Apply(false);
        }

        // Unity culls an actor's clips by its renderers (BasedOnRenderers). With SkinnedMeshRenderers
        // a hat seen on the arm of a body that is not keeps the body's clip sampled; with the
        // MeshRenderers here it does not, and the hat hangs on a joint left still (measured by
        // AttachedActorPlayModeTests). So while only what it carries is seen, its clips always play.
        static void CullByWhatItCarries(Actor a) {
            if (a.anim == null) return;
            bool own = false;
            foreach (var r in a.renderers) if (r != null && r.isVisible) { own = true; break; }
            var culling = !own && Seen(a) ? AnimationCullingType.AlwaysAnimate : AnimationCullingType.BasedOnRenderers;
            if (a.anim.cullingType != culling) a.anim.cullingType = culling;
        }

        // Whether a seen actor's clips are somewhere else than when its palette was written: an
        // unseen one is not sampled (BasedOnRenderers), so its joints have not moved.
        static bool Moved(Actor a) {
            if (a.states == null || !Seen(a)) return false;
            bool moved = false;
            var pose = a.pose;
            for (int i = 0, k = 0; i < a.states.Length; i++, k += 3) {
                var state = a.states[i];
                float on = state.enabled ? 1 : 0;
                float time = on > 0 ? state.time : 0;
                float weight = on > 0 ? state.weight : 0;
                if (pose[k] != on || pose[k + 1] != time || pose[k + 2] != weight) {
                    pose[k] = on;
                    pose[k + 1] = time;
                    pose[k + 2] = weight;
                    moved = true;
                }
            }
            return moved;
        }

        // Each joint of each skin as a 3x4 matrix from its renderer's space to the same space
        // posed: the renderer's inverse, the joint, and the joint's bind pose. A hidden part's
        // too, so it is posed when it is shown again.
        static void WritePose(Actor a) {
            foreach (var s in a.gpuSkins) {
                if (s.renderer == null) continue;
                var toSpace = s.space.worldToLocalMatrix;
                int t = s.baseJoint * 3;
                for (int j = 0; j < s.bones.Length; j++, t += 3) {
                    var bone = s.bones[j];
                    var m = bone != null ? toSpace * bone.localToWorldMatrix * s.bindposes[j] : Matrix4x4.identity;
                    _palette[t] = new Vector4(m.m00, m.m01, m.m02, m.m03);
                    _palette[t + 1] = new Vector4(m.m10, m.m11, m.m12, m.m13);
                    _palette[t + 2] = new Vector4(m.m20, m.m21, m.m22, m.m23);
                }
            }
            _paletteChanged = true;
            PosesWritten++;
        }
    }
}
