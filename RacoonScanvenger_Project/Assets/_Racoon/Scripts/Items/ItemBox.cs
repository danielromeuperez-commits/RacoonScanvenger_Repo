using System.Collections;
using Racoon.Feedback;
using Racoon.Player;
using Racoon.VFX;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Caja de objetos. El SERVIDOR decide quién la coge, qué objeto le toca (con peso según su posición en
    /// puntos, ver ItemData.boxWeightLeader/Last) y cuándo reaparece. Lo demás es visual y local:
    ///  - Giro y bote sincronizados con el reloj del servidor: todas las cajas giran a la vez en todas las pantallas.
    ///  - Anticipación: gira más rápido y crece un poco cuando TU jugador se acerca.
    ///  - «?» en las cuatro caras, sombra en el suelo y, mientras no está, un anillo que se llena hasta que vuelve.
    ///  - Pop al recogerla (vuela hacia quien la coge) y al reaparecer; «no-no» si llegas con el inventario lleno.
    /// La sombra y el anillo usan M_VFX_ToonFlat (campo Flat Material); sin él simplemente no salen.
    /// </summary>
    public class ItemBox : NetworkBehaviour
    {
        [Header("Items")]
        [Tooltip("Objetos que puede entregar esta Item Box. La probabilidad de cada uno sale de sus pesos en el ItemData " +
                 "(Box Weight Leader / Last) según la posición en puntos de quien la coge.")]
        [SerializeField] ItemData[] possibleItems;

        [Header("Animación")]
        [Tooltip("Objeto visual que gira y sube/baja.")]
        [SerializeField] Transform animatedVisual;

        [Tooltip("Velocidad de giro en grados por segundo.")]
        [SerializeField] float rotationSpeed = 90f;

        [Tooltip("Cuánto sube y baja la caja desde su posición inicial.")]
        [SerializeField] float bobHeight = 0.25f;

        [Tooltip("Velocidad de la animación de subida y bajada, en ciclos por segundo.")]
        [SerializeField] float bobSpeed = 1f;

        [Header("Anticipación (tu jugador se acerca)")]
        [Tooltip("Distancia a la que empieza a reaccionar (al máximo a la mitad de esta distancia).")]
        [SerializeField, Min(0.1f)] float anticipationRadius = 4f;
        [Tooltip("Multiplicador del giro con tu jugador al lado.")]
        [SerializeField, Min(1f)] float anticipationSpin = 3f;
        [Tooltip("Cuánto crece con tu jugador al lado (0,12 = un 12 %).")]
        [SerializeField, Min(0f)] float anticipationScale = 0.12f;

        [Header("Pickup")]
        [SerializeField] Collider pickupCollider;

        [Header("Respawn")]
        [Tooltip("Segundos hasta que la caja vuelve a aparecer tras recogerla. 0 = no vuelve.")]
        [SerializeField, Min(0f)] float respawnTime = 8f;

        [Header("Pop al recoger / reaparecer")]
        [Tooltip("Duración del pop al recogerla: se hincha y sale volando hacia el jugador encogiéndose.")]
        [SerializeField, Min(0.05f)] float pickupPopTime = 0.3f;
        [Tooltip("Escala máxima del hinchado al recogerla.")]
        [SerializeField, Min(1f)] float pickupPopScale = 1.35f;
        [Tooltip("Duración del pop al reaparecer (crece desde 0 con un pequeño rebote).")]
        [SerializeField, Min(0.05f)] float respawnPopTime = 0.4f;
        [Tooltip("Opcional: VFX al recogerla (confeti, basurilla...). Se crea en todos los clientes.")]
        [SerializeField] GameObject pickupVfxPrefab;
        [Tooltip("Opcional: VFX al reaparecer.")]
        [SerializeField] GameObject respawnVfxPrefab;
        [Tooltip("Segundos hasta destruir los VFX instanciados.")]
        [SerializeField, Min(0.1f)] float vfxLifeTime = 2f;

        [Header("Inventario lleno")]
        [Tooltip("Texto que ve SOLO quien llega con el inventario lleno (vacío = sin texto).")]
        [SerializeField] string inventoryFullText = "¡LLENO!";
        [SerializeField] Color inventoryFullTextColor = new(1f, 0.45f, 0.4f);

        [Header("Decoración")]
        [Tooltip("M_VFX_ToonFlat. Sombra en el suelo y anillo de reaparición (sin él no salen).")]
        [SerializeField] Material flatMaterial;
        [Tooltip("«?» en las cuatro caras de la caja.")]
        [SerializeField] bool questionMarks = true;
        [SerializeField] Color questionMarkColor = Color.white;
        [Tooltip("Opcional: fuente de los «?». Vacío = la de TMP por defecto.")]
        [SerializeField] TMP_FontAsset questionMarkFont;
        [Tooltip("Diámetro de la sombra respecto al ancho de la caja.")]
        [SerializeField, Min(0f)] float shadowSize = 1.1f;
        [SerializeField] Color shadowColor = new(0.12f, 0.1f, 0.18f);
        [Tooltip("Radio del anillo de reaparición (metros).")]
        [SerializeField, Min(0.05f)] float respawnRingRadius = 0.45f;
        [SerializeField] Color respawnRingColor = new(1f, 0.85f, 0.3f);

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al RECOGER la caja: «pop» de cartón que se abre + brillito corto. Lo oyen todos, sale de la caja.")]
        [SerializeField] SfxCue pickupSfx = new();
        [Tooltip("SFX al REAPARECER la caja: «bloop» suave, como algo que se infla. Lo oyen todos, sale de la caja.")]
        [SerializeField] SfxCue respawnSfx = new();
        [Tooltip("SFX al tocarla con el INVENTARIO LLENO: «bzzt» de error corto y gracioso. Lo oyen todos, sale de la caja.")]
        [SerializeField] SfxCue inventoryFullSfx = new();

        // Tiempos del «no-no» con el inventario lleno.
        const float ShakeTime = 0.4f, InventoryFullCooldown = 0.75f;

        NetworkVariable<bool> available = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Momento (reloj del servidor) en que reaparece: para que el anillo se llene igual en todos los clientes.
        NetworkVariable<double> respawnAt = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        Renderer[] renderers;

        Vector3 initialLocalPosition;
        Vector3 initialLocalScale;
        Quaternion initialLocalRotation;

        // Lo que se está mostrando ahora mismo (null = aún nada). El estado real es 'available'.
        bool? shown;
        Coroutine popRoutine;
        // Hacia dónde vuela la caja al recogerla (el jugador). Puede llegar después de empezar el pop.
        Transform popTarget;

        // Anticipación (0-1, suavizada) y giro extra que acumula.
        float nearness;
        float extraSpin;
        float shakeStart = -99f;
        float lastInventoryFullTime = -99f;

        // Decoración fuera de la caja (no gira ni bota con ella).
        GameObject fxRoot;
        FlatPiece shadow;
        FlatPiece respawnRing;
        bool hasGround;
        Vector3 groundPoint;

        void Awake()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider>();

            if (animatedVisual == null)
                animatedVisual = transform;

            initialLocalPosition = animatedVisual.localPosition;
            initialLocalScale = animatedVisual.localScale;
            initialLocalRotation = animatedVisual.localRotation;

            // Los «?» antes de recoger los renderers: así se esconden con la caja.
            if (questionMarks)
                BuildQuestionMarks();

            renderers = GetComponentsInChildren<Renderer>(true);

            BuildGroundFx();
        }

        void Reset()
        {
            pickupCollider = GetComponent<Collider>();

            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

        public override void OnNetworkSpawn()
        {
            available.OnValueChanged += OnAvailabilityChanged;

            // Al entrar a mitad de partida no se anima: se pone tal cual está.
            SetShown(available.Value, false);
        }

        public override void OnNetworkDespawn()
        {
            available.OnValueChanged -= OnAvailabilityChanged;
        }

        public override void OnDestroy()
        {
            if (fxRoot != null)
                Destroy(fxRoot);

            base.OnDestroy();
        }

        void Update()
        {
            UpdateNearness();
            AnimateBox();
            UpdateGroundFx();
        }

        // Reloj común a todos los clientes (el del servidor); sin red, el local.
        double SharedTime => IsSpawned ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        void UpdateNearness()
        {
            float target = 0f;
            PlayerController local = PlayerController.Local;

            if (local != null && shown == true)
            {
                float distance = Vector3.Distance(local.transform.position, transform.position);
                target = 1f - Mathf.Clamp01((distance - anticipationRadius * 0.5f) / (anticipationRadius * 0.5f));
            }

            nearness = Mathf.MoveTowards(nearness, target, Time.deltaTime * 4f);
        }

        void AnimateBox()
        {
            if (animatedVisual == null || shown != true)
                return;

            // Giro sincronizado con el servidor + el extra de la anticipación + el «no-no».
            extraSpin += rotationSpeed * (anticipationSpin - 1f) * nearness * Time.deltaTime;
            float angle = (float)(SharedTime * rotationSpeed % 360.0) + extraSpin;

            float shake = Time.time - shakeStart;
            if (shake < ShakeTime)
                angle += Mathf.Sin(shake * 45f) * 25f * (1f - shake / ShakeTime);

            animatedVisual.localRotation = initialLocalRotation * Quaternion.Euler(0f, angle, 0f);

            // Bote suave, también sincronizado (sube algo más con tu jugador cerca).
            float offset =
                Mathf.Sin((float)(SharedTime * bobSpeed % 1.0) * Mathf.PI * 2f)
                * bobHeight * (1f + 0.5f * nearness);

            Vector3 position = initialLocalPosition;
            position.y += offset;

            animatedVisual.localPosition = position;

            // Mientras reaparece manda su pop; si no, crece un poco con tu jugador cerca (y late).
            if (popRoutine == null)
            {
                float pulse = 1f + 0.03f * nearness * Mathf.Sin(Time.time * 12f);
                animatedVisual.localScale = initialLocalScale * ((1f + anticipationScale * nearness) * pulse);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            // Solo el servidor puede recoger la caja.
            if (!IsServer || !available.Value)
                return;

            PlayerController player =
                other.GetComponentInParent<PlayerController>();

            if (player == null || player.Inventory == null)
                return;

            // Inventario lleno: la caja no se coge y dice que no.
            if (player.Inventory.IsFull)
            {
                if (Time.time - lastInventoryFullTime >= InventoryFullCooldown)
                {
                    lastInventoryFullTime = Time.time;
                    InventoryFullRpc(player.NetworkObjectId);
                }

                return;
            }

            // Si no hay objetos configurados, no hacemos nada.
            ItemData item = GetRandomItem(player);

            if (item == null)
            {
                Debug.LogWarning(
                    "ItemBox: no hay ningún ItemData válido en Possible Items.",
                    this
                );

                return;
            }

            // Intentamos añadir el objeto al inventario.
            if (!player.Inventory.ServerTryAddItem(item))
                return;

            // La caja desaparece para todos (con su pop hacia quien la ha cogido).
            if (respawnTime > 0f)
            {
                respawnAt.Value = NetworkManager.ServerTime.Time + respawnTime;
                StartCoroutine(RespawnAfterDelay());
            }

            available.Value = false;
            PickedUpRpc(player.NetworkObjectId);
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnTime);
            available.Value = true;
        }

        // Elige con peso según la posición en puntos: cada objeto pesa entre boxWeightLeader (va primero)
        // y boxWeightLast (va último). Con todo a 1 es al azar puro.
        ItemData GetRandomItem(PlayerController player)
        {
            if (possibleItems == null || possibleItems.Length == 0)
                return null;

            float position = PositionFromLeader(player);
            float total = 0f;

            foreach (ItemData item in possibleItems)
            {
                if (item != null)
                    total += item.BoxWeight(position);
            }

            if (total <= 0f)
                return null;

            float pick = Random.value * total;

            foreach (ItemData item in possibleItems)
            {
                if (item == null)
                    continue;

                pick -= item.BoxWeight(position);

                if (pick <= 0f)
                    return item;
            }

            // Por redondeo: el último válido.
            for (int i = possibleItems.Length - 1; i >= 0; i--)
            {
                if (possibleItems[i] != null)
                    return possibleItems[i];
            }

            return null;
        }

        // 0 = va primero (o empatado en cabeza / juega solo), 1 = va último. Cuenta los puntos que tendría si vendiera.
        static float PositionFromLeader(PlayerController player)
        {
            PlayerLoot[] loots = FindObjectsByType<PlayerLoot>(FindObjectsSortMode.None);

            if (loots.Length <= 1 || !player.TryGetComponent(out PlayerLoot mine))
                return 0f;

            int ahead = 0;

            foreach (PlayerLoot loot in loots)
            {
                if (loot != mine && loot.PotentialPoints > mine.PotentialPoints)
                    ahead++;
            }

            return ahead / (float)(loots.Length - 1);
        }

        // Todos los clientes: quién la ha cogido (para que la caja vuele hacia él) + VFX y sonido.
        // El aviso de "has cogido un objeto" ya lo da la ruleta del HUD.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void PickedUpRpc(ulong pickerObjectId)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickerObjectId, out NetworkObject picker))
                popTarget = picker.transform;

            Vector3 position = animatedVisual.position;
            SpawnVfx(pickupVfxPrefab, position);
            pickupSfx.Play(position);

            SetShown(false, true);
        }

        // Todos los clientes: la caja dice que no con la cabeza; el texto solo lo ve quien la ha tocado.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void InventoryFullRpc(ulong playerObjectId)
        {
            shakeStart = Time.time;
            inventoryFullSfx.Play(animatedVisual.position);

            if (string.IsNullOrEmpty(inventoryFullText))
                return;

            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject) &&
                playerObject.IsOwner)
            {
                FloatingText.Spawn(inventoryFullText, animatedVisual.position + Vector3.up * 0.8f, inventoryFullTextColor, 0.45f);
                PlayerRumble.Rumble(playerObject.GetComponent<PlayerController>(), 0.2f, 0.1f, 0.12f);
            }
        }

        void OnAvailabilityChanged(bool previousValue, bool newValue)
        {
            SetShown(newValue, true);

            if (newValue)
            {
                Vector3 position = transform.TransformPoint(initialLocalPosition);
                SpawnVfx(respawnVfxPrefab, position);
                respawnSfx.Play(position);
            }
        }

        // Puede llegar dos veces (variable de red + RPC): solo actúa si cambia lo que se ve.
        void SetShown(bool visible, bool animate)
        {
            if (shown == visible)
                return;

            shown = visible;

            // El collider no espera a la animación.
            if (pickupCollider != null)
                pickupCollider.enabled = visible;

            if (popRoutine != null)
            {
                StopCoroutine(popRoutine);
                popRoutine = null;
            }

            ResetVisual();

            if (!animate)
            {
                SetRenderersEnabled(visible);
                return;
            }

            popRoutine = StartCoroutine(visible ? PopIn() : PopOut());
        }

        // Se hincha (aplastándose un poco) y sale volando hacia el jugador mientras se encoge.
        IEnumerator PopOut()
        {
            SetRenderersEnabled(true);

            Vector3 startPosition = animatedVisual.position;
            Vector3 startScale = animatedVisual.localScale;
            const float swellPart = 0.35f;

            for (float time = 0f; time < pickupPopTime; time += Time.deltaTime)
            {
                float t = time / pickupPopTime;

                if (t < swellPart)
                {
                    float k = Mathf.Sin(t / swellPart * Mathf.PI * 0.5f);
                    float swell = Mathf.Lerp(1f, pickupPopScale, k);
                    animatedVisual.localScale = Vector3.Scale(
                        startScale,
                        new Vector3(swell, Mathf.Lerp(1f, 0.85f, k) * swell, swell)
                    );
                }
                else
                {
                    float k = (t - swellPart) / (1f - swellPart);
                    float eased = k * k;
                    animatedVisual.localScale = startScale * Mathf.Lerp(pickupPopScale, 0f, eased);

                    Vector3 target = popTarget != null
                        ? popTarget.position + Vector3.up
                        : startPosition + Vector3.up * 0.75f;
                    animatedVisual.position = Vector3.Lerp(startPosition, target, eased);
                }

                yield return null;
            }

            SetRenderersEnabled(false);
            ResetVisual();
            popTarget = null;
            popRoutine = null;
        }

        // Crece desde 0 con un rebote al final (ease-out-back).
        IEnumerator PopIn()
        {
            SetRenderersEnabled(true);

            for (float time = 0f; time < respawnPopTime; time += Time.deltaTime)
            {
                animatedVisual.localScale = initialLocalScale * FlatFx.OutBack(time / respawnPopTime, 1.7f);
                yield return null;
            }

            animatedVisual.localScale = initialLocalScale;
            popRoutine = null;
        }

        void ResetVisual()
        {
            if (animatedVisual == null)
                return;

            animatedVisual.localPosition = initialLocalPosition;
            animatedVisual.localScale = initialLocalScale;
        }

        void SetRenderersEnabled(bool enabled)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        void SpawnVfx(GameObject prefab, Vector3 position)
        {
            if (prefab == null)
                return;

            Destroy(Instantiate(prefab, position, Quaternion.identity), vfxLifeTime);
        }

        // ---------------- Decoración ----------------

        // Un «?» en cada cara lateral del cubo (malla de 1×1×1 centrada, como el Cube de Unity).
        void BuildQuestionMarks()
        {
            Vector3[] faces = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            foreach (Vector3 face in faces)
            {
                var go = new GameObject("QuestionMark");
                go.transform.SetParent(animatedVisual, false);
                go.transform.localPosition = face * 0.505f;
                // TMP se lee desde su -Z: lo giramos para que se lea desde fuera de la cara.
                go.transform.localRotation = Quaternion.LookRotation(-face);
                // fontSize 10 ≈ 1 unidad: a 0,7 el «?» ocupa casi toda la cara.
                go.transform.localScale = Vector3.one * 0.7f;

                var text = go.AddComponent<TextMeshPro>();
                if (questionMarkFont != null) text.font = questionMarkFont;
                text.text = "?";
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.fontSize = 10f;
                text.fontStyle = FontStyles.Bold;
                text.color = questionMarkColor;
                text.outlineWidth = 0.25f;
                text.outlineColor = new Color32(30, 20, 40, 255);
                text.rectTransform.sizeDelta = new Vector2(10f, 10f);
            }
        }

        void BuildGroundFx()
        {
            if (flatMaterial == null)
                return;

            hasGround = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (!hasGround)
                return;

            groundPoint = hit.point + Vector3.up * 0.01f;

            fxRoot = new GameObject(name + " (FX)");
            fxRoot.transform.position = groundPoint;

            // Disco plano en el suelo (Burst con puntas iguales = círculo, mirando hacia arriba).
            shadow = new FlatPiece("Shadow", fxRoot.transform, FlatMeshes.Burst(1, 16, 1f, 0f, 0.06f), flatMaterial)
                .Colors(shadowColor, shadowColor, shadowColor * 0.7f);
            shadow.Transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Anillo que se va llenando (Window revela el arco desde 0 hasta el progreso).
            respawnRing = new FlatPiece("RespawnRing", fxRoot.transform, FlatMeshes.Arc(1f, 0f, 360f, 0.08f, 0f, 48), flatMaterial, 1)
                .Colors(respawnRingColor, Color.Lerp(respawnRingColor, Color.white, 0.5f), respawnRingColor);
            respawnRing.Transform.localPosition = Vector3.up * 0.005f;
            respawnRing.Transform.localScale = Vector3.one * respawnRingRadius;
        }

        void UpdateGroundFx()
        {
            if (fxRoot == null)
                return;

            // Sombra: del ancho de la caja, más pequeña cuanto más alta va.
            bool boxVisible = shown == true || popRoutine != null;
            shadow.Visible = boxVisible;

            if (boxVisible)
            {
                float width = Mathf.Abs(animatedVisual.lossyScale.x);
                float height = Mathf.Max(0f, animatedVisual.position.y - groundPoint.y);
                float size = width * shadowSize * 0.5f * Mathf.Clamp(1.2f - height * 0.25f, 0.4f, 1f);
                shadow.Transform.localScale = Vector3.one * size;
            }

            // Anillo: solo mientras espera a reaparecer.
            bool waiting = shown == false && respawnTime > 0f && popRoutine == null;
            respawnRing.Visible = waiting;

            if (waiting)
            {
                float remaining = (float)(respawnAt.Value - SharedTime);
                float progress = 1f - Mathf.Clamp01(remaining / respawnTime);
                respawnRing.Window(progress, 0f);
                // Late cada vez más rápido al acercarse el momento.
                float beat = 1f + 0.06f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 18f, progress));
                respawnRing.Transform.localScale = Vector3.one * respawnRingRadius * beat;
            }
        }
    }
}
