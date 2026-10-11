using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.PebbleDrop
{
    /// <summary>
    /// Pebble Drop: four players (Red, Yellow, Blue, Green) take turns dropping pebbles into a
    /// 16-column, 12-row board. The first to line up four of their own pebbles across, up and
    /// down, or on either slant wins. A full board with no line is a draw.
    ///
    /// Options read from <see cref="MatchConfig.Options"/>:
    ///   "firstSeat" = "0".."3", or "random" (default; uses the match seed).
    /// </summary>
    public sealed class PebbleDropModule : GameModule<PebbleDropState, DropAction, PebbleDropState>
    {
        public const string GameId = "pebble-drop";

        public const string ColumnOutOfRange = "column_out_of_range";
        public const string ColumnFull = "column_full";

        public static readonly GameInfo Definition =
            new GameInfo(GameId, "Pebble Drop", PebbleDropState.Players, PebbleDropState.Players, GameStatus.Playable, "pebble-drop");

        // Centre columns first: the order both the timeout and the bot fall back to.
        internal static readonly int[] CentreFirst = BuildCentreFirst();

        public static readonly string[] ColorNames = { "Red", "Yellow", "Blue", "Green" };

        private static int[] BuildCentreFirst()
        {
            var order = new int[PebbleDropState.Columns];
            int left = PebbleDropState.Columns / 2 - 1, right = left + 1, i = 0;
            while (i < order.Length)
            {
                if (right < PebbleDropState.Columns) order[i++] = right++;
                if (left >= 0 && i < order.Length) order[i++] = left--;
            }
            return order;
        }

        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        public override GameInfo Info => Definition;

        protected override PebbleDropState CreateInitialState(MatchConfig config)
        {
            string first = config.GetOption("firstSeat", "random");
            if (!int.TryParse(first, out int seat) || seat < 0 || seat >= PebbleDropState.Players)
            {
                seat = new SeededRandom(config.Seed).NextInt(PebbleDropState.Players);
            }
            return PebbleDropState.CreateEmpty(seat);
        }

        /// <summary>A fresh board for a rematch; the next player round the table moves first.</summary>
        public PebbleDropState Rematch(PebbleDropState previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            return PebbleDropState.CreateEmpty(NextSeat(previous.FirstSeat));
        }

        /// <summary>A fresh board with a chosen first player (Reset).</summary>
        public PebbleDropState NewBoard(int firstSeat)
        {
            if (firstSeat < 0 || firstSeat >= PebbleDropState.Players) throw new ArgumentOutOfRangeException(nameof(firstSeat));
            return PebbleDropState.CreateEmpty(firstSeat);
        }

        public static int NextSeat(int seat) => (seat + 1) % PebbleDropState.Players;

        public override ValidationResult Validate(PebbleDropState state, int seat, DropAction action)
        {
            if (state == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no board.");
            if (state.IsOver) return ValidationResult.Fail(ErrorCodes.GameOver, "This match is already over.");
            if (seat < 0 || seat >= PebbleDropState.Players) return ValidationResult.Fail(ErrorCodes.BadSeat, "Pebble Drop has seats 0 to 3 only.");
            if (seat != state.CurrentSeat) return ValidationResult.Fail(ErrorCodes.NotYourTurn, "It isn't your turn yet.");
            if (action.Column < 0 || action.Column >= PebbleDropState.Columns)
                return ValidationResult.Fail(ColumnOutOfRange, "Pick one of the 16 columns.");
            if (state.IsColumnFull(action.Column))
                return ValidationResult.Fail(ColumnFull, "That column is full. Pick another one.");
            return ValidationResult.Success;
        }

        protected override PebbleDropState ApplyValidated(PebbleDropState state, int seat, DropAction action)
        {
            int[] cells = state.CopyCells();
            int row = PebbleDropRules.LandingRow(cells, action.Column);
            int index = PebbleDropState.Index(row, action.Column);
            cells[index] = seat;

            int[] line = PebbleDropRules.FindLine(cells, row, action.Column, seat);
            int moves = state.MoveCount + 1;
            bool won = line != null;
            bool draw = !won && moves == PebbleDropState.CellCount;
            int next = won || draw ? PebbleDropState.NoSeat : NextSeat(seat);

            return new PebbleDropState(
                cells,
                state.FirstSeat,
                next,
                moves,
                won ? seat : PebbleDropState.NoSeat,
                line,
                draw,
                index);
        }

        /// <summary>Nothing is hidden in Pebble Drop, so every seat sees the whole board.</summary>
        public override PebbleDropState ViewFor(PebbleDropState state, int seat) => state;

        public override MatchResult Result(PebbleDropState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Winner != PebbleDropState.NoSeat) return MatchResult.Win(state.Winner);
            if (state.IsDraw) return MatchResult.Draw();
            return MatchResult.InProgress;
        }

        public override IReadOnlyList<int> SeatsToAct(PebbleDropState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.IsOver ? NoSeats : new[] { state.CurrentSeat };
        }

        /// <summary>When a turn timer runs out, the pebble goes into the centre-most open column.</summary>
        public override bool TryGetTimeoutAction(PebbleDropState state, int seat, out DropAction action)
        {
            action = default;
            if (state == null || state.IsOver || seat != state.CurrentSeat) return false;
            foreach (int column in CentreFirst)
            {
                if (!state.IsColumnFull(column))
                {
                    action = new DropAction(column);
                    return true;
                }
            }
            return false;
        }
    }
}
