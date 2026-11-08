#ifndef MVFX_COLOR_INCLUDED
#define MVFX_COLOR_INCLUDED

// =============================================================================
// MonedasVFX · Utilidades de color
// Cuerpo negro (fuego, explosiones, metal incandescente), espacios perceptuales
// para cuantizar paletas y conversiones sRGB sin depender de keywords.
// =============================================================================

float MVFX_Luminance(float3 linearRgb)
{
    return dot(linearRgb, float3(0.2126, 0.7152, 0.0722));
}

float3 MVFX_LinearToSRGB(float3 c)
{
    c = max(c, 0.0);
    return lerp(12.92 * c, 1.055 * pow(c, 1.0 / 2.4) - 0.055, step(0.0031308, c));
}

float3 MVFX_SRGBToLinear(float3 c)
{
    c = max(c, 0.0);
    return lerp(c / 12.92, pow((c + 0.055) / 1.055, 2.4), step(0.04045, c));
}

// ------------------------------------------------------------------ Cuerpo negro
// Aproximación analítica del locus de Planck (ajuste de Tanner Helland, en lineal).
// kelvin en [1000, 40000]. Devuelve cromaticidad normalizada (max = 1).
float3 MVFX_BlackbodyChromaticity(float kelvin)
{
    float t = clamp(kelvin, 1000.0, 40000.0) / 100.0;
    float hot = max(t - 60.0, 1e-3);   // evita pow/log de negativos en la rama descartada
    float3 c;
    c.r = t <= 66.0 ? 1.0 : saturate(1.29293618606 * pow(hot, -0.1332047592));
    c.g = t <= 66.0 ? saturate(0.39008157876 * log(t) - 0.63184144378)
                    : saturate(1.12989086089 * pow(hot, -0.0755148492));
    c.b = t >= 66.0 ? 1.0 : (t <= 19.0 ? 0.0 : saturate(0.54320678911 * log(max(t - 10.0, 1e-3)) - 1.19625408914));
    return MVFX_SRGBToLinear(c);
}

// Radiancia relativa ~ T^4 (Stefan-Boltzmann) normalizada a una temperatura de referencia.
// Devuelve color HDR lineal listo para bloom.
float3 MVFX_Blackbody(float kelvin, float referenceKelvin)
{
    float ratio = kelvin / max(referenceKelvin, 1.0);
    float radiance = ratio * ratio * ratio * ratio;
    return MVFX_BlackbodyChromaticity(kelvin) * radiance;
}

// Temperatura normalizada (0 = frío, 1 = núcleo) -> color de llama HDR.
// minKelvin/maxKelvin típicos: 900 / 2600 para madera, 1200 / 3200 para explosión.
float3 MVFX_FlameColor(float heat01, float minKelvin, float maxKelvin, float intensity)
{
    heat01 = saturate(heat01);
    float kelvin = lerp(minKelvin, maxKelvin, heat01);
    // El brillo crece mucho más rápido que el color: es lo que da "núcleo blanco" al fuego real
    return MVFX_BlackbodyChromaticity(kelvin) * (heat01 * heat01 * heat01 * intensity);
}

// ------------------------------------------------------------------ HSV
float3 MVFX_RgbToHsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 MVFX_HsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(1.0, saturate(p - 1.0), c.y);
}

// ------------------------------------------------------------------ Oklab (distancias perceptuales para paletas)
float3 MVFX_LinearToOklab(float3 c)
{
    float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
    float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
    float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
    l = pow(max(l, 0.0), 1.0 / 3.0);
    m = pow(max(m, 0.0), 1.0 / 3.0);
    s = pow(max(s, 0.0), 1.0 / 3.0);
    return float3(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                  1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                  0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
}

float3 MVFX_Saturation(float3 c, float saturation)
{
    float l = MVFX_Luminance(c);
    return lerp(l.xxx, c, saturation);
}

#endif
