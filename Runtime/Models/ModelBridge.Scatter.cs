using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Models {
    public static partial class ModelBridge {
        // RenderMeshInstanced draws at most this many in one call on every graphics API.
        internal const int ScatterChunk = 1023;
        const int ScatterStride = 5;

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
            public MaterialPropertyBlock light;
        }

        sealed class Scattered {
            public int model;
            public float[] data;
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
        public static int Scatter(int model, float[] data) {
            if (!_models.TryGetValue(model, out var m)) throw new ArgumentException($"no model {model}");
            if (data == null || data.Length % ScatterStride != 0) throw new ArgumentException("five floats a copy: x, y, z, yaw, scale");
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

            var scattered = new Scattered { model = model, data = data, parts = parts.ToArray() };
            scattered.chunks = Chunk(data, scattered.parts);
            Measure(scattered, m.bounds);
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
        static ScatterChunkData[] Chunk(float[] data, ScatterPart[] parts) {
            int n = data.Length / ScatterStride;
            if (n == 0) return Array.Empty<ScatterChunkData>();
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++) {
                float x = data[i * ScatterStride], z = data[i * ScatterStride + 2];
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
            float spanX = Math.Max(maxX - minX, 1e-6f), spanZ = Math.Max(maxZ - minZ, 1e-6f);
            var keys = new uint[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) {
                uint qx = (uint)((data[i * ScatterStride] - minX) / spanX * 65535f);
                uint qz = (uint)((data[i * ScatterStride + 2] - minZ) / spanZ * 65535f);
                keys[i] = Spread(qx) | (Spread(qz) << 1);
                order[i] = i;
            }
            Array.Sort(keys, order);

            var chunks = new ScatterChunkData[(n + ScatterChunk - 1) / ScatterChunk];
            for (int c = 0; c < chunks.Length; c++) {
                int start = c * ScatterChunk, count = Math.Min(ScatterChunk, n - start);
                var chunk = new ScatterChunkData { copies = new int[count], matrices = new Matrix4x4[parts.Length][] };
                for (int p = 0; p < parts.Length; p++) chunk.matrices[p] = new Matrix4x4[count];
                for (int k = 0; k < count; k++) {
                    int i = order[start + k];
                    chunk.copies[k] = i;
                    var copy = CopyMatrix(data, i);
                    for (int p = 0; p < parts.Length; p++) chunk.matrices[p][k] = copy * parts[p].local;
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

        static Matrix4x4 CopyMatrix(float[] data, int i) {
            int at = i * ScatterStride;
            return Matrix4x4.TRS(new Vector3(data[at], data[at + 1], data[at + 2]),
                Quaternion.Euler(0, data[at + 3], 0), Vector3.one * data[at + 4]);
        }

        /// <summary>Each chunk's bounds, from every copy's: the model's bounds turned and scaled.</summary>
        static void Measure(Scattered s, Bounds model) {
            s.measuredWith = model;
            // Turning about the up axis keeps the height and swings the footprint within this radius.
            float reach = new Vector2(Math.Max(Math.Abs(model.min.x), Math.Abs(model.max.x)), Math.Max(Math.Abs(model.min.z), Math.Abs(model.max.z))).magnitude;
            foreach (var chunk in s.chunks) {
                var bounds = new Bounds();
                for (int k = 0; k < chunk.copies.Length; k++) {
                    int at = chunk.copies[k] * ScatterStride;
                    float scale = s.data[at + 4], wide = Math.Abs(scale) * reach;
                    float low = Math.Min(model.min.y * scale, model.max.y * scale), high = Math.Max(model.min.y * scale, model.max.y * scale);
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
            foreach (var s in _scatters.Values) {
                if (!_models.TryGetValue(s.model, out var m)) continue;
                if (m.bounds != s.measuredWith) Measure(s, m.bounds);
                foreach (var chunk in s.chunks) {
                    for (int p = 0; p < s.parts.Length; p++) {
                        var part = s.parts[p];
                        var mesh = part.filter.sharedMesh;
                        for (int sub = 0; sub < mesh.subMeshCount && sub < part.materials.Length; sub++) {
                            if (part.materials[sub] == null) continue;
                            var rp = new RenderParams(part.materials[sub]) {
                                camera = camera, worldBounds = chunk.bounds, layer = part.layer,
                                shadowCastingMode = part.cast, receiveShadows = part.receive,
                                matProps = chunk.light,
                                // The built-in pipeline adds the ambient probe itself; a baked copy's is zero (Apply).
                                lightProbeUsage = chunk.light != null && BuiltInPipeline ? LightProbeUsage.CustomProvided : LightProbeUsage.BlendProbes,
                            };
                            Graphics.RenderMeshInstanced(rp, mesh, sub, chunk.matrices[p], chunk.copies.Length);
                        }
                    }
                }
            }
        }
    }
}
