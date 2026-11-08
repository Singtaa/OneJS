using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OneJS.Lighting;
using OneJS.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using LightFileFormat = OneJS.Lighting.LightFile;
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
    /// The lightmap is the global `_OjLightmap`, a texture array of up to 4 pages cut from one
    /// sheet (Lighting/LightSheet.cs, `_OjLightmapSeams`). Each baked actor reads its rectangle of
    /// the sheet from `_ActorLightmapST` once the last page it touches has a draft, and every other
    /// actor takes the light at its middle from the probes.
    /// </summary>
    public static partial class ModelBridge {
        static readonly int ActorLightmapId = Shader.PropertyToID("_ActorLightmapST");
        static readonly int LightmapId = Shader.PropertyToID("_OjLightmap");
        static readonly int LightmapSeamsId = Shader.PropertyToID("_OjLightmapSeams");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MetallicMapId = Shader.PropertyToID("_MetallicRoughnessMap");
        const int ActorStride = 5, CopyStride = 6, GridFloats = 7;

        static LightBaker _baker;
        // A bake BeginBake accepted that waits on its textures' means before its baker can start.
        static WaitingBake _waiting;
        // The finished bake's light file.
        static string _file;
        // Whether this bake's failed lightmap read has warned: LightFile reads again on each call, and says so once.
        static bool _fileReadWarned;
        // The lightmap ModelLit reads now, a slice a page: the bake's, drafts then finals, or a file's. Null until the first draft.
        static Texture _lightmap;
        // How many of its pages, from the first, have light: an actor reading a page past them is lit by the probes.
        static int _pagesLit;
        // The pages' sheet: which page a rectangle reads last.
        static LightSheet _sheet = new LightSheet(1, 1, 1);
        // A lightmap read from a light file (LoadLight), which the bridge owns; a bake's is the baker's.
        static Texture2DArray _loaded;
        // What _OjLightmap holds while there is no lightmap: ModelLit declares an array, and a 2D texture there is an error on some backends.
        static Texture2DArray _noLight;
        // The finished bake's pages' reads, which LightFile waits on.
        static TextureRead[] _fileReads;
        // The probes' 27 floats each, as LightBaker.ProbeSH lays them out, and their grid. Null until read.
        static float[] _probes;
        static Vector3 _gridOrigin;
        static float _gridSpacing;
        static Vector3Int _gridCounts;
        static bool _probing;
        static readonly SphericalHarmonicsL2[] _oneProbe = new SphericalHarmonicsL2[1];

        static bool BuiltInPipeline => GraphicsSettings.currentRenderPipeline == null;

        static bool Lightmapped(Actor a) => _lightmap != null && a.atlas.x > 0 && _sheet.LastPage(a.atlas) < _pagesLit;

        static Texture NoLight {
            get {
                if (_noLight != null) return _noLight;
                _noLight = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false, true) { name = "OneJS No Light", hideFlags = HideFlags.HideAndDontSave };
                _noLight.SetPixels(new[] { Color.clear }, 0);
                _noLight.Apply(false, true);
                return _noLight;
            }
        }

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
        /// floats in `actors` (its id, then its rectangle of the atlas's sheet as a uv scale and
        /// offset, in pages: LightSheet) and each scattered copy six in `copies` (the scatter's id,
        /// the copy's index, its rectangle); their models must have been given an unwrap. The atlas
        /// is `pages` pages of `width` by `height`, 1, 2 or 4. `grid` is the probe grid's origin, spacing
        /// and three counts. The bake bounces the ambient probe's two hemispheres (what SetAmbient
        /// writes and ModelLit's SampleSH reads) and the sun, and keeps the sun's first hit live:
        /// `samples` paths a texel, of up to `bounces` hits, with `occlusion` (BakeSettings).
        /// "" when it started, or why it could not.
        /// </summary>
        public static string BeginBake(float[] actors, float[] copies, float[] grid, int width, int height, int pages, float detail, int samples, int bounces, float occlusion) {
            ClearLight();
            if (actors == null || actors.Length % ActorStride != 0) return "five floats a static actor: its id and its rectangle";
            if (copies == null || copies.Length % CopyStride != 0) return "six floats a copy: its scatter, its index and its rectangle";
            if (grid == null || grid.Length != GridFloats) return "a probe grid is seven floats: origin, spacing and counts";
            if (!LightBaker.Supported(out var why)) return $"this GPU cannot bake light: {why}";

            var scene = new SceneBuilder();
            var problem = Place(actors, copies, width, height, pages, scene);
            if (problem != "") {
                ClearLight();
                return problem;
            }
            if (scene.Empty) return "there is nothing static to bake";

            var bake = scene.Build(width, height, pages);
            var settings = new BakeSettings { Samples = samples, Bounces = bounces, Occlusion = occlusion, Detail = detail };
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
                LightBaker.Validate(bake, settings);
            } catch (ArgumentException e) {
                ClearLight();
                return e.Message;
            }
            _gridOrigin = bake.GridOrigin;
            _gridSpacing = bake.GridSpacing;
            _gridCounts = bake.GridCounts;
            // The albedo waits on each texture's mean, which on WebGPU is read back a frame or more later.
            scene.ReadMeans();
            _waiting = new WaitingBake { scene = scene, bake = bake, settings = settings };
            problem = StartWaiting();
            if (problem != "") ClearLight();
            return problem;
        }

        sealed class WaitingBake {
            public SceneBuilder scene;
            public BakeScene bake;
            public BakeSettings settings;
        }

        // Starts the waiting bake's baker once its means have landed: "" when it started or still waits, or why it could not.
        static string StartWaiting() {
            if (_waiting == null || !_waiting.scene.MeansReady) return "";
            var w = _waiting;
            _waiting = null;
            w.scene.FillAlbedo(w.bake.Albedo);
            w.scene.Dispose();
            try {
                _baker = new LightBaker(w.bake, w.settings);
            } catch (Exception e) {
                return e.Message;
            }
            return "";
        }

        /// <summary>
        /// Gives each static actor and copy its rectangle of the atlas, as BeginBake and LoadLight
        /// read them, and hands its triangles to `scene` when there is one. "" or why not.
        /// </summary>
        static string Place(float[] actors, float[] copies, int width, int height, int pages, SceneBuilder scene) {
            try {
                _sheet = new LightSheet(width, height, pages);
            } catch (ArgumentException e) {
                return e.Message;
            }
            for (int i = 0; i < actors.Length; i += ActorStride) {
                if (!_actors.TryGetValue((int)actors[i], out var a)) return $"there is no actor {(int)actors[i]}";
                if (!_models.TryGetValue(a.model, out var m) || m.lit == null) return $"actor {(int)actors[i]}'s model has no unwrap";
                var st = new Vector4(actors[i + 1], actors[i + 2], actors[i + 3], actors[i + 4]);
                if (!_sheet.Holds(st)) return $"actor {(int)actors[i]}'s rectangle {st} is past its {pages} pages";
                if (scene != null) {
                    foreach (var (filter, renderer) in Parts(a.go)) scene.Add(filter.sharedMesh, filter.transform.localToWorldMatrix, renderer.sharedMaterials, st);
                }
                a.atlas = st;
            }
            for (int i = 0; i < copies.Length; i += CopyStride) {
                if (!_scatters.TryGetValue((int)copies[i], out var s)) return $"there is no scatter {(int)copies[i]}";
                if (!_models.TryGetValue(s.model, out var m) || m.lit == null) return $"scatter {(int)copies[i]}'s model has no unwrap";
                var copy = (int)copies[i + 1];
                if (copy < 0 || copy >= s.data.Length / ScatterStride) return $"scatter {(int)copies[i]} has no copy {copy}";
                var st = new Vector4(copies[i + 2], copies[i + 3], copies[i + 4], copies[i + 5]);
                if (!_sheet.Holds(st)) return $"scatter {(int)copies[i]}'s copy {copy}'s rectangle {st} is past its {pages} pages";
                if (scene != null) {
                    var matrix = CopyMatrix(s.data, copy);
                    foreach (var part in s.parts) scene.Add(part.filter.sharedMesh, matrix * part.local, part.materials, st);
                }
                s.atlas ??= new Vector4[s.data.Length / ScatterStride];
                s.atlas[copy] = st;
            }
            return "";
        }

        /// <summary>
        /// The finished bake as a light file (Lighting/LightFile.cs), base64: the lightmap's pages and
        /// the probes, which onejs-play saves with its record. "" while nothing has finished baking.
        /// </summary>
        public static string LightFile() {
            if (_baker == null || !_baker.Done || _baker.Lightmap == null) return "";
            if (_file != null) return _file;
            var map = _baker.Lightmap;
            var pages = _baker.Pages;
            // On WebGPU each page's read lands a frame or more after it starts, and until then this is "".
            if (_fileReads == null) {
                _fileReads = new TextureRead[pages];
                for (var p = 0; p < pages; p++) _fileReads[p] = TextureRead.Start(map, p);
            }
            foreach (var r in _fileReads) if (!r.Done) return "";
            var reads = _fileReads;
            _fileReads = null;
            var light = new Color[map.width * map.height * pages];
            for (var p = 0; p < pages; p++) {
                if (reads[p].Error != null) {
                    if (!_fileReadWarned) Debug.LogWarning($"[OneJS] The bake's lightmap could not be read back, and is read again until it is: {reads[p].Error}.");
                    _fileReadWarned = true;
                    return "";
                }
                reads[p].Colors().CopyTo(light, p * map.width * map.height);
            }
            _file = Convert.ToBase64String(LightFileFormat.Encode(light, map.width, map.height, pages, _baker.ProbeSH));
            return _file;
        }

        /// <summary>
        /// The SHA-256 of `bytes` as 64 lowercase hex digits: how onejs-play's light record names a
        /// model the build's file map does not. QuickJS has no crypto.subtle, and the digest in JS
        /// takes about half a millisecond a kilobyte there, seconds for a large model; a typed
        /// array crosses as its bytes, so here it is the copy and a native hash.
        /// </summary>
        public static string Sha256(byte[] bytes) {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
        }

        /// <summary>
        /// The SHA-256 of the file at `path`, as Sha256 gives it, read here so that a model on
        /// disk (a Unity project's) is hashed without its bytes crossing into JS at all.
        /// </summary>
        public static string FileSha256(string path) {
            using var sha = SHA256.Create();
            using var file = File.OpenRead(path);
            return Hex(sha.ComputeHash(file));
        }

        static string Hex(byte[] digest) {
            var text = new StringBuilder(digest.Length * 2);
            foreach (var b in digest) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        /// <summary>
        /// Shows a saved bake instead of baking: `file` is a light file as base64 (LightFile), and
        /// the rest are as BeginBake takes them, for the scene the file was baked for. Replaces any
        /// bake before it. "" when the light is showing, or why not, and then nothing is lit by it.
        /// </summary>
        public static string LoadLight(string file, float[] actors, float[] copies, float[] grid, int width, int height, int pages) {
            ClearLight();
            if (actors == null || actors.Length % ActorStride != 0) return "five floats a static actor: its id and its rectangle";
            if (copies == null || copies.Length % CopyStride != 0) return "six floats a copy: its scatter, its index and its rectangle";
            if (grid == null || grid.Length != GridFloats) return "a probe grid is seven floats: origin, spacing and counts";
            byte[] png;
            try {
                png = Convert.FromBase64String(file ?? "");
            } catch (FormatException) {
                return "the light file is not base64";
            }
            var counts = new Vector3Int((int)grid[4], (int)grid[5], (int)grid[6]);
            var problem = LightFileFormat.Decode(png, width, height, pages, counts.x * counts.y * counts.z, out var light, out var probes);
            if (problem == "") problem = Place(actors, copies, width, height, pages, null);
            if (problem != "") {
                if (light != null) Kill(light);
                ClearLight();
                return problem;
            }
            _loaded = light;
            _gridOrigin = new Vector3(grid[0], grid[1], grid[2]);
            _gridSpacing = grid[3];
            _gridCounts = counts;
            if (probes.Length > 0) {
                _probes = probes;
                Probing(true);
            }
            ShowLightmap(light, pages);
            UpdateProbes();
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
        /// Does `tiles` tiles of the bake (LightBaker.TilePaths paths each; a path is one ray from a
        /// texel or a probe, with its bounces), at least one, and shows what is ready: the probes,
        /// then each page's draft lightmap, then its final.
        /// Returns the bake's progress, 1 once done (the probes read back too), or -1 when nothing is
        /// baking. 0 while the bake waits on its textures' means.
        /// </summary>
        public static float StepBake(double tiles) {
            if (_waiting != null) {
                if (!_waiting.scene.MeansReady) return 0;
                var problem = StartWaiting();
                if (problem != "") {
                    Debug.LogError($"[OneJS] The bake could not start: {problem}");
                    ClearLight();
                    return -1;
                }
            }
            if (_baker == null) return -1;
            bool done;
            try {
                done = _baker.Step((long)Math.Max(1, Math.Round(tiles)) * _baker.TilePaths);
            } catch (InvalidOperationException e) {
                OneJSLog.Exception("[OneJS] The bake stopped", e);
                ClearLight();
                return -1;
            }
            if (_probes == null && _baker.ProbesReady && _baker.ProbeCount > 0) {
                _probes = _baker.ProbeSH;
                Probing(true);
                UpdateProbes();
            }
            if (_baker.Lightmap != null && (_lightmap != _baker.Lightmap || _pagesLit != _baker.PagesLit)) ShowLightmap(_baker.Lightmap, _baker.PagesLit);
            return done ? 1 : Mathf.Min(_baker.Progress, 0.999f);
        }

        /// <summary>
        /// What the bake's steps do now, as LightBaker.Phase's number: 0 none (or waiting on its
        /// textures' means), 1 the probes, 2 the lightmap while the probes' read is in flight, 3 the
        /// lightmap, 4 finishing. The frame budget starts over when it changes.
        /// </summary>
        public static int BakePhase() => _baker == null ? (int)global::OneJS.Lighting.BakePhase.None : (int)_baker.Phase;

        /// <summary>The page the bake's lightmap is on, from 0. The frame budget starts over when it changes, as at a phase.</summary>
        public static int BakePage() => _baker == null ? 0 : _baker.Page;

        /// <summary>Whether a bake has been started and not finished.</summary>
        public static bool Baking => _waiting != null || (_baker != null && !_baker.Done);

        /// <summary>The lightmap ModelLit reads now, or null.</summary>
        internal static Texture BakedLightmap => _lightmap;

        /// <summary>Whether every chunk of a scatter draws with its copies' rectangles.</summary>
        internal static bool ScatterDrawsBaked(int scatter) =>
            _scatters.TryGetValue(scatter, out var s) && s.chunks.Length > 0 && Array.TrueForAll(s.chunks, c => c.light != null);

        /// <summary>A scattered copy's rectangle of the atlas, zero when it has none.</summary>
        internal static Vector4 ScatterAtlas(int scatter, int copy) =>
            _scatters.TryGetValue(scatter, out var s) && s.atlas != null && copy >= 0 && copy < s.atlas.Length ? s.atlas[copy] : Vector4.zero;

        // Shows `lightmap` with its first `pagesLit` pages lit: the actors and chunks whose last page was not lit before draw from it now.
        static void ShowLightmap(Texture lightmap, int pagesLit) {
            var before = _lightmap == null ? 0 : _pagesLit;
            _lightmap = lightmap;
            _pagesLit = pagesLit;
            Shader.SetGlobalTexture(LightmapId, lightmap);
            Shader.SetGlobalVector(LightmapSeamsId, _sheet.Seams);
            if (pagesLit == before) return;
            foreach (var a in _actors.Values) {
                if (a.atlas.x > 0 && _sheet.LastPage(a.atlas) >= before && _sheet.LastPage(a.atlas) < pagesLit) Apply(a);
            }
            foreach (var s in _scatters.Values) if (s.atlas != null) LightChunks(s);
        }

        // Each chunk's copies' rectangles, as the per-instance values its draw reads, once every page they read is lit.
        static void LightChunks(Scattered s) {
            foreach (var chunk in s.chunks) {
                if (chunk.light != null) continue;
                var st = new Vector4[chunk.copies.Length];
                var lit = true;
                for (int k = 0; k < st.Length; k++) {
                    st[k] = s.atlas[chunk.copies[k]];
                    if (st[k].x > 0 && _sheet.LastPage(st[k]) >= _pagesLit) lit = false;
                }
                if (!lit) continue;
                chunk.light = new MaterialPropertyBlock();
                chunk.light.SetVectorArray(ActorLightmapId, st);
                if (BuiltInPipeline) chunk.light.CopySHCoefficientArraysFrom(new SphericalHarmonicsL2[st.Length]);
            }
        }

        /// <summary>Forgets the baked light: every actor and copy draws with the scene's ambient again.</summary>
        public static void ClearLight() {
            _baker?.Dispose();
            _baker = null;
            _waiting?.scene.Dispose();
            _waiting = null;
            _fileReads = null;
            _file = null;
            _fileReadWarned = false;
            if (_loaded != null) Kill(_loaded);
            _loaded = null;
            var had = _lightmap != null || _probes != null;
            _lightmap = null;
            _pagesLit = 0;
            _sheet = new LightSheet(1, 1, 1);
            _probes = null;
            Probing(false);
            Shader.SetGlobalTexture(LightmapId, NoLight);
            Shader.SetGlobalVector(LightmapSeamsId, _sheet.Seams);
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
            _probedFrame = -1;
            if (on) {
                RenderPipelineManager.beginContextRendering += ProbesFor;
                Camera.onPreCull += ProbesFor;
            } else {
                RenderPipelineManager.beginContextRendering -= ProbesFor;
                Camera.onPreCull -= ProbesFor;
            }
        }

        static void ProbesFor(ScriptableRenderContext _, List<Camera> __) => ProbesOnce();
        static void ProbesFor(Camera _) => ProbesOnce();

        // The frame the render callbacks last updated the probes in. They fire once a camera
        // (onPreCull) or a render request (beginContextRendering), and scene.texture adds a
        // camera, so a frame would otherwise blend every moved actor's probes once per camera.
        static int _probedFrame = -1;

        static void ProbesOnce() {
            if (Time.frameCount == _probedFrame) return;
            _probedFrame = Time.frameCount;
            UpdateProbes();
        }

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
            // Per triangle: which of _materials it is drawn with, and its vertex colours' mean (white without).
            readonly List<int> _surface = new();
            readonly List<Color> _tint = new();
            readonly List<Material> _materials = new();
            readonly Dictionary<Material, int> _materialIndex = new();
            // Each texture's mean, read back from a 16 by 16 copy drawn through its mips.
            readonly Dictionary<Texture, (RenderTexture rt, TextureRead read)> _means = new();
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
                    var surface = SurfaceOf(material);
                    var vertexColors = material != null && material.IsKeywordEnabled("_VERTEX_COLOR") && colors.Length == vertices.Length;
                    var tris = mesh.GetTriangles(sub);
                    for (int k = 0; k < tris.Length; k += 3) {
                        _indices.Add(at + tris[k]);
                        _indices.Add(at + tris[k + 1]);
                        _indices.Add(at + tris[k + 2]);
                        _surface.Add(surface);
                        _tint.Add(vertexColors ? (colors[tris[k]] + colors[tris[k + 1]] + colors[tris[k + 2]]) / 3f : Color.white);
                    }
                }
            }

            int SurfaceOf(Material material) {
                // A null material is a surface too, the gray Albedo gives it, kept under index -1.
                if (material == null) return -1;
                if (_materialIndex.TryGetValue(material, out var i)) return i;
                _materialIndex[material] = _materials.Count;
                _materials.Add(material);
                return _materials.Count - 1;
            }

            /// <summary>Starts reading back the mean of every texture the albedo needs: base maps, and metallic maps where a material is metal.</summary>
            public void ReadMeans() {
                foreach (var material in _materials) {
                    if (material.HasProperty(BaseMapId)) StartMean(material.GetTexture(BaseMapId));
                    if (material.HasProperty(MetallicId)) StartMean(material.GetTexture(MetallicMapId));
                }
            }

            void StartMean(Texture texture) {
                if (texture == null || _means.ContainsKey(texture)) return;
                var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(texture, rt);
                _means[texture] = (rt, TextureRead.Start(rt));
            }

            /// <summary>Whether every mean ReadMeans started has landed.</summary>
            public bool MeansReady {
                get {
                    foreach (var (_, read) in _means.Values) if (!read.Done) return false;
                    return true;
                }
            }

            /// <summary>Each triangle's albedo, once MeansReady: its material's, times its vertex colours' mean.</summary>
            public void FillAlbedo(Color[] albedo) {
                var surfaces = new Color[_materials.Count];
                for (int i = 0; i < surfaces.Length; i++) surfaces[i] = Albedo(_materials[i]);
                for (int t = 0; t < albedo.Length; t++) {
                    var c = (_surface[t] < 0 ? Albedo(null) : surfaces[_surface[t]]) * _tint[t];
                    c.a = 1;
                    albedo[t] = c;
                }
            }

            /// <summary>Gives back the means' render textures.</summary>
            public void Dispose() {
                foreach (var (rt, _) in _means.Values) RenderTexture.ReleaseTemporary(rt);
                _means.Clear();
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

            // A texture's mean colour, linear, from its read (ReadMeans). White when the read failed, so the material's colour stands alone.
            Color Mean(Texture texture) {
                if (!_means.TryGetValue(texture, out var mean)) return Color.white;
                var colors = mean.read.Colors();
                if (colors == null) {
                    Debug.LogWarning($"[OneJS] The bake could not read {texture.name}'s mean colour back ({mean.read.Error}), and bounces its material's colour alone.");
                    return Color.white;
                }
                var sum = Color.clear;
                foreach (var p in colors) sum += p;
                return sum / colors.Length;
            }

            /// <summary>The scene, its albedo left for FillAlbedo.</summary>
            public BakeScene Build(int width, int height, int pages) => new BakeScene {
                Positions = _positions.ToArray(), Normals = _normals.ToArray(), Uvs = _uvs.ToArray(),
                Indices = _indices.ToArray(), Albedo = new Color[_surface.Count], AtlasWidth = width, AtlasHeight = height, Pages = pages,
            };
        }
    }
}
