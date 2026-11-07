using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

/// <summary>
/// Abre la bóveda del punto de venta (sube la compuerta de abajo, Cube.004, y su tirador Cube.028 por Animator) cuando CUALQUIER jugador se acerca y
/// la cierra cuando no queda nadie cerca.
///
/// No necesita red: las posiciones de los jugadores ya se replican, así que cada instancia del juego calcula lo
/// mismo y todos ven la bóveda igual sin importar quién se acerque. Va en la raíz de PF_SM_SellPoint junto a su
/// Animator (AC_SellPointVault, bool «Open»).
/// </summary>
[RequireComponent(typeof(Animator))]
public class SellPointVault : MonoBehaviour
{
    static readonly int OpenId = Animator.StringToHash("Open");

    [Tooltip("Distancia (m, en el plano) a la que se abre al acercarse un jugador.")]
    [SerializeField, Min(0f)] float openDistance = 2.5f;

    [Tooltip("Distancia (m) a la que se cierra cuando ya no queda nadie. Algo mayor que la de apertura para que no parpadee en el borde.")]
    [SerializeField, Min(0f)] float closeDistance = 3f;

    [Tooltip("Cada cuántos segundos se comprueba.")]
    [SerializeField, Min(0.02f)] float checkInterval = 0.1f;

    Animator animator;
    bool open;
    float nextCheck;
    float nextPlayerSearch;
    readonly List<PlayerController> players = new();

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void OnValidate()
    {
        if (closeDistance < openDistance) closeDistance = openDistance;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;

        // La lista de jugadores se refresca de vez en cuando (entran, salen, reaparecen).
        if (Time.time >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.time + 1f;
            players.Clear();
            players.AddRange(FindObjectsByType<PlayerController>(FindObjectsSortMode.None));
        }

        float limit = open ? closeDistance : openDistance;
        float limitSqr = limit * limit;
        Vector3 center = transform.position;
        bool someoneNear = false;
        foreach (PlayerController player in players)
        {
            if (player == null || !player.isActiveAndEnabled || !player.IsSpawned) continue;
            Vector3 d = player.transform.position - center;
            d.y = 0f;
            if (d.sqrMagnitude <= limitSqr)
            {
                someoneNear = true;
                break;
            }
        }

        if (someoneNear == open) return;
        open = someoneNear;
        animator.SetBool(OpenId, open);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, openDistance);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, closeDistance);
    }
}
