using System;
using System.Collections.Generic;
using Racoon.Gameplay;
using Racoon.Items;
using Racoon.Network;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Conecta el HUD con la partida y lo anima. Va en la raíz de cada prefab PF_HUD_* (lo pone el montaje), así que
    /// basta con poner el prefab en la escena: él solo encuentra el MatchTimer y a los jugadores.
    ///  - Puntos: rodillo con los puntos vendidos (PlayerLoot.BankedPoints); al vender, «+N» y salto.
    ///  - Bolsa: puntos sin vender (PlayerLoot.PendingPoints); al vender vuela al marcador.
    ///  - Tiempo: MatchTimer (m:ss, barra, losa roja y latido en los últimos segundos).
    ///  - Estamina: PlayerController.StaminaNormalized de cada jugador (también la del rival).
    ///  - Ítems: PlayerInventory (ruleta al recoger, usar, intercambiar, entrada y flotar).
    ///  - Racha, llama y TOP: <see cref="MatchStandings"/>.
    ///  - Color, apodo e ilustración: PlayerVFX.WindTint y el personaje (PlayerCharacterDefinition).
    /// No necesita MatchHUD, ItemUIManager ni StaminaBarUI: pueden convivir con él o quitarse.
    /// </summary>
    [RequireComponent(typeof(HUDView))]
    public class HUDController : MonoBehaviour
    {
        [Serializable]
        public class CharacterLabel
        {
            public PlayerCharacterDefinition character;
            [Tooltip("Apodo de la etiqueta (solo en las variantes con apodo). Vacío = el displayName en mayúsculas.")]
            public string nickname;
            [Tooltip("Ilustración del retrato. Vacía = el portrait del personaje, si tiene.")]
            public Sprite illustration;
        }

        [SerializeField] HUDView view;
        [Tooltip("Vacío: el de la escena y, si no hay ninguno, uno propio en este objeto.")]
        [SerializeField] MatchStandings standings;
        [Tooltip("Vacío: el de la escena.")]
        [SerializeField] MatchTimer matchTimer;

        [Header("Personajes")]
        [SerializeField] List<CharacterLabel> characters = new();

        [Header("Opciones")]
        [Tooltip("Muestra junto a los ítems los puntos que lleva en la bolsa sin vender.")]
        [SerializeField] bool showBag = true;
        [Tooltip("Por debajo de esta estamina el aro parpadea.")]
        [SerializeField, Range(0f, 1f)] float lowStamina = .25f;
        [SerializeField, Min(0f)] float staminaSmoothing = 12f;
        [Tooltip("Segundos finales con la losa roja y el latido de los dígitos.")]
        [SerializeField, Min(0f)] float urgentSeconds = 30f;
        [Tooltip("Duración que se muestra mientras no hay MatchTimer (s).")]
        [SerializeField, Min(1f)] float defaultDuration = 300f;

        readonly HUDPlayerPresenter[] presenters = new HUDPlayerPresenter[2];
        readonly MatchStandings.Standing[] bound = new MatchStandings.Standing[2];
        readonly Action[] inventoryHandlers = new Action[2];
        readonly List<ItemData> itemPool = new();
        // Reacciones del retrato a lo que le pasa a cada jugador (solo en las variantes con HUDView.reactions)
        readonly Action<PlayerState>[] actionHandlers = new Action<PlayerState>[2];
        readonly Action<PlayerState, PlayerState>[] stateHandlers = new Action<PlayerState, PlayerState>[2];
        ItemDatabase poolSource;
        int shownSeconds = -1;
        float nextTimerSearch, sessionStart = -1f;
        bool demoMode, warnedNoTimer, reportedTimer;

        public HUDView View => view;
        public MatchStandings Standings => standings;
        /// <summary>Apodo e ilustración de cada personaje.</summary>
        public List<CharacterLabel> Characters => characters;

        /// <summary>P1 = 1, P2 = 2.</summary>
        public HUDPlayerPresenter Player(int number) => presenters[number - 1];

        /// <summary>
        /// Sin datos de partida: el HUD solo anima lo que le digan <see cref="Player"/> y <see cref="ShowTime"/>
        /// (lo usa <see cref="HUDDemo"/>). Al desactivarlo vuelve a leer la partida.
        /// </summary>
        public bool DemoMode
        {
            get => demoMode;
            set
            {
                if (demoMode == value) return;
                demoMode = value;
                for (int i = 0; i < 2; i++) Bind(i, null);
                if (!value) Rebind();
            }
        }

        void Awake()
        {
            if (!view) view = GetComponent<HUDView>();
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                presenters[i] = new HUDPlayerPresenter(this, view, i + 1)
                {
                    LowStamina = lowStamina,
                    StaminaSmoothing = staminaSmoothing,
                    ShowBag = showBag,
                };
                inventoryHandlers[i] = () => SyncItems(index, true);
                actionHandlers[i] = state => OnAction(index, state);
                stateHandlers[i] = (_, state) => OnState(index, state);
                presenters[i].ResetView();
            }
            ShowTime(defaultDuration, defaultDuration, false, false);
        }

        void OnEnable()
        {
            if (!standings) standings = FindFirstObjectByType<MatchStandings>();
            if (!standings) standings = gameObject.AddComponent<MatchStandings>();
            standings.PlayersChanged += Rebind;
            standings.Scored += OnScored;
            standings.PendingChanged += OnPendingChanged;
            standings.StreakChanged += OnStreakChanged;
            standings.LeaderChanged += RefreshLeader;
            Rebind();
        }

        void OnDisable()
        {
            if (standings)
            {
                standings.PlayersChanged -= Rebind;
                standings.Scored -= OnScored;
                standings.PendingChanged -= OnPendingChanged;
                standings.StreakChanged -= OnStreakChanged;
                standings.LeaderChanged -= RefreshLeader;
            }
            for (int i = 0; i < 2; i++) Unbind(i); // sin tocar las piezas: puede estar descargándose la escena
        }

        void OnValidate()
        {
            foreach (var presenter in presenters)
            {
                if (presenter == null) continue;
                presenter.LowStamina = lowStamina;
                presenter.StaminaSmoothing = staminaSmoothing;
                presenter.ShowBag = showBag;
            }
        }

        void Update()
        {
            if (!demoMode)
            {
                UpdateTimer();
                for (int i = 0; i < 2; i++)
                    if (bound[i] != null && bound[i].Controller) presenters[i].SetStamina(bound[i].Controller.StaminaNormalized);
            }
            float deltaTime = Time.unscaledDeltaTime;
            foreach (var presenter in presenters) presenter.Tick(deltaTime);
        }

        void LateUpdate()
        {
            foreach (var presenter in presenters) presenter.LateTick();
        }

        // ---------------- Tiempo ----------------

        void UpdateTimer()
        {
            if (!matchTimer && Time.unscaledTime >= nextTimerSearch)
            {
                nextTimerSearch = Time.unscaledTime + 1f;
                matchTimer = FindFirstObjectByType<MatchTimer>();
            }
            var network = NetworkManager.Singleton;
            bool inMatch = network && network.IsListening;
            if (!matchTimer)
            {
                if (inMatch) WarnOnce(ref warnedNoTimer, "HUD: la escena no tiene MatchTimer, así que el tiempo no corre. " +
                                                         "Añade PF_MNG_MatchManager (Racoon → HUD → Preparar escena abierta).");
                ShowTime(defaultDuration, defaultDuration, false, false);
                return;
            }
            // A los 3 s de empezar la sesión de red, una línea en consola con el estado del tiempo
            if (!inMatch) sessionStart = -1f;
            else if (sessionStart < 0f) sessionStart = Time.unscaledTime;
            else if (Time.unscaledTime - sessionStart > 3f) ReportTimerOnce(network);
            ShowTime(matchTimer.TimeRemaining, matchTimer.Duration, matchTimer.IsRunning, matchTimer.IsFinished);
        }

        void ReportTimerOnce(NetworkManager network)
        {
            if (reportedTimer) return;
            reportedTimer = true;
            static string YesNo(bool value) => value ? "sí" : "no";
            string state = $"MatchTimer de «{matchTimer.name}»: en red {YesNo(matchTimer.IsSpawned)}, en marcha {YesNo(matchTimer.IsRunning)}, " +
                           $"terminado {YesNo(matchTimer.IsFinished)}, quedan {matchTimer.TimeRemaining:0.0} s de {matchTimer.Duration:0} s; " +
                           $"este equipo es {(network.IsHost ? "host" : network.IsServer ? "servidor" : "cliente")}";
            if (matchTimer.IsRunning || matchTimer.IsFinished) Debug.Log($"HUD: el tiempo corre ({state}).", this);
            else Debug.LogWarning($"HUD: el tiempo NO corre ({state}). Sin «en red», al MatchTimer le falta el NetworkObject o no se ha " +
                                  "spawneado; sin «en marcha», no se ha llamado a ServerStartMatch (¿Auto Start desactivado?).", this);
        }

        void WarnOnce(ref bool warned, string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning(message, this);
        }

        /// <summary>
        /// Pinta el tiempo: m:ss en el texto y sus sombras, barra, losa roja en los últimos segundos y latido
        /// (acompasado con el cambio de segundo) mientras corre.
        /// </summary>
        public void ShowTime(float remaining, float duration, bool running, bool finished)
        {
            remaining = Mathf.Max(0f, remaining);
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds;
                view.SetTimeText($"{seconds / 60}:{seconds % 60:00}");
            }
            var timer = view.timer;
            if (timer.barFill) timer.barFill.fillAmount = duration > 0f ? remaining / duration : 0f;
            bool urgent = (running || finished) && remaining <= urgentSeconds;
            if (timer.urgentSlab && timer.urgentSlab.activeSelf != urgent) timer.urgentSlab.SetActive(urgent);
            if (timer.urgentTextColor.a > 0f && timer.time) timer.time.color = urgent ? timer.urgentTextColor : timer.textColor;
            float scale = urgent && running ? HUDAnim.Beat(Mathf.Repeat(-remaining, 1f)) : 1f;
            if (timer.digits) timer.digits.localScale = new Vector3(scale, scale, 1f);
        }

        // ---------------- Jugadores ----------------

        void Rebind()
        {
            if (demoMode || !standings) return;
            var players = standings.Players;
            for (int i = 0; i < 2; i++)
            {
                var standing = i < players.Count ? players[i] : null;
                if (standing != bound[i]) Bind(i, standing);
            }
            RefreshLeader();
        }

        void Unbind(int index)
        {
            var previous = bound[index];
            if (previous != null && previous.Inventory) previous.Inventory.InventoryChanged -= inventoryHandlers[index];
            if (previous != null && previous.Controller)
            {
                previous.Controller.ActionStarted -= actionHandlers[index];
                previous.Controller.StateChanged -= stateHandlers[index];
            }
            bound[index] = null;
        }

        void Bind(int index, MatchStandings.Standing standing)
        {
            Unbind(index);
            bound[index] = standing;

            var presenter = presenters[index];
            if (presenter == null) return;
            presenter.ResetView();
            if (standing == null || !standing.Loot) return;

            var vfx = standing.Loot.GetComponentInChildren<PlayerVFX>(true);
            if (vfx) presenter.SetColor(vfx.WindTint);
            var definition = FindCharacter(standing.Loot.NetworkObject, out var label);
            string nickname = label != null && !string.IsNullOrEmpty(label.nickname) ? label.nickname
                : definition ? definition.displayName?.ToUpperInvariant() : null;
            presenter.SetNickname(nickname);
            presenter.SetIllustration(label != null && label.illustration ? label.illustration
                : definition ? definition.portrait : null);

            presenter.SetScore(standing.Banked, 0);
            presenter.SetPending(standing.Pending, false);
            presenter.SetStreak(standing.Streak, standings.IsOnFire(standing), false);
            presenter.SetTop(standings.Leader == standing, false);
            if (standing.Controller) presenter.SetStamina(standing.Controller.StaminaNormalized);
            if (standing.Inventory) standing.Inventory.InventoryChanged += inventoryHandlers[index];
            SyncItems(index, false);
            if (standing.Controller)
            {
                standing.Controller.ActionStarted += actionHandlers[index];
                standing.Controller.StateChanged += stateHandlers[index];
                OnState(index, standing.Controller.State);
            }
        }

        // Golpe ligero, golpe fuerte, aturdido por un objeto (plátano), voltereta (dash) y puñetazo
        void OnAction(int index, PlayerState state)
        {
            if (demoMode) return;
            HUDReaction? reaction = state switch
            {
                PlayerState.HitStun => HUDReaction.Hit,
                PlayerState.Knockdown => HUDReaction.HeavyHit,
                PlayerState.Downed => HUDReaction.Slip,
                PlayerState.Dash => HUDReaction.Roll,
                PlayerState.Punch => HUDReaction.Punch,
                _ => null,
            };
            if (reaction.HasValue) presenters[index].React(reaction.Value);
        }

        void OnState(int index, PlayerState state)
        {
            if (demoMode) return;
            presenters[index].SetRunning(state == PlayerState.Run);
            presenters[index].SetDizzy(state.IsGrounded());
        }

        bool IsBound(MatchStandings.Standing standing) =>
            !demoMode && standing != null && standing.Index < 2 && bound[standing.Index] == standing;

        void OnScored(MatchStandings.Standing standing, int gained)
        {
            if (IsBound(standing)) presenters[standing.Index].SetScore(standing.Banked, gained);
        }

        void OnPendingChanged(MatchStandings.Standing standing, int before, int now)
        {
            if (IsBound(standing)) presenters[standing.Index].SetPending(now, true);
        }

        void OnStreakChanged(MatchStandings.Standing standing)
        {
            if (IsBound(standing)) presenters[standing.Index].SetStreak(standing.Streak, standings.IsOnFire(standing));
        }

        void RefreshLeader()
        {
            if (demoMode || !standings) return;
            for (int i = 0; i < 2; i++)
                presenters[i].SetTop(bound[i] != null && standings.Leader == bound[i]);
        }

        // Principal = el equipado; reserva = el otro (igual que ItemUIController)
        void SyncItems(int index, bool animate)
        {
            var inventory = bound[index]?.Inventory;
            if (!inventory || !inventory.IsSpawned)
            {
                presenters[index].SetItems(null, null, null, animate);
                return;
            }
            ItemData main, reserve = null;
            if (inventory.HasEquipped)
            {
                main = inventory.EquippedItem;
                for (int i = 0; i < inventory.Count; i++)
                    if (i != inventory.EquippedSlot)
                    {
                        reserve = inventory.GetItemAt(i);
                        break;
                    }
            }
            else
            {
                main = inventory.GetItemAt(0);
                reserve = inventory.GetItemAt(1);
            }
            presenters[index].SetItems(main, reserve, Pool(inventory.Database), animate);
        }

        // Iconos de la ruleta: todos los ítems de la base de datos que tienen icono
        IReadOnlyList<ItemData> Pool(ItemDatabase database)
        {
            if (database != poolSource)
            {
                poolSource = database;
                itemPool.Clear();
                if (database)
                    for (int id = 0; id < database.Count; id++)
                    {
                        var item = database.Get(id);
                        if (item && item.icon) itemPool.Add(item);
                    }
            }
            return itemPool;
        }

        // El personaje se reconoce por el prefab con el que se spawneó (roster del PlayerSpawnManager o lista de aquí)
        PlayerCharacterDefinition FindCharacter(NetworkObject player, out CharacterLabel label)
        {
            label = null;
            PlayerCharacterDefinition definition = null;
            var manager = NetworkManager.Singleton;
            var spawner = manager ? manager.GetComponent<PlayerSpawnManager>() : null;
            if (spawner && spawner.Roster)
                foreach (var character in spawner.Roster.Characters)
                    if (Matches(character, player))
                    {
                        definition = character;
                        break;
                    }
            foreach (var entry in characters)
                if (entry != null && (definition ? entry.character == definition : Matches(entry.character, player)))
                {
                    label = entry;
                    return entry.character;
                }
            return definition;
        }

        static bool Matches(PlayerCharacterDefinition character, NetworkObject player) =>
            character && character.prefab && player &&
            (character.prefab.PrefabIdHash == player.PrefabIdHash || player.name.StartsWith(character.prefab.name));
    }
}
