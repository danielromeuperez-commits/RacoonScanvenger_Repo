using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

/// <summary>
/// «Mantener para…» en el servidor (lo usa el punto de venta): cuenta cuánto lleva seguido cada jugador interactuando.
/// Llega una interacción al pulsar y, mientras mantiene, otra cada pocas décimas (IRepeatableInteractable); si deja de
/// llegar más de <c>tolerance</c> segundos, ha soltado y se empieza de cero. Los toques seguidos cuentan como mantener.
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
