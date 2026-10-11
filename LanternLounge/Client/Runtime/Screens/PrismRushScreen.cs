using System;
using System.Collections.Generic;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.PrismRush;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>
    /// Prism Rush on one device: you sit at the bottom of a round table with three Prism Pal bots
    /// on the left, top and right. The draw deck and the played pile sit in the middle.
    /// The screen only reads your own <see cref="PrismView"/>; all rules come from PrismModule.
    /// </summary>
    internal sealed class PrismRushScreen
    {
        private const int Players = 4;
        private const int You = 0;
        private const long BotDelayMs = 1200;
        private const long BotPlayDrawnDelayMs = 750;
        private const float FlySeconds = 0.42f;
        private const long FlyStaggerMs = 130;

        // Seat 1 sits on your left, seat 2 across from you, seat 3 on your right, so play goes round
        // the table clockwise: you, left, top, right.
        private static readonly string[] Names = { "You", "Pip", "Nova", "Zig" };
        private static readonly Color[] BuddyColors =
        {
            Theme.Hex("#FFB547"), Theme.Hex("#B7A3FF"), Theme.Hex("#FF9E7A"), Theme.Hex("#6EDBC8")
        };

        private static readonly Color Sky = Theme.Hex("#1A1030");
        private static readonly Color Cream = Theme.Hex("#FFF4E0");
        private static readonly Color Mist = Theme.Hex("#C9BCE6");

        private readonly LoungeApp app;
        private readonly PrismModule module = new PrismModule();
        private readonly LocalMatch<PrismState, PrismMove, PrismView> match;

        // Your chosen cards, in the order you tapped them. The last one sets the next color.
        private readonly List<int> selection = new List<int>();
        private readonly Dictionary<int, Button> handButtons = new Dictionary<int, Button>();
        private readonly Dictionary<int, VisualElement> handFaces = new Dictionary<int, VisualElement>();
        private readonly List<int> pileShown = new List<int>();
        private HashSet<int> lastHand = new HashSet<int>();

        private PrismView displayView;
        private PrismView shownView;
        private bool busy;
        private bool resultsShown;
        private bool pickerOpen;
        private int round;
        private int lastSeatToAct = -1;
        private float lastHandWidth = -1f;
        private IVisualElementScheduledItem pendingBotMove;
        private IVisualElementScheduledItem idleMotion;
        private IVisualElementScheduledItem ringSpin;
        private float idleClock;
        private float ringAngle;

        private VisualElement screen;
        private VisualElement table;
        private readonly VisualElement[] seatPanels = new VisualElement[Players];
        private readonly GemBuddy[] buddies = new GemBuddy[Players];
        private readonly VisualElement[] buddyRings = new VisualElement[Players];
        private readonly VisualElement[] seatFans = new VisualElement[Players];
        private readonly Label[] seatCounts = new Label[Players];
        private readonly Label[] seatTags = new Label[Players];
        private readonly Label[] bubbles = new Label[Players];
        private readonly IVisualElementScheduledItem[] bubbleTimers = new IVisualElementScheduledItem[Players];

        private Button deckButton;
        private Label deckCount;
        private Button pileButton;
        private VisualElement colorRing;
        private DirectionRing directionRing;
        private PrismShape colorShape;
        private Label colorName;
        private Label pulseBadge;

        private VisualElement banner;
        private Label bannerTitle;
        private Label bannerDetail;
        private Button playButton;
        private Button drawButton;
        private Button keepButton;
        private ScrollView handScroll;
        private VisualElement handFan;
        private VisualElement overlay;
        private VisualElement picker;

        public PrismRushScreen(LoungeApp app)
        {
            this.app = app;
            match = new LocalMatch<PrismState, PrismMove, PrismView>(module, new MatchConfig(Players, (ulong)DateTime.UtcNow.Ticks));
            ResetPileMemory();
        }

        private PrismView MyView => module.ViewFor(match.State, You);

        /// <summary>What the table shows: your live view, or the moment before a move while its cards are flying.</summary>
        private PrismView View => displayView ?? MyView;

        public VisualElement Build()
        {
            screen = new VisualElement();
            screen.style.backgroundColor = Sky;
            screen.style.overflow = Overflow.Hidden;
            screen.Add(Fx.Bubbles(Theme.Hex("#5B3F8F")));

            var content = Theme.Column();
            content.style.flexGrow = 1;
            Theme.Padding(content, 0, 12);
            screen.Add(content);

            content.Add(BuildHeader());
            content.Add(BuildTable());
            content.Add(BuildBanner());
            content.Add(BuildActions());
            content.Add(BuildHand());

            picker = FullCover(new Color(0.06f, 0.03f, 0.12f, 0.88f));
            screen.Add(picker);
            overlay = FullCover(new Color(0.06f, 0.03f, 0.12f, 0.92f));
            screen.Add(overlay);

            StartTableMotion();
            screen.RegisterCallback<DetachFromPanelEvent>(_ => StopAll());

            lastSeatToAct = MyView.CurrentSeat;
            Refresh();
            Say(MyView.CurrentSeat == You ? -1 : MyView.CurrentSeat, MyView.CurrentSeat == You ? null : "I'll start!");
            if (MyView.IsMyTurn) screen.schedule.Execute(() => Toast("Your turn!", Theme.Amber)).StartingIn(400);
            ScheduleBotsIfNeeded();
            return screen;
        }

        // ---------- Layout ----------

        private VisualElement BuildHeader()
        {
            var header = Theme.Row();
            Theme.Margin(header, 12, 0, 0, 0);

            var back = Theme.SecondaryButton("< Lounge", LeaveTable);
            back.style.height = 44;
            back.style.fontSize = 14;
            Theme.Padding(back, 0, 12);
            header.Add(back);

            // The title in the four prism colors.
            var title = Theme.Row();
            Theme.Margin(title, 0, 0, 0, 12);
            string word = "Prism Rush";
            for (int i = 0; i < word.Length; i++)
            {
                var letter = Theme.Label(word[i].ToString(), 26, word[i] == ' ' ? Cream : PrismPalette.Of(i % 4), bold: true);
                letter.style.unityTextOutlineWidth = 1.5f;
                letter.style.unityTextOutlineColor = PrismPalette.Ink;
                if (word[i] == ' ') letter.style.width = 8;
                title.Add(letter);
            }
            header.Add(title);
            return header;
        }

        private VisualElement BuildTable()
        {
            table = new VisualElement();
            table.style.flexGrow = 1;
            table.style.minHeight = 290;
            Theme.Margin(table, 8, 0, 0, 0);

            // The round table itself.
            var felt = new VisualElement();
            felt.pickingMode = PickingMode.Ignore;
            felt.style.position = Position.Absolute;
            felt.style.left = 4;
            felt.style.right = 4;
            felt.style.top = 46;
            felt.style.bottom = 22;
            felt.style.backgroundColor = PrismPalette.Felt;
            Theme.Border(felt, 7, PrismPalette.FeltRim);
            Theme.Radius(felt, 150);
            var inner = new VisualElement();
            inner.pickingMode = PickingMode.Ignore;
            inner.style.position = Position.Absolute;
            inner.style.left = 10;
            inner.style.right = 10;
            inner.style.top = 10;
            inner.style.bottom = 10;
            Theme.Border(inner, 2, new Color(1f, 1f, 1f, 0.08f));
            Theme.Radius(inner, 140);
            felt.Add(inner);
            table.Add(felt);

            table.Add(BuildCentre());

            // Bots around the table.
            var top = new VisualElement();
            top.pickingMode = PickingMode.Ignore;
            top.style.position = Position.Absolute;
            top.style.left = 0;
            top.style.right = 0;
            top.style.top = 0;
            top.style.alignItems = Align.Center;
            top.Add(BuildSeat(2));
            table.Add(top);

            table.Add(SideColumn(1, left: true));
            table.Add(SideColumn(3, left: false));

            // Your own spot at the table edge.
            var you = new VisualElement();
            you.pickingMode = PickingMode.Ignore;
            you.style.position = Position.Absolute;
            you.style.left = 0;
            you.style.right = 0;
            you.style.bottom = 0;
            you.style.alignItems = Align.Center;
            you.Add(BuildYouChip());
            table.Add(you);
            return table;
        }

        private VisualElement SideColumn(int seat, bool left)
        {
            var column = new VisualElement();
            column.pickingMode = PickingMode.Ignore;
            column.style.position = Position.Absolute;
            column.style.top = 30;
            column.style.bottom = 0;
            column.style.width = 78;
            if (left) column.style.left = 0;
            else column.style.right = 0;
            column.style.justifyContent = Justify.Center;
            column.style.alignItems = Align.Center;
            column.Add(BuildSeat(seat));
            return column;
        }

        private VisualElement BuildSeat(int seat)
        {
            var panel = Theme.Column();
            panel.style.alignItems = Align.Center;
            panel.style.width = 78;
            panel.pickingMode = PickingMode.Ignore;

            var ring = new VisualElement();
            Theme.Circle(ring, 58);
            ring.style.backgroundColor = PrismPalette.Night;
            Theme.Border(ring, 3, PrismPalette.FeltRim);
            ring.style.alignItems = Align.Center;
            ring.style.justifyContent = Justify.Center;
            ring.pickingMode = PickingMode.Ignore;
            buddies[seat] = new GemBuddy(BuddyColors[seat], 46);
            ring.Add(buddies[seat]);
            buddyRings[seat] = ring;
            panel.Add(ring);

            var name = Theme.Label(Names[seat], 14, Cream, bold: true);
            name.pickingMode = PickingMode.Ignore;
            Theme.Margin(name, 2, 0, 0, 0);
            panel.Add(name);

            var fanRow = Theme.Row();
            fanRow.pickingMode = PickingMode.Ignore;
            fanRow.style.height = 30;
            Theme.Margin(fanRow, 2, 0, 0, 0);
            seatFans[seat] = fanRow;
            panel.Add(fanRow);

            seatCounts[seat] = Theme.Label("", 11, Mist, bold: true);
            seatCounts[seat].pickingMode = PickingMode.Ignore;
            panel.Add(seatCounts[seat]);

            seatTags[seat] = Theme.Label(" ", 11, PrismPalette.Glow, bold: true);
            seatTags[seat].pickingMode = PickingMode.Ignore;
            panel.Add(seatTags[seat]);

            // A speech bubble beside each bot.
            var bubble = Theme.Label("", 13, PrismPalette.Ink, bold: true);
            bubble.pickingMode = PickingMode.Ignore;
            bubble.style.position = Position.Absolute;
            bubble.style.backgroundColor = Cream;
            Theme.Border(bubble, 2, PrismPalette.Ink);
            Theme.Radius(bubble, 12);
            Theme.Padding(bubble, 5, 9);
            bubble.style.maxWidth = 120;
            bubble.style.top = 4;
            if (seat == 3) bubble.style.right = Length.Percent(92);
            else bubble.style.left = Length.Percent(92);
            bubble.style.display = DisplayStyle.None;
            bubbles[seat] = bubble;
            panel.Add(bubble);

            seatPanels[seat] = panel;
            return panel;
        }

        private VisualElement BuildYouChip()
        {
            var chip = Theme.Row();
            chip.pickingMode = PickingMode.Ignore;
            chip.style.backgroundColor = PrismPalette.Night;
            Theme.Border(chip, 3, PrismPalette.FeltRim);
            Theme.Radius(chip, 22);
            Theme.Padding(chip, 3, 10);

            var ring = new VisualElement();
            ring.pickingMode = PickingMode.Ignore;
            Theme.Circle(ring, 34);
            ring.style.alignItems = Align.Center;
            ring.style.justifyContent = Justify.Center;
            buddies[You] = new GemBuddy(BuddyColors[You], 30);
            ring.Add(buddies[You]);
            buddyRings[You] = chip;
            chip.Add(ring);

            seatCounts[You] = Theme.Label("", 13, Cream, bold: true);
            seatCounts[You].pickingMode = PickingMode.Ignore;
            Theme.Margin(seatCounts[You], 0, 4, 0, 6);
            chip.Add(seatCounts[You]);

            seatTags[You] = Theme.Label("", 12, PrismPalette.Glow, bold: true);
            seatTags[You].pickingMode = PickingMode.Ignore;
            chip.Add(seatTags[You]);
            seatPanels[You] = chip;
            return chip;
        }

        /// <summary>The middle of the table: the draw deck, and the played pile inside its color ring.</summary>
        private VisualElement BuildCentre()
        {
            var centre = Theme.Row();
            centre.pickingMode = PickingMode.Ignore;
            centre.style.position = Position.Absolute;
            centre.style.left = 0;
            centre.style.right = 0;
            centre.style.top = 40;
            centre.style.bottom = 20;
            centre.style.justifyContent = Justify.Center;

            // Draw deck: a little stack of backs you can tap.
            deckButton = new Button(OnDeckTap) { text = string.Empty };
            ClearButton(deckButton);
            deckButton.style.width = 64;
            deckButton.style.height = 98;
            deckButton.tooltip = "Draw deck: tap to draw a card";
            Theme.Radius(deckButton, 10);
            Theme.Border(deckButton, 2, PrismPalette.Clear);
            Theme.FocusRing(deckButton, PrismPalette.Clear, Cream);
            for (int i = 0; i < 3; i++)
            {
                var back = PrismCardArt.Back(56, 82);
                back.style.position = Position.Absolute;
                back.style.left = 2 + i * 2;
                back.style.top = 10 - i * 3;
                deckButton.Add(back);
            }
            deckCount = Theme.Label("", 12, PrismPalette.Ink, bold: true);
            deckCount.pickingMode = PickingMode.Ignore;
            deckCount.style.position = Position.Absolute;
            deckCount.style.right = -4;
            deckCount.style.bottom = -2;
            deckCount.style.backgroundColor = PrismPalette.Glow;
            Theme.Border(deckCount, 2, PrismPalette.Ink);
            Theme.Radius(deckCount, 10);
            Theme.Padding(deckCount, 1, 6);
            deckButton.Add(deckCount);
            Theme.Margin(deckButton, 0, 14, 0, 0);
            centre.Add(deckButton);

            // Played pile, framed by a ring in the color in play and arrows for the direction.
            var holder = new VisualElement();
            holder.pickingMode = PickingMode.Ignore;
            holder.style.width = 132;
            holder.style.height = 132;

            colorRing = new VisualElement();
            colorRing.pickingMode = PickingMode.Ignore;
            colorRing.style.position = Position.Absolute;
            colorRing.style.left = 8;
            colorRing.style.top = 8;
            Theme.Circle(colorRing, 116);
            Theme.Border(colorRing, 6, PrismPalette.Of(0));
            holder.Add(colorRing);

            directionRing = new DirectionRing(132);
            directionRing.style.position = Position.Absolute;
            directionRing.style.left = 0;
            directionRing.style.top = 0;
            holder.Add(directionRing);

            pileButton = new Button(OnPileTap) { text = string.Empty };
            ClearButton(pileButton);
            pileButton.style.position = Position.Absolute;
            pileButton.style.left = 30;
            pileButton.style.top = 22;
            pileButton.style.width = 72;
            pileButton.style.height = 92;
            pileButton.tooltip = "Played pile: tap to play your chosen cards";
            Theme.Radius(pileButton, 10);
            Theme.Border(pileButton, 2, PrismPalette.Clear);
            Theme.FocusRing(pileButton, PrismPalette.Clear, Cream);
            holder.Add(pileButton);

            var chip = Theme.Row();
            chip.pickingMode = PickingMode.Ignore;
            chip.style.position = Position.Absolute;
            chip.style.bottom = -14;
            chip.style.left = 0;
            chip.style.right = 0;
            chip.style.justifyContent = Justify.Center;
            var pill = Theme.Row();
            pill.pickingMode = PickingMode.Ignore;
            pill.style.backgroundColor = Cream;
            Theme.Border(pill, 2, PrismPalette.Ink);
            Theme.Radius(pill, 12);
            Theme.Padding(pill, 2, 8);
            colorShape = new PrismShape(0, 16);
            pill.Add(colorShape);
            colorName = Theme.Label("", 12, PrismPalette.Ink, bold: true);
            Theme.Margin(colorName, 0, 0, 0, 4);
            pill.Add(colorName);
            chip.Add(pill);
            holder.Add(chip);

            pulseBadge = Theme.Label("+2", 16, PrismPalette.Ink, bold: true);
            pulseBadge.pickingMode = PickingMode.Ignore;
            pulseBadge.style.position = Position.Absolute;
            pulseBadge.style.right = -6;
            pulseBadge.style.top = -2;
            pulseBadge.style.backgroundColor = PrismPalette.Glow;
            Theme.Border(pulseBadge, 3, PrismPalette.Ink);
            Theme.Radius(pulseBadge, 16);
            Theme.Padding(pulseBadge, 3, 9);
            pulseBadge.style.display = DisplayStyle.None;
            holder.Add(pulseBadge);

            centre.Add(holder);
            return centre;
        }

        private VisualElement BuildBanner()
        {
            banner = Theme.Column();
            Theme.Radius(banner, 16);
            Theme.Border(banner, 3, PrismPalette.Ink);
            Theme.Padding(banner, 6, 12);
            Theme.Margin(banner, 8, 0, 0, 0);
            bannerTitle = Theme.Label("", 19, PrismPalette.Ink, bold: true);
            bannerDetail = Theme.Label("", 13, PrismPalette.Ink);
            bannerDetail.style.minHeight = 34;
            banner.Add(bannerTitle);
            banner.Add(bannerDetail);
            return banner;
        }

        private VisualElement BuildActions()
        {
            var row = Theme.Row();
            Theme.Margin(row, 8, 0, 0, 0);

            playButton = Theme.PrimaryButton("Play!", PlaySelection);
            playButton.style.flexGrow = 2;
            playButton.style.flexBasis = 0;
            playButton.style.fontSize = 18;
            Theme.Margin(playButton, 0, 8, 0, 0);
            row.Add(playButton);

            drawButton = Theme.SecondaryButton("Draw", OnDeckTap);
            drawButton.style.flexGrow = 1;
            drawButton.style.flexBasis = 0;
            drawButton.style.height = 56;
            row.Add(drawButton);

            keepButton = Theme.SecondaryButton("Keep it", KeepDrawn);
            keepButton.style.flexGrow = 1;
            keepButton.style.flexBasis = 0;
            keepButton.style.height = 56;
            Theme.Margin(keepButton, 0, 0, 0, 8);
            row.Add(keepButton);
            return row;
        }

        private VisualElement BuildHand()
        {
            handScroll = new ScrollView(ScrollViewMode.Horizontal);
            handScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            handScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            handScroll.style.height = HandHeight;
            handScroll.style.flexShrink = 0;
            Theme.Margin(handScroll, 4, -12, 6, -12);

            handFan = new VisualElement();
            handFan.style.height = HandHeight;
            handScroll.Add(handFan);
            handScroll.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float width = handScroll.contentRect.width;
                if (!busy && Mathf.Abs(width - lastHandWidth) > 1f)
                {
                    lastHandWidth = width;
                    DrawHand(View);
                }
            });
            return handScroll;
        }

        private const float CardW = 70f;
        private const float CardH = 100f;
        private const float HandHeight = 166f;
        private const float HandTop = 48f;

        private static VisualElement FullCover(Color color)
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
            Theme.Padding(cover, 0, 20);
            return cover;
        }

        private static void ClearButton(Button button)
        {
            button.style.backgroundColor = PrismPalette.Clear;
            Theme.Border(button, 0, PrismPalette.Clear);
            Theme.Margin(button, 0, 0, 0, 0);
            Theme.Padding(button, 0, 0);
        }

        // ---------- Your moves ----------

        private bool CanAct => !busy && !resultsShown && !pickerOpen && MyView.IsMyTurn;

        /// <summary>Which cards may be tapped right now, given what is already chosen.</summary>
        private bool IsSelectable(PrismView view, int card)
        {
            if (!view.IsMyTurn || busy) return false;
            if (view.DrawnCard >= 0) return card == view.DrawnCard;
            if (selection.Count == 0) return view.CanPlay(card);
            var first = PrismCards.Card(selection[0]);
            var c = PrismCards.Card(card);
            return first.Kind == CardKind.Number && c.Kind == CardKind.Number && c.Number == first.Number;
        }

        private void ToggleCard(int card)
        {
            if (!CanAct) return;
            var view = MyView;
            if (selection.Contains(card))
            {
                selection.Remove(card);
                // A chain needs a playable card at its front; if none is left, start again.
                bool anyPlayable = false;
                foreach (int c in selection) anyPlayable |= view.CanPlay(c);
                if (!anyPlayable) selection.Clear();
                Sfx.Play(Sound.Tap);
            }
            else if (IsSelectable(view, card))
            {
                selection.Add(card);
                Sfx.Play(Sound.Select);
            }
            else if (view.DrawnCard < 0 && view.CanPlay(card))
            {
                // Changed your mind: start a new choice with this card.
                selection.Clear();
                selection.Add(card);
                Sfx.Play(Sound.Select);
            }
            else
            {
                Hint(view.DrawnCard >= 0
                    ? "You can only play the card you just drew, or keep it."
                    : view.PendingDraw > 0
                        ? "Only a Pulse can answer a Pulse. Or draw your cards."
                        : "That card doesn't match the color, number or action.");
                if (handButtons.TryGetValue(card, out var b)) Shake(b);
                return;
            }
            DrawHand(view);
            DrawControls(view);
            if (handButtons.TryGetValue(card, out var button))
            {
                button.Focus();
                Fx.Pop(button, 0.08f);
            }
        }

        /// <summary>The chosen cards in play order: a playable card first, the rest as tapped.</summary>
        private List<int> OrderedSelection(PrismView view)
        {
            var ordered = new List<int>();
            int lead = -1;
            foreach (int c in selection)
            {
                if (view.CanPlay(c))
                {
                    lead = c;
                    break;
                }
            }
            if (lead < 0) return ordered;
            ordered.Add(lead);
            foreach (int c in selection)
            {
                if (c != lead) ordered.Add(c);
            }
            return ordered;
        }

        private void PlaySelection()
        {
            if (!CanAct) return;
            var view = MyView;
            var cards = OrderedSelection(view);
            if (cards.Count == 0)
            {
                Hint(view.DrawnCard >= 0 ? "Tap the card you drew, then Play." : "Tap a glowing card first.");
                return;
            }
            if (PrismCards.Card(cards[cards.Count - 1]).Kind == CardKind.Prism)
            {
                ShowColorPicker(color => Perform(You, PrismMove.PlayCards(cards, color)));
                return;
            }
            Perform(You, PrismMove.PlayCards(cards));
        }

        private void OnDeckTap()
        {
            if (!CanAct)
            {
                if (!busy && !resultsShown && !MyView.IsMyTurn) Hint($"Wait for {Names[MyView.CurrentSeat]}.");
                return;
            }
            var view = MyView;
            if (view.DrawnCard >= 0)
            {
                Hint("You already drew. Play it or keep it.");
                return;
            }
            selection.Clear();
            Perform(You, PrismMove.Draw());
        }

        private void OnPileTap()
        {
            if (!CanAct) return;
            if (selection.Count == 0)
            {
                Hint("Tap a glowing card, then tap here or press Play.");
                Fx.Pop(pileButton, 0.05f);
                return;
            }
            PlaySelection();
        }

        private void KeepDrawn()
        {
            if (!CanAct || MyView.DrawnCard < 0) return;
            selection.Clear();
            Perform(You, PrismMove.Keep());
        }

        private void ShowColorPicker(Action<int> chosen)
        {
            picker.Clear();
            picker.style.display = DisplayStyle.Flex;
            pickerOpen = true;

            var card = Theme.Column();
            card.style.backgroundColor = PrismPalette.Night;
            Theme.Border(card, 4, PrismPalette.Glow);
            Theme.Radius(card, 24);
            Theme.Padding(card, 16, 14);

            var art = Theme.Row();
            art.style.justifyContent = Justify.Center;
            art.Add(new PrismIcon(CardKind.Prism, 70));
            card.Add(art);

            var title = Theme.Label("Pick the next color!", 24, Cream, bold: true);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(title);

            var grid = Theme.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            Theme.Margin(grid, 10, 0, 0, 0);
            int favourite = PrismBot.FavouriteColor(MyView.MyHand);
            for (int c = 0; c < PrismCards.ColorCount; c++)
            {
                int color = c;
                var button = new Button(() =>
                {
                    ClosePicker();
                    Sfx.Play(Sound.Select);
                    chosen(color);
                }) { text = string.Empty };
                button.style.width = 132;
                button.style.height = 88;
                button.style.backgroundColor = PrismPalette.Of(color);
                Theme.Border(button, 3, PrismPalette.Ink);
                Theme.FocusRing(button, PrismPalette.Ink, Cream);
                Theme.Radius(button, 18);
                Theme.Margin(button, 5, 5, 5, 5);
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.Center;
                var shape = new PrismShape(color, 40);
                button.Add(shape);
                var name = Theme.Label(PrismCards.ColorName(color), 16, PrismPalette.Ink, bold: true);
                name.pickingMode = PickingMode.Ignore;
                button.Add(name);
                button.tooltip = $"{PrismCards.ColorName(color)} ({PrismCards.ShapeName(color)})";
                grid.Add(button);
                if (color == favourite) button.schedule.Execute(() => button.Focus()).StartingIn(50);
            }
            card.Add(grid);

            var cancel = Theme.SecondaryButton("Not yet", ClosePicker);
            Theme.Margin(cancel, 8, 0, 0, 0);
            card.Add(cancel);

            picker.Add(card);
            Fx.Pop(card, 0.08f, 0.3f);
        }

        private void ClosePicker()
        {
            pickerOpen = false;
            picker.style.display = DisplayStyle.None;
            picker.Clear();
        }

        // ---------- Running a move ----------

        /// <summary>
        /// Plays a move for any seat, then animates it: the table keeps showing the moment before
        /// the move while the cards fly, and updates when they land.
        /// </summary>
        private void Perform(int seat, PrismMove move)
        {
            var before = MyView;
            var sources = new Dictionary<int, Vector2>();
            if (seat == You)
            {
                foreach (int card in move.Cards)
                {
                    if (handButtons.TryGetValue(card, out var b) && b.panel != null) sources[card] = screen.WorldToLocal(b.worldBound.center);
                }
            }

            var result = match.TryPlay(seat, move);
            if (!result.Ok)
            {
                if (seat == You)
                {
                    Hint(result.Message);
                    return;
                }
                // A bot should never be refused, but if it is, it plays the always-legal timeout
                // move instead so the table never freezes.
                Debug.LogWarning($"Prism Rush: bot {seat} tried an illegal move ({move}): {result.Message}");
                if (module.TryGetTimeoutAction(match.State, seat, out var fallback) && module.Validate(match.State, seat, fallback).Ok)
                {
                    Perform(seat, fallback);
                }
                return;
            }

            selection.Clear();
            var after = MyView;
            displayView = before;
            busy = true;

            // Played cards leave your hand at once and fly to the pile.
            if (seat == You)
            {
                foreach (int card in move.Cards)
                {
                    if (handButtons.TryGetValue(card, out var b)) b.style.visibility = Visibility.Hidden;
                }
            }
            DrawControls(before);

            long at = 0;
            foreach (var e in after.LastEvents)
            {
                switch (e.Kind)
                {
                    case PrismEventKind.Played:
                        Say(e.Seat, PlayLine(e));
                        foreach (int card in e.Cards)
                        {
                            Vector2 from = sources.TryGetValue(card, out var p) ? p : Centre(SeatAnchor(e.Seat));
                            int c = card;
                            Schedule(at, () => FlyFace(c, from, Centre(pileButton)));
                            at += FlyStaggerMs;
                        }
                        break;
                    case PrismEventKind.Drew:
                        Say(e.Seat, e.Seat == You ? null : "I'll draw one.");
                        at = FlyBacks(1, e.Seat, at);
                        break;
                    case PrismEventKind.PulsePenalty:
                        Say(e.Seat, e.Seat == You ? null : $"Aww, +{e.Count}!");
                        at = FlyBacks(e.Count, e.Seat, at);
                        break;
                    case PrismEventKind.Kept:
                        Say(e.Seat, "I'll keep it.");
                        break;
                    case PrismEventKind.NothingToDraw:
                        Say(e.Seat, e.Seat == You ? null : "No cards left!");
                        break;
                }
            }

            long landing = Prefs.ReduceMotion ? 0 : at + (long)(FlySeconds * 1000f);
            Schedule(landing, () => Finish(after));
        }

        private void Finish(PrismView after)
        {
            foreach (var e in after.LastEvents)
            {
                if (e.Kind == PrismEventKind.Played)
                {
                    foreach (int card in e.Cards) pileShown.Add(card);
                }
                else if (e.Kind == PrismEventKind.Reshuffled)
                {
                    pileShown.Clear();
                }
            }
            if (pileShown.Count == 0 || pileShown[pileShown.Count - 1] != after.TopCard) pileShown.Add(after.TopCard);
            while (pileShown.Count > 4) pileShown.RemoveAt(0);

            displayView = null;
            busy = false;
            if (after.DrawnCard >= 0)
            {
                selection.Clear();
                selection.Add(after.DrawnCard);
            }
            Refresh();
            Celebrate(after);
            ScheduleBotsIfNeeded();
        }

        private long FlyBacks(int count, int seat, long at)
        {
            Vector2 from = Centre(deckButton);
            Vector2 to = seat == You ? Centre(handScroll) : Centre(SeatAnchor(seat));
            int shown = Mathf.Min(count, 8);
            for (int i = 0; i < shown; i++)
            {
                Schedule(at, () => FlyBack(from, to));
                at += FlyStaggerMs;
            }
            return at;
        }

        private void Schedule(long delayMs, Action action)
        {
            // With Reduce motion on, nothing flies (Fly does nothing), so everything happens at once.
            if (delayMs <= 0 || Prefs.ReduceMotion)
            {
                action();
                return;
            }
            screen.schedule.Execute(action).StartingIn(delayMs);
        }

        private void ScheduleBotsIfNeeded()
        {
            var state = match.State;
            if (busy || state.IsOver || state.CurrentSeat == You) return;

            pendingBotMove?.Pause();
            long delay = state.DrawnCard >= 0 ? BotPlayDrawnDelayMs : BotDelayMs;
            pendingBotMove = screen.schedule.Execute(() =>
            {
                pendingBotMove = null;
                var current = match.State;
                if (busy || current.IsOver || current.CurrentSeat == You) return;
                int seat = current.CurrentSeat;
                Perform(seat, PrismBot.ChooseMove(module.ViewFor(current, seat)));
            }).StartingIn(delay);
        }

        private void Rematch()
        {
            pendingBotMove?.Pause();
            pendingBotMove = null;
            match.Reset(module.Rematch(match.State, (ulong)DateTime.UtcNow.Ticks));
            selection.Clear();
            lastHand.Clear();
            displayView = null;
            busy = false;
            resultsShown = false;
            overlay.style.display = DisplayStyle.None;
            overlay.Clear();
            ClosePicker();
            round++;
            for (int s = 1; s < Players; s++)
            {
                bubbleTimers[s]?.Pause();
                bubbles[s].style.display = DisplayStyle.None;
            }
            ResetPileMemory();
            lastSeatToAct = MyView.CurrentSeat;
            Sfx.Play(Sound.Shuffle);
            Refresh();
            if (MyView.IsMyTurn) Toast("Your turn!", Theme.Amber);
            else Say(MyView.CurrentSeat, "I'll start!");
            ScheduleBotsIfNeeded();
        }

        private void ResetPileMemory()
        {
            pileShown.Clear();
            pileShown.Add(match.State.TopCard);
        }

        private void LeaveTable()
        {
            StopAll();
            app.ShowLounge();
        }

        private void StopAll()
        {
            pendingBotMove?.Pause();
            pendingBotMove = null;
            idleMotion?.Pause();
            ringSpin?.Pause();
        }

        // ---------- Fun: flying cards, bubbles, pop-ups ----------

        private VisualElement SeatAnchor(int seat) => seat == You ? (VisualElement)handScroll : buddyRings[seat];

        private Vector2 Centre(VisualElement e)
        {
            if (e == null || e.panel == null) return Vector2.zero;
            return screen.WorldToLocal(e.worldBound.center);
        }

        private void FlyFace(int card, Vector2 from, Vector2 to)
        {
            Sfx.Play(Sound.Card);
            var face = PrismCardArt.Face(card, 58, 83);
            float spin = (card * 37 % 21) - 10;
            Fly(face, from, to, 58, 83, 1f, 1.1f, spin, 50f);
        }

        private void FlyBack(Vector2 from, Vector2 to)
        {
            Sfx.Play(Sound.Card);
            Fly(PrismCardArt.Back(40, 58), from, to, 40, 58, 1f, 0.75f, 0f, 36f);
        }

        private void Fly(VisualElement card, Vector2 from, Vector2 to, float w, float h, float scaleFrom, float scaleTo, float spinTo, float arc)
        {
            if (Prefs.ReduceMotion || screen.panel == null) return;
            card.pickingMode = PickingMode.Ignore;
            card.style.position = Position.Absolute;
            card.style.left = from.x - w / 2f;
            card.style.top = from.y - h / 2f;
            screen.Add(card);

            float elapsed = 0f;
            card.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / FlySeconds);
                float eased = 1f - (1f - t) * (1f - t);
                float x = (to.x - from.x) * eased;
                float y = (to.y - from.y) * eased - Mathf.Sin(t * Mathf.PI) * arc;
                card.style.translate = new StyleTranslate(new Translate(x, y));
                float s = Mathf.Lerp(scaleFrom, scaleTo, t);
                card.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                card.style.rotate = new StyleRotate(new Rotate(new Angle(Mathf.Lerp(-20f, spinTo, eased), AngleUnit.Degree)));
                if (t >= 1f) card.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= FlySeconds);
        }

        /// <summary>A bot says something in its speech bubble for a couple of seconds.</summary>
        private void Say(int seat, string text)
        {
            if (seat <= You || seat >= Players || string.IsNullOrEmpty(text)) return;
            var bubble = bubbles[seat];
            bubble.text = text;
            bubble.style.display = DisplayStyle.Flex;
            Fx.Pop(bubble, 0.12f);
            bubbleTimers[seat]?.Pause();
            bubbleTimers[seat] = bubble.schedule.Execute(() => bubble.style.display = DisplayStyle.None).StartingIn(2200);
        }

        private void Toast(string text, Color color, long delayMs = 0)
        {
            if (delayMs <= 0)
            {
                Fx.Toast(screen, text, color, PrismPalette.Ink, 1.2f);
                return;
            }
            int round = this.round;
            screen.schedule.Execute(() =>
            {
                if (round == this.round) Fx.Toast(screen, text, color, PrismPalette.Ink, 1.2f);
            }).StartingIn(delayMs);
        }

        private void Hint(string text)
        {
            bannerDetail.text = text;
            Fx.Pop(banner, 0.04f);
        }

        private static void Shake(VisualElement e)
        {
            if (Prefs.ReduceMotion) return;
            float elapsed = 0f;
            e.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / 0.3f);
                float dx = Mathf.Sin(t * Mathf.PI * 6f) * 6f * (1f - t);
                e.style.translate = new StyleTranslate(new Translate(dx, 0));
            }).Every(16).Until(() => elapsed >= 0.3f);
        }

        private void Celebrate(PrismView view)
        {
            long at = 0;
            foreach (var e in view.LastEvents)
            {
                switch (e.Kind)
                {
                    case PrismEventKind.Flipped:
                        Sfx.Play(Sound.Flip);
                        Toast("Flip!", PrismPalette.Of(view.ActiveColor), at);
                        at += 800;
                        if (directionRing != null) Fx.Pop(directionRing, 0.15f, 0.4f);
                        break;
                    case PrismEventKind.Hopped:
                        Sfx.Play(Sound.Select);
                        Toast(e.Seat == You ? "Hop! You're skipped" : $"Hop! {Names[e.Seat]} skips", PrismPalette.Of(view.ActiveColor), at);
                        at += 800;
                        Say(e.Seat, "Hey!");
                        break;
                    case PrismEventKind.Pulsed:
                        Sfx.Play(Sound.Zap);
                        Toast(e.Seat == You ? $"Pulse! You owe +{e.Count}" : $"Pulse! +{e.Count}", PrismPalette.Glow, at);
                        at += 800;
                        Fx.Pop(pulseBadge, 0.25f, 0.4f);
                        break;
                    case PrismEventKind.Reshuffled:
                        Sfx.Play(Sound.Shuffle);
                        Toast("Shuffle!", Mist, at);
                        at += 700;
                        break;
                    case PrismEventKind.LastLight:
                        Sfx.Play(Sound.YourTide);
                        Toast(e.Seat == You ? "Last light! One card!" : $"Last light! {Names[e.Seat]} has 1!", PrismPalette.Glow, at);
                        at += 900;
                        Say(e.Seat, "Last light!");
                        break;
                    case PrismEventKind.Played:
                        Fx.Pop(pileButton, 0.12f, 0.3f);
                        if (e.Seat != You) Fx.Pop(buddies[e.Seat], 0.2f, 0.3f);
                        break;
                }
            }

            if (view.IsOver) return; // The results screen has its own fanfare.

            if (view.IsMyTurn && view.DrawnCard >= 0)
            {
                Sfx.Play(Sound.Found);
                Toast("It matches!", PrismPalette.Glow, at);
            }
            else if (view.IsMyTurn && lastSeatToAct != You)
            {
                screen.schedule.Execute(() => Sfx.Play(Sound.YourTide)).StartingIn(at + 1);
                Toast(view.PendingDraw > 0 ? $"Your turn! +{view.PendingDraw} coming" : "Your turn!", Theme.Amber, at);
            }
            lastSeatToAct = view.CurrentSeat;
        }

        /// <summary>The table's idle life: arrows drift round the pile and playable cards bob gently.</summary>
        private void StartTableMotion()
        {
            ringSpin = screen.schedule.Execute(timer =>
            {
                if (Prefs.ReduceMotion || directionRing == null) return;
                if (shownView == null) return;
                ringAngle += shownView.Direction * timer.deltaTime / 1000f * 25f;
                directionRing.style.rotate = new StyleRotate(new Rotate(new Angle(ringAngle, AngleUnit.Degree)));
            }).Every(33);

            idleMotion = screen.schedule.Execute(timer =>
            {
                idleClock += timer.deltaTime / 1000f;
                if (busy || shownView == null) return;
                var view = shownView;
                bool myTurn = view.IsMyTurn;
                float glow = 0.5f + 0.5f * Mathf.Sin(idleClock * 5f);

                foreach (var pair in handFaces)
                {
                    bool selected = selection.Contains(pair.Key);
                    if (!selected && myTurn && IsSelectable(view, pair.Key))
                    {
                        PrismCardArt.Highlight(pair.Value, Color.Lerp(PrismPalette.Glow, Color.white, glow), 4f);
                    }
                }
                for (int s = 0; s < Players; s++)
                {
                    bool turn = !view.IsOver && view.CurrentSeat == s;
                    Theme.BorderColor(buddyRings[s], turn ? Color.Lerp(PrismPalette.Glow, Theme.Amber, glow) : PrismPalette.FeltRim);
                }
                if (view.PendingDraw > 0 && !Prefs.ReduceMotion)
                {
                    float s = 1f + 0.08f * glow;
                    pulseBadge.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                }
            }).Every(50);
        }

        // ---------- Drawing ----------

        private void Refresh()
        {
            var view = View;
            shownView = view;
            DrawCentre(view);
            DrawSeats(view);
            DrawBanner(view);
            DrawControls(view);
            DrawHand(view);

            if (view.IsOver) ShowResults(view);
        }

        private void DrawCentre(PrismView view)
        {
            deckCount.text = view.DrawPileCount.ToString();
            deckButton.style.opacity = view.DrawPileCount == 0 ? 0.45f : 1f;

            pileButton.Clear();
            for (int i = 0; i < pileShown.Count; i++)
            {
                int card = pileShown[i];
                var face = PrismCardArt.Face(card, 64, 92);
                face.style.position = Position.Absolute;
                face.style.left = 4;
                face.style.top = 0;
                float tilt = i == pileShown.Count - 1 ? (card * 37 % 13) - 6 : (card * 53 % 31) - 15;
                face.style.rotate = new StyleRotate(new Rotate(new Angle(tilt, AngleUnit.Degree)));
                face.style.translate = new StyleTranslate(new Translate((card * 7 % 9) - 4, (card * 11 % 7) - 3));
                pileButton.Add(face);
            }

            Theme.BorderColor(colorRing, PrismPalette.Of(view.ActiveColor));
            colorRing.style.backgroundColor = new Color(PrismPalette.Of(view.ActiveColor).r, PrismPalette.Of(view.ActiveColor).g, PrismPalette.Of(view.ActiveColor).b, 0.16f);
            colorShape.ShapeColor = view.ActiveColor;
            colorName.text = PrismCards.ColorName(view.ActiveColor);
            directionRing.Direction = view.Direction;
            pileButton.tooltip = (view.Direction > 0 ? "Play goes clockwise. " : "Play goes anticlockwise. ") +
                $"Top card: {PrismCardArt.Describe(view.TopCard)}. Color in play: {PrismCards.ColorName(view.ActiveColor)}. Tap to play your chosen cards.";

            pulseBadge.text = $"+{view.PendingDraw}";
            pulseBadge.style.display = view.PendingDraw > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void DrawSeats(PrismView view)
        {
            for (int s = 0; s < Players; s++)
            {
                int cards = view.HandCounts[s];
                bool turn = !view.IsOver && view.CurrentSeat == s;
                seatCounts[s].text = s == You ? $"You · {cards}" : $"{cards} {(cards == 1 ? "card" : "cards")}";
                seatTags[s].text = turn ? (s == You ? "your turn" : "thinking...") : cards == 1 ? "last light!" : " ";
                buddies[s].Face = turn ? GemBuddy.Mood.Thinking : cards == 1 ? GemBuddy.Mood.Wow : GemBuddy.Mood.Happy;

                if (s == You) continue;
                var fan = seatFans[s];
                fan.Clear();
                int shown = Mathf.Min(cards, 6);
                for (int i = 0; i < shown; i++)
                {
                    var back = PrismCardArt.Back(18, 27);
                    float t = shown <= 1 ? 0f : i / (shown - 1f) * 2f - 1f;
                    back.style.marginLeft = i == 0 ? 0 : -9;
                    back.style.rotate = new StyleRotate(new Rotate(new Angle(t * 14f, AngleUnit.Degree)));
                    back.style.translate = new StyleTranslate(new Translate(0, t * t * 3f));
                    fan.Add(back);
                }
            }
        }

        private void DrawBanner(PrismView view)
        {
            if (view.IsOver)
            {
                banner.style.backgroundColor = PrismPalette.Glow;
                bannerTitle.text = "Round over!";
                bannerDetail.text = " ";
            }
            else if (busy)
            {
                banner.style.backgroundColor = Mist;
                bannerTitle.text = view.IsMyTurn ? "Here it goes!" : $"{Names[view.CurrentSeat]}'s turn";
                bannerDetail.text = " ";
            }
            else if (view.IsMyTurn)
            {
                banner.style.backgroundColor = Theme.Amber;
                if (view.DrawnCard >= 0)
                {
                    bannerTitle.text = $"You drew {PrismCards.Name(view.DrawnCard)}!";
                    bannerDetail.text = "It matches! Play it now, or keep it.";
                }
                else if (view.PendingDraw > 0)
                {
                    bannerTitle.text = $"Pulse! +{view.PendingDraw}";
                    bannerDetail.text = HasPulse(view) ? "Play a Pulse to pass it on, or draw the cards." : $"No Pulse to answer with. Draw {view.PendingDraw}.";
                }
                else
                {
                    bannerTitle.text = "Your turn!";
                    bannerDetail.text = AnyPlayable(view)
                        ? "Match the color, number or picture. Same numbers chain!"
                        : "Nothing matches. Tap the deck to draw one.";
                }
            }
            else
            {
                banner.style.backgroundColor = Mist;
                bannerTitle.text = $"{Names[view.CurrentSeat]}'s turn";
                bannerDetail.text = view.DrawnCard >= 0 ? "They drew a card they can play..." : $"{Names[view.CurrentSeat]} is thinking...";
            }
        }

        private void DrawControls(PrismView view)
        {
            bool myTurn = view.IsMyTurn && !busy && !view.IsOver;
            var ordered = OrderedSelection(view);

            if (ordered.Count == 0) playButton.text = "Play!";
            else if (ordered.Count == 1) playButton.text = $"Play {PrismCards.Name(ordered[0])}";
            else playButton.text = $"Play {ordered.Count} cards!";
            Theme.SetButtonEnabled(playButton, myTurn && ordered.Count > 0);

            bool drawn = view.DrawnCard >= 0;
            drawButton.style.display = drawn ? DisplayStyle.None : DisplayStyle.Flex;
            keepButton.style.display = drawn ? DisplayStyle.Flex : DisplayStyle.None;
            drawButton.text = view.PendingDraw > 0 ? $"Take +{view.PendingDraw}" : "Draw";
            Theme.SetButtonEnabled(drawButton, myTurn && !drawn);
            Theme.SetButtonEnabled(keepButton, myTurn && drawn);
            deckButton.SetEnabled(myTurn && !drawn);
        }

        private void DrawHand(PrismView view)
        {
            if (handFan == null) return;
            handFan.Clear();
            handButtons.Clear();
            handFaces.Clear();

            var hand = new List<int>(view.MyHand);
            hand.Sort(CompareForHand);
            int n = hand.Count;

            float avail = handScroll.contentRect.width;
            if (float.IsNaN(avail) || avail < 100f) avail = 360f;
            const float edge = 18f;
            float spacing = n <= 1 ? 0f : Mathf.Clamp((avail - edge * 2f - CardW) / (n - 1), 30f, 56f);
            float total = CardW + spacing * Mathf.Max(0, n - 1);
            float width = Mathf.Max(avail, total + edge * 2f);
            handFan.style.width = width;
            float start = (width - total) / 2f;
            float maxAngle = Mathf.Min(14f, n * 1.8f);

            var newCards = new List<Button>();
            for (int i = 0; i < n; i++)
            {
                int card = hand[i];
                float t = n <= 1 ? 0f : i / (n - 1f) * 2f - 1f;
                bool selected = selection.Contains(card);
                bool selectable = IsSelectable(view, card);
                bool dimmed = view.IsMyTurn && !busy && !selectable && !selected;

                float lift = selected ? 30f : selectable ? 12f : 0f;
                float y = HandTop + t * t * 10f - lift;

                var button = new Button(() => ToggleCard(card)) { text = string.Empty };
                ClearButton(button);
                button.style.position = Position.Absolute;
                button.style.left = start + i * spacing;
                button.style.top = y;
                button.style.width = CardW;
                button.style.height = CardH;
                button.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(50), Length.Percent(100), 0));
                button.style.rotate = new StyleRotate(new Rotate(new Angle(t * maxAngle, AngleUnit.Degree)));
                button.tooltip = PrismCardArt.Describe(card);
                button.focusable = true;

                var face = PrismCardArt.Face(card, CardW, CardH);
                if (selected) PrismCardArt.Highlight(face, Color.white, 4f);
                else if (selectable) PrismCardArt.Highlight(face, PrismPalette.Glow, 4f);
                face.style.opacity = dimmed ? 0.6f : 1f;
                button.Add(face);

                // Chosen cards show their order; the last one sets the next color.
                int order = selection.IndexOf(card);
                if (order >= 0 && selection.Count > 1) button.Add(Badge((order + 1).ToString(), Cream));
                if (card == view.DrawnCard) button.Add(Badge("NEW", PrismPalette.Glow));

                // Mouse users get a little lift when hovering.
                float restTop = y;
                button.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (!Prefs.ReduceMotion) button.style.top = restTop - 6f;
                });
                button.RegisterCallback<PointerLeaveEvent>(_ => button.style.top = restTop);
                button.RegisterCallback<FocusInEvent>(_ => Theme.BorderColor(face, Cream));
                button.RegisterCallback<FocusOutEvent>(_ =>
                    Theme.BorderColor(face, selection.Contains(card) ? Color.white : IsSelectable(View, card) ? PrismPalette.Glow : PrismPalette.Ink));

                handButtons[card] = button;
                handFaces[card] = face;
                handFan.Add(button);
                if (!lastHand.Contains(card)) newCards.Add(button);
            }

            if (n == 0)
            {
                var none = Theme.Label("No cards!", 16, Mist, bold: true);
                none.style.position = Position.Absolute;
                none.style.left = 20;
                none.style.top = HandTop + 30;
                handFan.Add(none);
            }

            foreach (var b in newCards) Fx.Pop(b, 0.2f, 0.35f);
            lastHand = new HashSet<int>(view.MyHand);
        }

        private static VisualElement Badge(string text, Color color)
        {
            var badge = Theme.Label(text, 11, PrismPalette.Ink, bold: true);
            badge.pickingMode = PickingMode.Ignore;
            badge.style.position = Position.Absolute;
            badge.style.top = -8;
            badge.style.right = -6;
            badge.style.backgroundColor = color;
            Theme.Border(badge, 2, PrismPalette.Ink);
            Theme.Radius(badge, 10);
            Theme.Padding(badge, 1, 6);
            return badge;
        }

        /// <summary>Hand order: by color, then numbers low to high, then actions; Prisms last.</summary>
        private static int CompareForHand(int a, int b)
        {
            var x = PrismCards.Card(a);
            var y = PrismCards.Card(b);
            int cx = x.IsWild ? 99 : x.Color;
            int cy = y.IsWild ? 99 : y.Color;
            if (cx != cy) return cx.CompareTo(cy);
            int kx = x.Kind == CardKind.Number ? x.Number : 10 + (int)x.Kind;
            int ky = y.Kind == CardKind.Number ? y.Number : 10 + (int)y.Kind;
            if (kx != ky) return kx.CompareTo(ky);
            return a.CompareTo(b);
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        private static bool HasPulse(PrismView view)
        {
            foreach (int card in view.MyHand)
            {
                if (PrismCards.Card(card).Kind == CardKind.Pulse) return true;
            }
            return false;
        }

        private static bool AnyPlayable(PrismView view)
        {
            foreach (int card in view.MyHand)
            {
                if (view.CanPlay(card)) return true;
            }
            return false;
        }

        private void ShowResults(PrismView view)
        {
            if (resultsShown) return;
            resultsShown = true;
            overlay.Clear();
            overlay.style.display = DisplayStyle.Flex;
            overlay.Add(Fx.Bubbles(PrismPalette.Glow, 12));

            var card = Theme.Column();
            card.style.backgroundColor = PrismPalette.Night;
            Theme.Border(card, 4, PrismPalette.Glow);
            Theme.Radius(card, 24);
            Theme.Padding(card, 18, 16);

            bool youWon = Contains(view.Winners, You);
            string title;
            if (view.Winners.Count > 1) title = youWon ? "You share the win!" : "It's a tie!";
            else if (youWon) title = "Prism Champion!";
            else title = $"{Names[view.Winners[0]]} wins!";

            var hero = Theme.Row();
            hero.style.justifyContent = Justify.Center;
            int heroSeat = youWon ? You : view.Winners[0];
            var buddy = new GemBuddy(BuddyColors[heroSeat], 100) { Face = GemBuddy.Mood.Wow };
            hero.Add(buddy);
            card.Add(hero);

            var heading = Theme.Label(title, 30, PrismPalette.Glow, bold: true);
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(heading);

            var seats = new List<int> { 0, 1, 2, 3 };
            seats.Sort((a, b) =>
            {
                bool wa = Contains(view.Winners, a), wb = Contains(view.Winners, b);
                if (wa != wb) return wa ? -1 : 1;
                int c = view.HandCounts[a].CompareTo(view.HandCounts[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int i = 0; i < seats.Count; i++)
            {
                int seat = seats[i];
                int left = view.HandCounts[seat];
                var row = Theme.Row();
                row.style.backgroundColor = seat == You ? Cream : Sky;
                Theme.Radius(row, 14);
                Theme.Padding(row, 8, 12);
                Theme.Margin(row, i == 0 ? 12 : 6, 0, 0, 0);
                Color text = seat == You ? PrismPalette.Ink : Cream;
                var place = Theme.Label((i + 1).ToString(), 18, text, bold: true);
                place.style.width = 24;
                row.Add(place);
                row.Add(new GemBuddy(BuddyColors[seat], 28));
                var name = Theme.Label(Names[seat], 15, text, bold: true);
                Theme.Margin(name, 0, 0, 0, 6);
                name.style.flexGrow = 1;
                row.Add(name);
                row.Add(Theme.Label(left == 0 ? "out!" : $"{left} left", 15, text, bold: true));
                card.Add(row);
            }

            var buttons = Theme.Row();
            Theme.Margin(buttons, 16, 0, 0, 0);
            var lounge = Theme.SecondaryButton("Lounge", LeaveTable);
            lounge.style.flexGrow = 1;
            Theme.Margin(lounge, 0, 8, 0, 0);
            var again = Theme.PrimaryButton("Play again!", Rematch);
            again.style.flexGrow = 2;
            buttons.Add(lounge);
            buttons.Add(again);
            card.Add(buttons);
            overlay.Add(card);

            Sfx.Play(youWon ? Sound.Win : Sound.Pod);
            Fx.Pop(card, 0.1f, 0.4f);
        }

        // ---------- Words ----------

        private static string PlayLine(PrismEvent e)
        {
            if (e.Seat == You) return null;
            int last = e.Cards[e.Cards.Count - 1];
            var card = PrismCards.Card(last);
            if (e.Cards.Count > 1) return $"{e.Cards.Count} {card.Number}s!";
            switch (card.Kind)
            {
                case CardKind.Flip: return "Flip it!";
                case CardKind.Hop: return "Hop!";
                case CardKind.Pulse: return "Pulse! Zap!";
                case CardKind.Prism: return $"Prism... {PrismCards.ColorName(e.Color)}!";
                default: return PrismCards.Name(last) + "!";
            }
        }
    }
}
