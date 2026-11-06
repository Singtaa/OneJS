using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Lighting {
    /// <summary>
    /// What a bake lights: the static triangles, in world space, already laid out in the atlas.
    /// Every triangle both receives light and blocks or bounces it.
    /// </summary>
    public sealed class BakeScene {
        public Vector3[] Positions;
        /// <summary>Per vertex, world space: what the G-buffer interpolates.</summary>
        public Vector3[] Normals;
        /// <summary>Per vertex, the atlas uv, 0 to 1 a side.</summary>
        public Vector2[] Uvs;
        public int[] Indices;
        /// <summary>Per triangle, linear: what a bounce off it is tinted by.</summary>
        public Color[] Albedo;
        public int AtlasWidth;
        public int AtlasHeight;

        /// <summary>The probe grid: its first probe, the distance between probes, and how many on each axis. No probes when any count is 0.</summary>
        public Vector3 GridOrigin;
        public float GridSpacing = 1;
        public Vector3Int GridCounts;

        /// <summary>Linear radiance from above the horizon and below it: the ambient probe's two hemispheres.</summary>
        public Color Sky;
        public Color Ground;
        /// <summary>Towards the sun, and its linear colour times its intensity. Black for no sun.</summary>
        public Vector3 SunDirection = Vector3.up;
        public Color Sun = Color.black;
        /// <summary>The sun's angular radius, radians: 0.53 degrees, the real sun's, gives soft bounced shadows.</summary>
        public float SunAngle = 0.0093f;
    }

    public sealed class BakeSettings {
        /// <summary>Paths a texel at the end, and at the draft that is shown first.</summary>
        public int Samples = 256;
        public int DraftSamples = 64;
        /// <summary>Rays a probe.</summary>
        public int ProbeRays = 1024;
        public int Bounces = 2;
        /// <summary>Paths a texel in one pass over one tile.</summary>
        public int SamplesPerPass = 4;
        /// <summary>A tile's side in texels: the most one draw traces, so no submission trips a GPU watchdog.</summary>
        public int TileSize = 64;
        public int DenoiseRadius = 3;
        /// <summary>The denoiser's reach in world distance, as texels at the bake's detail.</summary>
        public float DenoiseTexels = 2.5f;
        /// <summary>Texels a metre the atlas was packed at, for the denoiser's reach.</summary>
        public float Detail = 24;
    }

    /// <summary>
    /// Bakes sky and bounced light into a lightmap for static triangles, and the light at each point
    /// of a probe grid as spherical harmonics, on the GPU, a slice at a time (Runtime/Lighting/README.md).
    ///
    /// The probes come first, so moving things are lit within a few frames; then the lightmap, shown
    /// as a draft at DraftSamples and finished at Samples. Step does at most about the paths it is
    /// given, in tiles, so a game can bake beside its frame.
    /// </summary>
    public sealed class LightBaker : IDisposable {
        public const string ShaderPath = "OneJS/LightBake";
        const int PassGBuffer = 0, PassTrace = 1, PassProbes = 2, PassDilate = 3, PassResolve = 4, PassDenoise = 5;
        /// <summary>Texels a row of the triangle and node textures: inside every GPU's limit.</summary>
        const int DataWidth = 2048;
        /// <summary>A probe whose rays meet the back of a face more often than this is inside a solid and is left out.</summary>
        public const float InsideShare = 0.5f;
        const int DilateSteps = 4;

        static readonly int TrisId = Shader.PropertyToID("_Tris"), NodesId = Shader.PropertyToID("_Nodes"),
            DataWidthId = Shader.PropertyToID("_DataWidth"), NodeCountId = Shader.PropertyToID("_NodeCount"),
            SkyId = Shader.PropertyToID("_Sky"), GroundId = Shader.PropertyToID("_Ground"),
            SunDirId = Shader.PropertyToID("_SunDir"), SunColorId = Shader.PropertyToID("_SunColor"), SunAngleId = Shader.PropertyToID("_SunAngle"),
            BouncesId = Shader.PropertyToID("_Bounces"), PassId = Shader.PropertyToID("_Pass"),
            GPosId = Shader.PropertyToID("_GPos"), GNormalId = Shader.PropertyToID("_GNormal"),
            SamplesId = Shader.PropertyToID("_Samples"), WeightId = Shader.PropertyToID("_Weight"),
            SrcId = Shader.PropertyToID("_Src"), RadiusId = Shader.PropertyToID("_Radius"), SigmaPId = Shader.PropertyToID("_SigmaP"),
            GridOriginId = Shader.PropertyToID("_GridOrigin"), GridSpacingId = Shader.PropertyToID("_GridSpacing"),
            GridCountsId = Shader.PropertyToID("_GridCounts"), ProbesWideId = Shader.PropertyToID("_ProbesWide"), RaysId = Shader.PropertyToID("_Rays"),
            RayBaseId = Shader.PropertyToID("_RayBase"), RayTotalId = Shader.PropertyToID("_RayTotal");
        static readonly int[] PrevIds = { Shader.PropertyToID("_Prev0"), Shader.PropertyToID("_Prev1"), Shader.PropertyToID("_Prev2"), Shader.PropertyToID("_Prev3") };

        readonly BakeSettings _settings;
        readonly Material _material;
        readonly Texture2D _tris, _nodes;
        readonly Mesh _gbufferMesh;
        readonly int _width, _height;
        RenderTexture _gPos, _gNormal, _accum, _work0, _work1;
        RenderTexture _draft, _final;

        readonly int _probeCount, _probesWide, _probesHigh;
        readonly Vector3Int _gridCounts;
        RenderTexture[] _probeA, _probeB;
        int _probeRaysDone;
        float[] _probeSH;
        Color _sky, _ground;

        readonly int _tilesX, _tilesY;
        int _tile;
        /// <summary>Samples every texel has after the current sweep over the tiles completes, and before it.</summary>
        int _sweepSamples;
        uint _pass;
        bool _disposed;

        /// <summary>Whether this GPU can bake, and why not when it cannot.</summary>
        public static bool Supported(out string reason) {
            reason = null;
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)) reason = "it cannot draw into float textures";
            else if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)) reason = "it cannot draw into half-float textures";
            else if (SystemInfo.supportedRenderTargetCount < 4) reason = "it draws into fewer than four targets at once";
            else if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)) reason = "it cannot read float textures";
            else {
                var shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null) reason = $"{ShaderPath} is not in this build";
                else if (!shader.isSupported) reason = "its graphics API cannot run the bake's shader";
            }
            return reason == null;
        }

        public LightBaker(BakeScene scene, BakeSettings settings = null) {
            _settings = settings ?? new BakeSettings();
            Validate(scene, _settings);
            if (!Supported(out var why)) throw new NotSupportedException($"[OneJS] This GPU cannot bake light: {why}.");
            _width = scene.AtlasWidth;
            _height = scene.AtlasHeight;
            _material = new Material(Resources.Load<Shader>(ShaderPath)) { hideFlags = HideFlags.HideAndDontSave };

            var (nodes, order) = Bvh.Build(scene.Positions, scene.Indices);
            _tris = DataTexture(TriangleData(scene, order), "OneJS Bake Triangles");
            _nodes = DataTexture(nodes, "OneJS Bake Nodes");
            _material.SetTexture(TrisId, _tris);
            _material.SetTexture(NodesId, _nodes);
            _material.SetInteger(DataWidthId, DataWidth);
            _material.SetInteger(NodeCountId, nodes.Length / 8);
            _material.SetVector(SkyId, (Vector4)scene.Sky);
            _material.SetVector(GroundId, (Vector4)scene.Ground);
            _material.SetVector(SunDirId, scene.SunDirection.normalized);
            _material.SetVector(SunColorId, (Vector4)scene.Sun);
            _material.SetFloat(SunAngleId, scene.SunAngle);
            _material.SetInteger(BouncesId, _settings.Bounces);

            _gPos = Target(RenderTextureFormat.ARGBFloat, "OneJS Bake Positions");
            _gNormal = Target(RenderTextureFormat.ARGBFloat, "OneJS Bake Normals");
            _accum = Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Sum");
            _gbufferMesh = GBufferMesh(scene);
            using (var cmd = new CommandBuffer { name = "OneJS Bake G-buffer" }) {
                cmd.SetRenderTarget(new RenderTargetIdentifier[] { _gPos, _gNormal }, _gPos.depthBuffer);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.DrawMesh(_gbufferMesh, Matrix4x4.identity, _material, 0, PassGBuffer);
                cmd.SetRenderTarget(_accum);
                cmd.ClearRenderTarget(false, true, Color.clear);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            _material.SetTexture(GPosId, _gPos);
            _material.SetTexture(GNormalId, _gNormal);

            _gridCounts = scene.GridCounts;
            _probeCount = Math.Max(0, _gridCounts.x) * Math.Max(0, _gridCounts.y) * Math.Max(0, _gridCounts.z);
            if (_probeCount > 0) {
                _probesWide = Math.Min(_probeCount, DataWidth / 2);
                _probesHigh = (_probeCount + _probesWide - 1) / _probesWide;
                _probeA = ProbeTargets("A");
                _probeB = ProbeTargets("B");
                _material.SetVector(GridOriginId, scene.GridOrigin);
                _material.SetFloat(GridSpacingId, scene.GridSpacing);
                _material.SetVector(GridCountsId, new Vector4(_gridCounts.x, _gridCounts.y, _gridCounts.z, 0));
                _material.SetInteger(ProbesWideId, _probesWide);
                _sky = scene.Sky;
                _ground = scene.Ground;
            }

            var tile = Math.Max(8, _settings.TileSize);
            _tilesX = (_width + tile - 1) / tile;
            _tilesY = (_height + tile - 1) / tile;
        }

        public int Width => _width;
        public int Height => _height;
        public int ProbeCount => _probeCount;
        /// <summary>Samples a texel the lightmap has so far.</summary>
        public int Samples { get; private set; }
        /// <summary>True once the probes are read back: ProbeSH holds them.</summary>
        public bool ProbesReady => _probeSH != null || _probeCount == 0;
        /// <summary>True once the lightmap has its draft, then once it is finished.</summary>
        public bool DraftReady => _draft != null;
        public bool Done { get; private set; }

        /// <summary>The newest finished lightmap: the draft, then the final. Null before the draft. RGB light, A 1 where a texel has light.</summary>
        public RenderTexture Lightmap => _final != null ? _final : _draft;
        public RenderTexture Draft => _draft;

        /// <summary>
        /// Each probe's light as Unity's SphericalHarmonicsL2 holds the ambient probe: nine coefficients for
        /// red, then green, then blue, 27 floats a probe, in the grid's x, then y, then z order. A probe
        /// inside a solid holds its neighbours' mean.
        /// </summary>
        public float[] ProbeSH => _probeSH;

        /// <summary>0 to 1: the probes are the first tenth, the lightmap the rest.</summary>
        public float Progress {
            get {
                if (Done) return 1;
                var probes = _probeCount == 0 ? 1f : Mathf.Min(1f, _probeRaysDone / (float)_settings.ProbeRays);
                var map = Mathf.Min(1f, (Samples + SweepFraction() * _settings.SamplesPerPass) / _settings.Samples);
                return _probeCount == 0 ? map : 0.1f * probes + 0.9f * map;
            }
        }

        float SweepFraction() => _tile / (float)(_tilesX * _tilesY);

        /// <summary>
        /// Does about `paths` paths of work, at least one draw, and returns whether the bake is done.
        /// A path is one ray from a texel or a probe, with its bounces.
        /// </summary>
        public bool Step(long paths) {
            if (_disposed) throw new ObjectDisposedException(nameof(LightBaker));
            if (Done) return true;
            using var cmd = new CommandBuffer { name = "OneJS Bake" };
            long spent = 0;
            while (!Done && (spent == 0 || spent < paths)) {
                if (_probeCount > 0 && _probeRaysDone < _settings.ProbeRays) {
                    var perRay = (long)_probeCount * 2;
                    var rays = (int)Math.Clamp((paths - spent) / Math.Max(1, perRay), 1, 64);
                    rays = Math.Min(rays, _settings.ProbeRays - _probeRaysDone);
                    ProbePass(cmd, rays);
                    spent += perRay * rays;
                    if (_probeRaysDone >= _settings.ProbeRays) {
                        Flush(cmd);
                        ReadProbes();
                    }
                    continue;
                }
                spent += TilePass(cmd);
                if (_tile == 0) {
                    // A sweep over every tile finished.
                    Samples = _sweepSamples;
                    if (_draft == null && Samples >= Math.Min(_settings.DraftSamples, _settings.Samples)) _draft = Finish(cmd, "OneJS Lightmap Draft");
                    if (Samples >= _settings.Samples) {
                        _final = Finish(cmd, "OneJS Lightmap");
                        Done = true;
                    }
                }
            }
            Flush(cmd);
            if (Done) ReleaseWork();
            return Done;
        }

        /// <summary>Bakes to the end, a tile at a time, waiting on the GPU between submissions so none grows long.</summary>
        public void RunToEnd(long pathsPerSubmission = 2_000_000) {
            while (!Step(pathsPerSubmission)) Sync();
        }

        void ProbePass(CommandBuffer cmd, int rays) {
            var block = new MaterialPropertyBlock();
            for (var i = 0; i < 4; i++) block.SetTexture(PrevIds[i], _probeA[i]);
            block.SetInteger(RaysId, rays);
            block.SetInteger(RayBaseId, _probeRaysDone);
            block.SetInteger(RayTotalId, _settings.ProbeRays);
            cmd.SetGlobalInteger(PassId, (int)_pass++);
            cmd.SetRenderTarget(Ids(_probeB), _probeB[0].depthBuffer);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassProbes, MeshTopology.Triangles, 3, 1, block);
            (_probeA, _probeB) = (_probeB, _probeA);
            _probeRaysDone += rays;
        }

        long TilePass(CommandBuffer cmd) {
            var tile = Math.Max(8, _settings.TileSize);
            if (_tile == 0) _sweepSamples = Samples + _settings.SamplesPerPass;
            var tx = _tile % _tilesX;
            var ty = _tile / _tilesX;
            var rect = new Rect(tx * tile, ty * tile, Math.Min(tile, _width - tx * tile), Math.Min(tile, _height - ty * tile));
            var block = new MaterialPropertyBlock();
            block.SetInteger(SamplesId, _settings.SamplesPerPass);
            // The running mean: a texel with n samples takes k more at k / (n + k).
            block.SetFloat(WeightId, _settings.SamplesPerPass / (float)_sweepSamples);
            cmd.SetGlobalInteger(PassId, (int)_pass++);
            cmd.SetRenderTarget(_accum);
            cmd.EnableScissorRect(rect);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassTrace, MeshTopology.Triangles, 3, 1, block);
            cmd.DisableScissorRect();
            _tile = (_tile + 1) % (_tilesX * _tilesY);
            return (long)(rect.width * rect.height) * _settings.SamplesPerPass;
        }

        RenderTexture Finish(CommandBuffer cmd, string name) {
            _work0 ??= Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Work A");
            _work1 ??= Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Work B");
            var result = Target(RenderTextureFormat.ARGBHalf, name);
            result.filterMode = FilterMode.Bilinear;

            var block = new MaterialPropertyBlock();
            block.SetTexture(SrcId, _accum);
            cmd.SetRenderTarget(_work0);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassResolve, MeshTopology.Triangles, 3, 1, block);
            RenderTexture from = _work0, to = _work1;
            for (var i = 0; i < DilateSteps; i++) {
                block = new MaterialPropertyBlock();
                block.SetTexture(SrcId, from);
                cmd.SetRenderTarget(to);
                cmd.DrawProcedural(Matrix4x4.identity, _material, PassDilate, MeshTopology.Triangles, 3, 1, block);
                (from, to) = (to, from);
            }
            block = new MaterialPropertyBlock();
            block.SetTexture(SrcId, from);
            block.SetInteger(RadiusId, _settings.DenoiseRadius);
            block.SetFloat(SigmaPId, _settings.DenoiseTexels / Mathf.Max(0.01f, _settings.Detail));
            cmd.SetRenderTarget(result);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassDenoise, MeshTopology.Triangles, 3, 1, block);
            return result;
        }

        void ReadProbes() {
            var w = _probesWide * 2;
            var h = _probesHigh;
            var sums = new float[4][];
            var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try {
                for (var i = 0; i < 4; i++) {
                    RenderTexture.active = _probeA[i];
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                    sums[i] = tex.GetPixelData<float>(0).ToArray();
                }
            } finally {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(tex);
            }
            _probeSH = ProbeSolve.FromSums(sums, w, _probeCount, _probeRaysDone, _gridCounts, _sky, _ground);
            foreach (var t in _probeA) Release(t);
            foreach (var t in _probeB) Release(t);
            _probeA = _probeB = null;
        }

        /// <summary>Waits for the GPU to finish what has been submitted, by reading a texel back.</summary>
        public void Sync() {
            var target = _accum != null ? _accum : Lightmap;
            if (target == null) return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(tex);
        }

        static void Flush(CommandBuffer cmd) {
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        void ReleaseWork() {
            Release(_gPos);
            Release(_gNormal);
            Release(_accum);
            Release(_work0);
            Release(_work1);
            _gPos = _gNormal = _accum = _work0 = _work1 = null;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            ReleaseWork();
            if (_probeA != null) foreach (var t in _probeA) Release(t);
            if (_probeB != null) foreach (var t in _probeB) Release(t);
            Release(_draft);
            Release(_final);
            _draft = _final = null;
            UnityEngine.Object.Destroy(_tris);
            UnityEngine.Object.Destroy(_nodes);
            UnityEngine.Object.Destroy(_gbufferMesh);
            UnityEngine.Object.Destroy(_material);
        }

        static void Release(RenderTexture rt) {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        RenderTexture Target(RenderTextureFormat format, string name) => Target(_width, _height, format, name);

        static RenderTexture Target(int w, int h, RenderTextureFormat format, string name) {
            var rt = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear) {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            rt.Create();
            return rt;
        }

        RenderTexture[] ProbeTargets(string which) {
            var targets = new RenderTexture[4];
            for (var i = 0; i < 4; i++) {
                targets[i] = Target(_probesWide * 2, _probesHigh, RenderTextureFormat.ARGBFloat, $"OneJS Bake Probes {which}{i}");
                var cmd = new CommandBuffer();
                cmd.SetRenderTarget(targets[i]);
                cmd.ClearRenderTarget(false, true, Color.clear);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }
            return targets;
        }

        static RenderTargetIdentifier[] Ids(RenderTexture[] t) => new RenderTargetIdentifier[] { t[0], t[1], t[2], t[3] };

        static Texture2D DataTexture(float[] data, string name) {
            var texels = Math.Max(1, data.Length / 4);
            var rows = (texels + DataWidth - 1) / DataWidth;
            var padded = new float[DataWidth * rows * 4];
            Array.Copy(data, padded, data.Length);
            var tex = new Texture2D(DataWidth, rows, TextureFormat.RGBAFloat, false, true) {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixelData(padded, 0);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Five texels a triangle, in BVH order: v0, edge 1, edge 2, geometric normal, albedo.</summary>
        static float[] TriangleData(BakeScene s, int[] order) {
            var data = new float[order.Length * 20];
            for (var j = 0; j < order.Length; j++) {
                var t = order[j];
                Vector3 a = s.Positions[s.Indices[t * 3]], b = s.Positions[s.Indices[t * 3 + 1]], c = s.Positions[s.Indices[t * 3 + 2]];
                Vector3 e1 = b - a, e2 = c - a;
                var n = Vector3.Cross(e1, e2).normalized;
                // Facing the way its vertex normals do, which is what the back-of-a-face test needs.
                var vn = s.Normals[s.Indices[t * 3]] + s.Normals[s.Indices[t * 3 + 1]] + s.Normals[s.Indices[t * 3 + 2]];
                if (Vector3.Dot(n, vn) < 0) n = -n;
                var albedo = s.Albedo[t];
                var o = j * 20;
                Put(data, o, a); Put(data, o + 4, e1); Put(data, o + 8, e2); Put(data, o + 12, n);
                data[o + 16] = albedo.r; data[o + 17] = albedo.g; data[o + 18] = albedo.b;
            }
            return data;
        }

        static void Put(float[] d, int o, Vector3 v) {
            d[o] = v.x; d[o + 1] = v.y; d[o + 2] = v.z;
        }

        /// <summary>The static triangles with a corner each, so each carries its own centre to the G-buffer.</summary>
        static Mesh GBufferMesh(BakeScene s) {
            var n = s.Indices.Length;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var uv = new Vector2[n];
            var centre = new Vector3[n];
            for (var k = 0; k < n; k += 3) {
                var c = (s.Positions[s.Indices[k]] + s.Positions[s.Indices[k + 1]] + s.Positions[s.Indices[k + 2]]) / 3f;
                for (var j = 0; j < 3; j++) {
                    var v = s.Indices[k + j];
                    pos[k + j] = s.Positions[v];
                    nrm[k + j] = s.Normals[v];
                    uv[k + j] = s.Uvs[v];
                    centre[k + j] = c;
                }
            }
            var indices = new int[n];
            for (var i = 0; i < n; i++) indices[i] = i;
            var mesh = new Mesh { name = "OneJS Bake G-buffer", indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, centre);
            mesh.SetTriangles(indices, 0, false);
            // Placed by uv, not by position, so Unity must never cull it by its bounds.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        static void Validate(BakeScene s, BakeSettings o) {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Positions == null || s.Normals == null || s.Uvs == null || s.Indices == null || s.Albedo == null) throw new ArgumentException("[OneJS] a bake needs positions, normals, uvs, indices and albedo");
            if (s.Indices.Length == 0 || s.Indices.Length % 3 != 0) throw new ArgumentException($"[OneJS] a bake needs whole triangles, and has {s.Indices.Length} indices");
            if (s.Normals.Length != s.Positions.Length || s.Uvs.Length != s.Positions.Length) throw new ArgumentException("[OneJS] a bake needs a normal and a uv for every position");
            if (s.Albedo.Length != s.Indices.Length / 3) throw new ArgumentException($"[OneJS] a bake needs an albedo for each of its {s.Indices.Length / 3} triangles, and has {s.Albedo.Length}");
            foreach (var i in s.Indices) if (i < 0 || i >= s.Positions.Length) throw new ArgumentException($"[OneJS] a bake's index {i} is past its {s.Positions.Length} positions");
            if (s.AtlasWidth < 1 || s.AtlasHeight < 1) throw new ArgumentException($"[OneJS] a bake's atlas is {s.AtlasWidth} by {s.AtlasHeight}");
            if (o.Samples < 1 || o.SamplesPerPass < 1 || o.ProbeRays < 1 || o.Bounces < 0) throw new ArgumentException("[OneJS] a bake's samples, rays and bounces must be positive");
            if (s.GridCounts.x * s.GridCounts.y * s.GridCounts.z > 0 && !(s.GridSpacing > 0)) throw new ArgumentException("[OneJS] a probe grid needs a spacing above 0");
        }
    }
}
