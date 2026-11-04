// Cielo de degradado de tres tonos (arriba / horizonte / abajo), para que el escenario no flote en el gris por
// defecto de Unity. Lo usa M_Skybox_Pastel (Visuals/Skybox), puesto como Skybox Material en Lighting de cada escena.
Shader "Racoon/Skybox Gradient"
{
    Properties
    {
        _TopColor ("Arriba", Color) = (0.66, 0.77, 0.93, 1)
        _HorizonColor ("Horizonte", Color) = (1, 0.93, 0.86, 1)
        _BottomColor ("Abajo", Color) = (0.82, 0.79, 0.89, 1)
        _TopExponent ("Caída hacia arriba", Range(0.1, 8)) = 1.6
        _BottomExponent ("Caída hacia abajo", Range(0.1, 8)) = 2.5
        _HorizonOffset ("Altura del horizonte", Range(-0.5, 0.5)) = 0
        _Intensity ("Intensidad", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _TopColor, _HorizonColor, _BottomColor;
            half _TopExponent, _BottomExponent, _HorizonOffset, _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.dir).y - _HorizonOffset;
                // Peso de cada tono: arriba y abajo crecen desde el horizonte con su propia caída.
                float top = 1 - pow(min(1, 1 - y), _TopExponent);
                float bottom = 1 - pow(min(1, 1 + y), _BottomExponent);
                float horizon = 1 - top - bottom;
                half3 color = _TopColor.rgb * top + _HorizonColor.rgb * horizon + _BottomColor.rgb * bottom;
                return half4(color * _Intensity, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
