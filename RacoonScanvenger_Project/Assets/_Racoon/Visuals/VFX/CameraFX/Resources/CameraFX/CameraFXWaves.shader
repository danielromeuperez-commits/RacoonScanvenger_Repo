// UI: aura de energía cartoon de CameraFX. Varias bandas planas y translúcidas que suben desde el borde inferior, con
// un triángulo en cada esquina que trepa un poco por el lateral. El borde de cada banda ondula como una forma de onda
// de audio (senos modulados por ruido) y el latido viaja capa a capa. Cada capa usa su tono (_Tone0-3, los pone
// CameraFX a partir del color del canal aura) y se mezcla con el siguiente a lo largo del borde. Alfa: el del RawImage.
// Procedural (sin textura), así que no pierde resolución. Está en Resources para que entre en la build.
Shader "Racoon/UI/CameraFXWaves"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sin uso", 2D) = "white" {}
        _Tone0 ("Tono capa exterior", Color) = (0.15, 0.62, 0.52, 1)
        _Tone1 ("Tono 1", Color) = (0.25, 0.75, 0.7, 1)
        _Tone2 ("Tono 2", Color) = (0.25, 0.65, 0.78, 1)
        _Tone3 ("Tono capa pegada al borde", Color) = (0.6, 0.88, 0.85, 1)
        _Ink ("Línea del borde", Color) = (0.294, 0.263, 0.4, 1)
        _Layers ("Capas", Range(1, 4)) = 4
        _BottomHeight ("Altura en el borde inferior", Range(0, 0.3)) = 0.05
        _CornerHeight ("Altura extra en las esquinas", Range(0, 0.8)) = 0.3
        _CornerWidth ("Ancho del triángulo (fracción del ancho)", Range(0.02, 0.5)) = 0.2
        _Grow ("Crecimiento (lo pone CameraFX)", Range(0, 1)) = 1
        _Amplitude ("Amplitud de la onda", Range(0, 1)) = 0.35
        _Speed ("Velocidad de la onda", Float) = 1.2
        _LayerAlpha ("Alfa por capa", Range(0, 1)) = 0.2
        _InkAlpha ("Alfa de la línea", Range(0, 1)) = 0.3
        _FXTime ("Tiempo (lo pone CameraFX, sin escalar)", Float) = 0
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

            fixed4 _Tone0, _Tone1, _Tone2, _Tone3, _Ink;
            float _Layers, _BottomHeight, _CornerHeight, _CornerWidth, _Grow, _Amplitude, _Speed;
            float _LayerAlpha, _InkAlpha, _FXTime;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            float noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(hash(i), hash(i + 1), f);
            }

            // Forma de onda a lo largo del borde (p en 0-1): senos y una envolvente de ruido que hace que la «voz»
            // suba y baje por tramos.
            float waveform(float p, float t)
            {
                float w = sin((p * 9 + t * 1.3) * 6.2832) * 0.5
                        + sin((p * 17 - t * 2.1) * 6.2832) * 0.3
                        + sin((p * 31 + t * 3.4) * 6.2832) * 0.2;
                return w * (noise1(p * 12 + t * 2.0) * 0.8 + 0.2);
            }

            fixed3 tone(int k)
            {
                if (k == 0) return _Tone0.rgb;
                if (k == 1) return _Tone1.rgb;
                if (k == 2) return _Tone2.rgb;
                return _Tone3.rgb;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float u = i.uv.x;
                float v = i.uv.y;
                // Banda inferior más un triángulo en cada esquina.
                float triangle_ = saturate(1 - min(u, 1 - u) / _CornerWidth);
                float height = (_BottomHeight + _CornerHeight * triangle_) * _Grow;
                float t = _FXTime * _Speed;

                float alpha = 0;
                float3 col = 0;
                float ink = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (k >= (int)_Layers) break;
                    float kk = k / max(_Layers - 1, 1);
                    // El latido llega antes a las capas de fuera: la onda viaja de una capa a la otra.
                    float pulse = 0.85 + 0.15 * sin((t - k * 0.35) * 3.1416);
                    float reach = height * (1 - kk * 0.7) * pulse;
                    reach *= 1 + _Amplitude * waveform(u + k * 0.13, t - k * 0.25);
                    float edge = v - reach;
                    float px = max(fwidth(edge), 1e-5);
                    float cover = 1 - smoothstep(-px, px, edge);
                    // Variante de tono: cada capa se mezcla con la siguiente por tramos que se desplazan.
                    float mixTone = noise1(u * 8 + t * 0.6 + k * 3.1) * 0.7;
                    col = lerp(col, lerp(tone(k), tone(k == 3 ? 0 : k + 1), mixTone), cover);
                    alpha += cover * _LayerAlpha;
                    if (k == 0) ink = 1 - smoothstep(0, px * 1.5, abs(edge));
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
