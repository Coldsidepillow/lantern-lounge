using System;
using System.Collections.Generic;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.Tidebound;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>
    /// Tidebound on one device: you (seat 0) and three Reef Bots who talk as they play.
    /// The screen only ever reads your own <see cref="TideboundView"/>, so it shows exactly what
    /// a player is allowed to see. All rules come from the shared TideboundModule.
    /// </summary>
    internal sealed class TideboundScreen
    {
        private const int Players = 4;
        private const int You = 0;
        private const long BotDelayMs = 1500;

        private static readonly string[] Names = { "You", "Mira", "Theo", "Juno" };

        // Each Reef Bot has a creature mascot instead of a plain letter.
        private static readonly int[] Mascots = { 6, 2, 0, 4 };

        // Tidebound palette (from the Tidebound style board)
        private static readonly Color Water = Theme.Hex("#0C2F4A");
        private static readonly Color Hull = Theme.Hex("#13283D");
        private static readonly Color Trench = Theme.Hex("#081F33");
        private static readonly Color Rim = Theme.Hex("#1D4C6E");
        private static readonly Color Sand = Theme.Hex("#FFF4E0");
        private static readonly Color Coral = Theme.Hex("#FF6B4A");
        private static readonly Color Gold = Theme.Hex("#FFC233");
        private static readonly Color SeaGlass = Theme.Hex("#3FD0B8");
        private static readonly Color Foam = Theme.Hex("#EAF7F5");
        private static readonly Color Mist = Theme.Hex("#A9C9D6");
        private static readonly Color Clear = new Color(0, 0, 0, 0);
        private static readonly Color[] SeatColors =
        {
            Theme.Hex("#FFC233"), Theme.Hex("#B7A3FF"), Theme.Hex("#FF8A6B"), Theme.Hex("#6EDBC8")
        };

        private readonly LoungeApp app;
        private readonly TideboundModule module = new TideboundModule();
        private readonly LocalMatch<TideboundState, AskAction, TideboundView> match;
        private readonly Dictionary<int, VisualElement> tablets = new Dictionary<int, VisualElement>();
        private TideboundBot[] bots;
        private HashSet<int> lastHand = new HashSet<int>();

        private int selectedTarget = -1;
        private int selectedCreature = -1;
        private int lastTideSeat = -1;
        private bool resultsShown;

        // The Current in the middle of the table. After a "Nope!", you tap it to dive.
        private TideboundView displayView;
        private bool pendingDive;
        private bool diving;
        private Button pile;
        private Label pileCount;
        private Label pileHint;
        private IVisualElementScheduledItem pileGlow;
        private bool glowOn;
        private IVisualElementScheduledItem pendingBotMove;

        private VisualElement screen;
        private VisualElement content;
        private VisualElement banner;
        private Label bannerTitle;
        private Label bannerDetail;
        private readonly Button[] seatButtons = new Button[Players];
        private readonly Label[] seatInfo = new Label[Players];
        private readonly Label[] seatTag = new Label[Players];
        private Label promptLabel;
        private Button askButton;
        private Label handTitle;
        private VisualElement podTray;
        private VisualElement handGrid;
        private VisualElement overlay;

        public TideboundScreen(LoungeApp app)
        {
            this.app = app;
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            match = new LocalMatch<TideboundState, AskAction, TideboundView>(module, new MatchConfig(Players, seed));
            CreateBots(seed);
        }

        private TideboundView MyView => module.ViewFor(match.State, You);

        /// <summary>What the table shows: your live view, or the moment before a dive until you tap the Current.</summary>
        private TideboundView View => displayView ?? MyView;

        private void CreateBots(ulong seed)
        {
            bots = new TideboundBot[Players];
            for (int s = 1; s < Players; s++)
            {
                bots[s] = new TideboundBot(s, Players, new SeededRandom(seed * 31UL + (ulong)s));
            }
            ObserveAll();
        }

        private void ObserveAll()
        {
            for (int s = 1; s < Players; s++) bots[s].Observe(module.ViewFor(match.State, s));
        }

        public VisualElement Build()
        {
            screen = new VisualElement();
            screen.style.backgroundColor = Water;
            screen.style.overflow = Overflow.Hidden;

            screen.Add(Fx.Bubbles(Theme.Hex("#2E6F8E")));

            content = Theme.Column();
            content.style.flexGrow = 1;
            Theme.Padding(content, 0, 14);
            screen.Add(content);

            content.Add(BuildHeader());
            content.Add(BuildBanner());
            content.Add(BuildSeats());
            content.Add(BuildPile());
            content.Add(BuildPrompt());
            content.Add(BuildHand());

            overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0.03f, 0.12f, 0.2f, 0.9f);
            overlay.style.justifyContent = Justify.Center;
            overlay.style.display = DisplayStyle.None;
            Theme.Padding(overlay, 0, 20);
            screen.Add(overlay);

            Refresh();
            ScheduleBotsIfNeeded();
            return screen;
        }

        // ---------- Layout ----------

        private VisualElement BuildHeader()
        {
            var header = Theme.Row();
            Theme.Margin(header, 14, 0, 0, 0);

            var back = Theme.SecondaryButton("< Lounge", LeaveTable);
            back.style.height = 44;
            back.style.fontSize = 14;
            Theme.Padding(back, 0, 12);
            header.Add(back);

            var title = Theme.Label("Tidebound", 26, Foam, bold: true);
            Theme.Margin(title, 0, 0, 0, 12);
            title.style.flexGrow = 1;
            header.Add(title);
            return header;
        }

        private VisualElement BuildBanner()
        {
            banner = Theme.Column();
            Theme.Radius(banner, 18);
            Theme.Border(banner, 3, Hull);
            Theme.Padding(banner, 8, 14);
            Theme.Margin(banner, 10, 0, 0, 0);
            bannerTitle = Theme.Label("", 21, Hull, bold: true);
            bannerDetail = Theme.Label("", 13, Hull);
            banner.Add(bannerTitle);
            banner.Add(bannerDetail);
            return banner;
        }

        private VisualElement BuildSeats()
        {
            var row = Theme.Row();
            Theme.Margin(row, 10, 0, 0, 0);
            row.style.alignItems = Align.Stretch;
            for (int s = 1; s < Players; s++)
            {
                int seat = s;
                var button = new Button(() => SelectTarget(seat)) { text = string.Empty };
                button.style.flexGrow = 1;
                button.style.flexBasis = 0;
                button.style.flexDirection = FlexDirection.Column;
                button.style.alignItems = Align.Center;
                button.style.backgroundColor = Hull;
                Theme.Radius(button, 18);
                Theme.Border(button, 3, Rim);
                Theme.Padding(button, 6, 4);
                Theme.Margin(button, 0, s < Players - 1 ? 6 : 0, 0, 0);
                Theme.FocusRing(button, Rim, Foam);

                var badge = new VisualElement();
                Theme.Circle(badge, 50);
                badge.style.backgroundColor = SeatColors[seat];
                Theme.Border(badge, 3, Trench);
                badge.style.alignItems = Align.Center;
                badge.style.justifyContent = Justify.Center;
                badge.pickingMode = PickingMode.Ignore;
                badge.Add(new CreatureArt(Mascots[seat], 42, 34));
                button.Add(badge);

                var name = Theme.Label(Names[seat], 15, Foam, bold: true);
                Theme.Margin(name, 4, 0, 0, 0);
                name.pickingMode = PickingMode.Ignore;
                button.Add(name);

                seatInfo[seat] = Theme.Label("", 11, Mist);
                seatInfo[seat].pickingMode = PickingMode.Ignore;
                button.Add(seatInfo[seat]);

                seatTag[seat] = Theme.Label("", 12, Gold, bold: true);
                seatTag[seat].pickingMode = PickingMode.Ignore;
                button.Add(seatTag[seat]);

                seatButtons[seat] = button;
                row.Add(button);
            }
            return row;
        }

        private VisualElement BuildPile()
        {
            var row = Theme.Row();
            row.style.justifyContent = Justify.Center;
            Theme.Margin(row, 10, 0, 0, 0);

            pile = new Button(OnPileTap) { text = string.Empty };
            pile.style.width = 92;
            pile.style.height = 104;
            pile.style.backgroundColor = Clear;
            Theme.Border(pile, 3, Rim);
            Theme.Radius(pile, 18);
            Theme.Padding(pile, 0, 0);
            Theme.FocusRing(pile, Rim, Foam);
            for (int i = 0; i < 3; i++)
            {
                var back = CardBack(58, 80);
                back.style.position = Position.Absolute;
                back.style.left = 10 + i * 4;
                back.style.top = 14 - i * 4;
                pile.Add(back);
            }
            pileCount = Theme.Label("", 13, Hull, bold: true);
            pileCount.style.position = Position.Absolute;
            pileCount.style.right = 4;
            pileCount.style.bottom = 4;
            pileCount.style.backgroundColor = Gold;
            Theme.Border(pileCount, 2, Hull);
            Theme.Radius(pileCount, 11);
            Theme.Padding(pileCount, 1, 7);
            pileCount.pickingMode = PickingMode.Ignore;
            pile.Add(pileCount);
            row.Add(pile);

            var words = Theme.Column();
            Theme.Margin(words, 0, 0, 0, 14);
            words.style.justifyContent = Justify.Center;
            words.Add(Theme.Label("The Current", 18, Foam, bold: true));
            pileHint = Theme.Label("", 14, Mist, bold: true);
            words.Add(pileHint);
            row.Add(words);
            return row;
        }

        /// <summary>The back of a Tide Tablet: sea-glass waves and a compass bubble.</summary>
        private static VisualElement CardBack(float width, float height)
        {
            var back = new VisualElement();
            back.pickingMode = PickingMode.Ignore;
            back.style.width = width;
            back.style.height = height;
            back.style.backgroundColor = SeaGlass;
            back.style.borderTopLeftRadius = width * 0.45f;
            back.style.borderTopRightRadius = width * 0.45f;
            back.style.borderBottomLeftRadius = width * 0.16f;
            back.style.borderBottomRightRadius = width * 0.16f;
            Theme.Border(back, 3, Hull);
            back.style.alignItems = Align.Center;
            back.style.justifyContent = Justify.Center;

            var compass = new VisualElement();
            compass.pickingMode = PickingMode.Ignore;
            Theme.Circle(compass, width * 0.46f);
            compass.style.backgroundColor = Sand;
            Theme.Border(compass, 2, Hull);
            compass.style.alignItems = Align.Center;
            compass.style.justifyContent = Justify.Center;
            var needle = new VisualElement();
            needle.pickingMode = PickingMode.Ignore;
            needle.style.width = width * 0.08f;
            needle.style.height = width * 0.3f;
            needle.style.backgroundColor = Coral;
            Theme.Radius(needle, width * 0.04f);
            compass.Add(needle);
            back.Add(compass);
            return back;
        }

        private VisualElement BuildPrompt()
        {
            var card = Theme.Column();
            card.style.backgroundColor = Sand;
            Theme.Border(card, 3, Hull);
            Theme.Radius(card, 20);
            Theme.Padding(card, 10, 12);
            Theme.Margin(card, 10, 0, 0, 0);

            promptLabel = Theme.Label("", 17, Hull, bold: true);
            card.Add(promptLabel);

            askButton = Theme.PrimaryButton("Ask!", Ask);
            askButton.style.backgroundColor = Coral;
            Theme.Border(askButton, 3, Hull);
            Theme.FocusRing(askButton, Hull, Foam);
            askButton.style.color = Hull;
            askButton.style.height = 54;
            askButton.style.fontSize = 22;
            Theme.Margin(askButton, 8, 0, 0, 0);
            card.Add(askButton);
            return card;
        }

        private VisualElement BuildHand()
        {
            var area = Theme.Column();
            area.style.flexGrow = 1;
            Theme.Margin(area, 10, 0, 10, 0);

            var titleRow = Theme.Row();
            titleRow.style.flexWrap = Wrap.Wrap;
            handTitle = Theme.Label("", 14, Foam, bold: true);
            Theme.Margin(handTitle, 0, 8, 4, 0);
            titleRow.Add(handTitle);
            podTray = Theme.Row();
            podTray.style.flexWrap = Wrap.Wrap;
            titleRow.Add(podTray);
            area.Add(titleRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            area.Add(scroll);

            handGrid = Theme.Row();
            handGrid.style.flexWrap = Wrap.Wrap;
            handGrid.style.alignItems = Align.FlexStart;
            scroll.Add(handGrid);
            return area;
        }

        // ---------- Actions ----------

        private void SelectTarget(int seat)
        {
            if (pendingDive || diving) return;
            var view = MyView;
            if (!view.IsMyTide || view.HandCounts[seat] == 0) return;
            selectedTarget = seat;
            Sfx.Play(Sound.Tap);
            Refresh();
            Fx.Pop(seatButtons[seat], 0.08f);
        }

        private void SelectCreature(int creature)
        {
            if (pendingDive || diving || !MyView.IsMyTide) return;
            selectedCreature = creature;
            Sfx.Play(Sound.Select);
            Refresh();
            if (tablets.TryGetValue(creature, out var tablet)) Fx.Pop(tablet, 0.12f);
        }

        private void Ask()
        {
            if (pendingDive || diving) return;
            var before = MyView;
            if (!before.IsMyTide || selectedTarget < 0 || selectedCreature < 0) return;

            var check = match.TryPlay(You, new AskAction(selectedTarget, selectedCreature));
            if (!check.Ok)
            {
                bannerDetail.text = check.Message;
                return;
            }

            ObserveAll();
            var after = MyView;
            if (CardsDrawnBy(after.LastEvents, You, diveOnly: true) > 0)
            {
                // "Nope! Dive!" Hold the table as it was until you tap the Current yourself.
                displayView = before;
                pendingDive = true;
                Sfx.Play(Sound.Tap);
                StartPileGlow();
                Refresh();
                return;
            }

            AfterMove(byYou: true);
            ScheduleBotsIfNeeded();
        }

        private void OnPileTap()
        {
            if (diving) return;
            if (!pendingDive)
            {
                bannerDetail.text = " ";
                Fx.Pop(pile, 0.05f);
                return;
            }

            pendingDive = false;
            diving = true;
            StopPileGlow();
            Refresh();
            Sfx.Play(Sound.Dive);
            var after = MyView;
            FlyCards(CardsDrawnBy(after.LastEvents, You, diveOnly: true), handTitle, () =>
            {
                diving = false;
                displayView = null;

                // Any other draws this move caused (refills after your dive), shown too.
                int extra = CardsDrawnBy(after.LastEvents, You, diveOnly: false) - CardsDrawnBy(after.LastEvents, You, diveOnly: true);
                if (extra > 0) FlyCards(extra, handTitle, null);
                for (int s = 1; s < Players; s++)
                {
                    int drawn = CardsDrawnBy(after.LastEvents, s, diveOnly: false);
                    if (drawn > 0) FlyCards(drawn, seatButtons[s], null);
                }

                Refresh();
                Celebrate(after, byYou: true);
                ScheduleBotsIfNeeded();
            });
        }

        private void AfterMove(bool byYou)
        {
            ObserveAll();
            var view = MyView;

            // Show cards flying out of the Current to whoever drew them.
            for (int s = 0; s < Players; s++)
            {
                int drawn = CardsDrawnBy(view.LastEvents, s, diveOnly: false);
                if (drawn > 0) FlyCards(drawn, s == You ? (VisualElement)handTitle : seatButtons[s], null);
            }

            Refresh();
            Celebrate(view, byYou);
        }

        /// <summary>How many cards a seat took from the Current in these events.</summary>
        private static int CardsDrawnBy(IReadOnlyList<TideEvent> events, int seat, bool diveOnly)
        {
            int drawn = 0;
            foreach (var e in events)
            {
                if (e.Seat != seat) continue;
                if (e.Kind == TideEventKind.Dove) drawn += e.Count;
                else if (e.Kind == TideEventKind.LuckyDive) drawn += 1;
                else if (!diveOnly && (e.Kind == TideEventKind.Refilled || e.Kind == TideEventKind.NoOneToAsk)) drawn += 1;
            }
            return drawn;
        }

        private void StartPileGlow()
        {
            StopPileGlow();
            pileGlow = pile.schedule.Execute(() =>
            {
                glowOn = !glowOn;
                Theme.BorderColor(pile, glowOn ? Gold : Foam);
                if (glowOn) Fx.Pop(pile, 0.07f, 0.45f);
            }).Every(550);
        }

        private void StopPileGlow()
        {
            pileGlow?.Pause();
            pileGlow = null;
            glowOn = false;
            Theme.BorderColor(pile, Rim);
        }

        /// <summary>Little card backs fly from the Current to a hand or a seat, one after another.</summary>
        private void FlyCards(int count, VisualElement target, Action done)
        {
            int shown = Mathf.Min(count, 8);
            if (Prefs.ReduceMotion || shown <= 0 || target == null || pile.panel == null)
            {
                done?.Invoke();
                return;
            }

            Vector2 from = screen.WorldToLocal(pile.worldBound.center);
            Vector2 to = screen.WorldToLocal(target.worldBound.center);
            for (int i = 0; i < shown; i++)
            {
                screen.schedule.Execute(() => LaunchCard(from, to)).StartingIn(i * 110L);
            }
            if (done != null) screen.schedule.Execute(done).StartingIn(shown * 110L + 440L);
        }

        private void LaunchCard(Vector2 from, Vector2 to)
        {
            Sfx.Play(Sound.Tap);
            var card = CardBack(34, 46);
            card.style.position = Position.Absolute;
            card.style.left = from.x - 17f;
            card.style.top = from.y - 23f;
            screen.Add(card);

            const float seconds = 0.42f;
            float elapsed = 0f;
            card.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / seconds);
                float eased = 1f - (1f - t) * (1f - t);
                float x = (to.x - from.x) * eased;
                float y = (to.y - from.y) * eased - Mathf.Sin(t * Mathf.PI) * 40f;
                card.style.translate = new StyleTranslate(new Translate(x, y));
                float scaleNow = Mathf.Lerp(1f, 0.7f, t);
                card.style.scale = new StyleScale(new Scale(new Vector3(scaleNow, scaleNow, 1f)));
                if (t >= 1f) card.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= seconds);
        }

        private void ScheduleBotsIfNeeded()
        {
            var state = match.State;
            if (state.IsOver || state.CurrentSeat == You) return;

            pendingBotMove?.Pause();
            pendingBotMove = screen.schedule.Execute(() =>
            {
                pendingBotMove = null;
                var current = match.State;
                if (current.IsOver || current.CurrentSeat == You) return;

                int seat = current.CurrentSeat;
                var ask = bots[seat].ChooseAsk(module.ViewFor(current, seat));
                if (match.TryPlay(seat, ask).Ok) AfterMove(byYou: false);
                ScheduleBotsIfNeeded();
            }).StartingIn(BotDelayMs);
        }

        private void Rematch()
        {
            pendingBotMove?.Pause();
            pendingBotMove = null;
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            match.Reset(module.Rematch(match.State, seed));
            CreateBots(seed);
            lastHand.Clear();
            selectedTarget = -1;
            selectedCreature = -1;
            lastTideSeat = -1;
            pendingDive = false;
            diving = false;
            displayView = null;
            StopPileGlow();
            Sfx.Play(Sound.Tap);
            Refresh();
            ScheduleBotsIfNeeded();
        }

        private void LeaveTable()
        {
            StopPileGlow();
            pendingBotMove?.Pause();
            pendingBotMove = null;
            app.ShowLounge();
        }

        // ---------- Fun: sounds and pop-ups ----------

        private void Celebrate(TideboundView view, bool byYou)
        {
            int yourPods = 0;
            int podCreature = -1;
            bool youFound = false, gave = false, anyDive = false;
            foreach (var e in view.LastEvents)
            {
                if (e.Kind == TideEventKind.PodFormed && e.Seat == You)
                {
                    yourPods++;
                    podCreature = e.Creature;
                }
                if (e.Kind == TideEventKind.Asked && e.Count > 0 && e.Seat == You) gave = true;
                if (e.Kind == TideEventKind.Asked && e.Count == 0)
                {
                    if (e.Seat == You) anyDive = true;
                }
                if (e.Kind == TideEventKind.LuckyDive && e.Seat == You) youFound = true;
            }

            if (view.IsOver) return; // The results screen has its own fanfare.

            if (yourPods > 0)
            {
                Sfx.Play(Sound.Pod);
                string what = yourPods == 1 ? TideboundCards.Creature(podCreature).Name + " pod!" : $"{yourPods} pods!";
                Fx.Toast(screen, what, Gold, Hull);
            }
            else if (youFound)
            {
                Sfx.Play(Sound.Found);
                Fx.Toast(screen, "Found it!", SeaGlass, Hull);
            }
            else if (gave)
            {
                Sfx.Play(Sound.Give);
            }
            else if (anyDive)
            {
                Sfx.Play(Sound.Dive);
            }

            if (view.IsMyTide && lastTideSeat != You && !byYou)
            {
                screen.schedule.Execute(() =>
                {
                    Sfx.Play(Sound.YourTide);
                    Fx.Toast(screen, "Your tide!", Gold, Hull, 1.1f);
                }).StartingIn(yourPods > 0 ? 900 : 350);
            }
            lastTideSeat = view.CurrentSeat;
        }

        // ---------- Drawing ----------

        private void Refresh()
        {
            var view = View;
            KeepSelectionValid(view);

            DrawPile(view);
            DrawBanner(view);
            DrawSeats(view);
            DrawPrompt(view);
            DrawHand(view);

            if (view.IsOver)
            {
                ShowResults(view);
            }
            else
            {
                resultsShown = false;
                overlay.style.display = DisplayStyle.None;
            }
        }

        private void DrawPile(TideboundView view)
        {
            pileCount.text = view.CurrentCount.ToString();
            pile.style.opacity = view.CurrentCount == 0 ? 0.4f : 1f;
            if (diving) pileHint.text = "Diving...";
            else if (pendingDive) pileHint.text = "Tap to dive!";
            else if (view.CurrentCount == 0) pileHint.text = "All gone!";
            else pileHint.text = $"{view.CurrentCount} cards to dive for";
            pileHint.style.color = pendingDive || diving ? Gold : Mist;
        }

        private void KeepSelectionValid(TideboundView view)
        {
            if (selectedCreature >= 0 && view.CountInMyHand(selectedCreature) == 0) selectedCreature = -1;
            if (selectedTarget >= 0 && view.HandCounts[selectedTarget] == 0) selectedTarget = -1;
            if (selectedTarget < 0 && view.IsMyTide)
            {
                for (int s = 1; s < Players; s++)
                {
                    if (view.HandCounts[s] > 0)
                    {
                        selectedTarget = s;
                        break;
                    }
                }
            }
        }

        private void DrawBanner(TideboundView view)
        {
            if (view.IsOver)
            {
                banner.style.backgroundColor = SeaGlass;
                bannerTitle.text = "Every pod is claimed!";
                bannerDetail.text = "Let's see who rules the reef.";
            }
            else if (pendingDive || diving)
            {
                banner.style.backgroundColor = SeaGlass;
                bannerTitle.text = diving ? "Diving..." : "Nope! Dive!";
                bannerDetail.text = diving ? "Drawing a card..." : "Tap the Current to draw one card.";
            }
            else if (view.IsMyTide)
            {
                banner.style.backgroundColor = Gold;
                bannerTitle.text = "Your tide!";
                bannerDetail.text = " ";
            }
            else
            {
                banner.style.backgroundColor = Mist;
                bannerTitle.text = $"{Names[view.CurrentSeat]}'s tide";
                bannerDetail.text = " ";
            }
        }

        private void DrawSeats(TideboundView view)
        {
            for (int s = 1; s < Players; s++)
            {
                int cards = view.HandCounts[s];
                int pods = view.Pods[s].Count;
                seatInfo[s].text = $"{cards} cards · {pods} {(pods == 1 ? "pod" : "pods")}";

                bool theirTide = !view.IsOver && view.CurrentSeat == s;
                bool selected = view.IsMyTide && selectedTarget == s;
                seatTag[s].text = theirTide ? "Thinking..." : selected ? "Asking!" : cards == 0 ? "No cards" : " ";
                seatTag[s].style.color = selected ? Coral : Gold;

                Theme.BorderColor(seatButtons[s], theirTide ? Gold : selected ? Coral : Rim);
                seatButtons[s].style.backgroundColor = selected ? Theme.Hex("#2A2033") : Hull;
                seatButtons[s].SetEnabled(view.IsMyTide && cards > 0);
                seatButtons[s].style.opacity = cards == 0 ? 0.5f : 1f;
            }
        }

        private void DrawPrompt(TideboundView view)
        {
            bool ready = !pendingDive && !diving && view.IsMyTide && selectedTarget >= 0 && selectedCreature >= 0;
            if (pendingDive || diving) promptLabel.text = "Tap the Current in the middle to dive!";
            else if (view.IsOver) promptLabel.text = "Game over!";
            else if (!view.IsMyTide) promptLabel.text = " ";
            else if (selectedCreature < 0 || selectedTarget < 0) promptLabel.text = " ";
            else promptLabel.text = $"Ask {Names[selectedTarget]} for a {TideboundCards.Creature(selectedCreature).Name}?";

            Theme.SetButtonEnabled(askButton, ready);
        }

        private void DrawHand(TideboundView view)
        {
            int pods = view.Pods[You].Count;
            handTitle.text = $"Your hand · {view.MyHand.Count} {(view.MyHand.Count == 1 ? "card" : "cards")} · {pods} {(pods == 1 ? "pod" : "pods")}";

            podTray.Clear();
            foreach (int creature in view.Pods[You])
            {
                var chip = new VisualElement();
                Theme.Circle(chip, 30);
                chip.style.backgroundColor = Sand;
                Theme.Border(chip, 2, Gold);
                chip.style.alignItems = Align.Center;
                chip.style.justifyContent = Justify.Center;
                Theme.Margin(chip, 0, 3, 4, 0);
                chip.tooltip = TideboundCards.Creature(creature).Name + " pod";
                chip.Add(new CreatureArt(creature, 24, 20));
                podTray.Add(chip);
            }

            handGrid.Clear();
            tablets.Clear();
            var newCards = new List<int>();
            foreach (int card in view.MyHand)
            {
                int creature = TideboundCards.CreatureOf(card);
                var tablet = BuildTablet(view, card);
                tablets[creature] = tablet;
                handGrid.Add(tablet);
                if (!lastHand.Contains(card)) newCards.Add(creature);
            }
            if (view.MyHand.Count == 0)
            {
                handGrid.Add(Theme.Label("No cards right now. You'll draw one when your tide comes.", 13, Mist));
            }

            // New cards (including the opening deal) bounce in so you notice them.
            foreach (int creature in newCards)
            {
                if (tablets.TryGetValue(creature, out var tablet)) Fx.Pop(tablet, 0.18f, 0.35f);
            }
            lastHand = new HashSet<int>(view.MyHand);
        }

        // A Tide Tablet: a drawn creature on its family color, its name, and its tide.
        private VisualElement BuildTablet(TideboundView view, int card)
        {
            int creature = TideboundCards.CreatureOf(card);
            var info = TideboundCards.Creature(creature);
            bool selected = creature == selectedCreature;

            var tablet = new Button(() => SelectCreature(creature)) { text = string.Empty };
            tablet.style.width = 82;
            tablet.style.height = 132;
            tablet.style.flexDirection = FlexDirection.Column;
            tablet.style.alignItems = Align.Center;
            tablet.style.backgroundColor = Sand;
            tablet.style.borderTopLeftRadius = 38;
            tablet.style.borderTopRightRadius = 38;
            tablet.style.borderBottomLeftRadius = 14;
            tablet.style.borderBottomRightRadius = 14;
            tablet.style.overflow = Overflow.Hidden;
            Theme.Border(tablet, selected ? 4 : 3, selected ? Coral : Hull);
            Theme.Padding(tablet, 0, 0);
            Theme.Margin(tablet, 6, 6, 6, 0);
            tablet.style.translate = new StyleTranslate(new Translate(0, selected ? -6 : 0));
            tablet.SetEnabled(view.IsMyTide);
            tablet.style.opacity = 1f;
            tablet.tooltip = info.Trait;

            var band = Theme.Column();
            band.style.width = Length.Percent(100);
            band.style.height = 64;
            band.style.backgroundColor = FamilyColor(info.Family);
            band.style.alignItems = Align.Center;
            band.style.justifyContent = Justify.FlexEnd;
            band.pickingMode = PickingMode.Ignore;
            band.Add(new CreatureArt(creature, 70, 56));
            tablet.Add(band);

            var name = Theme.Label(info.Name, 12, Hull, bold: true);
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(name, 5, 3, 0, 3);
            name.pickingMode = PickingMode.Ignore;
            tablet.Add(name);

            var family = Theme.Label(info.Family.ToString().ToUpperInvariant(), 9, Theme.Hex("#5A4A30"), bold: true);
            family.pickingMode = PickingMode.Ignore;
            tablet.Add(family);

            var tide = Theme.Label(TideboundCards.TideOf(card).ToString(), 11, Hull, bold: true);
            tide.style.backgroundColor = FamilyColor(info.Family);
            Theme.Radius(tide, 8);
            Theme.Padding(tide, 1, 8);
            Theme.Margin(tide, 3, 0, 0, 0);
            tide.pickingMode = PickingMode.Ignore;
            tablet.Add(tide);
            return tablet;
        }

        private void ShowResults(TideboundView view)
        {
            bool firstShow = !resultsShown;
            resultsShown = true;
            overlay.Clear();
            overlay.style.display = DisplayStyle.Flex;
            overlay.Add(Fx.Bubbles(Gold, 12));

            var card = Theme.Column();
            card.style.backgroundColor = Hull;
            Theme.Border(card, 4, Gold);
            Theme.Radius(card, 24);
            Theme.Padding(card, 18, 16);

            bool youWon = Contains(view.Winners, You);
            string title;
            if (view.Winners.Count == Players) title = "Everyone tied!";
            else if (youWon && view.Winners.Count == 1) title = "Tide Champion!";
            else if (youWon) title = "You share the win!";
            else title = $"{Names[view.Winners[0]]} wins!";

            var mascot = Theme.Row();
            mascot.style.justifyContent = Justify.Center;
            int hero = youWon ? Mascots[You] : Mascots[view.Winners[0]];
            mascot.Add(new CreatureArt(hero, 120, 96));
            card.Add(mascot);

            var heading = Theme.Label(title, 30, Gold, bold: true);
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(heading);

            var seats = new List<int> { 0, 1, 2, 3 };
            seats.Sort((a, b) => view.Pods[b].Count.CompareTo(view.Pods[a].Count));
            int place = 0, lastPods = -1;
            for (int i = 0; i < seats.Count; i++)
            {
                int seat = seats[i];
                int pods = view.Pods[seat].Count;
                if (pods != lastPods) place = i + 1;
                lastPods = pods;

                var row = Theme.Row();
                row.style.backgroundColor = seat == You ? Sand : Trench;
                Theme.Radius(row, 14);
                Theme.Padding(row, 8, 12);
                Theme.Margin(row, i == 0 ? 12 : 6, 0, 0, 0);
                Color text = seat == You ? Hull : Foam;
                var placeLabel = Theme.Label(place.ToString(), 18, text, bold: true);
                placeLabel.style.width = 26;
                row.Add(placeLabel);
                row.Add(new CreatureArt(Mascots[seat], 30, 24));
                var nameLabel = Theme.Label(Names[seat], 15, text, bold: true);
                Theme.Margin(nameLabel, 0, 0, 0, 6);
                nameLabel.style.flexGrow = 1;
                row.Add(nameLabel);
                row.Add(Theme.Label($"{pods} {(pods == 1 ? "pod" : "pods")}", 15, text, bold: true));
                card.Add(row);
            }

            var buttons = Theme.Row();
            Theme.Margin(buttons, 16, 0, 0, 0);
            var lounge = Theme.SecondaryButton("Lounge", LeaveTable);
            lounge.style.flexGrow = 1;
            Theme.Margin(lounge, 0, 8, 0, 0);
            var again = Theme.PrimaryButton("Dive again!", Rematch);
            again.style.flexGrow = 2;
            buttons.Add(lounge);
            buttons.Add(again);
            card.Add(buttons);

            overlay.Add(card);

            if (firstShow)
            {
                Sfx.Play(youWon ? Sound.Win : Sound.Pod);
                Fx.Pop(card, 0.1f, 0.4f);
            }
        }

        // ---------- Words ----------

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        private static Color FamilyColor(Family family)
        {
            switch (family)
            {
                case Family.Reef: return Theme.Hex("#FFB59F");
                case Family.Drift: return Theme.Hex("#C9BAFF");
                case Family.Shallows: return Theme.Hex("#A8EBDD");
                case Family.Glow: return Theme.Hex("#FFE08A");
                case Family.Kelpwood: return Theme.Hex("#B9E6B4");
                default: return Theme.Hex("#A9C8FF");
            }
        }
    }
}
