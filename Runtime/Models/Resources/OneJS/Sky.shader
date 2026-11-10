// The sky ModelBridge.SetSky draws behind a cart's scene (onejs-play's `scene.sky`), as the
// skybox of the bridge's own camera (a Skybox component), so the open scene's skybox and
// lighting are never touched.
//
// A gradient from the horizon up to the top colour and down to the bottom colour, a sun disc and
// glow where the scene's sun shines from, in its colour, a field of stars, and the scene's fog
// (OneJSFog.hlsl), so the ground and the sky meet: with height fog the haze along each ray to
// infinity, thinning as the ray climbs, and with any other fog a band at the horizon. The bridge
// sets the sun before the camera renders each frame, from the light, so moving the sun moves the
// disc; ModelFog sets the fog's globals each frame.
//
// No keywords, so a build cannot strip a variant the sky needs. Written against UnityCG, which
// both URP and the built-in pipeline draw a skybox with, as Unity's own skybox shaders do.
Shader "OneJS/Sky" {
    Properties {
        _Top ("Top", Color) = (0.18, 0.43, 0.85, 1)
        _Horizon ("Horizon", Color) = (0.75, 0.89, 1, 1)
        _Bottom ("Bottom", Color) = (0.75, 0.89, 1, 1)
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
        // xyz: the direction to the sun. w: 1 while there is a sun, 0 for none.
        _SunDir ("Sun Direction", Vector) = (0, 1, 0, 0)
        // x: the disc's radius, y: the glow's, both in radians. 0 draws none.
        _SunShape ("Sun Shape", Vector) = (0.0175, 0.21, 0, 0)
        _Stars ("Stars", Range(0, 1)) = 0
    }
    SubShader {
        // The models assembly needs glTFast, so without it this compiles to nothing, as ModelLit does.
        PackageRequirements {
            "com.unity.cloud.gltfast"
        }
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "OneJSFog.hlsl"
            #include "OneJSSky.hlsl"

            half4 _Top, _Horizon, _Bottom, _SunColor;
            float4 _SunDir, _SunShape;
            half _Stars;

            struct appdata {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            float3 Hash33(float3 p) {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // About one star a hundred cells at full strength, brightest overhead and gone at the
            // horizon, where the haze is.
            half Stars(float3 d) {
                float3 p = d * 220.0;
                float3 cell = floor(p);
                float3 h = Hash33(cell);
                if (h.z > _Stars * 0.06) return 0;
                float3 centre = cell + 0.25 + h * 0.5;
                float glint = 1.0 - smoothstep(0.0, 0.35, length(p - centre));
                return glint * (0.5 + 0.5 * h.x) * saturate(d.y * 4.0);
            }

            half4 frag(v2f i) : SV_Target {
                float3 d = normalize(i.dir);
                half3 col = OneJSSkyGradient(d, _Top.rgb, _Horizon.rgb, _Bottom.rgb);
                if (_Stars > 0) col += Stars(d);

                if (_SunDir.w > 0) {
                    float angle = acos(clamp(dot(d, normalize(_SunDir.xyz)), -1.0, 1.0));
                    float r = _SunShape.x;
                    half disc = r > 0 ? 1.0 - smoothstep(r * 0.85, r, angle) : 0;
                    half glow = OneJSSkyGlow(angle, _SunShape.y);
                    col = lerp(col, _SunColor.rgb, saturate(max(disc, glow)));
                }

                col = lerp(col, (half3)_OneJSFogColor.rgb, (half)OneJSFogSky(d, _WorldSpaceCameraPos));
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
