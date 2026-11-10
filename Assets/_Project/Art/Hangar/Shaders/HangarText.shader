// Hangar tabelaları (TextMesh) için derinlik sınamalı yazı. Varsayılan yazı tipi malzemesi (GUI/Text Shader) duvarların arkasından
// da görünüyordu (ZTest her zaman geçer); bu gölgelendirici derinliği sınar, arka yüzü çizmez (yazı tersten okunmaz).
Shader "RescueTeam/HangarText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Back ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1
        Pass
        {
            Name "HangarText"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                // TextMesh rengi köşe rengi olarak gelir ve doğrusal projede gama düzeltmesinden geçmez (lacivert açık maviye döner)
                half4 vc = v.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                vc.rgb = SRGBToLinear(vc.rgb);
                #endif
                o.color = vc * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = i.color;
                c.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                return c;
            }
            ENDHLSL
        }
    }
}
