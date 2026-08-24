// Evaluates a fused run of per pixel image operations in one blit.
//
// Same constraint as OneJS/TextureFX: runtime shader compilation does not exist
// in player builds, so a JS chain cannot become generated shader code. The chain
// arrives as uniform arrays and a bounded loop walks them. That is the whole
// reason there is a fusion window rather than unlimited fusion, and why this
// runs identically on WebGL, where compute shaders do not exist at all.
//
// One spare sampler means one texture operand per pass. FxBridge flushes and
// starts a new pass when a chain wants a second one.
//
// Opcode contract: onejs-unity/src/fx/ops.ts and Runtime/Fx/FxBridge.cs.
Shader "OneJS/FxOps"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _TexB ("Operand", 2D) = "white" {}
        _OpCount ("Op count", Float) = 0
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

            #define MAX_OPS 16

            sampler2D _MainTex;
            sampler2D _TexB;
            float _OpCount;
            float _FlipY;

            // x = opcode, y = mode (0 scalar, 1 vector, 2 texture)
            float4 _Ops[MAX_OPS];
            // the right hand operand, or the parameters of a unary op
            float4 _Args[MAX_OPS];

            // Must match OP in ops.ts.
            #define OP_ADD 16
            #define OP_SUBTRACT 17
            #define OP_MULTIPLY 18
            #define OP_DIVIDE 19
            #define OP_POW 20
            #define OP_SQRT 21
            #define OP_CLAMP 32
            #define OP_FRACTION 33
            #define OP_MAXIMUM 34
            #define OP_MINIMUM 35
            #define OP_ONE_MINUS 36
            #define OP_REMAP 37
            #define OP_SATURATE 38
            #define OP_ABSOLUTE 48
            #define OP_EXPONENTIAL 49
            #define OP_LOG 50
            #define OP_MODULO 51
            #define OP_NEGATE 52
            #define OP_POSTERIZE 53
            #define OP_RECIPROCAL 54
            #define OP_LERP 64
            #define OP_SMOOTHSTEP 65
            #define OP_INVERSE_LERP 66

            #define MODE_SCALAR 0
            #define MODE_VECTOR 1
            #define MODE_TEXTURE 2

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float2(v.uv.x, lerp(v.uv.y, 1.0 - v.uv.y, _FlipY));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 a = tex2D(_MainTex, i.uv);
                int count = (int)_OpCount;

                [loop]
                for (int k = 0; k < MAX_OPS; k++)
                {
                    if (k >= count) break;

                    int op = (int)_Ops[k].x;
                    int mode = (int)_Ops[k].y;
                    float4 arg = _Args[k];

                    // The right hand side, for the ops that have one. A unary op
                    // ignores b and reads arg directly.
                    float4 b = arg;
                    if (mode == MODE_SCALAR) b = arg.xxxx;
                    else if (mode == MODE_TEXTURE) b = tex2D(_TexB, i.uv);

                    if (op == OP_ADD) a = a + b;
                    else if (op == OP_SUBTRACT) a = a - b;
                    else if (op == OP_MULTIPLY) a = a * b;
                    else if (op == OP_DIVIDE) a = a / b;
                    // pow of a negative base is undefined, and an image that dips
                    // below zero is normal, so raise the magnitude instead of
                    // returning NaN across half the picture.
                    else if (op == OP_POW) a = pow(abs(a), b);
                    else if (op == OP_SQRT) a = sqrt(abs(a));
                    else if (op == OP_CLAMP) a = clamp(a, arg.x, arg.y);
                    else if (op == OP_FRACTION) a = frac(a);
                    else if (op == OP_MAXIMUM) a = max(a, b);
                    else if (op == OP_MINIMUM) a = min(a, b);
                    else if (op == OP_ONE_MINUS) a = 1.0 - a;
                    else if (op == OP_REMAP)
                    {
                        float denom = arg.y - arg.x;
                        // A zero width source range would divide by zero; collapse
                        // to the low end of the target instead.
                        a = abs(denom) < 1e-6 ? arg.zzzz
                                              : arg.z + (a - arg.x) * (arg.w - arg.z) / denom;
                    }
                    else if (op == OP_SATURATE) a = saturate(a);
                    else if (op == OP_ABSOLUTE) a = abs(a);
                    else if (op == OP_EXPONENTIAL) a = exp(a);
                    else if (op == OP_LOG) a = log(max(a, 1e-8));
                    else if (op == OP_MODULO) a = fmod(a, b);
                    else if (op == OP_NEGATE) a = -a;
                    else if (op == OP_POSTERIZE) a = floor(a / max(b, 1e-6)) * max(b, 1e-6);
                    else if (op == OP_RECIPROCAL) a = 1.0 / max(abs(a), 1e-6) * sign(a);
                    // t rides in arg.w for a scalar or texture operand, which
                    // leaves that slot free; a vector operand spends all four, so
                    // it takes its t from the same place and callers pass it there.
                    else if (op == OP_LERP) a = lerp(a, b, arg.w);
                    else if (op == OP_SMOOTHSTEP) a = smoothstep(arg.x, arg.y, a);
                    else if (op == OP_INVERSE_LERP)
                    {
                        float4 denom = b - a;
                        a = abs(denom.x) < 1e-6 ? 0.0.xxxx : (a - b) / denom;
                    }
                }

                return a;
            }
            ENDCG
        }
    }
    Fallback Off
}
