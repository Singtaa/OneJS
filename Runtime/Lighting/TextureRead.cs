using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OneJS.Lighting {
    /// <summary>
    /// A float or half-float render texture's texels read back to the CPU, as four floats a texel,
    /// red, green, blue and alpha, row 0 at the bottom as Unity reads a texture.
    ///
    /// Where the GPU has <see cref="AsyncGPUReadback"/> the read lands a frame or more later, polled
    /// through <see cref="Done"/>: WebGPU has it, and its `Texture2D.ReadPixels` logs "Texture
    /// Readback is not supported by WebGPU" and reads black. WebGL 2 has it too, through a pixel pack
    /// buffer. Where a GPU has not, the read is `ReadPixels`, done when started.
    /// </summary>
    public sealed class TextureRead {
        /// <summary>For tests: read with `ReadPixels` even where async readback exists, as a GPU without it does.</summary>
        internal static bool UseReadPixels;

        /// <summary>For tests: an async read does not land while this holds, as on a GPU slower than the test.</summary>
        internal static bool Hold;

        /// <summary>For tests: a read started while this holds fails, as one the GPU drops does.</summary>
        internal static bool Fail;

        /// <summary>Whether a read started now lands later rather than at once.</summary>
        public static bool Async => !UseReadPixels && SystemInfo.supportsAsyncGPUReadback;

        readonly int _width, _height;
        readonly bool _half;
        AsyncGPUReadbackRequest _request;
        bool _pending;
        float[] _texels;
        string _error;

        public int Width => _width;
        public int Height => _height;

        /// <summary>True once the texels have landed or the read failed (<see cref="Error"/>).</summary>
        public bool Done {
            get {
                Poll();
                return !_pending;
            }
        }

        /// <summary>Null until done, and when the read failed.</summary>
        public float[] Texels {
            get {
                Poll();
                return _texels;
            }
        }

        /// <summary>Why the read failed, or null.</summary>
        public string Error {
            get {
                Poll();
                return _error;
            }
        }

        /// <summary>Starts reading `rt`, which must be ARGBFloat or ARGBHalf, after everything drawn into it so far.</summary>
        public static TextureRead Start(RenderTexture rt) => new TextureRead(rt, 0, 0, rt.width, rt.height, 0);

        /// <summary>Starts reading the `width` by `height` block of `rt` at (x, y), of its slice `slice` when it is an array.</summary>
        public static TextureRead Start(RenderTexture rt, int x, int y, int width, int height, int slice = 0) => new TextureRead(rt, x, y, width, height, slice);

        /// <summary>Starts reading slice `slice` of the texture array `rt`, whole.</summary>
        public static TextureRead Start(RenderTexture rt, int slice) => new TextureRead(rt, 0, 0, rt.width, rt.height, slice);

        TextureRead(RenderTexture rt, int x, int y, int width, int height, int slice) {
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            if (rt.format != RenderTextureFormat.ARGBFloat && rt.format != RenderTextureFormat.ARGBHalf) {
                throw new ArgumentException($"[OneJS] a texture read takes an ARGBFloat or ARGBHalf target, and {rt.name} is {rt.format}");
            }
            _width = width;
            _height = height;
            _half = rt.format == RenderTextureFormat.ARGBHalf;
            if (Fail) {
                _error = "a test failed the read";
            } else if (Async) {
                _request = AsyncGPUReadback.Request(rt, 0, x, width, y, height, slice, 1);
                _pending = true;
                Poll();
            } else {
                _texels = ReadPixels(rt, x, y, width, height, slice, _half);
            }
        }

        /// <summary>Blocks until the read lands. For the editor and tests: a browser cannot wait on its GPU.</summary>
        public TextureRead Wait() {
            if (_pending) _request.WaitForCompletion();
            Poll();
            return this;
        }

        /// <summary>The texels as colours, null until done.</summary>
        public Color[] Colors() {
            var t = Texels;
            if (t == null) return null;
            var colors = new Color[t.Length / 4];
            for (int i = 0, k = 0; i < colors.Length; i++, k += 4) colors[i] = new Color(t[k], t[k + 1], t[k + 2], t[k + 3]);
            return colors;
        }

        void Poll() {
            if (!_pending || Hold) return;
            _request.Update();
            if (!_request.done) return;
            _pending = false;
            if (_request.hasError) {
                _error = "the GPU could not read the texture back";
                return;
            }
            var count = _width * _height * 4;
            if (_half) {
                var halves = _request.GetData<ushort>();
                if (halves.Length < count) {
                    _error = $"the GPU read back {halves.Length} halves, not {count}";
                    return;
                }
                _texels = new float[count];
                for (var i = 0; i < count; i++) _texels[i] = Mathf.HalfToFloat(halves[i]);
            } else {
                var floats = _request.GetData<float>();
                if (floats.Length < count) {
                    _error = $"the GPU read back {floats.Length} floats, not {count}";
                    return;
                }
                _texels = new float[count];
                floats.GetSubArray(0, count).CopyTo(_texels);
            }
        }

        static float[] ReadPixels(RenderTexture rt, int x, int y, int width, int height, int slice, bool half) {
            var tex = new Texture2D(width, height, half ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try {
                if (rt.dimension == TextureDimension.Tex2DArray) Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, slice);
                else RenderTexture.active = rt;
                tex.ReadPixels(new Rect(x, y, width, height), 0, 0, false);
                if (!half) return tex.GetPixelData<float>(0).ToArray();
                var colors = tex.GetPixels();
                var texels = new float[colors.Length * 4];
                for (int i = 0, k = 0; i < colors.Length; i++, k += 4) {
                    texels[k] = colors[i].r;
                    texels[k + 1] = colors[i].g;
                    texels[k + 2] = colors[i].b;
                    texels[k + 3] = colors[i].a;
                }
                return texels;
            } finally {
                RenderTexture.active = previous;
                if (Application.isPlaying) Object.Destroy(tex);
                else Object.DestroyImmediate(tex);
            }
        }
    }
}
