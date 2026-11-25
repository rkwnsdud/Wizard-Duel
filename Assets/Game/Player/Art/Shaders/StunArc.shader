Shader "WizardDuel/Stun Arc"
{
    Properties
    {
        _Frame ("Frame", Float) = 0
        _Brightness ("Brightness", Float) = 1
        _Breakup ("Breakup", Range(0, 1)) = 1
        _GapShift ("Gap Shift", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Frame;
                float _Brightness;
                float _Breakup;
                float _GapShift;
            CBUFFER_END

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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float BoltNoise(float coordinate, float seed)
            {
                float cell = floor(coordinate);
                float left = frac(sin(cell * 127.1 + seed) * 43758.5453);
                float right = frac(sin((cell + 1.0) * 127.1 + seed) * 43758.5453);
                return lerp(left, right, frac(coordinate)) * 2.0 - 1.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float seed = _Frame * 17.65 + 4.7;
                float center = 0.5 + 0.19 * BoltNoise(uv.x * 17.0, seed)
                    + 0.075 * BoltNoise(uv.x * 43.0, seed + 12.0);
                float distance = abs(uv.y - center);
                float width = 0.019 + 0.009 * BoltNoise(uv.x * 12.0, seed + 32.0);
                float edge = max(fwidth(distance), 0.0025);
                half core = 1.0 - smoothstep(width, width + edge, distance);
                half glow = exp2(-distance * distance * 92.0) * 0.83;
                half fade = smoothstep(0.0, 0.055, uv.x) * (1.0 - smoothstep(0.93, 1.0, uv.x));
                fade *= smoothstep(0.0, 0.10, uv.y) * (1.0 - smoothstep(0.9, 1.0, uv.y));

                float gapA = abs(uv.x - (0.24 + _GapShift));
                float gapB = abs(uv.x - (0.64 - _GapShift * 0.65));
                half segments = smoothstep(0.024, 0.037, gapA) * smoothstep(0.034, 0.048, gapB);
                fade *= lerp(1.0, segments, _Breakup);

                half3 light = half3(0.61, 0.83, 1.0) * (core * 1.04)
                            + half3(0.085, 0.38, 1.0) * glow;
                return half4(light * fade * _Brightness, 0);
            }
            ENDHLSL
        }
    }
}
