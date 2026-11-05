#ifndef SV_COMMON_INCLUDED
#define SV_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Global lightning flash (0..1), driven by LightningFlash.cs. Defaults to 0 when unset.
float _SV_Flash;

// Wind sway: weight w comes from vertex colour alpha (0 = rigid, 1 = leaf tips).
float3 SV_Wind(float3 posWS, float w, float strength)
{
    if (w > 0.002)
    {
        float t = _Time.y;
        float gust = 0.6 + 0.4 * sin(t * 0.43 + posWS.x * 0.011 + posWS.z * 0.007);
        float2 off;
        off.x = sin(t * 1.9 + posWS.x * 0.11 + posWS.z * 0.07) + 0.35 * sin(t * 3.7 + posWS.y * 0.9 + posWS.x);
        off.y = cos(t * 1.6 + posWS.z * 0.09 + posWS.x * 0.05) * 0.6;
        posWS.xz += off * (w * strength * gust);
        posWS.y -= abs(off.x) * w * strength * 0.15;
    }
    return posWS;
}

// Faceted (low-poly) normal from screen-space derivatives, flipped toward the viewer.
float3 SV_FlatNormal(float3 posWS, float3 viewDirWS)
{
    float3 n = normalize(cross(ddy(posWS), ddx(posWS)));
    return dot(n, viewDirWS) < 0.0 ? -n : n;
}

float4 SV_ShadowCoord(float3 posWS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    return ComputeScreenPos(TransformWorldToHClip(posWS));
#else
    return TransformWorldToShadowCoord(posWS);
#endif
}

half3 SV_Ambient(half3 n, half3 ambSky, half3 ambGround)
{
    half3 a = lerp(ambGround, ambSky, saturate(n.y * 0.5 + 0.5));
    return a * (1.0 + _SV_Flash * 2.2);
}

half3 SV_Lit(half3 albedo, half3 n, float3 posWS, half gloss, half3 ambSky, half3 ambGround)
{
    Light L = GetMainLight(SV_ShadowCoord(posWS));
    half atten = L.shadowAttenuation * L.distanceAttenuation;
    half ndl = saturate(dot(n, L.direction));
    half3 c = albedo * (L.color * (ndl * atten) + SV_Ambient(n, ambSky, ambGround));
    float3 v = normalize(_WorldSpaceCameraPos - posWS);
    float3 h = normalize(L.direction + v);
    half spec = pow(saturate(dot(n, h)), exp2(2.0 + gloss * 8.0)) * gloss * gloss * 1.4;
    c += L.color * (spec * atten);
    return c;
}

#endif
