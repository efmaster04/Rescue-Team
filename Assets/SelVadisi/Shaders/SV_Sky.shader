// Overcast storm sky with slowly drifting cloud layers.
Shader "SelVadisi/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.50, 0.58, 0.65, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.76, 0.78, 1)
        _CloudLight ("Cloud Light", Color) = (0.80, 0.82, 0.84, 1)
        _CloudDark ("Cloud Dark", Color) = (0.42, 0.46, 0.50, 1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _CloudSpeed ("Cloud Speed", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Horizon;
                half4 _CloudLight;
                half4 _CloudDark;
                float _CloudSpeed;
            CBUFFER_END
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            float _SV_Flash;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 col = lerp(_Horizon.rgb, _Zenith.rgb, saturate(pow(saturate(h), 0.55)));
                float t = _Time.y * _CloudSpeed;
                float2 uv = d.xz / (max(h, 0.0) + 0.14);
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 0.11 + float2(t * 0.0021, t * 0.0013)).g * 0.62
                       + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 0.29 - float2(t * 0.0037, t * 0.0011)).r * 0.38;
                half cover = smoothstep(0.3, 0.72, n);
                half3 cloud = lerp(_CloudLight.rgb, _CloudDark.rgb, smoothstep(0.45, 0.9, n));
                half horizonFade = smoothstep(-0.02, 0.18, h);
                col = lerp(col, cloud, cover * horizonFade * 0.92);
                col = lerp(_Horizon.rgb, col, smoothstep(-0.05, 0.06, h));
                col += _SV_Flash * half3(0.55, 0.6, 0.72) * (0.4 + cover);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
