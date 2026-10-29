using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

public class MatchTimer : NetworkBehaviour
{
    [Header("Partida")]
    [Tooltip("Duración de la partida en segundos.")]
    [SerializeField, Min(1f)] float matchDuration = 300f;

    [SerializeField] bool autoStart = true;

    readonly NetworkVariable<double> matchEndTime = new(0);
    readonly NetworkVariable<bool> matchRunning = new(false);
    readonly NetworkVariable<bool> matchFinished = new(false);

    // Multiplicador de velocidad permanente para el resto de la partida
    readonly NetworkVariable<float> timeScale = new(1f);
    double lastServerTime = 0;

    public event Action MatchEnded;

    public bool IsRunning => matchRunning.Value;
    public bool IsFinished => matchFinished.Value;
    public float Duration => matchDuration;

    public float TimeRemaining
    {
        get
        {
            if (!IsSpawned)
                return matchDuration;

            if (matchFinished.Value)
                return 0f;

            if (!matchRunning.Value)
                return matchDuration;

            return Mathf.Max(
                0f,
                (float)(matchEndTime.Value - NetworkManager.ServerTime.Time)
            );
        }
    }

    public override void OnNetworkSpawn()
    {
        matchFinished.OnValueChanged += OnMatchFinishedChanged;
        lastServerTime = NetworkManager.ServerTime.Time;

        if (IsServer && autoStart)
            ServerStartMatch();
    }

    public override void OnNetworkDespawn()
    {
        matchFinished.OnValueChanged -= OnMatchFinishedChanged;
    }

    void Update()
    {
        if (!IsServer || !matchRunning.Value || matchFinished.Value)
            return;

        double currentServerTime = NetworkManager.ServerTime.Time;
        double deltaTime = currentServerTime - lastServerTime;
        lastServerTime = currentServerTime;

        // Si la escala de tiempo cambió (ej: x2 o x3), consumimos el tiempo restante más rápido de forma permanente
        if (timeScale.Value != 1f && deltaTime > 0)
        {
            double extraTimePassed = deltaTime * (timeScale.Value - 1.0);
            matchEndTime.Value -= extraTimePassed;
        }

        if (NetworkManager.ServerTime.Time < matchEndTime.Value)
            return;

        matchRunning.Value = false;
        DetermineWinner();
        matchFinished.Value = true;
    }

    public void ServerStartMatch()
    {
        if (!IsServer)
            return;

        matchFinished.Value = false;
        matchRunning.Value = true;
        timeScale.Value = 1f;
        lastServerTime = NetworkManager.ServerTime.Time;

        matchEndTime.Value =
            NetworkManager.ServerTime.Time + matchDuration;
    }

    // Aplica el multiplicador de forma permanente para el resto de la partida
    public void ApplyTimeMultiplier(float multiplier)
    {
        if (!IsServer || !matchRunning.Value)
            return;

        timeScale.Value = Mathf.Max(0.1f, multiplier);
        Debug.Log($"MatchTimer: ¡El cronómetro correrá a x{timeScale.Value} durante el resto de la partida!");
    }

    void DetermineWinner()
    {
        if (!IsServer)
            return;

        PlayerLoot[] players =
            FindObjectsByType<PlayerLoot>(FindObjectsSortMode.None);

        if (players.Length < 2)
        {
            Debug.LogWarning(
                "No hay suficientes jugadores para determinar un ganador."
            );
            return;
        }

        System.Array.Sort(players, (a, b) =>
            a.OwnerClientId.CompareTo(b.OwnerClientId)
        );

        PlayerLoot player1 = players[0];
        PlayerLoot player2 = players[1];

        int player1Points = player1.BankedPoints;
        int player2Points = player2.BankedPoints;

        Debug.Log(
            $"RESULTADO FINAL -> " +
            $"Jugador {player1.OwnerClientId}: {player1Points} puntos | " +
            $"Jugador {player2.OwnerClientId}: {player2Points} puntos"
        );

        if (player1Points > player2Points)
        {
            Debug.Log($"Ganador: jugador {player1.OwnerClientId}");
            player1.ServerSendMatchResult(true);
            player2.ServerSendMatchResult(false);
        }
        else if (player2Points > player1Points)
        {
            Debug.Log($"Ganador: jugador {player2.OwnerClientId}");
            player1.ServerSendMatchResult(false);
            player2.ServerSendMatchResult(true);
        }
        else
        {
            Debug.Log("La partida ha terminado en empate.");
            player1.ServerSendDrawResult();
            player2.ServerSendDrawResult();
        }
    }

    void OnMatchFinishedChanged(bool previous, bool current)
    {
        if (!current)
            return;

        MatchEnded?.Invoke();

        if (PlayerController.Local != null)
            PlayerController.Local.InputHandler.SetInputEnabled(false);
    }

    public void ApplyCustomTimeReduction(float secondsToSubtract)
    {
        if (!IsServer || !matchRunning.Value)
            return;

        matchEndTime.Value -= secondsToSubtract;
    }
}