Shader "WizardDuel/LightCrystal"
{
 Properties
 {
  _BaseColor("Crystal Tint", Color)=(0.78,0.88,1,1)
  [HDR] _EmissionColor("White Glow", Color)=(1.3,1.45,1.65,1)
  _Opacity("Transparency", Range(0,1))=0.42
  _FresnelPower("Edge Sharpness", Range(0.5,8))=3
  _GlowStrength("Glow Strength", Range(0,3))=0.7
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
  Pass
  {
   Name "CrystalForward"
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Back
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor, _EmissionColor;
   float _Opacity, _FresnelPower, _GlowStrength;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
   Varyings vert(Attributes v)
   {
    Varyings o;
    o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);
    return o;
   }
   half4 frag(Varyings i):SV_Target
   {
    float3 n=normalize(i.normalWS),v=GetWorldSpaceNormalizeViewDir(i.positionWS);
    float rim=pow(1-saturate(dot(n,v)),_FresnelPower);
    Light l=GetMainLight();
    float diffuse=saturate(dot(n,l.direction));
    float spec=pow(saturate(dot(n,normalize(l.direction+v))),64);
    float facet=0.12+0.18*abs(dot(n,normalize(float3(.6,.7,.3))));
    float3 color=_BaseColor.rgb*(.38+.5*diffuse+facet)+spec*l.color*.9;
    color+=_EmissionColor.rgb*_GlowStrength*(.12+rim*.9);
    return half4(color,saturate(_Opacity+rim*.42));
   }
   ENDHLSL
  }
 }
}
