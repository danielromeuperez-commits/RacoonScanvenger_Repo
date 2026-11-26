#ifndef MVFX_SG_PARTICLES_INCLUDED
#define MVFX_SG_PARTICLES_INCLUDED

// =============================================================================
// MonedasVFX · Funciones de Custom Function para Shader Graphs de partículas
//
// Convención de vertex streams (Shuriken, Custom Vertex Streams):
//   TEXCOORD0 = UV.xy + UV2.xy          -> nodo UV0 (frame actual y siguiente)
//   TEXCOORD1 = AnimBlend, Custom1.xyz  -> nodo UV1 (x mezcla de frames, y calor, z semilla, w densidad)
//   COLOR     = color sobre la vida
// =============================================================================

#include "Assets/_Racoon/Visuals/VFX/Fireworks/HLSL_FireworksColor.hlsl"
#include "Assets/_Racoon/Visuals/VFX/Fireworks/HLSL_FireworksNoise.hlsl"

// Partículas suaves (intersección con la geometría) + desvanecido junto a la cámara.
// ScreenPosRaw: nodo Screen Position en modo Raw (w = profundidad de ojo de la partícula).
float MVFX_ParticleFade(float4 screenPosRaw, float sceneRawDepth, float softDistance, float nearFade)
{
    float particleDepth = screenPosRaw.w;
    float fade = 1.0;
#if !defined(SHADERGRAPH_PREVIEW)
    if (softDistance > 0.0)
    {
        float sceneDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
        fade = saturate((sceneDepth - particleDepth) / softDistance);
    }
#endif
    float cameraFade = nearFade > 0.0 ? saturate((particleDepth - _ProjectionParams.y) / nearFade) : 1.0;
    return fade * cameraFade;
}

float4 MVFX_SampleFlipbook(UnityTexture2D map, float4 uv0, float blend)
{
    float4 a = SAMPLE_TEXTURE2D(map.tex, map.samplerstate, uv0.xy);
    float4 b = SAMPLE_TEXTURE2D(map.tex, map.samplerstate, uv0.zw);
    return lerp(a, b, saturate(blend));
}

// ------------------------------------------------------------------ Six-way (humo y bola de fuego)
// PositiveMap: R derecha, G arriba, B detrás, A transparencia (convención de Unity/EmberGen)
// NegativeMap: R izquierda, G abajo, B delante, A máscara de emisión (temperatura horneada)
void MVFX_SixWay_float(UnityTexture2D PositiveMap, UnityTexture2D NegativeMap, float4 UV0, float4 UV1,
    float4 VertexColor, float4 Tint, float Density, float EmissionIntensity, float2 Temperature,
    float SoftDistance, float NearFade, float4 ScreenPosRaw, float SceneRawDepth,
    out float3 BaseColor, out float3 RightTopBack, out float3 LeftBottomFront, out float3 Emission, out float Alpha)
{
    float4 positive = MVFX_SampleFlipbook(PositiveMap, UV0, UV1.x);
    float4 negative = MVFX_SampleFlipbook(NegativeMap, UV0, UV1.x);
    float fade = MVFX_ParticleFade(ScreenPosRaw, SceneRawDepth, SoftDistance, NearFade);

    // UV1.w = multiplicador de densidad por partícula (Custom Data). 0 si el stream no existe -> 1.
    float densityScale = UV1.w > 0.0 ? UV1.w : 1.0;
    Alpha = saturate(positive.a * Density * densityScale * VertexColor.a * Tint.a * fade);

    RightTopBack = positive.rgb;
    LeftBottomFront = negative.rgb;
    BaseColor = Tint.rgb * VertexColor.rgb;

    // Calor por partícula (UV1.y) x densidad: toda la bola arde al principio; la máscara
    // horneada (núcleo) lleva las zonas más densas al blanco del cuerpo negro.
    float heat = saturate(UV1.y * positive.a * (0.55 + 1.1 * sqrt(negative.a)));
    heat = heat * heat * (3.0 - 2.0 * heat);
    Emission = MVFX_FlameColor(heat, Temperature.x, Temperature.y, EmissionIntensity) * fade;
}

// ------------------------------------------------------------------ Llama (flipbook horneado)
// FlameMap: R calor (emisión), G opacidad del hollín, B núcleo (temperatura máxima en el rayo)
// Salida para mezcla premultiplicada: Color = emisión (aditiva), Alpha = hollín (oclusión).
void MVFX_Fire_float(UnityTexture2D FlameMap, float4 UV0, float4 UV1, float4 VertexColor, float Intensity,
    float2 Temperature, float SootOpacity, float Distortion, float Time, float SoftDistance, float NearFade,
    float4 ScreenPosRaw, float SceneRawDepth, out float3 Color, out float Alpha)
{
    // Pequeña distorsión animada: cada partícula (semilla en UV1.z) "respira" distinto
    float seed = UV1.z * 17.0;
    float2 warp = float2(MVFX_GradientNoise2D(UV0.xy * 6.0 + float2(seed, Time * 1.7)),
                         MVFX_GradientNoise2D(UV0.xy * 6.0 + float2(Time * 1.3, seed))) * Distortion;
    float4 uv = UV0 + float4(warp, warp) * float4(1.0, 0.5, 1.0, 0.5);

    float4 flame = MVFX_SampleFlipbook(FlameMap, uv, UV1.x);
    float fade = MVFX_ParticleFade(ScreenPosRaw, SceneRawDepth, SoftDistance, NearFade);

    float heatScale = UV1.y > 0.0 ? UV1.y : 1.0;
    float heat = saturate(flame.r * heatScale * VertexColor.a);
    float core = saturate(flame.b * heatScale);
    float3 emission = MVFX_FlameColor(saturate(heat * 0.85 + core * 0.25), Temperature.x, Temperature.y, Intensity);

    Color = emission * VertexColor.rgb * fade;
    Alpha = saturate(flame.g * SootOpacity * VertexColor.a * fade);
}

// ------------------------------------------------------------------ Chispas, brasas y destellos (aditivo)
void MVFX_GlowSprite_float(float4 UV0, float4 VertexColor, float Intensity, float CoreSharpness, float Hotness,
    out float3 Color, out float Alpha)
{
    float2 p = UV0.xy * 2.0 - 1.0;
    float r = length(p);
    float halo = saturate(1.0 - r);
    halo = halo * halo;
    float core = pow(saturate(1.0 - r), CoreSharpness);
    // El centro tiende a blanco (temperatura), el halo conserva el color de la partícula
    float3 color = lerp(VertexColor.rgb, float3(1.0, 0.95, 0.85), core * Hotness);
    Color = color * (halo * 0.6 + core) * Intensity * VertexColor.a;
    Alpha = saturate(halo * VertexColor.a);
}

// ------------------------------------------------------------------ Distorsión (calor y onda expansiva)
// Mode 0: aire caliente (ruido ascendente). Mode 1: anillo de onda expansiva (UV radial).
// SceneColorHint: nodo Scene Color conectado para que Shader Graph declare la opaque texture.
void MVFX_Distortion_float(float4 UV0, float4 VertexColor, float4 ScreenPos, float3 SceneColorHint, float Strength,
    float NoiseScale, float Speed, float Mode, float Time, out float3 Color, out float Alpha)
{
    float2 uv = UV0.xy;
    float2 offset;
    float mask;
    if (Mode < 0.5)
    {
        float2 p = uv * NoiseScale + float2(0.0, -Time * Speed);
        offset = float2(MVFX_GradientNoise2D(p), MVFX_GradientNoise2D(p + 23.1));
        float2 c = uv * 2.0 - 1.0;
        mask = saturate(1.0 - dot(c, c));
    }
    else
    {
        float2 c = uv * 2.0 - 1.0;
        float r = length(c);
        // Anillo fino que se adelgaza al expandirse (la vida va en el alpha del color)
        float ring = saturate(1.0 - abs(r - 0.82) / 0.16);
        ring = ring * ring;
        offset = c / max(r, 1e-3) * ring;
        mask = ring;
    }

    float2 screenUV = ScreenPos.xy;
    float2 distortedUV = screenUV + offset * Strength * VertexColor.a * mask;
#if defined(SHADERGRAPH_PREVIEW)
    Color = SceneColorHint + float3(offset * 0.5 + 0.5, 0.5);
#else
    Color = SHADERGRAPH_SAMPLE_SCENE_COLOR(distortedUV);
#endif
    Alpha = saturate(mask * VertexColor.a * 4.0);
}

#endif
