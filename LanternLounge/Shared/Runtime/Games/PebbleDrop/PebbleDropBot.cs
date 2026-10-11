using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.PebbleDrop
{
    public enum BotLevel
    {
        /// <summary>Any open column, at random.</summary>
        Easy,

        /// <summary>Takes a win, blocks other players' wins, avoids handing the next player a win, likes the centre.</summary>
        Normal
    }

    /// <summary>
    /// A simple opponent for practice and for filling an empty seat. It only reads the board,
    /// so it can run on the client for local play or on the server for online play.
    /// </summary>
    public static class PebbleDropBot
    {
        public static DropAction ChooseMove(PebbleDropState state, int seat, SeededRandom random, BotLevel level)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (state.IsOver) throw new InvalidOperationException("The match is over.");
            if (seat != state.CurrentSeat) throw new InvalidOperationException("It isn't the bot's turn.");

            var open = new List<int>();
            foreach (int column in PebbleDropModule.CentreFirst)
            {
                if (!state.IsColumnFull(column)) open.Add(column);
            }

            if (level == BotLevel.Easy) return new DropAction(open[random.NextInt(open.Count)]);

            int[] cells = state.CopyCells();

            // 1. Win now if possible.
            foreach (int column in open)
            {
                if (PebbleDropRules.WouldWin(cells, column, seat)) return new DropAction(column);
            }

            // 2. Block a win, starting with the player who moves next (the most urgent threat).
            for (int step = 1; step < PebbleDropState.Players; step++)
            {
                int other = (seat + step) % PebbleDropState.Players;
                foreach (int column in open)
                {
                    if (PebbleDropRules.WouldWin(cells, column, other)) return new DropAction(column);
                }
            }

            // 3. Prefer centre columns that don't let the next player win on top of our pebble.
            int next = PebbleDropModule.NextSeat(seat);
            foreach (int column in open)
            {
                if (IsSafe(cells, column, seat, next)) return new DropAction(column);
            }

            // 4. Every move hands over a win: take the centre-most anyway.
            return new DropAction(open[0]);
        }

        private static bool IsSafe(int[] cells, int column, int seat, int opponent)
        {
            int row = PebbleDropRules.LandingRow(cells, column);
            int index = PebbleDropState.Index(row, column);
            cells[index] = seat;
            bool safe = !PebbleDropRules.WouldWin(cells, column, opponent);
            cells[index] = PebbleDropState.Empty;
            return safe;
        }
    }
}
