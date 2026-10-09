Shader "ReusableEffects/Lightning Beam"
{
    Properties
    {
        _LightningTex ("Lightning Detail Texture", 2D) = "white" {}
        [HDR] _Tint ("Blue Glow", Color) = (0.08, 0.35, 1, 1)
        [HDR] _CoreColor ("White Blue Core", Color) = (0.88, 0.96, 1, 1)
        _Intensity ("Emission Intensity", Range(0, 10)) = 5
        _Opacity ("Flash Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "LightningBeam"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_LightningTex);
            SAMPLER(sampler_LightningTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _LightningTex_ST;
                float4 _Tint;
                float4 _CoreColor;
                float _Intensity;
                float _Opacity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float2 detailUV = TRANSFORM_TEX(uv, _LightningTex);
                half maskCenter = SAMPLE_TEXTURE2D(
                    _LightningTex,
                    sampler_LightningTex,
                    detailUV
                ).g;
                half maskForward = SAMPLE_TEXTURE2D(
                    _LightningTex,
                    sampler_LightningTex,
                    detailUV + float2(0.035, 0.0)
                ).g;
                half maskBackward = SAMPLE_TEXTURE2D(
                    _LightningTex,
                    sampler_LightningTex,
                    detailUV - float2(0.035, 0.0)
                ).g;
                half mask = saturate(max(maskCenter, max(maskForward, maskBackward)));

                // Continuous white channel, with texture only modulating its brightness.
                // The glow lives inside the ribbon and needs no post-processing bloom.
                float radius = abs(uv.y * 2.0 - 1.0);
                half edgeFade = 1.0 - smoothstep(0.65, 1.0, radius);
                half core = exp2(-radius * radius * 180.0);
                half halo = exp2(-radius * radius * 7.0) * edgeFade;
                half detail = lerp(0.85, 1.0, mask);
                half3 color = (_CoreColor.rgb * core + _Tint.rgb * halo * 0.22)
                    * (_Intensity * detail * input.color.a * _Opacity);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
