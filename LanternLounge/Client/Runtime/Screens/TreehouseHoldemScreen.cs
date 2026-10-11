using System;
using System.Collections.Generic;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.TreehouseHoldem;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>
    /// Treehouse Hold'em: a family game-night table with four animal friends. 1-4 people take turns
    /// on this device (with a "pass the device" screen between them) and computer friends fill the
    /// empty seats. Coins are play money only. All rules come from the shared HoldemModule; the
    /// screen only shows what the current person is allowed to see.
    /// </summary>
    internal sealed class TreehouseHoldemScreen
    {
        private const int Seats = 4;
        private const long BotDelayMs = 1100;
        private const float FlySeconds = 0.4f;
        private const long StaggerMs = 110;

        // Seat 0 sits at the bottom, 1 on the left, 2 at the top, 3 on the right.
        private static readonly AnimalArt.Kind[] Animals = { AnimalArt.Kind.Bunny, AnimalArt.Kind.Owl, AnimalArt.Kind.Fox, AnimalArt.Kind.Bear };
        private static readonly string[] FriendNames = { "Bea Bunny", "Ollie Owl", "Fifi Fox", "Benny Bear" };
        private static readonly Color[] SeatColors =
        {
            Theme.Hex("#D9D2FF"), Theme.Hex("#F3D7B5"), Theme.Hex("#FFD0AE"), Theme.Hex("#E6C7AE")
        };
        private static readonly float[] Boldness = { 0.5f, 0.35f, 0.65f, 0.5f };

        private static readonly Color Ink = HoldemPalette.Ink;
        private static readonly Color Cream = Theme.Hex("#FFF8EA");
        private static readonly Color Soft = Theme.Hex("#5B5470");

        private readonly LoungeApp app;
        private readonly int humans;
        private readonly HoldemModule module = new HoldemModule();
        private readonly LocalMatch<HoldemState, HoldemMove, HoldemView> match;
        private readonly HoldemBot[] bots = new HoldemBot[Seats];
        private readonly string[] names = new string[Seats];
        private readonly SeededRandom hintRandom = new SeededRandom(12345);

        /// <summary>The person whose secret cards are on screen, or -1 when they are hidden.</summary>
        private int viewer;
        private bool busy;
        private bool resultsShown;
        private bool curtainOpen;
        private bool rulesOpen;
        private bool dealing;
        private float toastFreeAt;
        private int toastRound;
        private int lastTurnShown = -1;
        private HoldemView displayView;
        private string roundSummary = "";
        private string hintKey;
        private float hintChance;
        private IVisualElementScheduledItem pendingBot;

        private VisualElement screen;
        private Label roundLabel;
        private Button hintsButton;
        private readonly VisualElement[] seatPanels = new VisualElement[Seats];
        private readonly VisualElement[] avatars = new VisualElement[Seats];
        private readonly Label[] seatCoins = new Label[Seats];
        private readonly Label[] seatBets = new Label[Seats];
        private readonly Label[] seatTags = new Label[Seats];
        private readonly VisualElement[] seatCards = new VisualElement[Seats];
        private readonly VisualElement[] dealerBadges = new VisualElement[Seats];
        private readonly Label[] bubbles = new Label[Seats];
        private readonly IVisualElementScheduledItem[] bubbleTimers = new IVisualElementScheduledItem[Seats];
        private readonly VisualElement[] boardSlots = new VisualElement[5];
        private Label streetLabel;
        private VisualElement pileHolder;
        private Label pileLabel;

        private VisualElement banner;
        private Label bannerTitle;
        private Label bannerDetail;
        private VisualElement myCardsRow;
        private VisualElement hintPanel;
        private Label hintHand;
        private VisualElement hintStars;
        private Label hintAdvice;
        private VisualElement actionRow;
        private Button foldButton;
        private Button stayButton;
        private Button raiseButton;
        private Button nextButton;
        private VisualElement curtain;
        private VisualElement rules;
        private VisualElement results;

        public TreehouseHoldemScreen(LoungeApp app, int humans)
        {
            this.app = app;
            this.humans = Mathf.Clamp(humans, 1, Seats);
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            match = new LocalMatch<HoldemState, HoldemMove, HoldemView>(module, new MatchConfig(Seats, seed));
            for (int s = 0; s < Seats; s++)
            {
                if (IsHuman(s))
                {
                    names[s] = this.humans == 1 ? "You" : $"Player {s + 1}";
                }
                else
                {
                    names[s] = FriendNames[s];
                    bots[s] = new HoldemBot(seed * 17UL + (ulong)s, Boldness[s]);
                }
            }
            viewer = this.humans == 1 ? 0 : -1;
        }

        private bool IsHuman(int seat) => seat >= 0 && seat < humans;

        private bool PassAndPlay => humans > 1;

        private HoldemView ViewOf(int seat) => module.ViewFor(match.State, seat);

        /// <summary>The table as it should look now: the moment before a move while things fly, or live.</summary>
        private HoldemView View => displayView ?? ViewOf(Math.Max(0, viewer));

        public VisualElement Build()
        {
            screen = new VisualElement();
            screen.style.backgroundColor = HoldemPalette.Sky;
            screen.style.overflow = Overflow.Hidden;
            screen.Add(BuildBackdrop());

            var content = Theme.Column();
            content.style.flexGrow = 1;
            Theme.Padding(content, 0, 12);
            screen.Add(content);

            content.Add(BuildHeader());
            content.Add(BuildTable());
            content.Add(BuildBanner());
            content.Add(BuildHandArea());
            content.Add(BuildActions());

            curtain = Cover(new Color(0.75f, 0.9f, 1f, 0.98f));
            screen.Add(curtain);
            rules = Cover(new Color(0.17f, 0.13f, 0.25f, 0.6f));
            screen.Add(rules);
            results = Cover(new Color(0.17f, 0.13f, 0.25f, 0.7f));
            screen.Add(results);

            screen.RegisterCallback<DetachFromPanelEvent>(_ => StopAll());

            // Show the first deal flying out, then start.
            busy = true;
            dealing = true;
            Refresh();
            screen.schedule.Execute(() =>
            {
                long at = AnimateDeal(match.State.LastEvents, 0);
                Schedule(at + 300, () =>
                {
                    busy = false;
                    dealing = false;
                    Refresh();
                    NextStep();
                });
            }).StartingIn(250);
            return screen;
        }

        // ---------- Layout ----------

        /// <summary>Soft hills, a sun and leafy corners behind everything.</summary>
        private static VisualElement BuildBackdrop()
        {
            var layer = new VisualElement();
            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.right = 0;
            layer.style.top = 0;
            layer.style.bottom = 0;

            var sun = new VisualElement();
            sun.pickingMode = PickingMode.Ignore;
            sun.style.position = Position.Absolute;
            sun.style.right = -30;
            sun.style.top = -30;
            Theme.Circle(sun, 130);
            sun.style.backgroundColor = new Color(1f, 0.88f, 0.4f, 0.55f);
            layer.Add(sun);

            var hill = new VisualElement();
            hill.pickingMode = PickingMode.Ignore;
            hill.style.position = Position.Absolute;
            hill.style.left = -80;
            hill.style.right = -80;
            hill.style.bottom = -260;
            hill.style.height = 520;
            Theme.Radius(hill, 400);
            hill.style.backgroundColor = Theme.Hex("#9ED98F");
            layer.Add(hill);

            for (int i = 0; i < 6; i++)
            {
                var leaf = new VisualElement();
                leaf.pickingMode = PickingMode.Ignore;
                leaf.style.position = Position.Absolute;
                leaf.style.width = 34;
                leaf.style.height = 18;
                leaf.style.backgroundColor = i % 2 == 0 ? HoldemPalette.Leaf : HoldemPalette.LeafDark;
                leaf.style.borderTopLeftRadius = 18;
                leaf.style.borderBottomRightRadius = 18;
                leaf.style.left = i < 3 ? -6 + i * 16 : Length.Percent(80 + (i - 3) * 6);
                leaf.style.top = i < 3 ? 60 + i * 22 : 110 + (i - 3) * 20;
                leaf.style.rotate = new StyleRotate(new Rotate(new Angle(i * 37f - 40f, AngleUnit.Degree)));
                leaf.style.opacity = 0.7f;
                layer.Add(leaf);
            }
            return layer;
        }

        private VisualElement BuildHeader()
        {
            var header = Theme.Row();
            Theme.Margin(header, 12, 0, 0, 0);

            var back = SmallButton("<", LeaveTable, Cream);
            back.style.width = 44;
            back.tooltip = "Back to the lounge";
            header.Add(back);

            var titles = Theme.Column();
            titles.style.flexGrow = 1;
            Theme.Margin(titles, 0, 6, 0, 10);
            var title = Theme.Label("Treehouse Hold'em", 19, HoldemPalette.LeafDark, bold: true);
            title.style.unityTextOutlineWidth = 1f;
            title.style.unityTextOutlineColor = Color.white;
            titles.Add(title);
            roundLabel = Theme.Label("", 12, Soft, bold: true);
            titles.Add(roundLabel);
            header.Add(titles);

            var help = SmallButton("?", ShowRules, HoldemPalette.Sun);
            help.style.width = 44;
            help.style.fontSize = 20;
            help.tooltip = "How to play and the best hands";
            header.Add(help);
            hintsButton = SmallButton(HintsLabel(), ToggleHints, Theme.Hex("#C8F0B8"));
            hintsButton.style.flexShrink = 0;
            Theme.Margin(hintsButton, 0, 0, 0, 6);
            header.Add(hintsButton);
            return header;
        }

        private static Button SmallButton(string text, Action onClick, Color color)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 44;
            button.style.minHeight = 44;
            button.style.fontSize = 14;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = Ink;
            button.style.backgroundColor = color;
            Theme.Border(button, 2, Ink);
            Theme.Radius(button, 14);
            Theme.Padding(button, 0, 10);
            Theme.Margin(button, 0, 0, 0, 0);
            Theme.FocusRing(button, Ink, Theme.Hex("#3D6BFF"));
            return button;
        }

        private VisualElement BuildTable()
        {
            var table = new VisualElement();
            table.style.flexGrow = 1;
            table.style.minHeight = 380;
            Theme.Margin(table, 6, 0, 0, 0);

            // A wooden table with a soft play mat.
            var wood = new VisualElement();
            wood.pickingMode = PickingMode.Ignore;
            wood.style.position = Position.Absolute;
            wood.style.left = 2;
            wood.style.right = 2;
            wood.style.top = 44;
            wood.style.bottom = 30;
            wood.style.backgroundColor = HoldemPalette.Mat;
            Theme.Border(wood, 9, HoldemPalette.Wood);
            Theme.Radius(wood, 70);
            var stitch = new VisualElement();
            stitch.pickingMode = PickingMode.Ignore;
            stitch.style.position = Position.Absolute;
            stitch.style.left = 8;
            stitch.style.right = 8;
            stitch.style.top = 8;
            stitch.style.bottom = 8;
            Theme.Border(stitch, 2, new Color(0.36f, 0.77f, 0.42f, 0.45f));
            Theme.Radius(stitch, 60);
            wood.Add(stitch);
            table.Add(wood);

            table.Add(BuildCentre());

            var top = Anchor(table);
            top.style.top = 0;
            top.style.left = 0;
            top.style.right = 0;
            top.style.alignItems = Align.Center;
            top.Add(BuildSeat(2, compactRow: false));

            var left = Anchor(table);
            left.style.top = 46;
            left.style.left = 0;
            left.Add(BuildSeat(1, compactRow: false));

            var right = Anchor(table);
            right.style.top = 46;
            right.style.right = 0;
            right.Add(BuildSeat(3, compactRow: false));

            var bottom = Anchor(table);
            bottom.style.bottom = 0;
            bottom.style.left = 0;
            bottom.style.right = 0;
            bottom.style.alignItems = Align.Center;
            bottom.Add(BuildSeat(0, compactRow: true));
            return table;
        }

        private static VisualElement Anchor(VisualElement parent)
        {
            var anchor = new VisualElement();
            anchor.pickingMode = PickingMode.Ignore;
            anchor.style.position = Position.Absolute;
            parent.Add(anchor);
            return anchor;
        }

        private VisualElement BuildSeat(int seat, bool compactRow)
        {
            var panel = compactRow ? Theme.Row() : Theme.Column();
            panel.pickingMode = PickingMode.Ignore;
            panel.style.alignItems = Align.Center;
            if (!compactRow) panel.style.width = 76;
            else
            {
                panel.style.backgroundColor = Cream;
                Theme.Border(panel, 3, HoldemPalette.Wood);
                Theme.Radius(panel, 22);
                Theme.Padding(panel, 2, 8);
            }

            var ring = new VisualElement();
            ring.pickingMode = PickingMode.Ignore;
            float size = compactRow ? 38 : 50;
            Theme.Circle(ring, size);
            ring.style.backgroundColor = SeatColors[seat];
            Theme.Border(ring, 3, HoldemPalette.Wood);
            ring.style.alignItems = Align.Center;
            ring.style.justifyContent = Justify.Center;
            ring.Add(new AnimalArt(Animals[seat], size - 8));
            ring.tooltip = $"{names[seat]} ({AnimalArt.KindName(Animals[seat])})";

            var badge = new FaceIcon(HoldemCards.Jack, 20);
            badge.style.position = Position.Absolute;
            badge.style.right = -6;
            badge.style.top = -6;
            badge.style.display = DisplayStyle.None;
            ring.Add(badge);
            dealerBadges[seat] = badge;
            avatars[seat] = ring;
            panel.Add(ring);

            var info = Theme.Column();
            info.pickingMode = PickingMode.Ignore;
            info.style.alignItems = compactRow ? Align.FlexStart : Align.Center;
            if (compactRow) Theme.Margin(info, 0, 6, 0, 6);
            var name = Theme.Label(names[seat], 12, Ink, bold: true);
            name.pickingMode = PickingMode.Ignore;
            NoWrap(name, compactRow ? 110 : 76);
            info.Add(name);

            var coinRow = Theme.Row();
            coinRow.pickingMode = PickingMode.Ignore;
            coinRow.Add(new CoinArt(1, 14));
            seatCoins[seat] = Theme.Label("", 12, Ink, bold: true);
            Theme.Margin(seatCoins[seat], 0, 0, 0, 3);
            coinRow.Add(seatCoins[seat]);
            info.Add(coinRow);

            seatTags[seat] = Theme.Label(" ", 11, Theme.Hex("#C2410C"), bold: true);
            seatTags[seat].pickingMode = PickingMode.Ignore;
            NoWrap(seatTags[seat], compactRow ? 110 : 76);
            info.Add(seatTags[seat]);
            panel.Add(info);

            var cards = Theme.Row();
            cards.pickingMode = PickingMode.Ignore;
            cards.style.height = compactRow ? 34 : 44;
            cards.style.alignItems = Align.Center;
            seatCards[seat] = cards;
            panel.Add(cards);

            var bet = Theme.Label("", 12, Ink, bold: true);
            bet.pickingMode = PickingMode.Ignore;
            bet.style.backgroundColor = HoldemPalette.Sun;
            Theme.Border(bet, 2, Ink);
            Theme.Radius(bet, 10);
            Theme.Padding(bet, 1, 6);
            if (compactRow) Theme.Margin(bet, 0, 0, 0, 6);
            seatBets[seat] = bet;
            panel.Add(bet);

            var bubble = Theme.Label("", 13, Ink, bold: true);
            bubble.pickingMode = PickingMode.Ignore;
            bubble.style.position = Position.Absolute;
            bubble.style.backgroundColor = Color.white;
            Theme.Border(bubble, 2, Ink);
            Theme.Radius(bubble, 12);
            Theme.Padding(bubble, 5, 9);
            bubble.style.maxWidth = 130;
            bubble.style.top = compactRow ? -40 : 6;
            if (seat == 3) bubble.style.right = Length.Percent(90);
            else bubble.style.left = Length.Percent(90);
            bubble.style.display = DisplayStyle.None;
            bubbles[seat] = bubble;
            panel.Add(bubble);

            seatPanels[seat] = panel;
            return panel;
        }

        private VisualElement BuildCentre()
        {
            var centre = Theme.Column();
            centre.pickingMode = PickingMode.Ignore;
            centre.style.position = Position.Absolute;
            centre.style.left = 0;
            centre.style.right = 0;
            centre.style.bottom = 56;
            centre.style.alignItems = Align.Center;

            streetLabel = Theme.Label("", 14, HoldemPalette.LeafDark, bold: true);
            streetLabel.pickingMode = PickingMode.Ignore;
            centre.Add(streetLabel);

            var board = Theme.Row();
            board.pickingMode = PickingMode.Ignore;
            Theme.Margin(board, 4, 0, 6, 0);
            for (int i = 0; i < 5; i++)
            {
                var slot = new VisualElement();
                slot.pickingMode = PickingMode.Ignore;
                slot.style.width = 42;
                slot.style.height = 58;
                Theme.Margin(slot, 0, 2, 0, 2);
                boardSlots[i] = slot;
                board.Add(slot);
            }
            centre.Add(board);

            var pileRow = Theme.Row();
            pileRow.pickingMode = PickingMode.Ignore;
            pileHolder = new VisualElement();
            pileHolder.pickingMode = PickingMode.Ignore;
            pileHolder.style.minWidth = 40;
            pileHolder.style.height = 36;
            pileHolder.style.justifyContent = Justify.FlexEnd;
            pileRow.Add(pileHolder);
            pileLabel = Theme.Label("", 13, Ink, bold: true);
            pileLabel.pickingMode = PickingMode.Ignore;
            Theme.Margin(pileLabel, 0, 0, 0, 6);
            pileRow.Add(pileLabel);
            centre.Add(pileRow);
            return centre;
        }

        private VisualElement BuildBanner()
        {
            banner = Theme.Column();
            Theme.Radius(banner, 16);
            Theme.Border(banner, 3, Ink);
            Theme.Padding(banner, 6, 12);
            Theme.Margin(banner, 6, 0, 0, 0);
            bannerTitle = Theme.Label("", 18, Ink, bold: true);
            bannerDetail = Theme.Label("", 13, Ink);
            bannerDetail.style.minHeight = 34;
            banner.Add(bannerTitle);
            banner.Add(bannerDetail);
            return banner;
        }

        private VisualElement BuildHandArea()
        {
            var row = Theme.Row();
            row.style.alignItems = Align.Stretch;
            Theme.Margin(row, 6, 0, 0, 0);

            myCardsRow = Theme.Row();
            myCardsRow.style.width = 168;
            myCardsRow.style.height = 112;
            myCardsRow.style.alignItems = Align.Center;
            row.Add(myCardsRow);

            hintPanel = Theme.Column();
            hintPanel.style.flexGrow = 1;
            hintPanel.style.flexShrink = 1;
            hintPanel.style.backgroundColor = Cream;
            Theme.Border(hintPanel, 2, HoldemPalette.Wood);
            Theme.Radius(hintPanel, 14);
            Theme.Padding(hintPanel, 6, 8);
            Theme.Margin(hintPanel, 0, 0, 0, 6);
            hintHand = Theme.Label("", 14, Ink, bold: true);
            hintPanel.Add(hintHand);
            hintStars = Theme.Row();
            Theme.Margin(hintStars, 3, 0, 3, 0);
            hintPanel.Add(hintStars);
            hintAdvice = Theme.Label("", 12, Soft);
            hintPanel.Add(hintAdvice);
            row.Add(hintPanel);
            return row;
        }

        private VisualElement BuildActions()
        {
            actionRow = Theme.Row();
            Theme.Margin(actionRow, 8, 0, 12, 0);

            foldButton = ActionButton("Fold", OnFold, Theme.Hex("#E8E2F5"));
            stayButton = ActionButton("Check", OnStay, Theme.Hex("#9EE6A8"));
            raiseButton = ActionButton("Raise +5", OnRaise, HoldemPalette.Sun);
            nextButton = ActionButton("Next round!", OnNextRound, HoldemPalette.Sun);
            foldButton.tooltip = "Fold: sit out this round. You keep the rest of your coins.";
            stayButton.tooltip = "Check: stay in for free. Call: put in coins to match.";
            raiseButton.tooltip = "Raise: add more coins. Everyone else must match to stay in.";
            actionRow.Add(foldButton);
            actionRow.Add(stayButton);
            actionRow.Add(raiseButton);
            actionRow.Add(nextButton);
            return actionRow;
        }

        private static Button ActionButton(string text, Action onClick, Color color)
        {
            var button = new Button(onClick) { text = text };
            button.style.flexGrow = 1;
            button.style.flexBasis = 0;
            button.style.height = 58;
            button.style.fontSize = 17;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = Ink;
            button.style.backgroundColor = color;
            Theme.Border(button, 3, Ink);
            Theme.Radius(button, 18);
            Theme.Margin(button, 0, 3, 0, 3);
            Theme.Padding(button, 0, 4);
            Theme.FocusRing(button, Ink, Theme.Hex("#3D6BFF"));
            return button;
        }

        private static VisualElement Cover(Color color)
        {
            var cover = new VisualElement();
            cover.style.position = Position.Absolute;
            cover.style.left = 0;
            cover.style.right = 0;
            cover.style.top = 0;
            cover.style.bottom = 0;
            cover.style.backgroundColor = color;
            cover.style.justifyContent = Justify.Center;
            cover.style.display = DisplayStyle.None;
            Theme.Padding(cover, 20, 16);
            return cover;
        }

        // ---------- Moves ----------

        private bool CanAct(out int seat)
        {
            seat = match.State.CurrentSeat;
            return !busy && !resultsShown && !curtainOpen &&
                   match.State.Phase == Phase.Betting && IsHuman(seat) && seat == viewer;
        }

        private void OnFold()
        {
            if (CanAct(out int seat)) Perform(seat, HoldemMove.Fold);
        }

        private void OnStay()
        {
            if (!CanAct(out int seat)) return;
            Perform(seat, ViewOf(seat).CostToCall > 0 ? HoldemMove.Call : HoldemMove.Check);
        }

        private void OnRaise()
        {
            if (CanAct(out int seat)) Perform(seat, HoldemMove.Raise);
        }

        private void OnNextRound()
        {
            if (busy || resultsShown || match.State.Phase != Phase.RoundOver) return;
            Perform(Math.Max(0, viewer), HoldemMove.NextRound);
        }

        private void Perform(int seat, HoldemMove move)
        {
            var before = View;
            var result = match.TryPlay(seat, move);
            if (!result.Ok)
            {
                if (IsHuman(seat))
                {
                    bannerDetail.text = result.Message;
                    Fx.Pop(banner, 0.04f);
                    return;
                }
                // A computer friend should never be refused; if it is, it plays the safe move.
                Debug.LogWarning($"Treehouse Hold'em: seat {seat} tried {move}: {result.Message}");
                if (module.TryGetTimeoutAction(match.State, seat, out var safe) && module.Validate(match.State, seat, safe).Ok)
                {
                    Perform(seat, safe);
                }
                else
                {
                    var fallback = module.Validate(match.State, seat, HoldemMove.Fold).Ok ? HoldemMove.Fold : HoldemMove.Check;
                    if (module.Validate(match.State, seat, fallback).Ok) Perform(seat, fallback);
                }
                return;
            }

            var events = match.State.LastEvents;
            displayView = before;
            busy = true;
            if (move.Kind == MoveKind.NextRound)
            {
                // Clear the table first, then deal.
                displayView = null;
                roundSummary = "";
                dealing = true;
            }
            Refresh();
            Speak(events);

            long at = AnimateEvents(events, 0);
            Schedule(at + (Prefs.ReduceMotion ? 0 : 250), () => Finish(events));
        }

        private void Finish(IReadOnlyList<HoldemEvent> events)
        {
            displayView = null;
            busy = false;
            dealing = false;
            roundSummary = Summarize(events, roundSummary);
            Refresh();
            Celebrate(events);
            NextStep();
        }

        /// <summary>Decides what happens next: a computer friend's turn, the pass screen, or waiting.</summary>
        private void NextStep()
        {
            var state = match.State;
            if (state.IsOver)
            {
                screen.schedule.Execute(ShowResults).StartingIn(Prefs.ReduceMotion ? 300 : 1400);
                return;
            }
            if (state.Phase == Phase.RoundOver)
            {
                lastTurnShown = -1;
                if (PassAndPlay) viewer = -1;
                Refresh();
                return;
            }

            int seat = state.CurrentSeat;
            if (IsHuman(seat))
            {
                if (PassAndPlay && viewer != seat)
                {
                    viewer = -1;
                    Refresh();
                    ShowCurtain(seat);
                    return;
                }
                AnnounceTurn(seat);
                return;
            }

            lastTurnShown = -1;
            if (PassAndPlay && viewer >= 0)
            {
                viewer = -1;
                Refresh();
            }
            pendingBot?.Pause();
            pendingBot = screen.schedule.Execute(() =>
            {
                pendingBot = null;
                var current = match.State;
                if (current.Phase != Phase.Betting || current.CurrentSeat != seat) return;
                if (busy || rulesOpen)
                {
                    // Paused (e.g. the rules are open): try again shortly.
                    if (!rulesOpen) NextStep();
                    return;
                }
                Perform(seat, bots[seat].ChooseMove(module.ViewFor(current, seat)));
            }).StartingIn(BotDelayMs);
        }

        private void AnnounceTurn(int seat)
        {
            if (lastTurnShown == seat || rulesOpen) return;
            lastTurnShown = seat;
            Sfx.Play(Sound.YourTide);
            Toast(humans == 1 ? "Your turn!" : $"{names[seat]}'s turn!", HoldemPalette.Sun);
        }

        private void ShowCurtain(int seat)
        {
            curtain.Clear();
            curtain.style.display = DisplayStyle.Flex;
            curtainOpen = true;

            var card = Theme.Column();
            card.style.alignItems = Align.Center;
            card.style.backgroundColor = Cream;
            Theme.Border(card, 4, HoldemPalette.Wood);
            Theme.Radius(card, 26);
            Theme.Padding(card, 20, 16);

            var ring = new VisualElement();
            Theme.Circle(ring, 120);
            ring.style.backgroundColor = SeatColors[seat];
            Theme.Border(ring, 4, HoldemPalette.Wood);
            ring.style.alignItems = Align.Center;
            ring.style.justifyContent = Justify.Center;
            ring.Add(new AnimalArt(Animals[seat], 104));
            card.Add(ring);

            var title = Theme.Label($"Pass to {names[seat]}!", 28, Ink, bold: true);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(title, 10, 0, 4, 0);
            card.Add(title);
            var line = Theme.Label("Everyone else: no peeking at the secret cards!", 15, Soft);
            line.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(line);

            var go = Theme.PrimaryButton($"I'm {names[seat]}: show my cards", () =>
            {
                curtain.style.display = DisplayStyle.None;
                curtain.Clear();
                curtainOpen = false;
                viewer = seat;
                lastTurnShown = -1;
                Sfx.Play(Sound.Select);
                Refresh();
                AnnounceTurn(seat);
            });
            go.style.backgroundColor = HoldemPalette.Leaf;
            Theme.Border(go, 3, Ink);
            Theme.Margin(go, 16, 0, 0, 0);
            go.style.alignSelf = Align.Stretch;
            card.Add(go);
            curtain.Add(card);
            Fx.Pop(card, 0.06f, 0.3f);
            var leave = SmallButton("< Back to the lounge", LeaveTable, Color.white);
            Theme.Margin(leave, 10, 0, 0, 0);
            card.Add(leave);
            go.schedule.Execute(() => go.Focus()).StartingIn(50);
        }

        private void Rematch()
        {
            pendingBot?.Pause();
            pendingBot = null;
            match.Reset(module.Rematch(match.State, (ulong)DateTime.UtcNow.Ticks));
            resultsShown = false;
            results.style.display = DisplayStyle.None;
            results.Clear();
            roundSummary = "";
            lastTurnShown = -1;
            viewer = humans == 1 ? 0 : -1;
            for (int s = 0; s < Seats; s++)
            {
                bubbleTimers[s]?.Pause();
                bubbles[s].style.display = DisplayStyle.None;
            }
            busy = true;
            dealing = true;
            toastRound++;
            Refresh();
            long at = AnimateDeal(match.State.LastEvents, 0);
            Schedule(at + 300, () =>
            {
                busy = false;
                dealing = false;
                Refresh();
                NextStep();
            });
        }

        private void LeaveTable()
        {
            StopAll();
            app.ShowLounge();
        }

        private void StopAll()
        {
            pendingBot?.Pause();
            pendingBot = null;
        }

        private void ToggleHints()
        {
            Prefs.HoldemHints = !Prefs.HoldemHints;
            hintsButton.text = HintsLabel();
            Sfx.Play(Sound.Tap);
            Refresh();
        }

        private static string HintsLabel() => Prefs.HoldemHints ? "Hints on" : "Hints off";

        // ---------- Animation ----------

        private void Schedule(long delayMs, Action action)
        {
            if (delayMs <= 0 || Prefs.ReduceMotion)
            {
                action();
                return;
            }
            screen.schedule.Execute(action).StartingIn(delayMs);
        }

        private Vector2 Centre(VisualElement e)
        {
            if (e == null || e.panel == null || screen.panel == null) return Vector2.zero;
            return screen.WorldToLocal(e.worldBound.center);
        }

        private long AnimateEvents(IReadOnlyList<HoldemEvent> events, long at)
        {
            int boardIndex = View.Board.Count;
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case HoldemEventKind.Ante:
                    case HoldemEventKind.Called:
                    case HoldemEventKind.Raised:
                        if (e.Amount > 0)
                        {
                            int seat = e.Seat, amount = e.Amount;
                            Schedule(at, () => FlyCoins(amount, Centre(avatars[seat]), Centre(pileHolder)));
                            at += e.Kind == HoldemEventKind.Ante ? 60 : StaggerMs * 2;
                        }
                        break;
                    case HoldemEventKind.Dealt:
                    {
                        int seat = e.Seat;
                        Schedule(at, () => FlyBack(Centre(pileHolder), Centre(avatars[seat])));
                        at += StaggerMs;
                        break;
                    }
                    case HoldemEventKind.SharedCards:
                        foreach (int card in e.Cards)
                        {
                            int c = card;
                            int slot = Mathf.Min(boardIndex, 4);
                            boardIndex++;
                            Schedule(at, () => FlyFace(c, Centre(pileHolder), Centre(boardSlots[slot])));
                            at += StaggerMs * 2;
                        }
                        at += 250;
                        break;
                    case HoldemEventKind.WonCoins:
                    {
                        int seat = e.Seat, amount = e.Amount;
                        Schedule(at + 200, () => FlyCoins(amount, Centre(pileHolder), Centre(avatars[seat])));
                        at += 350;
                        break;
                    }
                }
            }
            return at;
        }

        private long AnimateDeal(IReadOnlyList<HoldemEvent> events, long at)
        {
            Sfx.Play(Sound.Shuffle);
            return AnimateEvents(events, at);
        }

        private void FlyCoins(int amount, Vector2 from, Vector2 to)
        {
            if (Prefs.ReduceMotion || screen.panel == null) return;
            Sfx.Play(Sound.Coins);
            var counts = CoinArt.Breakdown(amount);
            int shown = 0;
            for (int i = 0; i < counts.Length && shown < 3; i++)
            {
                for (int k = 0; k < counts[i] && shown < 3; k++, shown++)
                {
                    var coin = new CoinArt(HoldemPalette.CoinValues[i], 24);
                    Vector2 offset = new Vector2(shown * 6f, -shown * 4f);
                    Fly(coin, from + offset, to + offset, 24, 24, 0f, 30f, shown * 0.05f);
                }
            }
        }

        private void FlyFace(int card, Vector2 from, Vector2 to)
        {
            if (Prefs.ReduceMotion || screen.panel == null) return;
            Sfx.Play(Sound.Card);
            Fly(HoldemCardArt.Face(card, 42, 58), from, to, 42, 58, -25f, 40f, 0f);
        }

        private void FlyBack(Vector2 from, Vector2 to)
        {
            if (Prefs.ReduceMotion || screen.panel == null) return;
            Sfx.Play(Sound.Card);
            Fly(HoldemCardArt.Back(28, 40), from, to, 28, 40, 20f, 30f, 0f);
        }

        private void Fly(VisualElement item, Vector2 from, Vector2 to, float w, float h, float spin, float arc, float delaySeconds)
        {
            item.pickingMode = PickingMode.Ignore;
            item.style.position = Position.Absolute;
            item.style.left = from.x - w / 2f;
            item.style.top = from.y - h / 2f;
            item.style.opacity = delaySeconds > 0 ? 0f : 1f;
            screen.Add(item);

            float elapsed = -delaySeconds;
            item.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                if (elapsed < 0f) return;
                item.style.opacity = 1f;
                float t = Mathf.Clamp01(elapsed / FlySeconds);
                float eased = 1f - (1f - t) * (1f - t);
                float x = (to.x - from.x) * eased;
                float y = (to.y - from.y) * eased - Mathf.Sin(t * Mathf.PI) * arc;
                item.style.translate = new StyleTranslate(new Translate(x, y));
                item.style.rotate = new StyleRotate(new Rotate(new Angle(Mathf.Lerp(spin, 0f, eased), AngleUnit.Degree)));
                if (t >= 1f) item.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= FlySeconds);
        }

        private void Say(int seat, string text)
        {
            if (seat < 0 || seat >= Seats || string.IsNullOrEmpty(text)) return;
            var bubble = bubbles[seat];
            bubble.text = text;
            bubble.style.display = DisplayStyle.Flex;
            Fx.Pop(bubble, 0.12f);
            bubbleTimers[seat]?.Pause();
            bubbleTimers[seat] = bubble.schedule.Execute(() => bubble.style.display = DisplayStyle.None).StartingIn(2000);
        }

        /// <summary>Pop-up messages wait their turn so they never pile on top of each other.</summary>
        private void Toast(string text, Color color, long delayMs = 0)
        {
            float now = Time.realtimeSinceStartup;
            float start = Mathf.Max(now + delayMs / 1000f, toastFreeAt);
            toastFreeAt = start + 1.05f;
            long wait = (long)((start - now) * 1000f);
            int round = toastRound;
            Action show = () =>
            {
                if (round == toastRound && !resultsShown) Fx.Toast(screen, text, color, Ink, 1.3f);
            };
            if (wait <= 0) show();
            else screen.schedule.Execute(show).StartingIn(wait);
        }

        // ---------- Friendly words ----------

        /// <summary>Computer friends say what they do; people get a quick sound.</summary>
        private void Speak(IReadOnlyList<HoldemEvent> events)
        {
            foreach (var e in events)
            {
                bool friend = e.Seat >= 0 && !IsHuman(e.Seat);
                switch (e.Kind)
                {
                    case HoldemEventKind.Checked:
                        Sfx.Play(Sound.Tap);
                        if (friend) Say(e.Seat, "Check! I'll stay in.");
                        break;
                    case HoldemEventKind.Called:
                        if (friend) Say(e.Seat, $"I'll match {e.Amount}!");
                        break;
                    case HoldemEventKind.Raised:
                        Sfx.Play(Sound.Select);
                        if (friend) Say(e.Seat, $"Ooh! I'll add {e.Amount}!");
                        break;
                    case HoldemEventKind.Folded:
                        Sfx.Play(Sound.Drop);
                        if (friend) Say(e.Seat, "I'll sit this one out.");
                        break;
                    case HoldemEventKind.AllIn:
                        if (friend) Say(e.Seat, "All my coins!");
                        break;
                }
            }
        }

        private void Celebrate(IReadOnlyList<HoldemEvent> events)
        {
            long at = 0;
            bool quiet = match.State.IsOver; // the results card does the cheering
            bool showdown = false;
            var winners = new List<int>();
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case HoldemEventKind.SharedCards:
                        Fx.Pop(streetLabel, 0.25f, 0.4f);
                        break;
                    case HoldemEventKind.Showdown:
                        showdown = true;
                        Sfx.Play(Sound.Found);
                        break;
                    case HoldemEventKind.WonCoins:
                        winners.Add(e.Seat);
                        Fx.Pop(avatars[e.Seat], 0.25f, 0.45f);
                        if (!IsHuman(e.Seat)) Say(e.Seat, "Yay! Good game, everyone!");
                        break;
                    case HoldemEventKind.AllIn:
                        if (!quiet) Toast($"{names[e.Seat]} is all in!", HoldemPalette.Peach, at);
                        break;
                    case HoldemEventKind.OutOfCoins:
                        if (!quiet) Toast(IsHuman(e.Seat) ? $"{names[e.Seat]} can cheer from the side now!" : $"{names[e.Seat]} is out of coins!", Theme.Hex("#C8F0B8"), at);
                        break;
                    case HoldemEventKind.Folded:
                        if (IsHuman(e.Seat) && !quiet) Toast("Smart choice! You're back in next round.", Theme.Hex("#C8F0B8"), at);
                        break;
                }
            }

            if (winners.Count == 0) return;
            bool humanWon = false;
            foreach (int w in winners) humanWon |= IsHuman(w);
            if (humanWon)
            {
                Sfx.Play(Sound.Win);
                Fx.Confetti(screen, new[] { HoldemPalette.Sun, HoldemPalette.Leaf, Theme.Hex("#FF6F91"), Theme.Hex("#34B6F0"), Theme.Hex("#9B6BFF") });
                string who = winners.Count == 1 ? (humans == 1 && winners[0] == 0 ? "You" : names[winners[0]]) : "Winners";
                if (!quiet) Toast(winners.Count == 1 ? $"{who} won the prize pile!" : "It's a share! Everyone wins some!", HoldemPalette.Sun, at);
            }
            else if (showdown && humans == 1 && match.State.Revealed[0])
            {
                Sfx.Play(Sound.Pod);
                if (!quiet) Toast("Good try! Next round!", Theme.Hex("#C8F0B8"), at);
            }
            else
            {
                Sfx.Play(Sound.Pod);
            }
        }

        /// <summary>A sentence about who won the round, shown in the banner until the next round.</summary>
        private string Summarize(IReadOnlyList<HoldemEvent> events, string previous)
        {
            var parts = new List<string>();
            foreach (var e in events)
            {
                if (e.Kind != HoldemEventKind.WonCoins) continue;
                string who = humans == 1 && e.Seat == 0 ? "You win" : $"{names[e.Seat]} wins";
                if (e.Extra < 0) parts.Add($"{who} {e.Amount} coins: everyone else folded.");
                else parts.Add($"{who} {e.Amount} coins with {Article((HandCategory)e.Extra)}!");
            }
            return parts.Count > 0 ? string.Join(" ", parts) : previous;
        }

        private static string Article(HandCategory category)
        {
            string name = HandEvaluator.CategoryName(category);
            switch (category)
            {
                case HandCategory.Pair:
                case HandCategory.Straight:
                case HandCategory.Flush:
                case HandCategory.FullHouse:
                case HandCategory.StraightFlush:
                case HandCategory.RoyalFlush:
                    return "a " + name;
                default:
                    return name;
            }
        }

        // ---------- Drawing ----------

        private void Refresh()
        {
            var view = View;
            roundLabel.text = $"Round {view.RoundNumber} of {view.Rounds}";
            DrawSeats(view);
            DrawCentre(view);
            DrawBanner(view);
            DrawHand(view);
            DrawActions(view);
        }

        private bool ShowPrivate(HoldemView view) => viewer >= 0 && view.Viewer == viewer;

        private void DrawSeats(HoldemView view)
        {
            for (int s = 0; s < Seats; s++)
            {
                bool inRound = view.InRound[s];
                bool live = view.IsLive(s);
                bool turn = view.Phase == Phase.Betting && view.CurrentSeat == s && !busy;
                seatCoins[s].text = view.Coins[s].ToString();
                dealerBadges[s].style.display = view.Dealer == s ? DisplayStyle.Flex : DisplayStyle.None;

                string tag = " ";
                if (!inRound && view.Coins[s] == 0) tag = "cheering!";
                else if (view.Folded[s]) tag = "sitting out";
                else if (inRound && view.Coins[s] == 0 && view.Phase == Phase.Betting) tag = "all in!";
                else if (turn) tag = IsHuman(s) ? "your turn!" : "thinking...";
                seatTags[s].text = tag;

                Theme.BorderColor(avatars[s], turn ? HoldemPalette.Sun : HoldemPalette.Wood);
                avatars[s].style.borderTopWidth = turn ? 5 : 3;
                avatars[s].style.borderBottomWidth = turn ? 5 : 3;
                avatars[s].style.borderLeftWidth = turn ? 5 : 3;
                avatars[s].style.borderRightWidth = turn ? 5 : 3;
                seatPanels[s].style.opacity = live || view.Phase != Phase.Betting ? 1f : 0.55f;

                int bet = view.RoundBet[s];
                seatBets[s].text = bet > 0 ? $"+{bet}" : "";
                seatBets[s].style.display = bet > 0 ? DisplayStyle.Flex : DisplayStyle.None;

                var cards = seatCards[s];
                cards.Clear();
                if (!live) continue;
                var shown = view.CardsOf(s);
                bool reveal = view.Revealed[s] && shown.Count == 2;
                for (int i = 0; i < 2; i++)
                {
                    VisualElement c = reveal ? HoldemCardArt.Face(shown[i], 30, 42) : HoldemCardArt.Back(22, 32);
                    c.style.rotate = new StyleRotate(new Rotate(new Angle(i == 0 ? -7f : 7f, AngleUnit.Degree)));
                    if (i == 1) c.style.marginLeft = reveal ? -4 : -8;
                    cards.Add(c);
                }
            }
        }

        private void DrawCentre(HoldemView view)
        {
            switch (view.Board.Count)
            {
                case 0: streetLabel.text = view.Phase == Phase.Betting ? "Peek at your 2 secret cards!" : " "; break;
                case 3: streetLabel.text = "The Flop: 3 shared cards"; break;
                case 4: streetLabel.text = "The Turn: card number 4"; break;
                default: streetLabel.text = "The River: the last card!"; break;
            }

            HandValue best = default;
            bool glow = Prefs.HoldemHints && ShowPrivate(view) && view.TryGetMyBestHand(out best) && view.Board.Count > 0;
            for (int i = 0; i < 5; i++)
            {
                var slot = boardSlots[i];
                slot.Clear();
                if (i < view.Board.Count)
                {
                    var face = HoldemCardArt.Face(view.Board[i], 42, 58);
                    if (glow && Contains(best.Best, view.Board[i])) Glow(face);
                    slot.Add(face);
                }
                else
                {
                    slot.Add(HoldemCardArt.Slot(42, 58));
                }
            }

            pileHolder.Clear();
            pileHolder.Add(CoinArt.Pile(view.Pot, 22));
            pileLabel.text = view.Pot > 0 ? $"Prize pile: {view.Pot}" : "Prize pile";
        }

        private void DrawBanner(HoldemView view)
        {
            var state = match.State;
            if (view.IsOver || resultsShown)
            {
                banner.style.backgroundColor = HoldemPalette.Sun;
                bannerTitle.text = "Game night is over!";
                bannerDetail.text = roundSummary;
                return;
            }
            if (view.Phase == Phase.RoundOver)
            {
                banner.style.backgroundColor = Theme.Hex("#C8F0B8");
                bannerTitle.text = "Round over!";
                bannerDetail.text = string.IsNullOrEmpty(roundSummary) ? "Tap Next round when everyone is ready." : roundSummary;
                return;
            }
            if (busy)
            {
                banner.style.backgroundColor = Cream;
                bannerTitle.text = dealing ? "Dealing the cards..." : "Here it goes...";
                bannerDetail.text = " ";
                return;
            }

            int seat = state.CurrentSeat;
            if (IsHuman(seat) && seat == viewer)
            {
                var mine = ViewOf(seat);
                banner.style.backgroundColor = HoldemPalette.Sun;
                bannerTitle.text = humans == 1 ? "Your turn!" : $"{names[seat]}, your turn!";
                bannerDetail.text = mine.CostToCall > 0
                    ? $"Someone added coins. Call {mine.CostToCall} to match, " + (mine.CanRaise ? $"Raise to add {mine.RaiseSize} more, " : "") + "or Fold to sit out."
                    : "Check to stay in for free" + (mine.CanRaise ? $", or Raise to add {mine.RaiseSize} coins." : ".");
            }
            else if (IsHuman(seat))
            {
                banner.style.backgroundColor = Cream;
                bannerTitle.text = $"{names[seat]}'s turn";
                bannerDetail.text = "Pass the device!";
            }
            else
            {
                banner.style.backgroundColor = Cream;
                bannerTitle.text = $"{names[seat]}'s turn";
                bannerDetail.text = $"{names[seat]} is thinking...";
            }
        }

        private void DrawHand(HoldemView view)
        {
            myCardsRow.Clear();
            bool show = ShowPrivate(view) && view.MyCards.Count == 2;
            HandValue best = default;
            bool hints = Prefs.HoldemHints && show && view.TryGetMyBestHand(out best);

            if (show)
            {
                for (int i = 0; i < 2; i++)
                {
                    var face = HoldemCardArt.Face(view.MyCards[i], 76, 106);
                    face.style.rotate = new StyleRotate(new Rotate(new Angle(i == 0 ? -4f : 4f, AngleUnit.Degree)));
                    Theme.Margin(face, 0, 3, 0, 3);
                    if (hints && view.Board.Count > 0 && Contains(best.Best, view.MyCards[i])) Glow(face);
                    if (view.Folded[view.Viewer]) face.style.opacity = 0.5f;
                    myCardsRow.Add(face);
                }
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    var back = HoldemCardArt.Back(64, 90);
                    back.style.rotate = new StyleRotate(new Rotate(new Angle(i == 0 ? -4f : 4f, AngleUnit.Degree)));
                    Theme.Margin(back, 0, 4, 0, 4);
                    back.style.opacity = 0.8f;
                    myCardsRow.Add(back);
                }
            }

            hintStars.Clear();
            if (!show)
            {
                hintHand.text = PassAndPlay ? "Secret cards are hidden" : (view.InRound[0] ? "Your cards" : "You're cheering!");
                hintAdvice.text = PassAndPlay
                    ? "Each player sees their own cards when it's their turn."
                    : "Out of coins this game. Watch your friends and cheer!";
                return;
            }

            if (!Prefs.HoldemHints)
            {
                hintHand.text = $"Your coins: {view.Coins[view.Viewer]}";
                hintStars.Add(CoinArt.Pile(view.Coins[view.Viewer], 20, 8));
                hintAdvice.text = "Hints are off. Tap Hints to turn them on.";
                return;
            }

            hintHand.text = view.Board.Count == 0 && best.Category == HandCategory.HighCard
                ? $"{HoldemCards.RankWord(best.TopRank)} is your top card"
                : HandEvaluator.Describe(best);

            if (view.Folded[view.Viewer] || view.Phase != Phase.Betting)
            {
                hintAdvice.text = view.Folded[view.Viewer] ? "You're sitting out this round." : "Round over! Let's see the next one.";
                return;
            }

            int stars = StarsFor(view, out string advice);
            for (int i = 0; i < 5; i++)
            {
                var star = new FaceIcon(HoldemCards.Jack, 20);
                star.style.opacity = i < stars ? 1f : 0.2f;
                hintStars.Add(star);
            }
            hintStars.tooltip = $"Hand strength: {stars} of 5 stars";
            hintAdvice.text = advice;
        }

        /// <summary>1-5 stars for how strong your hand is against the players still in, plus friendly advice.</summary>
        private int StarsFor(HoldemView view, out string advice)
        {
            int opponents = Math.Max(1, view.LiveOpponents);
            string key = $"{view.MyCards[0]},{view.MyCards[1]}|{string.Join(",", view.Board)}|{opponents}";
            if (key != hintKey)
            {
                hintKey = key;
                hintChance = HoldemOdds.WinChance(view.MyCards, view.Board, opponents, hintRandom, 300);
            }
            float edge = hintChance * (opponents + 1);
            int stars = edge >= 2.0f ? 5 : edge >= 1.5f ? 4 : edge >= 1.0f ? 3 : edge >= 0.6f ? 2 : 1;
            bool early = view.Board.Count < 5;
            switch (stars)
            {
                case 5: advice = "Super strong! Raising is a great idea."; break;
                case 4: advice = "Strong hand! Raising or calling are both good."; break;
                case 3: advice = "Pretty good. Checking or calling is fine."; break;
                case 2: advice = early ? "Just okay so far. More cards are coming!" : "Just okay. Folding is fine if someone raises."; break;
                default: advice = early ? "Not much yet, but cards are still coming!" : "Not much this time. It's fine to fold if someone raises."; break;
            }
            return stars;
        }

        private void DrawActions(HoldemView view)
        {
            var state = match.State;
            bool roundOver = state.Phase == Phase.RoundOver && !busy && !resultsShown;
            nextButton.style.display = roundOver ? DisplayStyle.Flex : DisplayStyle.None;
            foldButton.style.display = roundOver ? DisplayStyle.None : DisplayStyle.Flex;
            stayButton.style.display = roundOver ? DisplayStyle.None : DisplayStyle.Flex;
            raiseButton.style.display = roundOver ? DisplayStyle.None : DisplayStyle.Flex;
            if (roundOver) return;

            bool mine = CanActNow();
            var me = mine ? ViewOf(state.CurrentSeat) : null;
            stayButton.text = me != null && me.CostToCall > 0 ? $"Call {Mathf.Min(me.CostToCall, state.Coins[state.CurrentSeat])}" : "Check";
            raiseButton.text = $"Raise +{HoldemModule.RaiseSizeFor(state.Street)}";
            Theme.SetButtonEnabled(foldButton, me != null && me.CanFold);
            Theme.SetButtonEnabled(stayButton, me != null && (me.CanCheck || me.CanCall));
            Theme.SetButtonEnabled(raiseButton, me != null && me.CanRaise);
        }

        private bool CanActNow() => CanAct(out _);

        private void ShowRules()
        {
            Sfx.Play(Sound.Tap);
            rules.Clear();
            rules.style.display = DisplayStyle.Flex;
            rulesOpen = true;

            var card = Theme.Column();
            card.style.flexGrow = 1;
            card.style.backgroundColor = Cream;
            Theme.Border(card, 4, HoldemPalette.Wood);
            Theme.Radius(card, 24);
            Theme.Padding(card, 12, 12);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            card.Add(scroll);

            scroll.Add(Heading("How to play"));
            string[] steps =
            {
                "1. Everyone gets 2 secret cards. Peek, but don't show anyone!",
                "2. Everyone puts 2 coins in the prize pile to start.",
                "3. Shared cards are turned over in the middle: 3 cards, then 1, then 1 more.",
                "4. On your turn: Check (stay in for free), Call (match the coins), Raise (add more coins), or Fold (sit out this round).",
                "5. Make your best hand of 5 cards using your 2 secret cards and the shared cards. The best hand wins the prize pile!",
                "6. After 8 rounds, whoever has the most coins wins game night. Coins are pretend: just for fun!"
            };
            foreach (string step in steps)
            {
                var line = Theme.Label(step, 14, Ink);
                Theme.Margin(line, 4, 0, 0, 0);
                scroll.Add(line);
            }

            scroll.Add(Heading("Coins"));
            var coins = Theme.Row();
            coins.style.flexWrap = Wrap.Wrap;
            foreach (int value in HoldemPalette.CoinValues)
            {
                var item = Theme.Row();
                Theme.Margin(item, 4, 12, 4, 0);
                item.Add(new CoinArt(value, 32));
                var label = Theme.Label(HoldemPalette.CoinName(value), 13, Ink, bold: true);
                Theme.Margin(label, 0, 0, 0, 4);
                item.Add(label);
                coins.Add(item);
            }
            scroll.Add(coins);

            scroll.Add(Heading("Best hands, strongest first"));
            AddRanking(scroll, "Royal Flush", "10, J, Q, K, A all in one suit. The very best!",
                C(14, Suit.Hearts), C(13, Suit.Hearts), C(12, Suit.Hearts), C(11, Suit.Hearts), C(10, Suit.Hearts));
            AddRanking(scroll, "Straight Flush", "5 in a row, all in one suit.",
                C(9, Suit.Clubs), C(8, Suit.Clubs), C(7, Suit.Clubs), C(6, Suit.Clubs), C(5, Suit.Clubs));
            AddRanking(scroll, "Four of a Kind", "4 cards with the same number.",
                C(9, Suit.Hearts), C(9, Suit.Diamonds), C(9, Suit.Clubs), C(9, Suit.Spades), C(3, Suit.Hearts));
            AddRanking(scroll, "Full House", "3 of one number and 2 of another.",
                C(8, Suit.Hearts), C(8, Suit.Clubs), C(8, Suit.Spades), C(4, Suit.Diamonds), C(4, Suit.Spades));
            AddRanking(scroll, "Flush", "5 cards of the same suit.",
                C(13, Suit.Diamonds), C(10, Suit.Diamonds), C(7, Suit.Diamonds), C(4, Suit.Diamonds), C(2, Suit.Diamonds));
            AddRanking(scroll, "Straight", "5 numbers in a row, like 4-5-6-7-8.",
                C(8, Suit.Spades), C(7, Suit.Hearts), C(6, Suit.Clubs), C(5, Suit.Diamonds), C(4, Suit.Hearts));
            AddRanking(scroll, "Three of a Kind", "3 cards with the same number.",
                C(7, Suit.Hearts), C(7, Suit.Clubs), C(7, Suit.Spades), C(13, Suit.Diamonds), C(2, Suit.Clubs));
            AddRanking(scroll, "Two Pairs", "2 different pairs.",
                C(11, Suit.Hearts), C(11, Suit.Spades), C(4, Suit.Clubs), C(4, Suit.Diamonds), C(9, Suit.Hearts));
            AddRanking(scroll, "Pair", "2 cards with the same number.",
                C(10, Suit.Clubs), C(10, Suit.Diamonds), C(14, Suit.Spades), C(6, Suit.Hearts), C(3, Suit.Clubs));
            AddRanking(scroll, "High Card", "No match? Your highest card counts.",
                C(14, Suit.Diamonds), C(11, Suit.Clubs), C(8, Suit.Hearts), C(5, Suit.Spades), C(2, Suit.Diamonds));

            var tip = Theme.Label("Tip: turn on Hints to see your best hand, glowing cards and a star meter.", 13, Soft);
            Theme.Margin(tip, 10, 0, 6, 0);
            scroll.Add(tip);

            var close = Theme.PrimaryButton("Got it!", () =>
            {
                rules.style.display = DisplayStyle.None;
                rules.Clear();
                rulesOpen = false;
                if (!busy) NextStep();
            });
            close.style.backgroundColor = HoldemPalette.Leaf;
            Theme.Border(close, 3, Ink);
            Theme.Margin(close, 8, 0, 0, 0);
            card.Add(close);
            rules.Add(card);
        }

        private static int C(int rank, Suit suit) => HoldemCards.Id(rank, suit);

        private static Label Heading(string text)
        {
            var label = Theme.Label(text, 20, HoldemPalette.LeafDark, bold: true);
            Theme.Margin(label, 12, 0, 2, 0);
            return label;
        }

        private static void AddRanking(VisualElement parent, string name, string explain, params int[] cards)
        {
            var box = Theme.Column();
            box.style.backgroundColor = Color.white;
            Theme.Border(box, 2, Theme.Hex("#E7DCC8"));
            Theme.Radius(box, 12);
            Theme.Padding(box, 6, 8);
            Theme.Margin(box, 6, 0, 0, 0);
            box.Add(Theme.Label(name, 15, Ink, bold: true));
            box.Add(Theme.Label(explain, 12, Soft));
            var row = Theme.Row();
            Theme.Margin(row, 4, 0, 0, 0);
            foreach (int card in cards)
            {
                var face = HoldemCardArt.Face(card, 36, 50);
                Theme.Margin(face, 0, 3, 0, 0);
                row.Add(face);
            }
            box.Add(row);
            parent.Add(box);
        }

        private void ShowResults()
        {
            if (resultsShown) return;
            resultsShown = true;
            Refresh();
            results.Clear();
            results.style.display = DisplayStyle.Flex;
            var state = match.State;

            var card = Theme.Column();
            card.style.backgroundColor = Cream;
            Theme.Border(card, 4, HoldemPalette.Wood);
            Theme.Radius(card, 26);
            Theme.Padding(card, 16, 14);

            var winners = state.Winners;
            bool humanWon = false;
            foreach (int w in winners) humanWon |= IsHuman(w);

            var hero = Theme.Row();
            hero.style.justifyContent = Justify.Center;
            foreach (int w in winners)
            {
                var ring = new VisualElement();
                Theme.Circle(ring, 96);
                ring.style.backgroundColor = SeatColors[w];
                Theme.Border(ring, 4, HoldemPalette.Sun);
                ring.style.alignItems = Align.Center;
                ring.style.justifyContent = Justify.Center;
                Theme.Margin(ring, 0, 4, 0, 4);
                ring.Add(new AnimalArt(Animals[w], 80));
                hero.Add(ring);
            }
            card.Add(hero);

            string title = winners.Count > 1
                ? "It's a tie! Shared champions!"
                : humans == 1 && winners[0] == 0 ? "You're the game night champion!" : $"{names[winners[0]]} is the champion!";
            var heading = Theme.Label(title, 24, HoldemPalette.LeafDark, bold: true);
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(heading, 8, 0, 0, 0);
            card.Add(heading);
            var cheer = Theme.Label("Great playing, everybody!", 15, Soft);
            cheer.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(cheer);

            var seats = new List<int> { 0, 1, 2, 3 };
            seats.Sort((a, b) => state.Coins[b] != state.Coins[a] ? state.Coins[b].CompareTo(state.Coins[a]) : a.CompareTo(b));
            int place = 0, lastCoins = -1;
            for (int i = 0; i < seats.Count; i++)
            {
                int seat = seats[i];
                if (state.Coins[seat] != lastCoins) place = i + 1;
                lastCoins = state.Coins[seat];
                var row = Theme.Row();
                row.style.backgroundColor = IsHuman(seat) ? Theme.Hex("#FFF1B8") : Color.white;
                Theme.Border(row, 2, Theme.Hex("#E7DCC8"));
                Theme.Radius(row, 14);
                Theme.Padding(row, 6, 10);
                Theme.Margin(row, i == 0 ? 12 : 6, 0, 0, 0);
                var placeLabel = Theme.Label(place.ToString(), 18, Ink, bold: true);
                placeLabel.style.width = 22;
                row.Add(placeLabel);
                row.Add(new AnimalArt(Animals[seat], 30));
                var name = Theme.Label(names[seat], 15, Ink, bold: true);
                Theme.Margin(name, 0, 0, 0, 6);
                name.style.flexGrow = 1;
                row.Add(name);
                row.Add(CoinArt.Pile(state.Coins[seat], 18, 5));
                var coins = Theme.Label(state.Coins[seat].ToString(), 15, Ink, bold: true);
                Theme.Margin(coins, 0, 0, 0, 6);
                row.Add(coins);
                card.Add(row);
            }

            var buttons = Theme.Row();
            Theme.Margin(buttons, 16, 0, 0, 0);
            var lounge = SmallButton("Lounge", LeaveTable, Color.white);
            lounge.style.flexGrow = 1;
            lounge.style.height = 56;
            Theme.Margin(lounge, 0, 8, 0, 0);
            var again = SmallButton("Play again!", Rematch, HoldemPalette.Sun);
            again.style.flexGrow = 2;
            again.style.height = 56;
            again.style.fontSize = 18;
            buttons.Add(lounge);
            buttons.Add(again);
            card.Add(buttons);
            results.Add(card);

            Sfx.Play(humanWon ? Sound.Win : Sound.Pod);
            Fx.Confetti(results, new[] { HoldemPalette.Sun, HoldemPalette.Leaf, Theme.Hex("#FF6F91"), Theme.Hex("#34B6F0"), Theme.Hex("#9B6BFF") }, 48, 2.2f);
            Fx.Pop(card, 0.08f, 0.4f);
        }

        // ---------- Helpers ----------

        private static void NoWrap(Label label, float maxWidth)
        {
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.maxWidth = maxWidth;
        }

        private static void Glow(VisualElement face)
        {
            Theme.Border(face, 4, HoldemPalette.Sun);
            face.style.translate = new StyleTranslate(new Translate(0, -4));
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }
}
