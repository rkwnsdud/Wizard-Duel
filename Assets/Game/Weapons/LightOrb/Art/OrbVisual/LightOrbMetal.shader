Shader "WizardDuel/LightOrbMetal"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Original Tint", Color) = (1,1,1,1)
        _BumpMap("Original glTF RGB Normal", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        _MetallicGlossMap("Metallic R / Smoothness A", 2D) = "white" {}
        _Metallic("Metallic", Range(0,1)) = 1
        _Smoothness("Smoothness", Range(0,1)) = 1
        _Cutoff("Alpha Cutoff", Float) = .5
        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Float) = 1
        _EmissionMap("Emission", 2D) = "black" {}
        _EmissionColor("Emission Color", Color) = (0,0,0,0)
        _SpecColor("Specular", Color) = (.2,.2,.2,1)
        _Surface("Surface", Float) = 0
        _Cull("Cull", Float) = 0
        [NoScaleOffset] _OrbEnvironment("Shared Orb Environment", Cube) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "OrbForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment OrbFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #define _NORMALMAP 1
            #define _METALLICSPECGLOSSMAP 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            TEXTURECUBE(_OrbEnvironment); SAMPLER(sampler_OrbEnvironment);
            half4 OrbFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv, surface);
                // Embedded glTF PNG normals are RGB, not Unity's DXT5nm
                // packed RG/AG format. Preserve the source vectors exactly.
                half3 normalTS = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv).xyz * 2 - 1;
                normalTS.xy *= _BumpScale;
                surface.normalTS = normalize(normalTS);
                InputData lighting;
                InitializeInputData(input, surface.normalTS, lighting);
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.shadowMask = half4(1,1,1,1);
                half4 color = UniversalFragmentPBR(lighting, surface);
                BRDFData brdf;
                InitializeBRDFData(surface, brdf);
                half3 reflection = reflect(-lighting.viewDirectionWS, lighting.normalWS);
                half roughness = brdf.perceptualRoughness;
                half3 originalEnvironment = GlossyEnvironmentReflection(reflection, lighting.positionWS, roughness, 1.0h);
                half3 orbEnvironment = SAMPLE_TEXTURECUBE_LOD(_OrbEnvironment, sampler_OrbEnvironment,
                    reflection, PerceptualRoughnessToMipmapLevel(roughness)).rgb;
                half fresnel = Pow4(1 - saturate(dot(lighting.normalWS, lighting.viewDirectionWS)));
                // Replace only this material's specular environment; all scene
                // lights, maps, shadows and other renderers remain unchanged.
                color.rgb += EnvironmentBRDF(brdf, half3(0,0,0), orbEnvironment * .82h - originalEnvironment, fresnel);
                half3 diffuseEnvironment = SAMPLE_TEXTURECUBE_LOD(_OrbEnvironment, sampler_OrbEnvironment,
                    lighting.normalWS, 7).rgb;
                color.rgb += diffuseEnvironment * brdf.diffuse * .65h;
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}
