using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replica a todos los clientes cuántos productos le quedan a cada <see cref="LootPoint"/>, para que se vean
/// desaparecer al saquear y reaparecer al reponerse (también para quien entra tarde).
///
/// Los LootPoints no son objetos de red: se identifican por su índice en <see cref="LootPoint.All"/>, que está
/// ordenado por posición y es igual en todas las instancias. Va en el prefab PF_Event_DoublePoints (ya es un
/// NetworkObject de la escena). Si no está, el saqueo funciona igual; solo se pierden los visuales en clientes.
/// </summary>
public class LootPointSync : NetworkBehaviour
{
    public struct LootState : INetworkSerializable, IEquatable<LootState>
    {
        public byte remaining;
        public byte total;
        /// <summary>Quién la está usando (neveras): clientId + 1; 0 = nadie.</summary>
        public byte user;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref remaining);
            serializer.SerializeValue(ref total);
            serializer.SerializeValue(ref user);
        }

        public bool Equals(LootState other) => remaining == other.remaining && total == other.total && user == other.user;
    }

    static LootPointSync current;

    readonly NetworkList<LootState> states = new();

    public override void OnNetworkSpawn()
    {
        current = this;

        if (IsServer)
        {
            states.Clear();
            var all = LootPoint.All;
            for (int i = 0; i < all.Count; i++) states.Add(default);
            // Rellenar todas al arrancar: cada una publica su estado con ServerReport.
            foreach (LootPoint lp in all) lp.EnsureInitialized();
        }
        else
        {
            states.OnListChanged += OnStatesChanged;
            ApplyAll();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) states.OnListChanged -= OnStatesChanged;
        if (current == this) current = null;
    }

    public override void OnDestroy()
    {
        if (current == this) current = null;
        base.OnDestroy();
    }

    /// <summary>Solo servidor: guarda el estado de un LootPoint para los clientes.</summary>
    public static void ServerReport(LootPoint lootPoint, int remaining, int total, int userClientId = -1)
    {
        if (current == null || !current.IsSpawned || !current.IsServer) return;

        int index = LootPoint.IndexOf(lootPoint);
        if (index < 0 || index >= current.states.Count) return;

        var state = new LootState
        {
            remaining = (byte)Mathf.Clamp(remaining, 0, 255),
            total = (byte)Mathf.Clamp(total, 0, 255),
            user = (byte)Mathf.Clamp(userClientId + 1, 0, 255),
        };
        if (!current.states[index].Equals(state)) current.states[index] = state;
    }

    void OnStatesChanged(NetworkListEvent<LootState> change)
    {
        if (change.Type == NetworkListEvent<LootState>.EventType.Value ||
            change.Type == NetworkListEvent<LootState>.EventType.Add ||
            change.Type == NetworkListEvent<LootState>.EventType.Insert)
            Apply(change.Index);
        else
            ApplyAll();
    }

    void ApplyAll()
    {
        for (int i = 0; i < states.Count; i++) Apply(i);
    }

    void Apply(int index)
    {
        var all = LootPoint.All;
        if (index < 0 || index >= states.Count || index >= all.Count) return;
        LootState state = states[index];
        // total 0 = el servidor aún no la ha rellenado: no tocar.
        if (state.total == 0) return;
        all[index].ApplyVisualState(state.remaining, state.total);
        all[index].ApplyUser(state.user - 1);
    }
}
