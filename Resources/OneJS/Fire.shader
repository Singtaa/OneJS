// Procedural fire for the OneJS ShaderEffect element.
//
// The classic scrolling-noise fire: two noise fields scroll upward at different
// scales and speeds, multiply into a turbulent field, get shaped by a mask that
// defines the flame's silhouette, are eroded to a crisp edge, and finally index
// a colour ramp. No sprite sheet, no particles, and it never repeats visibly
// because the two scroll rates are irrational relative to each other.
//
// This is NOT a UI Toolkit shader. It is blitted into a RenderTexture by
// ShaderEffectElement, which then shows the result as the element's
// backgroundImage - so the element keeps normal UITK clipping, border-radius
// and antialiasing, and this shader keeps full control of its fragment.
//
// The ramp carries alpha, so transparency comes from the ramp's low end rather
// than a separate curve: colour and cutoff are authored in one place.
Shader "OneJS/Fire"
{
    Properties
    {
        _NoiseA ("Noise A", 2D) = "white" {}
        _NoiseB ("Noise B", 2D) = "white" {}
        _Ramp   ("Colour Ramp", 2D) = "white" {}

        // Silhouette, computed analytically rather than sampled from a mask
        // texture: a texture would have to agree with the render target's UV
        // orientation, and getting that wrong silently blanks the effect.
        _Width ("Base half-width", Float) = 0.42
        _Taper ("Taper", Float) = 0.75
        _BaseSoft ("Base softness", Float) = 0.10
        _TopFalloff ("Top falloff", Float) = 0.85

        _Secs ("Time (seconds)", Float) = 0
        _Speed ("Speed", Float) = 1
        _ScaleA ("Scale A", Vector) = (1, 1, 0, 0)
        _ScaleB ("Scale B", Vector) = (2, 2, 0, 0)
        _DriftA ("Drift A", Vector) = (0.03, -0.35, 0, 0)
        _DriftB ("Drift B", Vector) = (-0.05, -0.62, 0, 0)

        _Gain ("Gain", Float) = 2.4
        _Turbulence ("Turbulence", Float) = 1
        _Threshold ("Threshold", Float) = 0.22
        _Softness ("Softness", Float) = 0.38
        _Intensity ("Intensity", Float) = 1
        _Sway ("Sway", Float) = 0.06
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
            #include "UnityCG.cginc"

            sampler2D _NoiseA, _NoiseB, _Ramp;
            float _Secs, _Speed, _Gain, _Turbulence, _Threshold, _Softness, _Intensity, _Sway, _FlipY;
            float _Width, _Taper, _BaseSoft, _TopFalloff;
            float4 _ScaleA, _ScaleB, _DriftA, _DriftB;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Render-target UV origin differs across graphics APIs; the host
                // sets _FlipY so "up" is up everywhere.
                o.uv = float2(v.uv.x, lerp(v.uv.y, 1.0 - v.uv.y, _FlipY));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Secs * _Speed;

                // uv.y = 0 at the base, 1 at the tip. Scrolling the noise DOWN in
                // uv space makes the pattern travel up the flame.
                float2 uv = i.uv;

                // A slow lateral sway, stronger toward the tip, so the column is
                // never a straight vertical bar.
                uv.x += sin(t * 0.9 + uv.y * 3.1) * _Sway * uv.y;

                float2 uvA = uv * _ScaleA.xy + _DriftA.xy * t;
                float2 uvB = uv * _ScaleB.xy + _DriftB.xy * t;

                float a = tex2D(_NoiseA, uvA).r;
                float b = tex2D(_NoiseB, uvB).r;

                // The multiply: two independent fields agreeing is what produces
                // the wispy, non-repeating structure.
                float n = a * b * _Gain;
                n = lerp(0.5, n, _Turbulence);

                // Analytic silhouette. y = 0 is the flame's base.
                float y = saturate(i.uv.y);
                float up = saturate(1.0 - y);
                float halfW = max(_Width * pow(up, _Taper), 1e-4);
                float side = saturate(1.0 - abs(i.uv.x - 0.5) / halfW);
                side = side * side * (3.0 - 2.0 * side);            // smooth shoulders
                float base = smoothstep(0.0, max(_BaseSoft, 1e-4), y); // no hard cut at the bottom
                float m = side * base * pow(up, _TopFalloff);

                float v = n * m * _Intensity;

                // Erode to a defined edge. Without this the flame is a soft blob;
                // the threshold is what creates licks and detached wisps.
                float e = saturate((v - _Threshold) / max(1e-4, _Softness));

                fixed4 c = tex2D(_Ramp, float2(saturate(e), 0.5));
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
