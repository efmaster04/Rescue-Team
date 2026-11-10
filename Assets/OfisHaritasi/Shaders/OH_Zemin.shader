// Ofis haritası arazisi: köşe rengi (sRGB) albedo, yüz başına düz normal (faset görünüm),
// ana ışık + gölge, küresel harmonik ortam ışığı, sis. Doku kullanmaz.
Shader "OfisHaritasi/Zemin"
{
    Properties
    {
        _Tint ("Renk Çarpanı", Color) = (1,1,1,1)
        _Gloss ("Parlaklık", Range(0,1)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Gloss;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : TEXCOORD1;
                half fog : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 N = normalize(i.normalWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
            #else
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
            #endif
                Light L = GetMainLight(sc);
                half atten = L.shadowAttenuation * L.distanceAttenuation;
                half3 albedo = SRGBToLinear(i.color.rgb) * _Tint.rgb;
                half ndl = saturate(dot(N, L.direction));
                half3 amb = SampleSH(N);
                half3 c = albedo * (L.color * ndl * atten + amb);
                float3 H = normalize(L.direction + V);
                c += L.color * pow(saturate(dot(N, H)), 24.0) * _Gloss * atten;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            float4 vertShadow(Attributes v) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - p);
            #else
                float3 ld = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, ld)));
            }
            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            float4 vertDepth(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half fragDepth(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDN
            #pragma fragment fragDN
            struct VDN { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            VDN vertDN(Attributes v)
            {
                VDN o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(VDN i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
