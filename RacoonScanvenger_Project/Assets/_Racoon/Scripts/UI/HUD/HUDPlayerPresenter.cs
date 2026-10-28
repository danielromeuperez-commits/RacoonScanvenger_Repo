using System.Collections;
using System.Collections.Generic;
using Racoon.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.HUD
{
    /// <summary>Cosas que le pasan al jugador y a las que reacciona el retrato (variantes con <see cref="HUDView.reactions"/>).</summary>
    public enum HUDReaction { Hit, HeavyHit, Slip, Roll, Punch }

    /// <summary>
    /// Anima la parte de un jugador del HUD (P1 o P2) con las animaciones del mockup. No sabe nada de red:
    /// <see cref="HUDController"/> (o <see cref="HUDDemo"/>) le pasa los datos con los métodos Set*.
    /// Tiempos, curvas y distancias: los de Export/LEEME_Unity.md (px a 2560×1440).
    /// </summary>
    public class HUDPlayerPresenter
    {
        const float ReelTime = .75f, HopTime = .45f, BobPeriod = 1.6f, PopTime = 1f;
        const float FadeTime = .25f, BadgeTime = .35f, TopHideTime = .2f, FireFrameTime = .12f, LowBlinkTime = .175f;
        const float FloatPeriod = 2.2f, EntryTime = .5f, RouletteTime = 1.3f, RouletteStep = .07f, UseTime = .32f, FlyTime = .3f;
        const float BagFadeTime = .2f, BagBumpTime = .3f, BankTime = .35f, BagShakeTime = .35f;
        // Caja de reserva: flota a su aire (otro periodo y desfase) y algo menos que la principal
        const float ReserveFloatPeriod = 2.6f, ReserveFloatDelay = -1.1f, ReserveFloatAmount = .7f;
        // Aro de estamina: estela, recarga completa, agotado y aviso de estamina baja
        const float TrailDelay = .35f, TrailSpeed = 9f, RefillTime = .45f, EmptyShakeTime = .45f, LowPulsePeriod = .5f;
        // Panel de puntos según la variante: vaivén del cartel, tirón del tique, aplastado de la pegatina
        const float SwayPeriod = 3.2f, SwingTime = 1.6f, PrintTime = .5f, SquashTime = .45f;
        // Corona del que va ganando: caída, aterrizaje (aplastón, botecito y vaivén hasta quedar colgando) y salida despedida
        const float CrownDrop = 380f, CrownFallTime = .3f, CrownLandTime = .9f, CrownLoseTime = .55f, CrownSwayPeriod = 2.6f;
        // Bolsa: se mece sin parar para que se vea de reojo
        const float BagWigglePeriod = 1.1f, BagWiggleDegrees = 3f;
        // Reacciones: golpe, golpe fuerte, resbalón (plátano), voltereta y puñetazo; correr y mareo se funden
        const float HitTime = .4f, HeavyTime = .75f, SlipTime = .9f, RollTime = .5f, PunchTime = .25f, RunBlendSpeed = 8f;

        static float Now => Time.unscaledTime;

        public float LowStamina = .25f;
        public float StaminaSmoothing = 12f;
        public bool ShowBag = true;

        readonly MonoBehaviour host;
        readonly HUDView view;
        readonly HUDView.PlayerView pv;
        readonly int number;
        readonly float floatSign; // el giro de la caja principal va al revés en cada lado
        readonly bool hasNickname;

        // Rodillos
        readonly Vector2[] reelRest = new Vector2[4];
        readonly float[] reelPos = new float[4], reelFrom = new float[4], reelTo = new float[4], reelStart = { -99f, -99f, -99f, -99f };
        readonly int[] reelDigit = new int[4];
        float hopStart = -99f;

        // Panel de puntos (animación propia de la variante)
        Vector2 panelRestPos;
        Quaternion panelRestRot = Quaternion.identity;
        float panelHit = -99f;

        // «+N»
        readonly List<(RectTransform rect, CanvasGroup group, float start, Vector2 rest)> pops = new();
        Vector2 popRest;

        // Bolsa
        CanvasGroup bagGroup;
        Vector3 bagRest;
        int pending;
        bool bagVisible, banking;
        float bagChange = -99f, bagBump = -99f, bankStart = -99f, bagShake = -99f;

        // Estamina
        float staminaTarget = 1f, staminaShown = 1f, trailValue = 1f, trailHold;
        float refillStart = -99f, emptyStart = -99f;
        RectTransform portrait;
        Vector2 portraitRest;

        // Racha, llama y TOP
        CanvasGroup fireGroup, streakGroup;
        bool fireOn, topOn;
        int streakShown = -1;
        float fireChange = -99f, counterBump = -99f, topChange = -99f;
        Vector2 crownRestPos;
        Quaternion crownRestRot = Quaternion.identity;
        CanvasGroup crownGroup;

        // Reacciones
        HUDReaction reaction;
        float reactionStart = -99f, runBlend, dizzyBlend, reactDegrees;
        bool running, dizzy;
        Vector2 reactOffset, rootRest;
        Vector2 reactScale = Vector2.one;

        // Ítems
        Vector2 mainRestPos, reserveRestPos;
        Quaternion mainRestRot = Quaternion.identity, reserveRestRot = Quaternion.identity;
        float mainEntry = -99f, reserveEntry = -99f;
        ItemData shownMain, shownReserve, wantMain, wantReserve;
        IReadOnlyList<ItemData> pool;
        bool itemsBusy, itemsDirty;
        Coroutine itemsRoutine;
        readonly List<GameObject> flying = new();

        public int Number => number;

        public HUDPlayerPresenter(MonoBehaviour host, HUDView view, int number)
        {
            this.host = host;
            this.view = view;
            this.number = number;
            pv = view.Player(number);
            floatSign = number == 1 ? -1f : 1f;
            hasNickname = pv.tag && !string.IsNullOrEmpty(pv.tag.Text);

            for (int i = 0; i < 4; i++)
                if (Reel(i)) reelRest[i] = Reel(i).anchoredPosition;
            if (pv.mainSlot)
            {
                mainRestPos = pv.mainSlot.anchoredPosition;
                mainRestRot = pv.mainSlot.localRotation;
            }
            if (pv.reserveSlot)
            {
                reserveRestPos = pv.reserveSlot.anchoredPosition;
                reserveRestRot = pv.reserveSlot.localRotation;
            }
            if (pv.popTemplate)
            {
                popRest = pv.popTemplate.anchoredPosition;
                // El grupo de ejemplos se queda activo como capa de los «+N»; la plantilla, oculta
                var layer = pv.popTemplate.parent;
                if (layer && layer != pv.root) layer.gameObject.SetActive(true);
                pv.popTemplate.gameObject.SetActive(false);
            }
            if (pv.bag)
            {
                bagGroup = Group(pv.bag.gameObject);
                bagRest = pv.bag.transform.localPosition;
                pv.bag.gameObject.SetActive(false);
            }
            if (pv.fire)
            {
                fireGroup = Group(pv.fire);
                pv.fire.SetActive(false);
            }
            if (pv.streak)
            {
                streakGroup = Group(pv.streak);
                pv.streak.SetActive(false);
            }
            if (pv.top) pv.top.SetActive(false);
            if (pv.crown)
            {
                crownRestPos = pv.crown.anchoredPosition;
                crownRestRot = pv.crown.localRotation;
                crownGroup = Group(pv.crown.gameObject);
            }
            if (pv.staminaGlow) pv.staminaGlow.gameObject.SetActive(false);
            if (pv.scorePanel)
            {
                // El cartel se balancea colgado de lo alto de sus cadenas: pivote arriba sin moverlo de sitio
                if (view.scorePanelFx == HUDView.PanelFx.Swing) SetPivotInPlace(pv.scorePanel, new Vector2(.5f, 1f));
                panelRestPos = pv.scorePanel.anchoredPosition;
                panelRestRot = pv.scorePanel.localRotation;
            }
            if (pv.staminaFill)
            {
                portrait = pv.staminaFill.rectTransform.parent as RectTransform;
                if (portrait) portraitRest = portrait.anchoredPosition;
            }
            if (pv.root) rootRest = pv.root.anchoredPosition;
            if (pv.speedLines.Length > 0 && pv.speedLines[0]) pv.speedLines[0].parent.gameObject.SetActive(false);
            if (pv.dizzyStars.Length > 0 && pv.dizzyStars[0]) pv.dizzyStars[0].parent.gameObject.SetActive(false);
        }

        RectTransform Reel(int i) => pv.reels != null && i < pv.reels.Length ? pv.reels[i] : null;
        RectTransform Strip(int i) => pv.reelStrips != null && i < pv.reelStrips.Length ? pv.reelStrips[i] : null;
        Color PlayerColor => view.colors ? view.colors.Get(number) : Color.white;

        static CanvasGroup Group(GameObject go) =>
            go.TryGetComponent(out CanvasGroup group) ? group : go.AddComponent<CanvasGroup>();

        static void SetPivotInPlace(RectTransform rect, Vector2 pivot)
        {
            var size = rect.rect.size;
            var delta = pivot - rect.pivot;
            rect.pivot = pivot;
            rect.anchoredPosition += new Vector2(delta.x * size.x, delta.y * size.y);
        }

        // ---------------- Datos ----------------

        /// <summary>Vuelve al estado inicial (sin jugador): 0 puntos, sin bolsa, racha, TOP ni ítems y estamina llena.</summary>
        public void ResetView()
        {
            SetScore(0, 0);
            SetPending(0, false);
            SetStreak(0, false, false);
            SetTop(false, false);
            staminaTarget = staminaShown = trailValue = 1f;
            refillStart = emptyStart = -99f;
            SetItems(null, null, null, false);
            running = dizzy = false;
            reactionStart = -99f;
            foreach (var pop in pops)
                if (pop.rect) Object.Destroy(pop.rect.gameObject);
            pops.Clear();
        }

        public void SetColor(Color color)
        {
            if (view.colors) view.colors.SetPlayerColor(number, color);
        }

        /// <summary>Apodo en la etiqueta (solo si la variante lo muestra; la chapa «Pn» se mantiene).</summary>
        public void SetNickname(string nickname)
        {
            if (hasNickname && !string.IsNullOrEmpty(nickname) && pv.tag.Text != nickname) pv.tag.SetText(nickname);
        }

        public void SetIllustration(Sprite sprite)
        {
            if (!pv.illustration) return;
            pv.illustration.sprite = sprite;
            pv.illustration.enabled = sprite != null;
        }

        /// <summary>
        /// Puntos asegurados. Con <paramref name="gained"/> &gt; 0 los rodillos giran (siempre hacia delante),
        /// saltan, sale el «+N» y la bolsa vuela al marcador. Con 0, se colocan sin animar.
        /// </summary>
        public void SetScore(int total, int gained)
        {
            string digits = Mathf.Clamp(total, 0, 9999).ToString("0000");
            for (int i = 0; i < 4; i++)
            {
                int digit = digits[i] - '0';
                if (gained <= 0)
                {
                    reelDigit[i] = digit;
                    reelPos[i] = digit;
                    reelStart[i] = -99f;
                    PlaceStrip(i, digit);
                    continue;
                }
                if (digit == reelDigit[i]) continue;
                // Si el dígito nuevo es menor, se anima hasta d + 10 y al acabar salta a d (la tira repite 0-9)
                float from = Mathf.Repeat(reelPos[i], 10f);
                float to = digit;
                while (to <= from + .001f) to += 10f;
                reelFrom[i] = from;
                reelTo[i] = to;
                reelStart[i] = Now;
                reelDigit[i] = digit;
            }
            if (gained <= 0) return;
            hopStart = Now;
            panelHit = Now;
            SpawnPop(gained);
            Bank();
        }

        /// <summary>Puntos de la bolsa (sin vender). Se ve «BOLSA +N» junto a los ítems mientras haya algo.</summary>
        public void SetPending(int value, bool animate)
        {
            int before = pending;
            pending = value;
            if (!pv.bag) return;
            if (!ShowBag)
            {
                pv.bag.gameObject.SetActive(false);
                bagVisible = banking = false;
                return;
            }
            if (value > 0)
            {
                if (banking) StopBanking();
                // Con chapa «BOLSA» (P5 pastel) el número va solo y grande
                pv.bag.SetText(string.IsNullOrEmpty(pv.bag.chapa) ? "BOLSA +" + value : "+" + value);
                if (!bagVisible)
                {
                    bagVisible = true;
                    bagChange = animate ? Now : -99f;
                    pv.bag.gameObject.SetActive(true);
                }
                else if (animate && value > before) bagBump = Now;
                else if (animate && value < before) bagShake = Now; // ha perdido puntos de la bolsa
            }
            else if (bagVisible)
            {
                bagVisible = false;
                bagChange = animate ? Now : -99f;
                if (!animate) pv.bag.gameObject.SetActive(false);
            }
        }

        public void SetStamina(float value)
        {
            value = Mathf.Clamp01(value);
            if (value >= .999f && staminaTarget < .98f) refillStart = Now; // recarga completa: destello y rebote
            if (value <= .001f && staminaTarget > .01f) emptyStart = Now;  // agotado: el retrato tiembla
            staminaTarget = value;
        }

        /// <summary>Racha: con <paramref name="onFire"/> sale la llama, el contador «xN» y el brillo del aro.</summary>
        public void SetStreak(int streak, bool onFire, bool animate = true)
        {
            if (onFire && pv.streakCounter && streak != streakShown)
            {
                pv.streakCounter.SetText("x" + streak);
                if (fireOn && animate) counterBump = Now;
            }
            streakShown = onFire ? streak : -1;

            if (onFire == fireOn) return;
            fireOn = onFire;
            fireChange = animate ? Now : -99f;
            if (onFire || !animate)
            {
                if (pv.fire) pv.fire.SetActive(onFire);
                if (pv.streak) pv.streak.SetActive(onFire);
            }
        }

        public void SetTop(bool on, bool animate = true)
        {
            if (on == topOn) return;
            topOn = on;
            topChange = animate ? Now : -99f;
            if (pv.top && (on || !animate)) pv.top.SetActive(on);
        }

        /// <summary>
        /// Ítem principal (equipado) y de reserva. Con <paramref name="animate"/>, la transición se deduce del cambio:
        /// recoger (ruleta y entrada), usar (sale volando y la reserva ocupa su puesto) o intercambiar.
        /// <paramref name="roulettePool"/>: iconos que pasan por la ruleta.
        /// </summary>
        public void SetItems(ItemData main, ItemData reserve, IReadOnlyList<ItemData> roulettePool, bool animate = true)
        {
            wantMain = main;
            wantReserve = reserve;
            if (roulettePool != null) pool = roulettePool;
            if (animate)
            {
                itemsDirty = true;
                return;
            }
            StopItems();
            ShowSlot(true, main);
            ShowSlot(false, reserve);
            shownMain = main;
            shownReserve = reserve;
        }

        /// <summary>Algo le ha pasado al jugador: el retrato lo cuenta (solo en las variantes con reacciones).</summary>
        public void React(HUDReaction kind)
        {
            if (!view.reactions) return;
            reaction = kind;
            reactionStart = Now;
            string word = kind switch
            {
                HUDReaction.Hit => "¡PAM!",
                HUDReaction.HeavyHit => "¡ZASCA!",
                HUDReaction.Slip => "¡PLAF!",
                _ => null,
            };
            if (word != null) SpawnWord(word);
        }

        /// <summary>Corriendo: el retrato se inclina hacia delante, trota y salen rayas de velocidad.</summary>
        public void SetRunning(bool on) => running = on && view.reactions;

        /// <summary>Aturdido en el suelo: estrellas girando sobre el retrato, que se tambalea.</summary>
        public void SetDizzy(bool on) => dizzy = on && view.reactions;

        // ---------------- Fotograma a fotograma ----------------

        public void Tick(float deltaTime)
        {
            TickReels();
            TickPanel();
            TickPops();
            TickBag();
            TickReactions(deltaTime);
            TickStamina(deltaTime);
            TickFire();
            TickTop();
            TickSlot(pv.mainSlot, mainRestPos, mainRestRot, mainEntry, FloatPeriod, 0f, 1f, floatSign);
            TickSlot(pv.reserveSlot, reserveRestPos, reserveRestRot, reserveEntry, ReserveFloatPeriod, ReserveFloatDelay, ReserveFloatAmount, -floatSign);
        }

        /// <summary>Después de Tick: arranca la siguiente transición de ítems si hay una pendiente.</summary>
        public void LateTick()
        {
            if (!itemsDirty || itemsBusy) return;
            itemsDirty = false;
            if (wantMain == shownMain && wantReserve == shownReserve) return;
            itemsBusy = true;
            itemsRoutine = host.StartCoroutine(ItemTransition(shownMain, shownReserve, wantMain, wantReserve));
        }

        void TickReels()
        {
            for (int i = 0; i < 4; i++)
            {
                if (reelStart[i] < 0f) continue;
                float t = (Now - reelStart[i]) / ReelTime;
                if (t >= 1f)
                {
                    reelPos[i] = reelTo[i] >= 10f ? reelTo[i] - 10f : reelTo[i];
                    reelStart[i] = -99f;
                }
                else reelPos[i] = Mathf.LerpUnclamped(reelFrom[i], reelTo[i], HUDAnim.Reel.Evaluate(t));
                PlaceStrip(i, reelPos[i]);
            }

            // Al puntuar los cuatro saltan (28 px y ×1,12 al 35 %); el resto del tiempo se balancean
            float hop = (Now - hopStart) / HopTime;
            for (int i = 0; i < 4; i++)
            {
                var reel = Reel(i);
                if (!reel) continue;
                float y, scale = 1f;
                if (hop < 1f)
                {
                    y = HUDAnim.Peak(hop, .35f, 28f, HUDAnim.Bounce);
                    scale += HUDAnim.Peak(hop, .35f, .12f, HUDAnim.Bounce);
                }
                else
                {
                    // Desfases: rodillos 1 y 3, 0 s; el 2, −0,5 s; el último, −1 s
                    float delay = i == 3 ? -1f : i % 2 == 1 ? -.5f : 0f;
                    y = HUDAnim.ReelBob(HUDAnim.Phase(Now, BobPeriod, delay));
                }
                reel.anchoredPosition = reelRest[i] + new Vector2(0f, y);
                reel.localScale = new Vector3(scale, scale, 1f);
            }
        }

        void TickPanel()
        {
            var panel = pv.scorePanel;
            if (!panel || view.scorePanelFx == HUDView.PanelFx.None) return;
            float t = Now - panelHit;
            switch (view.scorePanelFx)
            {
                case HUDView.PanelFx.Swing:
                    // Vaivén suave de ±1° y, al puntuar, un columpio que se amortigua (sentido horario primero, como el HTML)
                    float degrees = Mathf.Sin(Now * 2f * Mathf.PI / SwayPeriod);
                    if (t < SwingTime) degrees += 6f * Mathf.Exp(-3.2f * t) * Mathf.Sin(t * 11f);
                    panel.localRotation = panelRestRot * Quaternion.Euler(0f, 0f, -degrees);
                    break;
                case HUDView.PanelFx.Print:
                    // Tirón hacia arriba que vuelve con un traqueteo, como el papel al salir de la impresora
                    var offset = Vector2.zero;
                    if (t < PrintTime)
                    {
                        float k = t / PrintTime, rest = 1f - k;
                        offset = new Vector2(2.5f * Mathf.Sin(k * 30f) * rest, 20f * rest * rest);
                    }
                    panel.anchoredPosition = panelRestPos + offset;
                    break;
                case HUDView.PanelFx.Squash:
                    // Se aplasta (×1,08 a lo ancho) y rebota (×1,04 a lo alto) antes de quedarse quieta
                    float sx = 1f, sy = 1f;
                    if (t < SquashTime)
                    {
                        float k = t / SquashTime;
                        if (k < .3f) { float e = HUDAnim.EaseOut.Evaluate(k / .3f); sx = 1f + .08f * e; sy = 1f - .08f * e; }
                        else if (k < .6f) { float e = HUDAnim.EaseInOut.Evaluate((k - .3f) / .3f); sx = Mathf.Lerp(1.08f, .97f, e); sy = Mathf.Lerp(.92f, 1.04f, e); }
                        else { float e = HUDAnim.EaseOut.Evaluate((k - .6f) / .4f); sx = Mathf.Lerp(.97f, 1f, e); sy = Mathf.Lerp(1.04f, 1f, e); }
                    }
                    panel.localScale = new Vector3(sx, sy, 1f);
                    break;
            }
        }

        void PlaceStrip(int i, float position)
        {
            var strip = Strip(i);
            if (strip) strip.anchoredPosition = new Vector2(strip.anchoredPosition.x, pv.reelOffset + position * pv.reelPitch);
        }

        void SpawnPop(int amount)
        {
            if (!pv.popTemplate) return;
            var clone = Object.Instantiate(pv.popTemplate, pv.popTemplate.parent);
            clone.name = "+" + amount;
            clone.gameObject.SetActive(true);
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true)) text.text = "+" + amount;
            var group = Group(clone.gameObject);
            group.alpha = 0f;
            pops.Add((clone, group, Now, popRest));
        }

        // Onomatopeya («¡PAM!») con la plantilla del «+N», saliendo del retrato hacia el centro de la pantalla
        void SpawnWord(string word)
        {
            if (!pv.popTemplate || !portrait) return;
            var clone = Object.Instantiate(pv.popTemplate, pv.popTemplate.parent);
            clone.name = word;
            clone.gameObject.SetActive(true);
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true)) text.text = word;
            clone.position = portrait.TransformPoint(portrait.rect.center);
            float lean = number == 1 ? 1f : -1f;
            var rest = clone.anchoredPosition + new Vector2(lean * 90f, 70f);
            clone.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-10f, 10f));
            var group = Group(clone.gameObject);
            group.alpha = 0f;
            pops.Add((clone, group, Now, rest));
        }

        void TickPops()
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var (rect, group, start, rest) = pops[i];
                float t = (Now - start) / PopTime;
                if (!rect || t >= 1f)
                {
                    if (rect) Object.Destroy(rect.gameObject);
                    pops.RemoveAt(i);
                    continue;
                }
                // Empieza 20 px abajo (×0,6, invisible); al 25 %, ×1,15 y opaco; acaba 68 px arriba, invisible
                float y, scale, alpha;
                if (t < .25f)
                {
                    float e = HUDAnim.EaseOut.Evaluate(t / .25f);
                    y = Mathf.Lerp(-20f, 0f, e);
                    scale = Mathf.Lerp(.6f, 1.15f, e);
                    alpha = e;
                }
                else
                {
                    float e = HUDAnim.EaseOut.Evaluate((t - .25f) / .75f);
                    y = Mathf.Lerp(0f, 68f, e);
                    scale = Mathf.Lerp(1.15f, 1f, e);
                    alpha = 1f - e;
                }
                rect.anchoredPosition = rest + new Vector2(0f, y);
                rect.localScale = new Vector3(scale, scale, 1f);
                group.alpha = alpha;
            }
        }

        void Bank()
        {
            if (!pv.bag || !bagVisible) return;
            bagVisible = false;
            banking = true;
            bankStart = Now;
        }

        void StopBanking()
        {
            banking = false;
            pv.bag.transform.localPosition = bagRest;
            pv.bag.transform.localScale = Vector3.one;
        }

        void TickBag()
        {
            if (!pv.bag || !pv.bag.gameObject.activeSelf) return;
            var rect = pv.bag.transform;
            if (banking)
            {
                // Al vender, la bolsa vuela al marcador y se desvanece
                float t = (Now - bankStart) / BankTime;
                if (t >= 1f)
                {
                    StopBanking();
                    pv.bag.gameObject.SetActive(false);
                    return;
                }
                float e = HUDAnim.EaseIn.Evaluate(t);
                var target = pv.scorePanel ? rect.parent.InverseTransformPoint(pv.scorePanel.TransformPoint(pv.scorePanel.rect.center)) : bagRest;
                rect.localPosition = Vector3.LerpUnclamped(bagRest, target, e);
                rect.localScale = Vector3.one * Mathf.Lerp(1f, .4f, e);
                bagGroup.alpha = 1f - e;
                return;
            }
            if (bagVisible)
            {
                bagGroup.alpha = Mathf.Clamp01((Now - bagChange) / BagFadeTime);
                float bump = (Now - bagBump) / BagBumpTime;
                float scale = bump < 1f ? 1f + HUDAnim.Peak(bump, .4f, .25f, HUDAnim.Ease) : 1f;
                rect.localScale = new Vector3(scale, scale, 1f);
                float shake = (Now - bagShake) / BagShakeTime;
                rect.localPosition = bagRest + (shake < 1f ? new Vector3(7f * Mathf.Sin(shake * 45f) * (1f - shake), 0f, 0f) : Vector3.zero);
                rect.localRotation = Quaternion.Euler(0f, 0f, BagWiggleDegrees * Mathf.Sin(Now * 2f * Mathf.PI / BagWigglePeriod));
            }
            else
            {
                float t = (Now - bagChange) / BagFadeTime;
                if (t >= 1f) pv.bag.gameObject.SetActive(false);
                else bagGroup.alpha = 1f - t;
            }
        }

        void TickStamina(float deltaTime)
        {
            staminaShown = Mathf.Lerp(staminaShown, staminaTarget, 1f - Mathf.Exp(-StaminaSmoothing * deltaTime));
            var color = PlayerColor;

            // Estela blanca: aguanta un momento lo que acabas de gastar y luego lo alcanza
            if (staminaShown >= trailValue)
            {
                trailValue = staminaShown;
                trailHold = Now + TrailDelay;
            }
            else if (Now >= trailHold) trailValue = Mathf.Lerp(trailValue, staminaShown, 1f - Mathf.Exp(-TrailSpeed * deltaTime));
            if (pv.staminaTrail)
            {
                bool visible = trailValue - staminaShown > .002f;
                if (pv.staminaTrail.enabled != visible) pv.staminaTrail.enabled = visible;
                pv.staminaTrail.fillAmount = trailValue;
            }

            float refill = (Now - refillStart) / RefillTime;
            if (pv.staminaFill)
            {
                pv.staminaFill.fillAmount = staminaShown;
                // Estamina baja: parpadeo seco entre el color del jugador y blanco. Recarga completa: destello blanco
                bool blink = staminaTarget < LowStamina && Mathf.FloorToInt(Now / LowBlinkTime) % 2 == 1;
                pv.staminaFill.color = blink ? Color.white : refill < 1f ? Color.Lerp(Color.white, color, refill) : color;
            }

            if (pv.staminaGlow)
            {
                // Durante la racha el aro brilla con el color del jugador
                if (pv.staminaGlow.gameObject.activeSelf != fireOn) pv.staminaGlow.gameObject.SetActive(fireOn);
                if (fireOn)
                {
                    pv.staminaGlow.fillAmount = staminaShown;
                    var glow = color;
                    glow.a = .45f + .15f * Mathf.Sin(Now * 6f);
                    pv.staminaGlow.color = glow;
                }
            }

            // Retrato entero: rebote al recargar del todo, latido con estamina baja y temblor al agotarse
            if (portrait)
            {
                float scale = 1f;
                if (refill < 1f) scale += HUDAnim.Peak(refill, .35f, .08f, HUDAnim.Bounce);
                else if (staminaTarget < LowStamina) scale += .03f * (.5f - .5f * Mathf.Cos(Now * 2f * Mathf.PI / LowPulsePeriod));
                portrait.localScale = new Vector3(scale * reactScale.x, scale * reactScale.y, 1f);
                float shake = (Now - emptyStart) / EmptyShakeTime;
                portrait.anchoredPosition = portraitRest + reactOffset + (shake < 1f ? new Vector2(9f * Mathf.Sin(shake * 50f) * (1f - shake), 0f) : Vector2.zero);
                if (view.reactions) portrait.localRotation = Quaternion.Euler(0f, 0f, reactDegrees);
            }
        }

        // Reacciones del retrato (las aplica TickStamina junto a sus propias animaciones) y del bloque entero del jugador
        void TickReactions(float deltaTime)
        {
            reactOffset = Vector2.zero;
            reactScale = Vector2.one;
            reactDegrees = 0f;
            if (!view.reactions) return;
            float lean = number == 1 ? 1f : -1f; // hacia el centro de la pantalla
            runBlend = Mathf.MoveTowards(runBlend, running ? 1f : 0f, deltaTime * RunBlendSpeed);
            dizzyBlend = Mathf.MoveTowards(dizzyBlend, dizzy ? 1f : 0f, deltaTime * 4f);
            var rootOffset = Vector2.zero;
            float t = Now - reactionStart, roll = 0f;
            switch (reaction)
            {
                case HUDReaction.Hit when t < HitTime:
                {
                    // Golpe: sacudida hacia fuera, cabeceo y aplastón
                    float k = t / HitTime, fade = 1f - k, p = HUDAnim.Peak(k, .2f, 1f, HUDAnim.EaseOut);
                    reactOffset.x = -lean * 16f * Mathf.Sin(t * 55f) * fade;
                    reactDegrees = 9f * Mathf.Sin(t * 40f) * fade;
                    reactScale = new Vector2(1f - .1f * p, 1f + .08f * p);
                    break;
                }
                case HUDReaction.HeavyHit when t < HeavyTime:
                {
                    // Golpe fuerte: tiembla todo el bloque del jugador y el retrato se tuerce y crece de golpe
                    float k = t / HeavyTime, fade = (1f - k) * (1f - k);
                    rootOffset = new Vector2(14f * Mathf.Sin(t * 48f), 8f * Mathf.Sin(t * 37f)) * fade;
                    reactDegrees = -lean * 30f * Mathf.Exp(-5f * t) * Mathf.Cos(t * 14f);
                    reactScale = Vector2.one * (1f + .15f * HUDAnim.Peak(k, .15f, 1f, HUDAnim.EaseOut));
                    break;
                }
                case HUDReaction.Slip when t < SlipTime:
                {
                    // Plátano: patas arriba, vuelta entera hacia fuera con un salto
                    float k = t / SlipTime;
                    reactDegrees = lean * 360f * HUDAnim.EaseOut.Evaluate(k);
                    reactOffset = new Vector2(-lean * 20f * Mathf.Sin(Mathf.PI * k), 34f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.6f)));
                    break;
                }
                case HUDReaction.Roll when t < RollTime:
                {
                    // Voltereta: rueda entera hacia el centro dando un saltito
                    float k = t / RollTime;
                    reactDegrees = -lean * 360f * HUDAnim.EaseInOut.Evaluate(k);
                    reactOffset = new Vector2(lean * 22f, 14f) * Mathf.Sin(Mathf.PI * k);
                    roll = 1f;
                    break;
                }
                case HUDReaction.Punch when t < PunchTime:
                {
                    // Puñetazo: el retrato da un empujón hacia el centro
                    float p = HUDAnim.Peak(t / PunchTime, .3f, 1f, HUDAnim.EaseOut);
                    reactOffset.x = lean * 18f * p;
                    reactScale = Vector2.one * (1f + .06f * p);
                    break;
                }
            }
            // Correr: inclinado hacia delante y trotando. Mareo: se tambalea
            reactDegrees += -lean * 7f * runBlend + dizzyBlend * 7f * Mathf.Sin(Now * 4.5f);
            reactOffset.y += runBlend * 5f * Mathf.Abs(Mathf.Sin(Now * 13f));
            if (pv.root) pv.root.anchoredPosition = rootRest + rootOffset;
            TickSpeedLines(lean, Mathf.Max(runBlend, roll));
            TickDizzy();
        }

        // Rayas detrás del retrato que salen hacia fuera y se desvanecen, en bucle
        void TickSpeedLines(float lean, float amount)
        {
            if (pv.speedLines.Length == 0 || !pv.speedLines[0]) return;
            var group = pv.speedLines[0].parent.gameObject;
            if (group.activeSelf != amount > .01f) group.SetActive(amount > .01f);
            if (amount <= .01f) return;
            for (int i = 0; i < pv.speedLines.Length; i++)
            {
                var line = pv.speedLines[i];
                float phase = Mathf.Repeat(Now * 2.4f + i * .37f, 1f);
                var position = line.anchoredPosition;
                position.x = -lean * (150f + 70f * phase);
                line.anchoredPosition = position;
                line.localScale = new Vector3(.6f + .7f * phase, 1f, 1f);
                if (line.TryGetComponent(out Image image))
                {
                    var color = image.color;
                    color.a = Mathf.Sin(Mathf.PI * phase) * amount;
                    image.color = color;
                }
            }
        }

        // Estrellas en órbita sobre el retrato: las de delante, más grandes
        void TickDizzy()
        {
            if (pv.dizzyStars.Length == 0 || !pv.dizzyStars[0]) return;
            var group = pv.dizzyStars[0].parent.gameObject;
            if (group.activeSelf != dizzyBlend > .01f) group.SetActive(dizzyBlend > .01f);
            if (dizzyBlend <= .01f) return;
            for (int i = 0; i < pv.dizzyStars.Length; i++)
            {
                var star = pv.dizzyStars[i];
                float angle = Now * 5f + i * 2f * Mathf.PI / pv.dizzyStars.Length;
                star.anchoredPosition = new Vector2(Mathf.Cos(angle) * 90f, Mathf.Sin(angle) * 22f);
                float scale = (.8f + .25f * Mathf.Sin(angle)) * dizzyBlend;
                star.localScale = new Vector3(scale, scale, 1f);
                star.localRotation = Quaternion.Euler(0f, 0f, Now * 220f);
            }
        }

        void TickFire()
        {
            if (!pv.fire || !pv.fire.activeSelf) return;
            float t = Now - fireChange;
            float alpha, scale;
            if (fireOn)
            {
                alpha = HUDAnim.Ease.Evaluate(Mathf.Clamp01(t / FadeTime));
                scale = Mathf.LerpUnclamped(.7f, 1f, HUDAnim.Bounce.Evaluate(Mathf.Clamp01(t / BadgeTime)));
            }
            else
            {
                alpha = 1f - HUDAnim.Ease.Evaluate(Mathf.Clamp01(t / FadeTime));
                scale = Mathf.Lerp(1f, .7f, HUDAnim.Ease.Evaluate(Mathf.Clamp01(t / BadgeTime)));
                if (t >= BadgeTime)
                {
                    pv.fire.SetActive(false);
                    if (pv.streak) pv.streak.SetActive(false);
                    return;
                }
            }
            fireGroup.alpha = alpha;
            pv.fire.transform.localScale = new Vector3(scale, scale, 1f);

            // Fotogramas 1 → 2 → 3, 0,12 s cada uno
            int frame = Mathf.FloorToInt(Now / FireFrameTime) % 3;
            for (int k = 0; k < pv.fireFrames.Length; k++)
                if (pv.fireFrames[k] && pv.fireFrames[k].activeSelf != (k == frame)) pv.fireFrames[k].SetActive(k == frame);

            if (streakGroup)
            {
                streakGroup.alpha = alpha;
                float bump = (Now - counterBump) / .3f;
                float s = bump < 1f ? 1f + HUDAnim.Peak(bump, .35f, .3f, HUDAnim.Ease) : 1f;
                pv.streak.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        void TickTop()
        {
            if (!pv.top || !pv.top.activeSelf) return;
            if (pv.crown)
            {
                TickCrown();
                return;
            }
            float t = Now - topChange;
            float scale;
            if (topOn) scale = HUDAnim.Bounce.Evaluate(Mathf.Clamp01(t / BadgeTime));
            else
            {
                scale = 1f - HUDAnim.EaseIn.Evaluate(Mathf.Clamp01(t / TopHideTime));
                if (t >= TopHideTime) pv.top.SetActive(false);
            }
            pv.top.transform.localScale = new Vector3(scale, scale, 1f);
        }

        void TickCrown()
        {
            var crown = pv.crown;
            float t = Now - topChange;
            float lean = number == 1 ? 1f : -1f; // hacia el centro de la pantalla: P1 a la derecha, P2 a la izquierda
            float x = 0f, y = 0f, degrees = 0f, sx = 1f, sy = 1f, alpha = 1f;
            if (!topOn)
            {
                // Sale despedida: saltito, cae hacia el centro girando y se desvanece
                float k = t / CrownLoseTime;
                if (k >= 1f)
                {
                    pv.top.SetActive(false);
                    k = 0f; // queda en reposo para la próxima vez
                }
                x = lean * 90f * k;
                y = 220f * k - 600f * k * k;
                degrees = -lean * 200f * k;
                alpha = 1f - HUDAnim.EaseIn.Evaluate(k);
            }
            else if (t < CrownFallTime)
            {
                // Cae desde fuera de la pantalla con gravedad, algo torcida hacia fuera, y llega derecha (reposo: 20° hacia dentro)
                float k = t / CrownFallTime;
                y = CrownDrop * (1f - HUDAnim.EaseIn.Evaluate(k));
                degrees = lean * (20f + 10f * (1f - k));
                alpha = Mathf.Clamp01(k * 4f);
                sy = 1f + .12f * k; // se estira un poco al caer
                sx = 1f - .06f * k;
            }
            else
            {
                float u = t - CrownFallTime;
                // Aterrizaje: aplastón al tocar el aro y otro más flojo al caer del botecito
                if (u < .14f) { float p = HUDAnim.Peak(u / .14f, .4f, 1f, HUDAnim.EaseOut); sx = 1f + .3f * p; sy = 1f - .32f * p; }
                if (u > .08f && u < .4f) y = 26f * Mathf.Sin(Mathf.PI * (u - .08f) / .32f);
                if (u > .4f && u < .52f) { float p = HUDAnim.Peak((u - .4f) / .12f, .4f, 1f, HUDAnim.EaseOut); sx = 1f + .1f * p; sy = 1f - .12f * p; }
                // Llega derecha y vence hacia su lado hasta quedarse colgando, con un vaivén que se amortigua
                degrees = lean * 20f * Mathf.Exp(-4.5f * u) * Mathf.Cos(u * 12f);
                // Colgando: se balancea suave sobre su base
                float sway = Mathf.Clamp01(u / CrownLandTime);
                degrees += sway * 3.5f * Mathf.Sin(2f * Mathf.PI * u / CrownSwayPeriod) * lean;
            }
            crown.anchoredPosition = crownRestPos + new Vector2(x, y);
            crown.localRotation = crownRestRot * Quaternion.Euler(0f, 0f, degrees);
            crown.localScale = new Vector3(sx, sy, 1f);
            crownGroup.alpha = alpha;
        }

        // Las cajas flotan (la principal, 2,2 s; la reserva a su aire) y hacen la entrada (×0,4 y 30° → reposo, 0,5 s)
        void TickSlot(RectTransform slot, Vector2 restPos, Quaternion restRot, float entryStart, float period, float delay, float amount, float sign)
        {
            if (!slot) return;
            HUDAnim.ItemFloat(HUDAnim.Phase(Now, period, delay), out float swing, out float rise);
            float degrees = view.calm ? 0f : swing * sign * amount, scale = 1f;
            rise *= amount;
            float t = (Now - entryStart) / EntryTime;
            if (t < 1f)
            {
                float e = HUDAnim.Entry.Evaluate(t);
                scale = Mathf.LerpUnclamped(.4f, 1f, e);
                degrees += Mathf.LerpUnclamped(30f, 0f, e);
            }
            slot.anchoredPosition = restPos + new Vector2(0f, rise);
            slot.localRotation = restRot * Quaternion.Euler(0f, 0f, degrees);
            slot.localScale = new Vector3(scale, scale, 1f);
        }

        // ---------------- Ítems ----------------

        static int Count(ItemData a, ItemData b) => (a ? 1 : 0) + (b ? 1 : 0);

        IEnumerator ItemTransition(ItemData oldMain, ItemData oldReserve, ItemData newMain, ItemData newReserve)
        {
            int oldCount = Count(oldMain, oldReserve), newCount = Count(newMain, newReserve);
            if (newCount < oldCount && oldMain)
            {
                // Usar: el principal sube, crece y se desvanece; si hay reserva, vuela a su puesto
                var used = Clone(pv.mainItemIcon);
                ShowSlot(true, null);
                yield return Vanish(used);
                if (oldReserve && newMain == oldReserve)
                {
                    var moving = Clone(pv.reserveItemIcon);
                    HideIcon(false);
                    yield return Fly(moving, pv.mainItemIcon, null, null);
                }
                ShowSlot(true, newMain);
                ShowSlot(false, newReserve);
                if (newMain) mainEntry = Now;
            }
            else if (newCount > oldCount)
            {
                // Recoger: ruleta en la caja donde entra (la principal si iba con las manos vacías)
                bool toMain = !oldMain;
                ShowSlot(!toMain, toMain ? newReserve : newMain);
                yield return Roulette(toMain, toMain ? newMain : newReserve);
                if (toMain) mainEntry = Now;
                else reserveEntry = Now;
            }
            else if (oldMain && oldReserve && newMain == oldReserve && newReserve == oldMain)
            {
                // Intercambiar: los dos iconos vuelan a la vez al centro de la otra caja
                var a = Clone(pv.mainItemIcon);
                var b = Clone(pv.reserveItemIcon);
                HideIcon(true);
                HideIcon(false);
                yield return Fly(a, pv.reserveItemIcon, b, pv.mainItemIcon);
                ShowSlot(true, newMain);
                ShowSlot(false, newReserve);
                mainEntry = Now;
            }
            else
            {
                ShowSlot(true, newMain);
                ShowSlot(false, newReserve);
                if (newMain && newMain != oldMain) mainEntry = Now;
            }
            shownMain = newMain;
            shownReserve = newReserve;
            itemsBusy = false;
            itemsRoutine = null;
            itemsDirty = true; // por si ha cambiado algo mientras tanto
        }

        void StopItems()
        {
            if (itemsRoutine != null) host.StopCoroutine(itemsRoutine);
            itemsRoutine = null;
            itemsBusy = false;
            itemsDirty = false;
            foreach (var go in flying)
                if (go) Object.Destroy(go);
            flying.Clear();
        }

        void ShowSlot(bool main, ItemData item)
        {
            var icon = main ? pv.mainItemIcon : pv.reserveItemIcon;
            var empty = main ? pv.mainItemEmpty : pv.reserveItemEmpty;
            bool has = item && item.icon;
            if (icon)
            {
                icon.sprite = has ? item.icon : null;
                icon.enabled = has;
            }
            if (empty) empty.SetActive(!has);
        }

        // La caja se queda vacía (sin «?») mientras su icono va de viaje
        void HideIcon(bool main)
        {
            var icon = main ? pv.mainItemIcon : pv.reserveItemIcon;
            var empty = main ? pv.mainItemEmpty : pv.reserveItemEmpty;
            if (icon) icon.enabled = false;
            if (empty) empty.SetActive(false);
        }

        IEnumerator Roulette(bool main, ItemData final)
        {
            var icon = main ? pv.mainItemIcon : pv.reserveItemIcon;
            var empty = main ? pv.mainItemEmpty : pv.reserveItemEmpty;
            if (icon && pool != null && pool.Count > 0)
            {
                if (empty) empty.SetActive(false);
                icon.enabled = true;
                int k = Random.Range(0, pool.Count);
                float end = Now + RouletteTime;
                while (Now < end)
                {
                    var item = pool[k++ % pool.Count];
                    if (item && item.icon) icon.sprite = item.icon;
                    yield return new WaitForSecondsRealtime(RouletteStep);
                }
            }
            ShowSlot(main, final);
        }

        // Copia del icono encima de todo el jugador, para animarla sin tocar las cajas
        RectTransform Clone(Image icon)
        {
            if (!icon || !icon.enabled || !icon.sprite) return null;
            var parent = pv.root ? pv.root : (RectTransform)view.transform;
            var go = new GameObject(icon.name + " (animación)", typeof(RectTransform)) { layer = icon.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetAsLastSibling();
            var image = go.AddComponent<Image>();
            image.sprite = icon.sprite;
            image.preserveAspect = icon.preserveAspect;
            image.raycastTarget = false;
            image.color = icon.color;
            go.AddComponent<CanvasGroup>();
            var source = icon.rectTransform;
            rect.sizeDelta = source.rect.size;
            rect.SetPositionAndRotation(source.position, source.rotation);
            float scale = source.lossyScale.x / Mathf.Max(1e-5f, parent.lossyScale.x);
            rect.localScale = new Vector3(scale, scale, 1f);
            flying.Add(go);
            return rect;
        }

        // Usar: sube 60 px, ×1,8 y se desvanece en 0,32 s
        IEnumerator Vanish(RectTransform clone)
        {
            if (!clone) yield break;
            var group = clone.GetComponent<CanvasGroup>();
            Vector3 start = clone.localPosition, scale = clone.localScale;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / UseTime)
            {
                float e = HUDAnim.EaseOut.Evaluate(t);
                clone.localPosition = start + new Vector3(0f, 60f * e, 0f);
                clone.localScale = scale * Mathf.Lerp(1f, 1.8f, e);
                group.alpha = 1f - e;
                yield return null;
            }
            Discard(clone);
        }

        // Vuela al centro del icono de destino, escalando a su tamaño (0,3 s); admite dos a la vez
        IEnumerator Fly(RectTransform a, Image toA, RectTransform b, Image toB)
        {
            var moves = new List<(RectTransform rect, RectTransform target, Vector3 p0, Quaternion r0, Vector3 s0, Vector3 s1)>();
            Add(a, toA);
            Add(b, toB);
            if (moves.Count == 0) yield break;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / FlyTime)
            {
                float e = HUDAnim.Fly.Evaluate(t);
                foreach (var m in moves)
                {
                    if (!m.rect || !m.target) continue;
                    m.rect.SetPositionAndRotation(Vector3.LerpUnclamped(m.p0, m.target.position, e), Quaternion.SlerpUnclamped(m.r0, m.target.rotation, e));
                    m.rect.localScale = Vector3.LerpUnclamped(m.s0, m.s1, e);
                }
                yield return null;
            }
            foreach (var m in moves) Discard(m.rect);

            void Add(RectTransform clone, Image target)
            {
                if (!clone || !target) return;
                var to = target.rectTransform;
                float ratio = to.rect.width * to.lossyScale.x / Mathf.Max(1e-5f, clone.rect.width * clone.lossyScale.x);
                moves.Add((clone, to, clone.position, clone.rotation, clone.localScale, clone.localScale * ratio));
            }
        }

        void Discard(RectTransform clone)
        {
            if (!clone) return;
            flying.Remove(clone.gameObject);
            Object.Destroy(clone.gameObject);
        }
    }
}
