// Low-poly vertex coloured surface for everything in Sel Vadisi.
// color.rgb = sRGB albedo, color.a = wind sway weight, uv0.x = gloss, uv0.y = emission.
// Surfaces just above the flood line are darkened (wet mud stain).
Shader "SelVadisi/VertexLit"
{
    Properties
    {
        _WaterA ("Water Level (y at z=0)", Float) = 4
        _WaterB ("Water Level Slope", Float) = 0.0035
        _Wind ("Wind Strength", Float) = 0.35
        _Wet ("Wet Stain", Range(0,1)) = 1
        _AmbSky ("Ambient Sky", Color) = (0.56, 0.61, 0.67, 1)
        _AmbGround ("Ambient Ground", Color) = (0.30, 0.29, 0.25, 1)
        _SmoothNormals ("Smooth Normals (characters)", Range(0,1)) = 0
        _Rim ("Rim Light", Range(0,1)) = 0
        _Wrap ("Wrap Lighting", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "SV_Common.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _WaterA;
            float _WaterB;
            float _Wind;
            float _Wet;
            half4 _AmbSky;
            half4 _AmbGround;
            float _SmoothNormals;
            float _Rim;
            float _Wrap;
        CBUFFER_END

        // faceted normal for scenery, interpolated mesh normal for characters
        half3 SV_SurfaceNormal(float3 positionWS, float3 normalWS, float3 V)
        {
            half3 nf = SV_FlatNormal(positionWS, V);
            if (_SmoothNormals < 0.001) return nf;
            half3 ns = normalize(normalWS);
            return normalize(lerp(nf, ns, _SmoothNormals));
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
        };

        float3 SV_WorldPos(Attributes v)
        {
            float3 p = TransformObjectToWorld(v.positionOS.xyz);
            return SV_Wind(p, v.color.a, _Wind);
        }
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
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = SV_WorldPos(v);
                o.positionWS = p;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(p);
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 N = SV_SurfaceNormal(i.positionWS, i.normalWS, V);
                half3 albedo = SRGBToLinear(i.color.rgb);

                float wl = _WaterA + _WaterB * i.positionWS.z;
                float h = i.positionWS.y - wl;
                half wet = (1.0 - smoothstep(0.1, 1.15, h)) * _Wet;
                half stain = (1.0 - smoothstep(0.0, 0.07, abs(h - 1.2))) * _Wet * 0.35;
                albedo = lerp(albedo, albedo * half3(0.60, 0.50, 0.38), saturate(wet * 0.8 + stain));
                half gloss = saturate(i.uv.x + wet * 0.3);

                half3 c;
                if (_Wrap > 0.001)
                {
                    // soft wrapped diffuse for characters (skin and cloth read rounder)
                    Light L = GetMainLight(SV_ShadowCoord(i.positionWS));
                    half atten = L.shadowAttenuation * L.distanceAttenuation;
                    half ndl = saturate((dot(N, L.direction) + _Wrap) / (1.0 + _Wrap));
                    c = albedo * (L.color * (ndl * lerp(0.35, 1.0, atten)) + SV_Ambient(N, _AmbSky.rgb, _AmbGround.rgb));
                    float3 H = normalize(L.direction + V);
                    half spec = pow(saturate(dot(N, H)), exp2(2.0 + gloss * 8.0)) * gloss * gloss * 1.4;
                    c += L.color * (spec * atten);
                }
                else c = SV_Lit(albedo, N, i.positionWS, gloss, _AmbSky.rgb, _AmbGround.rgb);
                half rim = pow(1.0 - saturate(dot(N, V)), 3.0) * _Rim;
                c += (_AmbSky.rgb * 0.7 + albedo * 0.3) * rim;
                c += albedo * (i.uv.y * 3.0);
                c = MixFog(c, i.fog);
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vertShadow(Attributes v) : SV_POSITION
            {
                float3 p = SV_WorldPos(v);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - p);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, ld));
                return ApplyShadowClamping(cs);
            }

            half4 fragShadow() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            float4 vertDepth(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(SV_WorldPos(v));
            }
            half fragDepth(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDN
            #pragma fragment fragDN

            struct VDN { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            VDN vertDN(Attributes v)
            {
                VDN o;
                o.positionWS = SV_WorldPos(v);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(VDN i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                return half4(NormalizeNormalPerPixel(SV_SurfaceNormal(i.positionWS, i.normalWS, V)), 0.0);
            }
            ENDHLSL
        }
    }
}
