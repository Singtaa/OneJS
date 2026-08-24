// Neighbourhood filters: blur, sharpen, edge, dilate, erode, outline.
//
// Its own shader for the same reason as OneJS/FxSpatial, one step further on.
// A fused op reads one pixel. A spatial op reads one pixel at a moved uv. These
// read *many* pixels, so they need the texel size and cannot fold into either.
//
// The separable ones (blur, dilate, erode) are two passes, horizontal then
// vertical, driven by _Dir. That is O(2r) taps instead of O(r squared), and it
// is exact for a Gaussian and for a square structuring element.
//
// Wire contract: onejs-unity/src/fx/ops.ts and Runtime/Fx/FxBridge.cs.
Shader "OneJS/FxFilter"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Filter ("Filter", Float) = 0
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

            // Taps per side. 65 taps total at the widest, which is where the
            // instruction count stops being free. FxBridge splits a wider blur
            // into repeated passes rather than letting this grow.
            #define MAX_TAPS 32

            #define F_BLUR 0
            #define F_SHARPEN 1
            #define F_EDGE 2
            #define F_DILATE 3
            #define F_ERODE 4
            #define F_OUTLINE 5

            sampler2D _MainTex;
            sampler2D _AltTex;      // outline: the un-dilated original
            float4 _TexelSize;      // xy = 1/width, 1/height
            float _Filter;
            float2 _Dir;            // (1,0) horizontal, (0,1) vertical
            float _Radius;          // in pixels
            float _Sigma;
            float _Amount;
            float4 _OutlineColor;
            float _OutlineOn;       // 0 = alpha is coverage, 1 = luminance is

            // Not UnityCG's Luminance(): that reads unity_ColorSpaceLuminance, whose
            // linear space weights sum to about 0.502 rather than 1, so white comes
            // back as half. Rec. 709, matching onejsLuma in FxColor.cginc.
            float fxLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 gaussian(float2 uv)
            {
                float2 texelStep = _Dir * _TexelSize.xy;
                float sigma = max(_Sigma, 1e-4);
                float4 acc = tex2D(_MainTex, uv);
                float sum = 1.0;
                [loop]
                for (int i = 1; i <= MAX_TAPS; i++)
                {
                    if (i > _Radius) break;
                    float w = exp(-0.5 * (i * i) / (sigma * sigma));
                    acc += tex2D(_MainTex, uv + texelStep * i) * w;
                    acc += tex2D(_MainTex, uv - texelStep * i) * w;
                    sum += 2.0 * w;
                }
                return acc / sum;
            }

            float4 morph(float2 uv, bool dilate)
            {
                float2 texelStep = _Dir * _TexelSize.xy;
                float4 best = tex2D(_MainTex, uv);
                [loop]
                for (int i = 1; i <= MAX_TAPS; i++)
                {
                    if (i > _Radius) break;
                    float4 a = tex2D(_MainTex, uv + texelStep * i);
                    float4 b = tex2D(_MainTex, uv - texelStep * i);
                    best = dilate ? max(best, max(a, b)) : min(best, min(a, b));
                }
                return best;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int f = (int)_Filter;
                float2 t = _TexelSize.xy;

                if (f == F_BLUR) return gaussian(i.uv);
                if (f == F_DILATE) return morph(i.uv, true);
                if (f == F_ERODE) return morph(i.uv, false);

                if (f == F_SHARPEN)
                {
                    // Unsharp with a 3x3 Laplacian, which needs no separate blur
                    // pass. Alpha is left alone: sharpening an edge into the
                    // alpha channel produces halos around cut outs.
                    float4 c = tex2D(_MainTex, i.uv);
                    float3 sum = tex2D(_MainTex, i.uv + float2(-t.x, 0)).rgb
                               + tex2D(_MainTex, i.uv + float2(t.x, 0)).rgb
                               + tex2D(_MainTex, i.uv + float2(0, -t.y)).rgb
                               + tex2D(_MainTex, i.uv + float2(0, t.y)).rgb;
                    float3 lap = c.rgb * 4.0 - sum;
                    return float4(c.rgb + lap * _Amount, c.a);
                }

                if (f == F_EDGE)
                {
                    // Sobel on luminance. Running it per channel would give three
                    // uncorrelated edge maps, which is almost never what is meant.
                    float tl = fxLuma(tex2D(_MainTex, i.uv + float2(-t.x, -t.y)).rgb);
                    float tc = fxLuma(tex2D(_MainTex, i.uv + float2(0, -t.y)).rgb);
                    float tr = fxLuma(tex2D(_MainTex, i.uv + float2(t.x, -t.y)).rgb);
                    float ml = fxLuma(tex2D(_MainTex, i.uv + float2(-t.x, 0)).rgb);
                    float mr = fxLuma(tex2D(_MainTex, i.uv + float2(t.x, 0)).rgb);
                    float bl = fxLuma(tex2D(_MainTex, i.uv + float2(-t.x, t.y)).rgb);
                    float bc = fxLuma(tex2D(_MainTex, i.uv + float2(0, t.y)).rgb);
                    float br = fxLuma(tex2D(_MainTex, i.uv + float2(t.x, t.y)).rgb);
                    float gx = (tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl);
                    float gy = (bl + 2.0 * bc + br) - (tl + 2.0 * tc + tr);
                    float g = saturate(sqrt(gx * gx + gy * gy) * _Amount);
                    return float4(g, g, g, tex2D(_MainTex, i.uv).a);
                }

                // F_OUTLINE. _MainTex is the dilated copy, _AltTex the original.
                // The ring is what the dilation added, so the original sits on top
                // of it unchanged.
                //
                // Which channel counts as coverage has to be told, not guessed: a
                // loaded sprite carries its shape in alpha, while the sdf and noise
                // sources carry it in rgb with alpha left at 1. Guessing alpha
                // silently produces an empty ring for half the sources.
                float4 orig = tex2D(_AltTex, i.uv);
                float4 grownC = tex2D(_MainTex, i.uv);
                bool useLuma = _OutlineOn > 0.5;
                float origK = useLuma ? fxLuma(orig.rgb) : orig.a;
                float grown = useLuma ? fxLuma(grownC.rgb) : grownC.a;
                float ring = saturate(grown - origK);
                // Not named `line`: that is a reserved word in HLSL (the geometry
                // shader primitive type) and the error it gives says only
                // "unexpected token".
                float4 ringCol = float4(_OutlineColor.rgb, _OutlineColor.a * ring);
                // Standard over, using whichever channel coverage lives in, so a
                // translucent original still shows the ring through it.
                float outA = origK + ringCol.a * (1.0 - origK);
                float3 outRGB = outA < 1e-6 ? 0.0.xxx
                    : (orig.rgb * origK + ringCol.rgb * ringCol.a * (1.0 - origK)) / outA;
                // In luminance mode the input carried its shape in rgb with alpha at
                // 1, so the result does too and alpha is left as it was found.
                return useLuma ? float4(outRGB, orig.a) : float4(outRGB, outA);
            }
            ENDCG
        }
    }
    Fallback Off
}
