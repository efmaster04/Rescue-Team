// Dere suyu: opak, stilize. uv.x = akış yönünde metre, uv.y = kıyıdan kıyıya 0..1.
// Akış yönünde kayan hafif açık bantlar, kıyıya doğru açılan ton, ana ışık yansıması.
Shader "OfisHaritasi/Su"
{
    Properties
    {
        _Deep ("Derin", Color) = (0.20, 0.33, 0.36, 1)
        _Shallow ("Sığ", Color) = (0.36, 0.47, 0.45, 1)
        _Foam ("Köpük", Color) = (0.70, 0.78, 0.78, 1)
        _Flow ("Akış Hızı (m/sn)", Float) = 0.9
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep; half4 _Shallow; half4 _Foam; float _Flow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; half fog : TEXCOORD2; float3 normalWS : TEXCOORD3; };
            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float x = i.uv.x, y = i.uv.y;
                float edge = abs(y - 0.5) * 2.0;
                half3 col = lerp(_Deep.rgb, _Shallow.rgb, smoothstep(0.35, 1.0, edge));
                float band = sin(x * 0.55 - t * _Flow * 0.55 + sin(y * 9.0 + x * 0.07) * 1.3);
                band += 0.6 * sin(x * 1.3 - t * _Flow * 1.3 + y * 17.0);
                half streak = smoothstep(1.05, 1.5, band) * (1.0 - edge * 0.6);
                half foam = smoothstep(0.86, 0.98, edge);
                col = lerp(col, _Foam.rgb, saturate(streak * 0.28 + foam * 0.55));
                float3 N = normalize(i.normalWS + float3(sin(x * 1.7 - t * 1.1) * 0.06, 0, cos(y * 21.0 + t) * 0.06));
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
            #else
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
            #endif
                Light L = GetMainLight(sc);
                half atten = L.shadowAttenuation;
                float3 Vd = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 albedo = SRGBToLinear(col);
                half3 c = albedo * (L.color * saturate(dot(N, L.direction)) * (0.5 + 0.5 * atten) + SampleSH(N));
                float3 H = normalize(L.direction + Vd);
                c += L.color * pow(saturate(dot(N, H)), 90.0) * 0.6 * atten;
                half fres = pow(1.0 - saturate(dot(N, Vd)), 4.0);
                c = lerp(c, SampleSH(float3(0, 1, 0)) * 1.1, fres * 0.35);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vd
            #pragma fragment fd
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vd(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half fd(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
