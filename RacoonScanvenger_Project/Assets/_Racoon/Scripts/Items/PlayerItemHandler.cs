using System.Collections;
using System.Collections.Generic;
using Racoon.Feedback;
using Racoon.Items;
using Racoon.VFX;
using UnityEngine;
using Unity.Netcode;

namespace Racoon.Player
{
    /// <summary>
    /// Parte visual del uso de objetos:
    ///  - El modelo en la mano (lo ven TODOS los clientes): entra con un pop, se balancea y, al cambiar de objeto,
    ///    el anterior se guarda a la espalda; al usarlo, se encoge (el de verdad ya lo ha creado el servidor).
    ///  - Mientras apuntas (solo el dueño): arco de puntos con la trayectoria real y un anillo donde caerá.
    ///    Con el stick hacia atrás el arco pasa a «soltar detrás» (otro color).
    /// Cuándo se usa el objeto lo decide PlayerController (mantener Usar = apuntar, soltar = usar);
    /// aquí solo se lee <see cref="PlayerController.IsAimingItem"/> / <see cref="PlayerController.IsAimingBehind"/>.
    /// </summary>
    public class PlayerItemHandler : NetworkBehaviour
    {
        [Header("Referencias")]
        [SerializeField] PlayerInventory inventory;
        [SerializeField] PlayerController controller;

        [Header("Puntos del jugador")]
        [Tooltip("Punto donde se verá el objeto equipado.")]
        [SerializeField] Transform itemHoldPoint;

        [Tooltip("Punto desde donde se lanzarán los objetos.")]
        [SerializeField] Transform throwPoint;

        [Header("Objeto en la mano")]
        [Tooltip("Duración del pop al aparecer en la mano.")]
        [SerializeField, Min(0.01f)] float equipPopTime = 0.25f;
        [Tooltip("Balanceo en reposo (grados).")]
        [SerializeField, Min(0f)] float swayDegrees = 7f;
        [Tooltip("Bote en reposo (metros, locales del punto de agarre).")]
        [SerializeField, Min(0f)] float swayBob = 0.015f;

        [Header("Apuntado")]
        [Tooltip("Opcional: flecha que aparece mientras mantenemos pulsado Use con un objeto.")]
        [SerializeField] GameObject aimArrow;

        [Tooltip("Segundos manteniendo Use antes de enseñar el apuntado. Así un toque rápido no lo hace parpadear.")]
        [SerializeField, Min(0f)] float aimArrowDelay = 0.12f;

        [Tooltip("M_VFX_ToonFlat. Arco de puntos y anillo de caída (sin él no salen).")]
        [SerializeField] Material flatMaterial;

        [Tooltip("Capas contra las que se calcula dónde cae el objeto.")]
        [SerializeField] LayerMask arcGroundMask = Physics.DefaultRaycastLayers;

        [Tooltip("Metros entre punto y punto del arco.")]
        [SerializeField, Min(0.05f)] float arcDotSpacing = 0.35f;

        [Tooltip("Tamaño de cada punto del arco (metros).")]
        [SerializeField, Min(0.01f)] float arcDotSize = 0.09f;

        [Tooltip("Radio del anillo donde caerá (metros).")]
        [SerializeField, Min(0.05f)] float landingRingRadius = 0.3f;

        [SerializeField] Color arcColor = Color.white;
        [Tooltip("Color del arco al soltarlo detrás.")]
        [SerializeField] Color arcBehindColor = new(1f, 0.6f, 0.2f);

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al CAMBIAR de objeto: «swish» corto de meter una cosa en la mochila y sacar otra. Lo oyen todos, sale del jugador.")]
        [SerializeField] SfxCue switchSfx = new();

        const int MaxArcDots = 28;
        const float ArcMaxTime = 2f, ArcStep = 0.03f, ArcMarchSpeed = 1.2f;

        GameObject currentHeldVisual;
        ItemData currentItem;
        Vector3 heldRestScale;
        float heldPopStart = -99f;
        int lastCount;
        float aimHeldTime;

        // Apuntado (solo dueño)
        GameObject arcRoot;
        readonly List<FlatPiece> arcDots = new();
        FlatPiece landingRing;
        readonly List<Vector3> arcPoints = new();

        void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventory>();

            if (controller == null)
                controller = GetComponent<PlayerController>();

            if (aimArrow != null)
                aimArrow.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            inventory.EquippedItemChanged += OnEquippedItemChanged;

            lastCount = inventory.Count;
            OnEquippedItemChanged(inventory.EquippedItem);
        }

        public override void OnNetworkDespawn()
        {
            inventory.EquippedItemChanged -= OnEquippedItemChanged;
        }

        public override void OnDestroy()
        {
            if (arcRoot != null)
                Destroy(arcRoot);

            base.OnDestroy();
        }

        // ---------------- Objeto en la mano ----------------

        void OnEquippedItemChanged(ItemData item)
        {
            int count = inventory.Count;
            bool used = count < lastCount;
            lastCount = count;

            // El inventario puede avisar dos veces seguidas con el mismo objeto.
            if (item == currentItem && (currentHeldVisual != null || item == null))
                return;

            bool switched = !used && currentItem != null && item != null;
            if (switched)
                switchSfx.Play(transform.position);

            if (currentHeldVisual != null)
                StartCoroutine(used ? ShrinkAway(currentHeldVisual) : PutAway(currentHeldVisual));

            currentHeldVisual = null;
            currentItem = item;

            if (item == null || item.heldVisualPrefab == null || itemHoldPoint == null)
                return;

            currentHeldVisual = InstantiateVisual(item.heldVisualPrefab, itemHoldPoint);
            currentHeldVisual.transform.localPosition = Vector3.zero;
            currentHeldVisual.transform.localRotation = Quaternion.identity;
            heldRestScale = currentHeldVisual.transform.localScale;
            heldPopStart = Time.time;
            ApplyHeldAnimation();
        }

        // Copia SOLO visual: si el prefab es un objeto de red (p. ej. PF_BananaTrap) se le quitan scripts,
        // física y colliders. Se instancia bajo un padre desactivado para que esos scripts no lleguen a arrancar.
        static GameObject InstantiateVisual(GameObject prefab, Transform parent)
        {
            var holder = new GameObject("HeldVisualHolder");
            holder.SetActive(false);
            GameObject go = Instantiate(prefab, holder.transform);

            // Primero los de red y al revés del orden en que se añadieron (los que dependen de otros van detrás).
            MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (behaviours[i] is NetworkBehaviour) DestroyImmediate(behaviours[i]);
            behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                DestroyImmediate(behaviours[i]);
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))
                DestroyImmediate(body);

            go.transform.SetParent(parent, false);
            Destroy(holder);
            return go;
        }

        void LateUpdate()
        {
            ApplyHeldAnimation();
        }

        // Pop de entrada + balanceo en reposo.
        void ApplyHeldAnimation()
        {
            if (currentHeldVisual == null)
                return;

            Transform held = currentHeldVisual.transform;
            float pop = FlatFx.OutBack((Time.time - heldPopStart) / equipPopTime, 2.2f);
            held.localScale = heldRestScale * pop;

            float t = Time.time;
            held.localRotation = Quaternion.Euler(
                Mathf.Sin(t * 2.1f) * swayDegrees * 0.6f,
                Mathf.Sin(t * 1.3f) * swayDegrees,
                Mathf.Sin(t * 1.7f) * swayDegrees * 0.7f);
            held.localPosition = Vector3.up * (Mathf.Sin(t * 3.2f) * swayBob);
        }

        // Usado: se encoge en el sitio (el objeto de verdad ya sale del servidor).
        static IEnumerator ShrinkAway(GameObject visual)
        {
            Vector3 scale = visual.transform.localScale;
            for (float time = 0f; time < 0.1f && visual != null; time += Time.deltaTime)
            {
                visual.transform.localScale = scale * (1f - time / 0.1f);
                yield return null;
            }

            if (visual != null)
                Destroy(visual);
        }

        // Cambiado: da un saltito hacia la espalda del jugador encogiéndose, como si lo guardara en la mochila.
        IEnumerator PutAway(GameObject visual)
        {
            Transform held = visual.transform;
            held.SetParent(null, true);
            Vector3 start = held.position;
            Vector3 scale = held.localScale;
            const float duration = 0.25f;

            for (float time = 0f; time < duration && visual != null; time += Time.deltaTime)
            {
                float t = time / duration;
                Vector3 back = transform.position - transform.forward * 0.3f + Vector3.up * 1.1f;
                held.position = Vector3.Lerp(start, back, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.35f);
                held.localScale = scale * (1f - t * t);
                yield return null;
            }

            if (visual != null)
                Destroy(visual);
        }

        // ---------------- Apuntado (solo dueño) ----------------

        void Update()
        {
            if (!IsOwner)
                return;

            UpdateAim();
        }

        void UpdateAim()
        {
            bool aiming = controller != null && controller.IsAimingItem;
            aimHeldTime = aiming ? aimHeldTime + Time.deltaTime : 0f;
            bool show = aiming && aimHeldTime >= aimArrowDelay;

            UpdateAimArrow(show);
            UpdateArc(show);
        }

        void UpdateAimArrow(bool show)
        {
            if (aimArrow == null)
                return;

            show &= throwPoint != null;
            if (aimArrow.activeSelf != show)
                aimArrow.SetActive(show);

            if (!show)
                return;

            aimArrow.transform.position = throwPoint.position;

            Vector3 direction = transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                aimArrow.transform.rotation =
                    Quaternion.LookRotation(direction);
            }
        }

        // Simula la misma trayectoria que usará el servidor (ItemEffect.TryGetThrow + gravedad) hasta tocar algo.
        void UpdateArc(bool show)
        {
            ItemEffect effect = show && inventory.EquippedItem != null ? inventory.EquippedItem.Effect : null;
            bool behind = controller != null && controller.IsAimingBehind;

            if (effect == null || flatMaterial == null ||
                !effect.TryGetThrow(controller, behind, out Vector3 start, out Vector3 velocity))
            {
                HideArc();
                return;
            }

            EnsureArc();

            arcPoints.Clear();
            arcPoints.Add(start);
            Vector3 previous = start;
            bool landed = false;
            Vector3 landing = start, landingNormal = Vector3.up;

            for (float t = ArcStep; t <= ArcMaxTime; t += ArcStep)
            {
                Vector3 point = start + velocity * t + 0.5f * t * t * Physics.gravity;

                if (Physics.Linecast(previous, point, out RaycastHit hit, arcGroundMask, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<PlayerController>() != controller)
                {
                    landing = hit.point;
                    landingNormal = hit.normal;
                    arcPoints.Add(landing);
                    landed = true;
                    break;
                }

                arcPoints.Add(point);
                previous = point;
            }

            Color color = behind ? arcBehindColor : arcColor;
            Color outline = new(0.12f, 0.1f, 0.18f);
            PlaceDots(color, outline);

            landingRing.Visible = landed;

            if (landed)
            {
                landingRing.Transform.SetPositionAndRotation(
                    landing + landingNormal * 0.02f,
                    Quaternion.FromToRotation(Vector3.up, landingNormal));
                float beat = 1f + 0.1f * Mathf.Sin(Time.time * 10f);
                landingRing.Transform.localScale = Vector3.one * landingRingRadius * beat;
                landingRing.Colors(color, Color.Lerp(color, Color.white, 0.5f), outline);
            }
        }

        // Puntos repartidos a distancia fija a lo largo del arco, avanzando poco a poco (efecto «hormigas»).
        void PlaceDots(Color color, Color outline)
        {
            float march = Mathf.Repeat(Time.time * ArcMarchSpeed, 1f) * arcDotSpacing;
            float nextDistance = march;
            float travelled = 0f;
            int dot = 0;

            for (int i = 1; i < arcPoints.Count && dot < arcDots.Count; i++)
            {
                Vector3 a = arcPoints[i - 1], b = arcPoints[i];
                float segment = Vector3.Distance(a, b);

                while (nextDistance <= travelled + segment && dot < arcDots.Count)
                {
                    float k = segment > 0f ? (nextDistance - travelled) / segment : 0f;
                    FlatPiece piece = arcDots[dot++];
                    piece.Transform.SetPositionAndRotation(Vector3.Lerp(a, b, k), FlatFx.Billboard());
                    piece.Transform.localScale = Vector3.one * arcDotSize;
                    piece.Colors(color, color, outline);
                    piece.Visible = true;
                    nextDistance += arcDotSpacing;
                }

                travelled += segment;
            }

            for (; dot < arcDots.Count; dot++)
                arcDots[dot].Visible = false;
        }

        void EnsureArc()
        {
            if (arcRoot != null)
                return;

            // Fuera del jugador: no gira ni escala con él.
            arcRoot = new GameObject(name + " (Aim)");
            Mesh dotMesh = FlatMeshes.Burst(2, 10, 1f, 0f, 0.18f);

            for (int i = 0; i < MaxArcDots; i++)
                arcDots.Add(new FlatPiece("ArcDot", arcRoot.transform, dotMesh, flatMaterial, 2));

            landingRing = new FlatPiece("LandingRing", arcRoot.transform, FlatMeshes.Arc(1f, 0f, 360f, 0.12f, 0f, 40), flatMaterial, 1);
        }

        void HideArc()
        {
            if (arcRoot == null)
                return;

            foreach (FlatPiece dot in arcDots)
                dot.Visible = false;

            landingRing.Visible = false;
        }
    }
}
