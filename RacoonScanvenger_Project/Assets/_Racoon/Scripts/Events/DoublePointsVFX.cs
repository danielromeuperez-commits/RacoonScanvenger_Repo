using Racoon.Cameras;
using Racoon.Player;
using System.Collections.Generic;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racoon.Events
{
    /// <summary>
    /// Visuales del evento «OFERTA x2» en cada cliente (lee lo que replica <see cref="eventDoublePoints"/>):
    ///  - Tarjeta de estado en el lateral de la pantalla con «QUEDAN 7» (<see cref="GameEventAnnouncer.SetCard"/>); late
    ///    con cada recolección doble y al agotarse dice «¡AGOTADA!».
    ///  - Anillo en el suelo bajo tu jugador con una puntita hacia la estantería (prefab PF_VFX_EventFloorArrow, con su
    ///    Animator). Se queda mientras la estantería tenga recolecciones x2 y se vuelve transparente al acercarte a ella.
    ///  - Toda la estantería (productos incluidos) brilla en amarillo con el mismo shader que la bebida energética
    ///    (CameraFXEnergyOverlay), y un golpe de cámara si eres tú quien la saquea.
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

        [Header("Brillo de la estantería")]
        [SerializeField] Color glowDeep = new(0.85f, 0.5f, 0.02f);
        [SerializeField] Color glowMid = new(1f, 0.8f, 0.12f);
        [SerializeField] Color glowLight = new(1f, 0.97f, 0.62f);
        [Tooltip("Hinchado del brillo sobre la malla (m).")]
        [SerializeField, Min(0f)] float glowPush = 0.008f;
        [SerializeField, Min(0.01f)] float glowFadeIn = 0.35f;
        [SerializeField, Min(0.01f)] float glowFadeOut = 0.6f;

        [Tooltip("Golpe de cámara al saquear tú la estantería x2.")]
        [SerializeField] CameraFXProfile grabFx = new(0.35f, decayPower: 2f)
        {
            priority = 36, duck = 0.4f, edge = 0.25f, flash = 0.04f,
        };

        eventDoublePoints evt;
        GameEventTheme theme;
        Color accent;

        const string GlowShaderPath = "CameraFX/CameraFXEnergyOverlay";
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int FXTimeId = Shader.PropertyToID("_FXTime");

        // Brillo montado: copia de cada malla de la estantería con el material del brillo.
        Material glowMaterial;
        readonly List<(Renderer source, MeshRenderer copy)> glowCopies = new();
        float glowLevel;

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
            ClearGlow();
            if (glowMaterial != null) Destroy(glowMaterial);
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
            ClearGlow();
            glowLevel = 0f;
            ending = false;
            shelfYaw = Quaternion.identity;
            footprintHalf = new Vector2(0.33f, 1.45f);
            built = true;
            LootPoint shelf = evt != null ? evt.Shelf : null;
            MeasureShelf(shelfPosition, shelf);
            BuildGlow(shelf);

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

        void MeasureShelf(Vector3 position, LootPoint best)
        {

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
            UpdateGlow(time);

            if (ending && time - endedAt > Mathf.Max(1.5f, glowFadeOut))
            {
                ClearGlow();
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

        // ---------------- Brillo amarillo ----------------

        void BuildGlow(LootPoint shelf)
        {
            if (shelf == null) return;
            if (glowMaterial == null)
            {
                var shader = Resources.Load<Shader>(GlowShaderPath);
                if (shader == null)
                {
                    Debug.LogWarning($"DoublePointsVFX: falta el shader Resources/{GlowShaderPath}.", this);
                    return;
                }
                glowMaterial = new Material(shader) { name = "CameraFX DoublePointsGlow" };
            }
            glowMaterial.SetColor("_Deep", glowDeep);
            glowMaterial.SetColor("_Mid", glowMid);
            glowMaterial.SetColor("_Light", glowLight);
            glowMaterial.SetFloat("_Push", glowPush);
            glowMaterial.SetFloat(IntensityId, 0f);

            // También los productos ocultos, para que brillen al reaparecer. Cada copia cuelga de su malla: cuando
            // un producto desaparece, su brillo se va con él.
            foreach (MeshRenderer source in shelf.VisualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (source.name.EndsWith("_x2")) continue;
                if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                if (source.isPartOfStaticBatch)
                {
                    // La malla está fusionada con el resto de la escena: no se puede copiar suelta.
                    Debug.LogWarning($"DoublePointsVFX: '{source.name}' está en un static batch; quita «Batching Static» del prefab para que brille.", source);
                    continue;
                }

                var go = new GameObject(source.name + "_x2") { layer = source.gameObject.layer };
                go.transform.SetParent(source.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = go.AddComponent<MeshRenderer>();
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                copy.lightProbeUsage = LightProbeUsage.Off;
                copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var materials = new Material[filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = glowMaterial;
                copy.sharedMaterials = materials;
                copy.enabled = false;
                glowCopies.Add((source, copy));
            }
        }

        void UpdateGlow(float time)
        {
            if (glowCopies.Count == 0 || glowMaterial == null) return;

            bool on = !ending && evt != null && evt.IsActive;
            glowLevel = Mathf.MoveTowards(glowLevel, on ? 1f : 0f, Time.deltaTime / (on ? glowFadeIn : glowFadeOut));
            // Respira suave para que se note «viva».
            float intensity = glowLevel * (0.85f + 0.15f * Mathf.Sin(time * 4f));

            bool show = glowLevel > 0.001f;
            foreach (var (source, copy) in glowCopies)
                if (copy != null) copy.enabled = show && source != null && source.enabled;
            if (!show) return;

            glowMaterial.SetFloat(IntensityId, intensity);
            glowMaterial.SetFloat(FXTimeId, time);
            glowMaterial.SetVector(OriginId, shelfCenter);
        }

        void ClearGlow()
        {
            foreach (var (_, copy) in glowCopies)
                if (copy != null) Destroy(copy.gameObject);
            glowCopies.Clear();
        }
    }
}
