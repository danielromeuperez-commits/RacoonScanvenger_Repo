using Racoon.Cameras;
using Racoon.Player;
using Racoon.VFX;
using UnityEngine;

namespace Racoon.Events
{
    /// <summary>
    /// Visuales del evento «OFERTA x2» en cada cliente (lee lo que replica <see cref="eventDoublePoints"/>):
    ///  - Tarjeta de estado en el lateral de la pantalla con «QUEDAN 7» (<see cref="GameEventAnnouncer.SetCard"/>); late
    ///    con cada recolección doble y al agotarse dice «¡AGOTADA!».
    ///  - Anillo en el suelo bajo tu jugador con una puntita hacia la estantería (prefab PF_VFX_EventFloorArrow, con su
    ///    Animator). Se queda mientras la estantería tenga recolecciones x2 y se vuelve transparente al acercarte a ella.
    ///  - Destellos que suben de la estantería y un golpe de cámara si eres tú quien la saquea.
    /// No se monta a mano: lo añade eventDoublePoints al aparecer. El aspecto común sale de <see cref="GameEventTheme"/>.
    /// </summary>
    public sealed class DoublePointsVFX : VfxBehaviour
    {
        static readonly int ArrowVisibleId = Animator.StringToHash("Visible");

        [Header("Tarjeta")]
        [SerializeField] string cardTitle = "¡OFERTA x2!";

        [Header("Anillo de suelo")]
        [Tooltip("Distancia (m) al borde de la estantería a la que el anillo ya es invisible.")]
        [SerializeField, Min(0f)] float ringFadeNear = 0.4f;
        [Tooltip("Distancia (m) al borde de la estantería a partir de la cual el anillo se ve del todo.")]
        [SerializeField, Min(0f)] float ringFadeFar = 3f;

        [Header("Destellos")]
        [Tooltip("Puntas de los destellos.")]
        [SerializeField] Color sparkleTip = new(1f, 0.93f, 0.78f);
        [SerializeField] int sparkleCount = 10;
        [SerializeField] float sparkleInterval = 0.16f;

        [Tooltip("Golpe de cámara al saquear tú la estantería x2.")]
        [SerializeField] CameraFXProfile grabFx = new(0.35f, decayPower: 2f)
        {
            priority = 36, duck = 0.4f, edge = 0.25f, flash = 0.04f,
        };

        eventDoublePoints evt;
        GameEventTheme theme;
        Color accent;

        // Escena montada
        Transform root;
        FlatPiece[] sparkles;
        float[] sparkleBorn;
        Vector3[] sparkleFrom;
        int nextSparkle;
        float lastSparkle;

        GameObject arrow;
        Animator arrowAnimator;
        SpriteRenderer[] arrowRenderers;
        Color[] arrowColors;
        PlayerController localPlayer;
        float nextPlayerSearch;

        // Geometría de la estantería
        Vector3 shelfCenter;
        Quaternion shelfYaw = Quaternion.identity;
        Vector2 footprintHalf = new(0.33f, 1.45f);
        float shelfTop, floorY;

        bool built, ending;
        float endedAt;

        void Awake()
        {
            evt = GetComponent<eventDoublePoints>();
            theme = GameEventTheme.Current;
        }

        void OnEnable()
        {
            if (evt != null) evt.StateChanged += OnStateChanged;
            PlayerLoot.AnyLootGrabbed += OnLootGrabbed;
        }

        void OnDisable()
        {
            if (evt != null) evt.StateChanged -= OnStateChanged;
            PlayerLoot.AnyLootGrabbed -= OnLootGrabbed;
            GameEventAnnouncer.ClearCard(evt);
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (arrow != null) Destroy(arrow);
        }

        // ---------------- Estado ----------------

        void OnStateChanged(eventDoublePoints e, bool started)
        {
            accent = e.Accent;
            if (e.IsActive)
            {
                // Primera vez, o el evento se repite (puede que mientras lo anterior aún se recoge).
                if (!built || ending) Build(e.ShelfPosition);
            }
            else if (built && !ending)
            {
                ending = true;
                endedAt = Time.time;
                GameEventAnnouncer.ClearCard(e, "¡AGOTADA!");
                if (arrowAnimator != null) arrowAnimator.SetBool(ArrowVisibleId, false);
            }
        }

        void OnLootGrabbed(PlayerLoot loot, bool doubled, Vector3 position, int points)
        {
            if (!doubled || !built || ending) return;
            if (Vector3.Distance(Flat(position), Flat(shelfCenter)) > 1f) return;
            if (loot != null && loot.IsOwner) CameraFX.Play(grabFx, 1f, accent);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        // ---------------- Montaje ----------------

        void Build(Vector3 shelfPosition)
        {
            if (root != null) Destroy(root.gameObject);
            sparkles = null;
            ending = false;
            shelfYaw = Quaternion.identity;
            footprintHalf = new Vector2(0.33f, 1.45f);
            built = true;
            MeasureShelf(shelfPosition);

            root = new GameObject("[DoublePointsVFX]").transform;

            Material flat = theme.flatMaterial;
            if (flat != null)
            {
                sparkles = new FlatPiece[Mathf.Max(0, sparkleCount)];
                sparkleBorn = new float[sparkles.Length];
                sparkleFrom = new Vector3[sparkles.Length];
                Mesh sparkleMesh = FlatMeshes.Burst(3, 4, 0.25f, 0f, 0.12f);
                for (int i = 0; i < sparkles.Length; i++)
                {
                    sparkles[i] = new FlatPiece("Sparkle", root, sparkleMesh, flat).Colors(theme.cream, sparkleTip, theme.ink);
                    sparkleBorn[i] = -100f;
                }
            }

            if (arrow == null && theme.floorArrowPrefab != null)
            {
                arrow = Instantiate(theme.floorArrowPrefab);
                arrowAnimator = arrow.GetComponent<Animator>();
            }
            if (arrow != null)
            {
                // Colores de partida (los del PSD, sin teñir) guardados una vez para el desvanecido.
                if (arrowRenderers == null)
                {
                    arrowRenderers = arrow.GetComponentsInChildren<SpriteRenderer>(true);
                    arrowColors = new Color[arrowRenderers.Length];
                    for (int i = 0; i < arrowRenderers.Length; i++) arrowColors[i] = arrowRenderers[i].color;
                }
                SetArrowAlpha(1f);
            }
        }

        void MeasureShelf(Vector3 position)
        {
            LootPoint best = null;
            float bestDistance = 1f;
            foreach (LootPoint lp in FindObjectsByType<LootPoint>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(lp.transform.position, position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = lp;
                }
            }

            Bounds bounds = new(position, new Vector3(0.65f, 1f, 2.9f));
            if (best != null && best.TryGetComponent(out Collider col))
            {
                bounds = col.bounds;
                if (col is BoxCollider box)
                {
                    Vector3 size = Vector3.Scale(box.size, best.transform.lossyScale);
                    footprintHalf = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 0.5f;
                    shelfYaw = Quaternion.Euler(0f, best.transform.eulerAngles.y, 0f);
                }
                else footprintHalf = new Vector2(bounds.extents.x, bounds.extents.z);
            }
            shelfCenter = bounds.center;

            // Suelo: rayo hacia abajo justo al lado de la estantería (desde dentro chocaría con sus baldas).
            floorY = bounds.min.y - 0.5f;
            Vector3 side = bounds.center + shelfYaw * Vector3.right * (footprintHalf.x + 0.45f);
            if (Physics.Raycast(side + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                floorY = hit.point.y;

            shelfTop = Mathf.Max(bounds.max.y, floorY + 1.6f);
        }

        // ---------------- Cada frame ----------------

        void LateUpdate()
        {
            if (!built) return;
            float time = Time.time;

            if (!ending && evt != null && evt.IsActive)
                GameEventAnnouncer.SetCard(evt, cardTitle, $"QUEDAN {evt.Remaining}", accent);

            UpdateArrow();
            UpdateSparkles(time);

            if (ending && time - endedAt > 1.5f)
            {
                if (root != null) Destroy(root.gameObject);
                root = null;
                built = false;
            }
        }

        void UpdateArrow()
        {
            if (arrow == null) return;
            if ((localPlayer == null || !localPlayer.IsSpawned) && Time.time >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.time + 0.5f;
                localPlayer = null;
                foreach (PlayerController p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (p.IsSpawned && p.IsOwner) { localPlayer = p; break; }
            }

            bool show = false;
            if (!ending && localPlayer != null)
            {
                Vector3 feet = localPlayer.transform.position;
                Vector3 to = Flat(shelfCenter) - Flat(feet);
                // Visible hasta que la estantería se vacíe (también al lado de ella).
                show = evt != null && evt.IsActive;
                if (to.sqrMagnitude > 1e-4f)
                {
                    // La puntita del anillo apunta a +X de la raíz: se gira para que +X mire a la estantería.
                    arrow.transform.SetPositionAndRotation(feet,
                        Quaternion.LookRotation(to.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f));
                }

                // Transparente al acercarse: distancia al borde de la estantería (no a su centro).
                Vector3 local = Quaternion.Inverse(shelfYaw) * (feet - shelfCenter);
                float dx = Mathf.Max(0f, Mathf.Abs(local.x) - footprintHalf.x);
                float dz = Mathf.Max(0f, Mathf.Abs(local.z) - footprintHalf.y);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ringFadeNear, ringFadeFar, Mathf.Sqrt(dx * dx + dz * dz)));
                SetArrowAlpha(alpha);
            }
            if (arrowAnimator != null) arrowAnimator.SetBool(ArrowVisibleId, show);
        }

        void SetArrowAlpha(float alpha)
        {
            if (arrowRenderers == null) return;
            for (int i = 0; i < arrowRenderers.Length; i++)
            {
                if (arrowRenderers[i] == null) continue;
                Color c = arrowColors[i];
                c.a *= alpha;
                arrowRenderers[i].color = c;
            }
        }

        void UpdateSparkles(float time)
        {
            if (sparkles == null) return;
            if (!ending && time - lastSparkle > sparkleInterval && sparkles.Length > 0)
            {
                lastSparkle = time;
                int i = nextSparkle++ % sparkles.Length;
                sparkleBorn[i] = time;
                Vector3 local = new(Random.Range(-1f, 1f) * (footprintHalf.x + 0.1f), 0f,
                    Random.Range(-1f, 1f) * footprintHalf.y);
                sparkleFrom[i] = new Vector3(shelfCenter.x, Random.Range(floorY + 0.5f, shelfTop), shelfCenter.z) + shelfYaw * local;
            }

            for (int i = 0; i < sparkles.Length; i++)
            {
                float t = (time - sparkleBorn[i]) / 0.9f;
                bool alive = t >= 0f && t < 1f;
                sparkles[i].Visible = alive;
                if (!alive) continue;
                Transform tr = sparkles[i].Transform;
                tr.position = sparkleFrom[i] + Vector3.up * FlatFx.OutCubic(t) * 0.7f;
                tr.rotation = FlatFx.Billboard((sparkleBorn[i] * 97f % 90f) + t * 120f);
                tr.localScale = Vector3.one * Mathf.Sin(t * Mathf.PI) * 0.17f;
            }
        }
    }
}
