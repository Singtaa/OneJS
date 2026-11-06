using System;
using UnityEngine;

namespace OneJS.Lighting {
    /// <summary>
    /// A bounding volume hierarchy over triangles, built on the CPU and walked by LightBake.shader.
    ///
    /// Median split on the longest axis of the centroids, leaves of up to four triangles. The nodes
    /// are laid out depth first, so a node's left child is the next node and only the right one is
    /// named. Each node is eight floats: min xyz, then the right child (inner) or the first triangle
    /// (leaf); max xyz, then 0 (inner) or the triangle count (leaf).
    /// </summary>
    public static class Bvh {
        public const int LeafSize = 4;

        /// <summary>The nodes, and the order the triangles must be stored in for the leaves to name them.</summary>
        public static (float[] nodes, int[] order) Build(Vector3[] positions, int[] indices) {
            var count = indices.Length / 3;
            var order = new int[count];
            for (var i = 0; i < count; i++) order[i] = i;
            if (count == 0) return (Array.Empty<float>(), order);

            var lo = new Vector3[count];
            var hi = new Vector3[count];
            var centre = new Vector3[count];
            for (var t = 0; t < count; t++) {
                Vector3 a = positions[indices[t * 3]], b = positions[indices[t * 3 + 1]], c = positions[indices[t * 3 + 2]];
                lo[t] = Vector3.Min(a, Vector3.Min(b, c));
                hi[t] = Vector3.Max(a, Vector3.Max(b, c));
                centre[t] = (a + b + c) / 3f;
            }

            // A median split tree over n triangles has fewer than 2n / LeafSize * 2 + 1 nodes.
            var nodes = new float[(count * 2 + 1) * 8];
            var next = 0;
            var keys = new float[count];

            int BuildRange(int from, int to) {
                var me = next++;
                Vector3 bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bmax = -bmin;
                Vector3 cmin = bmin, cmax = bmax;
                for (var i = from; i < to; i++) {
                    var t = order[i];
                    bmin = Vector3.Min(bmin, lo[t]);
                    bmax = Vector3.Max(bmax, hi[t]);
                    cmin = Vector3.Min(cmin, centre[t]);
                    cmax = Vector3.Max(cmax, centre[t]);
                }
                var o = me * 8;
                nodes[o] = bmin.x; nodes[o + 1] = bmin.y; nodes[o + 2] = bmin.z;
                nodes[o + 4] = bmax.x; nodes[o + 5] = bmax.y; nodes[o + 6] = bmax.z;
                if (to - from <= LeafSize) {
                    nodes[o + 3] = from;
                    nodes[o + 7] = to - from;
                    return me;
                }
                var extent = cmax - cmin;
                var axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                for (var i = from; i < to; i++) keys[i] = centre[order[i]][axis];
                // Array.Sort is deterministic for the same keys in the same order, so one scene always builds one tree.
                Array.Sort(keys, order, from, to - from);
                var half = (from + to) / 2;
                BuildRange(from, half);
                nodes[o + 3] = BuildRange(half, to);
                nodes[o + 7] = 0;
                return me;
            }

            BuildRange(0, count);
            Array.Resize(ref nodes, next * 8);
            return (nodes, order);
        }
    }
}
