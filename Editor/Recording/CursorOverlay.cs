using UnityEngine;

namespace OneJS.Editor {
    /// <summary>
    /// Draws a pointer cursor into recorded frames so a viewer can see what the
    /// scripted <see cref="InputTrack"/> is doing.
    ///
    /// The arrow is rasterized procedurally at construction, so there is no texture
    /// asset to ship or to go missing. Compositing happens on the CPU directly into
    /// the raw frame buffer rather than through GL, which avoids depending on any
    /// particular shader being present in the project's render pipeline and keeps
    /// the result byte-for-byte predictable.
    /// </summary>
    public sealed class CursorOverlay {
        // Classic arrow outline in its own units. The first point is the hotspot.
        static readonly Vector2[] Arrow = {
            new Vector2(0f, 0f),
            new Vector2(0f, 16f),
            new Vector2(4.2f, 12.4f),
            new Vector2(6.8f, 18.6f),
            new Vector2(9.4f, 17.6f),
            new Vector2(6.8f, 11.6f),
            new Vector2(11.6f, 11.6f),
        };

        const float ArrowHeightUnits = 18.6f;
        const float OutlineUnits = 1.1f;
        const int SubSamples = 3; // per axis, so 9 samples per pixel
        const double RippleSeconds = 0.42;

        readonly int _w, _h, _pad;
        readonly float[] _a;    // coverage, 0..1
        readonly float[] _lum;  // 0 = outline black, 1 = fill white
        readonly int _pressRadius;

        public CursorOverlay(int heightPx) {
            heightPx = Mathf.Max(8, heightPx);
            var scale = heightPx / ArrowHeightUnits;
            var pad = Mathf.CeilToInt((OutlineUnits + 1f) * scale);
            _pad = pad;

            _w = Mathf.CeilToInt(11.6f * scale) + pad * 2;
            _h = Mathf.CeilToInt(ArrowHeightUnits * scale) + pad * 2;
            _a = new float[_w * _h];
            _lum = new float[_w * _h];
            _pressRadius = Mathf.RoundToInt(heightPx * 0.42f);

            var inv = 1f / scale;
            var step = 1f / (SubSamples + 1);

            for (int y = 0; y < _h; y++) {
                for (int x = 0; x < _w; x++) {
                    float inside = 0f, outline = 0f;
                    for (int sy = 1; sy <= SubSamples; sy++) {
                        for (int sx = 1; sx <= SubSamples; sx++) {
                            var p = new Vector2(
                                (x + sx * step - pad) * inv,
                                (y + sy * step - pad) * inv);
                            if (Contains(p)) inside += 1f;
                            else if (DistanceToEdge(p) < OutlineUnits) outline += 1f;
                        }
                    }
                    var total = SubSamples * SubSamples;
                    inside /= total;
                    outline /= total;
                    var alpha = Mathf.Clamp01(inside + outline);
                    _a[y * _w + x] = alpha;
                    _lum[y * _w + x] = alpha > 0.0001f ? inside / alpha : 0f;
                }
            }
        }

        /// <summary>
        /// Blends the cursor into a bottom-up RGBA frame (the layout
        /// <c>Texture2D.ReadPixels</c> produces). <paramref name="pos"/> is the
        /// pointer position in UI space, with the origin at the top left.
        /// </summary>
        public void Composite(byte[] frame, int frameW, int frameH, Vector2 pos,
                              bool pressed, double timeSincePress) {
            if (frame == null) return;

            var hotX = Mathf.RoundToInt(pos.x);
            var hotY = Mathf.RoundToInt(pos.y);

            // Held state first (steady, shows a drag in progress), then the ripple on
            // top so a click still reads during a long press.
            if (pressed) CompositePressRing(frame, frameW, frameH, hotX, hotY);
            if (timeSincePress >= 0.0 && timeSincePress < RippleSeconds)
                CompositeRipple(frame, frameW, frameH, hotX, hotY, (float)(timeSincePress / RippleSeconds));

            for (int y = 0; y < _h; y++) {
                for (int x = 0; x < _w; x++) {
                    var a = _a[y * _w + x];
                    if (a <= 0.002f) continue;
                    // Arrow art has +y downward, matching UI space.
                    var v = _lum[y * _w + x];
                    Blend(frame, frameW, frameH,
                        hotX + x - _pad, hotY + y - _pad,
                        v, v, v, a);
                }
            }
        }

        /// <summary>
        /// Expanding, fading ring marking a click. <paramref name="u"/> runs 0 to 1
        /// over <see cref="RippleSeconds"/>. This outlives the few frames a press
        /// actually lasts, which is what makes a click legible at normal speed.
        /// </summary>
        void CompositeRipple(byte[] frame, int frameW, int frameH, int cx, int cy, float u) {
            var maxR = _pressRadius * 2.4f;
            var radius = Mathf.Lerp(_pressRadius * 0.5f, maxR, u * (2f - u)); // ease out
            var alpha = 0.55f * (1f - u) * (1f - u);
            var thickness = Mathf.Max(1.5f, _pressRadius * 0.30f);
            var outer = Mathf.CeilToInt(radius + thickness);

            for (int dy = -outer; dy <= outer; dy++) {
                for (int dx = -outer; dx <= outer; dx++) {
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var edge = Mathf.Abs(d - radius);
                    if (edge > thickness) continue;
                    var a = alpha * (1f - edge / thickness);
                    if (a <= 0.004f) continue;
                    Blend(frame, frameW, frameH, cx + dx, cy + dy, 1f, 1f, 1f, a);
                }
            }
        }

        void CompositePressRing(byte[] frame, int frameW, int frameH, int cx, int cy) {
            var r = _pressRadius;
            for (int dy = -r; dy <= r; dy++) {
                for (int dx = -r; dx <= r; dx++) {
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    // Soft disc, strongest at the centre, fading out at the rim.
                    var a = 0.30f * (1f - d / r);
                    Blend(frame, frameW, frameH, cx + dx, cy + dy, 1f, 1f, 1f, a);
                }
            }
        }

        /// <summary>Alpha-blends one pixel, converting UI y (top-down) to buffer y (bottom-up).</summary>
        static void Blend(byte[] frame, int frameW, int frameH, int uiX, int uiY,
                          float r, float g, float b, float a) {
            if (uiX < 0 || uiX >= frameW || uiY < 0 || uiY >= frameH) return;
            var row = frameH - 1 - uiY;
            var i = (row * frameW + uiX) * 4;
            frame[i + 0] = (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f * a + frame[i + 0] * (1f - a)), 0, 255);
            frame[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f * a + frame[i + 1] * (1f - a)), 0, 255);
            frame[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f * a + frame[i + 2] * (1f - a)), 0, 255);
            // Leave alpha alone: the encoder ignores it and the panel already wrote it.
        }

        static bool Contains(Vector2 p) {
            var inside = false;
            for (int i = 0, j = Arrow.Length - 1; i < Arrow.Length; j = i++) {
                if (Arrow[i].y > p.y != Arrow[j].y > p.y &&
                    p.x < (Arrow[j].x - Arrow[i].x) * (p.y - Arrow[i].y) / (Arrow[j].y - Arrow[i].y) + Arrow[i].x)
                    inside = !inside;
            }
            return inside;
        }

        static float DistanceToEdge(Vector2 p) {
            var best = float.MaxValue;
            for (int i = 0, j = Arrow.Length - 1; i < Arrow.Length; j = i++) {
                var d = DistanceToSegment(p, Arrow[j], Arrow[i]);
                if (d < best) best = d;
            }
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-6f) return Vector2.Distance(p, a);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
