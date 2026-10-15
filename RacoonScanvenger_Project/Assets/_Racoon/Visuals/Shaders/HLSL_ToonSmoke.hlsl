// Humo cartoon (nodos Custom Function de SG_VFX_ToonSmoke), al estilo del humo dibujado: nubes de
// burbujas redondas con tonos planos (claro hacia la luz, oscuro detrás y abajo), sin contorno, que al
// final se rompen en bolitas cada vez más pequeñas hasta desaparecer.
//
// Malla de cada partícula (la genera FootstepVFX): varias burbujas (esferas) solapadas.
//   UV0.xy de la malla = (orden de la burbuja 0-1, radio de la burbuja con tamaño 1)
// Datos por partícula (Custom Vertex Streams del ParticleSystemRenderer, los configura FootstepVFX):
//   UV0 = (orden, radio, center.x, center.y)
//   UV1 = (center.z, agePercent, stableRandom.x, size)
// Fuera de un sistema de partículas (preview del graph, una malla normal) no hay size: toda la malla
// se trata como una sola burbuja centrada en el objeto.
//
// Modo Wisp (material con Wisp = 1): para las Trails del sistema de partículas (colas en punta).
// No deforma nada y pinta el color claro plano.

#ifndef RACOON_TOON_SMOKE_INCLUDED
#define RACOON_TOON_SMOKE_INCLUDED

struct ToonSmokeParticle
{
    float3 center;
    float size;
    float age;
    float seed;
    float isParticle;
};

ToonSmokeParticle ToonSmoke_Particle(float4 uv0, float4 uv1)
{
    ToonSmokeParticle p;
    p.isParticle = uv1.w > 0.0 ? 1.0 : 0.0;
    p.center = uv1.w > 0.0 ? float3(uv0.zw, uv1.x) : TransformObjectToWorld(float3(0, 0, 0));
    p.size = uv1.w > 0.0 ? uv1.w : 1.0;
    p.age = uv1.w > 0.0 ? uv1.y : 0.0;
    p.seed = uv1.z;
    return p;
}

// Ruido triplanar: sin costuras y sin necesitar UVs de la malla.
float4 ToonSmoke_Noise(UnityTexture2D noiseTex, float3 p, float3 n)
{
    float3 w = pow(abs(n), 4.0);
    w /= max(w.x + w.y + w.z, 1e-4);
    float4 x = SAMPLE_TEXTURE2D_LOD(noiseTex.tex, noiseTex.samplerstate, p.zy, 0);
    float4 y = SAMPLE_TEXTURE2D_LOD(noiseTex.tex, noiseTex.samplerstate, p.xz, 0);
    float4 z = SAMPLE_TEXTURE2D_LOD(noiseTex.tex, noiseTex.samplerstate, p.xy, 0);
    return x * w.x + y * w.y + z * w.z;
}

// Desplazamiento distinto por partícula (semilla) que además sube con la edad: el humo "hierve".
float3 ToonSmoke_Offset(float seed, float age, float billowSpeed)
{
    return float3(seed * 13.7, seed * 7.13 - age * billowSpeed, seed * 3.31);
}

// Ruptura: desde breakUpStart cada burbuja se encoge hasta desaparecer, cada una a su ritmo (la grande,
// la última). Devuelve cuánto queda de su radio (1 → 0) y en k el avance de la ruptura (0 → 1).
float ToonSmoke_BubbleScale(ToonSmokeParticle part, float bubbleOrder, float breakUpStart, out float k)
{
    k = saturate((part.age - breakUpStart) / max(1.0 - breakUpStart, 1e-3));
    float order = saturate(bubbleOrder + (frac(part.seed * 7.31 + bubbleOrder * 3.17) - 0.5) * 0.3);
    return 1.0 - smoothstep(0.0, lerp(0.55, 1.0, order), k);
}

// Punto de una burbuja en la dirección dir, un poco abollado por el ruido (canal R).
float3 ToonSmoke_BubblePoint(UnityTexture2D noiseTex, float3 dir, float3 bubbleCenter, float bubbleRadius,
    ToonSmokeParticle part, float noiseScale, float lumpiness, float3 noiseOffset)
{
    float3 q = (bubbleCenter + dir * bubbleRadius - part.center) / part.size; // Dentro del puff: -0.5..0.5
    float lump = ToonSmoke_Noise(noiseTex, q * noiseScale + noiseOffset, dir).r;
    return bubbleCenter + dir * (bubbleRadius * (1.0 + lumpiness * (lump - 0.5) * 2.0));
}

void ToonSmokeVertex_float(float3 PositionWS, float3 NormalWS, float4 UV0, float4 UV1, UnityTexture2D NoiseTex,
    float NoiseScale, float Lumpiness, float BillowSpeed, float BreakUpStart, float BreakUpSpread, float Wisp,
    out float3 PositionOS, out float3 NormalOS)
{
    // Trails: tal cual.
    if (Wisp > 0.5)
    {
        PositionOS = TransformWorldToObject(PositionWS);
        NormalOS = float3(0, 1, 0);
        return;
    }

    ToonSmokeParticle part = ToonSmoke_Particle(UV0, UV1);

    // Burbuja del vértice: su centro está a "radio" hacia dentro de la superficie.
    float3 dir;
    float3 bubbleCenter;
    float bubbleRadius;
    float bubbleOrder;
    if (part.isParticle > 0.5)
    {
        dir = normalize(NormalWS);
        bubbleRadius = UV0.y * part.size;
        bubbleOrder = UV0.x;
        bubbleCenter = PositionWS - dir * bubbleRadius;
    }
    else
    {
        float3 offset = PositionWS - part.center;
        bubbleRadius = max(length(offset), 1e-4);
        dir = offset / bubbleRadius;
        bubbleOrder = 1.0;
        bubbleCenter = part.center;
    }

    // Ruptura: las burbujas se separan del centro y se encogen a distinto ritmo: el puff se rompe en
    // bolitas sueltas.
    float k;
    bubbleRadius *= ToonSmoke_BubbleScale(part, bubbleOrder, BreakUpStart, k);
    bubbleCenter += (bubbleCenter - part.center) * (BreakUpSpread * k);

    float3 noiseOffset = ToonSmoke_Offset(part.seed, part.age, BillowSpeed);
    float3 p0 = ToonSmoke_BubblePoint(NoiseTex, dir, bubbleCenter, bubbleRadius, part, NoiseScale, Lumpiness, noiseOffset);

    // Normal de la burbuja ya abollada (dos vecinos), para que la luz siga los bultos.
    float3 helper = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 tangent = normalize(cross(helper, dir));
    float3 bitangent = cross(dir, tangent);
    const float e = 0.1;
    float3 p1 = ToonSmoke_BubblePoint(NoiseTex, normalize(dir + tangent * e), bubbleCenter, bubbleRadius, part, NoiseScale, Lumpiness, noiseOffset);
    float3 p2 = ToonSmoke_BubblePoint(NoiseTex, normalize(dir + bitangent * e), bubbleCenter, bubbleRadius, part, NoiseScale, Lumpiness, noiseOffset);
    float3 n = cross(p1 - p0, p2 - p0);
    float3 normalWS = dot(n, n) > 1e-14 ? normalize(n) : dir; // Burbuja ya desaparecida: da igual.
    if (dot(normalWS, dir) < 0.0) normalWS = -normalWS;

    PositionOS = TransformWorldToObject(p0);
    NormalOS = TransformWorldToObjectNormal(normalWS);
}

void ToonSmokeFragment_float(float3 PositionWS, float3 NormalWS, float4 UV0, float4 UV1,
    float4 BaseColor, float4 ColorShaded, float SelfShadingSize, float ShadowEdgeSize,
    float4 ColorShadedExtra, float SelfShadingSizeExtra, float VolumeShading, float BottomShade,
    float CameraLight, float3 CameraLightDirection, float LightContribution,
    float4 RimColor, float RimSize, float RimEdgeSmoothness, float BreakUpStart, float Wisp,
    out float3 Color, out float Alpha)
{
    ToonSmokeParticle part = ToonSmoke_Particle(UV0, UV1);
    float3 N = normalize(NormalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(PositionWS);
    float3 rel = (PositionWS - part.center) / part.size; // Posición dentro del puff (-0.5..0.5).

    // Centro de la burbuja de este píxel (radio actual hacia dentro), relativo al centro del puff.
    float k;
    float bubbleRadius = UV0.y * part.size * ToonSmoke_BubbleScale(part, UV0.x, BreakUpStart, k) * part.isParticle;
    float3 bubbleRel = part.isParticle * (PositionWS - N * bubbleRadius - part.center) / part.size;

    // Luz: la principal de la escena o una fija respecto a la cámara (el mismo dibujo desde cualquier
    // ángulo). CameraLightDirection en espacio de vista: X derecha, Y arriba, Z hacia la cámara.
    float3 mainDir = normalize(float3(0.4, 0.8, -0.3));
    float3 lightColor = float3(1, 1, 1);
#if !defined(SHADERGRAPH_PREVIEW)
    Light mainLight = GetMainLight();
    mainDir = mainLight.direction;
    lightColor = mainLight.color;
#endif
    float3 cameraDir = SafeNormalize(mul(CameraLightDirection, (float3x3)UNITY_MATRIX_V));
    float3 L = SafeNormalize(lerp(mainDir, cameraDir, CameraLight));
    float3 tint = lerp(float3(1, 1, 1), lightColor, LightContribution);

    // Cada burbuja con su media luna de sombra (lambert) + volumen: las burbujas del lado de la luz se
    // aclaran enteras y las de detrás se oscurecen enteras, así la sombra sigue la forma de las burbujas
    // (burbujas claras delante de una masa oscura, como en el humo dibujado).
    float lambert = dot(N, L) * 0.5 + 0.5;
    float shade = lambert + VolumeShading * 2.0 * dot(bubbleRel, L);
    shade -= BottomShade * saturate(-rel.y * 2.0); // Abajo, contra el suelo, más oscuro.

    // Tonos planos con borde nítido (antialias con fwidth): claro, sombra y núcleo de la sombra.
    float aa = fwidth(lambert) + ShadowEdgeSize;
    float lit = smoothstep(SelfShadingSize - aa, SelfShadingSize + aa, shade);
    float core = smoothstep(SelfShadingSizeExtra - aa, SelfShadingSizeExtra + aa, shade);
    // Los tonos de sombra son TINTES del color base: si cambia el color, la sombra le acompaña.
    float3 color = BaseColor.rgb * lerp(ColorShaded.rgb, float3(1, 1, 1), lit);
    color = lerp(BaseColor.rgb * ColorShadedExtra.rgb, color, core);

    // Rim en el lado iluminado (apagado por defecto: alpha del Rim Color = 0).
    float rimSmooth = max(RimEdgeSmoothness * 0.5, 1e-4);
    float rim = smoothstep(RimSize - rimSmooth, RimSize + rimSmooth, 1.0 - saturate(dot(N, V))) * lit;
    color = lerp(color, RimColor.rgb, rim * RimColor.a);

    // Un poco de variación de tono entre puffs (con la semilla: el color de vértice de las partículas de malla
    // no es fiable, llega mal y oscurecía el humo).
    color *= lerp(0.94, 1.04, part.seed);

    // Wisp (trails): el tono claro, plano.
    color = Wisp > 0.5 ? BaseColor.rgb : color;

    Color = color * tint;
    Alpha = 1.0;
}

// Variantes half (si el graph se pone en precisión Half).
void ToonSmokeVertex_half(half3 PositionWS, half3 NormalWS, half4 UV0, half4 UV1, UnityTexture2D NoiseTex,
    half NoiseScale, half Lumpiness, half BillowSpeed, half BreakUpStart, half BreakUpSpread, half Wisp,
    out half3 PositionOS, out half3 NormalOS)
{
    float3 p, n;
    ToonSmokeVertex_float(PositionWS, NormalWS, UV0, UV1, NoiseTex, NoiseScale, Lumpiness, BillowSpeed,
        BreakUpStart, BreakUpSpread, Wisp, p, n);
    PositionOS = p;
    NormalOS = n;
}

void ToonSmokeFragment_half(half3 PositionWS, half3 NormalWS, half4 UV0, half4 UV1,
    half4 BaseColor, half4 ColorShaded, half SelfShadingSize, half ShadowEdgeSize,
    half4 ColorShadedExtra, half SelfShadingSizeExtra, half VolumeShading, half BottomShade,
    half CameraLight, half3 CameraLightDirection, half LightContribution,
    half4 RimColor, half RimSize, half RimEdgeSmoothness, half BreakUpStart, half Wisp,
    out half3 Color, out half Alpha)
{
    float3 c;
    float a;
    ToonSmokeFragment_float(PositionWS, NormalWS, UV0, UV1, BaseColor, ColorShaded, SelfShadingSize,
        ShadowEdgeSize, ColorShadedExtra, SelfShadingSizeExtra, VolumeShading, BottomShade, CameraLight,
        CameraLightDirection, LightContribution, RimColor, RimSize, RimEdgeSmoothness, BreakUpStart, Wisp, c, a);
    Color = c;
    Alpha = a;
}

#endif
