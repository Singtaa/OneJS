Shader "OneJS/CheckerboardUnlit"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.85, 0.85, 0.85, 1)
        _ColorB ("Color B", Color) = (0.25, 0.25, 0.25, 1)
        _Tiles ("Tiles", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _ColorA;
            float4 _ColorB;
            float _Tiles;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int cx = (int)(i.uv.x * _Tiles);
                int cy = (int)(i.uv.y * _Tiles);
                float checker = ((cx + cy) % 2 == 0) ? 0.0 : 1.0;
                return lerp(_ColorA, _ColorB, checker);
            }
            ENDCG
        }
    }
}
