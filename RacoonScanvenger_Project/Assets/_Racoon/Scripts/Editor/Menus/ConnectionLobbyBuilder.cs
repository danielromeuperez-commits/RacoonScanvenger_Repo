using System.Collections.Generic;
using System.IO;
using System.Linq;
using Racoon.Gameplay;
using Racoon.Network;
using Racoon.UI;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Monta el menú de conexión + selección de personaje maquetado (estilo del HUD pastel: Fredoka, tinta #4b4366,
    /// fichas redondeadas y el rojo de la tienda siempre presente):
    ///  - PF_UI_ConnectionLobby: Canvas (Screen Space - Camera) con ConnectionMenu y todos sus paneles conectados.
    ///  - PF_MNG_CharacterLobbyManager: NetworkObject con CharacterLobbyManager (va colocado en la escena).
    /// Los prefabs se rehacen enteros cada vez: no hay que retocarlos a mano.
    /// Deja capturas de cada panel en Temp/Lobby_Revision/&lt;fecha&gt;.
    /// «Añadir lobby a la escena abierta» mete los dos prefabs (y un EventSystem si falta) y apaga el ConnectionMenu antiguo.
    /// </summary>
    [InitializeOnLoad]
    public static class ConnectionLobbyBuilder
    {
        const string PrefabDir = "Assets/_Racoon/Prefabs/UI/Menus";
        const string ManagersDir = "Assets/_Racoon/Prefabs/Managers";
        const string LobbyPrefabPath = PrefabDir + "/PF_UI_ConnectionLobby.prefab";
        const string ManagerPrefabPath = ManagersDir + "/PF_MNG_CharacterLobbyManager.prefab";
        const string FontsDir = "Assets/_Racoon/2D/Fonts";
        const string RoundSpritePath = "Assets/_Racoon/2D/Sprites/UI/HUD/_Comun/9s96_FichaRedonda.png";
        // Pedido de montaje sin pasar por el menú (lo crea una herramienta externa; se borra al usarlo)
        const string RequestFile = "Temp/racoon_build_lobby.request";
        const float RefWidth = 1920f, RefHeight = 1080f;

        // Paleta: la del HUD pastel + los colores de la tienda
        static readonly Color Ink = Hex("#4b4366");
        static readonly Color Cream = Hex("#fff6e9");
        static readonly Color StoreRed = Hex("#ee2737");
        static readonly Color StoreOrange = Hex("#f7954a");
        static readonly Color SignBlue = Hex("#5b8def");
        static readonly Color ReadyGreen = Hex("#3fb27f");
        static readonly Color Mute = Hex("#9a93b5");

        // Color de cada personaje (windTint de sus prefabs), por nombre del prefab
        static readonly (string key, string color)[] CharacterTints = { ("Raccoon", "#395CF5"), ("Racoon", "#395CF5"), ("Opossum", "#39F576"), ("RedPanda", "#F53F39") };

        class Kit
        {
            public TMP_FontAsset font;
            public Material outline;
            public Sprite round;
        }

        static ConnectionLobbyBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(RequestFile)) return;
                File.Delete(RequestFile);
                Build();
            };
        }

        static int UILayer => LayerMask.NameToLayer("UI");

        static Color Hex(string html) => ColorUtility.TryParseHtmlString(html, out var color) ? color : Color.magenta;

        // ============================================================
        // MONTAJE
        // ============================================================

        [MenuItem("Racoon/Menús/Montar lobby de conexión")]
        public static void Build()
        {
            var kit = new Kit
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontsDir}/Fredoka SDF.asset"),
                outline = AssetDatabase.LoadAssetAtPath<Material>($"{FontsDir}/Fredoka SDF Contorno Pastel.mat"),
                round = AssetDatabase.LoadAssetAtPath<Sprite>(RoundSpritePath),
            };
            if (!kit.font || !kit.round)
            {
                Debug.LogError("Lobby: falta Fredoka SDF o la ficha redonda. Monta antes el HUD (Racoon → HUD → Montar variantes).");
                return;
            }

            EnsureFolder(PrefabDir);
            BuildManagerPrefab();

            var root = BuildLobby(kit, out var menu, out var panels);
            try
            {
                RenderPreviews(root, menu, panels);
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
                Debug.Log($"Lobby: montado {LobbyPrefabPath} y {ManagerPrefabPath}. Para usarlo: Racoon → Menús → Añadir lobby a la escena abierta.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void BuildManagerPrefab()
        {
            var go = new GameObject("PF_MNG_CharacterLobbyManager");
            try
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<CharacterLobbyManager>();
                PrefabUtility.SaveAsPrefabAsset(go, ManagerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        class Panels
        {
            public GameObject mode, local, online, waiting, character, status;
        }

        static GameObject BuildLobby(Kit kit, out ConnectionMenu menu, out Panels panels)
        {
            var root = new GameObject("PF_UI_ConnectionLobby", typeof(RectTransform));
            root.layer = UILayer;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; // la cámara la pone CanvasCameraBinder en runtime
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            menu = root.AddComponent<ConnectionMenu>();

            // Lo que se oculta al empezar la partida (el ConnectionMenu sigue vivo en la raíz)
            var content = Child(root, "Contenido");
            Stretch(content);

            var dim = Child(content, "Fondo");
            Stretch(dim);
            Img(dim, null, new Color(Ink.r, Ink.g, Ink.b, 0.72f)).raycastTarget = true;

            BuildLogo(kit, content);

            panels = new Panels();
            var so = new SerializedObject(menu);

            // ---------- Modo ----------
            {
                var card = Card(kit, content, "ModeSelectionPanel", new Vector2(820, 560), "¿CÓMO JUGAMOS?", out var body);
                panels.mode = card;
                var col = Column(body, 26);
                var local = BigButton(kit, col, "BTN_Local", "LOCAL", "mismo PC o misma red", StoreOrange, new Vector2(600, 130));
                var online = BigButton(kit, col, "BTN_Online", "ONLINE", "con código, desde cualquier sitio", SignBlue, new Vector2(600, 130));
                var back = SmallButton(kit, col, "BTN_Back", "VOLVER", Mute, new Vector2(300, 76));
                Listen(local, menu.OpenLocal);
                Listen(online, menu.OpenOnline);
                Listen(back, menu.BackToMainMenu);
            }

            // ---------- Local ----------
            {
                var card = Card(kit, content, "LocalPanel", new Vector2(820, 600), "PARTIDA LOCAL", out var body);
                panels.local = card;
                var col = Column(body, 22);
                var host = BigButton(kit, col, "BTN_HostLocal", "CREAR PARTIDA", "tu amigo se une con tu IP", StoreOrange, new Vector2(600, 130));
                Label(kit, col, "TXT_O", "— o únete a un amigo —", 28, Mute, new Vector2(600, 40));
                var row = Row(col, "Fila", 18, new Vector2(600, 96));
                var ip = Input(kit, row, "INPUT_IP", "IP del host (vacío = este PC)", new Vector2(382, 96), TMP_InputField.ContentType.Standard, 45);
                var join = SmallButton(kit, row, "BTN_JoinLocal", "UNIRSE", SignBlue, new Vector2(200, 96));
                var back = SmallButton(kit, col, "BTN_Back", "VOLVER", Mute, new Vector2(300, 76));
                Listen(host, menu.HostLocal);
                Listen(join, menu.JoinLocal);
                Listen(back, menu.OpenMainSelection);
                Set(so, "ipInputField", ip);
            }

            // ---------- Online ----------
            {
                var card = Card(kit, content, "OnlinePanel", new Vector2(820, 600), "PARTIDA ONLINE", out var body);
                panels.online = card;
                var col = Column(body, 22);
                var create = BigButton(kit, col, "BTN_CreateOnline", "CREAR PARTIDA", "te damos un código para tu amigo", SignBlue, new Vector2(600, 130));
                Label(kit, col, "TXT_O", "— o escribe su código —", 28, Mute, new Vector2(600, 40));
                var row = Row(col, "Fila", 18, new Vector2(600, 96));
                var code = Input(kit, row, "INPUT_JoinCode", "CÓDIGO", new Vector2(382, 96), TMP_InputField.ContentType.Alphanumeric, 12);
                code.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
                var join = SmallButton(kit, row, "BTN_JoinOnline", "UNIRSE", StoreOrange, new Vector2(200, 96));
                var back = SmallButton(kit, col, "BTN_Back", "VOLVER", Mute, new Vector2(300, 76));
                Listen(create, menu.CreateOnline);
                Listen(join, menu.JoinOnline);
                Listen(back, menu.OpenMainSelection);
                Set(so, "joinCodeInputField", code);
            }

            // ---------- Espera ----------
            {
                var card = Card(kit, content, "WaitingPanel", new Vector2(820, 600), "ESPERANDO RIVAL", out var body);
                panels.waiting = card;
                Set(so, "waitingTitleText", card.transform.Find("Cabecera/Titulo").GetComponent<TMP_Text>());
                var col = Column(body, 20);

                var codeRow = Row(col, "Codigo", 16, new Vector2(600, 110));
                var chip = Child(codeRow, "TXT_SessionCode");
                Size(chip, new Vector2(380, 110));
                Img(chip, kit.round, Ink, 2.4f);
                var codeText = Label(kit, chip, "Texto", "ABC123", 64, Cream, Vector2.zero);
                Stretch(codeText.gameObject);
                codeText.characterSpacing = 12;
                var copy = SmallButton(kit, codeRow, "BTN_CopyCode", "COPIAR", SignBlue, new Vector2(200, 96));
                Listen(copy, menu.CopySessionCode);
                Set(so, "sessionCodeText", codeText);
                Set(so, "copyCodeButton", copy.gameObject);

                Set(so, "hostIPAddressText", Label(kit, col, "TXT_HostIP", "TU IP: 192.168.1.20", 40, Ink, new Vector2(600, 56)));
                var dots = Label(kit, col, "TXT_Dots", "• • •", 56, StoreRed, new Vector2(600, 50));
                dots.gameObject.AddComponent<UIPulse>();
                Set(so, "playerCountText", Label(kit, col, "TXT_PlayerCount", "JUGADORES: 1/2", 36, Ink, new Vector2(600, 50)));
                var cancel = SmallButton(kit, col, "BTN_Cancel", "CANCELAR", StoreRed, new Vector2(320, 84));
                Listen(cancel, menu.CancelConnection);
                Set(so, "waitingPanel", card);
            }

            // ---------- Selección de personaje ----------
            {
                var card = Card(kit, content, "CharacterSelectionPanel", new Vector2(1180, 660), "ELIGE PERSONAJE", out var body);
                panels.character = card;

                // Columna izquierda: retrato redondo con su aro, flechas a los lados y nombre debajo
                var left = Child(body, "Retrato");
                Anchor(left, new Vector2(0, 0.5f), new Vector2(330, -10), new Vector2(560, 560));

                var ring = Child(left, "IMG_Ring");
                Anchor(ring, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(370, 370));
                var ringImage = Img(ring, kit.round, CharacterColor(null), 0.52f);
                var inner = Child(ring, "Interior");
                Anchor(inner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 330));
                Img(inner, kit.round, Color.white, 0.58f);
                var portrait = Child(inner, "IMG_CharacterPortrait");
                Anchor(portrait, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 300));
                var portraitImage = Img(portrait, null, Color.white);
                portraitImage.preserveAspect = true;

                var prev = SmallButton(kit, left, "BTN_PreviousCharacter", "<", Ink, new Vector2(96, 96));
                Anchor(prev.gameObject, new Vector2(0.5f, 0.6f), new Vector2(-240, 0), new Vector2(96, 96));
                var next = SmallButton(kit, left, "BTN_NextCharacter", ">", Ink, new Vector2(96, 96));
                Anchor(next.gameObject, new Vector2(0.5f, 0.6f), new Vector2(240, 0), new Vector2(96, 96));

                var nameChip = Child(left, "Nombre");
                Anchor(nameChip, new Vector2(0.5f, 0.12f), Vector2.zero, new Vector2(420, 92));
                Img(nameChip, kit.round, StoreRed, 2f);
                nameChip.transform.localRotation = Quaternion.Euler(0, 0, -2f);
                var nameText = Label(kit, nameChip, "TXT_CharacterName", "MAPACHE", 52, Color.white, Vector2.zero, outline: kit.outline);
                Stretch(nameText.gameObject);

                // Columna derecha: quién está listo y el botón READY
                var right = Child(body, "Listos");
                Anchor(right, new Vector2(1, 0.5f), new Vector2(-270, -10), new Vector2(460, 520));
                var col = Column(right, 24);
                var you = Pill(kit, col, "TXT_LocalReady", "TÚ · ELIGIENDO", Cream, Ink, new Vector2(420, 84));
                var rival = Pill(kit, col, "TXT_RivalReady", "RIVAL · ELIGIENDO", Cream, Ink, new Vector2(420, 84));
                var ready = BigButton(kit, col, "BTN_Ready", "READY", null, ReadyGreen, new Vector2(420, 150));
                var status = Label(kit, col, "TXT_ReadyStatus", "PLAYERS READY: 0/2", 30, Mute, new Vector2(420, 44));

                Listen(prev, menu.PreviousCharacter);
                Listen(next, menu.NextCharacter);
                Listen(ready, menu.ReadyCharacter);
                Set(so, "characterNameText", nameText);
                Set(so, "characterPortraitImage", portraitImage);
                Set(so, "characterRingImage", ringImage);
                Set(so, "localReadyText", you);
                Set(so, "rivalReadyText", rival);
                Set(so, "readyStatusText", status);
                Set(so, "previousCharacterButton", prev);
                Set(so, "nextCharacterButton", next);
                Set(so, "readyButton", ready);
            }

            // ---------- Mensajes ----------
            {
                var chip = Child(content, "TXT_Status");
                Anchor(chip, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(1000, 70));
                Img(chip, kit.round, Ink, 2.8f);
                var text = Label(kit, chip, "Texto", "Conectando a 127.0.0.1:7777...", 30, Cream, Vector2.zero);
                Stretch(text.gameObject, new Vector2(24, 0), new Vector2(-24, 0));
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Ellipsis;
                Set(so, "statusText", text);
                panels.status = chip;
            }

            Set(so, "connectionMenu", content);
            Set(so, "modeSelectionPanel", panels.mode);
            Set(so, "localPanel", panels.local);
            Set(so, "onlinePanel", panels.online);
            Set(so, "characterSelectionPanel", panels.character);
            SetCharacterColors(so);
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UILayer;
            return root;
        }

        static void BuildLogo(Kit kit, GameObject parent)
        {
            var logo = Child(parent, "Logo");
            Anchor(logo, new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(1100, 170));
            logo.transform.localRotation = Quaternion.Euler(0, 0, -2.5f);
            var title = Label(kit, logo, "Titulo", "ASALTO MAPACHE", 124, Color.white, Vector2.zero, outline: kit.outline);
            Stretch(title.gameObject);

            var tag = Child(logo, "Tienda");
            Anchor(tag, new Vector2(0.5f, 0), new Vector2(250, -20), new Vector2(300, 60));
            tag.transform.localRotation = Quaternion.Euler(0, 0, 5f);
            Img(tag, kit.round, StoreRed, 3.2f);
            var text = Label(kit, tag, "Texto", "NUKI MARKET", 32, Color.white, Vector2.zero);
            Stretch(text.gameObject);
        }

        // ============================================================
        // PIEZAS
        // ============================================================

        // Tarjeta crema con sombra de tinta y cabecera roja que sobresale por arriba. Devuelve la tarjeta; body = zona útil.
        static GameObject Card(Kit kit, GameObject parent, string name, Vector2 size, string title, out GameObject body)
        {
            var card = Child(parent, name);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0, -70), size);

            var shadow = Child(card, "Sombra");
            Stretch(shadow, new Vector2(10, -14), new Vector2(10, -14));
            Img(shadow, kit.round, new Color(Ink.r, Ink.g, Ink.b, 0.45f), 1.4f);

            var face = Child(card, "Cara");
            Stretch(face);
            Img(face, kit.round, Ink, 1.4f);
            var fill = Child(face, "Relleno");
            Stretch(fill, new Vector2(6, 6), new Vector2(-6, -6));
            Img(fill, kit.round, Cream, 1.45f);

            var header = Child(card, "Cabecera");
            Anchor(header, new Vector2(0.5f, 1), new Vector2(0, 6), new Vector2(Mathf.Min(size.x - 160, 640), 96));
            header.transform.localRotation = Quaternion.Euler(0, 0, -1.5f);
            Img(header, kit.round, StoreRed, 2f);
            var titleText = Label(kit, header, "Titulo", title, 54, Color.white, Vector2.zero, outline: kit.outline);
            Stretch(titleText.gameObject);

            body = Child(card, "Cuerpo");
            Stretch(body, new Vector2(40, 36), new Vector2(-40, -70));
            return card;
        }

        static GameObject Column(GameObject parent, float spacing)
        {
            var col = Child(parent, "Columna");
            Stretch(col);
            var layout = col.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return col;
        }

        static GameObject Row(GameObject parent, string name, float spacing, Vector2 size)
        {
            var row = Child(parent, name);
            Size(row, size);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        // Botón gordo: base oscura debajo (relieve), cara de color, texto con contorno y subtítulo opcional.
        static Button BigButton(Kit kit, GameObject parent, string name, string label, string subtitle, Color color, Vector2 size)
        {
            var button = ChunkyButton(kit, parent, name, color, size, 1.6f, out var face);
            var main = Label(kit, face, "Texto", label, subtitle == null ? 64 : 54, Color.white, Vector2.zero, outline: kit.outline);
            Stretch(main.gameObject, new Vector2(0, subtitle == null ? 0 : 24), Vector2.zero);
            if (subtitle != null)
            {
                var sub = Label(kit, face, "Subtitulo", subtitle, 24, new Color(1, 1, 1, 0.92f), Vector2.zero);
                Anchor(sub.gameObject, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(size.x - 40, 32));
            }
            return button;
        }

        static Button SmallButton(Kit kit, GameObject parent, string name, string label, Color color, Vector2 size)
        {
            var button = ChunkyButton(kit, parent, name, color, size, 2.2f, out var face);
            var text = Label(kit, face, "Texto", label, Mathf.Min(42, size.y * 0.5f), Color.white, Vector2.zero, outline: kit.outline);
            Stretch(text.gameObject);
            return button;
        }

        static Button ChunkyButton(Kit kit, GameObject parent, string name, Color color, Vector2 size, float roundness, out GameObject face)
        {
            var go = Child(parent, name);
            Size(go, size);

            var shade = Child(go, "Base");
            Stretch(shade, new Vector2(0, -9), new Vector2(0, -9));
            Img(shade, kit.round, Color.Lerp(color, Ink, 0.45f), roundness);

            face = Child(go, "Cara");
            Stretch(face);
            var faceImage = Img(face, kit.round, Color.white, roundness);
            faceImage.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = faceImage;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.selectedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Ink, 0.15f);
            colors.disabledColor = Color.Lerp(color, Mute, 0.65f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            go.AddComponent<UIButtonJuice>();
            return button;
        }

        static TMP_Text Pill(Kit kit, GameObject parent, string name, string value, Color fill, Color textColor, Vector2 size)
        {
            var go = Child(parent, name + "_Ficha");
            Size(go, size);
            Img(go, kit.round, Ink, 2.2f);
            var inside = Child(go, "Relleno");
            Stretch(inside, new Vector2(4, 4), new Vector2(-4, -4));
            Img(inside, kit.round, fill, 2.3f);
            var text = Label(kit, go, name, value, 36, textColor, Vector2.zero);
            Stretch(text.gameObject);
            return text;
        }

        static TMP_InputField Input(Kit kit, GameObject parent, string name, string placeholder, Vector2 size,
            TMP_InputField.ContentType contentType, int limit)
        {
            var go = Child(parent, name);
            Size(go, size);
            Img(go, kit.round, Ink, 2.2f);
            var inside = Child(go, "Relleno");
            Stretch(inside, new Vector2(4, 4), new Vector2(-4, -4));
            var bg = Img(inside, kit.round, Color.white, 2.3f);
            bg.raycastTarget = true;

            var area = Child(go, "Text Area");
            Stretch(area, new Vector2(28, 8), new Vector2(-28, -8));
            area.AddComponent<RectMask2D>();

            var hint = Label(kit, area, "Placeholder", placeholder, 30, Mute, Vector2.zero);
            Stretch(hint.gameObject);
            hint.fontStyle = FontStyles.Normal;
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            var text = Label(kit, area, "Text", "", 38, Ink, Vector2.zero);
            Stretch(text.gameObject);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var field = go.AddComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.textViewport = (RectTransform)area.transform;
            field.textComponent = text;
            field.placeholder = hint;
            field.fontAsset = kit.font;
            field.pointSize = 38;
            field.contentType = contentType;
            field.characterLimit = limit;
            field.caretColor = Ink;
            field.customCaretColor = true;
            field.caretWidth = 3;
            field.selectionColor = new Color(SignBlue.r, SignBlue.g, SignBlue.b, 0.35f);
            return field;
        }

        static TextMeshProUGUI Label(Kit kit, GameObject parent, string name, string value, float size, Color color, Vector2 box,
            Material outline = null)
        {
            var go = Child(parent, name);
            if (box != Vector2.zero) Size(go, box);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = kit.font;
            if (outline) text.fontSharedMaterial = outline;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static Image Img(GameObject go, Sprite sprite, Color color, float roundness = 1f)
        {
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite)
            {
                image.type = Image.Type.Sliced;
                // La ficha tiene un radio de 96 px: un multiplicador mayor la redondea menos
                image.pixelsPerUnitMultiplier = roundness;
            }
            return image;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void Size(GameObject go, Vector2 size) => ((RectTransform)go.transform).sizeDelta = size;

        static void Anchor(GameObject go, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void Stretch(GameObject go, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static void Listen(Button button, UnityAction action) => UnityEventTools.AddPersistentListener(button.onClick, action);

        static void Set(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null) Debug.LogError($"Lobby: ConnectionMenu no tiene el campo «{field}».");
            else property.objectReferenceValue = value;
        }

        static PlayerCharacterRoster FindRoster() =>
            AssetDatabase.FindAssets("t:PlayerCharacterRoster")
                .Select(g => AssetDatabase.LoadAssetAtPath<PlayerCharacterRoster>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(r => r && r.Count > 0);

        static Color CharacterColor(PlayerCharacterDefinition character)
        {
            string key = character && character.prefab ? character.prefab.name : "";
            foreach (var (name, color) in CharacterTints)
                if (key.Contains(name)) return Hex(color);
            return SignBlue;
        }

        static void SetCharacterColors(SerializedObject so)
        {
            var roster = FindRoster();
            var property = so.FindProperty("characterColors");
            var colors = roster ? roster.Characters.Select(CharacterColor).ToArray() : new Color[0];
            property.arraySize = colors.Length;
            for (int i = 0; i < colors.Length; i++) property.GetArrayElementAtIndex(i).colorValue = colors[i];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        // ============================================================
        // AÑADIR A LA ESCENA
        // ============================================================

        /// <summary>
        /// Deja la escena abierta con el lobby: añade lo que falte (con deshacer) y apaga el ConnectionMenu antiguo.
        /// </summary>
        [MenuItem("Racoon/Menús/Añadir lobby a la escena abierta")]
        public static void AddToOpenScene()
        {
            var lobbyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
            var managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            if (!lobbyPrefab || !managerPrefab)
            {
                Build();
                lobbyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
                managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
                if (!lobbyPrefab || !managerPrefab) return;
            }

            var scene = SceneManager.GetActiveScene();
            var added = new List<GameObject>();
            var notes = new List<string>();

            if (!Object.FindFirstObjectByType<NetworkTestMenu>(FindObjectsInactive.Include))
                notes.Add("La escena no tiene PF_MNG_NetworkManager (con NetworkTestMenu): añádelo o el menú no podrá conectar.");

            // Menús antiguos fuera (el de Fabre en SCN_Masip_Pruebas_Fabre): se apagan, no se borran
            foreach (var old in Object.FindObjectsByType<ConnectionMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject) == lobbyPrefab) continue;
                var oldUi = new SerializedObject(old).FindProperty("connectionMenu").objectReferenceValue as GameObject;
                foreach (var go in new[] { old.gameObject, oldUi }.Where(g => g && g.activeSelf))
                {
                    Undo.RecordObject(go, "Añadir lobby");
                    go.SetActive(false);
                    notes.Add($"Apagado el menú antiguo «{go.name}».");
                }
            }

            if (!Object.FindObjectsByType<ConnectionMenu>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Any())
                added.Add(Spawn(lobbyPrefab, scene));

            if (!Object.FindFirstObjectByType<CharacterLobbyManager>(FindObjectsInactive.Include))
                added.Add(Spawn(managerPrefab, scene));

            if (!Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include))
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                var module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                es.AddComponent(module ?? typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(es, scene);
                Undo.RegisterCreatedObjectUndo(es, "Añadir lobby");
                added.Add(es);
            }

            if (added.Count == 0 && notes.Count == 0)
            {
                EditorUtility.DisplayDialog("Lobby", "La escena ya tiene el lobby: no falta nada.", "Vale");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (added.Count > 0) Selection.objects = added.ToArray();
            foreach (var note in notes) Debug.LogWarning($"Lobby: {note}");
            Debug.Log($"Lobby: añadido a {scene.name}: {string.Join(", ", added.Select(a => a.name))}. Guarda la escena para conservarlo.");

            static GameObject Spawn(GameObject prefab, Scene target)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target);
                Undo.RegisterCreatedObjectUndo(instance, "Añadir lobby");
                return instance;
            }
        }

        // ============================================================
        // CAPTURAS DE REVISIÓN (Temp/Lobby_Revision/<fecha>)
        // ============================================================

        static void RenderPreviews(GameObject root, ConnectionMenu menu, Panels panels)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Lobby_Revision", System.DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(dir);

            // Datos de muestra en la selección: el primer personaje del roster
            var roster = FindRoster();
            var first = roster ? roster.Get(0) : null;
            var portrait = panels.character.GetComponentsInChildren<Image>(true).First(i => i.name == "IMG_CharacterPortrait");
            var name = panels.character.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "TXT_CharacterName");
            if (first)
            {
                portrait.sprite = first.portrait;
                name.text = first.displayName;
            }

            var cameraObject = new GameObject("Cámara revisión lobby");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("#7f8c8d");
            camera.cullingMask = 1 << UILayer;
            camera.orthographic = true;
            var target = new RenderTexture((int)RefWidth, (int)RefHeight, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var canvas = root.GetComponent<Canvas>();
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            try
            {
                var all = new[] { panels.mode, panels.local, panels.online, panels.waiting, panels.character };
                foreach (var panel in all)
                {
                    foreach (var other in all) other.SetActive(other == panel);
                    panels.status.SetActive(panel == panels.local || panel == panels.waiting);
                    Capture(camera, target, Path.Combine(dir, panel.name + ".png"));
                }
                Debug.Log($"Lobby: capturas de revisión en {dir}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Lobby: no se pudieron generar las capturas de revisión: {e.Message}");
            }
            finally
            {
                // Estado del prefab: solo el panel de modo visible y sin datos de muestra
                foreach (var panel in new[] { panels.local, panels.online, panels.waiting, panels.character }) panel.SetActive(false);
                panels.mode.SetActive(true);
                panels.status.SetActive(false);
                portrait.sprite = null;
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
    }
}
