// Capa de energía sobre la malla del personaje con la bebida energética (la ven todos los jugadores). Se dibuja en
// una copia de la malla, un pelo hinchada: borde de luz (fresnel), ruido que sube por el cuerpo y arcos finos, todo
// en bandas planas de azul verdoso, a juego con el cel-shading. _Intensity lleva la entrada, el parpadeo y la salida.
// Está en Resources para que entre en la build (PlayerEnergyOverlay lo carga con Resources.Load).
Shader "Racoon/CameraFXEnergyOverlay"
{
    Properties
    {
        _Deep ("Tono profundo", Color) = (0.1, 0.5, 0.6, 1)
        _Mid ("Tono medio", Color) = (0.2, 0.78, 0.72, 1)
        _Light ("Tono de luz", Color) = (0.78, 1, 0.94, 1)
        _Intensity ("Intensidad", Range(0, 1)) = 1
        _Push ("Hinchado sobre la malla (m)", Float) = 0.006
        _Scale ("Tamaño del ruido", Float) = 5
        _Speed ("Velocidad de subida", Float) = 1.4
        _Origin ("Posición del jugador (la pone el script)", Vector) = (0, 0, 0, 0)
        _FXTime ("Tiempo (lo pone el script)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "EnergyOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Mid, _Light;
                float _Intensity, _Push, _Scale, _Speed, _FXTime;
                float4 _Origin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz) + normalWS * _Push;
                o.positionWS = positionWS;
                o.normalWS = normalWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x),
                         lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x),
                         lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fresnel = 1.0 - saturate(dot(n, v));

                // Relativo al jugador: el dibujo no resbala por el cuerpo al moverse.
                float3 p = (i.positionWS - _Origin.xyz) * _Scale + float3(0, -_FXTime * _Speed, 0);
                float flow = noise3(p) * 0.7 + noise3(p * 2.1 + 7.0) * 0.3;
                float arc = 1.0 - abs(2.0 * flow - 1.0);

                // Bandas planas: velo profundo, medio y luz, y arcos finos de luz.
                float e = fresnel * 1.1 + (flow - 0.5) * 0.8;
                half3 color = _Deep.rgb;
                float alpha = 0.22;
                if (e > 0.38) { color = _Mid.rgb; alpha = 0.5; }
                if (e > 0.68) { color = _Light.rgb; alpha = 0.85; }
                if (arc > 0.94) { color = _Light.rgb; alpha = 0.9; }
                return half4(color, alpha * _Intensity);
            }
            ENDHLSL
        }
    }
}
