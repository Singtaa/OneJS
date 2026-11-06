using System;
using System.Collections.Generic;
using OneJS.Lighting;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OneJS.Models {
    /// <summary>
    /// Baked light for the scene's static actors and scattered copies, and probe light for
    /// everything else: onejs-play's `scene.bake` (PlaySite docs/lightmapping.md in the container).
    ///
    /// onejs-play owns the layout, since xatlas runs there: it reads each model's triangles
    /// (LightMesh), unwraps them, hands the result back (SetLightUnwrap), and then names each
    /// static actor's and copy's rectangle of the atlas and the probe grid (BeginBake). The bridge
    /// gathers the world's triangles and their albedo into a LightBaker and steps it (StepBake).
    /// The lightmap is the global `_OjLightmap`, each baked actor reads its rectangle from
    /// `_ActorLightmapST`, and every other actor takes the light at its middle from the probes.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int ActorLightmapId = Shader.PropertyToID("_ActorLightmapST");
        static readonly int LightmapId = Shader.PropertyToID("_OjLightmap");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int MetallicMapId = Shader.PropertyToID("_MetallicRoughnessMap");
        const int ActorStride = 5, CopyStride = 6, GridFloats = 7;

        static LightBaker _baker;
        // The lightmap ModelLit reads now: the bake's draft, then its final. Null until the draft.
        static Texture _lightmap;
        // The probes' 27 floats each, as LightBaker.ProbeSH lays them out, and their grid. Null until read.
        static float[] _probes;
        static Vector3 _gridOrigin;
        static float _gridSpacing;
        static Vector3Int _gridCounts;
        static bool _probing;
        static readonly SphericalHarmonicsL2[] _oneProbe = new SphericalHarmonicsL2[1];

        static bool BuiltInPipeline => GraphicsSettings.currentRenderPipeline == null;

        static bool Lightmapped(Actor a) => _lightmap != null && a.atlas.x > 0;

        /// <summary>A model's renderers that draw a mesh, in the order every copy of it has them.</summary>
        static List<(MeshFilter filter, MeshRenderer renderer)> Parts(GameObject root) {
            var parts = new List<(MeshFilter, MeshRenderer)>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh != null && filter.TryGetComponent<MeshRenderer>(out var renderer)) parts.Add((filter, renderer));
            }
            return parts;
        }

        /// <summary>
        /// A model's triangles in its own space, every part placed where it sits in the model, as
        /// base64 of: the vertex count and the index count (uint32), the positions (three float32 a
        /// vertex), then the indices (uint32), each part's after the last's. What onejs-play unwraps.
        /// "" for a model that cannot be baked into: one with a skinned mesh, a depth-only one, one
        /// drawing points or lines, or one with nothing to draw. It is lit by the probes instead.
        /// </summary>
        public static string LightMesh(int model) {
            if (!Bakeable(model, out var m)) return "";
            var positions = new List<Vector3>();
            var indices = new List<int>();
            foreach (var (filter, _) in Parts(m.template)) {
                var mesh = Unlit(m, filter);
                var local = filter.transform.localToWorldMatrix;
                var at = positions.Count;
                foreach (var v in mesh.vertices) positions.Add(local.MultiplyPoint3x4(v));
                for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                    foreach (var i in mesh.GetTriangles(sub)) indices.Add(at + i);
                }
            }
            var bytes = new byte[8 + positions.Count * 12 + indices.Count * 4];
            BitConverter.TryWriteBytes(new Span<byte>(bytes, 0, 4), (uint)positions.Count);
            BitConverter.TryWriteBytes(new Span<byte>(bytes, 4, 4), (uint)indices.Count);
            var floats = new float[positions.Count * 3];
            for (int i = 0; i < positions.Count; i++) {
                floats[i * 3] = positions[i].x;
                floats[i * 3 + 1] = positions[i].y;
                floats[i * 3 + 2] = positions[i].z;
            }
            Buffer.BlockCopy(floats, 0, bytes, 8, floats.Length * 4);
            Buffer.BlockCopy(indices.ToArray(), 0, bytes, 8 + floats.Length * 4, indices.Count * 4);
            return Convert.ToBase64String(bytes);
        }

        static bool Bakeable(int model, out Model m) {
            if (!_models.TryGetValue(model, out m)) return false;
            if (m.built != null && m.built.depthOnly) return false;
            if (m.template.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return false;
            var parts = Parts(m.template);
            if (parts.Count == 0) return false;
            foreach (var (filter, _) in parts) {
                var mesh = Unlit(m, filter);
                for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) return false;
                }
            }
            return true;
        }

        // The mesh a part had before any unwrap, which LightMesh's indices and an unwrap's xref refer to.
        static Mesh Unlit(Model m, MeshFilter filter) {
            if (m.unlit == null) return filter.sharedMesh;
            var parts = Parts(m.template);
            for (int i = 0; i < parts.Count; i++) if (parts[i].filter == filter) return m.unlit[i];
            return filter.sharedMesh;
        }

        /// <summary>
        /// Rebuilds a model's meshes from onejs-play's unwrap of LightMesh's triangles: for each
        /// output vertex, `xref` names the input vertex it was split from and `uv` (two floats) where
        /// it lies in the model's layout; `indices` are the output triangles in the input's order.
        /// Every attribute is carried over, the layout goes in TEXCOORD1, and the template, its
        /// actors and its scatters all draw the rebuilt meshes. "" when it took them, or what was wrong.
        /// </summary>
        public static string SetLightUnwrap(int model, uint[] xref, float[] uv, uint[] indices) {
            if (!Bakeable(model, out var m)) return $"model {model} cannot be baked into";
            if (xref == null || uv == null || indices == null) return "an unwrap needs xref, uv and indices";
            if (uv.Length != xref.Length * 2) return $"an unwrap has {uv.Length} uv numbers for {xref.Length} vertices";
            var parts = Parts(m.template);
            var sources = new Mesh[parts.Count];
            int vertexTotal = 0, indexTotal = 0;
            for (int p = 0; p < parts.Count; p++) {
                sources[p] = Unlit(m, parts[p].filter);
                vertexTotal += sources[p].vertexCount;
                for (int sub = 0; sub < sources[p].subMeshCount; sub++) indexTotal += (int)sources[p].GetIndexCount(sub);
            }
            if (indices.Length != indexTotal) return $"an unwrap has {indices.Length} indices, and the model has {indexTotal}";

            var lit = new Mesh[parts.Count];
            int vertexBase = 0, indexAt = 0;
            for (int p = 0; p < parts.Count; p++) {
                var source = sources[p];
                var built = Rebuild(source, xref, uv, indices, ref indexAt, vertexBase, out var problem);
                if (built == null) {
                    foreach (var made in lit) if (made != null) Kill(made);
                    return problem;
                }
                lit[p] = built;
                vertexBase += source.vertexCount;
            }

            FreeLit(m);
            m.unlit = sources;
            m.lit = lit;
            Swap(m, model, lit);
            return "";
        }

        /// <summary>One part's mesh again, with the vertices its triangles use split as the unwrap split them.</summary>
        static Mesh Rebuild(Mesh source, uint[] xref, float[] uv, uint[] indices, ref int indexAt, int vertexBase, out string problem) {
            problem = null;
            var count = source.vertexCount;
            var positions = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors;
            var uv0 = new List<Vector2>();
            source.GetUVs(0, uv0);
            bool hasNormals = normals.Length == count, hasTangents = tangents.Length == count, hasColors = colors.Length == count, hasUv0 = uv0.Count == count;

            var map = new Dictionary<uint, int>();
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outColors = new List<Color>();
            var outUv0 = new List<Vector2>();
            var outUv1 = new List<Vector2>();
            var triangles = new int[source.subMeshCount][];
            for (int sub = 0; sub < source.subMeshCount; sub++) {
                var n = (int)source.GetIndexCount(sub);
                var tris = new int[n];
                for (int k = 0; k < n; k++, indexAt++) {
                    var o = indices[indexAt];
                    if (o >= xref.Length) {
                        problem = $"unwrap index {indexAt} is {o}, past its {xref.Length} vertices";
                        return null;
                    }
                    if (!map.TryGetValue(o, out var at)) {
                        long from = (long)xref[o] - vertexBase;
                        if (from < 0 || from >= count) {
                            problem = $"unwrap vertex {o} comes from vertex {xref[o]}, which is not in the part its triangle is in";
                            return null;
                        }
                        var v = (int)from;
                        at = outPositions.Count;
                        map[o] = at;
                        outPositions.Add(positions[v]);
                        if (hasNormals) outNormals.Add(normals[v]);
                        if (hasTangents) outTangents.Add(tangents[v]);
                        if (hasColors) outColors.Add(colors[v]);
                        if (hasUv0) outUv0.Add(uv0[v]);
                        outUv1.Add(new Vector2(uv[o * 2], uv[o * 2 + 1]));
                    }
                    tris[k] = at;
                }
                triangles[sub] = tris;
            }

            var mesh = new Mesh { name = source.name + " (lit)" };
            if (outPositions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(outPositions);
            if (hasNormals) mesh.SetNormals(outNormals);
            if (hasTangents) mesh.SetTangents(outTangents);
            if (hasColors) mesh.SetColors(outColors);
            if (hasUv0) mesh.SetUVs(0, outUv0);
            mesh.SetUVs(1, outUv1);
            mesh.subMeshCount = triangles.Length;
            for (int sub = 0; sub < triangles.Length; sub++) mesh.SetTriangles(triangles[sub], sub, false);
            mesh.bounds = source.bounds;
            if (!Playing) mesh.hideFlags = HideFlags.DontSave;
            return mesh;
        }

        // The template and every actor of the model draw `meshes`, part by part.
        static void Swap(Model m, int model, Mesh[] meshes) {
            var parts = Parts(m.template);
            for (int p = 0; p < parts.Count && p < meshes.Length; p++) parts[p].filter.sharedMesh = meshes[p];
            foreach (var a in _actors.Values) {
                if (a.model != model) continue;
                var own = Parts(a.go);
                for (int p = 0; p < own.Count && p < meshes.Length; p++) own[p].filter.sharedMesh = meshes[p];
            }
        }

        static void FreeLit(Model m) {
            if (m.lit == null) return;
            foreach (var mesh in m.lit) if (mesh != null) Kill(mesh);
            m.lit = null;
        }

        /// <summary>A mesh model given new triangles: back to its own mesh, and its actors unbaked until the next bake.</summary>
        static void DropUnwrap(int model, Model m) {
            if (m.lit == null) return;
            Swap(m, model, m.unlit);
            FreeLit(m);
            m.unlit = null;
            foreach (var a in _actors.Values) {
                if (a.model != model || a.atlas == Vector4.zero) continue;
                a.atlas = Vector4.zero;
                a.probed = false;
                Apply(a);
            }
            foreach (var s in _scatters.Values) {
                if (s.model != model || s.atlas == null) continue;
                s.atlas = null;
                foreach (var chunk in s.chunks) chunk.light = null;
            }
        }

        /// <summary>
        /// Starts baking the scene's light, replacing any bake before it. Each static actor is five
        /// floats in `actors` (its id, then its rectangle of the atlas as a uv scale and offset) and
        /// each scattered copy six in `copies` (the scatter's id, the copy's index, its rectangle);
        /// their models must have been given an unwrap. `grid` is the probe grid's origin, spacing
        /// and three counts. The bake bounces the ambient probe's two hemispheres (what SetAmbient
        /// writes and ModelLit's SampleSH reads) and the sun, and keeps the sun's first hit live.
        /// "" when it started, or why it could not.
        /// </summary>
        public static string BeginBake(float[] actors, float[] copies, float[] grid, int width, int height, float detail, int samples) {
            ClearLight();
            if (actors == null || actors.Length % ActorStride != 0) return "five floats a static actor: its id and its rectangle";
            if (copies == null || copies.Length % CopyStride != 0) return "six floats a copy: its scatter, its index and its rectangle";
            if (grid == null || grid.Length != GridFloats) return "a probe grid is seven floats: origin, spacing and counts";
            if (!LightBaker.Supported(out var why)) return $"this GPU cannot bake light: {why}";

            var scene = new SceneBuilder();
            for (int i = 0; i < actors.Length; i += ActorStride) {
                if (!_actors.TryGetValue((int)actors[i], out var a)) return $"there is no actor {(int)actors[i]}";
                if (!_models.TryGetValue(a.model, out var m) || m.lit == null) return $"actor {(int)actors[i]}'s model has no unwrap";
                var st = new Vector4(actors[i + 1], actors[i + 2], actors[i + 3], actors[i + 4]);
                foreach (var (filter, renderer) in Parts(a.go)) scene.Add(filter.sharedMesh, filter.transform.localToWorldMatrix, renderer.sharedMaterials, st);
                a.atlas = st;
            }
            for (int i = 0; i < copies.Length; i += CopyStride) {
                if (!_scatters.TryGetValue((int)copies[i], out var s)) return $"there is no scatter {(int)copies[i]}";
                if (!_models.TryGetValue(s.model, out var m) || m.lit == null) return $"scatter {(int)copies[i]}'s model has no unwrap";
                var copy = (int)copies[i + 1];
                if (copy < 0 || copy >= s.data.Length / ScatterStride) return $"scatter {(int)copies[i]} has no copy {copy}";
                var st = new Vector4(copies[i + 2], copies[i + 3], copies[i + 4], copies[i + 5]);
                var matrix = CopyMatrix(s.data, copy);
                foreach (var part in s.parts) scene.Add(part.filter.sharedMesh, matrix * part.local, part.materials, st);
                s.atlas ??= new Vector4[s.data.Length / ScatterStride];
                s.atlas[copy] = st;
            }
            if (scene.Empty) return "there is nothing static to bake";

            var bake = scene.Build(width, height);
            bake.GridOrigin = new Vector3(grid[0], grid[1], grid[2]);
            bake.GridSpacing = grid[3];
            bake.GridCounts = new Vector3Int((int)grid[4], (int)grid[5], (int)grid[6]);
            // The ambient probe holds a hemisphere pair as the mean and half the difference (HemisphereProbe).
            var probe = RenderSettings.ambientProbe;
            bake.Sky = new Color(probe[0, 0] + probe[0, 1], probe[1, 0] + probe[1, 1], probe[2, 0] + probe[2, 1], 1);
            bake.Ground = new Color(probe[0, 0] - probe[0, 1], probe[1, 0] - probe[1, 1], probe[2, 0] - probe[2, 1], 1);
            var sun = LiveSun();
            if (sun != null) {
                bake.SunDirection = -sun.transform.forward;
                bake.Sun = sun.color.linear * sun.intensity;
            }
            try {
                _baker = new LightBaker(bake, new BakeSettings { Samples = Math.Max(1, samples), Detail = detail });
            } catch (Exception e) {
                ClearLight();
                return e.Message;
            }
            _gridOrigin = bake.GridOrigin;
            _gridSpacing = bake.GridSpacing;
            _gridCounts = bake.GridCounts;
            return "";
        }

        // The sun the scene is lit by: the bridge's own, or in the edit-mode preview the open scene's.
        static Light LiveSun() {
            if (_sun != null && _sun.isActiveAndEnabled) return _sun;
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            foreach (var light in FindCompat.FindObjectsByType<Light>()) {
                if (light.type == LightType.Directional && light.isActiveAndEnabled) return light;
            }
            return null;
        }

        /// <summary>
        /// Does about `paths` paths of the bake (a path is one ray from a texel or a probe, with its
        /// bounces) and shows what is ready: the probes, then the draft lightmap, then the final.
        /// Returns the bake's progress, 1 once done, or -1 when nothing is baking.
        /// </summary>
        public static float StepBake(double paths) {
            if (_baker == null) return -1;
            var done = _baker.Step((long)Math.Max(1, paths));
            if (_probes == null && _baker.ProbesReady && _baker.ProbeCount > 0) {
                _probes = _baker.ProbeSH;
                Probing(true);
                UpdateProbes();
            }
            if (_baker.Lightmap != null && _lightmap != _baker.Lightmap) ShowLightmap(_baker.Lightmap);
            return done ? 1 : Mathf.Min(_baker.Progress, 0.999f);
        }

        /// <summary>Whether a bake has been started and not finished.</summary>
        public static bool Baking => _baker != null && !_baker.Done;

        /// <summary>The lightmap ModelLit reads now, or null.</summary>
        internal static Texture BakedLightmap => _lightmap;

        /// <summary>Whether every chunk of a scatter draws with its copies' rectangles.</summary>
        internal static bool ScatterDrawsBaked(int scatter) =>
            _scatters.TryGetValue(scatter, out var s) && s.chunks.Length > 0 && Array.TrueForAll(s.chunks, c => c.light != null);

        /// <summary>A scattered copy's rectangle of the atlas, zero when it has none.</summary>
        internal static Vector4 ScatterAtlas(int scatter, int copy) =>
            _scatters.TryGetValue(scatter, out var s) && s.atlas != null && copy >= 0 && copy < s.atlas.Length ? s.atlas[copy] : Vector4.zero;

        static void ShowLightmap(Texture lightmap) {
            var first = _lightmap == null;
            _lightmap = lightmap;
            Shader.SetGlobalTexture(LightmapId, lightmap);
            if (!first) return;
            foreach (var a in _actors.Values) if (a.atlas.x > 0) Apply(a);
            foreach (var s in _scatters.Values) if (s.atlas != null) LightChunks(s);
        }

        // Each chunk's copies' rectangles, as the per-instance values its draw reads.
        static void LightChunks(Scattered s) {
            foreach (var chunk in s.chunks) {
                var st = new Vector4[chunk.copies.Length];
                for (int k = 0; k < st.Length; k++) st[k] = s.atlas[chunk.copies[k]];
                chunk.light = new MaterialPropertyBlock();
                chunk.light.SetVectorArray(ActorLightmapId, st);
                if (BuiltInPipeline) chunk.light.CopySHCoefficientArraysFrom(new SphericalHarmonicsL2[st.Length]);
            }
        }

        /// <summary>Forgets the baked light: every actor and copy draws with the scene's ambient again.</summary>
        public static void ClearLight() {
            _baker?.Dispose();
            _baker = null;
            var had = _lightmap != null || _probes != null;
            _lightmap = null;
            _probes = null;
            Probing(false);
            Shader.SetGlobalTexture(LightmapId, Texture2D.blackTexture);
            foreach (var a in _actors.Values) {
                var touched = a.atlas != Vector4.zero || a.probed;
                a.atlas = Vector4.zero;
                a.probed = false;
                if (touched && had && a.go != null) Apply(a);
            }
            foreach (var s in _scatters.Values) {
                s.atlas = null;
                foreach (var chunk in s.chunks) chunk.light = null;
            }
        }

        // Only while there are probes, so a scene that never bakes has no callback at all.
        static void Probing(bool on) {
            if (on == _probing) return;
            _probing = on;
            if (on) {
                RenderPipelineManager.beginContextRendering += ProbesFor;
                Camera.onPreCull += ProbesFor;
            } else {
                RenderPipelineManager.beginContextRendering -= ProbesFor;
                Camera.onPreCull -= ProbesFor;
            }
        }

        static void ProbesFor(ScriptableRenderContext _, List<Camera> __) => UpdateProbes();
        static void ProbesFor(Camera _) => UpdateProbes();

        /// <summary>
        /// Every actor not drawn from the lightmap takes the light at its middle from the eight
        /// probes round it, when it has moved a millimetre since it last did. Before the draft that
        /// is every actor, so the scene is lit by its probes within a few frames of a bake starting.
        /// </summary>
        internal static void UpdateProbes() {
            if (_probes == null) return;
            foreach (var a in _actors.Values) {
                if (a.go == null || Lightmapped(a) || !_models.TryGetValue(a.model, out var m)) continue;
                var middle = a.go.transform.TransformPoint(m.bounds.center);
                if (a.probed && (middle - a.probedAt).sqrMagnitude < 1e-6f) continue;
                a.probe = BlendProbes(middle);
                a.probed = true;
                a.probedAt = middle;
                Apply(a);
            }
        }

        /// <summary>The light at `at`, blended from the eight probes round it; outside the grid, the nearest edge's.</summary>
        internal static SphericalHarmonicsL2 BlendProbes(Vector3 at) {
            var sh = new SphericalHarmonicsL2();
            if (_probes == null) return sh;
            var cell = new int[3];
            var frac = new float[3];
            for (int k = 0; k < 3; k++) {
                var n = _gridCounts[k];
                var t = Mathf.Clamp((at[k] - _gridOrigin[k]) / _gridSpacing, 0, n - 1);
                cell[k] = Math.Min((int)Math.Floor(t), Math.Max(0, n - 2));
                frac[k] = n == 1 ? 0 : t - cell[k];
            }
            for (int corner = 0; corner < 8; corner++) {
                float w = 1;
                var index = new int[3];
                for (int k = 0; k < 3; k++) {
                    var up = (corner >> k) & 1;
                    w *= up == 1 ? frac[k] : 1 - frac[k];
                    index[k] = Math.Min(cell[k] + up, _gridCounts[k] - 1);
                }
                if (w == 0) continue;
                var at0 = (index[0] + _gridCounts.x * (index[1] + _gridCounts.y * index[2])) * ProbeSolve.Floats;
                for (int c = 0; c < 3; c++) {
                    for (int i = 0; i < 9; i++) sh[c, i] += w * _probes[at0 + c * 9 + i];
                }
            }
            return sh;
        }

        /// <summary>The scene's static triangles in world space, at their atlas uv, with each triangle's albedo.</summary>
        sealed class SceneBuilder {
            readonly List<Vector3> _positions = new();
            readonly List<Vector3> _normals = new();
            readonly List<Vector2> _uvs = new();
            readonly List<int> _indices = new();
            readonly List<Color> _albedo = new();
            readonly Dictionary<Texture, Color> _means = new();
            readonly List<Vector2> _layout = new();

            public bool Empty => _indices.Count == 0;

            public void Add(Mesh mesh, Matrix4x4 world, Material[] materials, Vector4 st) {
                var normalMatrix = world.inverse.transpose;
                var at = _positions.Count;
                var normals = mesh.normals;
                var colors = mesh.colors;
                var vertices = mesh.vertices;
                mesh.GetUVs(1, _layout);
                for (int v = 0; v < vertices.Length; v++) {
                    _positions.Add(world.MultiplyPoint3x4(vertices[v]));
                    _normals.Add(normals.Length == vertices.Length ? normalMatrix.MultiplyVector(normals[v]).normalized : Vector3.up);
                    var uv = v < _layout.Count ? _layout[v] : Vector2.zero;
                    _uvs.Add(new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w));
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                    var material = sub < materials.Length ? materials[sub] : null;
                    var surface = Albedo(material);
                    var vertexColors = material != null && material.IsKeywordEnabled("_VERTEX_COLOR") && colors.Length == vertices.Length;
                    var tris = mesh.GetTriangles(sub);
                    for (int k = 0; k < tris.Length; k += 3) {
                        _indices.Add(at + tris[k]);
                        _indices.Add(at + tris[k + 1]);
                        _indices.Add(at + tris[k + 2]);
                        var c = surface;
                        if (vertexColors) c *= (colors[tris[k]] + colors[tris[k + 1]] + colors[tris[k + 2]]) / 3f;
                        _albedo.Add(c);
                    }
                }
            }

            /// <summary>
            /// A material's diffuse colour, linear: its base colour times its base map's mean, less the
            /// part that is metal (ModelLit's metallic is `_Metallic` times its map's blue), which
            /// reflects rather than scatters and so bounces nothing a lightmap holds.
            /// </summary>
            Color Albedo(Material material) {
                if (material == null) return Color.gray;
                var c = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : material.color;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.linear;
                var map = material.HasProperty(BaseMapId) ? material.GetTexture(BaseMapId) : null;
                if (map != null) c *= Mean(map);
                if (material.HasProperty(MetallicId)) {
                    var metal = material.GetTexture(MetallicMapId);
                    c *= 1 - material.GetFloat(MetallicId) * (metal != null ? Mean(metal).b : 1);
                }
                c.a = 1;
                return c;
            }

            // A texture's mean colour, linear: drawn into 16 by 16 through its mips, read back and averaged.
            Color Mean(Texture texture) {
                if (_means.TryGetValue(texture, out var mean)) return mean;
                var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                Graphics.Blit(texture, rt);
                var read = new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0, false);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                var sum = Color.clear;
                foreach (var p in read.GetPixels()) sum += p;
                Kill(read);
                mean = sum / 256f;
                _means[texture] = mean;
                return mean;
            }

            public BakeScene Build(int width, int height) => new BakeScene {
                Positions = _positions.ToArray(), Normals = _normals.ToArray(), Uvs = _uvs.ToArray(),
                Indices = _indices.ToArray(), Albedo = _albedo.ToArray(), AtlasWidth = width, AtlasHeight = height,
            };
        }
    }
}
