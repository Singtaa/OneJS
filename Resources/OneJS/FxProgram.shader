// The shader language VM.
//
// Evaluates a program authored in TypeScript (`onejs-unity/sl`) and encoded by
// `sl/encode.ts` into a texture. This is the backend that runs where a shader
// cannot be compiled at runtime, which is every player build on every platform,
// and therefore every game on play.onejs.com.
//
// It is deliberately NOT the fast path. A project with an editor generates HLSL
// from the same program and compiles it, so an ejected game pays none of this.
// What this buys is that a Play author writes one thing, and it runs.
//
// WHY EIGHT REGISTERS, AND WHY AN INDEXED STORE. Both measured, not chosen.
// A dynamically indexed register file in a fragment shader is nearly free on
// Apple silicon and costs 8x to 10x on both of ANGLE's Windows backends, which
// is a spill to per thread local memory. Shrinking the file from 16 entries to
// 8 recovers 3.0x to 4.0x of that on Windows. At 8 the two Windows backends
// then disagree about the write strategy, D3D11 wanting the indexed store and
// GL an unrolled comparison chain, so this ships the indexed store everywhere:
// better worst case, and no branch that can only be tested on one machine.
// See Specs/SHADER_LANG.md section 5.4 and Tools/shader-vm-spike.
//
// THE PROGRAM IS A TEXTURE, not a uniform array. Uniform space is what caps
// FxOps at 16 fused ops and TextureFX at 6 layers. Two texels per instruction,
// fixed width, so instruction i is at texels 2i and 2i+1 with no cursor to
// advance and no dependent read to decode a length.
//
// Opcode numbers are the contract in onejs-unity/src/sl/ops.ts. Change both.
Shader "OneJS/FxProgram"
{
    Properties
    {
        _Program ("Program", 2D) = "black" {}
        _InstrCount ("Instruction count", Float) = 0
        _ProgramWidth ("Program texel count", Float) = 2
        _ResultReg ("Result register", Float) = 0
        _Secs ("Seconds", Float) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            // Shared with every generated shader, so the two backends cannot
            // drift on what noise means. See SLCommon.cginc.
            #include "SLCommon.cginc"

            #define REGS 8
            #define MAX_INSTR 256
            #define MAX_UNIFORMS 16
            #define MAX_TEXTURES 4

            // Must match SLOP in onejs-unity/src/sl/ops.ts.
            #define OP_CONST 0
            #define OP_INPUT 1
            #define OP_UNIFORM 2
            #define OP_COMPOSE 3
            #define OP_SWIZZLE 4

            #define OP_ADD 16
            #define OP_SUB 17
            #define OP_MUL 18
            #define OP_DIV 19
            #define OP_MOD 20
            #define OP_POW 21
            #define OP_NEG 22
            #define OP_RECIP 23

            #define OP_SIN 48
            #define OP_COS 49
            #define OP_TAN 50
            #define OP_ASIN 51
            #define OP_ACOS 52
            #define OP_ATAN2 53
            #define OP_EXP 54
            #define OP_LOG 55
            #define OP_SQRT 56
            #define OP_ABS 57
            #define OP_SIGN 58
            #define OP_FLOOR 59
            #define OP_CEIL 60
            #define OP_ROUND 61
            #define OP_FRACT 62
            #define OP_MIN 63
            #define OP_MAX 64
            #define OP_CLAMP 65
            #define OP_SATURATE 66

            #define OP_LENGTH 80
            #define OP_DISTANCE 81
            #define OP_DOT 82
            #define OP_CROSS 83
            #define OP_NORMALIZE 84
            #define OP_REFLECT 85

            #define OP_MIX 96
            #define OP_STEP 97
            #define OP_SMOOTHSTEP 98
            #define OP_SELECT 99
            #define OP_REMAP 100

            #define OP_HSV2RGB 113
            #define OP_RGB2HSV 114
            #define OP_LUMINANCE 115

            #define OP_NOISE 128
            #define OP_SIMPLEX 129
            #define OP_FBM 130

            #define OP_SAMPLE 144

            // Input ids, matching INPUT_ID in sl/encode.ts.
            #define IN_UV 0
            #define IN_FRAGCOORD 1
            #define IN_RESOLUTION 2
            #define IN_TIME 3
            #define IN_ASPECT 4

            sampler2D _Program;
            float _InstrCount;
            float _ProgramWidth;
            float _ResultReg;
            float _Secs;
            float _FlipY;

            float4 _Uniforms[MAX_UNIFORMS];

            sampler2D _Tex0;
            sampler2D _Tex1;
            sampler2D _Tex2;
            sampler2D _Tex3;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Origin corrected HERE, once, so an author never writes the
                // _FlipY dance and never ships a shader that is upside down in
                // a browser and right way up in the editor.
                o.uv = float2(v.uv.x, lerp(v.uv.y, 1.0 - v.uv.y, _FlipY));
                return o;
            }

            // Exact texel centre. Point filtering plus the half texel offset
            // makes this exact; a rounding error would silently decode a
            // neighbouring instruction rather than fail.
            float4 fetch(int i)
            {
                return tex2Dlod(_Program, float4((float(i) + 0.5) / _ProgramWidth, 0.5, 0, 0));
            }

            float4 sampleSlot(int slot, float2 uv)
            {
                // A switch rather than an array: sampler arrays need a
                // dynamically uniform index on the WebGL2 baseline, and a
                // per pixel program index is not that.
                if (slot == 0) return tex2D(_Tex0, uv);
                if (slot == 1) return tex2D(_Tex1, uv);
                if (slot == 2) return tex2D(_Tex2, uv);
                return tex2D(_Tex3, uv);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 r[REGS];
                [unroll]
                for (int z = 0; z < REGS; z++) r[z] = 0;

                int count = min((int)_InstrCount, MAX_INSTR);

                [loop]
                for (int k = 0; k < MAX_INSTR; k++)
                {
                    if (k >= count) break;

                    float4 head = fetch(k * 2);
                    float4 imm  = fetch(k * 2 + 1);

                    int op  = (int)head.x;
                    int dst = (int)head.y;
                    int ra  = (int)head.z;
                    int rb  = (int)head.w;

                    float4 a = r[ra];
                    float4 b = r[rb];
                    float4 res = 0;

                    // Ordered by family, cheapest and most common first. The
                    // opcode ranges make this readable: everything from 16 to 47
                    // is arithmetic, and so on.
                    if (op == OP_ADD)             res = a + b;
                    else if (op == OP_SUB)        res = a - b;
                    else if (op == OP_MUL)        res = a * b;
                    else if (op == OP_DIV)        res = a / b;
                    else if (op == OP_MOD)        res = fmod(a, b);
                    else if (op == OP_POW)        res = pow(abs(a), b);
                    else if (op == OP_NEG)        res = -a;
                    else if (op == OP_RECIP)      res = 1.0 / a;

                    else if (op == OP_CONST)      res = imm;
                    else if (op == OP_UNIFORM)    res = _Uniforms[min(ra, MAX_UNIFORMS - 1)];
                    else if (op == OP_INPUT)
                    {
                        if (ra == IN_UV)              res = float4(i.uv, 0, 0);
                        else if (ra == IN_FRAGCOORD)  res = float4(i.uv * _ScreenParams.xy, 0, 0);
                        else if (ra == IN_RESOLUTION) res = float4(_ScreenParams.xy, 0, 0);
                        else if (ra == IN_TIME)       res = _Secs;
                        else                          res = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    }
                    else if (op == OP_SWIZZLE)
                    {
                        // Channels arrive as imm, with -1 for unused. Reading a
                        // float4 by index is a small dynamic index of its own,
                        // but over four components rather than eight registers,
                        // and it is what makes p.x cost one instruction.
                        float4 src = a;
                        float comp[4] = { src.x, src.y, src.z, src.w };
                        res = 0;
                        if (imm.x >= 0) res.x = comp[(int)imm.x];
                        if (imm.y >= 0) res.y = comp[(int)imm.y];
                        if (imm.z >= 0) res.z = comp[(int)imm.z];
                        if (imm.w >= 0) res.w = comp[(int)imm.w];
                    }
                    else if (op == OP_COMPOSE)
                    {
                        // Builds a wider value from up to four narrower ones.
                        // Every part contributes its x, because a part is only
                        // ever a scalar or the result of a swizzle.
                        int n = (int)imm.z;
                        float4 c = r[(int)imm.x];
                        float4 d = r[(int)imm.y];
                        res = float4(a.x, n > 1 ? b.x : 0, n > 2 ? c.x : 0, n > 3 ? d.x : 0);
                    }

                    else if (op == OP_SIN)        res = sin(a);
                    else if (op == OP_COS)        res = cos(a);
                    else if (op == OP_TAN)        res = tan(a);
                    else if (op == OP_ASIN)       res = asin(clamp(a, -1, 1));
                    else if (op == OP_ACOS)       res = acos(clamp(a, -1, 1));
                    else if (op == OP_ATAN2)      res = atan2(a, b);
                    else if (op == OP_EXP)        res = exp(a);
                    else if (op == OP_LOG)        res = log(max(a, 1e-8));
                    else if (op == OP_SQRT)       res = sqrt(max(a, 0));
                    else if (op == OP_ABS)        res = abs(a);
                    else if (op == OP_SIGN)       res = sign(a);
                    else if (op == OP_FLOOR)      res = floor(a);
                    else if (op == OP_CEIL)       res = ceil(a);
                    else if (op == OP_ROUND)      res = round(a);
                    else if (op == OP_FRACT)      res = frac(a);
                    else if (op == OP_MIN)        res = min(a, b);
                    else if (op == OP_MAX)        res = max(a, b);
                    else if (op == OP_SATURATE)   res = saturate(a);
                    else if (op == OP_CLAMP)      res = clamp(a, b, r[(int)imm.x]);

                    // Narrowing ops. The destination is a float, so the source
                    // width has to travel in the immediate: a register is a
                    // float4 whatever the value inside it is, and length(vec2)
                    // is not the length of four components.
                    else if (op == OP_LENGTH)
                    {
                        int w = (int)imm.x;
                        res = w == 2 ? length(a.xy) : (w == 3 ? length(a.xyz) : length(a));
                    }
                    else if (op == OP_DISTANCE)
                    {
                        int w = (int)imm.x;
                        res = w == 2 ? distance(a.xy, b.xy) : (w == 3 ? distance(a.xyz, b.xyz) : distance(a, b));
                    }
                    else if (op == OP_DOT)
                    {
                        int w = (int)imm.x;
                        res = w == 2 ? dot(a.xy, b.xy) : (w == 3 ? dot(a.xyz, b.xyz) : dot(a, b));
                    }
                    else if (op == OP_NORMALIZE)
                    {
                        int w = (int)imm.x;
                        res = w == 2 ? float4(normalize(a.xy), 0, 0)
                            : (w == 3 ? float4(normalize(a.xyz), 0) : normalize(a));
                    }
                    else if (op == OP_LUMINANCE) res = sl_luminance(a.rgb);
                    else if (op == OP_CROSS)     res = float4(cross(a.xyz, b.xyz), 0);
                    else if (op == OP_REFLECT)   res = float4(reflect(a.xyz, b.xyz), 0);

                    else if (op == OP_MIX)        res = lerp(a, b, r[(int)imm.x]);
                    else if (op == OP_STEP)       res = step(a, b);
                    else if (op == OP_SMOOTHSTEP) res = smoothstep(a, b, r[(int)imm.x]);
                    else if (op == OP_SELECT)     res = lerp(r[(int)imm.x], b, step(0.5, a));

                    else if (op == OP_HSV2RGB)    res = float4(sl_hsv2rgb(a.xyz), 1);
                    else if (op == OP_NOISE)      res = sl_valueNoise(a.xy);
                    else if (op == OP_SIMPLEX)    res = sl_simplex(a.xy);
                    else if (op == OP_FBM)        res = sl_fbm(a.xy, (int)imm.x);
                    else if (op == OP_SAMPLE)     res = sampleSlot((int)imm.x, a.xy);

                    r[dst] = res;
                }

                return r[(int)_ResultReg];
            }
            ENDCG
        }
    }
}
