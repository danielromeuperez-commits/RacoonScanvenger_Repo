using System;
using TMPro;
using UnityEngine;

namespace Racoon.UI.P5
{
    /// <summary>Con qué se pinta una parte de una ficha.</summary>
    public enum P5Paint { Ninguno, Blanco, Negro, ColorJugador, Personalizado }

    /// <summary>Aspecto de una ficha: fondo, texto, borde y tamaño relativo de la letra.</summary>
    [Serializable]
    public class P5TileLook
    {
        public P5Paint fondo = P5Paint.Blanco;
        public P5Paint texto = P5Paint.Negro;
        public P5Paint borde = P5Paint.Ninguno;
        [Tooltip("Tamaño de la letra respecto al del estilo.")]
        [Min(.1f)] public float escala = 1f;
        public Color fondoPersonalizado = Color.white;
        public Color textoPersonalizado = Color.black;
        public Color bordePersonalizado = Color.black;
    }

    /// <summary>Una sombra. Se encadenan como el filtro drop-shadow del HTML: cada una copia todo lo anterior.</summary>
    [Serializable]
    public class P5ShadowLook
    {
        public P5Paint color = P5Paint.Negro;
        [Range(0f, 1f)] public float opacidad = 1f;
        public Vector2 desplazamiento = new(8f, -8f);
        public Color personalizado = Color.black;
    }

    /// <summary>
    /// Estilo de texto Persona 5 para <see cref="P5Text"/>: letras recortadas (una ficha por letra), bloque (una sola
    /// ficha, p. ej. una chapa) o texto suelto. Medidas en px a escala 2560×1440.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/UI/Estilo de texto P5", fileName = "EstiloP5")]
    public class P5TextStyle : ScriptableObject
    {
        public enum Mode { LetraPorFicha, Bloque, SoloTexto }

        /// <summary>Radio de las esquinas dibujado en el sprite de ficha redonda (9-slice).</summary>
        public const float SpriteRadius = 96f;

        [Header("Forma")]
        public Mode modo = Mode.LetraPorFicha;
        public bool mayusculas = true;

        [Header("Tipografía")]
        public TMP_FontAsset fuente;
        [Tooltip("Opcional, p. ej. un material con contorno.")]
        public Material material;
        public float tamano = 28f;
        [Tooltip("Espacio extra entre letras dentro de un mismo texto TMP (px).")]
        public float interletrado;
        public FontStyles estiloFuente = FontStyles.Normal;
        [Tooltip("Centra cada texto por la forma real de sus letras y no por la línea: útil para símbolos sueltos como «?».")]
        public bool centradoOptico;

        [Header("Fichas")]
        [Tooltip("Se repite letra a letra. En «Bloque» y «Solo texto» solo se usa la primera.")]
        public P5TileLook[] secuencia =
        {
            new(),
            new() { fondo = P5Paint.Negro, texto = P5Paint.Blanco, borde = P5Paint.Blanco, escala = 1.2f },
            new() { fondo = P5Paint.ColorJugador, texto = P5Paint.Blanco },
        };
        [Tooltip("Relleno interior de cada ficha (px), sin contar el borde.")]
        public Vector2 relleno = new(8f, 0f);
        [Tooltip("Alto del texto dentro de la ficha = tamaño × esto.")]
        public float altoLinea = 1.3f;
        public float grosorBorde = 4f;
        [Tooltip("Radio de las esquinas (px). 0 = esquinas rectas.")]
        public float radio;
        public Sprite fichaRecta;
        [Tooltip("Ficha redonda en 9-slice con radio SpriteRadius.")]
        public Sprite fichaRedonda;

        [Header("Composición")]
        public float separacionLetras = 4f;
        public float separacionPalabras = 12f;
        public float separacionFilas = 6f;
        [Tooltip("Ancho máximo antes de saltar de línea entre palabras (px). 0 = sin salto.")]
        public float anchoMaximo = 300f;
        [Tooltip("Giro máximo de cada letra (grados), con el patrón de las letras recortadas.")]
        public float giroLetras = 5f;

        [Header("Sombras de las fichas")]
        public P5ShadowLook[] sombras =
        {
            new(),
            new() { color = P5Paint.ColorJugador },
        };

        public P5TileLook Look(int index) =>
            secuencia == null || secuencia.Length == 0 ? new P5TileLook() : secuencia[index % secuencia.Length];

#if UNITY_EDITOR
        // Al retocar el estilo se regeneran todos los textos que lo usan (escenas abiertas y modo prefab)
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildUsers;
            UnityEditor.EditorApplication.delayCall += RebuildUsers;
        }

        void RebuildUsers()
        {
            if (this == null) return;
            foreach (var text in Resources.FindObjectsOfTypeAll<P5Text>())
                if (text.gameObject.scene.IsValid() && text.isActiveAndEnabled && (text.style == this || text.estiloChapa == this))
                    text.Rebuild();
        }
#endif
    }
}
