using System.Collections;
using Racoon.Feedback;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    public class ItemBox : NetworkBehaviour
    {
        [Header("Items")]
        [Tooltip("Objetos que puede entregar esta Item Box.")]
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

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al RECOGER la caja: «pop» de cartón que se abre + brillito corto. Lo oyen todos, sale de la caja.")]
        [SerializeField] SfxCue pickupSfx = new();
        [Tooltip("SFX al REAPARECER la caja: «bloop» suave, como algo que se infla. Lo oyen todos, sale de la caja.")]
        [SerializeField] SfxCue respawnSfx = new();

        NetworkVariable<bool> available = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        Renderer[] renderers;

        Vector3 initialLocalPosition;
        Vector3 initialLocalScale;

        // Lo que se está mostrando ahora mismo (null = aún nada). El estado real es 'available'.
        bool? shown;
        Coroutine popRoutine;
        // Hacia dónde vuela la caja al recogerla (el jugador). Puede llegar después de empezar el pop.
        Transform popTarget;

        void Awake()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider>();

            renderers = GetComponentsInChildren<Renderer>(true);

            if (animatedVisual == null)
                animatedVisual = transform;

            initialLocalPosition = animatedVisual.localPosition;
            initialLocalScale = animatedVisual.localScale;
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

        void Update()
        {
            AnimateBox();
        }

        void AnimateBox()
        {
            if (animatedVisual == null || shown != true)
                return;

            // Giro continuo sobre el eje Y.
            animatedVisual.Rotate(
                Vector3.up,
                rotationSpeed * Time.deltaTime,
                Space.Self
            );

            // Movimiento suave de subida y bajada.
            float offset =
                Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f)
                * bobHeight;

            Vector3 position = initialLocalPosition;
            position.y += offset;

            animatedVisual.localPosition = position;
        }

        void OnTriggerEnter(Collider other)
        {
            // Solo el servidor puede recoger la caja.
            if (!IsServer || !available.Value)
                return;

            PlayerController player =
                other.GetComponentInParent<PlayerController>();

            if (player == null)
                return;

            // Si no hay objetos configurados, no hacemos nada.
            ItemData item = GetRandomItem();

            if (item == null)
            {
                Debug.LogWarning(
                    "ItemBox: no hay ningún ItemData válido en Possible Items.",
                    this
                );

                return;
            }

            // Si el inventario está lleno, la caja tampoco se recoge.
            if (player.Inventory == null || player.Inventory.IsFull)
                return;

            // Intentamos añadir el objeto al inventario.
            if (!player.Inventory.ServerTryAddItem(item))
                return;

            // La caja desaparece para todos (con su pop hacia quien la ha cogido).
            available.Value = false;
            PickedUpRpc(player.NetworkObjectId);

            if (respawnTime > 0f)
                StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnTime);
            available.Value = true;
        }

        ItemData GetRandomItem()
        {
            if (possibleItems == null || possibleItems.Length == 0)
                return null;

            int validCount = 0;

            foreach (ItemData item in possibleItems)
            {
                if (item != null)
                    validCount++;
            }

            if (validCount == 0)
                return null;

            int selectedIndex = Random.Range(0, validCount);

            foreach (ItemData item in possibleItems)
            {
                if (item == null)
                    continue;

                if (selectedIndex == 0)
                    return item;

                selectedIndex--;
            }

            return null;
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
            const float swellPart = 0.35f;

            for (float time = 0f; time < pickupPopTime; time += Time.deltaTime)
            {
                float t = time / pickupPopTime;

                if (t < swellPart)
                {
                    float k = Mathf.Sin(t / swellPart * Mathf.PI * 0.5f);
                    float swell = Mathf.Lerp(1f, pickupPopScale, k);
                    animatedVisual.localScale = Vector3.Scale(
                        initialLocalScale,
                        new Vector3(swell, Mathf.Lerp(1f, 0.85f, k) * swell, swell)
                    );
                }
                else
                {
                    float k = (t - swellPart) / (1f - swellPart);
                    float eased = k * k;
                    animatedVisual.localScale = initialLocalScale * Mathf.Lerp(pickupPopScale, 0f, eased);

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
                float t = time / respawnPopTime - 1f;
                float back = 1f + 2.70158f * t * t * t + 1.70158f * t * t;
                animatedVisual.localScale = initialLocalScale * back;
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
    }
}
