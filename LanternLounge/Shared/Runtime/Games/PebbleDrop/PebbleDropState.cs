using System;
using System.Collections.Generic;

namespace LanternLounge.Games.PebbleDrop
{
    /// <summary>A move: drop a pebble into a column (0 = leftmost).</summary>
    public readonly struct DropAction
    {
        public DropAction(int column)
        {
            Column = column;
        }

        public int Column { get; }

        public override string ToString() => $"Drop in column {Column}";
    }

    /// <summary>
    /// An immutable Pebble Drop board: 16 columns by 12 rows for four players. Row 0 is the top
    /// row, so pebbles land on the highest empty row number. Cells hold the seat number
    /// (0 Red, 1 Yellow, 2 Blue, 3 Green) or <see cref="Empty"/>. Nothing is hidden, so this is
    /// also what every player sees.
    /// </summary>
    public sealed class PebbleDropState
    {
        public const int Columns = 16;
        public const int Rows = 12;
        public const int CellCount = Columns * Rows;
        public const int Players = 4;
        public const int Empty = -1;
        public const int NoSeat = -1;

        private readonly int[] cells;
        private readonly IReadOnlyList<int> winningCells;

        internal PebbleDropState(int[] cells, int firstSeat, int currentSeat, int moveCount, int winner, int[] winningCells, bool isDraw, int lastCell)
        {
            this.cells = cells;
            this.winningCells = Array.AsReadOnly(winningCells ?? Array.Empty<int>());
            FirstSeat = firstSeat;
            CurrentSeat = currentSeat;
            MoveCount = moveCount;
            Winner = winner;
            IsDraw = isDraw;
            LastCell = lastCell;
        }

        internal static PebbleDropState CreateEmpty(int firstSeat)
        {
            var empty = new int[CellCount];
            for (int i = 0; i < empty.Length; i++) empty[i] = Empty;
            return new PebbleDropState(empty, firstSeat, firstSeat, 0, NoSeat, null, false, -1);
        }

        /// <summary>
        /// Rebuilds a board from a snapshot, e.g. one the online server sent. Checks every value,
        /// so a bad snapshot throws instead of producing a broken board.
        /// </summary>
        public static PebbleDropState FromSnapshot(IReadOnlyList<int> cells, int firstSeat, int currentSeat, int moveCount,
            int winner, IReadOnlyList<int> winningCells, bool isDraw, int lastCell)
        {
            if (cells == null || cells.Count != CellCount) throw new ArgumentException($"A board has {CellCount} cells.", nameof(cells));
            var copy = new int[CellCount];
            for (int i = 0; i < CellCount; i++)
            {
                int v = cells[i];
                if (v != Empty && (v < 0 || v >= Players)) throw new ArgumentOutOfRangeException(nameof(cells));
                copy[i] = v;
            }
            if (firstSeat < 0 || firstSeat >= Players) throw new ArgumentOutOfRangeException(nameof(firstSeat));
            if (currentSeat != NoSeat && (currentSeat < 0 || currentSeat >= Players)) throw new ArgumentOutOfRangeException(nameof(currentSeat));
            if (winner != NoSeat && (winner < 0 || winner >= Players)) throw new ArgumentOutOfRangeException(nameof(winner));
            if (lastCell < -1 || lastCell >= CellCount) throw new ArgumentOutOfRangeException(nameof(lastCell));
            int[] line = null;
            if (winningCells != null && winningCells.Count > 0)
            {
                line = new int[winningCells.Count];
                for (int i = 0; i < line.Length; i++)
                {
                    if (winningCells[i] < 0 || winningCells[i] >= CellCount) throw new ArgumentOutOfRangeException(nameof(winningCells));
                    line[i] = winningCells[i];
                }
            }
            return new PebbleDropState(copy, firstSeat, currentSeat, Math.Max(0, moveCount), winner, line, isDraw, lastCell);
        }

        /// <summary>The seat that moved first this match (each rematch moves it on one seat).</summary>
        public int FirstSeat { get; }

        /// <summary>The seat to move, or <see cref="NoSeat"/> once the match is over.</summary>
        public int CurrentSeat { get; }

        public int MoveCount { get; }

        /// <summary>The winning seat, or <see cref="NoSeat"/>.</summary>
        public int Winner { get; }

        public bool IsDraw { get; }
        public bool IsOver => Winner != NoSeat || IsDraw;

        /// <summary>Cell index of the most recent pebble (for the drop animation), or -1.</summary>
        public int LastCell { get; }

        /// <summary>Cell indexes of the winning line (four or more), empty if nobody has won.</summary>
        public IReadOnlyList<int> WinningCells => winningCells;

        public static int Index(int row, int column) => row * Columns + column;
        public static int RowOf(int index) => index / Columns;
        public static int ColumnOf(int index) => index % Columns;

        public int CellAt(int row, int column)
        {
            if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
            return cells[Index(row, column)];
        }

        public bool IsColumnFull(int column)
        {
            CheckColumn(column);
            return cells[Index(0, column)] != Empty;
        }

        /// <summary>The row a pebble would land on, or -1 if the column is full.</summary>
        public int LandingRow(int column) => PebbleDropRules.LandingRow(cells, column);

        /// <summary>How many empty spaces a column has left.</summary>
        public int OpenSpaces(int column)
        {
            CheckColumn(column);
            int open = 0;
            for (int row = 0; row < Rows; row++)
            {
                if (cells[Index(row, column)] == Empty) open++;
            }
            return open;
        }

        internal int[] CopyCells() => (int[])cells.Clone();

        private static void CheckColumn(int column)
        {
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
        }
    }
}
