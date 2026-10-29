using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem; // <-- IMPORTANTE: Necesario para usar Gamepad

public class VibrationManager : MonoBehaviour
{
    public static VibrationManager Instance;

    private void Awake()
    {
        // Opcional: Patrón Singleton rápido para llamarlo desde cualquier script
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Activa la vibración del mando.
    /// </summary>
    /// <param "lowFrequency">Motor izquierdo (graves/pesado). Valores de 0.0 a 1.0</param>
    /// <param "highFrequency">Motor derecho (agudos/suave). Valores de 0.0 a 1.0</param>
    /// <param "duration">Duración en segundos que durará la vibración</param>
    public void Rumble(float lowFrequency, float highFrequency, float duration)
    {
        StartCoroutine(RumbleRoutine(lowFrequency, highFrequency, duration));
    }

    private IEnumerator RumbleRoutine(float lowFrequency, float highFrequency, float duration)
    {
        // Comprobamos si hay mandos conectados en general
        // Sin mando (jugando con teclado) simplemente no vibra; antes llenaba la consola de avisos.
        if (Gamepad.all.Count == 0)
            yield break;

        // Si Gamepad.current es null pero hay mandos, cogemos el primero de la lista
        Gamepad gamepad = Gamepad.current ?? Gamepad.all[0];

        // Aplica la velocidad
        gamepad.SetMotorSpeeds(lowFrequency, highFrequency);

        yield return new WaitForSeconds(duration);

        // Apaga los motores
        gamepad.SetMotorSpeeds(0f, 0f);
    }

    // Método de ejemplo para apagar la vibración de golpe si el jugador pausa o muere
    public void StopRumble()
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
        }
    }
}