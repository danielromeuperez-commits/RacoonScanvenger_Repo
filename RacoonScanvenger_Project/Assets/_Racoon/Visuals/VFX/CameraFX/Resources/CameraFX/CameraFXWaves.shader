// UI: viñeteado cartoon por capas para el aura de energía de CameraFX. Varias bandas planas y translúcidas que
// entran desde los bordes; el borde de cada una ondula como una forma de onda de audio (senos a lo largo del
// perímetro modulados por ruido) y el latido viaja de fuera hacia dentro, capa a capa.
// Grosor por zonas, como el resto de CameraFX: más en las esquinas inferiores, después el borde inferior y los
// laterales (parte media-baja) y casi nada arriba. Color y alfa: los del RawImage (canal aura).
// Procedural (sin textura), así que no pierde resolución. Está en Resources para que entre en la build.
Shader "Racoon/UI/CameraFXWaves"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sin uso", 2D) = "white" {}
        _Cream ("Luz de las capas internas", Color) = (1, 0.973, 0.925, 1)
        _Ink ("Línea del borde", Color) = (0.294, 0.263, 0.4, 1)
        _Layers ("Capas", Range(1, 6)) = 4
        _Thickness ("Grosor (fracción de la altura)", Range(0.02, 0.5)) = 0.13
        _Amplitude ("Amplitud de la onda", Range(0, 1)) = 0.35
        _Speed ("Velocidad de la onda", Float) = 1.2
        _LayerAlpha ("Alfa por capa", Range(0, 1)) = 0.15
        _InkAlpha ("Alfa de la línea", Range(0, 1)) = 0.3
        _FXTime ("Tiempo (lo pone CameraFX, sin escalar)", Float) = 0
        _Aspect ("Ancho / alto (lo pone CameraFX)", Float) = 1.7778
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Cream, _Ink;
            float _Layers, _Thickness, _Amplitude, _Speed, _LayerAlpha, _InkAlpha, _FXTime, _Aspect;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            // Ruido de valor 1D, periódico con periodo «period» (para que empalme al dar la vuelta al perímetro).
            float noise1(float x, float period)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(hash(fmod(i, period)), hash(fmod(i + 1, period)), f);
            }

            // Forma de onda a lo largo del perímetro (p en 0-1): suma de senos de frecuencia entera y una envolvente
            // de ruido que hace que la «voz» suba y baje por tramos.
            float waveform(float p, float t)
            {
                float w = sin((p * 9 + t * 1.3) * 6.2832) * 0.5
                        + sin((p * 17 - t * 2.1) * 6.2832) * 0.3
                        + sin((p * 31 + t * 3.4) * 6.2832) * 0.2;
                float env = noise1(p * 12 + t * 2.0, 12) * 0.8 + 0.2;
                return w * env;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                // Distancia al borde más cercano, en fracciones de la altura de pantalla.
                float dx = min(uv.x, 1 - uv.x) * _Aspect;
                float dy = min(uv.y, 1 - uv.y);
                float d = min(dx, dy);

                // Grosor por zonas (u, v en 0-1, v = 0 abajo).
                float2 a = float2(uv.x * _Aspect, uv.y);
                float2 b = float2((1 - uv.x) * _Aspect, uv.y);
                float corners = exp(-dot(a, a) / 0.12) + exp(-dot(b, b) / 0.12);
                float bottom = exp(-uv.y / 0.25) * 0.45;
                float sides = exp(-pow((uv.y - 0.38) / 0.3, 2)) * 0.45 * step(dx, dy);
                float zone = saturate(0.05 + corners + bottom + sides);
                float thickness = _Thickness * zone;

                // Posición a lo largo del perímetro (0-1).
                float p = atan2(uv.y - 0.5, (uv.x - 0.5) * _Aspect) / 6.2832 + 0.5;
                float t = _FXTime * _Speed;
                float px = fwidth(d) * 1.5;

                float alpha = 0;
                float3 col = 0;
                float ink = 0;
                int layers = (int)_Layers;
                for (int k = 0; k < 6; k++)
                {
                    if (k >= layers) break;
                    float kk = k / max(_Layers - 1, 1);
                    // El latido llega antes a las capas de fuera: la onda viaja hacia dentro.
                    float pulse = 0.85 + 0.15 * sin((t - k * 0.35) * 6.2832 * 0.5);
                    float reach = thickness * (1 - kk * 0.7) * pulse;
                    reach *= 1 + _Amplitude * waveform(p + k * 0.13, t - k * 0.25);
                    float cover = 1 - smoothstep(reach - px, reach + px, d);
                    // Capas internas (las de más cerca del borde) más claras: degradado por escalones, sin suavizar.
                    float3 layerCol = lerp(i.color.rgb, _Cream.rgb, kk * 0.25);
                    col = lerp(col, layerCol, cover);
                    alpha += cover * _LayerAlpha;
                    if (k == 0) ink = 1 - smoothstep(0, px * 1.5, abs(d - reach));
                }
                alpha = saturate(alpha);
                col = lerp(col, _Ink.rgb, ink * _InkAlpha);
                alpha = max(alpha, ink * _InkAlpha);
                return fixed4(col, alpha * i.color.a);
            }
            ENDCG
        }
    }
}
