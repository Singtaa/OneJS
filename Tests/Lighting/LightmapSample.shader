// A lightmap read at a uv the way a model's shader reads it, into a 1 by 1 target, so a test can ask
// what a uv shows without assuming which way up the platform keeps a render texture's rows.
Shader "Hidden/OneJS/Tests/LightmapSample" {
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _Lightmap;
            float2 _At;
            float4 vert(uint id : SV_VertexID) : SV_POSITION {
                float2 p = float2((id << 1) & 2, id & 2);
                return float4(p * 2 - 1, 0, 1);
            }
            float4 frag() : SV_Target { return tex2Dlod(_Lightmap, float4(_At, 0, 0)); }
            ENDCG
        }
    }
}
