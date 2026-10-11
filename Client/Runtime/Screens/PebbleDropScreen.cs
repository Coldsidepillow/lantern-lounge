using System;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>
    /// The Pebble Drop table on one device: pass-and-play, or you against the bot.
    /// All rules come from the shared PebbleDropModule; this class only draws and forwards taps.
    /// </summary>
    internal sealed class PebbleDropScreen
    {
        private const float CellSize = 40f;
        private const float CellGap = 4f;
        private const int HumanSeat = 0;
        private const int BotSeat = 1;
        private const long BotDelayMs = 550;
        private const float FallSeconds = 0.38f;

        private readonly LoungeApp app;
        private readonly bool againstBot;
        private readonly PebbleDropModule module = new PebbleDropModule();
        private readonly SeededRandom random;
        private readonly LocalMatch<PebbleDropState, DropAction, PebbleDropState> match;

        private readonly VisualElement[,] holes = new VisualElement[PebbleDropState.Rows, PebbleDropState.Columns];
        private readonly VisualElement[,] pebbles = new VisualElement[PebbleDropState.Rows, PebbleDropState.Columns];
        private readonly Button[] columnButtons = new Button[PebbleDropState.Columns];

        private VisualElement screen;
        private VisualElement banner;
        private Label bannerText;
        private VisualElement bannerPebble;
        private VisualElement seatCard0;
        private VisualElement seatCard1;
        private Label seatTag0;
        private Label seatTag1;
        private Label moveLabel;
        private Button restartButton;
        private bool botThinking;
        private IVisualElementScheduledItem pendingBotMove;

        public PebbleDropScreen(LoungeApp app, bool againstBot)
        {
            this.app = app;
            this.againstBot = againstBot;
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            random = new SeededRandom(seed ^ 0x5EEDUL);
            match = new LocalMatch<PebbleDropState, DropAction, PebbleDropState>(module, new MatchConfig(2, seed));
        }

        private string NameOf(int seat)
        {
            if (againstBot) return seat == HumanSeat ? "You" : "The bot";
            return seat == 0 ? "Player 1" : "Player 2";
        }

        public VisualElement Build()
        {
            screen = Theme.Column();
            screen.style.backgroundColor = Theme.PebbleGround;
            Theme.Padding(screen, 0, 16);

            screen.Add(BuildHeader());
            screen.Add(BuildBanner());
            screen.Add(BuildSeats());
            screen.Add(BuildBoard());

            var rules = Theme.Label("Get four of your pebbles in a row: across, up and down, or on a slant.", 12, Theme.DrawGrey);
            rules.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(rules, 10, 8, 0, 8);
            screen.Add(rules);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            screen.Add(spacer);
            screen.Add(BuildFooter());

            Refresh();
            ScheduleBotIfNeeded();
            return screen;
        }

        private VisualElement BuildHeader()
        {
            var header = Theme.Row();
            Theme.Margin(header, 16, 0, 0, 0);

            var back = Theme.SecondaryButton("< Lounge", app.ShowLounge);
            back.style.height = 44;
            back.style.fontSize = 14;
            Theme.Padding(back, 0, 12);
            header.Add(back);

            var title = Theme.Label("Pebble Drop", 24, Theme.Text, bold: true);
            Theme.Margin(title, 0, 0, 0, 12);
            title.style.flexGrow = 1;
            header.Add(title);

            moveLabel = Theme.Label("", 12, Theme.DrawGrey, bold: true);
            header.Add(moveLabel);
            return header;
        }

        private VisualElement BuildBanner()
        {
            banner = Theme.Row();
            Theme.Radius(banner, 16);
            Theme.Padding(banner, 12, 14);
            Theme.Margin(banner, 12, 0, 0, 0);

            bannerPebble = new VisualElement();
            Theme.Circle(bannerPebble, 26);
            Theme.Border(bannerPebble, 2, Theme.Ink);
            bannerPebble.style.alignItems = Align.Center;
            bannerPebble.style.justifyContent = Justify.Center;
            banner.Add(bannerPebble);

            bannerText = Theme.Label("", 19, Theme.Ink, bold: true);
            Theme.Margin(bannerText, 0, 0, 0, 10);
            banner.Add(bannerText);
            return banner;
        }

        private VisualElement BuildSeats()
        {
            var row = Theme.Row();
            Theme.Margin(row, 12, 0, 0, 0);
            seatCard0 = SeatCard(0, out seatTag0);
            seatCard1 = SeatCard(1, out seatTag1);
            Theme.Margin(seatCard0, 0, 5, 0, 0);
            Theme.Margin(seatCard1, 0, 0, 0, 5);
            row.Add(seatCard0);
            row.Add(seatCard1);
            return row;
        }

        private VisualElement SeatCard(int seat, out Label tag)
        {
            var card = Theme.Row();
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.backgroundColor = Theme.Hex("#26323C");
            Theme.Radius(card, 16);
            Theme.Border(card, 2, new Color(0, 0, 0, 0));
            Theme.Padding(card, 8, 10);

            var pebble = new VisualElement();
            Theme.Circle(pebble, 34);
            pebble.style.alignItems = Align.Center;
            pebble.style.justifyContent = Justify.Center;
            StylePebble(pebble, seat, 10f);
            card.Add(pebble);

            var text = Theme.Column();
            Theme.Margin(text, 0, 0, 0, 10);
            text.Add(Theme.Label(NameOf(seat), 14, Theme.Text, bold: true));
            string shape = seat == 0 ? "Ember · dot" : "Moss · ring";
            tag = Theme.Label(shape, 12, Theme.DrawGrey);
            text.Add(tag);
            card.Add(text);
            return card;
        }

        private VisualElement BuildBoard()
        {
            var board = Theme.Row();
            board.style.alignSelf = Align.Center;
            board.style.backgroundColor = Theme.Board;
            Theme.Border(board, 2, Theme.BoardEdge);
            Theme.Radius(board, 22);
            Theme.Padding(board, 6, 6);
            Theme.Margin(board, 16, 0, 0, 0);
            board.style.overflow = Overflow.Hidden;

            for (int column = 0; column < PebbleDropState.Columns; column++)
            {
                int c = column;
                var button = new Button(() => OnColumn(c)) { text = string.Empty };
                button.style.flexDirection = FlexDirection.Column;
                button.style.backgroundColor = new Color(0, 0, 0, 0);
                Theme.Radius(button, 12);
                Theme.Border(button, 2, new Color(0, 0, 0, 0));
                Theme.Padding(button, 2, 1);
                Theme.Margin(button, 0, 1, 0, 1);
                Theme.FocusRing(button, new Color(0, 0, 0, 0), Theme.Amber);
                button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = Theme.Hex("#2A3946"));
                button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = new Color(0, 0, 0, 0));
                button.tooltip = $"Column {c + 1}";

                for (int row = 0; row < PebbleDropState.Rows; row++)
                {
                    var hole = new VisualElement();
                    Theme.Circle(hole, CellSize);
                    Theme.Margin(hole, CellGap / 2f, 0, CellGap / 2f, 0);
                    hole.style.backgroundColor = Theme.Hole;
                    Theme.Border(hole, 3, new Color(0, 0, 0, 0));
                    hole.pickingMode = PickingMode.Ignore;

                    var pebble = new VisualElement();
                    pebble.style.position = Position.Absolute;
                    pebble.style.left = 0;
                    pebble.style.right = 0;
                    pebble.style.top = 0;
                    pebble.style.bottom = 0;
                    Theme.Radius(pebble, CellSize / 2f);
                    pebble.style.alignItems = Align.Center;
                    pebble.style.justifyContent = Justify.Center;
                    pebble.pickingMode = PickingMode.Ignore;
                    hole.Add(pebble);

                    holes[row, c] = hole;
                    pebbles[row, c] = pebble;
                    button.Add(hole);
                }

                columnButtons[c] = button;
                board.Add(button);
            }
            return board;
        }

        private VisualElement BuildFooter()
        {
            var footer = Theme.Row();
            Theme.Margin(footer, 0, 0, 24, 0);
            restartButton = Theme.PrimaryButton("Restart", Rematch);
            restartButton.style.flexGrow = 1;
            footer.Add(restartButton);
            return footer;
        }

        private void OnColumn(int column)
        {
            var state = match.State;
            if (state.IsOver || botThinking) return;
            int seat = state.CurrentSeat;
            if (againstBot && seat != HumanSeat) return;

            var check = match.TryPlay(seat, new DropAction(column));
            if (!check.Ok)
            {
                ShowMessage(check.Message);
                return;
            }

            AfterMove();
            ScheduleBotIfNeeded();
        }

        private void ScheduleBotIfNeeded()
        {
            var state = match.State;
            if (!againstBot || state.IsOver || state.CurrentSeat != BotSeat) return;

            pendingBotMove?.Pause();
            botThinking = true;
            Refresh();
            pendingBotMove = screen.schedule.Execute(() =>
            {
                botThinking = false;
                pendingBotMove = null;
                var current = match.State;
                if (current.IsOver || current.CurrentSeat != BotSeat) return;
                var move = PebbleDropBot.ChooseMove(current, BotSeat, random, BotLevel.Normal);
                if (match.TryPlay(BotSeat, move).Ok) AfterMove();
                else Refresh();
            }).StartingIn(BotDelayMs);
        }

        private void AfterMove()
        {
            Refresh();
            int last = match.State.LastCell;
            if (last < 0) return;
            AnimateDrop(PebbleDropState.RowOf(last), PebbleDropState.ColumnOf(last));
        }

        private void Rematch()
        {
            pendingBotMove?.Pause();
            pendingBotMove = null;
            botThinking = false;
            match.Reset(module.Rematch(match.State));
            Refresh();
            ScheduleBotIfNeeded();
        }

        private void ShowMessage(string message)
        {
            bannerText.text = message;
        }

        private void Refresh()
        {
            var state = match.State;
            bool over = state.IsOver;

            for (int row = 0; row < PebbleDropState.Rows; row++)
            {
                for (int column = 0; column < PebbleDropState.Columns; column++)
                {
                    int seat = state.CellAt(row, column);
                    var pebble = pebbles[row, column];
                    pebble.Clear();
                    if (seat == PebbleDropState.Empty)
                    {
                        pebble.style.backgroundColor = new Color(0, 0, 0, 0);
                    }
                    else
                    {
                        StylePebble(pebble, seat, 12f);
                    }

                    bool winning = Contains(state.WinningCells, PebbleDropState.Index(row, column));
                    Theme.BorderColor(holes[row, column], winning ? Theme.Amber : new Color(0, 0, 0, 0));
                }
            }

            for (int column = 0; column < PebbleDropState.Columns; column++)
            {
                bool humanTurn = !againstBot || state.CurrentSeat == HumanSeat;
                bool open = !over && !botThinking && humanTurn && !state.IsColumnFull(column);
                columnButtons[column].SetEnabled(open);
                if (!open) columnButtons[column].style.backgroundColor = new Color(0, 0, 0, 0);
            }

            int focusSeat = over ? state.Winner : state.CurrentSeat;
            if (state.IsDraw)
            {
                bannerText.text = "Board full: it's a draw";
                banner.style.backgroundColor = Theme.DrawGrey;
                bannerPebble.style.visibility = Visibility.Hidden;
            }
            else
            {
                bannerPebble.style.visibility = Visibility.Visible;
                bannerPebble.Clear();
                StylePebble(bannerPebble, focusSeat, 8f);
                banner.style.backgroundColor = focusSeat == 0 ? Theme.EmberLight : Theme.MossLight;
                if (over) bannerText.text = againstBot && focusSeat == HumanSeat ? "You win!" : $"{NameOf(focusSeat)} wins!";
                else if (botThinking) bannerText.text = "The bot is thinking...";
                else if (againstBot) bannerText.text = state.CurrentSeat == HumanSeat ? "Your turn" : "The bot's turn";
                else bannerText.text = $"{NameOf(state.CurrentSeat)}'s turn";
            }

            Theme.BorderColor(seatCard0, focusSeat == 0 && !state.IsDraw ? Theme.Amber : new Color(0, 0, 0, 0));
            Theme.BorderColor(seatCard1, focusSeat == 1 && !state.IsDraw ? Theme.Amber : new Color(0, 0, 0, 0));
            seatTag0.text = SeatTag(state, 0);
            seatTag1.text = SeatTag(state, 1);

            moveLabel.text = $"Move {state.MoveCount}";
            restartButton.text = over ? "Rematch" : "Restart";
        }

        private static string SeatTag(PebbleDropState state, int seat)
        {
            string shape = seat == 0 ? "Ember · dot" : "Moss · ring";
            if (state.Winner == seat) return shape + " · winner";
            if (!state.IsOver && state.CurrentSeat == seat) return shape + " · turn";
            return shape;
        }

        // Seat 0 is a coral pebble with a cream dot; seat 1 is a green pebble with a dark ring,
        // so the two read apart without relying on color.
        private static void StylePebble(VisualElement pebble, int seat, float markSize)
        {
            pebble.Clear();
            pebble.style.backgroundColor = seat == 0 ? Theme.Ember : Theme.Moss;
            var mark = new VisualElement();
            mark.pickingMode = PickingMode.Ignore;
            if (seat == 0)
            {
                Theme.Circle(mark, markSize);
                mark.style.backgroundColor = Theme.Cream;
            }
            else
            {
                Theme.Circle(mark, markSize + 4f);
                Theme.Border(mark, 3, Theme.Board);
            }
            pebble.Add(mark);
        }

        private void AnimateDrop(int row, int column)
        {
            var pebble = pebbles[row, column];
            if (Prefs.ReduceMotion)
            {
                pebble.style.translate = new StyleTranslate(new Translate(0, 0));
                return;
            }

            float start = -(row + 1) * (CellSize + CellGap);
            float elapsed = 0f;
            pebble.style.translate = new StyleTranslate(new Translate(0, start));
            pebble.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / FallSeconds);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                pebble.style.translate = new StyleTranslate(new Translate(0, Mathf.Lerp(start, 0f, eased)));
            }).Every(16).Until(() => elapsed >= FallSeconds);
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }
}
