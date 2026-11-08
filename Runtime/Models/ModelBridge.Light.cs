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
        const int ActorStride = 5, CopyStride = 6, GridFloats = 7;

        // The bake under way, from its wait on its textures' means to its light file, or null.
        static BakeSession _session;
        // Why the last bake failed on the bridge, for BakeProblem: kept past the ClearLight that follows the failure.
        static string _bakeProblem;
        // The lightmap ModelLit reads now, a slice a page: the bake's, drafts then finals, or one the bridge owns. Null until the first draft.
        static Texture _lightmap;
        // The bridge's own lightmap, released with the light: a file's (LoadLight), or one kept from a bake a same-layout rebake replaced.
        static Texture _owned;
        // While true, the light showing is kept from before the bake under way, which shows its own once every page has light.
        static bool _keeping;
        // How many of its pages, from the first, have light: an actor reading a page past them is lit by the probes.
        static int _pagesLit;
        // The pages' sheet, as BeginBake or LoadLight was given it: which page a rectangle reads last.
        static LightSheet _sheet = new LightSheet(1, 1, 1);
        static (int width, int height, int pages) _sheetSize;
        // What _OjLightmap holds while there is no lightmap: ModelLit declares an array, and a 2D texture there is an error on some backends.
        static Texture2DArray _noLight;
        // The probes showing, or null, and the grid the bake under way or the file showing stands its probes on.
        static ProbeField _probes;
        static Vector3 _gridOrigin, _gridSpacing;
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
            // A splat's weights (ModelBridge.Ground.cs), four a vertex.
            var uv2 = new List<Vector4>();
            source.GetUVs(2, uv2);
            bool hasNormals = normals.Length == count, hasTangents = tangents.Length == count, hasColors = colors.Length == count, hasUv0 = uv0.Count == count;
            bool hasUv2 = uv2.Count == count;

            var map = new Dictionary<uint, int>();
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outColors = new List<Color>();
            var outUv0 = new List<Vector2>();
            var outUv1 = new List<Vector2>();
            var outUv2 = new List<Vector4>();
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
                        if (hasUv2) outUv2.Add(uv2[v]);
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
            if (hasUv2) mesh.SetUVs(2, outUv2);
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
        /// is `pages` pages of `width` by `height`, 1, 2 or 4. `grid` is the probe grid: its origin,
        /// its spacing and its three counts (seven floats), or its origin, a spacing an axis and its
        /// counts (nine: ProbeGridFloats). The bake bounces the ambient probe's two hemispheres (what
        /// SetAmbient writes and ModelLit's SampleSH reads) and the sun, and keeps the sun's first hit
        /// live: `samples` paths a texel, of up to `bounces` hits, with `occlusion` (BakeSettings).
        ///
        /// When the light showing was laid out exactly as this bake is (every rectangle, the sheet
        /// and the grid: what a rebake after a change of sun or ambient gives), it stays showing
        /// until this bake has light on every page, and its probes until this bake's land, rather
        /// than the scene dropping to its ambient for the seconds a draft takes (review L1). A bake
        /// laid out otherwise clears it first, since its rectangles no longer belong to those surfaces.
        /// "" when it started, or why it could not, and then nothing is lit by baked light.
        /// </summary>
        public static string BeginBake(float[] actors, float[] copies, float[] grid, int width, int height, int pages, float detail, int samples, int bounces, float occlusion) {
            _bakeProblem = null;
            var problem = Shapes(actors, copies, grid, out var origin, out var spacing, out var counts);
            if (problem == "" && Showing && SameLayout(actors, copies, origin, spacing, counts, width, height, pages)) KeepShowing();
            else ClearLight();
            if (problem != "") return Refuse(problem);
            if (!LightBaker.Supported(out var why)) return Refuse($"this GPU cannot bake light: {why}");

            var scene = new BakeSceneBuilder();
            problem = Place(actors, copies, width, height, pages, scene);
            if (problem == "" && scene.Empty) problem = "there is nothing static to bake";
            if (problem != "") {
                scene.Dispose();
                return Refuse(problem);
            }

            var bake = scene.Build(width, height, pages);
            var settings = new BakeSettings { Samples = samples, Bounces = bounces, Occlusion = occlusion, Detail = detail };
            bake.GridOrigin = origin;
            bake.GridSpacing = spacing;
            bake.GridCounts = counts;
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
                scene.Dispose();
                return Refuse(e.Message);
            }
            _gridOrigin = origin;
            _gridSpacing = spacing;
            _gridCounts = counts;
            // The albedo waits on each texture's mean, which on WebGPU is read back a frame or more later.
            _session = new BakeSession(scene, bake, settings);
            if (!_session.TryStart() && _session.Problem != null) return Refuse(_session.Problem);
            return "";
        }

        // Clears what a refused bake or file left, and says why.
        static string Refuse(string problem) {
            ClearLight();
            return problem;
        }

        /// <summary>The probe grids BeginBake and LoadLight take, at most: 7 floats (one spacing), or 9 (a spacing an axis) from this OneJS on.</summary>
        public static int ProbeGridFloats() => 9;

        // Whether the arrays are the lengths a bake's are, and the grid they give: "" or why not.
        static string Shapes(float[] actors, float[] copies, float[] grid, out Vector3 origin, out Vector3 spacing, out Vector3Int counts) {
            origin = spacing = Vector3.zero;
            counts = Vector3Int.zero;
            if (actors == null || actors.Length % ActorStride != 0) return "five floats a static actor: its id and its rectangle";
            if (copies == null || copies.Length % CopyStride != 0) return "six floats a copy: its scatter, its index and its rectangle";
            if (grid == null || (grid.Length != 7 && grid.Length != 9)) return "a probe grid is seven floats (origin, spacing and counts) or nine (origin, a spacing an axis and counts)";
            origin = new Vector3(grid[0], grid[1], grid[2]);
            var at = grid.Length == 7 ? 4 : 6;
            spacing = grid.Length == 7 ? Vector3.one * grid[3] : new Vector3(grid[3], grid[4], grid[5]);
            counts = new Vector3Int((int)grid[at], (int)grid[at + 1], (int)grid[at + 2]);
            return ProbeField.Problem(spacing, counts) ?? "";
        }

        // Whether any baked light or probe light is showing.
        static bool Showing => _lightmap != null || _probes != null;

        /// <summary>Whether `actors` and `copies` are every rectangle showing now, on this sheet, with these probes' grid.</summary>
        static bool SameLayout(float[] actors, float[] copies, Vector3 origin, Vector3 spacing, Vector3Int counts, int width, int height, int pages) {
            if (_sheetSize != (width, height, pages) || origin != _gridOrigin || spacing != _gridSpacing || counts != _gridCounts) return false;
            var showing = 0;
            foreach (var a in _actors.Values) if (a.atlas != Vector4.zero) showing++;
            if (showing != actors.Length / ActorStride) return false;
            for (int i = 0; i < actors.Length; i += ActorStride) {
                if (!_actors.TryGetValue((int)actors[i], out var a) || !SameRect(a.atlas, actors, i + 1)) return false;
            }
            showing = 0;
            foreach (var s in _scatters.Values) if (s.atlas != null) foreach (var st in s.atlas) if (st != Vector4.zero) showing++;
            if (showing != copies.Length / CopyStride) return false;
            for (int i = 0; i < copies.Length; i += CopyStride) {
                if (!_scatters.TryGetValue((int)copies[i], out var s) || s.atlas == null) return false;
                var copy = (int)copies[i + 1];
                if (copy < 0 || copy >= s.atlas.Length || !SameRect(s.atlas[copy], copies, i + 2)) return false;
            }
            return true;
        }

        static bool SameRect(Vector4 st, float[] values, int at) =>
            st.x == values[at] && st.y == values[at + 1] && st.z == values[at + 2] && st.w == values[at + 3];

        // The light showing becomes the bridge's own, so it outlives the bake that made it, and stays until the next bake has its own.
        static void KeepShowing() {
            if (_session != null) {
                var taken = _session.TakeLightmap();
                if (taken != null && taken == _lightmap) {
                    Free(_owned);
                    _owned = taken;
                } else Free(taken);
                _session.Dispose();
                _session = null;
            }
            _keeping = true;
        }

        static void Free(Texture texture) {
            if (texture == null) return;
            if (texture is RenderTexture rt) rt.Release();
            Kill(texture);
        }

        /// <summary>
        /// Gives each static actor and copy its rectangle of the atlas, as BeginBake and LoadLight
        /// read them, and hands its triangles to `scene` when there is one. "" or why not.
        /// </summary>
        static string Place(float[] actors, float[] copies, int width, int height, int pages, BakeSceneBuilder scene) {
            try {
                _sheet = new LightSheet(width, height, pages);
            } catch (ArgumentException e) {
                return e.Message;
            }
            _sheetSize = (width, height, pages);
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
        public static string LightFile() => _session?.File() ?? "";

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
            _bakeProblem = null;
            ClearLight();
            var problem = Shapes(actors, copies, grid, out var origin, out var spacing, out var counts);
            if (problem != "") return Refuse(problem);
            byte[] png;
            try {
                png = Convert.FromBase64String(file ?? "");
            } catch (FormatException) {
                return "the light file is not base64";
            }
            problem = LightFileFormat.Decode(png, width, height, pages, counts.x * counts.y * counts.z, out var light, out var probes);
            if (problem == "") problem = Place(actors, copies, width, height, pages, null);
            if (problem != "") {
                Free(light);
                return Refuse(problem);
            }
            _owned = light;
            _gridOrigin = origin;
            _gridSpacing = spacing;
            _gridCounts = counts;
            if (probes.Length > 0) ShowProbes(new ProbeField(probes, origin, spacing, counts));
            ShowLightmap(light, pages);
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
        /// then each page's draft lightmap, then its final. A bake keeping the light it replaced
        /// (BeginBake) shows its lightmap once every page has its draft.
        /// Returns the bake's progress, 1 once done (the probes read back too), 0 while the bake
        /// waits on its textures' means, -1 when nothing is baking, or -2 when the bake failed,
        /// which BakeProblem says why; the light is cleared then, as by ClearLight.
        /// </summary>
        public static float StepBake(double tiles) {
            if (_session == null) return -1;
            var p = _session.Step(tiles);
            if (p == BakeSession.Failed) {
                var problem = _session.Problem;
                ClearLight();
                _bakeProblem = problem;
                return -2;
            }
            var baker = _session.Baker;
            if (baker == null) return p;
            if (baker.ProbesReady && baker.ProbeCount > 0 && (_probes == null || _probes.Probes != baker.ProbeSH)) {
                ShowProbes(new ProbeField(baker.ProbeSH, _gridOrigin, _gridSpacing, _gridCounts));
            }
            var ready = !_keeping || baker.PagesLit == baker.Pages;
            if (baker.Lightmap != null && ready && (_lightmap != baker.Lightmap || _pagesLit != baker.PagesLit)) {
                var kept = _keeping ? _owned : null;
                if (_keeping) {
                    _keeping = false;
                    _owned = null;
                }
                ShowLightmap(baker.Lightmap, baker.PagesLit);
                Free(kept);
            }
            return p;
        }

        /// <summary>Why the last bake failed on the bridge, once StepBake answered -2; "" otherwise.</summary>
        public static string BakeProblem() => _bakeProblem ?? "";

        /// <summary>
        /// What the bake's steps do now, as LightBaker.Phase's number: 0 none (or waiting on its
        /// textures' means), 1 the probes, 2 the lightmap while the probes' read is in flight, 3 the
        /// lightmap, 4 finishing. The frame budget starts over when it changes.
        /// </summary>
        public static int BakePhase() => _session?.Baker == null ? (int)global::OneJS.Lighting.BakePhase.None : (int)_session.Baker.Phase;

        /// <summary>The page the bake's lightmap is on, from 0. The frame budget starts over when it changes, as at a phase.</summary>
        public static int BakePage() => _session?.Baker?.Page ?? 0;

        /// <summary>Whether a bake has been started and not finished.</summary>
        public static bool Baking => _session != null && _session.Running;

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

        // Shows `probes`, and has every actor not drawn from the lightmap take its light from them afresh.
        static void ShowProbes(ProbeField probes) {
            _probes = probes;
            Probing(true);
            foreach (var a in _actors.Values) a.probed = false;
            UpdateProbes();
        }

        /// <summary>Forgets the baked light, and stops a bake under way: every actor and copy draws with the scene's ambient again.</summary>
        public static void ClearLight() {
            _session?.Dispose();
            _session = null;
            _keeping = false;
            Free(_owned);
            _owned = null;
            var had = Showing;
            _lightmap = null;
            _pagesLit = 0;
            _sheet = new LightSheet(1, 1, 1);
            _sheetSize = default;
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
                a.probe = _probes.Blend(middle);
                a.probed = true;
                a.probedAt = middle;
                Apply(a);
            }
        }

        /// <summary>The light at `at`, blended from the eight probes round it; outside the grid, the nearest edge's.</summary>
        internal static SphericalHarmonicsL2 BlendProbes(Vector3 at) => _probes?.Blend(at) ?? new SphericalHarmonicsL2();
    }
}
