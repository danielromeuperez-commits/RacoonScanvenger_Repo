using System.Collections.Generic;
using Racoon.Player;
using TMPro;
using UnityEngine;

public class MatchHUD : MonoBehaviour
{
    [Header("Puntos")]
    [SerializeField] TMP_Text player1PointsText;
    [SerializeField] TMP_Text player2PointsText;

    [Header("Tiempo")]
    [SerializeField] TMP_Text timerText;
    [SerializeField] MatchTimer matchTimer;

    readonly List<PlayerLoot> players = new();

    void Awake()
    {
        if (matchTimer == null)
            matchTimer = FindFirstObjectByType<MatchTimer>();

        RefreshPoints();
    }

    void Update()
    {
        UpdateTimer();
    }

    public void RegisterPlayer(PlayerLoot player)
    {
        if (player == null || players.Contains(player))
            return;

        players.Add(player);

        players.Sort((a, b) =>
        {
            int ownerComparison =
                a.OwnerClientId.CompareTo(b.OwnerClientId);

            if (ownerComparison != 0)
                return ownerComparison;

            return a.NetworkObjectId.CompareTo(b.NetworkObjectId);
        });

        player.LootChanged += RefreshPoints;

        RefreshPoints();
    }

    public void UnregisterPlayer(PlayerLoot player)
    {
        if (player == null)
            return;

        player.LootChanged -= RefreshPoints;
        players.Remove(player);

        RefreshPoints();
    }

    void RefreshPoints()
    {
        if (player1PointsText != null)
        {
            player1PointsText.text =
                players.Count > 0
                    ? GetPointsText(players[0])
                    : "0";
        }

        if (player2PointsText != null)
        {
            player2PointsText.text =
                players.Count > 1
                    ? GetPointsText(players[1])
                    : "0";
        }
    }

    string GetPointsText(PlayerLoot player)
    {
        if (player == null)
            return "0";

        int banked = player.BankedPoints;
        int pending = player.PendingPoints;

        // Si todavía no ha vendido nada, mostramos
        // únicamente los puntos pendientes en rojo.
        if (banked == 0 && pending > 0)
        {
            return $"<color=#E84C4C>{pending}</color>";
        }

        // Si tiene puntos vendidos y además lleva nuevo loot,
        // mostramos los vendidos en verde y los pendientes en rojo.
        if (pending > 0)
        {
            return
                $"<color=#55C96B>{banked}</color> " +
                $"<color=#E84C4C>+{pending}</color>";
        }

        // Si no lleva loot pendiente, mostramos
        // únicamente los puntos ya vendidos en verde.
        return $"<color=#55C96B>{banked}</color>";
    }

    void UpdateTimer()
    {
        if (timerText == null || matchTimer == null)
            return;

        int totalSeconds =
            Mathf.CeilToInt(matchTimer.TimeRemaining);

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        timerText.text = $"{minutes:00}:{seconds:00}";
    }
}