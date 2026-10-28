using System.Collections.Generic;
using Racoon.Items;
using UnityEngine;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Demo sin red para revisar el HUD en Play (el montaje la pone en SCN_HUD_Variantes). Hace lo mismo que el mockup
    /// HTML sobre el HUD activo: bolsa, ventas con rodillo y «+N», rachas con llama, TOP, estamina, ítems y tiempo.
    /// Si aparecen jugadores en red, se aparta. Clic derecho en el componente: «Últimos 30 s» y «Reiniciar».
    /// </summary>
    public class HUDDemo : MonoBehaviour
    {
        [Tooltip("Ítems (con icono) para la ruleta y las cajas.")]
        [SerializeField] ItemDatabase items;
        [SerializeField, Min(10f)] float duration = 300f;
        [Tooltip("Llamas: estas ventas seguidas deben sumar «Fire Points» (como MatchStandings).")]
        [SerializeField, Min(1)] int fireSales = 2;
        [SerializeField, Min(0)] int firePoints = 200;
        [Tooltip("Probabilidad, en cada objeto recogido, de que alguien pierda media bolsa (apaga su llama).")]
        [SerializeField, Range(0f, 1f)] float bagLossChance = .04f;

        class Side
        {
            public int banked, pending, streak;
            public readonly List<int> sales = new();
            public bool OnFire;
            public float stamina = 1f;
            public bool draining;
            public ItemData main, reserve;
        }

        readonly Side[] sides = { new(), new() };
        readonly List<ItemData> pool = new();
        HUDController hud;
        float remaining, nextLoot, nextSale, nextStamina, nextItems, nextReaction;
        readonly float[] dizzyUntil = new float[2], runUntil = new float[2];
        int lastScorer, itemTurn;

        void Start()
        {
            if (items)
                for (int id = 0; id < items.Count; id++)
                {
                    var item = items.Get(id);
                    if (item && item.icon) pool.Add(item);
                }
            Restart();
        }

        void OnDisable() => Release();

        void Update()
        {
            var active = FindActive();
            if (active != hud)
            {
                Release();
                hud = active;
                if (hud)
                {
                    hud.DemoMode = true;
                    ApplyAll();
                }
            }
            if (!hud) return;
            if (hud.Standings && hud.Standings.Players.Count > 0)
            {
                enabled = false; // hay partida de verdad: el HUD vuelve a leerla
                return;
            }

            float now = Time.unscaledTime;
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f) Restart();
            hud.ShowTime(remaining, duration, true, false);

            if (now >= nextLoot)
            {
                nextLoot = now + .45f;
                int n = Random.Range(0, 2);
                var side = sides[n];
                if (side.pending > 0 && Random.value < bagLossChance)
                {
                    // Pierde media bolsa: la llama se apaga y la racha vuelve a 0
                    side.pending /= 2;
                    side.sales.Clear();
                    side.streak = 0;
                    side.OnFire = false;
                    hud.Player(n + 1).SetStreak(0, false);
                }
                else side.pending += 10 * Random.Range(1, 5);
                hud.Player(n + 1).SetPending(side.pending, true);
            }
            if (now >= nextSale)
            {
                nextSale = now + 1.7f;
                Sale();
            }
            if (now >= nextStamina)
            {
                nextStamina = now + .05f;
                for (int n = 0; n < 2; n++)
                {
                    var s = sides[n];
                    if (Random.value < .015f) s.draining = !s.draining;
                    s.stamina = Mathf.Clamp01(s.stamina + (s.draining ? -.01f : .005f));
                    if (s.stamina <= 0f) s.draining = false;
                    hud.Player(n + 1).SetStamina(s.stamina);
                }
            }
            if (hud.View.reactions) Reactions(now);
            if (now >= nextItems && pool.Count > 0)
            {
                nextItems = now + 2.6f;
                ItemStep(itemTurn);
                itemTurn = 1 - itemTurn;
            }
        }

        // Vende más a menudo quien vendió el último, como en el mockup
        void Sale()
        {
            int n = Random.value < (lastScorer == 0 ? .65f : .35f) ? 0 : 1;
            lastScorer = n;
            var side = sides[n];
            int gained = side.pending > 0 ? side.pending : 10 * Random.Range(1, 6);
            side.pending = 0;
            side.banked += gained;
            side.streak++;
            side.sales.Add(gained);
            if (side.sales.Count > fireSales) side.sales.RemoveAt(0);
            int total = 0;
            foreach (int sale in side.sales) total += sale;
            side.OnFire = side.sales.Count >= fireSales && total >= firePoints;
            hud.Player(n + 1).SetScore(side.banked, gained);
            hud.Player(n + 1).SetPending(0, true);
            hud.Player(n + 1).SetStreak(side.streak, side.OnFire);
            for (int k = 0; k < 2; k++) hud.Player(k + 1).SetTop(sides[k].banked > sides[1 - k].banked);
        }

        // Variantes reactivas: golpes, plátanos, volteretas, puñetazos, carreras y mareos al azar
        void Reactions(float now)
        {
            for (int n = 0; n < 2; n++)
            {
                hud.Player(n + 1).SetDizzy(now < dizzyUntil[n]);
                hud.Player(n + 1).SetRunning(now < runUntil[n] && now >= dizzyUntil[n]);
            }
            if (now < nextReaction) return;
            nextReaction = now + Random.Range(1.2f, 2.4f);
            int side = Random.Range(0, 2);
            var reaction = (HUDReaction)Random.Range(0, 5);
            hud.Player(side + 1).React(reaction);
            if (reaction == HUDReaction.Slip || reaction == HUDReaction.HeavyHit) dizzyUntil[side] = now + 1.8f;
            else if (Random.value < .5f) runUntil[side] = now + Random.Range(1.5f, 3f);
        }

        void ItemStep(int n)
        {
            var side = sides[n];
            float r = Random.value;
            if (side.main && side.reserve)
            {
                if (r < .4f) (side.main, side.reserve) = (side.reserve, side.main);
                else
                {
                    side.main = side.reserve;
                    side.reserve = null;
                }
            }
            else if (side.main && r < .35f) side.main = null;
            else
            {
                var item = pool[Random.Range(0, pool.Count)];
                if (!side.main) side.main = item;
                else side.reserve = item;
            }
            hud.Player(n + 1).SetItems(side.main, side.reserve, pool);
        }

        void ApplyAll()
        {
            for (int n = 0; n < 2; n++)
            {
                var side = sides[n];
                var player = hud.Player(n + 1);
                player.SetScore(side.banked, 0);
                player.SetPending(side.pending, false);
                player.SetStreak(side.streak, side.OnFire, false);
                player.SetTop(side.banked > sides[1 - n].banked, false);
                player.SetStamina(side.stamina);
                player.SetItems(side.main, side.reserve, pool, false);
            }
        }

        void Release()
        {
            if (hud) hud.DemoMode = false;
            hud = null;
        }

        static HUDController FindActive()
        {
            foreach (var controller in FindObjectsByType<HUDController>(FindObjectsSortMode.None))
                if (controller.isActiveAndEnabled) return controller;
            return null;
        }

        [ContextMenu("Últimos 30 s")]
        void LastSeconds() => remaining = 30f;

        [ContextMenu("Reiniciar")]
        void Restart()
        {
            remaining = duration;
            for (int n = 0; n < 2; n++) sides[n] = new Side();
            if (hud) ApplyAll();
        }
    }
}
