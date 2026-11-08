using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Lighting {
    /// <summary>
    /// What a bake lights: the static triangles, in world space, already laid out in the atlas's
    /// sheet (LightSheet). Every triangle both receives light and blocks or bounces it, whichever
    /// page it is on.
    /// </summary>
    public sealed class BakeScene {
        public Vector3[] Positions;
        /// <summary>Per vertex, world space: what the G-buffer interpolates.</summary>
        public Vector3[] Normals;
        /// <summary>Per vertex, the uv on the sheet, in pages (LightSheet): 0 to 1 a side on one page.</summary>
        public Vector2[] Uvs;
        public int[] Indices;
        /// <summary>Per triangle, linear: what a bounce off it is tinted by.</summary>
        public Color[] Albedo;
        /// <summary>Each page's size, and how many pages: 1, 2 or 4 (LightSheet).</summary>
        public int AtlasWidth;
        public int AtlasHeight;
        public int Pages = 1;

        /// <summary>The probe grid: its first probe, the distance between probes on each axis, and how many on each. No probes when any count is 0.</summary>
        public Vector3 GridOrigin;
        public Vector3 GridSpacing = Vector3.one;
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

    /// <summary>What a bake's step traces: see LightBaker.Phase.</summary>
    public enum BakePhase { None, Probes, LightmapProbesReading, Lightmap, Finishing }

    public sealed class BakeSettings {
        /// <summary>Paths a texel at the end, and at the draft that is shown first.</summary>
        public int Samples = 256;
        public int DraftSamples = 64;
        /// <summary>Rays a probe.</summary>
        public int ProbeRays = 1024;
        /// <summary>Hits a path bounces off before it stops: 0 is the sky and the sun's first hit alone.</summary>
        public int Bounces = 2;
        /// <summary>Extra darkening in corners and where things touch, 0 to 1: a lightmap texel's light times one less this times how near, within a metre, its paths' first hits are. Probes take none.</summary>
        public float Occlusion = 0;
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
    /// The probes come first, so moving things are lit within a few frames; then the lightmap, a
    /// page at a time, each page shown as a draft at DraftSamples and finished at Samples. The world
    /// is built once for every page, and one page's working textures are held at a time. Step does
    /// at most about the paths it is given, in tiles, so a game can bake beside its frame.
    ///
    /// A page of a sheet of several bakes an apron round itself, as many texels as dilation and the
    /// denoiser reach, and seeds each texel's paths by where it is on the sheet and which sweep it
    /// is in. So the texels two pages share come out the same on both, and the seam between them
    /// is not seen. One page has no apron and keeps the seeds it had before pages: its bake is the
    /// same to the bit.
    /// </summary>
    public sealed class LightBaker : IDisposable {
        public const string ShaderPath = "OneJS/LightBake";
        const int PassGBuffer = 0, PassTrace = 1, PassProbes = 2, PassDilate = 3, PassResolve = 4, PassDenoise = 5;
        /// <summary>Texels a row of the triangle and node textures: inside every GPU's limit.</summary>
        const int DataWidth = 2048;
        /// <summary>A probe whose rays meet the back of a face more often than this is inside a solid and is left out.</summary>
        public const float InsideShare = 0.5f;
        const int DilateSteps = 4;
        /// <summary>The most bounces and samples a bake takes: past them a number is a mistake, and the time it asks for is minutes.</summary>
        public const int MaxBounces = 8, MaxSamples = 4096;

        static readonly int TrisId = Shader.PropertyToID("_Tris"), NodesId = Shader.PropertyToID("_Nodes"),
            DataWidthId = Shader.PropertyToID("_DataWidth"), NodeCountId = Shader.PropertyToID("_NodeCount"),
            SkyId = Shader.PropertyToID("_Sky"), GroundId = Shader.PropertyToID("_Ground"),
            SunDirId = Shader.PropertyToID("_SunDir"), SunColorId = Shader.PropertyToID("_SunColor"), SunAngleId = Shader.PropertyToID("_SunAngle"),
            BouncesId = Shader.PropertyToID("_Bounces"), OcclusionId = Shader.PropertyToID("_Occlusion"), PassId = Shader.PropertyToID("_Pass"),
            GPosId = Shader.PropertyToID("_GPos"), GNormalId = Shader.PropertyToID("_GNormal"),
            SamplesId = Shader.PropertyToID("_Samples"), WeightId = Shader.PropertyToID("_Weight"),
            SrcId = Shader.PropertyToID("_Src"), RadiusId = Shader.PropertyToID("_Radius"), SigmaPId = Shader.PropertyToID("_SigmaP"),
            GridOriginId = Shader.PropertyToID("_GridOrigin"), GridSpacingId = Shader.PropertyToID("_GridSpacing"),
            GridCountsId = Shader.PropertyToID("_GridCounts"), ProbesWideId = Shader.PropertyToID("_ProbesWide"), RaysId = Shader.PropertyToID("_Rays"),
            RayBaseId = Shader.PropertyToID("_RayBase"), RayTotalId = Shader.PropertyToID("_RayTotal"),
            WindowId = Shader.PropertyToID("_Window"), OriginXId = Shader.PropertyToID("_OriginX"), OriginYId = Shader.PropertyToID("_OriginY"),
            FromId = Shader.PropertyToID("_From"), WorkWidthId = Shader.PropertyToID("_WorkWidth"), WorkHeightId = Shader.PropertyToID("_WorkHeight");
        static readonly int[] PrevIds = { Shader.PropertyToID("_Prev0"), Shader.PropertyToID("_Prev1"), Shader.PropertyToID("_Prev2"), Shader.PropertyToID("_Prev3") };

        readonly BakeSettings _settings;
        Material _material;
        Texture2D _tris, _nodes;
        Mesh _gbufferMesh;
        readonly LightSheet _sheet;
        // Each page's size, and its working textures', its apron each side added.
        readonly int _width, _height, _pages, _apron, _workWidth, _workHeight;
        RenderTexture _gPos, _gNormal, _accum, _work0, _work1;
        // Every page, a slice each, once the first has its draft.
        RenderTexture _lightmap;
        // The page being baked, and how many have at least their draft.
        int _page, _pagesLit;

        readonly int _probeCount, _probesWide, _probesHigh;
        readonly Vector3Int _gridCounts;
        RenderTexture[] _probeA, _probeB;
        int _probeRaysDone;
        // The probe targets' four reads, between the last probe pass and their landing.
        TextureRead[] _probeReads;
        float[] _probeSH;
        Color _sky, _ground;

        readonly int _tilesX, _tilesY;
        int _tile;
        /// <summary>Samples every texel has after the current sweep over the tiles completes, and before it.</summary>
        int _sweepSamples;
        uint _pass;
        bool _mapDone, _disposed;

        // Kept for the bake, so a step allocates nothing: WebGL's collector stops the world.
        readonly CommandBuffer _cmd = new() { name = "OneJS Bake" };
        readonly MaterialPropertyBlock _probeBlock = new(), _pageBlock = new(), _tileBlock = new(), _finishBlock = new();
        RenderTargetIdentifier[] _probeIdsA, _probeIdsB;

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
            _pages = scene.Pages;
            _sheet = new LightSheet(_width, _height, _pages);
            _apron = _pages == 1 ? 0 : DilateSteps + _settings.DenoiseRadius;
            _workWidth = _width + 2 * _apron;
            _workHeight = _height + 2 * _apron;
            _gridCounts = scene.GridCounts;
            _probeCount = Math.Max(0, _gridCounts.x) * Math.Max(0, _gridCounts.y) * Math.Max(0, _gridCounts.z);
            if (_probeCount > 0) {
                _probesWide = Math.Min(_probeCount, DataWidth / 2);
                _probesHigh = (_probeCount + _probesWide - 1) / _probesWide;
            }
            var tile = Math.Max(8, _settings.TileSize);
            _tilesX = (_workWidth + tile - 1) / tile;
            _tilesY = (_workHeight + tile - 1) / tile;
            // What it made so far goes with a throw, since the caller never gets a baker to dispose.
            try {
                Build(scene);
            } catch {
                Dispose();
                throw;
            }
        }

        // The constructor's GPU half: the data textures, the targets, the G-buffer's first page and the probes' targets.
        void Build(BakeScene scene) {
            _material = new Material(Resources.Load<Shader>(ShaderPath)) { hideFlags = HideFlags.HideAndDontSave };
            var (nodeData, order) = Bvh.Build(scene.Positions, scene.Indices);
            _tris = DataTexture(TriangleData(scene, order), "OneJS Bake Triangles");
            _nodes = DataTexture(nodeData, "OneJS Bake Nodes");
            _material.SetTexture(TrisId, _tris);
            _material.SetTexture(NodesId, _nodes);
            _material.SetInteger(DataWidthId, DataWidth);
            _material.SetInteger(WorkWidthId, _workWidth);
            _material.SetInteger(WorkHeightId, _workHeight);
            _material.SetInteger(NodeCountId, nodeData.Length / 8);
            _material.SetVector(SkyId, (Vector4)scene.Sky);
            _material.SetVector(GroundId, (Vector4)scene.Ground);
            _material.SetVector(SunDirId, scene.SunDirection.normalized);
            _material.SetVector(SunColorId, (Vector4)scene.Sun);
            _material.SetFloat(SunAngleId, scene.SunAngle);
            _material.SetInteger(BouncesId, _settings.Bounces);
            _material.SetFloat(OcclusionId, _settings.Occlusion);

            _gPos = Target(RenderTextureFormat.ARGBFloat, "OneJS Bake Positions");
            _gNormal = Target(RenderTextureFormat.ARGBFloat, "OneJS Bake Normals");
            _accum = Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Sum");
            _gbufferMesh = GBufferMesh(scene);
            _material.SetTexture(GPosId, _gPos);
            _material.SetTexture(GNormalId, _gNormal);
            StartPage(_cmd, 0);
            Flush(_cmd);

            if (_probeCount > 0) {
                _probeA = ProbeTargets("A");
                _probeB = ProbeTargets("B");
                _probeIdsA = Ids(_probeA);
                _probeIdsB = Ids(_probeB);
                _material.SetVector(GridOriginId, scene.GridOrigin);
                _material.SetVector(GridSpacingId, scene.GridSpacing);
                _material.SetVector(GridCountsId, new Vector4(_gridCounts.x, _gridCounts.y, _gridCounts.z, 0));
                _material.SetInteger(ProbesWideId, _probesWide);
                _sky = scene.Sky;
                _ground = scene.Ground;
            }
        }

        public int Width => _width;
        /// <summary>How many pages the lightmap has, each Width by Height, and the page being baked.</summary>
        public int Pages => _pages;
        public int Page => _page;
        /// <summary>Pages with at least their draft, from the first: what a model on page n waits for to be more than n.</summary>
        public int PagesLit => _pagesLit;
        /// <summary>The paths one tile traces: the least a Step traces once the probes are done.</summary>
        public long TilePaths => (long)Math.Max(8, _settings.TileSize) * Math.Max(8, _settings.TileSize) * _settings.SamplesPerPass;
        public int Height => _height;
        public int ProbeCount => _probeCount;
        /// <summary>Samples a texel the page being baked has so far.</summary>
        public int Samples { get; private set; }
        /// <summary>True once the probes are read back: ProbeSH holds them.</summary>
        public bool ProbesReady => _probeSH != null || _probeCount == 0;
        /// <summary>True once the first page has its draft.</summary>
        public bool DraftReady => _pagesLit > 0;
        /// <summary>True once the lightmap is finished and the probes are read back, which can land a frame or more after it.</summary>
        public bool Done => _mapDone && ProbesReady;
        /// <summary>
        /// What a Step does now: the probes' rays, then the lightmap's tiles while the probes' read is in
        /// flight, then once it has landed, then nothing while it lands. A path of each costs the GPU
        /// differently, and the frame a phase changes in costs more than either, so a frame budget
        /// starts over at each, and at each new Page, whose frame lays its G-buffer.
        /// </summary>
        public BakePhase Phase => _mapDone ? BakePhase.Finishing
            : _probeCount > 0 && _probeRaysDone < _settings.ProbeRays ? BakePhase.Probes
            : ProbesReady ? BakePhase.Lightmap : BakePhase.LightmapProbesReading;

        /// <summary>
        /// The lightmap, a texture array of Pages slices: each page's draft, then its final, written in
        /// place. Null before the first page's draft. RGB light, A 1 where a texel has light. A slice
        /// from PagesLit on holds nothing yet.
        /// </summary>
        public RenderTexture Lightmap => _lightmap;

        /// <summary>
        /// Each probe's light as Unity's SphericalHarmonicsL2 holds the ambient probe: nine coefficients for
        /// red, then green, then blue, 27 floats a probe, in the grid's x, then y, then z order. A probe
        /// inside a solid holds its neighbours' mean.
        /// </summary>
        public float[] ProbeSH => _probeSH;

        /// <summary>0 to 1, 1 only once done: the probes are the first tenth, the lightmap's pages the rest, in equal shares.</summary>
        public float Progress {
            get {
                if (Done) return 1;
                var probes = _probeCount == 0 ? 1f : Mathf.Min(1f, _probeRaysDone / (float)_settings.ProbeRays);
                var page = _mapDone ? 1f : Mathf.Min(1f, (Samples + SweepFraction() * _settings.SamplesPerPass) / _settings.Samples);
                var map = (_page + page) / _pages;
                // Below 1 while the probes' read is on its way.
                return Mathf.Min(0.999f, _probeCount == 0 ? map : 0.1f * probes + 0.9f * map);
            }
        }

        float SweepFraction() => _tile / (float)(_tilesX * _tilesY);

        /// <summary>
        /// Does about `paths` paths of work, at least one draw, and returns whether the bake is done.
        /// A path is one ray from a texel or a probe, with its bounces. The step that finishes the
        /// probes ends there: a count of paths that fitted a frame of probe rays says nothing of
        /// the lightmap's, whose paths bounce off surfaces, and spent on its tiles it took a 117 ms
        /// frame at 4 bounces (review L2). Once the lightmap is finished it draws nothing more and
        /// only looks for the probes' read.
        /// </summary>
        public bool Step(long paths) {
            if (_disposed) throw new ObjectDisposedException(nameof(LightBaker));
            LandProbes();
            if (Done) return true;
            var cmd = _cmd;
            long spent = 0;
            while (!_mapDone && (spent == 0 || spent < paths)) {
                if (_probeCount > 0 && _probeRaysDone < _settings.ProbeRays) {
                    var perRay = (long)_probeCount * 2;
                    var rays = (int)Math.Clamp((paths - spent) / Math.Max(1, perRay), 1, 64);
                    rays = Math.Min(rays, _settings.ProbeRays - _probeRaysDone);
                    ProbePass(cmd, rays);
                    spent += perRay * rays;
                    if (_probeRaysDone >= _settings.ProbeRays) {
                        Flush(cmd);
                        ReadProbes();
                        break;
                    }
                    continue;
                }
                spent += TilePass(cmd);
                if (_tile == 0) {
                    // A sweep over the page's every tile finished.
                    Samples = _sweepSamples;
                    if (Samples >= _settings.Samples) {
                        Finish(cmd);
                        _pagesLit = _page + 1;
                        if (_page + 1 < _pages) StartPage(cmd, _page + 1);
                        else _mapDone = true;
                    } else if (_pagesLit == _page && Samples >= _settings.DraftSamples) {
                        Finish(cmd);
                        _pagesLit = _page + 1;
                    }
                }
            }
            Flush(cmd);
            if (_mapDone && _accum != null) ReleaseWork();
            LandProbes();
            return Done;
        }

        /// <summary>Bakes to the end, a tile at a time, waiting on the GPU between submissions so none grows long.</summary>
        public void RunToEnd(long pathsPerSubmission = 2_000_000) {
            while (!Step(pathsPerSubmission)) Sync();
        }

        void ProbePass(CommandBuffer cmd, int rays) {
            // A command buffer copies a block's values as it records each draw, so one block serves every draw.
            var block = _probeBlock;
            block.Clear();
            for (var i = 0; i < 4; i++) block.SetTexture(PrevIds[i], _probeA[i]);
            block.SetInteger(RaysId, rays);
            block.SetInteger(RayBaseId, _probeRaysDone);
            block.SetInteger(RayTotalId, _settings.ProbeRays);
            cmd.SetGlobalInteger(PassId, (int)_pass++);
            cmd.SetRenderTarget(_probeIdsB, _probeB[0].depthBuffer);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassProbes, MeshTopology.Triangles, 3, 1, block);
            (_probeA, _probeB) = (_probeB, _probeA);
            (_probeIdsA, _probeIdsB) = (_probeIdsB, _probeIdsA);
            _probeRaysDone += rays;
        }

        // Lays page `page`'s triangles into the G-buffer and clears its sum: the page's first frame.
        // Every triangle is drawn, moved so the page and its apron fill the target, which clips the rest.
        void StartPage(CommandBuffer cmd, int page) {
            _page = page;
            _tile = 0;
            Samples = 0;
            var corner = _sheet.Origin(page) - new Vector2Int(_apron, _apron);
            var block = _pageBlock;
            block.Clear();
            block.SetVector(WindowId, new Vector4(_width / (float)_workWidth, _height / (float)_workHeight, -corner.x / (float)_workWidth, -corner.y / (float)_workHeight));
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { _gPos, _gNormal }, _gPos.depthBuffer);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.DrawMesh(_gbufferMesh, Matrix4x4.identity, _material, 0, PassGBuffer, block);
            cmd.SetRenderTarget(_accum);
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        long TilePass(CommandBuffer cmd) {
            var tile = Math.Max(8, _settings.TileSize);
            if (_tile == 0) _sweepSamples = Samples + _settings.SamplesPerPass;
            var tx = _tile % _tilesX;
            var ty = _tile / _tilesX;
            var rect = new Rect(tx * tile, ty * tile, Math.Min(tile, _workWidth - tx * tile), Math.Min(tile, _workHeight - ty * tile));
            var block = _tileBlock;
            block.Clear();
            block.SetInteger(SamplesId, _settings.SamplesPerPass);
            // The running mean: a texel with n samples takes k more at k / (n + k).
            block.SetFloat(WeightId, _settings.SamplesPerPass / (float)_sweepSamples);
            // A texel's paths are seeded by its place on the sheet (its apron's corner added, so never
            // below 0) and the pass: on one page its count, as before pages; on a sheet the sweep, the
            // same for a texel on whichever page bakes it.
            var origin = _sheet.Origin(_page);
            block.SetInteger(OriginXId, origin.x);
            block.SetInteger(OriginYId, origin.y);
            cmd.SetGlobalInteger(PassId, (int)(_apron == 0 ? _pass : (uint)(_sweepSamples / _settings.SamplesPerPass)));
            _pass++;
            cmd.SetRenderTarget(_accum);
            cmd.EnableScissorRect(rect);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassTrace, MeshTopology.Triangles, 3, 1, block);
            cmd.DisableScissorRect();
            _tile = (_tile + 1) % (_tilesX * _tilesY);
            return (long)(rect.width * rect.height) * _settings.SamplesPerPass;
        }

        // Resolves, dilates and denoises the page's sum into its slice of the lightmap.
        void Finish(CommandBuffer cmd) {
            _work0 ??= Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Work A");
            _work1 ??= Target(RenderTextureFormat.ARGBHalf, "OneJS Bake Work B");
            if (_lightmap == null) {
                _lightmap = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                    name = "OneJS Lightmap", dimension = TextureDimension.Tex2DArray, volumeDepth = _pages,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!_lightmap.Create()) {
                    Kill(_lightmap);
                    _lightmap = null;
                    throw new InvalidOperationException($"[OneJS] The bake could not make its {_width} by {_height} lightmap of {_pages} pages.");
                }
            }

            var block = _finishBlock;
            block.Clear();
            block.SetTexture(SrcId, _accum);
            cmd.SetRenderTarget(_work0);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassResolve, MeshTopology.Triangles, 3, 1, block);
            RenderTexture from = _work0, to = _work1;
            for (var i = 0; i < DilateSteps; i++) {
                block.Clear();
                block.SetTexture(SrcId, from);
                cmd.SetRenderTarget(to);
                cmd.DrawProcedural(Matrix4x4.identity, _material, PassDilate, MeshTopology.Triangles, 3, 1, block);
                (from, to) = (to, from);
            }
            block.Clear();
            block.SetTexture(SrcId, from);
            block.SetInteger(RadiusId, _settings.DenoiseRadius);
            block.SetFloat(SigmaPId, _settings.DenoiseTexels / Mathf.Max(0.01f, _settings.Detail));
            // The page's own texels, inside its apron.
            block.SetInteger(FromId, _apron);
            cmd.SetRenderTarget(_lightmap, 0, CubemapFace.Unknown, _page);
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassDenoise, MeshTopology.Triangles, 3, 1, block);
        }

        // Starts reading the probe sums back; LandProbes solves them once all four have landed.
        void ReadProbes() {
            _probeReads = new TextureRead[4];
            for (var i = 0; i < 4; i++) _probeReads[i] = TextureRead.Start(_probeA[i]);
            LandProbes();
        }

        void LandProbes() {
            if (_probeReads == null) return;
            foreach (var read in _probeReads) if (!read.Done) return;
            var sums = new float[4][];
            for (var i = 0; i < 4; i++) {
                if (_probeReads[i].Error != null) throw new InvalidOperationException($"[OneJS] The bake's probes could not be read: {_probeReads[i].Error}.");
                sums[i] = _probeReads[i].Texels;
            }
            _probeReads = null;
            _probeSH = ProbeSolve.FromSums(sums, _probesWide * 2, _probeCount, _probeRaysDone, _gridCounts, _sky, _ground);
            foreach (var t in _probeA) Release(t);
            foreach (var t in _probeB) Release(t);
            _probeA = _probeB = null;
        }

        /// <summary>
        /// Waits for the GPU to finish what has been submitted, by reading a texel back, and for the
        /// probes' read. For the editor and tests (RunToEnd): a browser cannot wait on its GPU.
        /// </summary>
        public void Sync() {
            if (_probeReads != null) foreach (var read in _probeReads) read.Wait();
            var target = _accum != null ? _accum : Lightmap;
            if (target != null) TextureRead.Start(target, 0, 0, 1, 1).Wait();
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

        /// <summary>
        /// Hands the lightmap over to the caller, who releases it: this baker's Dispose leaves it, so
        /// a light can stay showing after its bake goes. Null before the first draft. Taken, the
        /// baker is done with: dispose it next.
        /// </summary>
        public RenderTexture TakeLightmap() {
            var map = _lightmap;
            _lightmap = null;
            return map;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _cmd.Release();
            ReleaseWork();
            if (_probeA != null) foreach (var t in _probeA) Release(t);
            if (_probeB != null) foreach (var t in _probeB) Release(t);
            Release(_lightmap);
            _lightmap = null;
            Kill(_tris);
            Kill(_nodes);
            Kill(_gbufferMesh);
            Kill(_material);
        }

        /// <summary>Destroy where a player runs, DestroyImmediate in the editor's edit mode, which refuses Destroy: a bake runs in the edit-mode preview too.</summary>
        static void Kill(UnityEngine.Object o) {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        static void Release(RenderTexture rt) {
            if (rt == null) return;
            rt.Release();
            Kill(rt);
        }

        // A working texture: the page and its apron.
        RenderTexture Target(RenderTextureFormat format, string name) => Target(_workWidth, _workHeight, format, name);

        static RenderTexture Target(int w, int h, RenderTextureFormat format, string name) {
            var rt = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear) {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!rt.Create()) {
                Kill(rt);
                throw new InvalidOperationException($"[OneJS] The bake could not make its {w} by {h} {format} target {name}.");
            }
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

        /// <summary>
        /// The static triangles with a corner each, so each carries its own centre to the G-buffer, at
        /// their uvs on the sheet: each page draws them all, moved to it.
        /// </summary>
        static Mesh GBufferMesh(BakeScene s) {
            var n = s.Indices.Length;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var uv = new Vector2[n];
            var centre = new Vector3[n];
            var order = new int[n];
            for (var k = 0; k < n; k += 3) {
                var c = (s.Positions[s.Indices[k]] + s.Positions[s.Indices[k + 1]] + s.Positions[s.Indices[k + 2]]) / 3f;
                for (var j = 0; j < 3; j++) {
                    var v = s.Indices[k + j];
                    pos[k + j] = s.Positions[v];
                    nrm[k + j] = s.Normals[v];
                    uv[k + j] = s.Uvs[v];
                    centre[k + j] = c;
                    order[k + j] = k + j;
                }
            }
            var mesh = new Mesh { name = "OneJS Bake G-buffer", indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, centre);
            mesh.SetTriangles(order, 0, false);
            // Placed by uv, not by position, so Unity must never cull it by its bounds.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        /// <summary>Throws ArgumentException naming what is wrong with a scene or its settings, as the constructor does.</summary>
        public static void Validate(BakeScene s, BakeSettings o) {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Positions == null || s.Normals == null || s.Uvs == null || s.Indices == null || s.Albedo == null) throw new ArgumentException("[OneJS] a bake needs positions, normals, uvs, indices and albedo");
            if (s.Indices.Length == 0 || s.Indices.Length % 3 != 0) throw new ArgumentException($"[OneJS] a bake needs whole triangles, and has {s.Indices.Length} indices");
            if (s.Normals.Length != s.Positions.Length || s.Uvs.Length != s.Positions.Length) throw new ArgumentException("[OneJS] a bake needs a normal and a uv for every position");
            if (s.Albedo.Length != s.Indices.Length / 3) throw new ArgumentException($"[OneJS] a bake needs an albedo for each of its {s.Indices.Length / 3} triangles, and has {s.Albedo.Length}");
            foreach (var i in s.Indices) if (i < 0 || i >= s.Positions.Length) throw new ArgumentException($"[OneJS] a bake's index {i} is past its {s.Positions.Length} positions");
            if (s.AtlasWidth < 1 || s.AtlasHeight < 1) throw new ArgumentException($"[OneJS] a bake's atlas is {s.AtlasWidth} by {s.AtlasHeight}");
            var sheet = new LightSheet(s.AtlasWidth, s.AtlasHeight, s.Pages);
            foreach (var uv in s.Uvs) if (!sheet.Holds(uv)) throw new ArgumentException($"[OneJS] a bake's uv {uv} is past its {s.Pages} pages");
            if (o.DenoiseRadius < 0) throw new ArgumentException($"[OneJS] a bake's denoise radius is 0 or more, not {o.DenoiseRadius}");
            if (o.Samples < 1 || o.SamplesPerPass < 1 || o.ProbeRays < 1) throw new ArgumentException("[OneJS] a bake's samples and rays must be positive");
            if (o.Samples > MaxSamples) throw new ArgumentException($"[OneJS] a bake's samples are 1 to {MaxSamples}, not {o.Samples}");
            if (o.Bounces < 0 || o.Bounces > MaxBounces) throw new ArgumentException($"[OneJS] a bake's bounces are 0 to {MaxBounces}, not {o.Bounces}");
            if (!(o.Occlusion >= 0 && o.Occlusion <= 1)) throw new ArgumentException($"[OneJS] a bake's occlusion is 0 to 1, not {o.Occlusion}");
            if (ProbeField.Problem(s.GridSpacing, s.GridCounts) is { } grid) throw new ArgumentException($"[OneJS] {grid}");
        }
    }
}
