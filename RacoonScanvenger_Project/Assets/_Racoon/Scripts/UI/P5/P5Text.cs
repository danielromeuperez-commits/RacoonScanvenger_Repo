using System;
using System.Collections.Generic;
using Racoon.UI.HUD;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.P5
{
    /// <summary>
    /// Escribe un texto con el estilo de Persona 5 de los mockups usando piezas de UI (fichas 9-slice y TextMeshPro),
    /// sin imágenes horneadas. El aspecto lo define un <see cref="P5TextStyle"/>; opcionalmente lleva una «chapa»
    /// (p. ej. «P1») con su propio estilo, que entra en el mismo salto de línea que las palabras.
    /// Las piezas del color del jugador llevan <see cref="HUDTint"/> (las tiñe <see cref="HUDPlayerColors"/>);
    /// fuera del HUD se usa <see cref="colorJugador"/>.
    /// El tamaño del RectTransform se ajusta al contenido; el pivote y los anclajes los decides tú.
    /// Las piezas no se guardan (como los submeshes de TextMeshPro): se generan al activarse y al cambiar cualquier
    /// campo, así que funciona igual en escena, en prefabs y en sus instancias. Para animar, mueve este objeto.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class P5Text : MonoBehaviour
    {
        const HideFlags PieceFlags = HideFlags.DontSave | HideFlags.NotEditable;

        public enum Alignment { Izquierda, Centro, Derecha }

        [SerializeField, TextArea] string text = "TEXTO";
        public P5TextStyle style;
        [Tooltip("Jugador cuyo color se usa. 0 = ninguno: se usa «colorJugador».")]
        [Range(0, 2)] public int player = 1;
        public Color colorJugador = new Color32(0x39, 0x5C, 0xF5, 0xFF);
        [Tooltip("Multiplica el tamaño de letra del estilo.")]
        [Min(.1f)] public float escalaTexto = 1f;
        public Alignment alineacion = Alignment.Izquierda;
        [Tooltip("Giro del bloque entero (grados, positivo = antihorario).")]
        public float inclinacion;
        [Tooltip("Cambia el patrón de giro de las letras.")]
        public int semilla;

        [Header("Chapa opcional (p. ej. «P1»)")]
        public string chapa = "";
        public P5TextStyle estiloChapa;
        public bool chapaAlFinal;
        public float separacionChapa = 8f;

        public string Text => text;

        [NonSerialized] bool built;

        void OnEnable()
        {
            if (!built) Rebuild();
        }

#if UNITY_EDITOR
        // Cualquier cambio en el inspector (o un deshacer) regenera las piezas; no se puede hacer dentro de OnValidate
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildIfAlive;
            UnityEditor.EditorApplication.delayCall += RebuildIfAlive;
        }

        void RebuildIfAlive()
        {
            if (this != null && isActiveAndEnabled) Rebuild();
        }
#endif

        public void SetText(string value)
        {
            text = value;
            Rebuild();
        }

        public void SetChip(string value)
        {
            chapa = value;
            Rebuild();
        }

        [ContextMenu("Regenerar")]
        public void Rebuild()
        {
            if (!gameObject.scene.IsValid()) return; // asset de prefab sin abrir: se genera en sus instancias
            // Desactivado, TextMeshPro no mide bien el texto (las fichas salían diminutas): se monta al activarse
            if (!isActiveAndEnabled)
            {
                built = false;
                return;
            }
            built = true;
            Clear();

            var pieces = new List<Piece>();
            bool hasChip = !string.IsNullOrEmpty(chapa) && estiloChapa != null;
            if (hasChip && !chapaAlFinal) pieces.Add(MakeBlock(chapa, estiloChapa, chip: true));
            if (style != null && !string.IsNullOrEmpty(text))
            {
                string value = style.mayusculas ? text.ToUpperInvariant() : text;
                switch (style.modo)
                {
                    case P5TextStyle.Mode.LetraPorFicha:
                        foreach (var word in value.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            pieces.Add(MakeWord(word, style));
                        break;
                    case P5TextStyle.Mode.Bloque:
                        pieces.Add(MakeBlock(value, style, chip: false));
                        break;
                    default:
                        pieces.Add(MakePlain(value, style));
                        break;
                }
            }
            if (hasChip && chapaAlFinal) pieces.Add(MakeBlock(chapa, estiloChapa, chip: true));
            Arrange(pieces);
            ApplyColors();
        }

        // ---------- Composición ----------

        class Piece
        {
            public RectTransform rect;
            public float width, height, gapAfter;
        }

        void Arrange(List<Piece> pieces)
        {
            float maxWidth = style != null ? style.anchoMaximo : 0f;
            float rowGap = style != null ? style.separacionFilas : 0f;
            var rows = new List<List<Piece>> { new() };
            var widths = new List<float> { 0f };
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                int r = rows.Count - 1;
                float gap = rows[r].Count > 0 ? rows[r][^1].gapAfter : 0f;
                if (maxWidth > 0f && rows[r].Count > 0 && widths[r] + gap + piece.width > maxWidth)
                {
                    rows.Add(new List<Piece>());
                    widths.Add(0f);
                    r++;
                    gap = 0f;
                }
                rows[r].Add(piece);
                widths[r] += gap + piece.width;
            }

            float width = 0f;
            foreach (var w in widths) width = Mathf.Max(width, w);
            float y = 0f;
            for (int r = 0; r < rows.Count; r++)
            {
                float rowHeight = 0f;
                foreach (var p in rows[r]) rowHeight = Mathf.Max(rowHeight, p.height);
                float x = alineacion switch
                {
                    Alignment.Centro => (width - widths[r]) / 2f,
                    Alignment.Derecha => width - widths[r],
                    _ => 0f,
                };
                for (int i = 0; i < rows[r].Count; i++)
                {
                    var p = rows[r][i];
                    if (i > 0) x += rows[r][i - 1].gapAfter;
                    p.rect.anchorMin = p.rect.anchorMax = new Vector2(0f, 1f);
                    p.rect.pivot = new Vector2(.5f, .5f);
                    p.rect.sizeDelta = new Vector2(p.width, p.height);
                    p.rect.anchoredPosition = new Vector2(x + p.width / 2f, -(y + rowHeight / 2f));
                    x += p.width;
                }
                y += rowHeight + (r < rows.Count - 1 ? rowGap : 0f);
            }

            // Las piezas van ancladas a la esquina superior izquierda del rect, así que valen para cualquier pivote
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(width, y);
            rt.localRotation = Quaternion.Euler(0f, 0f, inclinacion);
        }

        Piece MakeWord(string word, P5TextStyle s)
        {
            var go = NewChild("Palabra " + word, transform);
            float tallest = 0f;
            for (int i = 0; i < word.Length; i++) tallest = Mathf.Max(tallest, s.Look(i).escala);
            float tileHeight = Size(s) * tallest * s.altoLinea + s.relleno.y * 2f;
            float x = 0f;
            for (int i = 0; i < word.Length; i++)
            {
                var look = s.Look(i);
                string letter = word[i].ToString();
                var tile = MakeTile(letter, look, s, go.transform, out float textWidth);
                float border = look.borde != P5Paint.Ninguno ? s.grosorBorde : 0f;
                float w = textWidth + (s.relleno.x + border) * 2f;
                var rt = (RectTransform)tile.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, .5f);
                rt.pivot = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(w, tileHeight);
                rt.anchoredPosition = new Vector2(x + w / 2f, 0f);
                // Patrón de giro de las letras recortadas del HTML: (i·47 mod 11) − 5
                float angle = (((i * 47 + semilla * 13) % 11) - 5) / 5f * s.giroLetras;
                rt.localRotation = Quaternion.Euler(0f, 0f, -angle);
                x += w + (i < word.Length - 1 ? s.separacionLetras : 0f);
            }
            return new Piece { rect = (RectTransform)go.transform, width = x, height = tileHeight, gapAfter = s.separacionPalabras };
        }

        Piece MakeBlock(string value, P5TextStyle s, bool chip)
        {
            var look = s.Look(0);
            var tile = MakeTile(value, look, s, transform, out float textWidth);
            tile.name = chip ? "Chapa " + value : "Bloque";
            float border = look.borde != P5Paint.Ninguno ? s.grosorBorde : 0f;
            float w = textWidth + (s.relleno.x + border) * 2f;
            float h = Size(s) * look.escala * s.altoLinea + (s.relleno.y + border) * 2f;
            return new Piece { rect = (RectTransform)tile.transform, width = w, height = h, gapAfter = chip ? separacionChapa : s.separacionPalabras };
        }

        Piece MakePlain(string value, P5TextStyle s)
        {
            var go = NewChild("Texto", transform);
            var look = s.Look(0);
            var tmp = AddLabel(go, value, s, look);
            float w = tmp.GetPreferredValues(value).x;
            return new Piece { rect = (RectTransform)go.transform, width = w, height = Size(s) * look.escala * s.altoLinea, gapAfter = s.separacionPalabras };
        }

        // Ficha: [borde] → [fondo] → texto. Las sombras van en la pieza más exterior.
        GameObject MakeTile(string value, P5TileLook look, P5TextStyle s, Transform parent, out float textWidth)
        {
            var tile = NewChild("Ficha " + value, parent);
            float border = look.borde != P5Paint.Ninguno ? s.grosorBorde : 0f;
            Graphic outer = null;
            if (look.borde != P5Paint.Ninguno)
                outer = AddPanel(tile, s, s.radio, look.borde, look.bordePersonalizado);
            if (look.fondo != P5Paint.Ninguno)
            {
                var fill = tile;
                if (border > 0f)
                {
                    fill = NewChild("Fondo", tile.transform);
                    Stretch(fill, border);
                }
                var panel = AddPanel(fill, s, Mathf.Max(0f, s.radio - border), look.fondo, look.fondoPersonalizado);
                outer ??= panel;
            }
            if (outer != null) AddShadows(outer.gameObject, s);

            var labelObject = NewChild("Texto", tile.transform);
            Stretch(labelObject, 0f);
            var label = AddLabel(labelObject, value, s, look);
            textWidth = label.GetPreferredValues(value).x;
            return tile;
        }

        Image AddPanel(GameObject go, P5TextStyle s, float radius, P5Paint paint, Color custom)
        {
            var image = go.AddComponent<Image>();
            bool rounded = radius > 0f && s.fichaRedonda != null;
            image.sprite = rounded ? s.fichaRedonda : s.fichaRecta;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = rounded ? P5TextStyle.SpriteRadius / radius : 1f;
            image.raycastTarget = false;
            Paint(image, paint, custom);
            return image;
        }

        TextMeshProUGUI AddLabel(GameObject go, string value, P5TextStyle s, P5TileLook look)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = s.fuente;
            if (s.material != null) tmp.fontSharedMaterial = s.material;
            tmp.fontSize = Size(s) * look.escala;
            tmp.fontStyle = s.estiloFuente;
            tmp.characterSpacing = s.interletrado / Mathf.Max(1f, tmp.fontSize) * 100f;
            tmp.text = value;
            tmp.alignment = s.centradoOptico ? TextAlignmentOptions.CenterGeoAligned : TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            Paint(tmp, look.texto == P5Paint.Ninguno ? P5Paint.Negro : look.texto, look.textoPersonalizado);
            return tmp;
        }

        void AddShadows(GameObject go, P5TextStyle s)
        {
            if (s.sombras == null) return;
            foreach (var look in s.sombras)
            {
                if (look.color == P5Paint.Ninguno) continue;
                var shadow = go.AddComponent<Shadow>();
                shadow.effectDistance = look.desplazamiento;
                if (look.color == P5Paint.ColorJugador && player > 0)
                {
                    var tint = go.AddComponent<HUDTint>();
                    tint.player = player;
                    tint.shadow = shadow;
                }
                var color = Resolve(look.color, look.personalizado);
                color.a *= look.opacidad;
                shadow.effectColor = color;
            }
        }

        void Paint(Graphic graphic, P5Paint paint, Color custom)
        {
            graphic.color = Resolve(paint, custom);
            if (paint == P5Paint.ColorJugador && player > 0)
                graphic.gameObject.AddComponent<HUDTint>().player = player;
        }

        Color Resolve(P5Paint paint, Color custom) => paint switch
        {
            P5Paint.Blanco => Color.white,
            P5Paint.Negro => Color.black,
            P5Paint.ColorJugador => colorJugador,
            P5Paint.Personalizado => custom,
            _ => Color.clear,
        };

        void ApplyColors()
        {
            var colors = GetComponentInParent<HUDPlayerColors>(true);
            if (colors != null) colors.Apply();
        }

        float Size(P5TextStyle s) => s.tamano * escalaTexto;

        // ---------- Utilidades ----------

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
                // Piezas guardadas por una versión anterior dentro de un prefab: desde la instancia no se pueden
                // borrar; se ocultan hasta que se vuelva a montar el prefab
                if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabInstance(child))
                {
                    if (child.activeSelf) child.SetActive(false);
                    continue;
                }
#endif
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        GameObject NewChild(string childName, Transform parent)
        {
            var go = new GameObject(childName, typeof(RectTransform)) { layer = gameObject.layer, hideFlags = PieceFlags };
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(GameObject go, float inset)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
