// Muddy flood water with a real current.
// _FlowTex (baked by the generator over the whole map): xy = velocity (m/s), z = white-water amount, w = depth (m).
// - normals and mud swirls are advected with a two-phase flow map
// - white current lines are oriented along the local flow direction (directional flow grid)
// - foam piles up around houses, poles and piers, in rapids and at the waterfall
// - rain ripples and a gentle swell animate the surface
Shader "SelVadisi/FloodWater"
{
    Properties
    {
        _DeepColor ("Deep Mud", Color) = (0.42, 0.30, 0.18, 1)
        _ShallowColor ("Shallow Mud", Color) = (0.63, 0.50, 0.33, 1)
        _FoamColor ("Foam", Color) = (0.90, 0.86, 0.77, 1)
        _ReflectColor ("Sky Reflection", Color) = (0.62, 0.68, 0.72, 1)
        _FlowTex ("Flow (xy vel, z foam, w depth)", 2D) = "black" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _NormalTex ("Wave Normals", 2D) = "gray" {}
        _FlowRect ("Flow Rect (minX, minZ, sizeX, sizeZ)", Vector) = (-520, -760, 1040, 1520)
        _WaterA ("Water Level (y at z=0)", Float) = 4
        _WaterB ("Water Level Slope", Float) = 0.0035
        _WaveAmp ("Swell Height", Float) = 0.07
        _NormalStrength ("Ripple Strength", Float) = 0.45
        _LineStrength ("Current Lines", Range(0, 2)) = 1.25
        _Rain ("Rain Ripples", Range(0, 1)) = 1
        _AmbSky ("Ambient Sky", Color) = (0.56, 0.61, 0.67, 1)
        _AmbGround ("Ambient Ground", Color) = (0.30, 0.29, 0.25, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "SV_Common.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _FoamColor;
            half4 _ReflectColor;
            float4 _FlowRect;
            float _WaterA;
            float _WaterB;
            float _WaveAmp;
            float _NormalStrength;
            float _LineStrength;
            float _Rain;
            half4 _AmbSky;
            half4 _AmbGround;
        CBUFFER_END
        TEXTURE2D(_FlowTex); SAMPLER(sampler_FlowTex);
        TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

        struct Attributes { float4 positionOS : POSITION; };

        float4 SampleFlow(float2 xz)
        {
            float2 uv = (xz - _FlowRect.xy) / _FlowRect.zw;
            return SAMPLE_TEXTURE2D_LOD(_FlowTex, sampler_FlowTex, uv, 0);
        }

        float3 SV_WaterPos(float3 p)
        {
            float4 f = SampleFlow(p.xz);
            float sp = length(f.xy);
            float amp = _WaveAmp * (0.6 + 0.3 * sp + f.z * 1.3);
            float t = _Time.y;
            p.y += amp * (0.5 * sin(p.x * 0.21 + p.z * 0.13 + t * 1.3)
                        + 0.35 * sin(-p.x * 0.17 + p.z * 0.29 + t * 1.7)
                        + 0.25 * sin(p.x * 0.37 - p.z * 0.11 + t * 2.3));
            // small choppy bumps in white water
            p.y += f.z * 0.06 * sin(p.x * 1.3 + t * 5.0) * sin(p.z * 1.1 - t * 4.3);
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = SV_WaterPos(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // One cell of the directional-flow grid: the line pattern is rotated so it streams along the
            // flow sampled at the cell centre, then scrolled downstream at the local speed.
            float StreakCell(float2 p, float2 cell, float t, float2 dpdx, float2 dpdy)
            {
                const float G = 6.0;
                float4 f = SampleFlow((cell + 0.5) * G);
                float sp = length(f.xy);
                float2 d = sp > 0.02 ? f.xy / sp : float2(0.0, -1.0);
                float2 perp = float2(d.y, -d.x);
                float2 q = float2(dot(p, perp), dot(p, d));
                q.y -= t * sp * 0.9;
                const float2 sc = float2(1.0 / 7.0, 1.0 / 26.0);
                float2 gx = float2(dot(dpdx, perp), dot(dpdx, d)) * sc;
                float2 gy = float2(dot(dpdy, perp), dot(dpdy, d)) * sc;
                float n = SAMPLE_TEXTURE2D_GRAD(_NoiseTex, sampler_NoiseTex, q * sc, gx, gy).r;
                float m = SAMPLE_TEXTURE2D_GRAD(_NoiseTex, sampler_NoiseTex, q * float2(1.0 / 17.0, 1.0 / 43.0) + 0.37, gx * 0.4, gy * 0.6).g;
                // anti-aliased isoline of constant world width: far away it thins out instead of bloating
                float fw = max(length(float2(length(dpdx), length(dpdy))) * 0.32, 1e-4);
                const float lw = 0.03;
                float lineV = saturate(1.0 - abs(n - 0.5) / max(lw, fw)) * saturate(lw / fw);
                lineV *= smoothstep(0.44, 0.6, m);
                return lineV * saturate(sp * 0.7 - 0.05);
            }

            float Streaks(float2 p, float t)
            {
                float2 dpdx = ddx(p), dpdy = ddy(p);
                const float G = 6.0;
                float2 g = p / G - 0.5;
                float2 i0 = floor(g);
                float2 fr = g - i0;
                float2 w = fr * fr * (3.0 - 2.0 * fr);
                float a = StreakCell(p, i0, t, dpdx, dpdy);
                float b = StreakCell(p, i0 + float2(1, 0), t, dpdx, dpdy);
                float c = StreakCell(p, i0 + float2(0, 1), t, dpdx, dpdy);
                float e = StreakCell(p, i0 + float2(1, 1), t, dpdx, dpdy);
                return lerp(lerp(a, b, w.x), lerp(c, e, w.x), w.y);
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // Expanding rain-drop rings, returned as a normal offset.
            float2 Ripples(float2 p, float t, float cellSize, float seed)
            {
                float2 g = p / cellSize + seed;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float h1 = Hash(id), h2 = Hash(id + 17.17);
                float2 o = (float2(h1, h2) - 0.5) * 0.5;
                float ph = frac(t * 0.85 + h1 * 7.31);
                float2 r = f - o;
                float d = length(r);
                float rad = ph * 0.42;
                float ring = exp(-pow((d - rad) / 0.04, 2.0)) * (1.0 - ph) * (1.0 - ph);
                return d > 1e-4 ? (r / d) * ring : 0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 pw = i.positionWS;
                float2 p = pw.xz;
                float t = _Time.y;
                float4 flow = SampleFlow(p);
                float2 vel = flow.xy;
                float sp = length(vel);
                float foamMask = flow.z;
                float depth = max(flow.w, 0.0);

                // two-phase flow map (period 3 s)
                float ph0 = frac(t / 3.0);
                float ph1 = frac(t / 3.0 + 0.5);
                float w0 = 1.0 - abs(1.0 - 2.0 * ph0);
                float w1 = 1.0 - w0;
                float2 uvA = (p - vel * (ph0 * 3.0)) * 0.11;
                float2 uvB = (p - vel * (ph1 * 3.0)) * 0.11 + 0.5;
                half4 nA = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvA);
                half4 nB = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvB);
                half4 nC = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvA * 2.7 + 0.21);
                half4 nD = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvB * 2.7 + 0.63);
                half2 nxy = (nA.rg * 2.0 - 1.0) * w0 + (nB.rg * 2.0 - 1.0) * w1;
                nxy += ((nC.rg * 2.0 - 1.0) * w0 + (nD.rg * 2.0 - 1.0) * w1) * 0.6;
                half hgt = nA.b * w0 + nB.b * w1;

                // big slow mud swirls
                float2 uvS0 = (p - vel * (ph0 * 3.0)) * 0.018;
                float2 uvS1 = (p - vel * (ph1 * 3.0)) * 0.018 + 0.31;
                half swirl = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvS0).g * w0 + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvS1).g * w1;

                // foam: advected cellular bubbles masked by the baked white-water amount
                half cellA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA * 1.6).b;
                half cellB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvB * 1.6).b;
                half bubbles = cellA * w0 + cellB * w1;
                half foamField = foamMask * 1.15 + (hgt - 0.5) * 0.45 + (bubbles - 0.5) * 0.55;
                half foam = smoothstep(0.62, 0.8, foamField);
                foam = max(foam, smoothstep(0.85, 1.0, foamMask) * 0.85);

                float dist = distance(_WorldSpaceCameraPos, pw);
                half lines = Streaks(p, t) * _LineStrength * (1.0 - smoothstep(250.0, 600.0, dist));
                half white = saturate(foam + lines * 0.8);

                // rain rings near the camera
                float2 rip = 0;
                float rainFade = (1.0 - smoothstep(25.0, 90.0, dist)) * _Rain;
                if (rainFade > 0.001)
                {
                    rip = Ripples(p, t, 1.35, 0.0) + Ripples(p, t * 1.13, 0.95, 3.7) * 0.8;
                    rip *= rainFade * 0.9;
                }

                float ns = _NormalStrength * (0.75 + 0.25 * saturate(sp)) * (1.0 + foamMask);
                float3 N = normalize(float3(nxy.x * ns + rip.x, 1.0, nxy.y * ns + rip.y));

                // albedo: deeper water darker, shallow edges lighter and more ochre
                half shallow = 1.0 - saturate(depth / 2.2);
                half3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(shallow * 0.75 + (swirl - 0.5) * 0.7));
                baseCol *= 0.9 + 0.2 * hgt;
                half3 albedo = lerp(baseCol, _FoamColor.rgb, white);

                float3 V = normalize(_WorldSpaceCameraPos - pw);
                Light L = GetMainLight(SV_ShadowCoord(pw));
                half atten = L.shadowAttenuation;
                half ndl = saturate(dot(N, L.direction) * 0.5 + 0.5);
                half3 col = albedo * (L.color * ndl * lerp(0.45, 1.0, atten) * 0.85 + SV_Ambient(half3(0, 1, 0), _AmbSky.rgb, _AmbGround.rgb));

                half fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(N, V)), 5.0);
                half3 refl = _ReflectColor.rgb * (1.0 + _SV_Flash * 2.0);
                col = lerp(col, refl, fres * 0.5 * (1.0 - white));
                float3 H = normalize(L.direction + V);
                half spec = pow(saturate(dot(N, H)), 160.0) * 1.6 * atten;
                col += L.color * spec * (1.0 - white * 0.8);

                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            float4 vertDepth(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(SV_WaterPos(TransformObjectToWorld(v.positionOS.xyz)));
            }
            half fragDepth(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDN
            #pragma fragment fragDN
            float4 vertDN(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(SV_WaterPos(TransformObjectToWorld(v.positionOS.xyz)));
            }
            half4 fragDN(float4 pos : SV_POSITION) : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
