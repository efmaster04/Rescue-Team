// U1: Etkileşim hedefinin beyaz dış hattı (ters kabuk / inverted hull).
// Hedef mesh'in kopyası bu malzemeyle ikinci kez çizilir: ön yüzler atılır (Cull Front), köşeler ekranda
// sabit kalınlıkta (piksel) normal yönünde dışarı itilir; yalnız silüet çevresinde görünür.
// Köşe normalleri InteractionHighlight tarafından pozisyona göre ortalanır (sert kenarlarda boşluk kalmasın).
Shader "RescueTeam/HighlightOutline"
{
    Properties
    {
        _Color ("Renk", Color) = (1, 1, 1, 1)
        _WidthPx ("Kalınlık (px, 1080p'de)", Float) = 3
        _PulseSpeed ("Nabız hızı (rad/sn)", Float) = 4
        _PulseMin ("Nabız en düşük parlaklık", Range(0, 1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            // Derinlikte hafif geriye: ince/tek yüzlü yüzeylerde (sedye kanvası) kabuk yüzeyin üstünü kaplamasın, yalnız silüet kenarı kalsın
            Offset 2, 2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _WidthPx;
                float _PulseSpeed;
                float _PulseMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 posCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 normalVS = normalize(TransformWorldToViewDir(normalWS));
                // Ekran düzlemindeki yön normalize edilir: kameraya eğik bakan yüzeylerde (sedye kanvası) de tam kalınlık
                float2 dir2 = normalVS.xy;
                float len = length(dir2);
                dir2 = len > 1e-3 ? dir2 / len : float2(0.0, 0.0);
                // Ekranda sabit kalınlık: piksel → kırpma uzayı (w ile ölçeklenir, uzaklıktan bağımsız).
                float px = _WidthPx * (_ScreenParams.y / 1080.0);
                posCS.xy += dir2 * px * 2.0 / _ScreenParams.xy * posCS.w;
                o.positionCS = posCS;
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
