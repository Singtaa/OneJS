Shader "Hidden/OneJS/BackdropBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Sigma ("Blur Sigma", Float) = 5.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off ZTest Always Cull Off

        // Pass 0: Horizontal blur
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragH

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Sigma;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 FragH(Varyings input) : SV_Target
            {
                float sigma = max(_Sigma, 0.001);
                int radius = min((int)ceil(sigma * 3.0), 64);
                float invSigmaSq2 = -0.5 / (sigma * sigma);

                half4 color = 0;
                float weightSum = 0;

                for (int x = -radius; x <= radius; x++)
                {
                    float w = exp((float)(x * x) * invSigmaSq2);
                    color += tex2D(_MainTex, input.uv + float2(x * _MainTex_TexelSize.x, 0)) * w;
                    weightSum += w;
                }

                half4 result = color / max(weightSum, 0.0001);
                result.a = 1.0;
                return result;
            }
            ENDHLSL
        }

        // Pass 1: Vertical blur
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragV

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Sigma;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 FragV(Varyings input) : SV_Target
            {
                float sigma = max(_Sigma, 0.001);
                int radius = min((int)ceil(sigma * 3.0), 64);
                float invSigmaSq2 = -0.5 / (sigma * sigma);

                half4 color = 0;
                float weightSum = 0;

                for (int y = -radius; y <= radius; y++)
                {
                    float w = exp((float)(y * y) * invSigmaSq2);
                    color += tex2D(_MainTex, input.uv + float2(0, y * _MainTex_TexelSize.y)) * w;
                    weightSum += w;
                }

                half4 result = color / max(weightSum, 0.0001);
                result.a = 1.0;
                return result;
            }
            ENDHLSL
        }
    }
}
