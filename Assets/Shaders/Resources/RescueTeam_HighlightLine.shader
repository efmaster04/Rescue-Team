// U1: Halat (LineRenderer) vurgusu. Halat çizgisinin kopyası daha geniş ve beyaz çizilir; derinlikte hafif
// geriye itildiği (Offset) için ortada asıl halat görünür, iki yanında beyaz kenar kalır.
Shader "RescueTeam/HighlightLine"
{
    Properties
    {
        _Color ("Renk", Color) = (1, 1, 1, 1)
        _PulseSpeed ("Nabız hızı (rad/sn)", Float) = 4
        _PulseMin ("Nabız en düşük parlaklık", Range(0, 1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Line"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Offset 1, 1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _PulseSpeed;
                float _PulseMin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float pulse = lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
                return half4(_Color.rgb * pulse, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
