using Unity.Netcode;
using UnityEngine;
using TMPro;

public class eventSpeedUpTimer : NetworkBehaviour, IGameEvent
{
    [Header("Configuración del Evento")]
    [Tooltip("Segundos exactos que se restarán del reloj de la partida cuando salte el evento.")]
    [SerializeField] float secondsToCut = 60f;

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
            // Aplicamos el recorte usando directamente el valor configurado en el Inspector
            matchTimer.ApplyCustomTimeReduction(secondsToCut);
        }
        else
        {
            Debug.LogError("eventSpeedUpTimer: No se encontró ningún MatchTimer para recortar tiempo.");
        }

        TriggerSpeedUpClientRpc(Mathf.RoundToInt(secondsToCut));

        Debug.Log($"¡TIEMPO ACELERADO! Se han recortado exactamente {secondsToCut} segundos configurados en el Inspector.");
    }

    [ClientRpc]
    void TriggerSpeedUpClientRpc(int secondsCut)
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
            PopUpText.text = $"¡TIEMPO ACELERADO!\n¡Se han recortado {secondsCut} segundos!";
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