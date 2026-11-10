// Copies a normal map's bytes into a linear texture unchanged (ModelBridge.TakeNormalCopy). A picture
// from `tex` or `loadTexture` is sRGB, so sampling it decodes its bytes as if they were colour; with
// _Encode 1 this encodes them back before they are written to the linear copy, which then reads them
// as the data they are. A blit between an sRGB and a linear target would decode them on WebGL 2.
Shader "Hidden/OneJS/NormalCopy" {
    Properties {
        _MainTex ("Picture", 2D) = "white" {}
        _Encode ("Encode sRGB", Float) = 1
    }
    SubShader {
        ZTest Always Cull Off ZWrite Off
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Encode;

            float4 Frag(v2f_img i) : SV_Target {
                float4 c = tex2D(_MainTex, i.uv);
                float3 encoded = float3(LinearToGammaSpaceExact(c.r), LinearToGammaSpaceExact(c.g), LinearToGammaSpaceExact(c.b));
                return float4(lerp(c.rgb, encoded, _Encode), c.a);
            }
            ENDCG
        }
    }
}
