using Racoon.Player;
using UnityEngine;

/// <summary>
/// Pickup de prueba que hace vibrar el mando. No es un objeto de red: cada cliente detecta el trigger
/// por su cuenta (los jugadores remotos también tienen collider) y lo esconde a la vez, así todos lo ven igual.
/// Solo vibra el mando de quien lo coge.
/// </summary>
public class PickupWatch : MonoBehaviour
{
    [Header("Configuración de Vibración")]
    [Range(0f, 1f)] public float lowFrequency = 0.5f;   // Motor grave
    [Range(0f, 1f)] public float highFrequency = 0.8f;  // Motor agudo
    public float duration = 0.2f;                       // Tiempo que vibra en segundos

    [Header("Efectos Opcionales")]
    public GameObject pickupEffectPrefab;               // Efecto visual al recogerlo (partículas, etc.)

    private void OnTriggerEnter(Collider other)
    {
        // Antes se comprobaba el tag "Player"; así funciona con cualquier collider hijo del jugador.
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        // 1. Vibrar solo en la máquina de quien lo ha cogido
        if (player.IsOwner && VibrationManager.Instance != null)
        {
            VibrationManager.Instance.Rumble(lowFrequency, highFrequency, duration);
        }

        // 2. Instanciar efectos visuales o sonoros si los tienes configurados
        if (pickupEffectPrefab != null)
        {
            Instantiate(pickupEffectPrefab, transform.position, transform.rotation);
        }

        // 3. Esconderlo (no Destroy: si algún día lleva NetworkObject, destruirlo en un cliente da error)
        gameObject.SetActive(false);
    }
}
