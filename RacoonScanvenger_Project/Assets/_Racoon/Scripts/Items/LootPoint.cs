using System.Collections;
using System.Collections.Generic;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Punto de saqueo (estantería, puesto de fruta, panadería...). Se mantiene pulsado Interact delante de él para ir
/// cogiendo objetos. El balance (puntos, cantidad, reposición, oferta x2) sale del <see cref="LootRarityPreset"/>
/// asignado; si no hay preset se usan los valores sueltos de abajo.
///
/// Los productos se ven desaparecer a medida que se saquea y vuelven al reponerse. El servidor manda; el estado llega
/// a los clientes por <see cref="LootPointSync"/> (en el prefab del evento x2).
/// </summary>
public class LootPoint : MonoBehaviour, IRepeatableInteractable
{
    [Header("Rareza")]
    [Tooltip("Preset de rareza. Si está asignado, manda sobre los valores sueltos de abajo.")]
    [SerializeField] LootRarityPreset preset;
    [Tooltip("Producto que se cae al recibir un golpe con esto en la bolsa (DroppedLoot > Variants): " +
             "0 = bolsita genérica, 1 = manzana, 2 = naranja, 3 = pan, 4 = patatas de bolsa, 5 = NukiProd, 6 = lata.")]
    [SerializeField, Min(0)] int dropVariant;
    [Tooltip("PF_HUD_AvisoSaquear: aviso de «mantén para saquear» (uno solo por partida, lo crea la primera estantería).")]
    [SerializeField] Racoon.UI.LootPromptUI lootPromptPrefab;

    [Header("Visuales")]
    [Tooltip("Raíz del mueble: todo lo que brilla durante la oferta x2. Vacío = el padre de este objeto.")]
    [SerializeField] Transform visualRoot;

    [Tooltip("Modelos de los productos que se van ocultando al saquear. Si está vacío se buscan solos " +
             "bajo la raíz por los prefijos de nombre de abajo.")]
    [SerializeField] List<GameObject> itemVisuals = new();

    [Tooltip("Prefijos de nombre (sin distinguir mayúsculas) para encontrar los productos si la lista está vacía.")]
    [SerializeField] string[] itemNamePrefixes = { };

    [Tooltip("Segundos del encogido/estirado al desaparecer y reaparecer un producto.")]
    [SerializeField, Min(0.01f)] float itemPopDuration = 0.25f;

    [Header("Valores sueltos (sin preset)")]
    [Tooltip("Puntos que da cada objeto recogido.")]
    [FormerlySerializedAs("points")]
    [SerializeField, Min(0)] int pointsPerItem = 10;

    [Tooltip("Peso que añade cada objeto recogido.")]
    [FormerlySerializedAs("weight")]
    [SerializeField, Min(0f)] float weightPerItem = 1f;

    [Tooltip("Número mínimo de objetos que puede contener esta estantería.")]
    [SerializeField, Min(1)] int minItems = 4;

    [Tooltip("Número máximo de objetos que puede contener esta estantería.")]
    [SerializeField, Min(1)] int maxItems = 7;

    [Tooltip("Segundos hasta rellenarse. 0 = una vez vacía, no reaparece.")]
    [SerializeField, Min(0f)] float respawnTime = 0f;

    [Tooltip("Número de recolecciones permitidas antes de que expire el evento x2.")]
    [SerializeField, Min(1)] int maxDoublePointsCollections = 10;

    [Tooltip("Tiempo de espera entre recolecciones de un mismo jugador en estado normal (segundos). " +
             "Cada jugador lleva su propia espera: uno saqueando no bloquea a los demás.")]
    [SerializeField, Min(0f)] float normalCooldown = 0f;

    [Tooltip("Tiempo de espera entre recolecciones de un mismo jugador cuando el evento x2 está activo (segundos).")]
    [SerializeField, Min(0f)] float doublePointsCooldown = 2f;

    // ---------------- Valores efectivos (preset o sueltos) ----------------

    int PointsPerItem => preset != null ? preset.pointsPerItem : pointsPerItem;
    float WeightPerItem => preset != null ? preset.weightPerItem : weightPerItem;
    int MinItems => preset != null ? preset.minItems : minItems;
    int MaxItems => Mathf.Max(MinItems, preset != null ? preset.maxItems : maxItems);
    float NormalCooldown => preset != null ? preset.normalCooldown : normalCooldown;
    float DoublePointsCooldown => preset != null ? preset.doublePointsCooldown : doublePointsCooldown;
    float RollRespawnTime() => preset != null ? preset.RollRespawnTime() : respawnTime;

    bool isDoublePoints = false;
    int currentDoublePointsCollections = 0;

    /// <summary>Solo servidor. Salta al activar o apagar el x2 y en cada recolección doble (para los visuales del evento).</summary>
    public event System.Action<LootPoint> DoublePointsChanged;

    public LootRarityPreset Preset => preset;
    public bool IsDoublePoints => isDoublePoints;
    public int MaxDoublePointsCollections => preset != null ? preset.maxDoublePointsCollections : maxDoublePointsCollections;
    /// <summary>Recolecciones dobles que quedan (0 si el x2 no está activo).</summary>
    public int DoublePointsRemaining => isDoublePoints ? Mathf.Max(0, MaxDoublePointsCollections - currentDoublePointsCollections) : 0;

    /// <summary>Raíz del mueble (para el brillo de la oferta x2).</summary>
    public Transform VisualRoot => visualRoot != null ? visualRoot : transform.parent != null ? transform.parent : transform;

    /// <summary>Solo servidor: tiene objetos que coger ahora mismo.</summary>
    public bool IsAvailable => initialized && available && itemsRemaining > 0;
    /// <summary>
    /// Para el autoapuntado del dueño: en el servidor, el estado real; en un cliente, lo que se ve
    /// (sin estado recibido aún cuenta como que hay loot).
    /// </summary>
    public bool LooksLootable => initialized ? IsAvailable : visibleItems != 0;

    // Siguiente momento en que puede saquear cada jugador (por NetworkObjectId). Solo servidor.
    readonly Dictionary<ulong, float> nextInteractTimeByPlayer = new();

    int itemsRemaining;
    int itemsTotal;
    bool available;
    bool initialized;
    Coroutine respawnRoutine;

    // ---------------- Registro (orden idéntico en todas las instancias del juego) ----------------

    static readonly List<LootPoint> registry = new();
    static bool registrySorted;

    /// <summary>Todos los LootPoints de la escena, ordenados por posición: mismo índice en servidor y clientes.</summary>
    public static IReadOnlyList<LootPoint> All
    {
        get
        {
            if (!registrySorted)
            {
                registry.RemoveAll(lp => lp == null);
                registry.Sort(Compare);
                registrySorted = true;
            }
            return registry;
        }
    }

    public static int IndexOf(LootPoint lootPoint) => lootPoint == null ? -1 : ((List<LootPoint>)All).IndexOf(lootPoint);

    static int Compare(LootPoint a, LootPoint b)
    {
        Vector3 pa = a.transform.position, pb = b.transform.position;
        int c = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
        if (c != 0) return c;
        c = Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
        if (c != 0) return c;
        c = Mathf.RoundToInt(pa.y * 100f).CompareTo(Mathf.RoundToInt(pb.y * 100f));
        return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
    }

    // ---------------- Ciclo de vida ----------------

    void OnValidate()
    {
        if (maxItems < minItems)
            maxItems = minItems;
    }

    void Start() => Racoon.UI.LootPromptUI.EnsureInstance(lootPromptPrefab);

    void Awake()
    {
        CollectItemVisuals();
    }

    void OnEnable()
    {
        registry.Add(this);
        registrySorted = false;
    }

    void OnDisable()
    {
        registry.Remove(this);
        registrySorted = false;
    }

    /// <summary>Solo servidor: rellena la estantería si aún no se había hecho (al arrancar la red).</summary>
    public void EnsureInitialized()
    {
        if (initialized)
            return;

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        initialized = true;
        Refill();
    }

    void Refill()
    {
        itemsTotal = Random.Range(MinItems, MaxItems + 1);
        itemsRemaining = itemsTotal;
        available = true;
        respawnRoutine = null;
        PublishState();
    }

    public void SetDoublePoints(bool enabled)
    {
        isDoublePoints = enabled;
        if (enabled)
        {
            currentDoublePointsCollections = 0; // Reiniciar contador al activarse el evento

            // La oferta llena la estantería: que haya objetos para todas las recolecciones dobles.
            EnsureInitialized();
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
            int wanted = Mathf.Max(itemsRemaining, MaxDoublePointsCollections);
            itemsTotal = Mathf.Max(itemsTotal, wanted);
            itemsRemaining = wanted;
            available = true;
            PublishState();
        }
        DoublePointsChanged?.Invoke(this);
    }

    public bool CanInteract(PlayerController player)
    {
        EnsureInitialized();

        if (!available ||
            itemsRemaining <= 0 ||
            player == null)
            return false;

        // Comprobar si este jugador está en tiempo de espera (cooldown)
        if (nextInteractTimeByPlayer.TryGetValue(player.NetworkObjectId, out float nextTime) &&
            Time.time < nextTime)
            return false;

        return player.TryGetComponent(out PlayerLoot _);
    }

    public void Interact(PlayerController player)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer ||
            !CanInteract(player))
            return;

        PlayerLoot playerLoot = player.GetComponent<PlayerLoot>();

        // Calcular puntos según si el evento x2 está activo
        bool doubled = isDoublePoints;
        int finalPoints = doubled ? PointsPerItem * 2 : PointsPerItem;

        if (!playerLoot.ServerAddLoot(finalPoints, WeightPerItem, dropVariant))
            return;

        // Sonido de saquear, en todos los clientes.
        playerLoot.ServerNotifyLootGrabbed(doubled, transform.position, finalPoints);

        // Aplicar el tiempo de espera (cooldown) correspondiente a este jugador
        float activeCooldown = doubled ? DoublePointsCooldown : NormalCooldown;
        nextInteractTimeByPlayer[player.NetworkObjectId] = Time.time + activeCooldown;

        itemsRemaining--;

        // Si el x2 está activo, sumamos una recolección al contador del evento
        if (doubled)
        {
            currentDoublePointsCollections++;
            // Se acaba al llegar al límite o si la estantería se queda vacía.
            if (currentDoublePointsCollections >= MaxDoublePointsCollections || itemsRemaining <= 0)
                SetDoublePoints(false);
            else
                DoublePointsChanged?.Invoke(this);
        }

        if (itemsRemaining <= 0)
        {
            itemsRemaining = 0;
            available = false;

            float wait = RollRespawnTime();
            if (wait > 0f)
                respawnRoutine = StartCoroutine(Respawn(wait));
        }

        PublishState();
    }

    IEnumerator Respawn(float wait)
    {
        yield return new WaitForSeconds(wait);
        Refill();
    }

    // ---------------- Estado en red y visuales ----------------

    void PublishState()
    {
        ApplyVisualState(itemsRemaining, itemsTotal);
        LootPointSync.ServerReport(this, itemsRemaining, itemsTotal);
    }

    // Productos en el orden en que se van quitando (barajado fijo: igual en todas las instancias).
    readonly List<Transform> items = new();
    readonly List<Vector3> itemScales = new();
    readonly List<Coroutine> itemPops = new();
    int visibleItems = -1;

    void CollectItemVisuals()
    {
        items.Clear();
        itemScales.Clear();
        itemPops.Clear();

        var found = new List<Transform>();
        foreach (GameObject go in itemVisuals)
            if (go != null) found.Add(go.transform);

        if (found.Count == 0 && itemNamePrefixes != null && itemNamePrefixes.Length > 0)
            FindByPrefix(VisualRoot, found);

        // Barajado con semilla fija por nombre y número de productos: mismo orden en todas las instancias.
        var rng = new System.Random(StableHash(name) ^ found.Count);
        for (int i = found.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (found[i], found[j]) = (found[j], found[i]);
        }

        foreach (Transform t in found)
        {
            items.Add(t);
            itemScales.Add(t.localScale);
            itemPops.Add(null);
        }
    }

    void FindByPrefix(Transform parent, List<Transform> found)
    {
        foreach (Transform child in parent)
        {
            if (child == transform) continue;
            if (MatchesPrefix(child.name))
            {
                found.Add(child);
                continue; // sus hijos van con él
            }
            FindByPrefix(child, found);
        }
    }

    bool MatchesPrefix(string objectName)
    {
        foreach (string prefix in itemNamePrefixes)
            if (!string.IsNullOrEmpty(prefix) &&
                objectName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }

    /// <summary>Muestra tantos productos como correspondan a lo que queda (en servidor y clientes).</summary>
    public void ApplyVisualState(int remaining, int total)
    {
        if (items.Count == 0) return;

        int visible = remaining <= 0 || total <= 0
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(items.Count * (float)remaining / total), 1, items.Count);
        if (visible == visibleItems) return;

        bool animate = visibleItems >= 0 && isActiveAndEnabled;
        visibleItems = visible;

        // Los primeros «visible» de la lista se ven; el resto se ocultan.
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null) continue;
            bool show = i < visible;
            if (item.gameObject.activeSelf == show && itemPops[i] == null) continue;

            if (itemPops[i] != null) StopCoroutine(itemPops[i]);
            itemPops[i] = null;

            if (!animate)
            {
                item.localScale = itemScales[i];
                item.gameObject.SetActive(show);
            }
            else
            {
                itemPops[i] = StartCoroutine(Pop(i, show));
            }
        }
    }

    IEnumerator Pop(int index, bool show)
    {
        Transform item = items[index];
        Vector3 full = itemScales[index];
        if (show) item.gameObject.SetActive(true);

        // Al reaparecer, cada producto llega con un pequeño retraso para que no salgan todos de golpe.
        if (show) yield return new WaitForSeconds(index * 0.03f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / itemPopDuration;
            float k = Mathf.Clamp01(t);
            // Aparecer con un pequeño rebote; desaparecer encogiendo.
            float s = show ? 1f + Mathf.Sin(k * Mathf.PI) * 0.25f * (1f - k) : 1f - k * k;
            item.localScale = full * (show ? Mathf.SmoothStep(0f, 1f, k) * s : s);
            yield return null;
        }

        item.localScale = full;
        if (!show) item.gameObject.SetActive(false);
        itemPops[index] = null;
    }
}
