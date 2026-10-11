using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>Whether a match has ended, and how. Each game decides when that is.</summary>
    public sealed class MatchResult
    {
        public static readonly MatchResult InProgress = new MatchResult(false, Array.Empty<int>(), false);

        private MatchResult(bool isOver, IReadOnlyList<int> winningSeats, bool isDraw)
        {
            IsOver = isOver;
            WinningSeats = winningSeats;
            IsDraw = isDraw;
        }

        public bool IsOver { get; }

        /// <summary>Seats that won. More than one for shared wins; empty for a draw or an unfinished match.</summary>
        public IReadOnlyList<int> WinningSeats { get; }

        public bool IsDraw { get; }

        public static MatchResult Win(params int[] seats)
        {
            if (seats == null || seats.Length == 0) throw new ArgumentException("A win needs at least one seat.", nameof(seats));
            return new MatchResult(true, Array.AsReadOnly((int[])seats.Clone()), false);
        }

        public static MatchResult Draw() => new MatchResult(true, Array.Empty<int>(), true);
    }
}
