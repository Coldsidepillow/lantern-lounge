using System.Collections.Generic;

namespace LanternLounge.Games.PebbleDrop
{
    /// <summary>Board maths shared by the module and the bot.</summary>
    internal static class PebbleDropRules
    {
        private static readonly int[,] Directions = { { 0, 1 }, { 1, 0 }, { 1, 1 }, { 1, -1 } };

        public static int LandingRow(int[] cells, int column)
        {
            if (column < 0 || column >= PebbleDropState.Columns) return -1;
            for (int row = PebbleDropState.Rows - 1; row >= 0; row--)
            {
                if (cells[PebbleDropState.Index(row, column)] == PebbleDropState.Empty) return row;
            }
            return -1;
        }

        /// <summary>
        /// Looks for four or more of <paramref name="seat"/>'s pebbles in a line through (row, column):
        /// across, up and down, and both slants. Returns the line's cell indexes, or null.
        /// </summary>
        public static int[] FindLine(int[] cells, int row, int column, int seat)
        {
            for (int d = 0; d < 4; d++)
            {
                int dr = Directions[d, 0];
                int dc = Directions[d, 1];
                var line = new List<int> { PebbleDropState.Index(row, column) };

                Walk(cells, row, column, dr, dc, seat, line);
                Walk(cells, row, column, -dr, -dc, seat, line);

                if (line.Count >= 4) return line.ToArray();
            }
            return null;
        }

        private static void Walk(int[] cells, int row, int column, int dr, int dc, int seat, List<int> line)
        {
            for (int step = 1; step < PebbleDropState.Columns; step++)
            {
                int r = row + dr * step;
                int c = column + dc * step;
                if (r < 0 || r >= PebbleDropState.Rows || c < 0 || c >= PebbleDropState.Columns) return;
                int index = PebbleDropState.Index(r, c);
                if (cells[index] != seat) return;
                line.Add(index);
            }
        }

        /// <summary>Would dropping in this column win immediately for this seat?</summary>
        public static bool WouldWin(int[] cells, int column, int seat)
        {
            int row = LandingRow(cells, column);
            if (row < 0) return false;
            int index = PebbleDropState.Index(row, column);
            cells[index] = seat;
            bool wins = FindLine(cells, row, column, seat) != null;
            cells[index] = PebbleDropState.Empty;
            return wins;
        }
    }
}
