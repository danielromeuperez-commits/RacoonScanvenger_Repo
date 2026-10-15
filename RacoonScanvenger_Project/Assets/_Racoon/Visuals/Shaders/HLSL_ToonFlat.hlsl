// Efectos planos estilo Persona 5 / cartoon (nodo Custom Function de SG_VFX_ToonFlat y SG_VFX_ToonFlatOverlay).
//
// Las mallas las genera FlatMeshes (C#) y llevan en las UV lo que necesita el shader:
//   UV0.x = recorrido de la pieza (0 base / centro → 1 punta). Lo usa el "revelado".
//   UV0.y = aleatorio por pieza (0-1): cada rayo / esquirla va a su ritmo (Spread).
//   UV1.x = 1 en el contorno (la cáscara de detrás), 0 en el relleno.
//   UV1.y = degradado de color (0 = BaseColor → 1 = TipColor).
//
// Revelado: se ve la parte de cada pieza entre Shrink y Grow (en unidades de UV0.x). Animando Grow de 0 a 1
// la pieza sale disparada desde la base; animando después Shrink de 0 a 1 se recoge hacia la punta. Con
// Shrink pegado a Grow queda un trozo corto que viaja (chispas).
//
// Cel (CelAmount = 1): sombreado de dos tonos con luz fija respecto a la cámara (como el humo), para los
// objetos 3D (estrellas, gotas). Los gráficos planos usan CelAmount = 0.

#ifndef RACOON_TOON_FLAT_INCLUDED
#define RACOON_TOON_FLAT_INCLUDED

void ToonFlat_float(float3 NormalWS, float4 UV0, float4 UV1,
    float4 BaseColor, float4 TipColor, float4 OutlineColor, float4 ShadowColor,
    float CelAmount, float CelThreshold, float3 CameraLightDirection,
    float Grow, float Shrink, float Spread,
    out float3 Color, out float Alpha)
{
    // Revelado (recorte duro: los bordes del corte son nítidos, como un dibujo).
    float along = UV0.x;
    float speed = 1.0 + (UV0.y * 2.0 - 1.0) * Spread;
    clip(min(Grow * speed - along, along - Shrink * speed));

    float3 color = lerp(BaseColor.rgb, TipColor.rgb, saturate(UV1.y));

    // Cel de dos tonos con la luz de cámara (X derecha, Y arriba, Z hacia la cámara).
    float3 L = SafeNormalize(mul(CameraLightDirection, (float3x3)UNITY_MATRIX_V));
    float ndl = dot(SafeNormalize(NormalWS), L) * 0.5 + 0.5;
    float aa = max(fwidth(ndl), 1e-4);
    float lit = smoothstep(CelThreshold - aa, CelThreshold + aa, ndl);
    color *= lerp(float3(1, 1, 1), lerp(ShadowColor.rgb, float3(1, 1, 1), lit), CelAmount);

    Color = UV1.x > 0.5 ? OutlineColor.rgb : color;
    Alpha = 1.0;
}

void ToonFlat_half(half3 NormalWS, half4 UV0, half4 UV1,
    half4 BaseColor, half4 TipColor, half4 OutlineColor, half4 ShadowColor,
    half CelAmount, half CelThreshold, half3 CameraLightDirection,
    half Grow, half Shrink, half Spread,
    out half3 Color, out half Alpha)
{
    float3 c;
    float a;
    ToonFlat_float(NormalWS, UV0, UV1, BaseColor, TipColor, OutlineColor, ShadowColor, CelAmount, CelThreshold,
        CameraLightDirection, Grow, Shrink, Spread, c, a);
    Color = c;
    Alpha = a;
}

#endif
