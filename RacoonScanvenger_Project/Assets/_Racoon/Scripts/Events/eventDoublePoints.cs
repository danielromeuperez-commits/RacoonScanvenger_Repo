using System;
using Racoon.Events;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Evento «OFERTA x2»: el servidor elige una estantería (LootPoint) al azar y la pone a puntos dobles durante
/// <c>maxDoublePointsCollections</c> recolecciones.
///
/// Estado en red (lo ven todos, también quien entra tarde): posición de la estantería, recolecciones que quedan y
/// total. Con eso cada cliente monta sus visuales: el anuncio por megafonía y la ficha de estado
/// (<see cref="GameEventAnnouncer"/>) y el cartel colgante, el marco dorado y los destellos (<see cref="DoublePointsVFX"/>).
/// </summary>
public class eventDoublePoints : NetworkBehaviour, IGameEvent
{
    [Header("Presentación")]
    [SerializeField] string title = "¡OFERTA x2!";
    [SerializeField] string subtitle = "Una estantería da el doble de puntos. ¡Busca el cartel!";
    [Tooltip("Color del evento (golpe de cámara): verde pastel, el de la cinta.")]
    [SerializeField] Color accent = new(0.66f, 0.9f, 0.79f);
    [Tooltip("Piel de color de la cinta (Verde, Amarillo o Rojo; sprites del PSD, sin teñir).")]
    [SerializeField] string colorSkin = "Verde";

    // Declaradas en este orden a propósito: se replican en orden, así que cuando llega «remaining» (lo que arranca
    // los visuales) la posición y el total ya están. -1 = aún no ha empezado; 0 = se acabó; >0 = activo.
    readonly NetworkVariable<Vector3> shelfPosition = new();
    // Índice en LootPoint.All (mismo orden en todas las instancias). -1 = usar la posición.
    readonly NetworkVariable<int> shelfIndex = new(-1);
    readonly NetworkVariable<int> total = new(0);
    readonly NetworkVariable<int> remaining = new(-1);

    LootPoint serverShelf;

    // Propiedad requerida por el EventManager para saber si ya se ejecutó
    public bool HasTriggered { get; private set; } = false;

    /// <summary>Mientras haya una estantería x2 activa no se lanza otra.</summary>
    public bool IsBusy => IsActive;

    public bool IsActive => remaining.Value > 0;
    public bool HasStarted => remaining.Value >= 0;
    public int Remaining => Mathf.Max(0, remaining.Value);
    public int Total => total.Value;
    public Vector3 ShelfPosition => shelfPosition.Value;

    /// <summary>La estantería de la oferta en esta instancia (por índice; si no, la más cercana a la posición).</summary>
    public LootPoint Shelf
    {
        get
        {
            var all = LootPoint.All;
            int i = shelfIndex.Value;
            if (i >= 0 && i < all.Count) return all[i];

            LootPoint best = null;
            float bestDistance = 1f;
            foreach (LootPoint lp in all)
            {
                float d = Vector3.Distance(lp.transform.position, shelfPosition.Value);
                if (d < bestDistance) { bestDistance = d; best = lp; }
            }
            return best;
        }
    }
    public Color Accent => accent;

    /// <summary>En cada cliente: empieza (true) o cambia lo que queda / se acaba (false).</summary>
    public event Action<eventDoublePoints, bool> StateChanged;

    public override void OnNetworkSpawn()
    {
        remaining.OnValueChanged += OnRemainingChanged;
        // Los visuales viven en el cliente (también en el host). Si ya estaba activo al entrar, se montan sin anuncio.
        if (!TryGetComponent(out DoublePointsVFX _)) gameObject.AddComponent<DoublePointsVFX>();
        if (IsActive) StateChanged?.Invoke(this, false);
    }

    public override void OnNetworkDespawn()
    {
        remaining.OnValueChanged -= OnRemainingChanged;
        if (serverShelf != null) serverShelf.DoublePointsChanged -= OnServerShelfChanged;
    }

    // Método público que llamará el EventManager de forma aleatoria
    public void TriggerEventManually()
    {
        if (!IsServer || IsBusy)
            return;

        HasTriggered = true;

        var lootPoints = LootPoint.All;
        if (lootPoints.Count == 0)
        {
            Debug.LogWarning("EventDoublePoints: No se encontraron LootPoints en la escena.");
            return;
        }

        // Preferimos una estantería con producto; si todas están vacías, cualquiera (la oferta la rellena).
        var candidates = new System.Collections.Generic.List<LootPoint>();
        foreach (LootPoint lp in lootPoints)
        {
            lp.EnsureInitialized();
            if (lp.IsAvailable) candidates.Add(lp);
        }
        if (candidates.Count == 0) candidates.AddRange(lootPoints);

        serverShelf = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        serverShelf.DoublePointsChanged += OnServerShelfChanged;

        // Primero dónde y cuántas; lo último, «remaining», que es lo que arranca los visuales.
        shelfPosition.Value = serverShelf.transform.position;
        shelfIndex.Value = LootPoint.IndexOf(serverShelf);
        total.Value = serverShelf.MaxDoublePointsCollections;
        serverShelf.SetDoublePoints(true);

        Debug.Log($"¡EVENTO X2 ALEATORIO! La estantería '{serverShelf.gameObject.name}' ha sido marcada.");
    }

    void OnServerShelfChanged(LootPoint shelf)
    {
        remaining.Value = shelf.DoublePointsRemaining;
        if (!shelf.IsDoublePoints)
        {
            shelf.DoublePointsChanged -= OnServerShelfChanged;
            serverShelf = null;
        }
    }

    void OnRemainingChanged(int previous, int current)
    {
        // Empieza (también al repetirse en la misma partida: de 0 a >0).
        bool started = previous <= 0 && current > 0;
        if (started) GameEventAnnouncer.Announce(this, title, subtitle, accent, colorSkin);
        StateChanged?.Invoke(this, started);
    }
}
