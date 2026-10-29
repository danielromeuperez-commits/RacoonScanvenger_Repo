using Unity.Netcode;
using UnityEngine;
using TMPro;

public class eventSpeedUpTimer : NetworkBehaviour, IGameEvent
{
    [Header("Configuración del Evento de Velocidad")]
    [Tooltip("Multiplicador de velocidad permanente (ej: 2 para el doble de rápido, 3 para el triple).")]
    [SerializeField] float speedMultiplier = 2f;

    [Header("Configuración de UI")]
    [SerializeField] GameObject PopUpPanel;
    [SerializeField] TextMeshProUGUI PopUpText;
    [SerializeField] float displayDuration = 5f;

    public bool HasTriggered { get; private set; } = false;

    public override void OnNetworkSpawn()
    {
        if (PopUpPanel != null)
            PopUpPanel.SetActive(false);
    }

    public void TriggerEventManually()
    {
        if (HasTriggered || !IsServer)
            return;

        HasTriggered = true;

        MatchTimer matchTimer = FindFirstObjectByType<MatchTimer>();

        if (matchTimer != null)
        {
            // Aplica la velocidad permanente en el MatchTimer
            matchTimer.ApplyTimeMultiplier(speedMultiplier);
        }
        else
        {
            Debug.LogError("eventSpeedUpTimer: No se encontró ningún MatchTimer para acelerar.");
        }

        TriggerSpeedUpClientRpc(speedMultiplier);

        Debug.Log($"¡EVENTO DE VELOCIDAD PERMANENTE! El cronómetro irá a x{speedMultiplier} hasta el final de la partida.");
    }

    [ClientRpc]
    void TriggerSpeedUpClientRpc(float multiplier)
    {
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(true);

            if (displayDuration > 0)
            {
                CancelInvoke(nameof(HideUI));
                Invoke(nameof(HideUI), displayDuration);
            }
        }

        if (PopUpText != null)
        {
            PopUpText.text = $"¡RITMO ACELERADO (x{multiplier})!\n¡El tiempo irá más rápido el resto de la partida!";
        }
    }

    void HideUI()
    {
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(false);
        }
    }
}