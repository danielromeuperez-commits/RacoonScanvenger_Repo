using Racoon.Items;
using Racoon.Player;
using UnityEngine;

/// <summary>
/// Punto de venta: hay que MANTENER Interact para vender la bolsa, más tiempo cuantos más puntos lleve
/// (<see cref="HoldTimeFor"/>; el aviso SellPromptUI llena su dial con el mismo tiempo).
///
/// No necesita nada en PlayerController: al pulsar llega una interacción y, mientras se mantiene,
/// PlayerController repite la interacción con los IRepeatableInteractable cada pocas décimas. Aquí se mide cuánto
/// lleva seguido cada jugador; si deja de llegar (ha soltado o se ha ido), se empieza de cero.
/// </summary>
public class SellPoint : MonoBehaviour, IRepeatableInteractable
{
    [Header("Tiempo a mantener = base + segundos por punto × puntos (con un máximo)")]
    [Tooltip("Segundos manteniendo Interact con la bolsa casi vacía.")]
    [SerializeField, Min(0f)] float baseHoldTime = 0.5f;
    [Tooltip("Segundos extra por cada punto que se vende (100 puntos × 0,013 = 1,3 s más).")]
    [SerializeField, Min(0f)] float secondsPerPoint = 0.013f;
    [Tooltip("Lo más que hay que mantener, lleve lo que lleve.")]
    [SerializeField, Min(0f)] float maxHoldTime = 3f;
    [Tooltip("Si pasa más que esto entre una interacción y la siguiente, ha soltado: se reinicia. " +
             "Tiene que ser mayor que la duración de la acción Interact del jugador (0,4 s).")]
    [SerializeField, Min(0.1f)] float releaseTolerance = 0.7f;

    [Tooltip("PF_HUD_AvisoVender: aviso de «mantén para vender» que ve cada jugador en su instancia. Vacío = sin aviso.")]
    [SerializeField] Racoon.UI.SellPromptUI sellPromptPrefab;

    // Por jugador (solo servidor): cuánto lleva manteniendo.
    readonly HoldInteraction hold = new();

    /// <summary>
    /// Segundos que hay que mantener para vender <paramref name="points"/> puntos (servidor y aviso). Con la bebida
    /// energética de <paramref name="player"/> se vende más rápido (PlayerController.ActionTimeScale).
    /// </summary>
    public float HoldTimeFor(int points, PlayerController player = null) =>
        Mathf.Min(maxHoldTime, baseHoldTime + secondsPerPoint * Mathf.Max(0, points)) *
        (player != null ? player.ActionTimeScale : 1f);

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
    /// Cuenta el tiempo que lleva manteniendo; al llegar a HoldTimeFor(puntos) vende todo el contenido de la bolsa.
    /// </summary>
    public void Interact(PlayerController player)
    {
        if (player == null || !player.IsServer)
            return;

        PlayerLoot playerLoot =
            player.GetComponent<PlayerLoot>();

        if (playerLoot == null)
            return;

        if (hold.Tick(player, HoldTimeFor(playerLoot.PendingPoints, player), releaseTolerance))
            playerLoot.ServerSellLoot();
    }
}
