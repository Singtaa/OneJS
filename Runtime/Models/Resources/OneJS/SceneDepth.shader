// scene.depth's pass (OneJS.Runtime.Models.URP's UrpSceneDepth): the camera's depth texture in,
// each pixel's distance in metres along its ray out, and the far distance where nothing was drawn.
Shader "Hidden/OneJS/SceneDepth" {
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_OjDepth);
            // x, y: a view ray's x and y at z = 1 per unit of normalized screen; z: 1 if orthographic.
            float4 _OjDepthRay;
            // x: near, y: far, zw: one over the target's size.
            float4 _OjDepthClip;

            float4 Vert(uint id : SV_VertexID) : SV_Position {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2 - 1, 0.5, 1);
            }

            float Frag(float4 position : SV_Position) : SV_Target {
                // The depth and this target are both render textures of one size, so a pixel
                // reads the depth texel it sits on, whichever way up the platform stores them.
                float2 uv = position.xy * _OjDepthClip.zw;
                float raw = SAMPLE_DEPTH_TEXTURE_LOD(_OjDepth, float4(uv, 0, 0));
                float near = _OjDepthClip.x, far = _OjDepthClip.y;
            #if UNITY_REVERSED_Z
                bool sky = raw <= 0;
                float linear01 = 1 - raw;
            #else
                bool sky = raw >= 1;
                float linear01 = raw;
            #endif
                if (sky) return far;
                // Parallel rays: the distance from the camera's plane.
                if (_OjDepthRay.z > 0.5) return near + (far - near) * linear01;
                float2 ndc = uv * 2 - 1;
                float2 aside = ndc * _OjDepthRay.xy;
                return LinearEyeDepth(raw) * sqrt(1 + dot(aside, aside));
            }
            ENDCG
        }
    }
}
