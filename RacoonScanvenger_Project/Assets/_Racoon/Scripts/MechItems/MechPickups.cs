using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

/// <summary>
/// Objeto mecánico del suelo (extintor, despertador) que se coge MANTENIENDO Interact (o tocando seguido), igual que
/// se vende en el punto de venta. Lo usa el aviso de «mantén» (LootPromptUI) para saber dónde están.
/// </summary>
public interface IMechPickup : IRepeatableInteractable
{
    Transform transform { get; }
    /// <summary>Segundos manteniendo para cogerlo.</summary>
    float HoldTime { get; }
    /// <summary>Sigue en el suelo (nadie lo ha cogido).</summary>
    bool IsAvailable { get; }
}

/// <summary>Registro de los objetos mecánicos que hay en el suelo.</summary>
public static class MechPickups
{
    static readonly List<IMechPickup> all = new();
    public static IReadOnlyList<IMechPickup> All => all;
    public static void Register(IMechPickup pickup) { if (!all.Contains(pickup)) all.Add(pickup); }
    public static void Unregister(IMechPickup pickup) => all.Remove(pickup);

    /// <summary>Capa en la que busca la interacción del jugador (PlayerController.interactMask).</summary>
    public static void UseInteractableLayer(GameObject go)
    {
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0) go.layer = layer;
    }
}

/// <summary>
/// «Mantener para…» en el servidor: cuenta cuánto lleva seguido cada jugador interactuando. Llega una interacción al
/// pulsar y, mientras mantiene, otra cada pocas décimas (IRepeatableInteractable); si deja de llegar más de
/// <c>tolerance</c> segundos, ha soltado y se empieza de cero. Los toques seguidos cuentan como mantener.
/// </summary>
public class HoldInteraction
{
    readonly Dictionary<ulong, (float start, float last)> holds = new();

    /// <summary>Solo servidor. True cuando ya lleva <paramref name="holdTime"/> segundos (y se reinicia).</summary>
    public bool Tick(PlayerController player, float holdTime, float tolerance = 0.7f)
    {
        float now = Time.time;
        ulong id = player.NetworkObjectId;
        if (!holds.TryGetValue(id, out var hold) || now - hold.last > tolerance)
            hold = (now, now);
        hold.last = now;

        if (now - hold.start + 0.001f < holdTime)
        {
            holds[id] = hold;
            return false;
        }

        holds.Remove(id);
        return true;
    }
}
