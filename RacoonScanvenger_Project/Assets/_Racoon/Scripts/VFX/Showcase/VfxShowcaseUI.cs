using System.Collections.Generic;
using System.Reflection;
using Racoon.Events;
using Racoon.Gameplay;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Showcase: la UI del juego sin partida. El director le llama con SendMessage (cue "Msg:SHOWCASE_UI:Método:arg"):
    ///  - EventDouble / EventSpeed: cinta de evento (GameEventAnnouncer) con los textos y pieles de los eventos reales.
    ///  - Card: tarjeta de estado lateral; CardHit: la misma con el contador bajando (late); CardClear: «¡AGOTADA!».
    ///  - Result: pantalla de resultado (MatchResultUI) con mapache ganador y panda rojo, y sus fuegos artificiales.
    /// </summary>
    public class VfxShowcaseUI : MonoBehaviour
    {
        [SerializeField] PlayerCharacterDefinition winner;
        [SerializeField] PlayerCharacterDefinition loser;

        [Header("Lootpoint con x2 (plano de la flecha)")]
        [Tooltip("Estantería con LootPoint (p. ej. PF_SM_Shelves_Chips). Se coloca junto a la flecha del evento.")]
        [SerializeField] GameObject x2ShelfPrefab;
        [SerializeField] Vector3 x2ShelfOffset = new(1.1f, 0f, 2.6f);
        [SerializeField] float x2ShelfYaw = -90f;

        [Header("Apuntado del plátano")]
        [Tooltip("Modelo del plátano (el heldVisualPrefab del objeto). Solo se usa su malla.")]
        [SerializeField] GameObject bananaPrefab;
        [Tooltip("M_VFX_ToonFlat: arco de puntos y anillo de caída.")]
        [SerializeField] Material flatMaterial;
        [SerializeField, Min(0.2f)] float aimFlightTime = 0.55f;
        [Tooltip("Caja de objeto (PF_BoxItem) del plano de gameplay; solo se usa su modelo.")]
        [SerializeField] GameObject itemBoxPrefab;

        [Header("Decorado (solo visual)")]
        [Tooltip("Muebles de la tienda que se colocan en fila detrás de cada plano de VFX.")]
        [SerializeField] GameObject[] enviroPrefabs = new GameObject[0];
        [Tooltip("X de los sets que llevan decorado (cada plano está en su set, cada 25 m).")]
        [SerializeField] float[] enviroSetsX = { 25f, 50f, 75f, 100f, 125f, 150f, 175f, 200f, 225f, 250f, 275f, 300f, 325f, 350f, 400f, 575f, 625f };
        [SerializeField] float enviroZ = 3.6f;
        [Tooltip("Separación entre muebles de la fila (m).")]
        [SerializeField] float enviroGap = 0.04f;
        [SerializeField, Min(1)] int enviroPerSet = 5;

        int cardCount = 7;

        void Start()
        {
            BuildEnviro();
            PlaceX2Shelf();
            MuteGamePrompts();
        }

        void Update()
        {
            UpdateX2();
            UpdateAim();
            UpdateFills();
            UpdateBox();
        }

        // ---------------- Avisos de mantener / machacar ----------------

        // Los avisos del juego (LootPromptUI / SellPromptUI) se esconden y vacían cada frame si no hay jugador local:
        // en el showcase los manejan los cues (Animator + barra).
        void MuteGamePrompts()
        {
            foreach (string name in new[] { "AvisoSaquear", "AvisoVender" })
            {
                GameObject prop = FindInScene(name);
                if (prop == null) continue;
                foreach (MonoBehaviour b in prop.GetComponentsInChildren<MonoBehaviour>(true))
                    if (b is Racoon.UI.LootPromptUI || b is Racoon.UI.SellPromptUI) b.enabled = false;
            }
        }

        readonly Dictionary<UnityEngine.UI.Image, (float start, float duration)> fills = new();

        /// <summary>Cue "Msg:SHOWCASE_UI:Fill:AvisoVender,1.2": la barra del aviso se llena de 0 a 1 en esos segundos.</summary>
        public void Fill(string arg)
        {
            string[] p = (arg ?? "").Split(',');
            GameObject prop = FindInScene(p[0]);
            if (prop == null) return;
            float seconds = p.Length > 1 && float.TryParse(p[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 1f;
            foreach (UnityEngine.UI.Image image in prop.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (image.type == UnityEngine.UI.Image.Type.Filled && image.name.Contains("Relleno"))
                {
                    image.fillAmount = 0f;
                    fills[image] = (Time.time, Mathf.Max(0.05f, seconds));
                }
        }

        void UpdateFills()
        {
            if (fills.Count == 0) return;
            var done = new List<UnityEngine.UI.Image>();
            foreach (var kv in fills)
            {
                if (kv.Key == null) { done.Add(kv.Key); continue; }
                float k = Mathf.Clamp01((Time.time - kv.Value.start) / kv.Value.duration);
                kv.Key.fillAmount = k;
                if (k >= 1f) done.Add(kv.Key);
            }
            foreach (var image in done) fills.Remove(image);
        }

        static GameObject FindInScene(string name)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == name && go.scene.IsValid() && go.transform.parent == null) return go;
            return null;
        }

        // ---------------- Caja de objeto (plano de gameplay) ----------------

        Transform box, heldBanana;
        float boxPickedAt = -1f, boxBaseY;
        const float boxHover = 0.18f;

        /// <summary>Cue "Msg:SHOWCASE_UI:BoxSpawn:x,z": caja de objeto en el suelo.</summary>
        public void BoxSpawn(string arg)
        {
            if (itemBoxPrefab == null) return;
            string[] p = (arg ?? "").Split(',');
            float x = p.Length > 0 ? ParseF(p[0], 0f) : 0f, z = p.Length > 1 ? ParseF(p[1], 0f) : 0f;
            if (box != null) Destroy(box.gameObject);
            GameObject go = Instantiate(itemBoxPrefab, new Vector3(x, 0f, z), Quaternion.identity);
            StripToVisual(go);
            box = go.transform;
            boxPickedAt = -1f;
            // El pivote está en el centro del modelo: se sube para que flote sobre el suelo, no medio enterrada.
            Bounds b = RendererBounds(go);
            boxBaseY = box.position.y - b.min.y + boxHover;
            box.position = new Vector3(x, boxBaseY, z);
        }

        /// <summary>Cue "Msg:SHOWCASE_UI:BoxPick": A coge la caja y le toca el plátano (a la espalda).</summary>
        public void BoxPick(string _)
        {
            boxPickedAt = Time.time;
            Transform a = FindActor("Actor_A");
            if (a == null || bananaPrefab == null) return;
            if (heldBanana != null) Destroy(heldBanana.gameObject);
            GameObject go = Instantiate(bananaPrefab, a);
            StripToVisual(go);
            heldBanana = go.transform;
            heldBanana.localPosition = new Vector3(0f, 0.95f, -0.28f);
            heldBanana.localRotation = Quaternion.Euler(0f, 0f, 25f);
            heldBanana.localScale = Vector3.zero;
        }

        void UpdateBox()
        {
            if (box != null && boxPickedAt < 0f)
            {
                // En espera: flota y gira despacio.
                Vector3 p = box.position;
                box.position = new Vector3(p.x, boxBaseY + Mathf.Sin(Time.time * 2.4f) * 0.06f, p.z);
                box.rotation = Quaternion.Euler(0f, Time.time * 45f, 0f);
            }
            if (box != null && boxPickedAt >= 0f)
            {
                // Pop: crece un poco y desaparece.
                float k = (Time.time - boxPickedAt) / 0.25f;
                float s = k < 0.4f ? Mathf.Lerp(1f, 1.3f, k / 0.4f) : Mathf.Lerp(1.3f, 0f, (k - 0.4f) / 0.6f);
                box.localScale = Vector3.one * Mathf.Max(0f, s);
                if (k >= 1f) { Destroy(box.gameObject); box = null; }
            }
            if (heldBanana != null && boxPickedAt >= 0f)
            {
                float k = Mathf.Clamp01((Time.time - boxPickedAt - 0.15f) / 0.3f);
                heldBanana.localScale = Vector3.one * FlatFx.OutBack(k, 2.2f);
            }
        }

        static float ParseF(string s, float fallback) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        // ---------------- Decorado ----------------

        void BuildEnviro()
        {
            if (enviroPrefabs == null || enviroPrefabs.Length == 0) return;
            var root = new GameObject("SHOWCASE_Enviro").transform;
            int n = 0;
            foreach (float x in enviroSetsX)
            {
                // Fila de muebles con el frente a la misma profundidad (enviroZ) y pegados uno al lado de otro,
                // centrada en el set. Se miden con sus mallas porque cada prefab tiene el pivote en un sitio.
                var row = new List<(Transform t, Bounds b)>();
                float width = 0f;
                for (int i = 0; i < enviroPerSet; i++)
                {
                    GameObject prefab = enviroPrefabs[(n + i) % enviroPrefabs.Length];
                    if (prefab == null) continue;
                    GameObject go = Instantiate(prefab, new Vector3(x, 0f, enviroZ), Quaternion.identity, root);
                    FaceCamera(go);
                    StripToVisual(go);
                    Bounds b = RendererBounds(go);
                    row.Add((go.transform, b));
                    width += b.size.x;
                }
                width += enviroGap * Mathf.Max(0, row.Count - 1);
                float cursor = x - width * 0.5f;
                foreach (var (t, b) in row)
                {
                    // Desplaza para que el borde izquierdo caiga en el cursor y el frente (lado de cámara, -Z) en enviroZ.
                    t.position += new Vector3(cursor - b.min.x, -b.min.y, enviroZ - b.min.z);
                    cursor += b.size.x + enviroGap;
                }
                n += 2;
            }
        }

        static Bounds RendererBounds(GameObject go)
        {
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        // Cada mueble tiene el frente hacia un lado: se gira para que el lado desde el que se saquea mire a cámara (-Z).
        static void FaceCamera(GameObject go)
        {
            LootPoint lp = go.GetComponentInChildren<LootPoint>(true);
            Collider trigger = lp != null ? lp.GetComponent<Collider>() : null;
            if (trigger == null) return;
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            Vector3 front = trigger.bounds.center - bounds.center;
            front.y = 0f;
            if (front.sqrMagnitude < 0.0001f) return;
            go.transform.rotation = Quaternion.FromToRotation(front.normalized, Vector3.back) * go.transform.rotation;
            // Que no se hunda ni flote si el giro no es solo en Y.
            Vector3 e = go.transform.eulerAngles;
            go.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
        }

        // Solo lo que se ve: sin scripts (LootPoint, neveras...) ni física.
        static void StripToVisual(GameObject go)
        {
            foreach (MonoBehaviour b in go.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
        }

        // ---------------- x2 sobre el lootpoint ----------------

        LootPoint x2Shelf;
        DoublePointsVFX x2Vfx;
        float x2Level, x2Target;
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        void PlaceX2Shelf()
        {
            if (x2ShelfPrefab == null) return;
            GameObject arrow = GameObject.Find("PF_VFX_EventFloorArrow");
            Vector3 at = (arrow != null ? arrow.transform.position : Vector3.zero) + x2ShelfOffset;
            at.y = 0f;
            GameObject shelf = Instantiate(x2ShelfPrefab, at, Quaternion.Euler(0f, x2ShelfYaw, 0f));
            x2Shelf = shelf.GetComponentInChildren<LootPoint>(true);
        }

        public void X2On(string _)
        {
            if (x2Shelf == null) return;
            if (x2Vfx == null)
            {
                x2Vfx = new GameObject("SHOWCASE_X2").AddComponent<DoublePointsVFX>();
                typeof(DoublePointsVFX).GetMethod("BuildGlow", Private)?.Invoke(x2Vfx, new object[] { x2Shelf });
                var bounds = new Bounds(x2Shelf.VisualRoot.position, Vector3.zero);
                foreach (Renderer r in x2Shelf.VisualRoot.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                typeof(DoublePointsVFX).GetField("shelfCenter", Private)?.SetValue(x2Vfx, bounds.center);
            }
            x2Target = 1f;
        }

        public void X2Off(string _) => x2Target = 0f;

        void UpdateX2()
        {
            if (x2Vfx == null) return;
            x2Level = Mathf.MoveTowards(x2Level, x2Target, Time.deltaTime / 0.35f);
            typeof(DoublePointsVFX).GetField("glowLevel", Private)?.SetValue(x2Vfx, x2Level);
            typeof(DoublePointsVFX).GetMethod("UpdateGlow", Private)?.Invoke(x2Vfx, new object[] { Time.time });
        }

        // ---------------- Apuntado del plátano ----------------

        enum AimState { Off, Aiming, Flying, Landed }
        AimState aim;
        Transform aimRoot, banana;
        readonly List<FlatPiece> dots = new();
        FlatPiece ring;
        Vector3 aimStart, aimVelocity, aimLanding, aimFrom;
        float aimTime, aimSweep;
        Transform aimer, aimVictim;

        /// <summary>Hacia dónde mira el que apunta (lo usa el director para girarlo). Null si nadie apunta.</summary>
        public static Vector3? AimPoint { get; private set; }
        public static Transform Aimer { get; private set; }
        const int DotCount = 7;

        /// <summary>Cue "AimBegin" (apunta ya a B) o "AimBegin:1.6" (el punto de caída va de delante de A hasta B en 1,6 s).</summary>
        public void AimBegin(string arg)
        {
            Transform a = FindActor("Actor_A"), b = FindActor("Actor_B");
            if (a == null || b == null || flatMaterial == null) return;
            AimClear(null);
            aimer = a;
            aimVictim = b;
            aimSweep = ParseF(arg, 0f);
            Vector3 toB = b.position - a.position;
            toB.y = 0f;
            Vector3 dir = toB.sqrMagnitude > 0.001f ? toB.normalized : a.forward;
            aimFrom = a.position + dir * 0.9f;
            aimFrom.y = 0.02f;
            RefreshAim();

            aimRoot = new GameObject("SHOWCASE_Aim").transform;
            Mesh dotMesh = FlatMeshes.Burst(2, 10, 1f, 0f, 0.18f);
            for (int i = 0; i < DotCount; i++) dots.Add(new FlatPiece("Dot", aimRoot, dotMesh, flatMaterial, 2));
            ring = new FlatPiece("Ring", aimRoot, FlatMeshes.Arc(1f, 0f, 360f, 0.12f, 0f, 40), flatMaterial, 1);
            aim = AimState.Aiming;
            aimTime = 0f;
        }

        // Arco desde la mano de A hasta el punto de caída (que se acerca a B si el apuntado es progresivo).
        void RefreshAim()
        {
            if (aimer == null || aimVictim == null) return;
            Vector3 toB = aimVictim.position - aimer.position;
            toB.y = 0f;
            Vector3 dir = toB.sqrMagnitude > 0.001f ? toB.normalized : aimer.forward;
            Vector3 target = aimVictim.position - dir * 0.15f;
            target.y = 0.02f;
            float k = aimSweep > 0f ? Mathf.SmoothStep(0f, 1f, aimTime / aimSweep) : 1f;
            aimLanding = Vector3.Lerp(aimFrom, target, k);
            Vector3 flat = aimLanding - aimer.position;
            flat.y = 0f;
            Vector3 face = flat.sqrMagnitude > 0.001f ? flat.normalized : dir;
            aimStart = aimer.position + Vector3.up * 1.0f + face * 0.35f;
            aimVelocity = (aimLanding - aimStart) / aimFlightTime - 0.5f * aimFlightTime * Physics.gravity;
            AimPoint = aimLanding;
            Aimer = aimer;
        }

        public void AimThrow(string _)
        {
            if (aim != AimState.Aiming) return;
            foreach (FlatPiece d in dots) d.Visible = false;
            AimPoint = null;
            if (heldBanana != null) { Destroy(heldBanana.gameObject); heldBanana = null; }
            if (bananaPrefab != null)
            {
                GameObject go = Instantiate(bananaPrefab, aimStart, Quaternion.identity, aimRoot);
                StripToVisual(go);
                banana = go.transform;
            }
            aim = AimState.Flying;
            aimTime = 0f;
        }

        public void AimClear(string _)
        {
            if (aimRoot != null) Destroy(aimRoot.gameObject);
            aimRoot = null;
            banana = null;
            ring = null;
            dots.Clear();
            aim = AimState.Off;
            AimPoint = null;
            Aimer = null;
        }

        void UpdateAim()
        {
            if (aim == AimState.Off || aimRoot == null) return;
            aimTime += Time.deltaTime;
            Color ink = new(0.12f, 0.1f, 0.18f), white = Color.white;
            float beat = 1f + 0.1f * Mathf.Sin(Time.time * 10f);

            if (aim == AimState.Aiming)
            {
                RefreshAim();
                // Puntos repartidos por el arco avanzando (como el apuntado del juego) y anillo latiendo donde caerá.
                float march = Mathf.Repeat(Time.time * 1.2f, 1f) / DotCount;
                float grow = Mathf.Clamp01(aimTime / 0.25f);
                for (int i = 0; i < dots.Count; i++)
                {
                    float k = (i / (float)DotCount + march) * aimFlightTime;
                    Vector3 p = aimStart + aimVelocity * k + 0.5f * k * k * Physics.gravity;
                    dots[i].Transform.SetPositionAndRotation(p, FlatFx.Billboard());
                    dots[i].Transform.localScale = Vector3.one * 0.065f * grow;
                    dots[i].Colors(white, white, ink);
                    dots[i].Visible = true;
                }
            }
            else if (aim == AimState.Flying && banana != null)
            {
                float k = Mathf.Min(aimTime, aimFlightTime);
                banana.position = aimStart + aimVelocity * k + 0.5f * k * k * Physics.gravity;
                banana.rotation = Quaternion.Euler(0f, aimTime * 540f, aimTime * 720f);
                if (aimTime >= aimFlightTime)
                {
                    aim = AimState.Landed;
                    aimTime = 0f;
                    banana.rotation = Quaternion.Euler(0f, 35f, 0f);
                }
            }
            else if (aim == AimState.Landed && banana != null)
            {
                // Pequeño aplastón al caer.
                float s = 1f + 0.35f * Mathf.Exp(-aimTime * 10f) * Mathf.Sin(aimTime * 30f);
                banana.localScale = new Vector3(s, 1f / s, s);
            }

            if (ring != null)
            {
                ring.Visible = aim != AimState.Landed;
                ring.Transform.SetPositionAndRotation(aimLanding, Quaternion.identity);
                ring.Transform.localScale = Vector3.one * 0.32f * beat;
                ring.Colors(white, Color.Lerp(white, Color.yellow, 0.4f), ink);
            }
        }

        static Transform FindActor(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }


        public void EventDouble(string _)
        {
            GameEventAnnouncer.Announce(this, "¡OFERTA x2!", "Una estantería da el doble de puntos. ¡Busca el cartel!",
                new Color(0.66f, 0.9f, 0.79f), "Verde");
            TickerInView();
        }

        public void EventSpeed(string _)
        {
            GameEventAnnouncer.Announce(this, "¡RITMO ACELERADO!", "El reloj corre x2 hasta el final de la partida.",
                new Color(1f, 0.68f, 0.7f), "Rojo");
            TickerInView();
        }

        // En partida el texto entra desde fuera de la pantalla tras una pausa (unos 7 s en cruzar); en el reel la cinta
        // se ve poco rato y salía vacía. Aquí arranca ya dentro y moviéndose.
        static void TickerInView()
        {
            GameEventAnnouncer a = FindFirstObjectByType<GameEventAnnouncer>();
            if (a == null) return;
            var t = typeof(GameEventAnnouncer);
            t.GetField("scrollX", Private)?.SetValue(a, 260f);
            t.GetField("scrollStartAt", Private)?.SetValue(a, Time.unscaledTime);
            t.GetMethod("SetTickerX", Private)?.Invoke(a, new object[] { 260f });
        }

        public void Card(string _) =>
            GameEventAnnouncer.SetCard(this, "¡OFERTA x2!", $"QUEDAN {cardCount}", new Color(0.66f, 0.9f, 0.79f));

        public void CardHit(string _)
        {
            cardCount = Mathf.Max(0, cardCount - 1);
            Card(null);
        }

        public void CardClear(string _) => GameEventAnnouncer.ClearCard(this, "¡AGOTADA!");

        /// <summary>Quita la cinta y la tarjeta para que no se mezclen con la siguiente UI.</summary>
        public void EventHide(string _)
        {
            GameEventAnnouncer announcer = FindFirstObjectByType<GameEventAnnouncer>();
            if (announcer != null) announcer.gameObject.SetActive(false);
        }

        // Un frame después: Show crea los escenarios de los retratos y no se puede destruir dentro de un Animation Event.
        public void Result(string _) => StartCoroutine(ResultNextFrame());

        System.Collections.IEnumerator ResultNextFrame()
        {
            yield return null;
            MatchResultUI ui = MatchResultUI.Instance != null ? MatchResultUI.Instance : FindFirstObjectByType<MatchResultUI>(FindObjectsInactive.Include);
            if (ui == null) yield break;
            // Show es privado (lo llama MatchTimer con los jugadores de la red): aquí se le dan los personajes directamente.
            MethodInfo show = typeof(MatchResultUI).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic);
            show?.Invoke(ui, new object[]
            {
                "YOU WIN!!", new Color(1f, 0.82f, 0.32f, 1f),
                (winner, true, 1), (loser, false, 2), false,
            });
        }

        public void ResultHide(string _)
        {
            MatchResultUI ui = MatchResultUI.Instance;
            if (ui != null) ui.Hide();
        }
    }
}
