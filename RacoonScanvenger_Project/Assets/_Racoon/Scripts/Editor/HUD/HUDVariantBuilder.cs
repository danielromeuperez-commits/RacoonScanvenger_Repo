using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Orden de dibujo dentro de cada jugador (de abajo arriba): las insignias siempre por encima de los paneles
        static readonly string[] PlayerOrder = { "Llama (racha)", "Retrato", "Puntos", "Ítems", "Etiqueta", "Racha", "TOP", "Ejemplos (texto dinámico)" };
        // Piezas que se montan aquí (TMP, P5Text, máscaras…) aunque el manifiesto traiga una imagen de muestra
        static readonly HashSet<string> GeneratedRoles = new() { "reelDigit", "timeText", "timeShadow", "pop", "header", "tag", "top", "streak", "qMain", "qSub", "pill" };

#pragma warning disable 0649 // los rellena JsonUtility
        [System.Serializable]
        class Node
        {
            public int id, parent, x, y, w, h, tint, player, k;
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
            public TMP_FontAsset dela, lilita;
            public Material delaOutline;
            public Sprite tile, round;
            public P5TextStyle apodo, cabecera, cabeceraLimpia, chapa, insignia, contador, interrogante, pastilla;
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
                var instances = new List<GameObject>();
                foreach (var path in manifests)
                {
                    try // una variante con problemas no impide montar las demás
                    {
                        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                        var prefab = BuildPrefab(manifest, Path.GetDirectoryName(path).Replace('\\', '/'), kit);
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
                round = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesRoot}/_Comun/9s24_FichaRedonda.png"),
            };
            kit.delaOutline = EnsureOutlineMaterial(kit.dela);
            EnsureStyles(kit);
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

        static Material EnsureOutlineMaterial(TMP_FontAsset font)
        {
            string path = $"{FontsDir}/DelaGothicOne SDF Contorno.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.CopyPropertiesFromMaterial(font.material);
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, .3f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Estilos P5 del mockup (px a 2x). Solo se crean si no existen: si los retocas, el montaje los respeta.
        static void EnsureStyles(Kit kit)
        {
            EnsureFolder(StylesDir);
            var noShadows = new P5ShadowLook[0];
            P5TileLook Look(P5Paint fondo, P5Paint texto, P5Paint borde = P5Paint.Ninguno) => new() { fondo = fondo, texto = texto, borde = borde };

            kit.apodo = Style("P5_Apodo", kit, s => { });
            kit.cabecera = Style("P5_Cabecera", kit, s => { s.tamano = 24f; s.radio = 8f; s.anchoMaximo = 0f; });
            kit.cabeceraLimpia = Style("Limpio_Cabecera", kit, s =>
            {
                s.modo = P5TextStyle.Mode.SoloTexto; s.tamano = 24f; s.interletrado = 4f; s.altoLinea = 1.2f;
                s.secuencia = new[] { Look(P5Paint.Ninguno, P5Paint.Negro) }; s.sombras = noShadows;
            });
            kit.chapa = Style("P5_Chapa", kit, s =>
            {
                s.modo = P5TextStyle.Mode.Bloque; s.tamano = 40f; s.altoLinea = 1f; s.relleno = new Vector2(14f, 6f); s.grosorBorde = 6f;
                s.secuencia = new[] { Look(P5Paint.ColorJugador, P5Paint.Blanco, P5Paint.Negro) };
            });
            kit.insignia = Style("P5_Insignia", kit, s =>
            {
                s.modo = P5TextStyle.Mode.Bloque; s.tamano = 24f; s.relleno = new Vector2(18f, 6f); s.grosorBorde = 5f; s.radio = 16f;
                s.secuencia = new[] { Look(P5Paint.ColorJugador, P5Paint.Blanco, P5Paint.Negro) }; s.sombras = noShadows;
            });
            kit.contador = Style("P5_Contador", kit, s =>
            {
                s.modo = P5TextStyle.Mode.Bloque; s.tamano = 22f; s.relleno = new Vector2(10f, 2f); s.grosorBorde = 4f; s.radio = 12f;
                s.secuencia = new[] { Look(P5Paint.Negro, P5Paint.Blanco, P5Paint.Blanco) }; s.sombras = noShadows;
            });
            kit.interrogante = Style("P5_Interrogante", kit, s =>
            {
                s.modo = P5TextStyle.Mode.SoloTexto; s.tamano = 60f; s.altoLinea = 1.2f;
                s.secuencia = new[] { Look(P5Paint.Ninguno, P5Paint.ColorJugador) }; s.sombras = noShadows;
            });
            kit.pastilla = Style("P5_Pastilla", kit, s =>
            {
                s.modo = P5TextStyle.Mode.Bloque; s.fuente = kit.lilita; s.tamano = 30f; s.altoLinea = 1.2f; s.relleno = new Vector2(28f, 6f);
                s.grosorBorde = 6f; s.radio = 30f; s.secuencia = new[] { Look(P5Paint.Blanco, P5Paint.Negro, P5Paint.Negro) };
                s.sombras = new[] { new P5ShadowLook { color = P5Paint.Negro, opacidad = .2f, desplazamiento = new Vector2(0f, -6f) } };
            });
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        static GameObject BuildPrefab(Manifest m, string dir, Kit kit)
        {
            var root = NewObject("PF_HUD_" + m.name, null);
            ((RectTransform)root.transform).sizeDelta = new Vector2(m.canvasWidth, m.canvasHeight);
            var colors = root.AddComponent<HUDPlayerColors>();
            var view = root.AddComponent<HUDView>();
            view.colors = colors;

            var objects = new GameObject[m.nodes.Length];
            foreach (var n in m.nodes) // los padres siempre van antes que sus hijos
            {
                var parentNode = n.parent < 0 ? null : m.nodes[n.parent];
                var go = NewObject(n.name, parentNode == null ? root.transform : objects[n.parent].transform);
                objects[n.id] = go;
                Place(go, n.x, n.y, n.w, n.h, parentNode);
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
                    case "timeShadow": BuildTimeText(go, n, kit, view); break;
                    case "reelDigit": BuildReel(go, n, m, dir, kit, pv); break;
                    case "fire": pv.fire = go; break;
                    case "fireFrame": pv.fireFrames[n.k - 1] = go; break;
                    case "score": pv.scorePanel = (RectTransform)go.transform; break;
                    case "itemMain": pv.mainItemBox = (RectTransform)go.transform; break;
                    case "itemSub": pv.reserveItemBox = (RectTransform)go.transform; break;
                    case "pop": BuildPop(go, n, kit, pv); break;

                    // Textos con la herramienta P5 (sin imágenes)
                    case "header":
                        pv.scoreHeader = AddP5Centered(go, n, parentNode, "PUNTOS", m.variant == "calm" ? kit.cabeceraLimpia : kit.cabecera, n.player, n.rot);
                        break;
                    case "tag": BuildTag(go, n, m, kit, pv); break;
                    case "top":
                        go.name = "TOP";
                        AddP5Centered(go, n, parentNode, "★ TOP", kit.insignia, n.player, n.rot);
                        pv.top = go;
                        break;
                    case "streak":
                        pv.streakCounter = AddP5Centered(go, n, parentNode, "x3", kit.contador, n.player, 0f);
                        pv.streak = go;
                        break;
                    case "qMain":
                    case "qSub": BuildItemSlot(go, n, parentNode, kit, pv); break;
                    case "pill":
                        go.name = "TIEMPO";
                        view.timer.label = AddP5Centered(go, n, parentNode, "TIEMPO", kit.pastilla, 0, 0f);
                        break;
                }
                if (!n.visible) go.SetActive(false);
            }

            foreach (var player in new[] { view.player1.root, view.player2.root })
                if (player) SortPlayer(player);
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
            Place(go, n.x + n.w / 2f - shadow.x / 2f, n.y + n.h / 2f + shadow.y / 2f, 0f, 0f, parentNode);
            var text = go.AddComponent<P5Text>();
            text.style = style;
            text.player = player;
            text.inclinacion = tilt;
            text.escalaTexto = scale;
            text.SetText(value);
            return text;
        }

        // Etiqueta: chapa «Pn» y, si la variante lo muestra, el apodo. Cuelga de la esquina del HTML (P1 izquierda, P2 derecha)
        static void BuildTag(GameObject anchor, Node n, Manifest m, Kit kit, HUDView.PlayerView pv)
        {
            anchor.name = "Esquina";
            bool p2 = n.player == 2;
            var go = NewObject(m.nick ? "Chapa y apodo" : "Chapa", anchor.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(p2 ? 1f : 0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            var text = go.AddComponent<P5Text>();
            text.style = kit.apodo;
            text.player = n.player;
            text.inclinacion = n.rot;
            text.alineacion = p2 ? P5Text.Alignment.Derecha : P5Text.Alignment.Izquierda;
            text.chapa = m.chip ? $"P{n.player}" : "";
            text.estiloChapa = kit.chapa;
            text.chapaAlFinal = p2;
            text.SetText(m.nick ? (p2 ? "PANDA ROJO" : "MAPACHE") : "");
            pv.tag = text;
        }

        static void BuildItemSlot(GameObject go, Node n, Node parentNode, Kit kit, HUDView.PlayerView pv)
        {
            bool main = n.role == "qMain";
            go.name = main ? "? principal" : "? reserva";
            AddP5Centered(go, n, parentNode, "?", kit.interrogante, n.player, n.rot, main ? 1f : 2f / 3f);

            var icon = NewObject(main ? "Icono principal" : "Icono reserva", go.transform.parent);
            Place(icon, n.x + n.w / 2f - n.size / 2f, n.y + n.h / 2f - n.size / 2f, n.size, n.size, parentNode);
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
        }

        static void BuildTimeText(GameObject go, Node n, Kit kit, HUDView view)
        {
            go.name = n.role == "timeText" ? "Tiempo" : $"Tiempo sombra P{n.tint}";
            var text = AddText(go, kit.lilita, 120f, Color.white, "5:00");
            text.fontStyle = FontStyles.Italic;
            text.characterSpacing = 3.3f; // 2 px CSS a 120 px
            if (n.role == "timeText")
            {
                view.timer.time = text;
                return;
            }
            Tint(go, n.tint);
            if (n.tint == 1) view.timer.shadowP1 = text;
            else view.timer.shadowP2 = text;
        }

        // Rodillo: máscara con la forma de la ficha → eje inclinado → tira 0-9,0-9 que se desplaza en vertical
        static void BuildReel(GameObject go, Node n, Manifest m, string dir, Kit kit, HUDView.PlayerView pv)
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

            var axis = NewObject("Eje", go.transform);
            var axisRect = (RectTransform)axis.transform;
            axisRect.sizeDelta = new Vector2(64f, ReelPitch);
            axisRect.localRotation = Quaternion.Euler(0f, 0f, n.rot);

            var strip = NewObject("Tira", axis.transform);
            var stripRect = (RectTransform)strip.transform;
            stripRect.pivot = new Vector2(.5f, 1f);
            stripRect.sizeDelta = new Vector2(64f, ReelPitch * 20f);
            stripRect.anchoredPosition = new Vector2(0f, ReelOffset);
            ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(n.color) ? "#000000" : n.color, out var color);
            for (int i = 0; i < 20; i++)
            {
                var cell = NewObject((i % 10).ToString(), strip.transform);
                var rt = (RectTransform)cell.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(.5f, 1f);
                rt.sizeDelta = new Vector2(0f, ReelPitch);
                rt.anchoredPosition = new Vector2(0f, -i * ReelPitch);
                AddText(cell, kit.dela, 52f, color, (i % 10).ToString());
            }
            pv.reelStrips[n.k - 1] = stripRect;
            pv.reelPitch = ReelPitch;
            pv.reelOffset = ReelOffset;
        }

        static void BuildPop(GameObject go, Node n, Kit kit, HUDView.PlayerView pv)
        {
            go.name = "+N (plantilla)";
            var shadow = NewObject("Sombra", go.transform);
            Stretch(shadow, new Vector2(3f, -3f));
            var shadowText = AddText(shadow, kit.dela, 60f, Color.white, "+120");
            Tint(shadow, n.player);
            var main = NewObject("Texto", go.transform);
            Stretch(main, new Vector2(-3f, 3f));
            var mainText = AddText(main, kit.dela, 60f, Color.white, "+120");
            mainText.fontSharedMaterial = kit.delaOutline;
            pv.popTemplate = (RectTransform)go.transform;
            pv.popText = mainText;
            pv.popShadow = shadowText;
        }

        // ---------- Utilidades ----------

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
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        // Llama, racha, TOP y «+N» (ocultos en el prefab): se activan solo para la captura
        static void SetHighlights(HUDView view, bool on)
        {
            foreach (var player in new[] { view.player1, view.player2 })
            {
                if (player.fire) player.fire.SetActive(on);
                if (player.streak) player.streak.SetActive(on);
                if (player.top) player.top.SetActive(on);
                if (player.popTemplate)
                {
                    player.popTemplate.parent.gameObject.SetActive(on);
                    player.popTemplate.gameObject.SetActive(on);
                }
            }
        }
    }
}
