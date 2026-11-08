#ifndef MVFX_NOISE_INCLUDED
#define MVFX_NOISE_INCLUDED

// =============================================================================
// MonedasVFX · Librería de ruido
// Hashes sin seno (estables en GPU), value/gradient noise 2D-3D, fBm, Worley,
// curl noise, IGN y matrices de Bayer. Todo determinista y sin texturas.
// =============================================================================

// ------------------------------------------------------------------ Hashes (Dave Hoskins, "Hash without Sine")
float MVFX_Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float MVFX_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float MVFX_Hash13(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

float2 MVFX_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float3 MVFX_Hash33(float3 p3)
{
    p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

// ------------------------------------------------------------------ Value noise
float MVFX_ValueNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = MVFX_Hash12(i);
    float b = MVFX_Hash12(i + float2(1, 0));
    float c = MVFX_Hash12(i + float2(0, 1));
    float d = MVFX_Hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float MVFX_ValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = MVFX_Hash13(i);
    float n100 = MVFX_Hash13(i + float3(1, 0, 0));
    float n010 = MVFX_Hash13(i + float3(0, 1, 0));
    float n110 = MVFX_Hash13(i + float3(1, 1, 0));
    float n001 = MVFX_Hash13(i + float3(0, 0, 1));
    float n101 = MVFX_Hash13(i + float3(1, 0, 1));
    float n011 = MVFX_Hash13(i + float3(0, 1, 1));
    float n111 = MVFX_Hash13(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// ------------------------------------------------------------------ Gradient noise (Perlin) con interpolación quíntica. Rango aprox. [-1, 1]
float3 MVFX_GradientDir3(float3 cell)
{
    return normalize(MVFX_Hash33(cell) * 2.0 - 1.0 + 1e-4);
}

float MVFX_GradientNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float n000 = dot(MVFX_GradientDir3(i + float3(0, 0, 0)), f - float3(0, 0, 0));
    float n100 = dot(MVFX_GradientDir3(i + float3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(MVFX_GradientDir3(i + float3(0, 1, 0)), f - float3(0, 1, 0));
    float n110 = dot(MVFX_GradientDir3(i + float3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(MVFX_GradientDir3(i + float3(0, 0, 1)), f - float3(0, 0, 1));
    float n101 = dot(MVFX_GradientDir3(i + float3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(MVFX_GradientDir3(i + float3(0, 1, 1)), f - float3(0, 1, 1));
    float n111 = dot(MVFX_GradientDir3(i + float3(1, 1, 1)), f - float3(1, 1, 1));

    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z) * 1.4;
}

float MVFX_GradientNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 g00 = normalize(MVFX_Hash22(i) * 2.0 - 1.0 + 1e-4);
    float2 g10 = normalize(MVFX_Hash22(i + float2(1, 0)) * 2.0 - 1.0 + 1e-4);
    float2 g01 = normalize(MVFX_Hash22(i + float2(0, 1)) * 2.0 - 1.0 + 1e-4);
    float2 g11 = normalize(MVFX_Hash22(i + float2(1, 1)) * 2.0 - 1.0 + 1e-4);
    float n00 = dot(g00, f);
    float n10 = dot(g10, f - float2(1, 0));
    float n01 = dot(g01, f - float2(0, 1));
    float n11 = dot(g11, f - float2(1, 1));
    return lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y) * 1.4;
}

// ------------------------------------------------------------------ fBm
// Rotación entre octavas para romper la alineación con la rejilla.
static const float3x3 MVFX_OCTAVE_ROT = float3x3(0.00, 0.80, 0.60,
                                                -0.80, 0.36, -0.48,
                                                -0.60, -0.48, 0.64);

float MVFX_Fbm3D(float3 p, int octaves, float lacunarity, float gain)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float norm = 0.0;
    for (int i = 0; i < octaves; i++)
    {
        sum += MVFX_GradientNoise3D(p) * amplitude;
        norm += amplitude;
        p = mul(MVFX_OCTAVE_ROT, p) * lacunarity;
        amplitude *= gain;
    }
    return sum / max(norm, 1e-4); // ~[-1, 1]
}

float MVFX_Fbm2D(float2 p, int octaves, float lacunarity, float gain)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float norm = 0.0;
    const float2x2 rot = float2x2(0.8, -0.6, 0.6, 0.8);
    for (int i = 0; i < octaves; i++)
    {
        sum += MVFX_GradientNoise2D(p) * amplitude;
        norm += amplitude;
        p = mul(rot, p) * lacunarity;
        amplitude *= gain;
    }
    return sum / max(norm, 1e-4);
}

// Ridged: crestas afiladas (venas de roca, grietas, rayos)
float MVFX_RidgedFbm3D(float3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float norm = 0.0;
    for (int i = 0; i < octaves; i++)
    {
        float n = 1.0 - abs(MVFX_GradientNoise3D(p));
        sum += n * n * amplitude;
        norm += amplitude;
        p = mul(MVFX_OCTAVE_ROT, p) * 2.03;
        amplitude *= 0.5;
    }
    return sum / max(norm, 1e-4); // [0, 1]
}

// ------------------------------------------------------------------ Worley / Voronoi
// Devuelve (F1, F2) en 2D.
float2 MVFX_Worley2D(float2 p, float jitter)
{
    float2 cell = floor(p);
    float2 local = frac(p);
    float f1 = 8.0;
    float f2 = 8.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y);
            float2 feature = offset + MVFX_Hash22(cell + offset) * jitter - local;
            float d = dot(feature, feature);
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) { f2 = d; }
        }
    }
    return sqrt(float2(f1, f2));
}

float2 MVFX_Worley3D(float3 p, float jitter)
{
    float3 cell = floor(p);
    float3 local = frac(p);
    float f1 = 8.0;
    float f2 = 8.0;
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float3 offset = float3(x, y, z);
        float3 feature = offset + MVFX_Hash33(cell + offset) * jitter - local;
        float d = dot(feature, feature);
        if (d < f1) { f2 = f1; f1 = d; }
        else if (d < f2) { f2 = d; }
    }
    return sqrt(float2(f1, f2));
}

// ------------------------------------------------------------------ Curl noise (campo sin divergencia: humo, chispas, pétalos)
float3 MVFX_CurlNoise3D(float3 p)
{
    const float e = 0.1;
    float3 dx = float3(e, 0, 0);
    float3 dy = float3(0, e, 0);
    float3 dz = float3(0, 0, e);

    // Tres campos escalares desfasados = potencial vectorial
    float3 o1 = float3(0, 0, 0);
    float3 o2 = float3(31.416, 12.73, 7.11);
    float3 o3 = float3(-17.9, 43.1, 91.3);

    float p1y = MVFX_GradientNoise3D(p + o1 + dy) - MVFX_GradientNoise3D(p + o1 - dy);
    float p1z = MVFX_GradientNoise3D(p + o1 + dz) - MVFX_GradientNoise3D(p + o1 - dz);
    float p2x = MVFX_GradientNoise3D(p + o2 + dx) - MVFX_GradientNoise3D(p + o2 - dx);
    float p2z = MVFX_GradientNoise3D(p + o2 + dz) - MVFX_GradientNoise3D(p + o2 - dz);
    float p3x = MVFX_GradientNoise3D(p + o3 + dx) - MVFX_GradientNoise3D(p + o3 - dx);
    float p3y = MVFX_GradientNoise3D(p + o3 + dy) - MVFX_GradientNoise3D(p + o3 - dy);

    return float3(p3y - p2z, p1z - p3x, p2x - p1y) / (2.0 * e);
}

// ------------------------------------------------------------------ Ruido de pantalla
// Interleaved Gradient Noise (Jimenez 2014): ideal para jitter de raymarching.
float MVFX_IGN(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float MVFX_IGNAnimated(float2 pixel, uint frame)
{
    pixel += 5.588238 * (float)(frame % 64u);
    return MVFX_IGN(pixel);
}

// Bayer ordenado. Índices enteros con la recursión clásica
// M(2n)(x, y) = 4 * M(n)(x mod n, y mod n) + M(2)(x div n, y div n)
uint MVFX_BayerIndex2(uint2 p)
{
    p &= 1u;
    return (p.x ^ p.y) * 2u + p.y;
}

uint MVFX_BayerIndex4(uint2 p)
{
    return 4u * MVFX_BayerIndex2(p & 1u) + MVFX_BayerIndex2(p >> 1u);
}

uint MVFX_BayerIndex8(uint2 p)
{
    return 4u * MVFX_BayerIndex4(p & 3u) + MVFX_BayerIndex2(p >> 2u);
}

// Umbrales normalizados en [0, 1)
float MVFX_Bayer2(uint2 p) { return MVFX_BayerIndex2(p) / 4.0; }
float MVFX_Bayer4(uint2 p) { return MVFX_BayerIndex4(p) / 16.0; }
float MVFX_Bayer8(uint2 p) { return MVFX_BayerIndex8(p) / 64.0; }

#endif
