using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

public class MatchTimer : NetworkBehaviour
{
    [Header("Partida")]
    [Tooltip("Duración de la partida en segundos.")]
    [SerializeField, Min(1f)] float matchDuration = 300f;

    [Tooltip("Déjalo desactivado si utilizas el lobby de selección de personaje.")]
    [SerializeField] bool autoStart = false;

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
    /// <summary>Multiplicador de velocidad actual del cronómetro (1 = normal).</summary>
    public float TimeScale => timeScale.Value;

    public float TimeRemaining
    {
        get
        {
            if (!IsSpawned)
                return matchDuration;

            if (matchFinished.Value)
                return 0f;

            // Mientras todavía estamos en selección de personaje,
            // mostramos la duración completa y el reloj no avanza.
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

        // Solo se utilizará si quieres probar el MatchTimer
        // sin pasar por el lobby.
        if (IsServer && autoStart)
            ServerStartMatch();
    }

    public override void OnNetworkDespawn()
    {
        matchFinished.OnValueChanged -= OnMatchFinishedChanged;
    }

    void Update()
    {
        if (!IsServer ||
            !matchRunning.Value ||
            matchFinished.Value)
        {
            return;
        }

        double currentServerTime =
            NetworkManager.ServerTime.Time;

        double deltaTime =
            currentServerTime - lastServerTime;

        lastServerTime =
            currentServerTime;

        // Si la escala de tiempo cambió
        // (ej: x2 o x3), consumimos tiempo extra.
        if (timeScale.Value != 1f &&
            deltaTime > 0)
        {
            double extraTimePassed =
                deltaTime * (timeScale.Value - 1.0);

            matchEndTime.Value -=
                extraTimePassed;
        }

        if (NetworkManager.ServerTime.Time <
            matchEndTime.Value)
        {
            return;
        }

        matchRunning.Value = false;

        DetermineWinner();

        matchFinished.Value = true;
    }

    // ============================================================
    // EMPEZAR PARTIDA
    // ============================================================

    public void ServerStartMatch()
    {
        if (!IsServer)
            return;

        // Evita arrancarla dos veces.
        if (matchRunning.Value)
            return;

        matchFinished.Value = false;
        matchRunning.Value = true;

        timeScale.Value = 1f;

        lastServerTime =
            NetworkManager.ServerTime.Time;

        matchEndTime.Value =
            NetworkManager.ServerTime.Time +
            matchDuration;

        Debug.Log(
            $"MatchTimer: comienza la partida. " +
            $"Duración: {matchDuration} segundos."
        );
    }

    // ============================================================
    // MULTIPLICADOR DE TIEMPO
    // ============================================================

    public void ApplyTimeMultiplier(
        float multiplier)
    {
        if (!IsServer ||
            !matchRunning.Value)
        {
            return;
        }

        timeScale.Value =
            Mathf.Max(0.1f, multiplier);

        Debug.Log(
            $"MatchTimer: ¡El cronómetro correrá a " +
            $"x{timeScale.Value} durante el resto de la partida!"
        );
    }

    // ============================================================
    // GANADOR
    // ============================================================

    void DetermineWinner()
    {
        if (!IsServer)
            return;

        PlayerLoot[] players =
            FindObjectsByType<PlayerLoot>(
                FindObjectsSortMode.None
            );

        if (players.Length < 2)
        {
            Debug.LogWarning(
                "No hay suficientes jugadores " +
                "para determinar un ganador."
            );

            return;
        }

        Array.Sort(
            players,
            (a, b) =>
                a.OwnerClientId.CompareTo(
                    b.OwnerClientId
                )
        );

        PlayerLoot player1 =
            players[0];

        PlayerLoot player2 =
            players[1];

        int player1Points =
            player1.BankedPoints;

        int player2Points =
            player2.BankedPoints;

        Debug.Log(
            $"RESULTADO FINAL -> " +
            $"Jugador {player1.OwnerClientId}: " +
            $"{player1Points} puntos | " +
            $"Jugador {player2.OwnerClientId}: " +
            $"{player2Points} puntos"
        );

        if (player1Points >
            player2Points)
        {
            Debug.Log(
                $"Ganador: jugador " +
                $"{player1.OwnerClientId}"
            );

            player1.ServerSendMatchResult(true);
            player2.ServerSendMatchResult(false);

            // Avisamos a los dos clientes de qui�n ha ganado.
            ShowMatchResultClientRpc(
                player1.OwnerClientId,
                false
            );
        }
        else if (
            player2Points >
            player1Points)
        {
            Debug.Log(
                $"Ganador: jugador " +
                $"{player2.OwnerClientId}"
            );

            player1.ServerSendMatchResult(false);
            player2.ServerSendMatchResult(true);

            // Avisamos a los dos clientes de qui�n ha ganado.
            ShowMatchResultClientRpc(
                player2.OwnerClientId,
                false
            );
        }
        else
        {
            Debug.Log(
                "La partida ha terminado en empate."
            );

            player1.ServerSendDrawResult();
            player2.ServerSendDrawResult();

            // En empate ambos jugadores ver�n el panel DRAW.
            ShowMatchResultClientRpc(
                0,
                true
            );
        }
    }

    // ============================================================
    // MOSTRAR RESULTADO
    // ============================================================

    [ClientRpc]
    void ShowMatchResultClientRpc(
        ulong winnerClientId,
        bool draw)
    {
        if (MatchResultUI.Instance == null)
        {
            Debug.LogWarning(
                "MatchTimer: no se encontr� ning�n MatchResultUI."
            );

            return;
        }

        // Cada PC monta su pantalla: ganador, perdedor (o empate)
        // y los personajes de los dos jugadores.
        MatchResultUI.Instance.ShowResult(winnerClientId, draw);
    }

    void OnMatchFinishedChanged(
        bool previous,
        bool current)
    {
        if (!current)
            return;

        MatchEnded?.Invoke();

        if (PlayerController.Local != null)
        {
            PlayerController.Local
                .InputHandler
                .SetInputEnabled(false);
        }
    }

    public void ApplyCustomTimeReduction(
        float secondsToSubtract)
    {
        if (!IsServer ||
            !matchRunning.Value)
        {
            return;
        }

        matchEndTime.Value -=
            secondsToSubtract;
    }
}