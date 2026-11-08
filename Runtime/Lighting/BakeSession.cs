using System;
using UnityEngine;

namespace OneJS.Lighting {
    /// <summary>
    /// One bake from start to file: it waits on its textures' means (BakeSceneBuilder), then runs
    /// its LightBaker a step at a time, then reads its lightmap back into a light file. Everything
    /// it holds goes with Dispose, so a bake replaced or cleared at any point leaves nothing behind.
    /// What shows the light it makes is the caller's: ModelBridge reads Baker's lightmap and probes.
    /// </summary>
    internal sealed class BakeSession : IDisposable {
        /// <summary>Step's answer when the bake failed: Problem says why.</summary>
        public const float Failed = -2;

        BakeSceneBuilder _scene;
        BakeScene _bake;
        readonly BakeSettings _settings;
        TextureRead[] _fileReads;
        string _file;
        bool _fileReadWarned, _disposed;

        /// <summary>The baker, once the means have landed and it started; null before, or when it could not.</summary>
        public LightBaker Baker { get; private set; }

        /// <summary>Why the bake failed, in words for a console line ("this GPU cannot ..."), or null while it has not.</summary>
        public string Problem { get; private set; }

        /// <summary>Starts reading `scene`'s means for `bake`, which it fills and bakes once they land. Takes `scene`.</summary>
        public BakeSession(BakeSceneBuilder scene, BakeScene bake, BakeSettings settings) {
            _scene = scene;
            _bake = bake;
            _settings = settings;
            _scene.ReadMeans();
        }

        /// <summary>Whether it has not finished: waiting on its means, or baking.</summary>
        public bool Running => Problem == null && (Baker == null || !Baker.Done);

        /// <summary>
        /// Starts the baker once its means have landed: false while they have not, or when it could
        /// not start, which Problem then says.
        /// </summary>
        public bool TryStart() {
            if (_disposed) throw new ObjectDisposedException(nameof(BakeSession));
            if (Baker != null) return true;
            if (Problem != null || !_scene.MeansReady) return false;
            _scene.FillAlbedo(_bake.Albedo);
            _scene.Dispose();
            _scene = null;
            try {
                Baker = new LightBaker(_bake, _settings);
            } catch (Exception e) {
                Fail(e);
                return false;
            } finally {
                _bake = null;
            }
            return true;
        }

        /// <summary>
        /// Does `tiles` tiles of the bake (LightBaker.TilePaths paths each), at least one, once its
        /// means have landed: its progress, 1 once done, 0 while it waits, or Failed.
        /// </summary>
        public float Step(double tiles) {
            if (!TryStart()) return Problem != null ? Failed : 0;
            bool done;
            try {
                done = Baker.Step((long)Math.Max(1, Math.Round(tiles)) * Baker.TilePaths);
            } catch (InvalidOperationException e) {
                return Fail(e);
            }
            return done ? 1 : Mathf.Min(Baker.Progress, 0.999f);
        }

        float Fail(Exception e) {
            Problem = Words(e.Message);
            Baker?.Dispose();
            Baker = null;
            return Failed;
        }

        /// <summary>"[OneJS] The bake's probes could not be read: X." as "the bake's probes could not be read: X", for a line that says it.</summary>
        internal static string Words(string message) {
            var text = (message ?? "").Trim();
            if (text.StartsWith("[OneJS] ")) text = text.Substring("[OneJS] ".Length);
            if (text.EndsWith(".")) text = text.Substring(0, text.Length - 1);
            if (text.Length > 0 && char.IsUpper(text[0]) && !(text.Length > 1 && char.IsUpper(text[1]))) text = char.ToLowerInvariant(text[0]) + text.Substring(1);
            return text == "" ? "the bake failed" : text;
        }

        /// <summary>
        /// The finished bake as a light file (LightFile), base64, or "" while it has not finished or
        /// its lightmap's read has not landed. Read once: later calls answer the same file.
        /// </summary>
        public string File() {
            if (Baker == null || !Baker.Done || Baker.Lightmap == null) return "";
            if (_file != null) return _file;
            var map = Baker.Lightmap;
            var pages = Baker.Pages;
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
            _file = Convert.ToBase64String(LightFile.Encode(light, map.width, map.height, pages, Baker.ProbeSH));
            return _file;
        }

        /// <summary>The baker's lightmap, its own now: Dispose leaves it for the caller to release. Null when there is none.</summary>
        public RenderTexture TakeLightmap() => Baker?.TakeLightmap();

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _scene?.Dispose();
            _scene = null;
            Baker?.Dispose();
            Baker = null;
            _fileReads = null;
        }
    }
}
