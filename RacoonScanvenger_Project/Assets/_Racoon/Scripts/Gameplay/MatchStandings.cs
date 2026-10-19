using System;
using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Clasificación de la partida: quién es P1 y P2, quién va ganando (TOP) y la racha de cada jugador.
    ///
    /// Llamas: el jugador está en llamas cuando sus últimas <see cref="FireSales"/> ventas seguidas suman
    /// <see cref="FirePoints"/> puntos o más (por defecto, 200 en 2 ventas) sin haber perdido puntos de la bolsa.
    /// Se recalcula en cada venta, así que la llama dura mientras sigas vendiendo fuerte. Perder puntos de la bolsa
    /// (si algo te los quita sin venderlos) apaga la llama y empieza la racha de cero.
    /// La racha («xN») cuenta las ventas seguidas sin perder puntos de la bolsa.
    ///
    /// Se calcula en cada cliente a partir de lo que ya replica la red (los puntos de PlayerLoot). Todos los clientes
    /// ven la misma secuencia, así que llegan al mismo resultado sin sincronizar nada más. Si algún día la llama da
    /// ventajas de juego (p. ej. puntos dobles), hay que calcularla en el servidor.
    ///
    /// Orden de P1 y P2: por OwnerClientId y, si empatan, por NetworkObjectId (igual que MatchHUD e ItemUIManager).
    /// Puede ir en el MatchManager; si no hay ninguno en la escena, el HUD crea el suyo.
    /// </summary>
    public class MatchStandings : MonoBehaviour
    {
        public const int MaxPlayers = 2;

        /// <summary>Estado de un jugador en la clasificación.</summary>
        public sealed class Standing
        {
            /// <summary>0 = P1, 1 = P2.</summary>
            public int Index { get; internal set; }
            public PlayerLoot Loot { get; internal set; }
            public PlayerController Controller { get; internal set; }
            public PlayerInventory Inventory { get; internal set; }
            /// <summary>Puntos asegurados (vendidos): son los que deciden la partida.</summary>
            public int Banked { get; internal set; }
            /// <summary>Puntos que lleva en la bolsa sin vender.</summary>
            public int Pending { get; internal set; }
            /// <summary>Ventas seguidas sin perder puntos de la bolsa.</summary>
            public int Streak { get; internal set; }
            public bool OnFire { get; internal set; }

            internal readonly List<int> recentSales = new();
            internal int frameGain, frameDrop;
            internal Action lootHandler;
        }

        [Header("Llamas")]
        [Tooltip("Ventas seguidas que se tienen en cuenta.")]
        [SerializeField, Min(1)] int fireSales = 2;
        [Tooltip("Puntos que deben sumar esas ventas (sin perder puntos de la bolsa entre medias).")]
        [SerializeField, Min(0)] int firePoints = 200;
        [Tooltip("Activado: cada una de esas ventas debe llegar a los puntos por sí sola, en lugar de sumarlos entre todas.")]
        [SerializeField] bool eachSale;

        [Tooltip("Cada cuánto se buscan jugadores mientras falte alguno (s).")]
        [SerializeField, Min(.1f)] float rescanInterval = .5f;

        readonly List<Standing> players = new();
        readonly List<PlayerLoot> scanBuffer = new();
        float nextScan;

        /// <summary>Cambian los jugadores o su orden (alguien entra o sale).</summary>
        public event Action PlayersChanged;
        /// <summary>Un jugador vende: (jugador, puntos asegurados en esta venta).</summary>
        public event Action<Standing, int> Scored;
        /// <summary>Cambian los puntos de la bolsa: (jugador, antes, ahora).</summary>
        public event Action<Standing, int, int> PendingChanged;
        /// <summary>Un jugador pierde puntos de la bolsa sin venderlos: (jugador, puntos perdidos).</summary>
        public event Action<Standing, int> BagLost;
        /// <summary>Cambia la racha o las llamas de un jugador.</summary>
        public event Action<Standing> StreakChanged;
        public event Action LeaderChanged;

        public IReadOnlyList<Standing> Players => players;
        public int FireSales => fireSales;
        public int FirePoints => firePoints;
        /// <summary>Quien va ganando con puntos asegurados. Null si empatan.</summary>
        public Standing Leader { get; private set; }

        public bool IsOnFire(Standing standing) => standing != null && standing.OnFire;

        public Standing Find(PlayerLoot loot) => players.Find(p => p.Loot == loot);

        void Update()
        {
            if (Time.unscaledTime < nextScan || !NeedsScan()) return;
            nextScan = Time.unscaledTime + rescanInterval;
            Scan();
        }

        // Una venta llega como «más puntos asegurados» y «bolsa a 0», a veces en avisos separados del mismo
        // fotograma: se compara al final del fotograma lo que bajó la bolsa con lo que se aseguró
        void LateUpdate()
        {
            foreach (var standing in players)
            {
                int lost = standing.frameDrop - standing.frameGain;
                standing.frameDrop = standing.frameGain = 0;
                if (lost <= 0) continue;
                standing.recentSales.Clear();
                SetStreak(standing, 0, false);
                BagLost?.Invoke(standing, lost);
            }
        }

        void OnDestroy()
        {
            foreach (var standing in players) Unsubscribe(standing);
            players.Clear();
        }

        bool NeedsScan()
        {
            if (players.Count < MaxPlayers) return true;
            foreach (var standing in players)
                if (standing.Loot == null || !standing.Loot.IsSpawned) return true;
            return false;
        }

        void Scan()
        {
            scanBuffer.Clear();
            foreach (var loot in FindObjectsByType<PlayerLoot>(FindObjectsSortMode.None))
                if (loot.IsSpawned) scanBuffer.Add(loot);
            scanBuffer.Sort((a, b) =>
            {
                int owner = a.OwnerClientId.CompareTo(b.OwnerClientId);
                return owner != 0 ? owner : a.NetworkObjectId.CompareTo(b.NetworkObjectId);
            });
            if (scanBuffer.Count > MaxPlayers) scanBuffer.RemoveRange(MaxPlayers, scanBuffer.Count - MaxPlayers);

            bool same = scanBuffer.Count == players.Count;
            for (int i = 0; same && i < players.Count; i++) same = players[i].Loot == scanBuffer[i];
            if (same) return;

            var previous = new List<Standing>(players);
            players.Clear();
            foreach (var loot in scanBuffer)
            {
                var standing = previous.Find(p => p.Loot == loot) ?? Create(loot);
                previous.Remove(standing);
                standing.Index = players.Count;
                players.Add(standing);
            }
            foreach (var gone in previous) Unsubscribe(gone);

            UpdateLeader();
            PlayersChanged?.Invoke();
        }

        Standing Create(PlayerLoot loot)
        {
            var standing = new Standing
            {
                Loot = loot,
                Controller = loot.GetComponent<PlayerController>(),
                Inventory = loot.GetComponent<PlayerInventory>(),
                Banked = loot.BankedPoints,
                Pending = loot.PendingPoints,
            };
            standing.lootHandler = () => OnLootChanged(standing);
            loot.LootChanged += standing.lootHandler;
            return standing;
        }

        static void Unsubscribe(Standing standing)
        {
            if (standing.Loot != null) standing.Loot.LootChanged -= standing.lootHandler;
        }

        void OnLootChanged(Standing standing)
        {
            if (standing.Loot == null) return;

            int banked = standing.Loot.BankedPoints;
            if (banked != standing.Banked)
            {
                int gained = banked - standing.Banked;
                standing.Banked = banked;
                if (gained > 0)
                {
                    standing.frameGain += gained;
                    RegisterSale(standing, gained);
                    Scored?.Invoke(standing, gained);
                }
                UpdateLeader();
            }

            int pending = standing.Loot.PendingPoints;
            if (pending != standing.Pending)
            {
                int before = standing.Pending;
                standing.Pending = pending;
                if (pending < before) standing.frameDrop += before - pending;
                PendingChanged?.Invoke(standing, before, pending);
            }
        }

        void RegisterSale(Standing standing, int gained)
        {
            var sales = standing.recentSales;
            sales.Add(gained);
            if (sales.Count > fireSales) sales.RemoveRange(0, sales.Count - fireSales);

            bool onFire = sales.Count >= fireSales;
            int total = 0;
            foreach (int sale in sales)
            {
                total += sale;
                if (eachSale && sale < firePoints) onFire = false;
            }
            if (!eachSale && total < firePoints) onFire = false;
            SetStreak(standing, standing.Streak + 1, onFire);
        }

        void SetStreak(Standing standing, int streak, bool onFire)
        {
            if (standing.Streak == streak && standing.OnFire == onFire) return;
            standing.Streak = streak;
            standing.OnFire = onFire;
            StreakChanged?.Invoke(standing);
        }

        void UpdateLeader()
        {
            // Con todo a 0 o empatados en cabeza, nadie lleva el TOP
            Standing leader = null;
            int best = 0;
            bool tie = false;
            foreach (var standing in players)
            {
                if (standing.Banked > best)
                {
                    best = standing.Banked;
                    leader = standing;
                    tie = false;
                }
                else if (standing.Banked == best && best > 0) tie = true;
            }
            if (tie) leader = null;

            if (leader == Leader) return;
            Leader = leader;
            LeaderChanged?.Invoke();
        }
    }
}
