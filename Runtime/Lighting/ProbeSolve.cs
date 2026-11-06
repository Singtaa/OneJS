using System;
using UnityEngine;

namespace OneJS.Lighting {
    /// <summary>
    /// Turns the probe pass's sums into the light at each probe, as Unity's ambient probe holds it.
    ///
    /// The pass sums radiance times each real spherical harmonic over uniform rays, so a sum times
    /// 4π over the rays is the radiance's projection. SphericalHarmonicsL2 holds something else: the
    /// light a surface of albedo 1 receives, as a polynomial in the normal (Unity folds the basis
    /// constants into its coefficients, which is why AddAmbientLight(white) makes the first one 1).
    /// Receiving is the projection convolved with a cosine, which scales band l by A_l / π: 1, 2/3
    /// and 1/4. So each coefficient is the projection times that, times its basis constant.
    /// </summary>
    public static class ProbeSolve {
        public const int Floats = 27;

        /// <summary>Each coefficient's basis constant, in Unity's order (1, y, z, x, xy, yz, 3z² - 1, xz, x² - y²).</summary>
        static readonly float[] Basis = { 0.282095f, 0.488603f, 0.488603f, 0.488603f, 1.092548f, 1.092548f, 0.315392f, 1.092548f, 0.546274f };
        /// <summary>The cosine's convolution over π, by band.</summary>
        static readonly float[] Band = { 1f, 2f / 3f, 2f / 3f, 2f / 3f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f };

        /// <summary>
        /// Every probe's 27 floats (nine coefficients of red, then green, then blue), from the pass's four
        /// targets read back, `width` texels a row. Columns keep their order on every platform, so a probe's
        /// left texel is the one before its right; rows may not, so each probe is placed by the index its
        /// right texel carries. A probe inside a solid takes its neighbours' mean; a
        /// grid entirely inside takes the bare ambient of `sky` and `ground`.
        /// </summary>
        public static float[] FromSums(float[][] targets, int width, int probes, int rays, Vector3Int counts, Color sky, Color ground) {
            var sh = new float[probes * Floats];
            var valid = new bool[probes];
            var flat = new float[32];
            var texels = targets[0].Length / 4;
            for (var t = 1; t < texels; t += 2) {
                // The right texel of a probe carries its index plus one in its third target's alpha.
                if (t % width % 2 != 1) throw new ArgumentException("[OneJS] the probe targets' rows must be an even number of texels");
                var index = (int)Math.Round(targets[2][t * 4 + 3]) - 1;
                if (index < 0 || index >= probes) continue;
                for (var i = 0; i < 4; i++)
                    for (var c = 0; c < 4; c++) {
                        flat[i * 4 + c] = targets[i][(t - 1) * 4 + c];
                        flat[16 + i * 4 + c] = targets[i][t * 4 + c];
                    }
                var scale = 4f * Mathf.PI / rays;
                for (var k = 0; k < 9; k++)
                    for (var c = 0; c < 3; c++)
                        sh[index * Floats + c * 9 + k] = flat[k * 3 + c] * scale * Band[k] * Basis[k];
                var back = flat[16 + 12] / rays;
                valid[index] = back <= LightBaker.InsideShare;
            }
            Fill(sh, valid, counts, sky, ground);
            return sh;
        }

        /// <summary>Gives each invalid probe the mean of its valid neighbours, a ring at a time, so light fills in from outside a solid.</summary>
        public static void Fill(float[] sh, bool[] valid, Vector3Int counts, Color sky, Color ground) {
            var probes = valid.Length;
            var any = false;
            foreach (var v in valid) any |= v;
            if (!any) {
                for (var p = 0; p < probes; p++) {
                    Array.Clear(sh, p * Floats, Floats);
                    for (var c = 0; c < 3; c++) {
                        sh[p * Floats + c * 9] = (sky[c] + ground[c]) * 0.5f;
                        sh[p * Floats + c * 9 + 1] = (sky[c] - ground[c]) * 0.5f;
                    }
                }
                return;
            }
            var next = (bool[])valid.Clone();
            var sum = new float[Floats];
            for (var changed = true; changed;) {
                changed = false;
                for (var p = 0; p < probes; p++) {
                    if (valid[p]) continue;
                    int x = p % counts.x, y = p / counts.x % counts.y, z = p / (counts.x * counts.y);
                    var n = 0;
                    Array.Clear(sum, 0, Floats);
                    void Take(int q) {
                        if (!valid[q]) return;
                        for (var i = 0; i < Floats; i++) sum[i] += sh[q * Floats + i];
                        n++;
                    }
                    if (x > 0) Take(p - 1);
                    if (x < counts.x - 1) Take(p + 1);
                    if (y > 0) Take(p - counts.x);
                    if (y < counts.y - 1) Take(p + counts.x);
                    if (z > 0) Take(p - counts.x * counts.y);
                    if (z < counts.z - 1) Take(p + counts.x * counts.y);
                    if (n == 0) continue;
                    for (var i = 0; i < Floats; i++) sh[p * Floats + i] = sum[i] / n;
                    next[p] = true;
                    changed = true;
                }
                Array.Copy(next, valid, probes);
            }
        }

        /// <summary>One probe's floats as Unity's own type.</summary>
        public static UnityEngine.Rendering.SphericalHarmonicsL2 ToUnity(float[] sh, int probe) {
            var u = new UnityEngine.Rendering.SphericalHarmonicsL2();
            for (var c = 0; c < 3; c++)
                for (var k = 0; k < 9; k++)
                    u[c, k] = sh[probe * Floats + c * 9 + k];
            return u;
        }
    }
}
