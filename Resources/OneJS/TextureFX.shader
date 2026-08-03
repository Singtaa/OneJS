// Layer-stack procedural texture for the OneJS ShaderEffect element.
//
// One shader evaluates a small stack of layers described from JS: each layer
// picks a source (noise / shape / constant), is scaled and scrolled, and blends
// into an accumulator. The accumulator is then eroded and mapped through a
// colour ramp.
//
// Why a fixed stack rather than generating shader code from the JS graph:
// runtime shader compilation is not available in player builds, so the graph has
// to be *data*. Uniform arrays plus a bounded loop is the shape that works
// everywhere, and it keeps the whole effect to one draw with no compilation step.
//
// Noise is computed procedurally rather than sampled, so any seed works without
// shipping a texture per seed. It does not need to tile either: the field is
// infinite, so scrolling never repeats.
Shader "OneJS/TextureFX"
{
    Properties
    {
        _Ramp ("Colour Ramp", 2D) = "white" {}
        _Secs ("Time (seconds)", Float) = 0
        _Speed ("Speed", Float) = 1
        _LayerCount ("Layer count", Float) = 0
        _Threshold ("Threshold", Float) = 0
        _Softness ("Softness", Float) = 1
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define MAX_LAYERS 6

            sampler2D _Ramp;
            float _Secs, _Speed, _LayerCount, _Threshold, _Softness, _FlipY;

            // xy = scale, z = octaves, w = seed
            float4 _LScale[MAX_LAYERS];
            // xy = scroll velocity, z = amount, w = shape id
            float4 _LScroll[MAX_LAYERS];
            // per-source params: flame/box use xyzw, radial uses x = falloff
            float4 _LParams[MAX_LAYERS];
            // x = source (0 noise, 1 shape, 2 constant), y = blend op
            float4 _LMode[MAX_LAYERS];

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Render-target UV origin differs across graphics APIs; the host sets
                // _FlipY so a layer can always treat uv.y = 0 as the bottom.
                o.uv = float2(v.uv.x, lerp(v.uv.y, 1.0 - v.uv.y, _FlipY));
                return o;
            }

            float hash21(float2 p, float seed)
            {
                p = frac(p * float2(123.34, 456.21) + seed * 0.1731);
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p, float seed)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i, seed);
                float b = hash21(i + float2(1, 0), seed);
                float c = hash21(i + float2(0, 1), seed);
                float d = hash21(i + float2(1, 1), seed);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p, float seed, int octaves)
            {
                float sum = 0, amp = 0.5, norm = 0;
                [unroll(4)]
                for (int o = 0; o < 4; o++)
                {
                    if (o >= octaves) break;
                    sum += vnoise(p, seed + o * 19.0) * amp;
                    norm += amp;
                    p *= 2.0;
                    amp *= 0.5;
                }
                return sum / max(norm, 1e-4);
            }

            float shapeValue(int id, float2 uv, float4 p)
            {
                if (id == 1)        // radial: soft disc
                {
                    float d = length(uv - 0.5) * 2.0;
                    return pow(saturate(1.0 - d), max(p.x, 1e-4));
                }
                else if (id == 2)   // linear: vertical gradient, p.x is the falloff exponent
                {
                    return pow(saturate(uv.y), max(p.x, 1e-4));
                }
                else if (id == 3)   // box: rect with soft edges, p.xy = half-size, p.z = softness
                {
                    float2 d = abs(uv - 0.5) - p.xy;
                    float m = saturate(1.0 - max(d.x, d.y) / max(p.z, 1e-4));
                    return m * m * (3.0 - 2.0 * m);
                }
                // 0 = flame: wide at the base, pinching to nothing at the tip.
                // p = (halfWidth, taper, baseSoftness, topFalloff)
                float up = saturate(1.0 - uv.y);
                float halfW = max(p.x * pow(up, p.y), 1e-4);
                float side = saturate(1.0 - abs(uv.x - 0.5) / halfW);
                side = side * side * (3.0 - 2.0 * side);
                float base = smoothstep(0.0, max(p.z, 1e-4), uv.y);
                return side * base * pow(up, p.w);
            }

            float blendValue(int op, float acc, float v)
            {
                if (op == 1) return acc * v;
                if (op == 2) return acc + v;
                if (op == 3) return acc - v;
                if (op == 4) return min(acc, v);
                if (op == 5) return max(acc, v);
                if (op == 6) return 1.0 - (1.0 - acc) * (1.0 - v); // screen
                return v;                                          // set
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Secs * _Speed;
                float acc = 0.0;
                int count = (int)_LayerCount;

                [loop]
                for (int L = 0; L < MAX_LAYERS; L++)
                {
                    if (L >= count) break;

                    float v;
                    int src = (int)_LMode[L].x;
                    if (src == 0)
                    {
                        // Noise is the only source that scales and scrolls: shapes are
                        // positional, so distorting their UV would deform the silhouette.
                        float2 uv = i.uv * _LScale[L].xy + _LScroll[L].xy * t;
                        v = fbm(uv, _LScale[L].w, (int)_LScale[L].z);
                    }
                    else if (src == 1)
                    {
                        v = shapeValue((int)_LScroll[L].w, i.uv, _LParams[L]);
                    }
                    else
                    {
                        v = _LParams[L].x;
                    }

                    v *= _LScroll[L].z; // amount
                    // The first layer has nothing to blend against, so it always sets.
                    acc = (L == 0) ? v : blendValue((int)_LMode[L].y, acc, v);
                }

                float e = saturate((acc - _Threshold) / max(_Softness, 1e-4));
                return tex2D(_Ramp, float2(e, 0.5));
            }
            ENDCG
        }
    }
    Fallback Off
}
