// 부시 안에서 보이는 캐릭터와 무기를 한 덩어리로 반투명하게 그린다(이슈 #93). URP Lit 전방 패스를 그대로 써서 빛은 원래 머티리얼과 같다.
// 파츠마다 따로 알파 블렌딩하면 로브 뒤의 다리, 몸 뒤의 지팡이, 모자 안쪽처럼 원래 가려지던 면이 비친다.
// 그래서 두 단계로 그린다.
//   1. CharacterFadeDepth 패스: CharacterFadeFeature가 불투명 다음, 반투명 전에 이 셰이더를 쓰는 렌더러를 모두 모아 깊이만 쓴다.
//      캐릭터와 무기 전체에서 화면 픽셀마다 가장 앞에 있는 면의 깊이만 남는다.
//   2. ForwardLit 패스: 깊이를 쓰지 않고 그 깊이와 같거나 앞인 면만 _BaseColor.a(BushVisibilityRules.CharacterAlpha)로 섞는다.
//      스텐실 한 비트(64)로 한 픽셀에 한 번만 섞어서, 깊이가 같은 면이 겹쳐도 더 진해지지 않는다.
// 결과는 불투명하게 그린 캐릭터 그림 한 장을 0.8로 얹은 것과 같다. 캐릭터 뒤의 바닥과 벽은 비치고, 캐릭터 자신의 뒷면은 비치지 않는다.
// 반투명 갈대(WizardDuel/ReedFaded, 큐 3000)보다 먼저(큐 2999) 그려서, 캐릭터 앞의 갈대가 캐릭터 위에 겹친다.
// 캐릭터 깊이를 먼저 쓰므로 캐릭터 바로 뒤의 반투명 이펙트와 갈대는 캐릭터에 가려 비치지 않는다(0.2만큼만 비칠 것이 빠진다).
// 머티리얼은 Tools > Wizard Duel > Rebuild Faded Player Materials가 원래 URP Lit 머티리얼을 복사해 만든다(PlayerBushVisibility.CreateFadedCopy).
Shader "WizardDuel/CharacterFaded"
{
    Properties
    {
        // 원래 URP Lit 머티리얼의 값을 그대로 옮겨 받도록 Lit과 같은 이름을 쓴다.
        _WorkflowMode("WorkflowMode", Float) = 1.0
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        // 알파는 텍스처 알파와 상관없이 이 값만 쓴다.
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 0.8)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0, 0, 0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0
        // 원래 머티리얼의 면 그리기를 그대로 쓴다. 양면 로브는 앞에서 보이는 안쪽 면(모자 안 등)도 원래처럼 그린다.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2.0
        [HideInInspector] _Surface("__surface", Float) = 1.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CharacterFadeDepth"
            Tags { "LightMode" = "CharacterFadeDepth" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CharacterFadeDepthVertex
            #pragma fragment CharacterFadeDepthFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 두 패스가 같은 식으로 정점 위치를 계산해야 ForwardLit의 깊이 검사(LEqual)가 이 패스가 쓴 깊이와 정확히 맞는다.
            // 그래서 URP Lit 전방 패스(LitPassVertex)와 같은 GetVertexPositionInputs를 쓴다.
            DepthVaryings CharacterFadeDepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                return output;
            }

            half CharacterFadeDepthFragment(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            // 64 비트: 이번 프레임에 반투명 캐릭터가 이미 섞인 픽셀. 반투명 갈대는 128 비트를 따로 쓴다. 카메라가 프레임마다 스텐실을 지운다.
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment CharacterFadedFragment

            // 늘 반투명이라 키워드 대신 정의로 둔다. 반투명이면 화면 공간 AO를 받지 않는다(캐릭터는 AO 계산에 들어가지 않으므로 맞다).
            #define _SURFACE_TYPE_TRANSPARENT 1

            // 원래 머티리얼에서 옮겨 오는 URP Lit 재질 키워드.
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            // URP Lit의 ForwardLit 패스와 같은 파이프라인 키워드. 렌더링 레이어 출력(_WRITE_RENDERING_LAYERS)은 반투명이라 쓰지 않는다.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

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

            half4 CharacterFadedFragment(Varyings input) : SV_Target0
            {
                half4 color;
                LitPassFragment(input, color);
                // 텍스처 알파는 불투명 머티리얼에서 쓰지 않던 값이라 무시하고, 정해 둔 알파만 쓴다.
                return half4(color.rgb, _BaseColor.a);
            }
            ENDHLSL
        }

        // 부시 안에서도 그림자는 원래처럼 드리운다. 부시에 들어가고 나올 때 그림자가 깜빡이지 않는다.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack Off
}
