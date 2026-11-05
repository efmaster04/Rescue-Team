// Mountain stream / waterfall ribbon. uv.x = across (0..1), uv.y = distance downstream (m),
// color.r = steepness (0 calm .. 1 free fall).
Shader "SelVadisi/Waterfall"
{
    Properties
    {
        _WaterColor ("Water", Color) = (0.52, 0.42, 0.29, 1)
        _FoamColor ("Foam", Color) = (0.92, 0.90, 0.84, 1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _AmbSky ("Ambient Sky", Color) = (0.56, 0.61, 0.67, 1)
        _AmbGround ("Ambient Ground", Color) = (0.30, 0.29, 0.25, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+11" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "SV_Common.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _FoamColor;
                half4 _AmbSky;
                half4 _AmbGround;
            CBUFFER_END
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; half steep : TEXCOORD2; half fog : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;
                p.y += 0.05 * sin(v.uv.y * 1.7 - t * 6.0) * (1.0 - v.color.r);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                o.steep = v.color.r;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float speed = lerp(1.6, 7.5, i.steep);
                float2 uvA = float2(i.uv.x * 0.55, i.uv.y * 0.16 - t * speed * 0.16);
                half n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA).g;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA * float2(2.3, 1.9) + 0.3).r;
                half n3 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * 1.3, i.uv.y * 0.5 - t * speed * 0.5)).b;
                half edge = 1.0 - smoothstep(0.0, 0.22, min(i.uv.x, 1.0 - i.uv.x));
                half foam = saturate(i.steep * 0.85 + (n1 * 0.7 + n2 * 0.5 - 0.66) * 1.6 + edge * 0.35);
                half lines = (1.0 - smoothstep(0.03, 0.08, abs(n2 - 0.5))) * 0.55;
                foam = saturate(foam + lines + (n3 - 0.6) * i.steep);

                half3 albedo = lerp(_WaterColor.rgb, _FoamColor.rgb, foam);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 N = SV_FlatNormal(i.positionWS, V);
                N = normalize(N + half3(n1 - 0.5, 0, n2 - 0.5) * 0.4);
                half3 c = SV_Lit(albedo, N, i.positionWS, 0.55 * (1.0 - foam), _AmbSky.rgb, _AmbGround.rgb);
                c = MixFog(c, i.fog);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
