// Efectos 3D con volumen, estilo toon / FlatKit (nodos Custom Function de SG_VFX_ToonVolume): onda de choque,
// cristales, gotas de sudor...
//
// Las mallas las genera FlatMeshes (C#) y llevan en las UV lo que necesita el shader:
//   UV0.x = recorrido de la pieza (0 base → 1 punta; en los anillos, la vuelta). Lo usa el "revelado".
//   UV0.y = aleatorio por pieza (0-1): cada cristal va a su ritmo (Spread).
//   UV1.x = 1 en la cáscara de contorno (inverted hull), 0 en el volumen.
//   UV1.y = degradado de color (0 = BaseColor → 1 = TipColor).
//   UV2.x = radio del tubo (anillos): Thickness lo adelgaza hacia su eje sin mover el anillo.
//
// Revelado (igual que ToonFlat): se ve la parte de cada pieza entre Shrink y Grow (en unidades de UV0.x).
//
// Volumen, con una luz fija respecto a la cámara (como el humo: se lee igual en todo el mapa):
//   - Luz y sombra en dos tonos con el borde nítido (ShadowColor multiplica el color en la sombra).
//   - Brillo especular toon: una mancha nítida donde la superficie mira a la luz → se lee redondo.
//   - Luz de borde (rim) en la silueta, más fuerte en el lado iluminado → separa la pieza del fondo.
//   - RimCut > 0: recorta el centro de la pieza vista de frente (fresnel) y deja solo la silueta: burbuja de
//     choque hueca que se va vaciando.

#ifndef RACOON_TOON_VOLUME_INCLUDED
#define RACOON_TOON_VOLUME_INCLUDED

void ToonVolumeVertex_float(float3 PositionOS, float3 NormalOS, float4 UV2, float Thickness, out float3 Position)
{
    // Tubo: el centro del tubo está en PositionOS - NormalOS * radio; se acerca a él (la cáscara de contorno
    // guarda el mismo radio, así mantiene su grosor).
    Position = PositionOS - NormalOS * (UV2.x * (1.0 - saturate(Thickness)));
}

void ToonVolumeFragment_float(float3 PositionWS, float3 NormalWS, float4 UV0, float4 UV1,
    float4 BaseColor, float4 TipColor, float4 OutlineColor, float4 ShadowColor,
    float CelAmount, float CelThreshold, float3 CameraLightDirection,
    float4 HighlightColor, float HighlightSize, float4 RimColor, float RimSize, float RimCut,
    float Grow, float Shrink, float Spread,
    out float3 Color, out float Alpha)
{
    // Revelado (recorte duro).
    float along = UV0.x;
    float speed = 1.0 + (UV0.y * 2.0 - 1.0) * Spread;
    clip(min(Grow * speed - along, along - Shrink * speed));

    float3 N = SafeNormalize(NormalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(PositionWS);
    // Las caras de dentro (piezas huecas) llevan la normal al revés: abs para que la silueta valga igual.
    float facing = saturate(abs(dot(N, V)));
    float fresnel = 1.0 - facing;

    // Burbuja hueca: solo la silueta.
    if (RimCut > 0.0) clip(fresnel - RimCut);

    if (UV1.x > 0.5)
    {
        Color = OutlineColor.rgb;
        Alpha = 1.0;
        return;
    }

    float3 color = lerp(BaseColor.rgb, TipColor.rgb, saturate(UV1.y));

    // Luz de cámara (X derecha, Y arriba, Z hacia la cámara).
    float3 L = SafeNormalize(mul(CameraLightDirection, (float3x3)UNITY_MATRIX_V));
    float ndl = dot(N, L) * 0.5 + 0.5;
    float aa = max(fwidth(ndl), 1e-4);
    float lit = smoothstep(CelThreshold - aa, CelThreshold + aa, ndl);
    color *= lerp(float3(1, 1, 1), lerp(ShadowColor.rgb, float3(1, 1, 1), lit), CelAmount);

    // Brillo especular toon (Blinn con la luz de cámara), solo en la parte iluminada.
    float nh = dot(N, SafeNormalize(L + V));
    float edge = 1.0 - HighlightSize * HighlightSize;
    float aaH = max(fwidth(nh), 1e-4);
    float highlight = smoothstep(edge - aaH, edge + aaH, nh) * lit * CelAmount;
    color = lerp(color, HighlightColor.rgb, highlight * HighlightColor.a);

    // Luz de borde en la silueta (más fuerte del lado de la luz).
    float rimEdge = 1.0 - RimSize;
    float aaR = max(fwidth(fresnel), 1e-4);
    float rim = smoothstep(rimEdge - aaR, rimEdge + aaR, fresnel) * lerp(0.35, 1.0, lit);
    color = lerp(color, RimColor.rgb, rim * RimColor.a);

    Color = color;
    Alpha = 1.0;
}

void ToonVolumeVertex_half(half3 PositionOS, half3 NormalOS, half4 UV2, half Thickness, out half3 Position)
{
    float3 p;
    ToonVolumeVertex_float(PositionOS, NormalOS, UV2, Thickness, p);
    Position = p;
}

void ToonVolumeFragment_half(half3 PositionWS, half3 NormalWS, half4 UV0, half4 UV1,
    half4 BaseColor, half4 TipColor, half4 OutlineColor, half4 ShadowColor,
    half CelAmount, half CelThreshold, half3 CameraLightDirection,
    half4 HighlightColor, half HighlightSize, half4 RimColor, half RimSize, half RimCut,
    half Grow, half Shrink, half Spread,
    out half3 Color, out half Alpha)
{
    float3 c;
    float a;
    ToonVolumeFragment_float(PositionWS, NormalWS, UV0, UV1, BaseColor, TipColor, OutlineColor, ShadowColor,
        CelAmount, CelThreshold, CameraLightDirection, HighlightColor, HighlightSize, RimColor, RimSize, RimCut,
        Grow, Shrink, Spread, c, a);
    Color = c;
    Alpha = a;
}

#endif
