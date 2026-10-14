using UnityEngine;

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
        // Comprobamos si el objeto que entra en contacto es el jugador (puedes usar un Tag "Player")
        if (other.CompareTag("Player"))
        {
            // 1. Activar la vibración llamando al GameManager / RumbleManager
            if (VibrationManager.Instance != null)
            {
                VibrationManager.Instance.Rumble(lowFrequency, highFrequency, duration);
            }

            // 2. Instanciar efectos visuales o sonoros si los tienes configurados
            if (pickupEffectPrefab != null)
            {
                Instantiate(pickupEffectPrefab, transform.position, transform.rotation);
            }

            // 3. Destruir el pickup para que desaparezca de la escena
            Destroy(gameObject);
        }
    }
}