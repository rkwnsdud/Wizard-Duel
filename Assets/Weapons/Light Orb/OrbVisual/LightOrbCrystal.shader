Shader "WizardDuel/LightOrbCrystal"
{
    Properties
    {
        [HideInInspector] _AttackCoreFlash("Attack Core Flash", Range(0,1)) = 0
        _BaseColor("Tint", Color) = (0.96,0.99,1,1)
        _WarmColor("Champagne Reflection", Color) = (1,0.83,0.55,1)
        _EdgeColor("Cool Edge", Color) = (0.72,0.9,1,1)
        _Opacity("Opacity", Range(0.01,0.5)) = 0.12
        _FresnelPower("Fresnel Power", Range(1,8)) = 4
        _FacetContrast("Facet Contrast", Range(0,1)) = 0.45
        _Smoothness("Smoothness", Range(0,1)) = 0.96
        [NoScaleOffset] _CrystalCube("Crystal Studio Cube", Cube) = "black" {}
        _CrystalCubeBlend("Studio Cube Blend", Range(0,1)) = 0.8
        _ReflectionStrength("Reflection Strength", Range(0,3)) = 1
        _ReflectionRotation("Reflection Rotation", Range(-180,180)) = 0
        [NoScaleOffset] _FacetPlanes("Baked Mesh Facet Planes", Cube) = "black" {}
        [NoScaleOffset] _ExactFacetPlanes("Inner Convex Planes", 2D) = "black" {}
        _PlaneCount("Exact Plane Count", Float) = 0
        _VolumeCenter("Local Volume Centre", Vector) = (0,0,0,0)
        _VolumeRadius("Local Bounding Radius", Float) = 1
        _IOR("Index of Refraction", Range(1.01,2)) = 1.46
        _VolumeContribution("Internal Optical Contribution", Range(0,1)) = .2
        _Absorption("Absorption per Local Unit", Float) = .025
        _SparkleStrength("Facet Light Strength", Range(0,2)) = 1
        _RefractionStrength("Screen Refraction Strength", Range(0,1)) = .5
        _GameplayReadability("Distant Gameplay Readability", Range(0,1)) = 1
        _GameplayLayerWeight("Distant Layer Weight", Range(0,1)) = .5
        _ReadabilityRadius("Shared Orb Radius", Float) = 1
        _OuterShellAccent("Outermost Crystal Accent", Range(0,1)) = 0
        _MiddleShellAccent("Third Crystal From Centre Accent", Range(0,1)) = 0
        [NoScaleOffset] _DiagnosticCube("Diagnostic Reflection Cube", Cube) = "black" {}
        [Toggle] _UseDiagnosticCube("Use Diagnostic Cube", Float) = 0
        [Toggle] _ReflectionOnly("Reflection Only", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="LightOrbRefraction" }
            // The sampled background is already included in the final RGB.
            // Replace it once; alpha blending here would attenuate it twice.
            Blend One Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _AttackCoreFlash;
            half4 _BaseColor, _WarmColor, _EdgeColor;
            half _Opacity, _FresnelPower, _FacetContrast, _Smoothness;
            half _CrystalCubeBlend, _ReflectionStrength, _ReflectionRotation, _UseDiagnosticCube, _ReflectionOnly;
            float4 _VolumeCenter;
            float _VolumeRadius, _IOR, _VolumeContribution, _Absorption;
            float _PlaneCount;
            float _SparkleStrength;
            float _RefractionStrength;
            float _GameplayReadability, _GameplayLayerWeight;
            float _ReadabilityRadius;
            float _OuterShellAccent;
            float _MiddleShellAccent;
            CBUFFER_END
            TEXTURECUBE(_FacetPlanes); SAMPLER(sampler_FacetPlanes);
            TEXTURE2D(_ExactFacetPlanes);
            TEXTURE2D_X(_OrbLayerBackground);
            TEXTURECUBE(_CrystalCube); SAMPLER(sampler_CrystalCube);
            TEXTURECUBE(_DiagnosticCube); SAMPLER(sampler_DiagnosticCube);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 bary : TEXCOORD2; float3 edges : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float3 bary : TEXCOORD2; float3 edges : TEXCOORD3; };
            Varyings Vert(Attributes v)
            {
                Varyings o; VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.bary=v.bary; o.edges=v.edges; return o;
            }

            // Finite angular light sources: camera/mesh motion moves their
            // reflections across facets. Pixel footprint filtering prevents
            // subpixel flashing; there is no time noise or emissive overlay.
            float FacetSource(float3 direction, float3 lightDirection, float roughness)
            {
                float width = .0035 + roughness * roughness * .6;
                float footprint = max(dot(ddx(direction),ddx(direction)),dot(ddy(direction),ddy(direction)));
                float filteredWidth = width + min(footprint,.025);
                float angularDistance = max(0.0,1.0-dot(direction,normalize(lightDirection)));
                return exp2(-angularDistance / filteredWidth * 1.442695) * width / filteredWidth;
            }
            half3 FacetLights(float3 direction, half roughness)
            {
                return _SparkleStrength * (
                    half3(11.0,11.4,12.0)*FacetSource(direction,float3(-.45,.65,1),roughness)
                    + half3(9.0,8.5,7.7)*FacetSource(direction,float3(.35,.45,-1),roughness)
                    + half3(7.0,7.4,8.0)*FacetSource(direction,float3(-1,.25,-.25),roughness)
                    + half3(6.5,6.4,6.1)*FacetSource(direction,float3(1,.75,.2),roughness));
            }
            float3 StudioRay(float3 direction,float3 positionWS)
            {
                // Finite virtual studio: a reflection changes across a large
                // planar facet instead of filling it with one cubemap value.
                float radius=length(TransformObjectToWorldDir(float3(_ReadabilityRadius,0,0),false))*2.5;
                float3 offset=positionWS-TransformObjectToWorld(_VolumeCenter.xyz);
                float b=dot(offset,direction);
                float distance=-b+sqrt(max(0,b*b+radius*radius-dot(offset,offset)));
                return normalize(offset+direction*distance);
            }
            half3 JewelGlints(float3 direction)
            {
                // Sparse small sources: bright reflections cover only a small
                // fraction of each layer, preserving the view of the interior.
                half3 highlights = _SparkleStrength * (
                    half3(15,16,18)*FacetSource(direction,float3(.8,.25,.55),.015)
                    + half3(17,16,14)*FacetSource(direction,float3(-.6,.65,.3),.015)
                    + half3(13,15,18)*FacetSource(direction,float3(.3,-.55,.8),.015)
                    + half3(16,15,13)*FacetSource(direction,float3(-.7,-.3,-.6),.015)
                    + half3(14,16,18)*FacetSource(direction,float3(.45,.65,-.6),.015)
                    + half3(16,16,15)*FacetSource(direction,float3(-.2,.95,.1),.015));
                return highlights/(1.0h+highlights*.16h);
            }
            half3 OpticalEnvironment(float3 direction, float3 positionWS, half roughness)
            {
                half3 scene = GlossyEnvironmentReflection(direction, positionWS, roughness, 1.0h);
                direction=StudioRay(direction,positionWS);
                half3 studio = SAMPLE_TEXTURECUBE_LOD(_CrystalCube, sampler_CrystalCube, direction, roughness * 6.0h).rgb;
                return lerp(scene, studio + FacetLights(direction,roughness), _CrystalCubeBlend);
            }

            // Intersect the original convex mesh's baked planes. The sphere is
            // only an initial estimate; two corrections recover the exit facet.
            void ExitFacet(float3 origin, float3 direction, out float3 hit, out float3 normal, out float distance)
            {
                hit = origin;
                normal = -direction;
                distance = .0001;
                if (_PlaneCount > 0)
                {
                    distance = 1e8;
                    normal = -direction;
                    [loop] for (int index = 0; index < (int)_PlaneCount; index++)
                    {
                        float4 plane = LOAD_TEXTURE2D(_ExactFacetPlanes, int2(index,0));
                        float denominator = dot(plane.xyz,direction);
                        if (denominator <= .00001) continue;
                        float candidate = (plane.w-dot(plane.xyz,origin))/denominator;
                        if (candidate > .00001 && candidate < distance)
                        { distance = candidate; normal = plane.xyz; }
                    }
                    hit = origin + direction * distance;
                    return;
                }
                float b = dot(origin, direction);
                float t = -b + sqrt(max(0.0, b*b + _VolumeRadius*_VolumeRadius - dot(origin,origin)));
                hit = origin + direction * t;
                float4 plane = 0;
                [unroll] for (int step = 0; step < 3; step++)
                {
                    plane = SAMPLE_TEXTURECUBE_LOD(_FacetPlanes, sampler_FacetPlanes, normalize(hit), 0);
                    float denominator = dot(plane.xyz, direction);
                    if (denominator > .0001)
                        t = max(.0001, (plane.w - dot(plane.xyz, origin)) / denominator);
                    hit = origin + direction * t;
                }
                normal = normalize(plane.xyz);
                distance = t;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                Light key = GetMainLight();
                half fresnel = pow(1.0h - saturate(dot(n, v)), _FresnelPower);
                half3 h = SafeNormalize(key.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(32.0h, 256.0h, _Smoothness));
                half3 r = reflect(-v, n);
                half perceptualRoughness = saturate(1.0h - _Smoothness);
                half3 environment = GlossyEnvironmentReflection(r, i.positionWS, perceptualRoughness, 1.0h);
                // Each nested crystal samples the studio environment at a
                // different azimuth. This avoids four identical bright panels
                // stacking into detached white bands while leaving the real
                // game reflection direction untouched.
                half angle = radians(_ReflectionRotation);
                half sineAngle, cosineAngle;
                sincos(angle, sineAngle, cosineAngle);
                half3 studioDirection = half3(
                    cosineAngle * r.x - sineAngle * r.z,
                    r.y,
                    sineAngle * r.x + cosineAngle * r.z);
                studioDirection=StudioRay(studioDirection,i.positionWS);
                half3 studioEnvironment = SAMPLE_TEXTURECUBE_LOD(_CrystalCube, sampler_CrystalCube, studioDirection, perceptualRoughness * 6.0h).rgb;
                studioEnvironment += FacetLights(studioDirection,perceptualRoughness);
                environment = lerp(environment, studioEnvironment, _CrystalCubeBlend);
                half3 diagnosticEnvironment = SAMPLE_TEXTURECUBE_LOD(_DiagnosticCube, sampler_DiagnosticCube, r, perceptualRoughness * 6.0h).rgb;
                environment = lerp(environment, diagnosticEnvironment, _UseDiagnosticCube);

                // A clear dielectric does not paint diffuse grey over what is
                // behind it. Face centres preserve the framebuffer; reflection
                // grows from a restrained F0 and with the viewing angle.
                // A slightly broader dielectric response lets low and middle
                // environment values describe the complete solid without
                // raising opacity, tinting the surface white, or adding glow.
                float f0 = (_IOR - 1.0) / (_IOR + 1.0);
                f0 *= f0;
                // The centre carries the visual mass; outer layers transmit
                // more instead of stacking four equally grey surfaces.
                half coreWeight=smoothstep(.35h,1.0h,_GameplayLayerWeight);
                half reflectivity = lerp(.035h,.25h,coreWeight)
                    + lerp(.16h,.40h,coreWeight)*pow(1.0-saturate(dot(n,v)),5.0);
                // Give outer facets optical weight without outlining a sphere.
                reflectivity += _OuterShellAccent*(.055h+.10h*pow(1.0-saturate(dot(n,v)),2.0));
                reflectivity += _MiddleShellAccent*.065h;
                // At gameplay size, subpixel refraction reads as noise. Crossfade
                // to stable facet reflections without a spherical rim overlay.
                float radiusWS = length(TransformObjectToWorldDir(float3(_ReadabilityRadius,0,0),false));
                float centreDepth = max(.01,-TransformWorldToView(TransformObjectToWorld(_VolumeCenter.xyz)).z);
                float diameterPixels = radiusWS * abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y
                    / lerp(centreDepth,1.0,unity_OrthoParams.w);
                float gameplay = (1.0-smoothstep(90.0,220.0,diameterPixels))*_GameplayReadability;
                half3 gameBackground = SAMPLE_TEXTURE2D_X(_OrbLayerBackground,sampler_LinearClamp,GetNormalizedScreenSpaceUV(i.positionCS)).rgb;
                float3 radial = normalize(TransformObjectToWorldNormal(TransformWorldToObject(i.positionWS)-_VolumeCenter.xyz));
                // Preserve the image of inner crystals through the central
                // viewing area of each surrounding layer. The centre itself
                // keeps its complete optical response.
                // L3 is also a surrounding shell: it must reveal L4. Do not
                // use its reflection weight to decide whether it contains a core.
                float containsInnerCrystal=1.0-smoothstep(.85,.99,_GameplayLayerWeight);
                float innerVisibility=containsInnerCrystal*smoothstep(.52,.88,saturate(dot(radial,v)));
                float gameReflection = lerp(.09,.25,_GameplayLayerWeight) + .32*pow(1.0-saturate(dot(n,v)),3.0);
                half3 controlledEnvironment = environment / (1.0 + environment*.22);
                half3 gameColor = gameBackground*(1.0-gameReflection) + controlledEnvironment*gameReflection;
                // Broader finite sources survive a 30-50 pixel orb. Their
                // highlights still follow facet orientation, never a timer.
                gameColor += FacetLights(studioDirection,.18h)*lerp(.35,.60,_GameplayLayerWeight);
                gameColor += JewelGlints(studioDirection)*_OuterShellAccent*.18h;
                half3 attackFlash=half3(9.0h,9.5h,10.0h)*saturate(_AttackCoreFlash);
                if (gameplay > .999 && _ReflectionOnly < .5) return half4(gameColor+attackFlash,1);
                float3 origin = TransformWorldToObject(i.positionWS) - _VolumeCenter.xyz;
                float3 entry = refract(-v, n, 1.0 / _IOR);
                float3 direction = normalize(TransformWorldToObjectDir(entry));
                float3 hit = origin, exitNormal = -direction;
                float pathLength = .0001;
                ExitFacet(origin + direction * .0002, direction, hit, exitNormal, pathLength);
                float3 exitWorldNormal = normalize(TransformObjectToWorldNormal(exitNormal));
                float3 exitWorldDirection = normalize(TransformObjectToWorldDir(direction));
                float3 transmitted = refract(exitWorldDirection, -exitWorldNormal, _IOR);
                float exitFresnel = f0 + (1.0-f0) * pow(1.0-saturate(dot(exitWorldDirection,exitWorldNormal)),5.0);
                bool totalInternalReflection = dot(transmitted,transmitted) < .0001;
                float3 internalDirection = reflect(direction, exitNormal);
                float3 secondHit = hit, secondNormal = -internalDirection;
                float secondLength = .0001;
                ExitFacet(hit + internalDirection * .0002, internalDirection, secondHit, secondNormal, secondLength);
                float3 secondWorld = normalize(TransformObjectToWorldDir(internalDirection));
                float3 secondExit = refract(secondWorld, -normalize(TransformObjectToWorldNormal(secondNormal)), _IOR);
                // Bound the ray to one internal bounce. Unresolved TIR uses a
                // reflected environment approximation, never an emission term.
                if (dot(secondExit,secondExit) < .0001) secondExit = reflect(secondWorld, normalize(TransformObjectToWorldNormal(secondNormal)));
                half3 internalLight = OpticalEnvironment(normalize(secondExit), i.positionWS, perceptualRoughness);
                half3 throughLight = totalInternalReflection ? internalLight : OpticalEnvironment(normalize(transmitted), i.positionWS, perceptualRoughness);
                half3 volumeLight = lerp(throughLight, internalLight, totalInternalReflection ? 1.0 : exitFresnel);
                float attenuation = exp(-max(0.0,_Absorption) * (pathLength + (totalInternalReflection ? secondLength : 0.0)));
                // Clear, near-normal exit faces retain the scene behind them;
                // internal reflection and grazing exits carry the solid's
                // faceted light response. This prevents a white painted core.
                float internalCoverage = totalInternalReflection ? 1.0 : lerp(.16,1.0,saturate(exitFresnel*3.0));
                float volumeWeight = (1.0-reflectivity) * _VolumeContribution * internalCoverage;
                // Reflection and optical transmission replace the same share
                // of framebuffer energy; no independent additive white fill.
                // Keep the near-view sparkle on reflective facets, rather than
                // replacing an entire transmitted face with a bright softbox.
                half3 environmentReflection = environment * reflectivity
                    + FacetLights(studioDirection,.10h)*.06h
                    + JewelGlints(studioDirection)*(lerp(.38h,.62h,_GameplayLayerWeight)+_OuterShellAccent*.18h);
                half3 directHighlight = key.color * spec * reflectivity * .15h;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 exitPositionWS = TransformObjectToWorld((totalInternalReflection ? secondHit : hit) + _VolumeCenter.xyz);
                float3 exitDirectionWS = normalize(totalInternalReflection ? secondExit : transmitted);
                float surfaceEyeDepth = -TransformWorldToView(i.positionWS).z;
                float backgroundDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float worldRadius = length(TransformObjectToWorldDir(float3(_VolumeRadius,0,0), false));
                float travel = clamp(backgroundDepth - surfaceEyeDepth, .02, worldRadius * 3.0);
                float3 projectedPosition = exitPositionWS + exitDirectionWS * travel;
                // Dense outer shells use a stable thin-prism displacement;
                // their directional exit-plane lookup is for reflection only.
                if (_PlaneCount < .5)
                    projectedPosition = i.positionWS + (entry + v) * worldRadius * saturate(pathLength / _VolumeRadius) * .35;
                float4 projectedClip = TransformWorldToHClip(projectedPosition);
                float4 projectedScreen = ComputeScreenPos(projectedClip);
                float2 refractedUV = projectedScreen.xy / max(.0001,projectedScreen.w);
                refractedUV = lerp(screenUV,refractedUV,_RefractionStrength);
                float2 pixelOffset = (refractedUV-screenUV)*_ScaledScreenParams.xy;
                float maxPixels = lerp(3.0,20.0,_RefractionStrength);
                // A ray leaving the visible image must not keep sampling the
                // dark character directly behind the original pixel. Use the
                // shared optical environment for off-screen transmission.
                float screenConfidence = smoothstep(.15,.75,dot(exitDirectionWS,-v));
                screenConfidence *= 1.0-smoothstep(maxPixels,maxPixels*4.0,length(pixelOffset));
                if (_PlaneCount < .5) screenConfidence = 1.0;
                pixelOffset *= min(1.0,maxPixels/max(.001,length(pixelOffset)));
                pixelOffset *= lerp(1.0,.15,innerVisibility);
                refractedUV = screenUV + pixelOffset/_ScaledScreenParams.xy;
                float border = min(min(refractedUV.x,1-refractedUV.x),min(refractedUV.y,1-refractedUV.y));
                refractedUV = lerp(screenUV,refractedUV,saturate(border*40));
                float sampledDepth = LinearEyeDepth(SampleSceneDepth(saturate(refractedUV)),_ZBufferParams);
                // Do not pull a foreground character/weapon across the crystal.
                if (sampledDepth < surfaceEyeDepth-.015) refractedUV = screenUV;
                half3 refractedBackground = SAMPLE_TEXTURE2D_X(_OrbLayerBackground,sampler_LinearClamp,saturate(refractedUV)).rgb;
                half3 boundedThroughLight = throughLight / (1.0h + throughLight*.45h);
                refractedBackground = lerp(refractedBackground,boundedThroughLight,(1.0-screenConfidence)*.18);
                // A layer samples the previous layer's complete image. Keep the
                // optical environment only for reflected light, not transmission.
                half exitReflection = min(lerp(.08h,.34h,coreWeight),_VolumeContribution * (totalInternalReflection ? .6 : exitFresnel));
                half3 boundedInternalLight = internalLight / (1.0h + internalLight*.35h);
                half3 transmission = refractedBackground * (1.0-exitReflection) + boundedInternalLight * exitReflection;
                half3 premultiplied = environmentReflection + transmission * ((1.0-reflectivity)*attenuation) + directHighlight;
                // A solid outer crystal has a second optical interface. Show
                // that exit facet through the front instead of relying only
                // on the front surface's rim and isolated glints.
                // The dense outer mesh uses a discrete exit-plane lookup.
                // Stabilize only its rear reflection to avoid tiny lookup jumps.
                float3 rearNormal=normalize(lerp(exitWorldNormal,
                    TransformObjectToWorldNormal(normalize(hit)),_OuterShellAccent*.45));
                float3 rearRay=reflect(exitWorldDirection,-rearNormal);
                float3 rearPosition=TransformObjectToWorld(hit+_VolumeCenter.xyz);
                half3 rearEnvironment=OpticalEnvironment(rearRay,rearPosition,.12h);
                rearEnvironment=rearEnvironment/(1.0h+rearEnvironment*.22h);
                float opticalDepth=saturate(pathLength/max(.0001,_VolumeRadius));
                float rearCoverage=_OuterShellAccent*opticalDepth*lerp(.16,.38,saturate(exitFresnel*3.0));
                transmission=lerp(transmission,rearEnvironment,rearCoverage);
                premultiplied=environmentReflection+transmission*((1.0-reflectivity)*attenuation)+directHighlight;
                // The small inner polyhedron needs broad, bounded reflections
                // from the same sources to stay legible against a dark player.
                half3 coreReflection=FacetLights(studioDirection,.28h);
                coreReflection=coreReflection/(1.0h+coreReflection*.5h);
                premultiplied += coreReflection*(coreWeight*.32h+_MiddleShellAccent*.16h);
                // Narrow, antialiased optical bevels on actual facet boundaries.
                // Unlike wireframe, coplanar triangulation edges are excluded.
                float3 edgePixels=i.bary/max(fwidth(i.bary),.0001);
                edgePixels=lerp(float3(1000,1000,1000),edgePixels,i.edges);
                float bevel=1.0-smoothstep(.15,.85,min(edgePixels.x,min(edgePixels.y,edgePixels.z)));
                float3 bevelNormal=normalize(lerp(n,radial,.65));
                half3 bevelEnvironment=OpticalEnvironment(reflect(-v,bevelNormal),i.positionWS,.08h);
                bevelEnvironment=bevelEnvironment/(1+bevelEnvironment*.25);
                // Preserve subdued environment-dependent edges on the dark
                // side instead of cutting them off at a hard .8 threshold.
                // Compress bright edges so one side does not dominate.
                half3 edgeResponse=bevelEnvironment*.14h+max(bevelEnvironment-.55h,0)*.55h;
                edgeResponse=edgeResponse/(1.0h+edgeResponse*.65h);
                premultiplied += edgeResponse*bevel*(lerp(.55h,.80h,_OuterShellAccent)+_MiddleShellAccent*.30h);
                premultiplied += JewelGlints(StudioRay(reflect(-v,bevelNormal),i.positionWS))*bevel*.18h;
                // No radial silhouette light: only real facets and selective
                // bevel reflections describe the crystal boundary.
                // The undistorted previous-layer image is not a flat tint:
                // it contains the actual inner polyhedra, metal and background.
                // On the largest layer, reserve the clear window for the
                // centre; leave the surrounding real facets visibly reflective.
                float visibilityMix=lerp(innerVisibility,pow(innerVisibility,2.0),_OuterShellAccent);
                // L2 alone was over-suppressed around the inner pair. Retain
                // a central clear window but expose its surrounding facet band.
                visibilityMix=lerp(visibilityMix,pow(innerVisibility,2.0),_MiddleShellAccent);
                premultiplied=lerp(premultiplied,gameBackground,visibilityMix*.78);
                if (_ReflectionOnly > .5h) return half4(environment, 1.0h);
                return half4(lerp(premultiplied,gameColor,gameplay)+attackFlash, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
