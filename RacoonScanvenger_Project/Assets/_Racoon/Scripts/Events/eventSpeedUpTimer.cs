using Unity.Netcode;
using UnityEngine;
using Racoon.Events;

public class eventSpeedUpTimer : NetworkBehaviour, IGameEvent
{
    [Header("Configuración del Evento de Velocidad")]
    [Tooltip("Multiplicador de velocidad permanente (ej: 2 para el doble de rápido, 3 para el triple).")]
    [SerializeField] float speedMultiplier = 2f;
    [Tooltip("Si el evento se repite en la misma partida, el ritmo se multiplica otra vez, hasta este tope.")]
    [SerializeField, Min(1f)] float maxTimeScale = 4f;

    [Header("Presentación (anuncio común: GameEventAnnouncer)")]
    [SerializeField] string title = "¡RITMO ACELERADO!";
    [Tooltip("{0} = multiplicador.")]
    [SerializeField] string subtitle = "El reloj corre x{0} hasta el final de la partida.";
    [Tooltip("Color del evento (golpe de cámara): rojo pastel, el de la cinta.")]
    [SerializeField] Color accent = new(1f, 0.68f, 0.7f);
    [Tooltip("Piel de color de la cinta (Verde, Amarillo o Rojo; sprites del PSD, sin teñir).")]
    [SerializeField] string colorSkin = "Rojo";

    public bool HasTriggered { get; private set; } = false;

    MatchTimer matchTimer;

    MatchTimer Timer => matchTimer != null ? matchTimer : matchTimer = FindFirstObjectByType<MatchTimer>();

    /// <summary>Con el reloj ya al tope no tiene sentido repetirlo.</summary>
    public bool IsBusy => Timer != null && Timer.TimeScale >= maxTimeScale - 0.001f;

    public void TriggerEventManually()
    {
        if (!IsServer || IsBusy)
            return;

        HasTriggered = true;

        float newScale = speedMultiplier;
        if (Timer != null)
        {
            // Aplica la velocidad permanente en el MatchTimer (si ya iba acelerado, se acumula hasta el tope).
            newScale = Mathf.Min(maxTimeScale, Timer.TimeScale * speedMultiplier);
            Timer.ApplyTimeMultiplier(newScale);
        }
        else
        {
            Debug.LogError("eventSpeedUpTimer: No se encontró ningún MatchTimer para acelerar.");
        }

        TriggerSpeedUpClientRpc(newScale);

        Debug.Log($"¡EVENTO DE VELOCIDAD PERMANENTE! El cronómetro irá a x{speedMultiplier} hasta el final de la partida.");
    }

    [ClientRpc]
    void TriggerSpeedUpClientRpc(float multiplier)
    {
        GameEventAnnouncer.Announce(this, title, string.Format(subtitle, multiplier), accent, colorSkin);
    }
}