using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    public static partial class ModelBridge {
        // RenderMeshInstanced draws at most this many in one call on every graphics API.
        internal const int ScatterChunk = 1023;
        // x, y, z, yaw and scale; and the same turned three ways and tinted: x, y, z, yaw, pitch,
        // roll, scale, then r, g and b in sRGB.
        const int ScatterStride = 5, ScatterStrideFull = 10;
        static readonly int ActorTintId = Shader.PropertyToID("_ActorTint");
        static readonly int ScatterRangeId = Shader.PropertyToID("_ScatterRange");
        static readonly int EyeId = Shader.PropertyToID("_OjEye");
        // Where a range has no edge: past any camera, and still a float a shader takes whole.
        const float NoEdge = 1e30f;

        /// <summary>One renderer of a model: what a scatter draws for it, and where it sits in the model.</summary>
        sealed class ScatterPart {
            // The template's own, so the mesh drawn is whichever the model has now: an unwrap swaps it.
            public MeshFilter filter;
            public Material[] materials;
            public Matrix4x4 local;
            public ShadowCastingMode cast;
            public bool receive;
            public int layer;
        }

        /// <summary>Up to ScatterChunk copies near one another: one instanced draw a part and submesh.</summary>
        sealed class ScatterChunkData {
            public int[] copies;
            // Per part, each copy's matrix with the part's own placement in the model applied.
            public Matrix4x4[][] matrices;
            public Bounds bounds;
            // Each copy's rectangle of the baked light's atlas, once a bake has a lightmap; null before.
            // It carries the copies' tints and the range too (Dress), and is drawn in place of `look`.
            public MaterialPropertyBlock light;
            // Each copy's tint, linear with an alpha of 1, where the scatter is tinted; null otherwise.
            public Vector4[] tints;
            // The tints and the range, before a bake: null for a scatter with neither, which draws
            // with no block at all, as every scatter did before them.
            public MaterialPropertyBlock look;
        }

        sealed class Scattered {
            public int model;
            public float[] data;
            public int stride = ScatterStride;
            // From, to, half the fade and 1, as the shader reads them (ModelRange.hlsl); w is 0 with no range.
            public Vector4 range;
            public int Count => data.Length / stride;
            public ScatterPart[] parts;
            public ScatterChunkData[] chunks;
            // The model's bounds the chunks were measured with: a mesh model's set moves them.
            public Bounds measuredWith;
            // By copy, its rectangle of the baked light's atlas (ModelBridge.Light.cs); null when not baked.
            public Vector4[] atlas;
        }

        static readonly Dictionary<int, Scattered> _scatters = new();
        static bool _drawing;

        /// <summary>
        /// Copies of a model that never move: five floats a copy (x, y, z, yaw in degrees, scale).
        /// No collider, no pick, no animation, and no GameObject: the copies are sorted into
        /// chunks of nearby ones, each drawn with one instanced call a submesh, which Unity culls
        /// by the chunk's bounds for every camera and every shadow pass. Returns the scatter's
        /// handle, or 0 for a model it cannot draw this way: one with a skinned mesh, or none.
        /// </summary>
        public static int Scatter(int model, float[] data) => ScatterWith(model, data, ScatterStride, 0, float.PositiveInfinity, 0);

        /// <summary>
        /// <see cref="Scatter"/> with each copy turned three ways and tinted, and drawn only within a
        /// range of the camera. `stride` is 5 (x, y, z, yaw, scale) or 10 (x, y, z, yaw, pitch, roll
        /// in degrees as Quaternion.Euler(pitch, yaw, roll) takes them, scale, then r, g and b in sRGB,
        /// multiplied into the copy's colour). A copy draws while the camera is between `from` and
        /// `to` of its foot, dithered across `fade` metres centred on each edge (ModelRange.hlsl);
        /// 0, infinity and 0 draw every copy whole at any distance. A chunk wholly outside its range
        /// is not drawn at all.
        /// </summary>
        public static int ScatterWith(int model, float[] data, int stride, float from, float to, float fade) {
            if (!_models.TryGetValue(model, out var m)) throw new ArgumentException($"no model {model}");
            if (stride != ScatterStride && stride != ScatterStrideFull) throw new ArgumentException($"a copy is five or ten floats, not {stride}");
            if (data == null || data.Length % stride != 0) {
                throw new ArgumentException(stride == ScatterStride ? "five floats a copy: x, y, z, yaw, scale" : "ten floats a copy: x, y, z, yaw, pitch, roll, scale, r, g, b");
            }
            if (!(from >= 0) || float.IsInfinity(from)) throw new ArgumentException($"from is {from}, and is 0 or more metres");
            if (!(to > from)) throw new ArgumentException($"to is {to}, and is further than from ({from})");
            if (!(fade >= 0) || float.IsInfinity(fade)) throw new ArgumentException($"fade is {fade}, and is 0 or more metres");
            if (m.template.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return 0;

            var parts = new List<ScatterPart>();
            foreach (var (filter, renderer) in Parts(m.template)) {
                var materials = renderer.sharedMaterials;
                // RenderMeshInstanced refuses a material that does not instance; ModelLit's all do.
                foreach (var material in materials) if (material != null) material.enableInstancing = true;
                parts.Add(new ScatterPart {
                    filter = filter, materials = materials,
                    // The template sits at the origin, so a renderer's world matrix is its place in the model.
                    local = filter.transform.localToWorldMatrix,
                    cast = renderer.shadowCastingMode, receive = renderer.receiveShadows, layer = renderer.gameObject.layer,
                });
            }
            if (parts.Count == 0) return 0;

            var scattered = new Scattered { model = model, data = data, stride = stride, parts = parts.ToArray() };
            if (from > 0 || !float.IsPositiveInfinity(to)) {
                scattered.range = new Vector4(from > 0 ? from : -NoEdge, float.IsPositiveInfinity(to) ? NoEdge : to, fade / 2, 1);
            }
            scattered.chunks = Chunk(scattered);
            Measure(scattered, m.bounds);
            foreach (var chunk in scattered.chunks) {
                if (chunk.tints == null && scattered.range.w == 0) continue;
                chunk.look = new MaterialPropertyBlock();
                Dress(scattered, chunk, chunk.look);
            }
            var id = ++_next;
            _scatters[id] = scattered;
            Draw(true);
            return id;
        }

        public static void DestroyScatter(int scatter) {
            if (!_scatters.Remove(scatter)) return;
            if (_scatters.Count == 0) Draw(false);
        }

        public static int ScatterCount => _scatters.Count;

        /// <summary>Whether the per-camera draw is registered: only while a scatter exists.</summary>
        internal static bool ScatterDrawing => _drawing;

        /// <summary>Adds what a scatter's chunk carries of its own, its copies' tints and the range, to `block`.</summary>
        static void Dress(Scattered s, ScatterChunkData chunk, MaterialPropertyBlock block) {
            if (chunk.tints != null) block.SetVectorArray(ActorTintId, chunk.tints);
            if (s.range.w != 0) block.SetVector(ScatterRangeId, s.range);
        }

        /// <summary>The block a scatter's chunk is drawn with: its light's once baked, else its look, else none.</summary>
        internal static MaterialPropertyBlock ScatterBlock(int scatter, int chunk) =>
            _scatters.TryGetValue(scatter, out var s) && chunk >= 0 && chunk < s.chunks.Length ? s.chunks[chunk].light ?? s.chunks[chunk].look : null;

        /// <summary>A scatter's chunks: how many copies each holds, and its bounds.</summary>
        internal static (int count, Bounds bounds)[] ScatterChunks(int scatter) {
            if (!_scatters.TryGetValue(scatter, out var s)) return Array.Empty<(int, Bounds)>();
            var chunks = new (int, Bounds)[s.chunks.Length];
            for (int i = 0; i < chunks.Length; i++) chunks[i] = (s.chunks[i].copies.Length, s.chunks[i].bounds);
            return chunks;
        }

        static bool Scatters(int model) {
            foreach (var s in _scatters.Values) if (s.model == model) return true;
            return false;
        }

        static void ClearScatters() {
            _scatters.Clear();
            Draw(false);
        }

        /// <summary>
        /// Orders the copies along a curve that keeps neighbours together (Morton order on x and
        /// z), then cuts it every ScatterChunk copies, so a chunk is a patch of ground and a camera
        /// sees few of them at once.
        /// </summary>
        static ScatterChunkData[] Chunk(Scattered s) {
            float[] data = s.data;
            var parts = s.parts;
            int stride = s.stride, n = s.Count;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (n == 0) return Array.Empty<ScatterChunkData>();
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++) {
                float x = data[i * stride], z = data[i * stride + 2];
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
            float spanX = Math.Max(maxX - minX, 1e-6f), spanZ = Math.Max(maxZ - minZ, 1e-6f);
            var keys = new uint[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) {
                uint qx = (uint)((data[i * stride] - minX) / spanX * 65535f);
                uint qz = (uint)((data[i * stride + 2] - minZ) / spanZ * 65535f);
                keys[i] = Spread(qx) | (Spread(qz) << 1);
                order[i] = i;
            }
            Array.Sort(keys, order);

            var chunks = new ScatterChunkData[(n + ScatterChunk - 1) / ScatterChunk];
            for (int c = 0; c < chunks.Length; c++) {
                int start = c * ScatterChunk, count = Math.Min(ScatterChunk, n - start);
                var chunk = new ScatterChunkData { copies = new int[count], matrices = new Matrix4x4[parts.Length][] };
                for (int p = 0; p < parts.Length; p++) chunk.matrices[p] = new Matrix4x4[count];
                if (stride == ScatterStrideFull) chunk.tints = new Vector4[count];
                for (int k = 0; k < count; k++) {
                    int i = order[start + k];
                    chunk.copies[k] = i;
                    var copy = CopyMatrix(s, i);
                    for (int p = 0; p < parts.Length; p++) chunk.matrices[p][k] = copy * parts[p].local;
                    if (chunk.tints != null) chunk.tints[k] = CopyTint(s, i, linear);
                }
                chunks[c] = chunk;
            }
            return chunks;
        }

        // The bits of a 16-bit number spread to every other place, for a Morton key.
        static uint Spread(uint v) {
            v &= 0xffff;
            v = (v | (v << 8)) & 0x00ff00ff;
            v = (v | (v << 4)) & 0x0f0f0f0f;
            v = (v | (v << 2)) & 0x33333333;
            v = (v | (v << 1)) & 0x55555555;
            return v;
        }

        static Matrix4x4 CopyMatrix(Scattered s, int i) {
            var data = s.data;
            int at = i * s.stride;
            if (s.stride == ScatterStride) {
                return Matrix4x4.TRS(new Vector3(data[at], data[at + 1], data[at + 2]),
                    Quaternion.Euler(0, data[at + 3], 0), Vector3.one * data[at + 4]);
            }
            return Matrix4x4.TRS(new Vector3(data[at], data[at + 1], data[at + 2]),
                Quaternion.Euler(data[at + 4], data[at + 3], data[at + 5]), Vector3.one * data[at + 6]);
        }

        static float CopyScale(Scattered s, int i) => s.data[i * s.stride + (s.stride == ScatterStride ? 4 : 6)];

        /// <summary>A copy's tint as its draw reads it: white for a scatter without tints.</summary>
        static Color CopyTintColor(Scattered s, int i) {
            if (s.stride != ScatterStrideFull) return Color.white;
            int at = i * s.stride;
            return new Color(s.data[at + 7], s.data[at + 8], s.data[at + 9], 1);
        }

        // sRGB as everywhere in oj, made linear for a linear project as vertex colours are (SetMesh).
        static Vector4 CopyTint(Scattered s, int i, bool linear) {
            var c = CopyTintColor(s, i);
            if (linear) c = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), 1);
            return c;
        }

        static bool Tilted(Scattered s, int i) => s.stride == ScatterStrideFull && (s.data[i * s.stride + 4] != 0 || s.data[i * s.stride + 5] != 0);

        /// <summary>Each chunk's bounds, from every copy's: the model's bounds turned and scaled.</summary>
        static void Measure(Scattered s, Bounds model) {
            s.measuredWith = model;
            // Turning about the up axis keeps the height and swings the footprint within this radius.
            float reach = new Vector2(Math.Max(Math.Abs(model.min.x), Math.Abs(model.max.x)), Math.Max(Math.Abs(model.min.z), Math.Abs(model.max.z))).magnitude;
            // Tilted as well, the model stays within the sphere through its furthest corner.
            float ball = Vector3.Max(new Vector3(Math.Abs(model.min.x), Math.Abs(model.min.y), Math.Abs(model.min.z)),
                new Vector3(Math.Abs(model.max.x), Math.Abs(model.max.y), Math.Abs(model.max.z))).magnitude;
            foreach (var chunk in s.chunks) {
                var bounds = new Bounds();
                for (int k = 0; k < chunk.copies.Length; k++) {
                    int i = chunk.copies[k], at = i * s.stride;
                    float scale = CopyScale(s, i), wide = Math.Abs(scale) * reach;
                    float low = Math.Min(model.min.y * scale, model.max.y * scale), high = Math.Max(model.min.y * scale, model.max.y * scale);
                    if (Tilted(s, i)) {
                        wide = Math.Abs(scale) * ball;
                        low = -wide;
                        high = wide;
                    }
                    var lo = new Vector3(s.data[at] - wide, s.data[at + 1] + low, s.data[at + 2] - wide);
                    var hi = new Vector3(s.data[at] + wide, s.data[at + 1] + high, s.data[at + 2] + wide);
                    if (k == 0) bounds.SetMinMax(lo, hi);
                    else {
                        bounds.Encapsulate(lo);
                        bounds.Encapsulate(hi);
                    }
                }
                chunk.bounds = bounds;
            }
        }

        // Only while a scatter exists, so a cart that scatters nothing has no callback at all.
        static void Draw(bool on) {
            if (on == _drawing) return;
            _drawing = on;
            if (on) {
                RenderPipelineManager.beginCameraRendering += DrawFor;
                Camera.onPreCull += DrawFor;
            } else {
                RenderPipelineManager.beginCameraRendering -= DrawFor;
                Camera.onPreCull -= DrawFor;
            }
        }

        static void DrawFor(ScriptableRenderContext _, Camera camera) => DrawFor(camera);

        /// <summary>
        /// Every scatter, for one camera about to render. URP calls this from beginCameraRendering
        /// and the built-in pipeline from onPreCull, each before it culls, so the draws are culled
        /// with the camera's own frustum and its shadow cascades by each chunk's bounds.
        /// </summary>
        static void DrawFor(Camera camera) {
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            var eye = camera.transform.position;
            // A shadow pass draws from the light, and a range still measures from this camera.
            Shader.SetGlobalVector(EyeId, eye);
            foreach (var s in _scatters.Values) {
                if (!_models.TryGetValue(s.model, out var m)) continue;
                if (m.bounds != s.measuredWith) Measure(s, m.bounds);
                foreach (var chunk in s.chunks) {
                    if (s.range.w != 0 && OutOfRange(chunk.bounds, eye, s.range)) continue;
                    for (int p = 0; p < s.parts.Length; p++) {
                        var part = s.parts[p];
                        var mesh = part.filter.sharedMesh;
                        for (int sub = 0; sub < mesh.subMeshCount && sub < part.materials.Length; sub++) {
                            if (part.materials[sub] == null) continue;
                            var rp = new RenderParams(part.materials[sub]) {
                                camera = camera, worldBounds = chunk.bounds, layer = part.layer,
                                shadowCastingMode = part.cast, receiveShadows = part.receive,
                                matProps = chunk.light ?? chunk.look,
                                // The built-in pipeline adds the ambient probe itself; a baked copy's is zero (Apply).
                                lightProbeUsage = chunk.light != null && BuiltInPipeline ? LightProbeUsage.CustomProvided : LightProbeUsage.BlendProbes,
                            };
                            Graphics.RenderMeshInstanced(rp, mesh, sub, chunk.matrices[p], chunk.copies.Length);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether every copy in a chunk is outside its range: the chunk's bounds hold each copy's
        /// foot, so the nearest and furthest of them from the camera bracket every copy's distance.
        /// </summary>
        internal static bool OutOfRange(Bounds bounds, Vector3 eye, Vector4 range) {
            float near = Vector3.Distance(bounds.ClosestPoint(eye), eye);
            var far = Vector3.Max(eye - bounds.min, bounds.max - eye);
            return near > range.y + range.z || far.magnitude < range.x - range.z;
        }

        /// <summary>A scatter's range as its draw reads it (ModelRange.hlsl): w is 0 for one with none.</summary>
        internal static Vector4 ScatterRange(int scatter) => _scatters.TryGetValue(scatter, out var s) ? s.range : default;
    }
}
