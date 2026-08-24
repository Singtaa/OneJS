// Moves pixels around: transform, tile, flip, crop.
//
// Its own shader, and its own pass, because these are the one family that
// cannot fuse. Every op in OneJS/FxOps works on a colour that has already been
// sampled; a spatial op has to change the uv *before* the sample happens, so it
// can never be folded into the middle of a fused run.
//
// Wire contract: onejs-unity/src/fx/ops.ts and Runtime/Fx/FxBridge.cs.
Shader "OneJS/FxSpatial"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Op ("Spatial op", Float) = 0
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

            #define SP_TRANSFORM 0
            #define SP_TILE 1
            #define SP_FLIP 2
            #define SP_CROP 3

            sampler2D _MainTex;
            float _Op;
            // transform: xy = offset (uv), z = rotation (radians), w = uniform scale
            float4 _Xform;
            // transform: xy = pivot, z = 1 to wrap instead of clamping to edge
            float4 _Xform2;
            // tile: xy = repeats, zw = offset
            float4 _Tile;
            // flip: x = horizontal, y = vertical
            float4 _Flip;
            // crop: xy = origin in uv, zw = size in uv
            float4 _Crop;
            float4 _BgColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int op = (int)_Op;
                float2 uv = i.uv;

                if (op == SP_FLIP)
                {
                    uv.x = _Flip.x > 0.5 ? 1.0 - uv.x : uv.x;
                    uv.y = _Flip.y > 0.5 ? 1.0 - uv.y : uv.y;
                    return tex2D(_MainTex, uv);
                }

                if (op == SP_CROP)
                {
                    // The destination is already the cropped size, so this just
                    // maps the whole output back onto the requested window.
                    return tex2D(_MainTex, _Crop.xy + uv * _Crop.zw);
                }

                if (op == SP_TILE)
                {
                    return tex2D(_MainTex, frac(uv * _Tile.xy + _Tile.zw));
                }

                // SP_TRANSFORM. Rotating and scaling about a pivot, inverted:
                // the fragment asks where it came FROM, so the transform runs
                // backwards from the one the caller described.
                float2 p = uv - _Xform2.xy;
                float s, c;
                sincos(-_Xform.z, s, c);
                p = float2(p.x * c - p.y * s, p.x * s + p.y * c);
                p /= max(_Xform.w, 1e-4);
                p += _Xform2.xy;
                p -= _Xform.xy;

                if (_Xform2.z > 0.5) return tex2D(_MainTex, frac(p));
                // Outside the source is background rather than a smeared edge,
                // which is what clamping would give and almost never wanted.
                bool outside = p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0;
                return outside ? _BgColor : tex2D(_MainTex, p);
            }
            ENDCG
        }
    }
    Fallback Off
}
