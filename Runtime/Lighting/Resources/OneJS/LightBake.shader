// The passes LightBaker draws to bake light at runtime (Runtime/Lighting/README.md).
//
// Fragment shaders, not compute, so the same passes run on WebGL 2. Every pass but the G-buffer
// draws a full-target triangle and reads its inputs with Load at its own pixel, so they agree on
// which texel is which on every platform; only the G-buffer places geometry, by its atlas uv.
//
// Light is in the units ModelLit lights with: a surface of albedo 1 under a sky of radiance S is
// lit S, as SampleSH lights it under the ambient probe, and a sun of colour C lights a surface
// facing it C. The bake leaves out the sun's first hit, which stays live; everything after it,
// and the sky and ground, are baked.
Shader "Hidden/OneJS/LightBake" {
    SubShader {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #pragma target 3.5
        #include "UnityCG.cginc"

        // Triangles, five texels each: v0, edge 1, edge 2, geometric normal, linear albedo.
        Texture2D<float4> _Tris;
        // BVH nodes, two texels each: (min, right child or first triangle), (max, triangle count; 0 for an inner node).
        Texture2D<float4> _Nodes;
        int _DataWidth;
        int _NodeCount;

        // The world beyond the scene: radiance from above the horizon and from below it.
        float3 _Sky;
        float3 _Ground;
        // Towards the sun, its colour times intensity, and its angular radius in radians.
        float3 _SunDir;
        float3 _SunColor;
        float _SunAngle;
        int _Bounces;
        // Extra darkening, 0 to 1, of a lightmap texel by how much geometry is within OCCLUSION_REACH of it.
        float _Occlusion;
        uint _Pass;

        static const float PI = 3.14159265;
        // How far a ray starts off the surface it leaves, in metres.
        static const float LIFT = 2e-3;
        // How near, in metres, geometry has to be for _Occlusion to darken the light it sends: fixed, not an option.
        static const float OCCLUSION_REACH = 1.0;

        float4 tri(int k) { return _Tris.Load(int3(k % _DataWidth, k / _DataWidth, 0)); }
        float4 node(int k) { return _Nodes.Load(int3(k % _DataWidth, k / _DataWidth, 0)); }

        static float best;
        // The triangle hit, or -1, and below the nodes and the stack, all held as float values. As ints, FXC
        // keeps them in typeless registers and HLSLcc's GLSL (WebGL 2) moves them through floats by their
        // bits, where a -1 is a NaN pattern the GPU may rewrite: every ray then counts as a hit, and the
        // bake is black. A float holds every index to 2^24 exactly.
        static float hit;

        // The triangles' indices are floats too: as an int, HLSLcc moves the counter through a float by its
        // bits, where triangle 5 is a denormal a GPU may flush to 0.
        void testTriangles(float3 o, float3 d, float from, float count, bool any) {
            for (float i = from; i < from + count; i++) {
                int k = (int)i * 5;
                float3 v0 = tri(k).xyz, e1 = tri(k + 1).xyz, e2 = tri(k + 2).xyz;
                float3 p = cross(d, e2);
                float det = dot(e1, p);
                if (abs(det) < 1e-12) continue;
                float inv = 1.0 / det;
                float3 s = o - v0;
                float u = dot(s, p) * inv;
                if (u < -1e-4 || u > 1.0001) continue;
                float3 q = cross(s, e1);
                float v = dot(d, q) * inv;
                if (v < -1e-4 || u + v > 1.0001) continue;
                float t = dot(e2, q) * inv;
                if (t > 1e-4 && t < best) {
                    best = t;
                    hit = i;
                    if (any) return;
                }
            }
        }

        // The nearest triangle along the ray, or any one when `any`: its distance, or -1 for none.
        float trace(float3 o, float3 d, bool any) {
            best = 1e30;
            hit = -1;
            if (_NodeCount == 0) return -1;
            float3 inv = 1.0 / (abs(d) < 1e-12 ? (d < 0 ? -1e-12 : 1e-12) : d);
            float stack[32];
            int sp = 0;
            float n = 0;
            [loop] for (int guard = 0; guard < 8192; guard++) {
                float4 a = node((int)n * 2), b = node((int)n * 2 + 1);
                float3 t0 = (a.xyz - o) * inv, t1 = (b.xyz - o) * inv;
                float3 tn = min(t0, t1), tf = max(t0, t1);
                float near = max(max(tn.x, tn.y), tn.z), far = min(min(tf.x, tf.y), tf.z);
                if (far >= max(near, 0.0) && near < best) {
                    if (b.w > 0) {
                        testTriangles(o, d, a.w, b.w, any);
                        if (any && hit >= 0) break;
                    } else if (sp < 32) {
                        // The left child follows its parent; the right one waits on the stack.
                        stack[sp++] = a.w;
                        n = n + 1;
                        continue;
                    }
                }
                if (sp == 0) break;
                n = stack[--sp];
            }
            return hit < 0 ? -1 : best;
        }

        static uint seed;
        uint pcg() {
            seed = seed * 747796405u + 2891336453u;
            uint w = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
            return (w >> 22u) ^ w;
        }
        float rnd() { return (float)pcg() / 4294967296.0; }
        void seedFor(uint a, uint b) {
            seed = a * 1973u + b * 9277u + _Pass * 26699u;
            pcg();
            pcg();
        }

        float3x3 basis(float3 n) {
            float3 t = abs(n.y) < 0.99 ? normalize(cross(n, float3(0, 1, 0))) : normalize(cross(n, float3(1, 0, 0)));
            return float3x3(t, cross(n, t), n);
        }
        float3 cosine(float3 n) {
            float r = sqrt(rnd()), a = 2.0 * PI * rnd();
            return mul(float3(r * cos(a), r * sin(a), sqrt(max(0.0, 1.0 - r * r))), basis(n));
        }
        float3 cone(float3 d, float angle) {
            float c = 1.0 - rnd() * (1.0 - cos(angle)), s = sqrt(1.0 - c * c), a = 2.0 * PI * rnd();
            return mul(float3(s * cos(a), s * sin(a), c), basis(d));
        }

        float3 environment(float3 d) { return d.y >= 0 ? _Sky : _Ground; }

        // The sun's light arriving at a point facing n, shadows included.
        float3 sunAt(float3 pos, float3 n) {
            float3 l = cone(_SunDir, _SunAngle);
            float c = dot(n, l);
            if (c <= 0 || dot(_SunColor, _SunColor) == 0) return 0;
            return trace(pos, l, true) < 0 ? _SunColor * c : 0;
        }

        // The light arriving at pos along d, from paths of up to _Bounces hits. A path that meets the back of
        // a face sets `inside` to 1: the point is in a solid. A float for the reason `hit` is one: as a bool,
        // FXC keeps it as an all-ones mask, a NaN pattern in HLSLcc's GLSL. `first` is how far the path went
        // before its first hit, or -1 when it met nothing.
        float3 incoming(float3 pos, float3 d, out float inside, out float first) {
            inside = 0;
            first = -1;
            float3 throughput = 1, light = 0;
            for (int b = 0; b <= _Bounces; b++) {
                float t = trace(pos, d, false);
                if (t < 0) {
                    light += throughput * environment(d);
                    break;
                }
                if (b == 0) first = t;
                int k = (int)hit;
                float3 hn = tri(k * 5 + 3).xyz;
                if (dot(hn, d) > 0) {
                    if (b == 0) inside = 1;
                    hn = -hn;
                }
                throughput *= tri(k * 5 + 4).xyz;
                pos = pos + d * t + hn * LIFT;
                light += throughput * sunAt(pos, hn);
                if (b == _Bounces) break;
                d = cosine(hn);
            }
            return light;
        }

        struct Full { float4 pos : SV_POSITION; };
        Full fullVert(uint id : SV_VertexID) {
            Full o;
            float2 p = float2((id << 1) & 2, id & 2);
            o.pos = float4(p * 2 - 1, 0, 1);
            return o;
        }
        ENDCG

        // 0: the G-buffer. Each static triangle drawn at its atlas uv, writing world position and normal.
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // The page being baked, with its apron, as a scale and offset of the sheet's uv: (1, 1, 0, 0) on one page.
            float4 _Window;

            struct In { float3 pos : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float3 centre : TEXCOORD1; };
            struct V2F { float4 clip : SV_POSITION; float3 pos : TEXCOORD0; float3 normal : TEXCOORD1; float3 centre : TEXCOORD2; };
            struct Out { float4 pos : SV_Target0; float4 normal : SV_Target1; };

            V2F vert(In v) {
                V2F o;
                float2 p = (v.uv * _Window.xy + _Window.zw) * 2 - 1;
                // Placed so a texture read at uv finds this texel, on every platform.
                #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
                #endif
                o.clip = float4(p, 0, 1);
                o.pos = v.pos;
                o.normal = v.normal;
                o.centre = v.centre;
                return o;
            }

            Out frag(V2F i) {
                Out o;
                // xatlas centres a chart's border texels on its edges, where the next face begins, so a ray
                // leaving one would cross that face at distance 0 and let the sky in along every corner.
                // Each texel moves 5 mm towards its triangle's centre, off the line.
                float3 d = i.centre - i.pos;
                float l = length(d);
                o.pos = float4(i.pos + (l > 1e-6 ? d / l * min(5e-3, l) : 0), 1);
                o.normal = float4(normalize(i.normal), 1);
                return o;
            }
            ENDCG
        }

        // 1: one pass of the lightmap: _Samples paths a texel, blended into the running mean with weight _Weight.
        Pass {
            Blend One OneMinusSrcAlpha
            ColorMask RGB
            CGPROGRAM
            #pragma vertex fullVert
            #pragma fragment frag
            Texture2D<float4> _GPos;
            Texture2D<float4> _GNormal;
            int _Samples;
            float _Weight;
            // The page's first texel on the sheet: a texel's place there, plus the apron, is its own plus this. 0 on one page.
            int _OriginX;
            int _OriginY;

            float4 frag(Full i) : SV_Target {
                int2 c = int2(i.pos.xy);
                float4 P = _GPos.Load(int3(c, 0));
                if (P.w == 0) return 0;
                float3 n = _GNormal.Load(int3(c, 0)).xyz;
                seedFor((uint)(c.x + _OriginX), (uint)(c.y + _OriginY));
                float3 sum = 0;
                float inside, first, near = 0;
                for (int s = 0; s < _Samples; s++) {
                    sum += incoming(P.xyz + n * LIFT, cosine(n), inside, first);
                    near += first >= 0 ? saturate(1.0 - first / OCCLUSION_REACH) : 0.0;
                }
                // Occlusion: the texel's light, from every direction, darkened by how near its paths' first hits
                // are, as ambient occlusion is, so corners and contact darken and open ground keeps its light. The
                // same paths say how near, so no ray is added; the running mean over passes smooths the estimate.
                float keep = 1.0 - _Occlusion * near / _Samples;
                return float4(sum / _Samples * keep * _Weight, _Weight);
            }
            ENDCG
        }

        // 2: one pass of the probes, as sums. Each probe is two texels, side by side, each writing four targets:
        // the left holds the first 16 of its 27 coefficients (coefficient 0's rgb, 1's rgb, and so on), the
        // right the other 11, then the probe's index plus one, then the rays that met the back of a face. The
        // index is what LightBaker reads a probe back by, so no platform's row order can mix two up.
        Pass {
            CGPROGRAM
            #pragma vertex fullVert
            #pragma fragment frag
            Texture2D<float4> _Prev0;
            Texture2D<float4> _Prev1;
            Texture2D<float4> _Prev2;
            Texture2D<float4> _Prev3;
            float3 _GridOrigin;
            float _GridSpacing;
            int3 _GridCounts;
            int _ProbesWide;
            int _Rays;
            // Rays every probe has had before this pass, and will have at the end.
            int _RayBase;
            int _RayTotal;
            struct Out { float4 a : SV_Target0; float4 b : SV_Target1; float4 c : SV_Target2; float4 d : SV_Target3; };

            Out frag(Full i) {
                int2 c = int2(i.pos.xy);
                Out o;
                o.a = _Prev0.Load(int3(c, 0));
                o.b = _Prev1.Load(int3(c, 0));
                o.c = _Prev2.Load(int3(c, 0));
                o.d = _Prev3.Load(int3(c, 0));
                int probe = c.y * _ProbesWide + c.x / 2;
                int count = _GridCounts.x * _GridCounts.y * _GridCounts.z;
                if (probe >= count) return o;
                int3 g = int3(probe % _GridCounts.x, (probe / _GridCounts.x) % _GridCounts.y, probe / (_GridCounts.x * _GridCounts.y));
                float3 at = _GridOrigin + float3(g) * _GridSpacing;
                // Both texels of a probe trace the same rays, so their halves are one projection.
                seedFor((uint)probe, 7u);
                float sh[9][3];
                for (int k = 0; k < 9; k++) { sh[k][0] = 0; sh[k][1] = 0; sh[k][2] = 0; }
                float back = 0;
                for (int r = 0; r < _Rays; r++) {
                    // The first ray of each path is the next point of a spherical Fibonacci set, each point once
                    // over the bake: random directions leave a probe's coefficients a few percent off at 1,024
                    // rays, where this set is an evenly spread sample of the sphere. The bounces stay random.
                    float i = (float)(_RayBase + r) + 0.5;
                    float height = 1.0 - 2.0 * i / (float)_RayTotal, ring = sqrt(max(0.0, 1.0 - height * height)), phi = i * 2.39996323;
                    float3 d = float3(ring * cos(phi), ring * sin(phi), height);
                    // Probes light what moves, which takes no occlusion: `first` goes unused.
                    float inside, first;
                    float3 L = incoming(at, d, inside, first);
                    back += inside;
                    // The real spherical harmonics, bands 0 to 2, in Unity's order.
                    float x = d.x, y = d.y, z = d.z;
                    float Y[9] = {
                        0.282095, 0.488603 * y, 0.488603 * z, 0.488603 * x,
                        1.092548 * x * y, 1.092548 * y * z, 0.315392 * (3 * z * z - 1), 1.092548 * x * z, 0.546274 * (x * x - y * y)
                    };
                    for (int k2 = 0; k2 < 9; k2++) { sh[k2][0] += L.r * Y[k2]; sh[k2][1] += L.g * Y[k2]; sh[k2][2] += L.b * Y[k2]; }
                }
                if ((c.x & 1) == 0) {
                    o.a += float4(sh[0][0], sh[0][1], sh[0][2], sh[1][0]);
                    o.b += float4(sh[1][1], sh[1][2], sh[2][0], sh[2][1]);
                    o.c += float4(sh[2][2], sh[3][0], sh[3][1], sh[3][2]);
                    o.d += float4(sh[4][0], sh[4][1], sh[4][2], sh[5][0]);
                } else {
                    o.a += float4(sh[5][1], sh[5][2], sh[6][0], sh[6][1]);
                    o.b += float4(sh[6][2], sh[7][0], sh[7][1], sh[7][2]);
                    o.c = float4(o.c.rgb + float3(sh[8][0], sh[8][1], sh[8][2]), probe + 1);
                    o.d += float4(back, 0, 0, 0);
                }
                return o;
            }
            ENDCG
        }

        // 3: dilation. An empty texel takes the mean of its covered neighbours, and is covered after.
        Pass {
            CGPROGRAM
            #pragma vertex fullVert
            #pragma fragment frag
            Texture2D<float4> _Src;
            float4 frag(Full i) : SV_Target {
                int2 c = int2(i.pos.xy);
                float4 v = _Src.Load(int3(c, 0));
                if (v.w > 0) return v;
                float4 s = 0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++) {
                        float4 w = _Src.Load(int3(c + int2(x, y), 0));
                        if (w.w > 0) s += float4(w.rgb, 1);
                    }
                return s.w > 0 ? float4(s.rgb / s.w, 1) : 0;
            }
            ENDCG
        }

        // 4: the mean with its coverage: the accumulator's colour where the G-buffer has a texel.
        Pass {
            CGPROGRAM
            #pragma vertex fullVert
            #pragma fragment frag
            Texture2D<float4> _Src;
            Texture2D<float4> _GPos;
            float4 frag(Full i) : SV_Target {
                int2 c = int2(i.pos.xy);
                // A product rather than a choice: HLSLcc moves a chosen texel through an int register and
                // back by its bits, the shape ShaderWebGL2NaNTests refuses. The accumulator is finite.
                float covered = _GPos.Load(int3(c, 0)).w > 0 ? 1.0 : 0.0;
                return float4(_Src.Load(int3(c, 0)).rgb, 1) * covered;
            }
            ENDCG
        }

        // 5: the denoiser: a filter over the texels of one surface, weighted by normal and distance. A texel
        // outside every chart keeps its dilated value, which is what bilinear sampling at a chart's edge reads.
        Pass {
            CGPROGRAM
            #pragma vertex fullVert
            #pragma fragment frag
            Texture2D<float4> _Src;
            Texture2D<float4> _GPos;
            Texture2D<float4> _GNormal;
            int _Radius;
            float _SigmaP;
            // Where the page's texels start in the working textures: past the apron.
            int _From;
            float4 frag(Full i) : SV_Target {
                int2 c = int2(i.pos.xy) + _From;
                float4 P = _GPos.Load(int3(c, 0));
                float4 v = _Src.Load(int3(c, 0));
                if (P.w == 0 || _Radius == 0) return v;
                float3 N = _GNormal.Load(int3(c, 0)).xyz;
                float3 s = 0;
                float ws = 0;
                for (int y = -_Radius; y <= _Radius; y++)
                    for (int x = -_Radius; x <= _Radius; x++) {
                        int3 k = int3(c + int2(x, y), 0);
                        float4 Q = _GPos.Load(k);
                        if (Q.w == 0) continue;
                        float3 M = _GNormal.Load(k).xyz;
                        float d = distance(P.xyz, Q.xyz);
                        float w = pow(max(0.0, dot(N, M)), 32.0) * exp(-d * d / (_SigmaP * _SigmaP));
                        s += _Src.Load(k).rgb * w;
                        ws += w;
                    }
                return float4(s / max(ws, 1e-6), 1);
            }
            ENDCG
        }
    }
}
