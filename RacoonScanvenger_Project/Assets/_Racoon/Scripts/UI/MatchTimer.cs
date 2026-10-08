using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

public class MatchTimer : NetworkBehaviour
{
    [Header("Partida")]
    [SerializeField] float matchDuration = 300f;
    [SerializeField] bool autoStart = true;

    readonly NetworkVariable<double> matchEndTime = new(0);
    readonly NetworkVariable<bool> matchRunning = new(false);
    readonly NetworkVariable<bool> matchFinished = new(false);

    public event Action MatchEnded;

    public bool IsRunning => matchRunning.Value;
    public bool IsFinished => matchFinished.Value;

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
            return;

        if (NetworkManager.ServerTime.Time < matchEndTime.Value)
            return;

        matchRunning.Value = false;
        matchFinished.Value = true;
    }

    public void ServerStartMatch()
    {
        if (!IsServer)
            return;

        matchFinished.Value = false;
        matchRunning.Value = true;

        matchEndTime.Value =
            NetworkManager.ServerTime.Time + matchDuration;
    }

    void OnMatchFinishedChanged(bool previous, bool current)
    {
        if (!current)
            return;

        MatchEnded?.Invoke();

        if (PlayerController.Local != null)
            PlayerController.Local.InputHandler.SetInputEnabled(false);
    }
}