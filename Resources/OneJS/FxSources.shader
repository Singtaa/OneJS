// Generates a texture from nothing, for the fx chain sources that have no input.
//
// Separate from OneJS/FxOps because the two do different jobs: FxOps reads
// _MainTex and transforms it, this one only reads uv. Folding both into one
// shader would mean every fused pass carried the generator code it never runs.
//
// Wire contract: onejs-unity/src/fx/ops.ts and Runtime/Fx/FxBridge.cs.
Shader "OneJS/FxSources"
{
    Properties
    {
        _SourceType ("Source type", Float) = 0
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
            #include "SDF2D.cginc"
            #include "Noise2D.cginc"

            #define MAX_STOPS 8

            #define SRC_NOISE 0
            #define SRC_GRADIENT 1
            #define SRC_SDF 2

            float _SourceType;
            float _Aspect;

            // noise: xy = scale, z = octaves, w = seed
            float4 _NoiseScale;
            // noise: xy = offset, z = rotation (radians)
            float4 _NoiseOffset;
            // noise: x = lacunarity, y = gain
            float4 _NoiseFbm;

            // gradient: angle in radians, and how many stops are live
            float _GradAngle;
            float _GradStopCount;
            float4 _GradColors[MAX_STOPS];
            // only .x is used; a float array would pack to the same size anyway
            float4 _GradPositions[MAX_STOPS];

            // sdf: xyzw = params 1..4
            float4 _SdfParams;
            // sdf: xy = params 5..6, z = edge softness, w = 1 for raw field
            float4 _SdfParams2;
            // sdf: xy = position, z = rotation (radians), w = uniform scale
            float4 _SdfTransform;
            // sdf: x = shape id, y = rounded, z = onion
            float4 _SdfShape;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Duplicated from TextureFX rather than shared: the two disagree on
            // purpose. TextureFX packs its shape into a layer slot, this one has
            // the whole uniform block to itself, and tying them together would
            // make every change to one a change to the other.
            float sdfDistance(int id, float2 p, float4 a, float2 b)
            {
                switch (id)
                {
                case 0:  return sdCircle(p, a.x);
                case 1:  return sdRoundedBox(p, a.xy, float4(a.z, a.w, b.x, b.y));
                case 2:  return sdBox(p, a.xy);
                case 3:  return sdOrientedBox(p, a.xy, a.zw, b.x);
                case 4:  return sdSegment(p, a.xy, a.zw);
                case 5:  return sdRhombus(p, a.xy);
                case 6:  return sdTrapezoid(p, a.x, a.y, a.z);
                case 7:  return sdParallelogram(p, a.x, a.y, a.z);
                case 8:  return sdEquilateralTriangle(p, a.x);
                case 9:  return sdTriangleIsosceles(p, a.xy);
                case 10: return sdTriangle(p, a.xy, a.zw, b.xy);
                case 11: return sdUnevenCapsule(p, a.x, a.y, a.z);
                case 12: return sdPentagon(p, a.x);
                case 13: return sdHexagon(p, a.x);
                case 14: return sdOctogon(p, a.x);
                case 15: return sdHexagram(p, a.x);
                case 16: return sdStar5(p, a.x, a.y);
                case 17: return sdStar(p, a.x, (int)a.y, a.z);
                case 18: return sdPie(p, a.xy, a.z);
                case 19: return sdCutDisk(p, a.x, a.y);
                case 20: return sdArc(p, a.xy, a.z, a.w);
                case 21: return sdRing(p, a.xy, a.z, a.w);
                case 22: return sdHorseshoe(p, a.xy, a.z, float2(a.w, b.x));
                case 23: return sdVesica(p, a.x, a.y);
                case 24: return sdOrientedVesica(p, a.xy, a.zw, b.x);
                case 25: return sdMoon(p, a.x, a.y, a.z);
                case 26: return sdRoundedCross(p, a.x);
                case 27: return sdEgg(p, a.x, a.y, a.z, a.w);
                case 28: return sdHeart(p);
                case 29: return sdCross(p, a.xy, a.z);
                case 30: return sdRoundedX(p, a.x, a.y);
                case 31: return sdEllipse(p, a.xy);
                case 32: return sdParabola(p, a.x);
                case 33: return sdParabolaSegment(p, a.x, a.y);
                case 34: return sdBezier(p, a.xy, a.zw, b.xy);
                case 35: return sdBlobbyCross(p, a.x);
                case 36: return sdTunnel(p, a.xy);
                case 37: return sdStairs(p, a.xy, a.z);
                case 38: return sdQuadraticCircle(p);
                case 39: return sdHyberbola(p, a.x, a.y);
                case 40: return sdCoolS(p);
                case 41: return sdCircleWave(p, a.x, a.y);
                default: return 1e6;
                }
            }

            float4 gradientAt(float2 uv)
            {
                // Project uv onto the gradient direction and remap so the whole
                // element is covered whatever the angle: a diagonal gradient
                // otherwise runs out before the far corner.
                float s, c;
                sincos(_GradAngle, s, c);
                float2 d = float2(c, s);
                float2 p = uv - 0.5;
                float halfSpan = 0.5 * (abs(c) + abs(s));
                float t = saturate((dot(p, d) + halfSpan) / max(2.0 * halfSpan, 1e-6));

                int count = (int)_GradStopCount;
                if (count <= 0) return float4(0, 0, 0, 0);
                if (count == 1) return _GradColors[0];

                float4 col = _GradColors[0];
                [loop]
                for (int i = 1; i < MAX_STOPS; i++)
                {
                    if (i >= count) break;
                    float p0 = _GradPositions[i - 1].x;
                    float p1 = _GradPositions[i].x;
                    // Stops are sorted on the JS side, so a zero width span means
                    // two stops at the same place: a hard edge, not an error.
                    float k = saturate((t - p0) / max(p1 - p0, 1e-6));
                    col = t >= p0 ? lerp(_GradColors[i - 1], _GradColors[i], k) : col;
                }
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int type = (int)_SourceType;

                if (type == SRC_NOISE)
                {
                    float s, c;
                    sincos(_NoiseOffset.z, s, c);
                    float2 uv = i.uv;
                    float2 p = float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
                    p = p * _NoiseScale.xy + _NoiseOffset.xy;
                    float n = onejsFbmKind((int)_NoiseFbm.z, p, _NoiseScale.w, (int)_NoiseScale.z,
                                           _NoiseFbm.x, _NoiseFbm.y);
                    return float4(n, n, n, 1);
                }

                if (type == SRC_GRADIENT)
                {
                    return gradientAt(i.uv);
                }

                // SRC_SDF. Same centred, aspect corrected space TextureFX uses, so
                // a radius means the same thing in both.
                float2 sp = float2((i.uv.x - 0.5) * _Aspect, i.uv.y - 0.5);
                sp -= _SdfTransform.xy;
                float ss, sc;
                sincos(_SdfTransform.z, ss, sc);
                sp = float2(sp.x * sc - sp.y * ss, sp.x * ss + sp.y * sc);
                // Non-uniform: an egg stretched on Y is a flame silhouette, and
                // forcing one factor loses that. Y rides in _SdfShape.w, spare.
                float2 us = float2(max(_SdfTransform.w, 1e-4), max(_SdfShape.w, 1e-4));
                sp /= us;

                float d = sdfDistance((int)_SdfShape.x, sp, _SdfParams, _SdfParams2.xy);
                float onion = _SdfShape.z;
                d = lerp(d, abs(d) - onion, step(1e-6, onion));
                d -= _SdfShape.y;
                // Back out of the scale so the field stays roughly metric. With
                // an anisotropic scale there is no single right factor; the
                // smaller axis keeps rounded and onion widths from overshooting.
                d *= min(us.x, us.y);

                float mask = saturate(0.5 - d / max(_SdfParams2.z, 1e-4));
                float v = lerp(mask, -d, _SdfParams2.w);
                return float4(v, v, v, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
