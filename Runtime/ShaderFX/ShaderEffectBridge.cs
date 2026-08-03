using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.ShaderFX {
    /// <summary>
    /// Registry, clock and shared-resource provider for ShaderEffectElement.
    ///
    /// TickAll is driven from QuickJSUIBridge.Tick(), the same integration point
    /// the particle engine uses, so effects animate in play mode, edit-mode
    /// preview and JSPad without a per-mode code path. dt is derived from
    /// realtime deltas and guarded so two live bridges in one frame do not
    /// double-step.
    ///
    /// The built-in textures exist so an effect can ship as a shader plus a JS
    /// wrapper with no art at all. Tiling value noise and gradients are exactly
    /// the kind of art an algorithm is good at, unlike a hand-painted silhouette
    /// (see Assets/DevSpikes/PARTICLE_FLAME_FINDINGS.md for that lesson).
    /// </summary>
    public static class ShaderEffectBridge {
        static readonly List<ShaderEffectElement> s_Elements = new List<ShaderEffectElement>();
        static double s_LastTick;

        internal static void Register(ShaderEffectElement e) {
            if (!s_Elements.Contains(e)) s_Elements.Add(e);
        }

        internal static void Unregister(ShaderEffectElement e) => s_Elements.Remove(e);

        public static int LiveEffectCount => s_Elements.Count;

        /// <summary>Advances every live effect. Safe to call from multiple bridges per frame.</summary>
        public static void TickAll() {
            // Same clock as ParticleBridge, so shader effects step deterministically
            // under the offline panel recorder instead of following wall time.
            double now = VirtualClock.RealtimeSeconds;
            float dt = (float)(now - s_LastTick);
            // The clock can step backwards when the recorder hands control back to
            // engine realtime, or across a domain reload. Resync rather than stalling:
            // a plain `return` here leaves every later frame failing the same test and
            // wedges the effect permanently.
            if (dt < 0f) {
                s_LastTick = now;
                return;
            }
            if (dt <= 0.0005f) return; // second bridge ticking the same frame
            s_LastTick = now;
            // Under a virtual clock dt is exactly what the renderer asked for, so the
            // hitch clamp would silently slow the effect at low frame rates.
            if (dt > 0.05f && !VirtualClock.IsActive) dt = 0.05f; // first tick / editor hitches

            for (int i = s_Elements.Count - 1; i >= 0; i--)
                s_Elements[i]?.Tick(dt);
        }

        /// <summary>Context teardown safety net: JS effect cleanups run first.</summary>
        public static void DisposeAll() {
            for (int i = s_Elements.Count - 1; i >= 0; i--)
                s_Elements[i]?.Dispose();
            s_Elements.Clear();
        }

        // MARK: built-in procedural textures (lazy, shared, recreated after domain reload)

        static readonly Dictionary<string, Texture2D> s_Builtins = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Named procedural texture. "noise" / "noise:SEED" is seamlessly tiling
        /// value noise; "flame-mask" is a flame-shaped falloff; "radial-mask" a
        /// soft disc. Returns null for an unknown name.
        /// </summary>
        public static Texture2D GetBuiltinTexture(string name) {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_Builtins.TryGetValue(name, out var cached) && cached != null) return cached;

            Texture2D tex;
            if (name == "flame-mask") tex = BuildFlameMask(128, 256);
            else if (name == "radial-mask") tex = BuildRadialMask(128);
            else if (name == "noise" || name.StartsWith("noise:")) {
                int seed = 1;
                if (name.Length > 6 && !int.TryParse(name.Substring(6), out seed)) seed = 1;
                tex = BuildTilingNoise(128, seed);
            } else {
                Debug.LogWarning($"[OneJS ShaderFX] unknown built-in texture \"{name}\".");
                return null;
            }
            s_Builtins[name] = tex;
            return tex;
        }

        /// <summary>
        /// 256x1 gradient from evenly spaced RGBA stops (flat float array, 4 per
        /// stop). Alpha is carried through, so an effect's transparency and colour
        /// are authored in the same place. Ramps are cached by content.
        /// </summary>
        public static Texture2D BuildRamp(float[] rgba) {
            if (rgba == null || rgba.Length < 8 || rgba.Length % 4 != 0) {
                Debug.LogWarning("[OneJS ShaderFX] ramp needs at least 2 stops as a flat RGBA array.");
                return null;
            }
            var key = "ramp:" + string.Join(",", rgba);
            if (s_Builtins.TryGetValue(key, out var cached) && cached != null) return cached;

            const int N = 256;
            int stops = rgba.Length / 4;
            var tex = new Texture2D(N, 1, TextureFormat.RGBA32, false) {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[N];
            for (int i = 0; i < N; i++) {
                float t = (float)i / (N - 1) * (stops - 1);
                int a = Mathf.Min(stops - 2, Mathf.FloorToInt(t));
                float f = Mathf.Clamp01(t - a);
                int ia = a * 4, ib = (a + 1) * 4;
                px[i] = new Color32(
                    (byte)(Mathf.Lerp(rgba[ia], rgba[ib], f) * 255f + 0.5f),
                    (byte)(Mathf.Lerp(rgba[ia + 1], rgba[ib + 1], f) * 255f + 0.5f),
                    (byte)(Mathf.Lerp(rgba[ia + 2], rgba[ib + 2], f) * 255f + 0.5f),
                    (byte)(Mathf.Lerp(rgba[ia + 3], rgba[ib + 3], f) * 255f + 0.5f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_Builtins[key] = tex;
            return tex;
        }

        // MARK: generators

        static uint Hash(uint x) {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        static float Rand(int x, int y, int period, int seed) {
            // Wrapping the lattice is what makes the noise tile seamlessly, which
            // matters because the shader scrolls it forever.
            uint ix = (uint)(((x % period) + period) % period);
            uint iy = (uint)(((y % period) + period) % period);
            return (Hash(ix * 374761393u + iy * 668265263u + (uint)seed * 2246822519u) & 0xFFFFFF) / 16777216f;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static float ValueNoise(float x, float y, int period, int seed) {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = Smooth(xf), v = Smooth(yf);
            float a = Rand(xi, yi, period, seed), b = Rand(xi + 1, yi, period, seed);
            float c = Rand(xi, yi + 1, period, seed), d = Rand(xi + 1, yi + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Seamlessly tiling fBm value noise. Tiles because every octave's lattice wraps.</summary>
        static Texture2D BuildTilingNoise(int size, int seed) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = $"OneJS_Noise_{seed}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float u = (float)x / size, v = (float)y / size;
                    float amp = 0.5f, sum = 0f, norm = 0f;
                    int period = 4;
                    for (int o = 0; o < 4; o++) {
                        sum += ValueNoise(u * period, v * period, period, seed + o * 7919) * amp;
                        norm += amp;
                        amp *= 0.5f;
                        period *= 2;
                    }
                    byte n = (byte)(Mathf.Clamp01(sum / norm) * 255f + 0.5f);
                    px[y * size + x] = new Color32(n, n, n, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Flame silhouette falloff: hot and wide at the base, pinching to nothing
        /// at the tip, soft at the sides. The shader multiplies noise by this, so
        /// it is what stops the effect being a full-rect noise field.
        /// </summary>
        static Texture2D BuildFlameMask(int w, int h) {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) {
                name = "OneJS_FlameMask",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) {
                // v = 0 at the base of the flame, 1 at the tip.
                float v = (float)y / (h - 1);
                // Width tapers toward the tip; the exponent keeps the base broad.
                float halfWidth = 0.52f * Mathf.Pow(1f - v, 0.55f);
                // Vertical envelope: quick ramp off the base, long fade to the tip.
                float vert = Mathf.Clamp01(v / 0.06f) * Mathf.Pow(1f - v, 0.85f);
                for (int x = 0; x < w; x++) {
                    float u = (float)x / (w - 1) * 2f - 1f; // -1..1
                    float d = halfWidth > 1e-4f ? Mathf.Abs(u) / halfWidth : 999f;
                    float side = Mathf.Clamp01(1f - d);
                    side = side * side * (3f - 2f * side); // smoothstep for soft edges
                    byte m = (byte)(Mathf.Clamp01(side * vert) * 255f + 0.5f);
                    px[y * w + x] = new Color32(m, m, m, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D BuildRadialMask(int size) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "OneJS_RadialMask",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float f = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte m = (byte)(f * f * 255f + 0.5f);
                    px[y * size + x] = new Color32(m, m, m, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
