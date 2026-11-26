// RACOON: «pompa de visión» (la usan FlatKit Stylized Surface y SH_Racoon_LitPompa). Lo que tapa a un jugador (entre la cámara y él)
// se recorta en un círculo a su alrededor con un tramado suave, para verle siempre. Los datos los pone cada frame
// Racoon.VFX.OcclusionBubbles (C#). No toca el suelo (solo por encima de los pies) ni las sombras.
#ifndef RACOON_BUBBLE_INCLUDED
#define RACOON_BUBBLE_INCLUDED

#define RACOON_MAX_BUBBLES 4
// Bit de rendering layer con el que Racoon.VFX.OcclusionBubbles marca los objetos de las capas elegidas
// (OcclusionBubbleSettings). Lo que no lo lleva no se recorta nunca (los jugadores, por ejemplo).
#define RACOON_BUBBLE_LAYER (1u << 30)

float4 _RacoonBubbles[RACOON_MAX_BUBBLES];   // xyz = centro del jugador (pecho), w = altura de sus pies
float4 _RacoonBubbleParams;                  // x = nº de pompas, y = radio (m), z = borde suave (m), w = margen delante (m)

// Tramado 4x4 (Bayer): un recorte «transparente» sin ordenar ni mezclar.
float RacoonBayer(float2 pixel)
{
    uint2 p = uint2(pixel) & 3u;
    static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

void RacoonBubbleClip(float3 positionWS, float4 positionCS)
{
    int count = (int)_RacoonBubbleParams.x;
    if (count <= 0) return;
    if ((GetMeshRenderingLayer() & RACOON_BUBBLE_LAYER) == 0) return;

    float3 cam = GetCameraPositionWS();
    float3 toFrag = positionWS - cam;
    float fragDist = length(toFrag);
    float3 dir = toFrag / max(fragDist, 1e-4);
    float radius = _RacoonBubbleParams.y;
    float edge = max(_RacoonBubbleParams.z, 1e-3);

    float cut = 0.0;
    [unroll]
    for (int i = 0; i < RACOON_MAX_BUBBLES; i++)
    {
        if (i >= count) break;
        float3 p = _RacoonBubbles[i].xyz;
        float3 toPlayer = p - cam;
        float playerDist = dot(toPlayer, dir);
        // Solo lo que está delante del jugador (más cerca de la cámara) y por encima de sus pies.
        if (fragDist > playerDist - _RacoonBubbleParams.w || positionWS.y < _RacoonBubbles[i].w + 0.08) continue;
        // Distancia del jugador al rayo de este píxel = círculo alrededor de él en pantalla.
        float away = length(toPlayer - dir * playerDist);
        cut = max(cut, saturate((radius - away) / edge));
    }

    if (cut > 0.0)
        clip(RacoonBayer(positionCS.xy) - cut);
}

// Para los pases que no tienen la posición en el mundo (profundidad, contorno): se reconstruye del píxel.
void RacoonBubbleClipCS(float4 positionCS)
{
    if ((int)_RacoonBubbleParams.x <= 0) return;
    if ((GetMeshRenderingLayer() & RACOON_BUBBLE_LAYER) == 0) return;
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float3 positionWS = ComputeWorldSpacePosition(uv, positionCS.z, UNITY_MATRIX_I_VP);
    RacoonBubbleClip(positionWS, positionCS);
}

#endif
