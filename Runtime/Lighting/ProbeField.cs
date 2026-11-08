using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Lighting {
    /// <summary>
    /// A bake's probes and the grid they stand on: what lights a moving thing where it stands.
    /// Each probe is LightBaker.ProbeSH's 27 floats, in the grid's x, then y, then z order.
    /// </summary>
    internal sealed class ProbeField {
        public readonly float[] Probes;
        public readonly Vector3 Origin;
        /// <summary>The distance between probes on each axis.</summary>
        public readonly Vector3 Spacing;
        public readonly Vector3Int Counts;

        public ProbeField(float[] probes, Vector3 origin, Vector3 spacing, Vector3Int counts) {
            if (probes == null) throw new ArgumentNullException(nameof(probes));
            if (Problem(spacing, counts) is { } problem) throw new ArgumentException(problem);
            if (probes.Length != counts.x * counts.y * counts.z * ProbeSolve.Floats) throw new ArgumentException($"{counts.x * counts.y * counts.z} probes are {counts.x * counts.y * counts.z * ProbeSolve.Floats} floats, not {probes.Length}");
            Probes = probes;
            Origin = origin;
            Spacing = spacing;
            Counts = counts;
        }

        /// <summary>What is wrong with a grid of these spacings and counts, or null when it can stand.</summary>
        public static string Problem(Vector3 spacing, Vector3Int counts) {
            if (counts.x < 0 || counts.y < 0 || counts.z < 0) return $"a probe grid's counts are 0 or more, not {counts}";
            if (counts.x * counts.y * counts.z > 0 && !(spacing.x > 0 && spacing.y > 0 && spacing.z > 0)) return $"a probe grid needs a spacing above 0 on every axis, not {spacing}";
            return null;
        }

        /// <summary>The light at `at`, blended from the eight probes round it; outside the grid, the nearest edge's.</summary>
        public SphericalHarmonicsL2 Blend(Vector3 at) {
            var sh = new SphericalHarmonicsL2();
            if (Counts.x * Counts.y * Counts.z == 0) return sh;
            // Per axis, the cell's lower corner and how far into it; scalars, since this runs for each moved actor each frame.
            Axis(at.x, Origin.x, Spacing.x, Counts.x, out var cx, out var fx);
            Axis(at.y, Origin.y, Spacing.y, Counts.y, out var cy, out var fy);
            Axis(at.z, Origin.z, Spacing.z, Counts.z, out var cz, out var fz);
            for (int corner = 0; corner < 8; corner++) {
                int ux = corner & 1, uy = (corner >> 1) & 1, uz = (corner >> 2) & 1;
                var w = (ux == 1 ? fx : 1 - fx) * (uy == 1 ? fy : 1 - fy) * (uz == 1 ? fz : 1 - fz);
                if (w == 0) continue;
                int ix = Math.Min(cx + ux, Counts.x - 1), iy = Math.Min(cy + uy, Counts.y - 1), iz = Math.Min(cz + uz, Counts.z - 1);
                var at0 = (ix + Counts.x * (iy + Counts.y * iz)) * ProbeSolve.Floats;
                for (int c = 0; c < 3; c++) {
                    for (int i = 0; i < 9; i++) sh[c, i] += w * Probes[at0 + c * 9 + i];
                }
            }
            return sh;
        }

        static void Axis(float at, float origin, float spacing, int n, out int cell, out float frac) {
            var t = Mathf.Clamp((at - origin) / spacing, 0, n - 1);
            cell = Math.Min((int)Math.Floor(t), Math.Max(0, n - 2));
            frac = n == 1 ? 0 : t - cell;
        }
    }
}
