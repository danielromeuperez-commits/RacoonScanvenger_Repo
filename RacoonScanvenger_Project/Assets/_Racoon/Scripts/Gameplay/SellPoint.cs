using System.Collections.Generic;
using Racoon.Items;
using Racoon.Player;
using UnityEngine;

/// <summary>
/// Punto de venta: hay que MANTENER Interact para vender la bolsa.
///
/// No necesita nada en PlayerController: al pulsar llega una interacción y, mientras se mantiene,
/// PlayerController repite la interacción con los IRepeatableInteractable cada pocas décimas. Aquí se mide cuánto
/// lleva seguido cada jugador; si deja de llegar (ha soltado o se ha ido), se empieza de cero.
/// </summary>
public class SellPoint : MonoBehaviour, IRepeatableInteractable
{
    [Tooltip("Segundos manteniendo Interact para vender.")]
    [SerializeField, Min(0f)] float holdToSellTime = 1.2f;
    [Tooltip("Si pasa más que esto entre una interacción y la siguiente, ha soltado: se reinicia. " +
             "Tiene que ser mayor que la duración de la acción Interact del jugador (0,4 s).")]
    [SerializeField, Min(0.1f)] float releaseTolerance = 0.7f;

    [Tooltip("PF_HUD_AvisoVender: aviso de «mantén para vender» que ve cada jugador en su instancia. Vacío = sin aviso.")]
    [SerializeField] Racoon.UI.SellPromptUI sellPromptPrefab;

    // Por jugador (solo servidor): cuándo empezó a mantener y cuándo llegó la última interacción.
    readonly Dictionary<ulong, (float start, float last)> holds = new();

    public float HoldToSellTime => holdToSellTime;

    void Start()
    {
        // En todas las instancias (también en los clientes): cada una enseña el aviso a su jugador local.
        if (sellPromptPrefab != null && !Application.isBatchMode)
            Instantiate(sellPromptPrefab).Bind(this);
    }

    /// <summary>
    /// Solo permite interactuar si el jugador lleva
    /// algún objeto pendiente de vender.
    /// </summary>
    public bool CanInteract(PlayerController player)
    {
        if (player == null)
            return false;

        if (!player.TryGetComponent(out PlayerLoot playerLoot))
            return false;

        return playerLoot.PendingPoints > 0;
    }

    /// <summary>
    /// Cuenta el tiempo que lleva manteniendo; al llegar a holdToSellTime vende todo el contenido de la bolsa.
    /// </summary>
    public void Interact(PlayerController player)
    {
        if (player == null || !player.IsServer)
            return;

        PlayerLoot playerLoot =
            player.GetComponent<PlayerLoot>();

        if (playerLoot == null)
            return;

        float now = Time.time;
        ulong id = player.NetworkObjectId;
        if (!holds.TryGetValue(id, out var hold) || now - hold.last > releaseTolerance)
            hold = (now, now);
        hold.last = now;

        if (now - hold.start + 0.001f < holdToSellTime)
        {
            holds[id] = hold;
            return;
        }

        holds.Remove(id);
        playerLoot.ServerSellLoot();
    }
}
