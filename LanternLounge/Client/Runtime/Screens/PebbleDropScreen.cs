using System;
using System.Collections.Generic;
using LanternLounge.Client.Online;
using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>
    /// Four-player Pebble Drop on a 16 x 12 riverbed. Each seat (Red, Yellow, Blue, Green) can be
    /// a person or a bot; tap a player card to switch. All rules come from the shared
    /// PebbleDropModule; this class only draws, animates and forwards taps.
    /// Online, the same screen plays through the server: your taps are sent as moves, and the
    /// board the server sends back is what's drawn.
    /// </summary>
    internal sealed class PebbleDropScreen
    {
        private const int Seats = PebbleDropState.Players;
        private const int Cols = PebbleDropState.Columns;
        private const int Rows = PebbleDropState.Rows;
        private const long BotDelayMs = 650;
        private const string MarksKey = "lanternlounge.pebbleMarks";

        private static readonly Color[] SeatColors =
        {
            Theme.Hex("#E5484D"), Theme.Hex("#F5C518"), Theme.Hex("#3E8EF7"), Theme.Hex("#2FB86B")
        };

        private static readonly Color Ground = Theme.Hex("#121B22");
        private static readonly Color Card = Theme.Hex("#18252F");
        private static readonly Color Slate = Theme.Hex("#22313D");
        private static readonly Color SlateEdge = Theme.Hex("#3A4C5B");
        private static readonly Color HoleColor = Theme.Hex("#0E171E");
        private static readonly Color Text = Theme.Hex("#EEF3F6");
        private static readonly Color Muted = Theme.Hex("#9DB0BE");
        private static readonly Color Clear = new Color(0, 0, 0, 0);

        private readonly LoungeApp app;
        private readonly PebbleDropModule module = new PebbleDropModule();
        private readonly LocalMatch<PebbleDropState, DropAction, PebbleDropState> match;
        private readonly SeededRandom random;
        private readonly bool[] isBot = new bool[Seats];
        private readonly int[] scores = new int[Seats];
        private readonly Stack<PebbleDropState> undoStack = new Stack<PebbleDropState>();

        private int hoverColumn = 8;
        private bool busy;
        private bool hints;
        private bool marks;
        private bool scored;
        private IVisualElementScheduledItem pendingBot;
        private IVisualElementScheduledItem winPulse;

        private VisualElement screen;
        private VisualElement area;
        private VisualElement rail;
        private VisualElement board;
        private VisualElement overlay;
        private WinLine winLine;
        private readonly VisualElement[] railCells = new VisualElement[Cols];
        private readonly VisualElement[] railHints = new VisualElement[Cols];
        private readonly VisualElement[,] cells = new VisualElement[Rows, Cols];
        private readonly VisualElement[,] holes = new VisualElement[Rows, Cols];
        private readonly VisualElement[,] pebbles = new VisualElement[Rows, Cols];
        private VisualElement ghost;
        private readonly VisualElement[] seatCards = new VisualElement[Seats];
        private readonly VisualElement[] seatPebbles = new VisualElement[Seats];
        private readonly Label[] seatWins = new Label[Seats];
        private readonly Label[] seatKind = new Label[Seats];
        private VisualElement statusPebble;
        private Label statusLabel;
        private Button undoButton;
        private Button newGameButton;
        private Button resetButton;
        private readonly Label[] seatNames = new Label[Seats];
        private Button hintsButton;

        // Online play (null when playing on this device).
        private readonly OnlineSession online;
        private RoomSnapshot room;
        private PebbleDropState onlineState;
        private readonly Queue<PebbleDropState> incoming = new Queue<PebbleDropState>();
        private bool awaitingServer;
        private int onlineMatch = -1;
        private Button marksButton;

        /// <param name="humans">Seats 0..humans-1 start as people; the rest start as bots.</param>
        public PebbleDropScreen(LoungeApp app, int humans)
        {
            this.app = app;
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            random = new SeededRandom(seed ^ 0x5EEDUL);
            var options = new Dictionary<string, string> { { "firstSeat", "0" } };
            match = new LocalMatch<PebbleDropState, DropAction, PebbleDropState>(module, new MatchConfig(Seats, seed, options));
            humans = Mathf.Clamp(humans, 0, Seats);
            for (int s = 0; s < Seats; s++) isBot[s] = s >= humans;
            marks = PlayerPrefs.GetInt(MarksKey, 1) == 1;
        }

        /// <summary>Plays online through the server, in the room the session is in.</summary>
        public PebbleDropScreen(LoungeApp app, OnlineSession online)
        {
            this.app = app;
            this.online = online;
            random = new SeededRandom(1);
            marks = PlayerPrefs.GetInt(MarksKey, 1) == 1;
            room = online.Room;
            onlineMatch = room?.Match ?? -1;
            onlineState = ParseState(room) ?? module.NewBoard(0);
            ApplySeats();
        }

        private bool IsOnline => online != null;

        private PebbleDropState State => IsOnline ? onlineState : match.State;

        private string NameOf(int seat)
        {
            if (IsOnline)
            {
                var info = room?.SeatAt(seat);
                if (info != null && !string.IsNullOrEmpty(info.Name)) return info.Name;
            }
            return PebbleDropModule.ColorNames[seat];
        }

        public VisualElement Build()
        {
            screen = Theme.Column();
            screen.style.backgroundColor = Ground;
            screen.style.overflow = Overflow.Hidden;
            Theme.Padding(screen, 0, 12);

            screen.Add(BuildHeader());
            screen.Add(BuildPlayers());
            screen.Add(BuildStatus());
            screen.Add(BuildBoard());
            screen.Add(BuildControls());

            var help = Theme.Label("Tap a column to drop. Tap a player card to switch between person and bot. Connect four in any direction!", 12, Muted);
            help.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(help, 8, 6, 12, 6);
            screen.Add(help);

            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                StopTimers();
                if (IsOnline)
                {
                    online.RoomChanged -= OnRoom;
                    online.Error -= OnOnlineError;
                    online.StatusChanged -= OnOnlineStatus;
                    online.Left -= OnLeftRoom;
                }
            });
            if (IsOnline)
            {
                online.RoomChanged += OnRoom;
                online.Error += OnOnlineError;
                online.StatusChanged += OnOnlineStatus;
                online.Left += OnLeftRoom;
                help.text = $"Online room {room?.Code}. Tap a column on your turn. Connect four in any direction!";
            }
            Refresh();
            NextStep();
            return screen;
        }

        // ---------- Layout ----------

        private VisualElement BuildHeader()
        {
            var header = Theme.Row();
            Theme.Margin(header, 12, 0, 0, 0);

            var back = Chip("<", LeaveTable);
            back.style.width = 44;
            back.tooltip = "Back to the lounge";
            back.style.flexShrink = 0;
            header.Add(back);

            var dots = Theme.Row();
            dots.style.flexShrink = 0;
            Theme.Margin(dots, 0, 0, 0, 10);
            for (int s = 0; s < Seats; s++)
            {
                var dot = Pebble(s, 12, withMark: false);
                Theme.Margin(dot, 0, 2, 0, 0);
                dots.Add(dot);
            }
            header.Add(dots);

            var title = Theme.Label("Pebble Drop", 19, Text, bold: true);
            Theme.Margin(title, 0, 0, 0, 8);
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            header.Add(title);

            hintsButton = Chip("Hints", ToggleHints);
            hintsButton.tooltip = "Mark columns where the current player can win right now";
            hintsButton.style.flexShrink = 0;
            header.Add(hintsButton);
            marksButton = Chip("Shapes", ToggleMarks);
            marksButton.tooltip = "Small shapes on the pebbles, so colors are easy to tell apart";
            marksButton.style.flexShrink = 0;
            Theme.Margin(marksButton, 0, 0, 0, 6);
            header.Add(marksButton);
            return header;
        }

        private VisualElement BuildPlayers()
        {
            var row = Theme.Row();
            row.style.alignItems = Align.Stretch;
            Theme.Margin(row, 10, 0, 0, 0);
            for (int s = 0; s < Seats; s++)
            {
                int seat = s;
                var card = new Button(() => ToggleBot(seat)) { text = string.Empty };
                card.style.flexGrow = 1;
                card.style.flexBasis = 0;
                card.style.flexDirection = FlexDirection.Column;
                card.style.alignItems = Align.Center;
                card.style.backgroundColor = Card;
                Theme.Border(card, 2, Clear);
                Theme.Radius(card, 14);
                Theme.Padding(card, 6, 2);
                Theme.Margin(card, 0, s < Seats - 1 ? 6 : 0, 0, 0);
                card.tooltip = $"{NameOf(seat)}: tap to switch between person and bot";

                var pebble = Pebble(seat, 26, withMark: true);
                seatPebbles[s] = pebble;
                card.Add(pebble);
                var name = Theme.Label(NameOf(seat), 14, Text, bold: true);
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                name.style.maxWidth = 84;
                seatNames[s] = name;
                name.pickingMode = PickingMode.Ignore;
                card.Add(name);
                seatWins[s] = Theme.Label("", 11, Muted);
                seatWins[s].pickingMode = PickingMode.Ignore;
                card.Add(seatWins[s]);
                seatKind[s] = Theme.Label("", 11, Muted, bold: true);
                seatKind[s].pickingMode = PickingMode.Ignore;
                card.Add(seatKind[s]);

                seatCards[s] = card;
                row.Add(card);
            }
            return row;
        }

        private VisualElement BuildStatus()
        {
            var row = Theme.Row();
            row.style.justifyContent = Justify.Center;
            row.style.minHeight = 40;
            Theme.Margin(row, 8, 0, 4, 0);
            statusPebble = new VisualElement();
            statusPebble.style.width = 24;
            statusPebble.style.height = 24;
            Theme.Margin(statusPebble, 0, 8, 0, 0);
            row.Add(statusPebble);
            statusLabel = Theme.Label("", 20, Text, bold: true);
            row.Add(statusLabel);
            return row;
        }

        private VisualElement BuildBoard()
        {
            area = new VisualElement();
            area.focusable = true;
            area.tooltip = "Board: left and right arrows choose a column, Enter drops";

            rail = Theme.Row();
            rail.style.height = 24;
            Theme.Padding(rail, 0, 7);
            for (int c = 0; c < Cols; c++)
            {
                var cell = new VisualElement();
                cell.pickingMode = PickingMode.Ignore;
                cell.style.flexGrow = 1;
                cell.style.flexBasis = 0;
                cell.style.alignItems = Align.Center;
                cell.style.justifyContent = Justify.Center;
                var hint = new VisualElement();
                hint.pickingMode = PickingMode.Ignore;
                hint.style.width = Length.Percent(40);
                hint.style.height = Length.Percent(40);
                Theme.Radius(hint, 20);
                hint.style.position = Position.Absolute;
                hint.style.bottom = 0;
                hint.style.display = DisplayStyle.None;
                cell.Add(hint);
                railHints[c] = hint;
                railCells[c] = cell;
                rail.Add(cell);
            }
            area.Add(rail);

            board = Theme.Column();
            board.style.backgroundColor = Slate;
            Theme.Border(board, 3, SlateEdge);
            Theme.Radius(board, 16);
            Theme.Padding(board, 4, 4);
            for (int r = 0; r < Rows; r++)
            {
                var row = Theme.Row();
                row.pickingMode = PickingMode.Ignore;
                row.style.flexGrow = 1;
                row.style.alignItems = Align.Stretch;
                for (int c = 0; c < Cols; c++)
                {
                    var cell = new VisualElement();
                    cell.pickingMode = PickingMode.Ignore;
                    cell.style.flexGrow = 1;
                    cell.style.flexBasis = 0;
                    cell.style.alignItems = Align.Center;
                    cell.style.justifyContent = Justify.Center;
                    Theme.Radius(cell, 6);
                    var hole = new VisualElement();
                    hole.pickingMode = PickingMode.Ignore;
                    hole.style.width = Length.Percent(84);
                    hole.style.height = Length.Percent(84);
                    hole.style.backgroundColor = HoleColor;
                    Theme.Radius(hole, 40);
                    Theme.Border(hole, 0, Clear);
                    hole.style.alignItems = Align.Center;
                    hole.style.justifyContent = Justify.Center;
                    cell.Add(hole);
                    cells[r, c] = cell;
                    holes[r, c] = hole;
                    row.Add(cell);
                }
                board.Add(row);
            }
            area.Add(board);

            winLine = new WinLine();
            board.Add(winLine);

            overlay = new VisualElement();
            overlay.pickingMode = PickingMode.Ignore;
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            area.Add(overlay);

            // Keep the board 16:12 at any width, and the rail one cell tall.
            area.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float width = area.resolvedStyle.width;
                if (float.IsNaN(width) || width < 50f) return;
                float cell = (width - 14f) / Cols;
                float boardHeight = cell * Rows + 14f;
                if (Mathf.Abs(board.resolvedStyle.height - boardHeight) > 0.5f) board.style.height = boardHeight;
                if (Mathf.Abs(rail.resolvedStyle.height - cell) > 0.5f) rail.style.height = cell;
                DrawWinLine();
            });

            area.RegisterCallback<PointerMoveEvent>(e => SetHover(ColumnAt(e.position)));
            area.RegisterCallback<PointerDownEvent>(e =>
            {
                int column = ColumnAt(e.position);
                SetHover(column);
                area.Focus();
                TryDrop(column, byPerson: true);
            });
            area.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.LeftArrow) { SetHover((hoverColumn + Cols - 1) % Cols); Sfx.Play(Sound.Tap); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.RightArrow) { SetHover((hoverColumn + 1) % Cols); Sfx.Play(Sound.Tap); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.DownArrow)
                {
                    TryDrop(hoverColumn, byPerson: true);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.U) Undo();
                else if (e.keyCode == KeyCode.N) NewGame();
            });
            return area;
        }

        private VisualElement BuildControls()
        {
            var row = Theme.Row();
            Theme.Margin(row, 12, 0, 0, 0);
            var newGame = Theme.PrimaryButton(IsOnline ? "Play again" : "New Game", NewGame);
            newGameButton = newGame;
            newGame.style.flexGrow = 2;
            newGame.style.flexBasis = 0;
            newGame.tooltip = "Clear the board and keep the score. The next player starts.";
            Theme.Margin(newGame, 0, 8, 0, 0);
            row.Add(newGame);
            undoButton = Theme.SecondaryButton("Undo", Undo);
            undoButton.style.flexGrow = 1;
            undoButton.style.flexBasis = 0;
            Theme.Margin(undoButton, 0, 8, 0, 0);
            row.Add(undoButton);
            var reset = Theme.SecondaryButton(IsOnline ? "Leave" : "Reset", ResetAll);
            resetButton = reset;
            if (IsOnline)
            {
                undoButton.style.display = DisplayStyle.None;
                newGame.tooltip = "The host can start a new game once this one ends.";
            }
            reset.style.flexGrow = 1;
            reset.style.flexBasis = 0;
            reset.tooltip = "Clear the board and the score. Red starts.";
            row.Add(reset);
            return row;
        }

        private static Button Chip(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 40;
            button.style.minHeight = 40;
            button.style.fontSize = 13;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = Text;
            button.style.backgroundColor = Card;
            Theme.Border(button, 2, SlateEdge);
            Theme.Radius(button, 20);
            Theme.Padding(button, 0, 10);
            Theme.Margin(button, 0, 0, 0, 0);
            Theme.FocusRing(button, SlateEdge, Text);
            return button;
        }

        /// <summary>A pebble: its color, a shine, and (optionally) a small shape so colors are easy to tell apart.</summary>
        private VisualElement Pebble(int seat, float size, bool withMark)
        {
            var pebble = new VisualElement();
            pebble.pickingMode = PickingMode.Ignore;
            if (size > 0)
            {
                pebble.style.width = size;
                pebble.style.height = size;
            }
            else
            {
                pebble.style.width = Length.Percent(100);
                pebble.style.height = Length.Percent(100);
            }
            Color color = SeatColors[seat];
            pebble.style.backgroundColor = color;
            Theme.Radius(pebble, 60);
            pebble.style.borderBottomWidth = 2;
            pebble.style.borderBottomColor = Color.Lerp(color, Color.black, 0.35f);
            pebble.style.alignItems = Align.Center;
            pebble.style.justifyContent = Justify.Center;

            var shine = new VisualElement();
            shine.pickingMode = PickingMode.Ignore;
            shine.style.position = Position.Absolute;
            shine.style.left = Length.Percent(20);
            shine.style.top = Length.Percent(14);
            shine.style.width = Length.Percent(26);
            shine.style.height = Length.Percent(26);
            shine.style.backgroundColor = new Color(1f, 1f, 1f, 0.5f);
            Theme.Radius(shine, 30);
            pebble.Add(shine);

            if (withMark && marks)
            {
                var mark = new VisualElement();
                mark.pickingMode = PickingMode.Ignore;
                mark.style.width = Length.Percent(32);
                mark.style.height = Length.Percent(32);
                Color ink = seat == 1 ? new Color(0f, 0f, 0f, 0.4f) : new Color(1f, 1f, 1f, 0.6f);
                switch (seat)
                {
                    case 0: // Red: dot
                        mark.style.backgroundColor = ink;
                        Theme.Radius(mark, 30);
                        break;
                    case 1: // Yellow: ring
                        Theme.Border(mark, 2, ink);
                        Theme.Radius(mark, 30);
                        break;
                    case 2: // Blue: diamond
                        mark.style.backgroundColor = ink;
                        mark.style.rotate = new StyleRotate(new Rotate(new Angle(45f, AngleUnit.Degree)));
                        mark.style.width = Length.Percent(26);
                        mark.style.height = Length.Percent(26);
                        break;
                    default: // Green: square
                        mark.style.backgroundColor = ink;
                        Theme.Radius(mark, 2);
                        break;
                }
                pebble.Add(mark);
            }
            return pebble;
        }

        // ---------- Input ----------

        private int ColumnAt(Vector2 worldPosition)
        {
            var rect = board.worldBound;
            float inner = rect.width - 14f;
            float x = worldPosition.x - rect.x - 7f;
            return Mathf.Clamp(Mathf.FloorToInt(x / (inner / Cols)), 0, Cols - 1);
        }

        private void SetHover(int column)
        {
            if (column == hoverColumn) return;
            hoverColumn = column;
            DrawHover();
        }

        private bool PersonsTurn => IsOnline
            ? !State.IsOver && State.CurrentSeat == online.Seat && !awaitingServer
            : !State.IsOver && !isBot[State.CurrentSeat];

        private void TryDrop(int column, bool byPerson)
        {
            if (busy) return;
            if (State.IsOver)
            {
                if (byPerson) Fx.Toast(screen, IsOnline ? "Game over!" : "Game over! Press New Game.", Theme.Amber, Theme.Ink, 1.2f);
                return;
            }
            if (byPerson && !PersonsTurn) return;
            if (State.IsColumnFull(column))
            {
                Sfx.Play(Sound.Tap);
                Shake(board);
                Fx.Toast(screen, $"Column {column + 1} is full. Pick another!", SlateEdge, Text, 1.1f);
                return;
            }

            if (IsOnline)
            {
                if (online.Status != OnlineStatus.Connected)
                {
                    Fx.Toast(screen, "Reconnecting... try again in a moment.", SlateEdge, Text, 1.2f);
                    return;
                }
                // The server checks the move and sends the new board to everyone.
                awaitingServer = true;
                online.Move(new Dictionary<string, object> { { "column", column } });
                DrawHover();
                return;
            }

            var before = State;
            int seat = before.CurrentSeat;
            int row = before.LandingRow(column);
            var check = match.TryPlay(seat, new DropAction(column));
            if (!check.Ok)
            {
                if (byPerson) Fx.Toast(screen, check.Message, SlateEdge, Text, 1.1f);
                return;
            }
            undoStack.Push(before);
            AnimateDrop(seat, row, column, () =>
            {
                Landed(seat, row, column);
                NextStep();
            });
        }

        /// <summary>After a pebble lands: sound, ripple, and a win or draw if the game just ended.</summary>
        private void Landed(int seat, int row, int column)
        {
            Sfx.Play(Sound.Drop);
            Refresh();
            Ripple(seat, row, column);
            if (State.Winner != PebbleDropState.NoSeat) Celebrate();
            else if (State.IsDraw)
            {
                Sfx.Play(Sound.Pod);
                Fx.Toast(screen, "The board is full. It's a draw!", Muted, Theme.Ink, 1.6f);
            }
        }

        // ---------- Online ----------

        private void OnRoom(RoomSnapshot snapshot)
        {
            room = snapshot;
            ApplySeats();
            if (snapshot.InLobby)
            {
                app.ShowOnline(PebbleDropModule.GameId);
                return;
            }
            var next = ParseState(snapshot);
            if (next != null)
            {
                if (snapshot.Match != onlineMatch)
                {
                    // The host started a new game (even if a reconnect skipped its first board).
                    onlineMatch = snapshot.Match;
                    incoming.Clear();
                    StopWinPulse();
                    scored = false;
                    awaitingServer = false;
                    onlineState = next;
                    Refresh();
                    Fx.Toast(screen, $"New game! {NameOf(Math.Max(0, next.CurrentSeat))} goes first.", SeatColors[Math.Max(0, next.CurrentSeat)], Theme.Ink, 1.1f);
                    return;
                }
                incoming.Enqueue(next);
            }
            ProcessIncoming();
        }

        /// <summary>Shows boards from the server one at a time, animating each new pebble.</summary>
        private void ProcessIncoming()
        {
            while (!busy && incoming.Count > 0)
            {
                var next = incoming.Dequeue();
                var previous = State;
                // The same board sent again (someone joined or reconnected) doesn't answer our move.
                if (next.MoveCount != previous.MoveCount || next.CurrentSeat != online.Seat) awaitingServer = false;
                bool oneMore = next.MoveCount == previous.MoveCount + 1 && next.LastCell >= 0;
                onlineState = next;
                if (oneMore)
                {
                    int row = PebbleDropState.RowOf(next.LastCell);
                    int column = PebbleDropState.ColumnOf(next.LastCell);
                    int seat = next.CellAt(row, column);
                    SetHover(column);
                    AnimateDrop(seat, row, column, () =>
                    {
                        Landed(seat, row, column);
                        ProcessIncoming();
                    });
                    return;
                }
                Refresh();
                if (next.Winner != PebbleDropState.NoSeat && !scored) Celebrate();
            }
            Refresh();
        }

        private void ApplySeats()
        {
            if (room == null) return;
            for (int s = 0; s < Seats; s++)
            {
                var info = room.SeatAt(s);
                isBot[s] = info != null && info.IsBot;
            }
        }

        private void OnOnlineError(string request, string code, string message)
        {
            if (request == "move") awaitingServer = false;
            Fx.Toast(screen, message, SlateEdge, Text, 1.6f);
            Refresh();
        }

        private void OnOnlineStatus(OnlineStatus status) => Refresh();

        private void OnLeftRoom()
        {
            StopTimers();
            // Removed by the server (not by pressing Leave): show why on the online screen.
            if (!string.IsNullOrEmpty(online.LastLeaveReason)) app.ShowOnline(PebbleDropModule.GameId);
            else app.ShowLounge();
        }

        private static PebbleDropState ParseState(RoomSnapshot snapshot)
        {
            var v = snapshot?.View;
            if (v == null) return null;
            try
            {
                return PebbleDropState.FromSnapshot(
                    Json.Ints(Json.Arr(v, "cells")),
                    Json.Int(v, "firstSeat"),
                    Json.Int(v, "currentSeat", PebbleDropState.NoSeat),
                    Json.Int(v, "moveCount"),
                    Json.Int(v, "winner", PebbleDropState.NoSeat),
                    Json.Ints(Json.Arr(v, "winningCells")),
                    Json.Bool(v, "isDraw"),
                    Json.Int(v, "lastCell", -1));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Pebble Drop online: couldn't read the board: " + e.Message);
                return null;
            }
        }

        private void NextStep()
        {
            pendingBot?.Pause();
            pendingBot = null;
            if (IsOnline || State.IsOver || busy || !isBot[State.CurrentSeat]) return;
            int seat = State.CurrentSeat;
            pendingBot = screen.schedule.Execute(() =>
            {
                pendingBot = null;
                if (busy || State.IsOver || State.CurrentSeat != seat || !isBot[seat]) return;
                var move = PebbleDropBot.ChooseMove(State, seat, random, BotLevel.Normal);
                SetHover(move.Column);
                TryDrop(move.Column, byPerson: false);
            }).StartingIn(BotDelayMs);
        }

        // ---------- Buttons ----------

        private void NewGame()
        {
            if (busy) return;
            if (IsOnline)
            {
                if (!State.IsOver) Fx.Toast(screen, "Finish this game first!", SlateEdge, Text, 1.2f);
                else if (room == null || !room.IsHost) Fx.Toast(screen, "The host starts the next game.", SlateEdge, Text, 1.2f);
                else online.Rematch();
                return;
            }
            StopWinPulse();
            var next = State.MoveCount == 0 ? State : module.Rematch(State);
            match.Reset(next);
            undoStack.Clear();
            scored = false;
            Sfx.Play(Sound.Tap);
            Refresh();
            Fx.Toast(screen, $"New game! {NameOf(next.CurrentSeat)} goes first.", SeatColors[next.CurrentSeat], Theme.Ink, 1.1f);
            NextStep();
        }

        private void ResetAll()
        {
            if (IsOnline)
            {
                LeaveTable();
                return;
            }
            if (busy) return;
            StopWinPulse();
            match.Reset(module.NewBoard(0));
            undoStack.Clear();
            for (int s = 0; s < Seats; s++) scores[s] = 0;
            scored = false;
            Sfx.Play(Sound.Tap);
            Refresh();
            Fx.Toast(screen, "Scores cleared. Red goes first.", SeatColors[0], Theme.Ink, 1.1f);
            NextStep();
        }

        /// <summary>Takes back moves until it's a person's turn again (bots' replies are undone too).</summary>
        private void Undo()
        {
            if (IsOnline || busy || undoStack.Count == 0) return;
            pendingBot?.Pause();
            pendingBot = null;
            bool anyPerson = false;
            for (int s = 0; s < Seats; s++) anyPerson |= !isBot[s];

            if (State.Winner != PebbleDropState.NoSeat && scored)
            {
                scores[State.Winner] = Math.Max(0, scores[State.Winner] - 1);
                scored = false;
            }
            var target = undoStack.Pop();
            while (anyPerson && isBot[target.CurrentSeat] && undoStack.Count > 0) target = undoStack.Pop();
            StopWinPulse();
            match.Reset(target);
            Sfx.Play(Sound.Select);
            Refresh();
            NextStep();
        }

        private void ToggleBot(int seat)
        {
            if (IsOnline) return;
            isBot[seat] = !isBot[seat];
            Sfx.Play(Sound.Select);
            Fx.Pop(seatCards[seat], 0.08f);
            Refresh();
            NextStep();
        }

        private void ToggleHints()
        {
            hints = !hints;
            Sfx.Play(Sound.Tap);
            Refresh();
        }

        private void ToggleMarks()
        {
            marks = !marks;
            PlayerPrefs.SetInt(MarksKey, marks ? 1 : 0);
            PlayerPrefs.Save();
            Sfx.Play(Sound.Tap);
            // Rebuild every pebble with or without its shape.
            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                pebbles[r, c]?.RemoveFromHierarchy();
                pebbles[r, c] = null;
            }
            for (int s = 0; s < Seats; s++)
            {
                var fresh = Pebble(s, 26, withMark: true);
                int index = seatCards[s].IndexOf(seatPebbles[s]);
                seatPebbles[s].RemoveFromHierarchy();
                seatCards[s].Insert(index, fresh);
                seatPebbles[s] = fresh;
            }
            ghost?.RemoveFromHierarchy();
            ghost = null;
            Refresh();
        }

        private void LeaveTable()
        {
            StopTimers();
            if (IsOnline && online.InRoom) online.Leave();
            app.ShowLounge();
        }

        private void StopTimers()
        {
            pendingBot?.Pause();
            pendingBot = null;
            StopWinPulse();
        }

        // ---------- Animation ----------

        private Vector2 Local(VisualElement e) => overlay.WorldToLocal(e.worldBound.center);

        private void AnimateDrop(int seat, int row, int column, Action done)
        {
            if (Prefs.ReduceMotion || overlay.panel == null)
            {
                done();
                return;
            }
            busy = true;
            DrawHover();
            float size = holes[row, column].worldBound.width;
            Vector2 from = Local(railCells[column]);
            Vector2 to = Local(holes[row, column]);
            var faller = Pebble(seat, size, withMark: true);
            faller.style.position = Position.Absolute;
            faller.style.left = to.x - size / 2f;
            faller.style.top = from.y - size / 2f;
            overlay.Add(faller);

            float distance = to.y - from.y;
            float seconds = 0.18f + Mathf.Sqrt(Mathf.Max(distance, 1f)) * 0.026f;
            float bounce = Mathf.Min(14f, size * 0.45f);
            float elapsed = 0f;
            faller.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / seconds);
                float y;
                if (t < 0.72f)
                {
                    float k = t / 0.72f;
                    y = distance * k * k; // falling faster and faster
                }
                else if (t < 0.86f)
                {
                    float k = (t - 0.72f) / 0.14f;
                    y = distance - bounce * Mathf.Sin(k * Mathf.PI * 0.5f);
                }
                else
                {
                    float k = (t - 0.86f) / 0.14f;
                    y = distance - bounce * Mathf.Cos(k * Mathf.PI * 0.5f);
                }
                faller.style.translate = new StyleTranslate(new Translate(0, y));
                if (t >= 1f)
                {
                    faller.RemoveFromHierarchy();
                    busy = false;
                    done();
                }
            }).Every(16).Until(() => elapsed >= seconds);
        }

        private void Ripple(int seat, int row, int column)
        {
            if (Prefs.ReduceMotion || overlay.panel == null) return;
            float size = holes[row, column].worldBound.width;
            Vector2 at = Local(holes[row, column]);
            var ring = new VisualElement();
            ring.pickingMode = PickingMode.Ignore;
            ring.style.position = Position.Absolute;
            ring.style.left = at.x - size / 2f;
            ring.style.top = at.y - size / 2f;
            ring.style.width = size;
            ring.style.height = size;
            Theme.Radius(ring, size);
            Theme.Border(ring, 3, SeatColors[seat]);
            overlay.Add(ring);
            float elapsed = 0f;
            ring.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / 0.45f);
                float s = 1f + t * 0.9f;
                ring.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                ring.style.opacity = 1f - t;
                if (t >= 1f) ring.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= 0.45f);
        }

        private static void Shake(VisualElement e)
        {
            if (Prefs.ReduceMotion) return;
            float elapsed = 0f;
            e.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / 0.35f);
                e.style.translate = new StyleTranslate(new Translate(Mathf.Sin(t * Mathf.PI * 6f) * 6f * (1f - t), 0));
            }).Every(16).Until(() => elapsed >= 0.35f);
        }

        private void Celebrate()
        {
            int winner = State.Winner;
            if (!scored)
            {
                scores[winner]++;
                scored = true;
            }
            Refresh();
            Sfx.Play(Sound.Win);
            Fx.Toast(screen, $"{NameOf(winner)} connects four!", SeatColors[winner], Theme.Ink, 1.6f);
            Fx.Confetti(screen, new[] { SeatColors[winner], SeatColors[winner], SeatColors[0], SeatColors[1], SeatColors[2], SeatColors[3], Color.white }, 48, 2f);
            Sparks();
            StartWinPulse();
        }

        private void Sparks()
        {
            if (Prefs.ReduceMotion || overlay.panel == null) return;
            Color color = SeatColors[State.Winner];
            int n = 0;
            foreach (int index in State.WinningCells)
            {
                var hole = holes[PebbleDropState.RowOf(index), PebbleDropState.ColumnOf(index)];
                Vector2 at = Local(hole);
                for (int i = 0; i < 8; i++, n++)
                {
                    var spark = new VisualElement();
                    spark.pickingMode = PickingMode.Ignore;
                    spark.style.position = Position.Absolute;
                    float size = 4f + (n * 7 % 5);
                    spark.style.width = size;
                    spark.style.height = size;
                    spark.style.left = at.x - size / 2f;
                    spark.style.top = at.y - size / 2f;
                    spark.style.backgroundColor = i % 3 == 0 ? Color.white : color;
                    Theme.Radius(spark, size);
                    overlay.Add(spark);
                    float angle = (n * 47 % 360) * Mathf.Deg2Rad;
                    float dist = 30f + (n * 13 % 40);
                    float elapsed = 0f;
                    spark.schedule.Execute(timer =>
                    {
                        elapsed += timer.deltaTime / 1000f;
                        float t = Mathf.Clamp01(elapsed / 0.8f);
                        float eased = 1f - (1f - t) * (1f - t);
                        spark.style.translate = new StyleTranslate(new Translate(Mathf.Cos(angle) * dist * eased, Mathf.Sin(angle) * dist * eased));
                        spark.style.opacity = 1f - t;
                        if (t >= 1f) spark.RemoveFromHierarchy();
                    }).Every(16).Until(() => elapsed >= 0.8f);
                }
            }
        }

        private void StartWinPulse()
        {
            StopWinPulse();
            if (Prefs.ReduceMotion) return;
            float clock = 0f;
            winPulse = screen.schedule.Execute(timer =>
            {
                clock += timer.deltaTime / 1000f;
                float s = 1f + 0.1f * (0.5f + 0.5f * Mathf.Sin(clock * 6f));
                foreach (int index in State.WinningCells)
                {
                    var pebble = pebbles[PebbleDropState.RowOf(index), PebbleDropState.ColumnOf(index)];
                    if (pebble != null) pebble.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                }
            }).Every(33);
        }

        private void StopWinPulse()
        {
            winPulse?.Pause();
            winPulse = null;
        }

        // ---------- Drawing ----------

        private void Refresh()
        {
            var state = State;
            int last = state.LastCell;
            var winning = new HashSet<int>(state.WinningCells);

            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    int seat = state.CellAt(r, c);
                    var hole = holes[r, c];
                    if (seat == PebbleDropState.Empty)
                    {
                        pebbles[r, c]?.RemoveFromHierarchy();
                        pebbles[r, c] = null;
                    }
                    else
                    {
                        if (pebbles[r, c] == null || (int)pebbles[r, c].userData != seat)
                        {
                            pebbles[r, c]?.RemoveFromHierarchy();
                            var p = Pebble(seat, 0, withMark: true);
                            p.userData = seat;
                            hole.Add(p);
                            pebbles[r, c] = p;
                        }
                        var pebble = pebbles[r, c];
                        int index = PebbleDropState.Index(r, c);
                        bool win = winning.Contains(index);
                        bool isLast = index == last && !win;
                        if (win || isLast)
                        {
                            Theme.Border(pebble, win ? 3 : 2, win ? Color.white : new Color(1f, 1f, 1f, 0.8f));
                        }
                        else
                        {
                            Theme.Border(pebble, 0, Clear);
                            pebble.style.borderBottomWidth = 2;
                            pebble.style.borderBottomColor = Color.Lerp(SeatColors[seat], Color.black, 0.35f);
                        }
                        if (!win) pebble.style.scale = new StyleScale(new Scale(Vector3.one));
                    }
                }
            }

            for (int s = 0; s < Seats; s++)
            {
                bool turn = !state.IsOver && state.CurrentSeat == s;
                bool won = state.Winner == s;
                Color color = SeatColors[s];
                seatCards[s].style.backgroundColor = turn || won ? Color.Lerp(Card, color, won ? 0.35f : 0.18f) : Card;
                Theme.BorderColor(seatCards[s], turn || won ? color : Clear);
                seatCards[s].style.translate = new StyleTranslate(new Translate(0, turn ? -3 : 0));
                seatCards[s].style.opacity = state.Winner != PebbleDropState.NoSeat && !won ? 0.55f : 1f;
                seatWins[s].text = $"{scores[s]} {(scores[s] == 1 ? "win" : "wins")}";
                seatNames[s].text = NameOf(s);
                if (IsOnline)
                {
                    var info = room?.SeatAt(s);
                    string kind = s == online.Seat ? "You" : isBot[s] ? "Bot" : info != null && !info.Connected ? "Away" : "Player";
                    seatKind[s].text = kind;
                    seatKind[s].style.color = kind == "You" ? Theme.Amber : kind == "Player" ? Theme.Good : Muted;
                }
                else
                {
                    seatKind[s].text = isBot[s] ? "Bot" : "Person";
                    seatKind[s].style.color = isBot[s] ? Muted : Theme.Good;
                }
                if (turn) Fx.Pop(seatPebbles[s], 0.12f, 0.35f);
            }

            statusPebble.Clear();
            if (state.Winner != PebbleDropState.NoSeat)
            {
                statusPebble.style.display = DisplayStyle.Flex;
                statusPebble.Add(Pebble(state.Winner, 24, withMark: true));
                statusLabel.text = $"{NameOf(state.Winner)} wins! Four in a row.";
            }
            else if (state.IsDraw)
            {
                statusPebble.style.display = DisplayStyle.None;
                statusLabel.text = "The board is full. It's a draw!";
            }
            else
            {
                statusPebble.style.display = DisplayStyle.Flex;
                statusPebble.Add(Pebble(state.CurrentSeat, 24, withMark: true));
                if (IsOnline && online.Status == OnlineStatus.Reconnecting) statusLabel.text = "Reconnecting...";
                else if (IsOnline && state.CurrentSeat == online.Seat) statusLabel.text = awaitingServer ? "Dropping..." : "Your turn!";
                else statusLabel.text = isBot[state.CurrentSeat] ? $"{NameOf(state.CurrentSeat)} (bot) is thinking..." : $"{NameOf(state.CurrentSeat)}'s turn";
            }

            Theme.SetButtonEnabled(undoButton, undoStack.Count > 0);
            if (IsOnline) Theme.SetButtonEnabled(newGameButton, state.IsOver && room != null && room.IsHost);
            StyleToggle(hintsButton, hints);
            StyleToggle(marksButton, marks);
            DrawWinLine();
            DrawHover();
        }

        private void StyleToggle(Button button, bool on)
        {
            button.style.backgroundColor = on ? Color.Lerp(Card, SeatColors[3], 0.35f) : Card;
            Theme.BorderColor(button, on ? SeatColors[3] : SlateEdge);
        }

        private void DrawHover()
        {
            var state = State;
            bool over = state.IsOver;
            bool show = !over && !busy && PersonsTurn;
            int seat = over ? 0 : state.CurrentSeat;
            Color color = SeatColors[seat];

            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    cells[r, c].style.backgroundColor = !over && c == hoverColumn ? new Color(color.r, color.g, color.b, 0.13f) : Clear;
                    Theme.Border(holes[r, c], 0, Clear);
                }
            }

            ghost?.RemoveFromHierarchy();
            ghost = null;
            if (show && !state.IsColumnFull(hoverColumn))
            {
                ghost = Pebble(seat, 0, withMark: true);
                ghost.style.width = Length.Percent(78);
                ghost.style.height = Length.Percent(78);
                railCells[hoverColumn].Add(ghost);
                int row = state.LandingRow(hoverColumn);
                Theme.Border(holes[row, hoverColumn], 2, new Color(color.r, color.g, color.b, 0.8f));
            }

            var winning = hints && !over ? WinningColumns(state, state.CurrentSeat) : null;
            for (int c = 0; c < Cols; c++)
            {
                bool on = winning != null && winning.Contains(c);
                railHints[c].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                railHints[c].style.backgroundColor = color;
                railHints[c].tooltip = on ? $"{NameOf(seat)} can win here!" : null;
            }
        }

        /// <summary>Columns where this seat would connect four right now (tries each column on the shared rules).</summary>
        private List<int> WinningColumns(PebbleDropState state, int seat)
        {
            var result = new List<int>();
            for (int c = 0; c < Cols; c++)
            {
                if (state.IsColumnFull(c)) continue;
                if (!module.TryApply(state, seat, new DropAction(c), out var next, out _)) continue;
                if (next.Winner == seat) result.Add(c);
            }
            return result;
        }

        private void DrawWinLine()
        {
            if (winLine == null) return;
            var cellsInLine = State.WinningCells;
            if (cellsInLine.Count < 2 || board.panel == null)
            {
                winLine.SetLine(false, Vector2.zero, Vector2.zero);
                return;
            }
            int first = cellsInLine[0], lastCell = cellsInLine[0];
            // The line runs between the two ends of the run.
            foreach (int index in cellsInLine)
            {
                if (Order(index) < Order(first)) first = index;
                if (Order(index) > Order(lastCell)) lastCell = index;
            }
            var a = winLine.WorldToLocal(holes[PebbleDropState.RowOf(first), PebbleDropState.ColumnOf(first)].worldBound.center);
            var b = winLine.WorldToLocal(holes[PebbleDropState.RowOf(lastCell), PebbleDropState.ColumnOf(lastCell)].worldBound.center);
            winLine.SetLine(true, a, b);
        }

        // Sorts cells along any line: by column, then by row.
        private static int Order(int index) => PebbleDropState.ColumnOf(index) * 100 + PebbleDropState.RowOf(index);

        /// <summary>A white line drawn through the winning pebbles.</summary>
        private sealed class WinLine : VisualElement
        {
            private bool showLine;
            private Vector2 from, to;

            public WinLine()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0;
                style.right = 0;
                style.top = 0;
                style.bottom = 0;
                generateVisualContent += Draw;
            }

            public void SetLine(bool show, Vector2 a, Vector2 b)
            {
                if (show == showLine && a == from && b == to) return;
                showLine = show;
                from = a;
                to = b;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (!showLine) return;
                var p = context.painter2D;
                p.lineCap = LineCap.Round;
                p.strokeColor = new Color(1f, 1f, 1f, 0.85f);
                p.lineWidth = 6f;
                p.BeginPath();
                p.MoveTo(from);
                p.LineTo(to);
                p.Stroke();
            }
        }
    }
}
