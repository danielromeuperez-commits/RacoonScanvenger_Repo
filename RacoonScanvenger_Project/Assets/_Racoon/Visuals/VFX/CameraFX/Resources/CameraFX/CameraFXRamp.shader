// UI cel-shading: convierte la banda horneada (R: 0 = contorno, 1/3 = sombra, 2/3 = base, 1 = luz) en colores
// planos. Lo usan llamas, virutas y rayos de CameraFX: la misma textura sirve para cada personaje.
// El color del vértice (RawImage.color) tiñe las bandas pero no el contorno, que siempre es la tinta del HUD.
// Está en Resources para que se incluya en la build (CameraFX lo carga con Resources.Load).
Shader "Racoon/UI/CameraFXRamp"
{
    Properties
    {
        [PerRendererData] _MainTex ("Banda (R) y alfa (A)", 2D) = "white" {}
        _Outline ("Contorno", Color) = (0.294, 0.263, 0.4, 1)
        _Low ("Sombra", Color) = (0.75, 0.35, 0.4, 1)
        _Mid ("Base", Color) = (0.92, 0.45, 0.49, 1)
        _High ("Luz", Color) = (1, 0.97, 0.93, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Outline, _Low, _Mid, _High;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float h = t.r;
                fixed3 band = h < 0.5 ? _Low.rgb : (h < 0.8333 ? _Mid.rgb : _High.rgb);
                fixed3 c = h < 0.1667 ? _Outline.rgb : band * i.color.rgb;
                return fixed4(c, t.a * i.color.a);
            }
            ENDCG
        }
    }
}
