// 내 화면에서 속이 비쳐 보이는 갈대(이슈 #93). URP Lit 전방 패스를 그대로 써서 불투명 갈대와 같은 빛을 받는다.
// 갈대 잎은 여러 겹 겹치므로 그냥 알파 블렌딩하면 겹친 곳마다 진해진다. 스텐실 한 비트로 한 픽셀에 한 번만 섞어서,
// 잎이 몇 겹이든, 옆 칸 갈대와 겹치든 BushVisibilityRules.BushAlpha만큼만 비쳐 보이게 한다.
// 깊이는 쓰지 않고 검사만 하므로, 캐릭터 앞에 있는 갈대가 캐릭터 위에 겹쳐 부시 안에 서 있는 것처럼 보인다.
// 부시 안의 캐릭터(WizardDuel/CharacterFaded, 큐 2999)는 이 갈대(큐 3000)보다 먼저 깊이를 쓰고 그려지므로 순서가 맞는다.
// 정점 단계에서 갈대를 카메라 쪽으로 조금 당겨(REED_DEPTH_PULL) 캐릭터 바로 뒤 갈대까지 겹치게 한다.
// 갈대 뒤의 캐릭터와 이펙트는 그대로 비쳐 보인다. 그림자와 깊이 텍스처에는 들어가지 않는다.
Shader "WizardDuel/ReedFaded"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 0.6)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        [HDR] _EmissionColor("Color", Color) = (0, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0.0
        [HideInInspector] _Surface("__surface", Float) = 1.0
        [HideInInspector] _Cutoff("Alpha Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            // 128 비트: 이번 프레임에 반투명 갈대가 이미 섞인 픽셀. 카메라가 프레임마다 스텐실을 지운다.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ReedFadedVertex
            #pragma fragment LitPassFragment

            // 늘 반투명이라 키워드 대신 정의로 둔다. 알파는 _Surface = 1로 OutputAlpha가 그대로 내보낸다.
            #define _SURFACE_TYPE_TRANSPARENT 1
            // 마른 갈대는 무광이다. 불투명 Reeds.mat도 반사광과 환경 반사를 꺼서 둘의 밝기를 맞춘다.
            #define _SPECULARHIGHLIGHTS_OFF 1
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            // URP Lit의 ForwardLit 패스와 같은 파이프라인 키워드.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // 반투명 갈대를 카메라 쪽으로 끌어당겨 그리는 거리(m). 캐릭터는 갈대보다 먼저 깊이를 쓰므로, 캐릭터 몸보다 이만큼 뒤에 있는
            // 갈대까지 캐릭터 위에 겹친다. 앞줄 칸에 서서 앞에 갈대가 거의 없어도 갈대 사이에 서 있는 것처럼 보이게 하려는 값이다.
            // 전투 카메라는 직교라 화면 위치는 그대로고 깊이만 바뀐다. 1.0이면 부시 바로 앞 벽 윗면에 갈대 끝이 비쳐서 0.5로 둔다.
            #define REED_DEPTH_PULL 0.5

            Varyings ReedFadedVertex(Attributes input)
            {
                Varyings output = LitPassVertex(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // 뷰 행렬 셋째 행은 카메라 뒤쪽, 곧 화면에서 보는 사람 쪽 월드 방향이다.
                output.positionCS = TransformWorldToHClip(positionWS + UNITY_MATRIX_V[2].xyz * REED_DEPTH_PULL);
                return output;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
