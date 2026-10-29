using System.Collections.Generic;
using System.IO;
using System.Linq;
using Racoon.Gameplay;
using Racoon.Items;
using Racoon.UI.HUD;
using Racoon.UI.P5;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Monta las variantes del HUD a partir de los manifiestos «.hud.json» de Assets/_Racoon/2D/Sprites/UI/HUD:
    /// fuentes TMP, estilos de texto P5, un prefab por variante (paneles en sprite; textos con P5Text/TMP; rodillos
    /// con máscara; huecos de icono; referencias en HUDView) y la escena SCN_HUD_Variantes con todas.
    /// No toca la escena abierta. Deja capturas de revisión en Temp/HUD_Revision/&lt;fecha&gt;.
    /// </summary>
    public static class HUDVariantBuilder
    {
        const string SpritesRoot = "Assets/_Racoon/2D/Sprites/UI/HUD";
        const string FontsDir = "Assets/_Racoon/2D/Fonts";
        const string StylesDir = "Assets/_Racoon/2D/UI/EstilosTextoP5";
        const string PrefabDir = "Assets/_Racoon/Prefabs/UI/HUD";
        const string ScenePath = "Assets/_Racoon/Scenes/SCN_HUD_Variantes.unity";
        const float ReelPitch = 100f, ReelOffset = 50f;
        // 0-9, 0-9 y dos más: el rebote del rodillo puede pasarse un poco del último dígito
        const int ReelCells = 22;

        // Orden de dibujo dentro de cada jugador (de abajo arriba): las insignias siempre por encima de los paneles
        static readonly string[] PlayerOrder = { "Velocidad", "Llama (racha)", "Retrato", "Puntos", "Ítems", "Bolsa", "Etiqueta", "Racha", "TOP", "Corona (va ganando)", "Mareo", "Ejemplos (texto dinámico)" };
        // Apodos de los personajes en el HUD (por el nombre de su prefab); el resto usa su displayName
        static readonly (string prefab, string nickname)[] Nicknames = { ("Raccoon", "MAPACHE"), ("Racoon", "MAPACHE"), ("Opossum", "ZARIGÜEYA"), ("RedPanda", "PANDA ROJO") };
        // Piezas que se montan aquí (TMP, P5Text, máscaras…) aunque el manifiesto traiga una imagen de muestra
        static readonly HashSet<string> GeneratedRoles = new() { "reelDigit", "timeText", "timeShadow", "pop", "header", "tag", "top", "streak", "qMain", "qSub", "pill" };

#pragma warning disable 0649 // los rellena JsonUtility
        [System.Serializable]
        class Node
        {
            public int id, parent, tint, player, k;
            public float x, y, w, h; // en el lienzo de 2560×1440 aunque el PSD tenga más resolución
            public float rot, size;
            public string name, sprite, role, color;
            public bool visible;
        }

        [System.Serializable]
        class Manifest
        {
            public string name, variant;
            public bool nick, chip, chipP5;
            public int canvasWidth, canvasHeight;
            public Node[] nodes;
        }
#pragma warning restore 0649

        class Kit
        {
            public TMP_FontAsset dela, lilita, gochi, spaceMono, vt323, sniglet, fredoka, titan, baloo;
            public Material delaOutline;
            public Sprite tile, round;
            public readonly Dictionary<string, Look> looks = new();
            public List<PlayerCharacterDefinition> characters;
            public ItemDatabase items;

            public Look LookFor(string variant) => looks.TryGetValue(variant ?? "", out var look) ? look : looks["p5s"];
        }

        /// <summary>
        /// Aspecto de una variante en Unity (la «variant» del manifiesto): estilos de los textos P5, tipografía y color de
        /// los textos TMP (tiempo, rodillos, «+N») y animación propia del panel de puntos.
        /// </summary>
        class Look
        {
            public P5TextStyle apodo, chapa, cabecera, insignia, contador, interrogante, pastilla, bolsa;
            // Opcional: chapa «BOLSA» delante del número de la bolsa (si no, todo va en una ficha: «BOLSA +N»)
            public P5TextStyle bolsaChapa;
            public TMP_FontAsset timeFont, reelFont, popFont;
            public Material timeMaterial, popMaterial;
            public float timeSize = 120f, timeSpacing = 3.3f, reelSize = 52f, popSize = 60f;
            public bool timeItalic = true, timeShadows = true, popTint, popShadow = true;
            public Color timeColor = Color.white, urgentTimeColor = Color.clear, popColor = Color.white;
            // Las cifras oscuras y claras de los rodillos (el manifiesto solo distingue negro y blanco)
            public Color reelDark = Color.black, reelLight = Color.white;
            public HUDView.PanelFx panelFx;
            // Colores de jugador por defecto (null = los de HUDPlayerColors) y cuánto se aclaran hacia el blanco
            public Color[] playerColors;
            public float pastel;
            // El retrato reacciona a lo que le pasa al jugador (HUDView.reactions)
            public bool reactions;

            public Look Copy() => (Look)MemberwiseClone();
        }

        static int UILayer => LayerMask.NameToLayer("UI");

        [MenuItem("Racoon/HUD/Montar variantes")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("HUD", "Sal del modo Play antes de montar el HUD.", "Vale");
                return;
            }
            var manifests = AssetDatabase.FindAssets("t:TextAsset", new[] { SpritesRoot })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".hud.json")).OrderBy(p => p).ToList();
            if (manifests.Count == 0)
            {
                Debug.LogError($"HUD: no hay manifiestos .hud.json en {SpritesRoot}");
                return;
            }

            var kit = PrepareAssets();
            EnsureFolder(PrefabDir);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var built = new List<string>();
            try
            {
                var canvas = CreateCanvas();
                // Demo sin red: al darle a Play en la escena de variantes se ven todas las animaciones
                SetReference(canvas.gameObject.AddComponent<HUDDemo>(), "items", kit.items);
                var instances = new List<GameObject>();
                foreach (var path in manifests)
                {
                    try // una variante con problemas no impide montar las demás
                    {
                        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                        var prefab = BuildPrefab(manifest, Path.GetDirectoryName(path).Replace('\\', '/'), kit, canvas.transform);
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        instance.transform.SetParent(canvas.transform, false);
                        instance.SetActive(instances.Count == 0);
                        instances.Add(instance);
                        built.Add(prefab.name);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"HUD: no se pudo montar {path}");
                    }
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
                RenderPreviews(canvas, instances);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"HUD: montadas {built.Count} variantes ({string.Join(", ", built)}) en {PrefabDir} y la escena {ScenePath}.");
        }

        /// <summary>
        /// Deja la escena abierta lista para el HUD: añade lo que falte de esto, con deshacer.
        ///  - El HUD: la variante seleccionada en el Project o, si no hay, la P5 pastel reactiva con apodos y P1/P2. Es su propio
        ///    Canvas y busca solo el MatchTimer y a los jugadores: no hay que conectar nada.
        ///  - El MatchManager (PF_MNG_MatchManager con el MatchTimer): sin él el tiempo y su barra no corren.
        ///  - El manager de efectos de cámara (MatchCameraFX), para ajustarlos desde la jerarquía.
        /// </summary>
        [MenuItem("Racoon/HUD/Preparar escena abierta (HUD + tiempo)")]
        public static void PrepareOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            var added = new List<GameObject>();

            var hud = Object.FindFirstObjectByType<HUDController>(FindObjectsInactive.Include);
            if (!hud)
            {
                var selected = Selection.activeObject as GameObject;
                var prefab = selected && PrefabUtility.IsPartOfPrefabAsset(selected) && selected.GetComponent<HUDController>()
                    ? selected
                    : AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/PF_HUD_P5PastelReactivo_ConNick_ConP.prefab")
                      ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/PF_HUD_P5Pastel_ConNick_ConP.prefab")
                      ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/PF_HUD_P5Soft_ConNick_ConP.prefab");
                if (!prefab || !prefab.GetComponent<HUDController>())
                {
                    EditorUtility.DisplayDialog("HUD", "Primero monta las variantes: Racoon → HUD → Montar variantes.", "Vale");
                    return;
                }
                added.Add(Spawn(prefab, scene));
            }

            if (!Object.FindFirstObjectByType<MatchTimer>(FindObjectsInactive.Include))
            {
                var manager = AssetDatabase.FindAssets("PF_MNG_MatchManager t:Prefab")
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(p => p && p.GetComponentInChildren<MatchTimer>(true));
                if (manager) added.Add(Spawn(manager, scene));
                else Debug.LogWarning("HUD: no encuentro PF_MNG_MatchManager (con MatchTimer); sin él el tiempo no corre.");
            }

            var cameraFX = CameraFXManagerMenu.Ensure();
            if (cameraFX) added.Add(cameraFX);

            if (added.Count == 0)
            {
                EditorUtility.DisplayDialog("HUD", "La escena ya tiene el HUD, el MatchTimer y los efectos de cámara: no falta nada.", "Vale");
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.objects = added.ToArray();
            Debug.Log($"HUD: añadido a {scene.name}: {string.Join(", ", added.Select(a => a.name))}. Guarda la escena para conservarlo.");

            static GameObject Spawn(GameObject prefab, Scene target)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target);
                Undo.RegisterCreatedObjectUndo(instance, "Preparar escena para el HUD");
                return instance;
            }
        }

        // ---------- Recursos compartidos: sprites, fuentes y estilos P5 ----------

        static Kit PrepareAssets()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SpritesRoot }))
                    if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti && HUDSpriteImporter.Configure(ti))
                        ti.SaveAndReimport();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var kit = new Kit
            {
                dela = EnsureFont("DelaGothicOne-Regular.ttf", "DelaGothicOne SDF", 2048, 12, 0),
                lilita = EnsureFont("LilitaOne-Regular.ttf", "LilitaOne SDF", 1024, 9, 18), // 18 = losa inclinada ~10°
                tile = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesRoot}/_Comun/9s_FichaLisa.png"),
                round = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesRoot}/_Comun/9s96_FichaRedonda.png"),
            };
            // Tipografías de las variantes del concept (si falta alguna, se usa Lilita One)
            kit.gochi = TryFont("GochiHand-Regular.ttf", "GochiHand SDF", kit);
            kit.spaceMono = TryFont("SpaceMono-Bold.ttf", "SpaceMono SDF", kit);
            kit.vt323 = TryFont("VT323-Regular.ttf", "VT323 SDF", kit);
            kit.sniglet = TryFont("Sniglet-ExtraBold.ttf", "Sniglet SDF", kit);
            kit.fredoka = TryFont("Fredoka-Bold.ttf", "Fredoka SDF", kit);
            kit.titan = TryFont("TitanOne-Regular.ttf", "TitanOne SDF", kit);
            kit.baloo = TryFont("Baloo2-ExtraBold.ttf", "Baloo2 SDF", kit);
            kit.delaOutline = EnsureEffectMaterial(kit.dela, "DelaGothicOne SDF Contorno", m => Outline(m, .3f, Color.black));
            EnsureStyles(kit);
            kit.characters = AssetDatabase.FindAssets("t:PlayerCharacterDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<PlayerCharacterDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c).OrderBy(c => c.name).ToList();
            kit.items = AssetDatabase.FindAssets("t:ItemDatabase")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(d => d);
            return kit;
        }

        static TMP_FontAsset EnsureFont(string file, string assetName, int atlasSize, int padding, byte italic)
        {
            string path = $"{FontsDir}/{assetName}.asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/{file}");
            if (font == null) throw new FileNotFoundException($"HUD: falta la fuente {FontsDir}/{file}");

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            bool broken = fontAsset != null && (fontAsset.atlasTextures == null || fontAsset.atlasTextures.Length == 0 ||
                                                fontAsset.atlasTextures[0] == null || fontAsset.material == null);
            if (broken) AssetDatabase.DeleteAsset(path); // p. ej. de un montaje interrumpido antes de guardar
            FontEngine.InitializeFontEngine(); // como hace el menú de TextMeshPro antes de cargar una fuente
            if (fontAsset == null || broken)
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(font, 90, padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, false);
                if (fontAsset == null) throw new System.InvalidOperationException($"HUD: TextMeshPro no pudo crear {assetName} desde {file}");
                fontAsset.name = assetName;
                AssetDatabase.CreateAsset(fontAsset, path);
                fontAsset.atlasTextures[0].name = assetName + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
                fontAsset.material.name = assetName + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                AssetDatabase.SaveAssets(); // el atlas y el material quedan guardados aunque el montaje falle después
            }
            fontAsset.italicStyle = italic;
            // Atlas dinámico: TextMeshPro añade cada glifo la primera vez que se dibuja (también en partida)
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        static TMP_FontAsset TryFont(string file, string assetName, Kit kit)
        {
            if (!File.Exists($"{FontsDir}/{file}"))
            {
                Debug.LogWarning($"HUD: falta {FontsDir}/{file}; se usa Lilita One en su lugar.");
                return kit.lilita;
            }
            var font = EnsureFont(file, assetName, 1024, 9, 0);
            // Ninguna de estas trae la «★» del TOP: la toman de Dela Gothic One
            font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(kit.dela))
            {
                font.fallbackFontAssetTable.Add(kit.dela);
                EditorUtility.SetDirty(font);
            }
            return font;
        }

        // Material de una fuente con efecto (contorno, brillo…). Se rehace en cada montaje a partir del de la fuente
        static Material EnsureEffectMaterial(TMP_FontAsset font, string name, System.Action<Material> setup)
        {
            string path = $"{FontsDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.CopyPropertiesFromMaterial(font.material);
            setup(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Outline(Material m, float width, Color color)
        {
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            m.SetColor(ShaderUtilities.ID_OutlineColor, color);
        }

        static void Glow(Material m, Color color)
        {
            m.EnableKeyword(ShaderUtilities.Keyword_Glow);
            m.SetColor(ShaderUtilities.ID_GlowColor, color);
            m.SetFloat(ShaderUtilities.ID_GlowOuter, .55f);
            m.SetFloat(ShaderUtilities.ID_GlowPower, .7f);
        }

        static Color Hex(string html) => ColorUtility.TryParseHtmlString(html, out var color) ? color : Color.magenta;

        // Estilos de texto de cada variante (px a 2x). Solo se crean si no existen: si los retocas, el montaje los respeta.
        static void EnsureStyles(Kit kit)
        {
            EnsureFolder(StylesDir);
            var noShadows = new P5ShadowLook[0];
            const P5Paint C = P5Paint.Personalizado, J = P5Paint.ColorJugador, W = P5Paint.Blanco, K = P5Paint.Negro, N = P5Paint.Ninguno;
            P5TileLook Tile(P5Paint fondo, P5Paint texto, P5Paint borde = N, Color? f = null, Color? t = null, Color? b = null) => new()
            {
                fondo = fondo, texto = texto, borde = borde,
                fondoPersonalizado = f ?? Color.white, textoPersonalizado = t ?? Color.black, bordePersonalizado = b ?? Color.black,
            };
            P5ShadowLook[] Drop(Color color, float alpha, float dy) =>
                new[] { new P5ShadowLook { color = C, personalizado = color, opacidad = alpha, desplazamiento = new Vector2(0f, -dy) } };
            P5TextStyle Block(string name, TMP_FontAsset font, float size, Vector2 pad, float border, float radius, P5TileLook tile,
                              P5ShadowLook[] shadows, float spacing = 0f) => Style(name, kit, s =>
            {
                s.modo = P5TextStyle.Mode.Bloque; s.fuente = font; s.tamano = size; s.relleno = pad; s.grosorBorde = border; s.radio = radius;
                s.secuencia = new[] { tile }; s.sombras = shadows; s.interletrado = spacing; s.altoLinea = 1.15f; s.anchoMaximo = 0f;
            });
            P5TextStyle Plain(string name, TMP_FontAsset font, float size, P5TileLook tile) => Style(name, kit, s =>
            {
                s.modo = P5TextStyle.Mode.SoloTexto; s.fuente = font; s.tamano = size; s.altoLinea = 1.2f;
                s.secuencia = new[] { tile }; s.sombras = noShadows; s.centradoOptico = true;
            });

            // ----- Persona 5 soft y Soft limpio -----
            var p5 = new Look
            {
                apodo = Style("P5_Apodo", kit, s => { }),
                cabecera = Style("P5_Cabecera", kit, s => { s.tamano = 24f; s.radio = 8f; s.anchoMaximo = 0f; }),
                chapa = Style("P5_Chapa", kit, s =>
                {
                    s.modo = P5TextStyle.Mode.Bloque; s.tamano = 40f; s.altoLinea = 1f; s.relleno = new Vector2(14f, 6f); s.grosorBorde = 6f;
                    s.secuencia = new[] { Tile(J, W, K) };
                }),
                insignia = Style("P5_Insignia", kit, s =>
                {
                    s.modo = P5TextStyle.Mode.Bloque; s.tamano = 24f; s.relleno = new Vector2(18f, 6f); s.grosorBorde = 5f; s.radio = 16f;
                    s.secuencia = new[] { Tile(J, W, K) }; s.sombras = noShadows;
                }),
                contador = Style("P5_Contador", kit, s =>
                {
                    s.modo = P5TextStyle.Mode.Bloque; s.tamano = 22f; s.relleno = new Vector2(10f, 2f); s.grosorBorde = 4f; s.radio = 12f;
                    s.secuencia = new[] { Tile(K, W, W) }; s.sombras = noShadows;
                }),
                interrogante = Style("P5_Interrogante", kit, s =>
                {
                    s.modo = P5TextStyle.Mode.SoloTexto; s.tamano = 60f; s.altoLinea = 1.2f;
                    s.secuencia = new[] { Tile(N, J) }; s.sombras = noShadows;
                }),
                pastilla = Style("P5_Pastilla", kit, s =>
                {
                    s.modo = P5TextStyle.Mode.Bloque; s.fuente = kit.lilita; s.tamano = 30f; s.altoLinea = 1.2f; s.relleno = new Vector2(28f, 6f);
                    s.grosorBorde = 6f; s.radio = 30f; s.secuencia = new[] { Tile(W, K, K) };
                    s.sombras = new[] { new P5ShadowLook { color = K, opacidad = .2f, desplazamiento = new Vector2(0f, -6f) } };
                }),
                timeFont = kit.lilita, reelFont = kit.dela, popFont = kit.dela, popMaterial = kit.delaOutline,
            };
            p5.bolsa = p5.contador;
            p5.interrogante.centradoOptico = true; // el «?» va justo en el centro de la caja
            kit.looks["p5s"] = p5;
            var calm = p5.Copy();
            calm.cabecera = Style("Limpio_Cabecera", kit, s =>
            {
                s.modo = P5TextStyle.Mode.SoloTexto; s.tamano = 24f; s.interletrado = 4f; s.altoLinea = 1.2f;
                s.secuencia = new[] { Tile(N, K) }; s.sombras = noShadows;
            });
            kit.looks["calm"] = calm;

            Color orange = Hex("#F47B20"), red = Hex("#EE2737");

            // ----- Rótulos de la tienda: carteles colgantes, rotulado a mano (Gochi Hand) y cifras en Lilita One -----
            {
                Color ink = Hex("#1d2340"), cream = Hex("#fff6e6");
                var tagShadow = Drop(ink, .22f, 12f);
                kit.looks["sig"] = new Look
                {
                    cabecera = Block("Rotulos_Cabecera", kit.gochi, 38f, new Vector2(24f, 4f), 6f, 16f, Tile(C, C, C, cream, ink, ink), noShadows),
                    apodo = Block("Rotulos_Apodo", kit.gochi, 42f, new Vector2(20f, 3f), 6f, 18f, Tile(C, C, C, cream, ink, ink), tagShadow),
                    chapa = Block("Rotulos_Chapa", kit.gochi, 42f, new Vector2(20f, 3f), 6f, 18f, Tile(J, C, C, null, cream, ink), tagShadow),
                    insignia = Block("Rotulos_Insignia", kit.gochi, 38f, new Vector2(22f, 4f), 6f, 16f, Tile(C, C, C, orange, cream, ink), noShadows),
                    contador = Block("Rotulos_Contador", kit.gochi, 36f, new Vector2(18f, 3f), 5f, 30f, Tile(C, C, C, red, cream, ink), noShadows),
                    interrogante = Plain("Rotulos_Interrogante", kit.lilita, 60f, Tile(N, J)),
                    pastilla = Block("Rotulos_Pastilla", kit.gochi, 38f, new Vector2(32f, 3f), 0f, 16f, Tile(C, C, N, ink, cream), noShadows),
                    bolsa = Block("Rotulos_Bolsa", kit.gochi, 34f, new Vector2(16f, 3f), 0f, 14f, Tile(C, C, N, ink, cream), noShadows),
                    timeFont = kit.lilita, timeSize = 128f, timeItalic = false, timeSpacing = 3.1f, timeShadows = false,
                    timeColor = ink, urgentTimeColor = cream,
                    reelFont = kit.lilita, reelSize = 64f, reelDark = ink, reelLight = cream,
                    popFont = kit.lilita, popSize = 68f, popColor = cream,
                    popMaterial = EnsureEffectMaterial(kit.lilita, "LilitaOne SDF Contorno Rotulos", m => Outline(m, .3f, ink)),
                    panelFx = HUDView.PanelFx.Swing,
                };
            }

            // ----- Ticket de caja: tique, etiquetas y visor de registradora (Space Mono y VT323) -----
            {
                Color ink = Hex("#24221f"), paper = Hex("#fbf7ee"), led = Hex("#7dffb8");
                var tagShadow = Drop(ink, .19f, 10f);
                kit.looks["tik"] = new Look
                {
                    cabecera = Block("Ticket_Cabecera", kit.spaceMono, 26f, new Vector2(20f, 2f), 0f, 0f, Tile(C, C, N, ink, paper), noShadows, 6f),
                    apodo = Block("Ticket_Apodo", kit.spaceMono, 30f, new Vector2(16f, 4f), 5f, 0f, Tile(C, C, C, paper, ink, ink), tagShadow, 2f),
                    chapa = Block("Ticket_Chapa", kit.spaceMono, 30f, new Vector2(16f, 4f), 5f, 0f, Tile(J, W, C, null, null, ink), tagShadow),
                    insignia = Block("Ticket_Insignia", kit.spaceMono, 26f, new Vector2(18f, 4f), 5f, 8f, Tile(C, W, C, orange, null, ink), noShadows),
                    contador = Block("Ticket_Contador", kit.spaceMono, 26f, new Vector2(16f, 2f), 5f, 26f, Tile(C, W, C, red, null, ink), noShadows),
                    interrogante = Plain("Ticket_Interrogante", kit.spaceMono, 60f, Tile(N, J)),
                    pastilla = Block("Ticket_Pastilla", kit.spaceMono, 26f, new Vector2(24f, 2f), 5f, 0f, Tile(C, C, C, paper, ink, ink), noShadows, 6f),
                    bolsa = Block("Ticket_Bolsa", kit.spaceMono, 26f, new Vector2(16f, 2f), 5f, 0f, Tile(C, C, C, paper, ink, ink), noShadows),
                    timeFont = kit.vt323, timeSize = 168f, timeItalic = false, timeSpacing = 0f, timeShadows = false,
                    timeColor = led, urgentTimeColor = Color.white,
                    timeMaterial = EnsureEffectMaterial(kit.vt323, "VT323 SDF Brillo", m => Glow(m, new Color(led.r, led.g, led.b, .55f))),
                    reelFont = kit.spaceMono, reelSize = 60f, reelDark = ink,
                    popFont = kit.spaceMono, popSize = 60f, popTint = true, popShadow = false,
                    popMaterial = EnsureEffectMaterial(kit.spaceMono, "SpaceMono SDF Contorno Ticket", m => Outline(m, .3f, paper)),
                    panelFx = HUDView.PanelFx.Print,
                };
            }

            // ----- Pegatinas: troqueladas y redondas (Sniglet, cifras en Lilita One) -----
            {
                Color ink = Hex("#2b2a35"), cream = Hex("#fffaf0");
                var tagShadow = Drop(ink, .15f, 10f);
                kit.looks["peg"] = new Look
                {
                    cabecera = Block("Pegatinas_Cabecera", kit.sniglet, 30f, new Vector2(22f, 2f), 5f, 30f, Tile(J, W, C, null, null, ink), noShadows, 2f),
                    apodo = Block("Pegatinas_Apodo", kit.sniglet, 34f, new Vector2(20f, 2f), 6f, 34f, Tile(C, C, C, cream, ink, ink), tagShadow),
                    chapa = Block("Pegatinas_Chapa", kit.sniglet, 34f, new Vector2(20f, 2f), 6f, 34f, Tile(J, W, C, null, null, ink), tagShadow),
                    insignia = Block("Pegatinas_Insignia", kit.sniglet, 30f, new Vector2(20f, 4f), 6f, 30f, Tile(C, W, C, orange, null, ink), noShadows),
                    contador = Block("Pegatinas_Contador", kit.sniglet, 30f, new Vector2(16f, 2f), 5f, 30f, Tile(C, W, C, red, null, ink), noShadows),
                    interrogante = Plain("Pegatinas_Interrogante", kit.sniglet, 60f, Tile(N, J)),
                    pastilla = Block("Pegatinas_Pastilla", kit.sniglet, 28f, new Vector2(28f, 4f), 6f, 30f, Tile(C, W, C, red, null, ink), noShadows),
                    bolsa = Block("Pegatinas_Bolsa", kit.sniglet, 28f, new Vector2(16f, 2f), 0f, 30f, Tile(C, W, N, ink), noShadows),
                    timeFont = kit.lilita, timeSize = 128f, timeItalic = false, timeSpacing = 1.6f, timeShadows = false,
                    timeColor = ink, urgentTimeColor = Color.white,
                    reelFont = kit.lilita, reelSize = 64f, reelDark = ink,
                    popFont = kit.lilita, popSize = 68f,
                    popMaterial = EnsureEffectMaterial(kit.lilita, "LilitaOne SDF Contorno Pegatinas", m => Outline(m, .3f, ink)),
                    panelFx = HUDView.PanelFx.Squash,
                };
            }

            // ----- Familia P5 pastel: el Soft del juego en pastel, sin negro puro (tinta de color), letras redondas, corona y
            // tiempo de Pegatinas. «accent» es el segundo color de las letras recortadas o el de las píldoras
            Look Pastel(string prefix, TMP_FontAsset font, string inkHex, string accentHex, string streakHex, bool cutLetters, HUDView.PanelFx fx,
                        string bagHex = "#ffe08a")
            {
                Color ink = Hex(inkHex), accent = Hex(accentHex), cream = Hex("#fff8ec"), coral = Hex("#ff7b86"), peach = Hex("#ffad70");
                var soft = Drop(ink, .15f, 8f);
                // Letras recortadas: blanco, el color de acento y el del jugador, siempre con tinta (ningún fondo negro)
                var letters = new[] { Tile(C, C, C, Color.white, ink, ink), Tile(C, C, C, accent, ink, ink), Tile(J, C, C, null, ink, ink) };
                P5TextStyle Letters(string name, float size, float maxWidth) => Style(name, kit, s =>
                {
                    s.modo = P5TextStyle.Mode.LetraPorFicha; s.fuente = font; s.tamano = size; s.relleno = new Vector2(8f, 0f);
                    s.altoLinea = 1.3f; s.grosorBorde = 4f; s.radio = 10f; s.secuencia = letters; s.sombras = soft; s.anchoMaximo = maxWidth;
                    // Letras juntas, casi solapadas como recortes pegados, y las filas del apodo pegadas
                    s.separacionLetras = -2f; s.separacionPalabras = 8f; s.separacionFilas = 2f;
                });
                P5TextStyle Pill(string name, float size, Color background, P5ShadowLook[] shadows) =>
                    Block(name, font, size, new Vector2(20f, 3f), 5f, 30f, Tile(C, C, C, background, ink, ink), shadows, 1f);
                return new Look
                {
                    cabecera = cutLetters ? Letters(prefix + "_Cabecera", 24f, 0f) : Pill(prefix + "_Cabecera", 26f, accent, noShadows),
                    apodo = cutLetters ? Letters(prefix + "_Apodo", 28f, 300f) : Pill(prefix + "_Apodo", 30f, Color.white, soft),
                    chapa = Style(prefix + "_Chapa", kit, s =>
                    {
                        s.modo = P5TextStyle.Mode.Bloque; s.fuente = font; s.tamano = cutLetters ? 40f : 34f; s.altoLinea = 1f;
                        s.relleno = new Vector2(14f, 6f); s.grosorBorde = cutLetters ? 6f : 5f; s.radio = cutLetters ? 18f : 30f;
                        s.secuencia = new[] { Tile(J, C, C, null, ink, ink) }; s.sombras = soft;
                    }),
                    insignia = Block(prefix + "_Insignia", font, 28f, new Vector2(20f, 4f), 6f, 30f, Tile(C, C, C, peach, ink, ink), noShadows),
                    contador = Block(prefix + "_Contador", font, 26f, new Vector2(16f, 2f), 5f, 30f, Tile(C, C, C, Hex(streakHex), ink, ink), noShadows),
                    interrogante = Plain(prefix + "_Interrogante", font, 60f, Tile(N, J)),
                    pastilla = Block(prefix + "_Pastilla", font, 28f, new Vector2(28f, 4f), 6f, 30f, Tile(C, C, C, coral, ink, ink), noShadows),
                    // Bolsa bien visible: chapa «BOLSA» y el número grande (amarillo, salvo en la variante blanca)
                    bolsa = Block(prefix + "_Bolsa", font, 44f, new Vector2(18f, 0f), 6f, 30f, Tile(C, C, C, Hex(bagHex), ink, ink), Drop(ink, .2f, 8f)),
                    bolsaChapa = Block(prefix + "_BolsaChapa", font, 24f, new Vector2(14f, 2f), 5f, 24f, Tile(C, C, C, cream, ink, ink), noShadows, 2f),
                    timeFont = font, timeSize = 124f, timeItalic = false, timeSpacing = 1.6f, timeShadows = false,
                    timeColor = ink, urgentTimeColor = Color.white,
                    reelFont = font, reelSize = 60f, reelDark = ink, reelLight = cream,
                    popFont = font, popSize = 64f,
                    popMaterial = EnsureEffectMaterial(font, $"{font.name} Contorno {prefix}", m => Outline(m, .3f, ink)),
                    panelFx = fx,
                    playerColors = new[] { Hex("#138266"), Hex("#E02735") }, // los que se pusieron a mano en el P5 soft
                    pastel = .35f,
                };
            }
            kit.looks["pst"] = Pastel("Pastel", kit.fredoka, "#4b4366", "#ffe08a", "#ff7b86", true, HUDView.PanelFx.Squash);
            // Chicle: gominola brillante, todo en píldoras (Titan One), tinta ciruela y menta
            kit.looks["pch"] = Pastel("Chicle", kit.titan, "#5b4163", "#a6ead0", "#a6ead0", false, HUDView.PanelFx.Squash);
            // Nuki: el azul de los carteles del concept (Baloo 2), tinta azul marino; el panel se balancea como un cartel
            kit.looks["pnu"] = Pastel("Nuki", kit.baloo, "#33406b", "#8fc7f2", "#8fc7f2", true, HUDView.PanelFx.Swing);
            // Reactivo: el P5 pastel con el retrato reaccionando a golpes, plátanos, volteretas, puñetazos, carreras y mareos
            var reactive = kit.looks["pst"].Copy();
            reactive.reactions = true;
            kit.looks["prx"] = reactive;
            // Reactivo blanco: sin amarillo; letras, rodillos y bolsa con el blanco de la UI (la corona y las estrellas siguen doradas)
            var white = Pastel("PastelBlanco", kit.fredoka, "#4b4366", "#ffffff", "#ff7b86", true, HUDView.PanelFx.Squash, "#ffffff");
            white.reactions = true;
            kit.looks["prw"] = white;
        }

        static P5TextStyle Style(string name, Kit kit, System.Action<P5TextStyle> setup)
        {
            string path = $"{StylesDir}/{name}.asset";
            var style = AssetDatabase.LoadAssetAtPath<P5TextStyle>(path);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<P5TextStyle>();
                setup(style);
                AssetDatabase.CreateAsset(style, path);
            }
            if (style.fuente == null) style.fuente = kit.dela;
            if (style.fichaRecta == null) style.fichaRecta = kit.tile;
            if (style.fichaRedonda == null) style.fichaRedonda = kit.round;
            EditorUtility.SetDirty(style);
            return style;
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = UILayer };
            var canvas = go.GetComponent<Canvas>();
            // Todos los Canvas en Screen Space - Camera; la cámara se la pone CanvasCameraBinder en runtime.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1f;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = .5f;
            var background = NewObject("Fondo provisional (solo para revisar)", go.transform);
            Stretch(background);
            var image = background.AddComponent<Image>();
            image.color = new Color32(0xA9, 0xA2, 0xA0, 0xFF);
            image.raycastTarget = false;
            return canvas;
        }

        // ---------- Prefab de una variante ----------

        static GameObject BuildPrefab(Manifest m, string dir, Kit kit, Transform builderCanvas)
        {
            // La raíz llena el Canvas en el que se ponga; sola en la escena es su propio Canvas a 2560×1440.
            // Se monta dentro de otro Canvas: si fuera un Canvas raíz, Unity le cambiaría el tamaño y la escala al guardar
            var root = NewObject("PF_HUD_" + m.name, builderCanvas);
            Stretch(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 10;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(m.canvasWidth, m.canvasHeight);
            scaler.matchWidthOrHeight = .5f;
            var colors = root.AddComponent<HUDPlayerColors>();
            var view = root.AddComponent<HUDView>();
            view.colors = colors;
            view.calm = m.variant == "calm";
            var look = kit.LookFor(m.variant);
            view.scorePanelFx = look.panelFx;
            view.timer.textColor = look.timeColor;
            view.timer.urgentTextColor = look.urgentTimeColor;
            if (look.playerColors != null)
            {
                colors.player1 = look.playerColors[0];
                colors.player2 = look.playerColors[1];
            }
            colors.pastel = look.pastel;
            view.reactions = look.reactions;

            var objects = new GameObject[m.nodes.Length];
            foreach (var n in m.nodes) // los padres siempre van antes que sus hijos
            {
                var parentNode = n.parent < 0 ? null : m.nodes[n.parent];
                var go = NewObject(n.name, parentNode == null ? root.transform : objects[n.parent].transform);
                objects[n.id] = go;
                if (parentNode == null) PlaceTop(go, n, m);
                else Place(go, n.x, n.y, n.w, n.h, parentNode);
                var pv = view.Player(n.player == 2 ? 2 : 1);
                if (parentNode == null && (n.name == "P1" || n.name == "P2")) pv.root = (RectTransform)go.transform;

                Image image = null;
                if (!string.IsNullOrEmpty(n.sprite) && !GeneratedRoles.Contains(n.role))
                {
                    image = go.AddComponent<Image>();
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/Sprites/{n.sprite}");
                    image.raycastTarget = false;
                    if (n.tint > 0) Tint(go, n.tint);
                }

                switch (n.role)
                {
                    case "stamina":
                        image.type = Image.Type.Filled;
                        image.fillMethod = Image.FillMethod.Radial360;
                        image.fillOrigin = (int)Image.Origin360.Top;
                        image.fillClockwise = false; // se vacía en sentido horario desde las 12
                        pv.staminaFill = image;
                        pv.staminaGlow = BuildGlow(go, image);
                        BuildStaminaExtras(go, image, pv);
                        break;
                    case "barFill":
                        image.type = Image.Type.Filled;
                        image.fillMethod = Image.FillMethod.Horizontal;
                        image.fillOrigin = (int)Image.OriginHorizontal.Left;
                        view.timer.barFill = image;
                        break;
                    case "urgent": view.timer.urgentSlab = go; break;
                    case "portraitBg": BuildIllustrationMask(go, pv); break;
                    case "timeText":
                    case "timeShadow": BuildTimeText(go, n, look, view); break;
                    case "reelDigit": BuildReel(go, n, m, dir, look, pv, objects); break;
                    case "fire": pv.fire = go; break;
                    case "fireFrame": pv.fireFrames[n.k - 1] = go; break;
                    case "score": pv.scorePanel = (RectTransform)go.transform; break;
                    case "itemMain": pv.mainItemBox = (RectTransform)go.transform; break;
                    case "itemSub": pv.reserveItemBox = (RectTransform)go.transform; break;
                    case "pop": BuildPop(go, n, look, pv); break;

                    // Textos con la herramienta P5 (sin imágenes)
                    case "header":
                        pv.scoreHeader = AddP5Centered(go, n, parentNode, "PUNTOS", look.cabecera, n.player, n.rot);
                        break;
                    case "tag": BuildTag(go, n, m, look, pv); break;
                    case "top":
                        go.name = "TOP";
                        AddP5Centered(go, n, parentNode, "★ TOP", look.insignia, n.player, n.rot);
                        pv.top = go;
                        break;
                    case "crown":
                    {
                        // Pivote en su base (donde se apoya en el aro) e inclinada colgando hacia el centro; la anima el presentador
                        var crown = (RectTransform)go.transform;
                        crown.pivot = new Vector2(.5f, .1f);
                        crown.anchoredPosition += new Vector2(0f, -.4f * crown.sizeDelta.y);
                        crown.localRotation = Quaternion.Euler(0f, 0f, n.rot);
                        pv.crown = crown;
                        pv.top = go;
                        break;
                    }
                    case "streak":
                        pv.streakCounter = AddP5Centered(go, n, parentNode, "x3", look.contador, n.player, 0f);
                        pv.streak = go;
                        break;
                    case "qMain":
                    case "qSub": BuildItemSlot(go, n, parentNode, m, dir, look, pv); break;
                    case "pill":
                        go.name = "TIEMPO";
                        view.timer.label = AddP5Centered(go, n, parentNode, "TIEMPO", look.pastilla, 0, 0f);
                        break;
                }
                if (!n.visible) go.SetActive(false);
            }

            for (int player = 1; player <= 2; player++)
            {
                var pv = view.Player(player);
                pv.mainSlot = GroupSlot("Principal", pv.mainItemBox, pv.mainItemEmpty, pv.mainItemIcon);
                pv.reserveSlot = GroupSlot("Reserva", pv.reserveItemBox, pv.reserveItemEmpty, pv.reserveItemIcon);
                BuildBag(m, look, pv, player);
                if (look.reactions) BuildReactionExtras(look, kit, pv, player);
                if (pv.root) SortPlayer(pv.root);
            }
            GroupTimeDigits(view.timer);

            root.AddComponent<HUDLivePortraits>();
            var controller = root.AddComponent<HUDController>();
            SetReference(controller, "view", view);
            foreach (var character in kit.characters)
                controller.Characters.Add(new HUDController.CharacterLabel { character = character, nickname = Nickname(character) });
            colors.Apply();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{root.name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void SortPlayer(Transform player)
        {
            int index = 0;
            foreach (var childName in PlayerOrder)
            {
                var child = player.Find(childName);
                if (child) child.SetSiblingIndex(index++);
            }
        }

        // Texto P5 centrado donde estaba la pieza del PSD (cuyo rect incluye la sombra: se descuenta)
        static P5Text AddP5Centered(GameObject go, Node n, Node parentNode, string value, P5TextStyle style, int player, float tilt, float scale = 1f)
        {
            var shadow = Vector2.zero;
            foreach (var s in style.sombras) shadow += s.desplazamiento;
            return AddP5At(go, new Vector2(n.x + n.w / 2f - shadow.x / 2f, n.y + n.h / 2f + shadow.y / 2f), parentNode, value, style, player, tilt, scale);
        }

        // Texto P5 con su centro en un punto del PSD (px, origen arriba a la izquierda)
        static P5Text AddP5At(GameObject go, Vector2 center, Node parentNode, string value, P5TextStyle style, int player, float tilt, float scale = 1f)
        {
            Place(go, center.x, center.y, 0f, 0f, parentNode);
            var text = go.AddComponent<P5Text>();
            text.style = style;
            text.player = player;
            text.inclinacion = tilt;
            text.escalaTexto = scale;
            text.SetText(value);
            return text;
        }

        // Etiqueta: chapa «Pn» y, si la variante lo muestra, el apodo. Cuelga de la esquina del HTML (P1 izquierda, P2 derecha)
        static void BuildTag(GameObject anchor, Node n, Manifest m, Look look, HUDView.PlayerView pv)
        {
            anchor.name = "Esquina";
            bool p2 = n.player == 2;
            var go = NewObject(m.nick ? "Chapa y apodo" : "Chapa", anchor.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(p2 ? 1f : 0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            var text = go.AddComponent<P5Text>();
            text.style = look.apodo;
            text.player = n.player;
            text.inclinacion = n.rot;
            text.alineacion = p2 ? P5Text.Alignment.Derecha : P5Text.Alignment.Izquierda;
            text.chapa = m.chip ? $"P{n.player}" : "";
            text.estiloChapa = look.chapa;
            text.chapaAlFinal = p2;
            text.SetText(m.nick ? (p2 ? "PANDA ROJO" : "MAPACHE") : "");
            pv.tag = text;
        }

        static void BuildItemSlot(GameObject go, Node n, Node parentNode, Manifest m, string dir, Look look, HUDView.PlayerView pv)
        {
            bool main = n.role == "qMain";
            go.name = main ? "? principal" : "? reserva";
            var center = FaceCenter(m, dir, n.player, main ? "itemMain" : "itemSub", new Vector2(n.x + n.w / 2f, n.y + n.h / 2f));
            AddP5At(go, center, parentNode, "?", look.interrogante, n.player, n.rot, main ? 1f : 2f / 3f);

            var icon = NewObject(main ? "Icono principal" : "Icono reserva", go.transform.parent);
            Place(icon, center.x - n.size / 2f, center.y - n.size / 2f, n.size, n.size, parentNode);
            icon.transform.localRotation = Quaternion.Euler(0f, 0f, n.rot);
            var image = icon.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false; // sin ítem se ve el «?»
            if (main) { pv.mainItemEmpty = go; pv.mainItemIcon = image; }
            else { pv.reserveItemEmpty = go; pv.reserveItemIcon = image; }
        }

        static void BuildIllustrationMask(GameObject background, HUDView.PlayerView pv)
        {
            background.AddComponent<Mask>().showMaskGraphic = true;
            var go = NewObject("Ilustración", background.transform);
            Stretch(go);
            var image = go.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false; // sin ilustración todavía
            pv.illustration = image;

            // Encima, el retrato en vivo de la cámara grúa (HUDLivePortraits)
            var live = NewObject("Retrato en vivo", background.transform);
            Stretch(live);
            var raw = live.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            pv.liveIllustration = raw;
        }

        static void BuildTimeText(GameObject go, Node n, Look look, HUDView view)
        {
            go.name = n.role == "timeText" ? "Tiempo" : $"Tiempo sombra P{n.tint}";
            if (n.role == "timeShadow" && !look.timeShadows)
            {
                go.SetActive(false); // esta variante no lleva las sombras de color en el tiempo
                return;
            }
            var text = AddText(go, look.timeFont, look.timeSize, n.role == "timeText" ? look.timeColor : Color.white, "5:00");
            text.fontStyle = look.timeItalic ? FontStyles.Italic : FontStyles.Normal;
            text.characterSpacing = look.timeSpacing;
            if (n.role == "timeText")
            {
                if (look.timeMaterial) text.fontSharedMaterial = look.timeMaterial;
                view.timer.time = text;
                return;
            }
            Tint(go, n.tint);
            if (n.tint == 1) view.timer.shadowP1 = text;
            else view.timer.shadowP2 = text;
        }

        // Rodillo: máscara con la forma de la ficha → eje inclinado → tira 0-9,0-9 que se desplaza en vertical
        static void BuildReel(GameObject go, Node n, Manifest m, string dir, Look look, HUDView.PlayerView pv, GameObject[] objects)
        {
            var reel = m.nodes.First(r => r.parent == n.parent && r.role == "reel" && r.k == n.k);
            var parentNode = n.parent < 0 ? null : m.nodes[n.parent];
            go.name = $"Rodillo {n.k} Dígitos";
            Place(go, reel.x, reel.y, reel.w, reel.h, parentNode);
            string stencil = !string.IsNullOrEmpty(reel.sprite) ? reel.sprite
                : m.nodes.Where(c => c.parent == reel.id && !string.IsNullOrEmpty(c.sprite)).OrderByDescending(c => c.tint).First().sprite;
            var mask = go.AddComponent<Image>();
            mask.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/Sprites/{stencil}");
            mask.raycastTarget = false;
            go.AddComponent<Mask>().showMaskGraphic = false;
            // Dentro de su ficha: se balancean y saltan juntas
            var tile = objects[reel.id];
            go.transform.SetParent(tile.transform, true);
            pv.reels[n.k - 1] = (RectTransform)tile.transform;

            var axis = NewObject("Eje", go.transform);
            var axisRect = (RectTransform)axis.transform;
            axisRect.sizeDelta = new Vector2(64f, ReelPitch);
            axisRect.localRotation = Quaternion.Euler(0f, 0f, n.rot);

            var strip = NewObject("Tira", axis.transform);
            var stripRect = (RectTransform)strip.transform;
            stripRect.pivot = new Vector2(.5f, 1f);
            stripRect.sizeDelta = new Vector2(64f, ReelPitch * ReelCells);
            stripRect.anchoredPosition = new Vector2(0f, ReelOffset);
            var color = n.color == "#FFFFFF" ? look.reelLight : look.reelDark;
            for (int i = 0; i < ReelCells; i++)
            {
                var cell = NewObject((i % 10).ToString(), strip.transform);
                var rt = (RectTransform)cell.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(.5f, 1f);
                rt.sizeDelta = new Vector2(0f, ReelPitch);
                rt.anchoredPosition = new Vector2(0f, -i * ReelPitch);
                AddText(cell, look.reelFont, look.reelSize, color, (i % 10).ToString());
                if (n.tint > 0) Tint(cell, n.tint); // cifras del color del jugador
            }
            pv.reelStrips[n.k - 1] = stripRect;
            pv.reelPitch = ReelPitch;
            pv.reelOffset = ReelOffset;
        }

        static void BuildPop(GameObject go, Node n, Look look, HUDView.PlayerView pv)
        {
            go.name = "+N (plantilla)";
            TMP_Text shadowText = null;
            if (look.popShadow)
            {
                var shadow = NewObject("Sombra", go.transform);
                Stretch(shadow, new Vector2(3f, -3f));
                shadowText = AddText(shadow, look.popFont, look.popSize, Color.white, "+120");
                Tint(shadow, n.player);
            }
            var main = NewObject("Texto", go.transform);
            Stretch(main, look.popShadow ? new Vector2(-3f, 3f) : Vector2.zero);
            var mainText = AddText(main, look.popFont, look.popSize, look.popColor, "+120");
            if (look.popMaterial) mainText.fontSharedMaterial = look.popMaterial;
            if (look.popTint) Tint(main, n.player);
            pv.popTemplate = (RectTransform)go.transform;
            pv.popText = mainText;
            pv.popShadow = shadowText;
        }

        // Copia del aro de estamina, detrás y algo mayor: brilla con el color del jugador durante la racha
        static Image BuildGlow(GameObject ring, Image fill) => CloneRing(ring, fill, "Brillo racha", new Color(1f, 1f, 1f, .5f), 1.06f, false);

        // Estela: copia blanca detrás del aro con lo que se acaba de gastar
        static void BuildStaminaExtras(GameObject ring, Image fill, HUDView.PlayerView pv)
        {
            var trail = CloneRing(ring, fill, "Estela", new Color(1f, 1f, 1f, .9f), 1f, true);
            trail.enabled = false;
            pv.staminaTrail = trail;
        }

        static Image CloneRing(GameObject ring, Image fill, string name, Color color, float scale, bool keepActive)
        {
            var go = NewObject(name, ring.transform.parent);
            go.transform.SetSiblingIndex(ring.transform.GetSiblingIndex());
            var source = (RectTransform)ring.transform;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = source.anchorMin;
            rt.anchorMax = source.anchorMax;
            rt.pivot = source.pivot;
            rt.sizeDelta = source.sizeDelta;
            rt.anchoredPosition = source.anchoredPosition;
            rt.localScale = new Vector3(scale, scale, 1f);
            var image = go.AddComponent<Image>();
            image.sprite = fill.sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = fill.fillMethod;
            image.fillOrigin = fill.fillOrigin;
            image.fillClockwise = fill.fillClockwise;
            image.raycastTarget = false;
            image.color = color;
            if (!keepActive) go.SetActive(false);
            return image;
        }

        // Centro de la cara blanca de una caja de ítem (sin contar sus sombras), en px del PSD
        static Vector2 FaceCenter(Manifest m, string dir, int player, string boxRole, Vector2 fallback)
        {
            var box = m.nodes.FirstOrDefault(n => n.player == player && n.role == boxRole);
            if (box == null) return fallback;
            var face = m.nodes.FirstOrDefault(n => n.parent == box.id && n.name == "Base" && !string.IsNullOrEmpty(n.sprite));
            var layer = face ?? (string.IsNullOrEmpty(box.sprite) ? null : box);
            if (layer == null || !TryLoadPixels($"{dir}/Sprites/{layer.sprite}", out var pixels, out int w, out int h)) return fallback;
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = pixels[y * w + x];
                    if (c.a < 230 || c.r < 230 || c.g < 230 || c.b < 230) continue;
                    sx += x + .5;
                    sy += y + .5;
                    count++;
                }
            if (count == 0) return fallback;
            return new Vector2(layer.x + (float)(sx / count) * layer.w / w, layer.y + (h - (float)(sy / count)) * layer.h / h);
        }

        static bool TryLoadPixels(string assetPath, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            width = height = 0;
            if (!File.Exists(assetPath)) return false;
            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(assetPath))) return false;
                pixels = texture.GetPixels32();
                width = texture.width;
                height = texture.height;
                return true;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // Caja, «?» e icono en un mismo grupo centrado en el icono: flota, gira y hace la entrada de una pieza
        static RectTransform GroupSlot(string name, RectTransform box, GameObject empty, Image icon)
        {
            if (!box || !icon) return null;
            var slot = NewObject(name, box.parent);
            var rt = (RectTransform)slot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = box.sizeDelta;
            rt.anchoredPosition = icon.rectTransform.anchoredPosition;
            slot.transform.SetSiblingIndex(box.GetSiblingIndex());
            box.SetParent(rt, true);
            if (empty) empty.transform.SetParent(rt, true);
            icon.transform.SetParent(rt, true);
            return rt;
        }

        // «BOLSA +N»: puntos sin vender, junto a la caja de reserva por el lado del centro de la pantalla
        static void BuildBag(Manifest m, Look look, HUDView.PlayerView pv, int player)
        {
            var box = m.nodes.FirstOrDefault(n => n.player == player && n.role == "itemSub");
            var playerNode = m.nodes.FirstOrDefault(n => n.parent < 0 && n.name == "P" + player);
            if (box == null || playerNode == null || !pv.root) return;
            bool p2 = player == 2;
            var go = NewObject("Bolsa", pv.root);
            Place(go, p2 ? box.x - 18f : box.x + box.w + 18f, box.y + box.h / 2f, 0f, 0f, playerNode);
            ((RectTransform)go.transform).pivot = new Vector2(p2 ? 1f : 0f, .5f);
            var text = go.AddComponent<P5Text>();
            text.style = look.bolsa;
            text.player = player;
            text.alineacion = p2 ? P5Text.Alignment.Derecha : P5Text.Alignment.Izquierda;
            if (look.bolsaChapa)
            {
                // Compacta: «BOLSA» en su chapa encima del número, montada un poco sobre él
                text.chapa = "BOLSA";
                text.estiloChapa = look.bolsaChapa;
                text.chapaArriba = true;
                text.separacionChapa = -6f;
                text.alineacion = P5Text.Alignment.Centro;
                text.SetText("+40");
            }
            else
            {
                text.escalaTexto = 1.25f;
                text.SetText("BOLSA +40");
            }
            go.SetActive(false);
            pv.bag = text;
        }

        // Variante reactiva: rayas de velocidad detrás del retrato y estrellas de mareo encima (ocultas; las anima el presentador)
        static void BuildReactionExtras(Look look, Kit kit, HUDView.PlayerView pv, int player)
        {
            var portrait = pv.staminaFill ? pv.staminaFill.rectTransform.parent as RectTransform : null;
            if (!portrait || !pv.root || portrait.parent != pv.root) return;
            float lean = player == 1 ? 1f : -1f;
            RectTransform Group(string name, Vector2 offset)
            {
                var rt = (RectTransform)NewObject(name, pv.root).transform;
                rt.anchorMin = rt.anchorMax = portrait.anchorMin;
                rt.pivot = new Vector2(.5f, .5f);
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = portrait.anchoredPosition + offset;
                return rt;
            }

            var speed = Group("Velocidad", Vector2.zero);
            pv.speedLines = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                var line = (RectTransform)NewObject($"Raya {i + 1}", speed).transform;
                line.pivot = new Vector2(lean > 0f ? 1f : 0f, .5f); // crece hacia fuera
                line.sizeDelta = new Vector2(80f - 16f * i, 14f);
                line.anchoredPosition = new Vector2(-lean * 150f, (i - 1) * 46f);
                var image = line.gameObject.AddComponent<Image>();
                image.sprite = kit.round;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = P5TextStyle.SpriteRadius / 7f;
                image.color = Hex("#fff8ec");
                image.raycastTarget = false;
                var outline = line.gameObject.AddComponent<Outline>();
                outline.effectColor = look.reelDark;
                outline.effectDistance = new Vector2(3f, -3f);
                pv.speedLines[i] = line;
            }
            speed.gameObject.SetActive(false);

            var dizzy = Group("Mareo", new Vector2(0f, portrait.sizeDelta.y * .4f));
            pv.dizzyStars = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                var star = NewObject($"Estrella {i + 1}", dizzy);
                ((RectTransform)star.transform).sizeDelta = new Vector2(60f, 60f);
                var text = AddText(star, look.popFont, 52f, Hex("#ffd36e"), "★");
                if (look.popMaterial) text.fontSharedMaterial = look.popMaterial;
                pv.dizzyStars[i] = (RectTransform)star.transform;
            }
            dizzy.gameObject.SetActive(false);
        }

        // Texto del tiempo y sus dos sombras en un grupo (de abajo arriba: P2, P1, blanco) que late entero
        static void GroupTimeDigits(HUDView.TimerView timer)
        {
            if (!timer.time) return;
            var time = timer.time.rectTransform;
            var go = NewObject("Dígitos", time.parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = time.sizeDelta;
            rt.anchoredPosition = time.anchoredPosition;
            var texts = new[] { timer.shadowP2, timer.shadowP1, timer.time }.Where(t => t).ToList();
            go.transform.SetSiblingIndex(texts.Min(t => t.transform.GetSiblingIndex()));
            foreach (var text in texts) text.transform.SetParent(rt, true);
            timer.digits = rt;
        }

        static string Nickname(PlayerCharacterDefinition character)
        {
            string prefab = character.prefab ? character.prefab.name : character.name;
            foreach (var (key, nickname) in Nicknames)
                if (prefab.Contains(key)) return nickname;
            return string.IsNullOrEmpty(character.displayName) ? "" : character.displayName.ToUpperInvariant();
        }

        static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Utilidades ----------

        // Grupos de primer nivel anclados a su lado de la pantalla (P1 izquierda, P2 derecha, tiempo al centro),
        // para que sigan en su sitio con otras proporciones de pantalla
        static void PlaceTop(GameObject go, Node n, Manifest m)
        {
            float anchor = n.name == "P1" ? 0f : n.name == "P2" ? 1f : .5f;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(anchor, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(n.w, n.h);
            rt.anchoredPosition = new Vector2(n.x + n.w / 2f - anchor * m.canvasWidth, -(n.y + n.h / 2f));
        }

        static GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        // Coloca un nodo con su rect del PSD (px, origen arriba a la izquierda) dentro del rect de su padre
        static void Place(GameObject go, float x, float y, float w, float h, Node parent)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x - (parent?.x ?? 0) + w / 2f, -(y - (parent?.y ?? 0) + h / 2f));
        }

        static void Stretch(GameObject go, Vector2 offset = default)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offset;
            rt.offsetMax = offset;
        }

        static void Tint(GameObject go, int player) => go.AddComponent<HUDTint>().player = player;

        static TextMeshProUGUI AddText(GameObject go, TMP_FontAsset font, float size, Color color, string value)
        {
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = TextAlignmentOptions.CenterGeoAligned;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        // ---------- Capturas de revisión (Temp/HUD_Revision/<fecha>) ----------

        static void RenderPreviews(Canvas canvas, List<GameObject> instances)
        {
            // Carpeta nueva por montaje: así nunca choca con un PNG abierto en un visor o bloqueado por OneDrive
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/HUD_Revision", System.DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(dir);
            var cameraObject = new GameObject("Cámara revisión");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.gray;
            camera.cullingMask = 1 << UILayer;
            camera.orthographic = true;
            var target = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            try
            {
                foreach (var instance in instances)
                {
                    foreach (var other in instances) other.SetActive(other == instance);
                    var view = instance.GetComponent<HUDView>();
                    Capture(camera, target, Path.Combine(dir, instance.name + ".png"));
                    SetHighlights(view, true);
                    Capture(camera, target, Path.Combine(dir, instance.name + "_TOP_racha_+N.png"));
                    SetHighlights(view, false);
                }
                Debug.Log($"HUD: capturas de revisión en {dir}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"HUD: no se pudieron generar las capturas de revisión: {e.Message}");
            }
            finally
            {
                canvas.worldCamera = null;
                canvas.planeDistance = 1f;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static void Capture(Camera camera, RenderTexture target, string file)
        {
            Canvas.ForceUpdateCanvases();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(file, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // Llama, racha, TOP, brillo del aro, bolsa y «+N» (ocultos en el prefab): se activan solo para la captura
        static void SetHighlights(HUDView view, bool on)
        {
            foreach (var player in new[] { view.player1, view.player2 })
            {
                if (player.fire) player.fire.SetActive(on);
                if (player.streak) player.streak.SetActive(on);
                if (player.top) player.top.SetActive(on);
                if (player.staminaGlow) player.staminaGlow.gameObject.SetActive(on);
                if (player.bag) player.bag.gameObject.SetActive(on);
                if (player.popTemplate)
                {
                    player.popTemplate.parent.gameObject.SetActive(on);
                    player.popTemplate.gameObject.SetActive(on);
                }
            }
        }
    }
}
