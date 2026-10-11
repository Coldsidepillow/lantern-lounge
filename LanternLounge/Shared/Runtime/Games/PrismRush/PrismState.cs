using System;
using System.Collections.Generic;

namespace LanternLounge.Games.PrismRush
{
    /// <summary>
    /// The full, authoritative Prism Rush table, including every hand and the draw deck order.
    /// Never send this to a player: send <see cref="PrismView"/> from <see cref="PrismModule.ViewFor"/>.
    /// Immutable once built.
    /// </summary>
    public sealed class PrismState
    {
        public const int NoSeat = -1;
        public const int NoCard = -1;

        private readonly IReadOnlyList<int>[] hands;

        internal PrismState(int[][] hands, int[] drawPile, int[] discard, int activeColor, int direction, int pendingDraw,
            int currentSeat, int drawnCard, int passes, int[] winners, ulong randomState, int firstSeat, int turn, PrismEvent[] lastEvents)
        {
            PlayerCount = hands.Length;
            this.hands = new IReadOnlyList<int>[PlayerCount];
            for (int s = 0; s < PlayerCount; s++) this.hands[s] = Array.AsReadOnly(hands[s]);
            DrawPile = Array.AsReadOnly(drawPile);
            Discard = Array.AsReadOnly(discard);
            ActiveColor = activeColor;
            Direction = direction;
            PendingDraw = pendingDraw;
            CurrentSeat = currentSeat;
            DrawnCard = drawnCard;
            Passes = passes;
            Winners = Array.AsReadOnly(winners);
            RandomState = randomState;
            FirstSeat = firstSeat;
            Turn = turn;
            LastEvents = Array.AsReadOnly(lastEvents);
        }

        public int PlayerCount { get; }

        /// <summary>The draw deck, top card first. Server/authority use only.</summary>
        public IReadOnlyList<int> DrawPile { get; }

        /// <summary>The played pile, bottom first; the last card is the top card.</summary>
        public IReadOnlyList<int> Discard { get; }

        public int TopCard => Discard[Discard.Count - 1];

        /// <summary>The color in play (0..3). Usually the top card's color; chosen by the player after a Prism.</summary>
        public int ActiveColor { get; }

        /// <summary>+1 for clockwise (seat 0, 1, 2, ...), -1 after a Flip.</summary>
        public int Direction { get; }

        /// <summary>Cards the current player must draw because of Pulses, unless they answer with a Pulse.</summary>
        public int PendingDraw { get; }

        public int CurrentSeat { get; }

        /// <summary>A card the current player just drew and may play now, or <see cref="NoCard"/>.</summary>
        public int DrawnCard { get; }

        /// <summary>How many players in a row could not draw anything (ends the game when it reaches PlayerCount).</summary>
        public int Passes { get; }

        public IReadOnlyList<int> Winners { get; }

        public bool IsOver => CurrentSeat == NoSeat;

        /// <summary>The seeded generator's state, so reshuffles stay reproducible.</summary>
        public ulong RandomState { get; }

        public int FirstSeat { get; }
        public int Turn { get; }

        public IReadOnlyList<PrismEvent> LastEvents { get; }

        public IReadOnlyList<int> HandOf(int seat) => hands[CheckSeat(seat)];

        public int HandCount(int seat) => hands[CheckSeat(seat)].Count;

        public int Next(int seat, int steps = 1)
        {
            int n = PlayerCount;
            return (((seat + Direction * steps) % n) + n) % n;
        }

        private int CheckSeat(int seat)
        {
            if (seat < 0 || seat >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return seat;
        }
    }

    /// <summary>
    /// What one player may see: their own hand, everyone's card counts, the top of the pile,
    /// the color in play, the direction, any Pulse penalty, and how many cards are left to draw.
    /// </summary>
    public sealed class PrismView
    {
        internal PrismView(PrismState state, int viewer)
        {
            Viewer = viewer;
            PlayerCount = state.PlayerCount;
            MyHand = state.HandOf(viewer);
            var counts = new int[PlayerCount];
            for (int s = 0; s < PlayerCount; s++) counts[s] = state.HandCount(s);
            HandCounts = Array.AsReadOnly(counts);
            TopCard = state.TopCard;
            ActiveColor = state.ActiveColor;
            Direction = state.Direction;
            PendingDraw = state.PendingDraw;
            CurrentSeat = state.CurrentSeat;
            DrawPileCount = state.DrawPile.Count;
            DiscardCount = state.Discard.Count;
            DrawnCard = state.CurrentSeat == viewer ? state.DrawnCard : PrismState.NoCard;
            Winners = state.Winners;
            Turn = state.Turn;

            var events = new PrismEvent[state.LastEvents.Count];
            for (int i = 0; i < events.Length; i++)
            {
                var e = state.LastEvents[i];
                events[i] = e.Kind == PrismEventKind.Drew && e.Seat != viewer ? e.WithCardHidden() : e;
            }
            LastEvents = Array.AsReadOnly(events);
        }

        public int Viewer { get; }
        public int PlayerCount { get; }
        public IReadOnlyList<int> MyHand { get; }
        public IReadOnlyList<int> HandCounts { get; }
        public int TopCard { get; }
        public int ActiveColor { get; }
        public int Direction { get; }
        public int PendingDraw { get; }
        public int CurrentSeat { get; }
        public bool IsOver => CurrentSeat == PrismState.NoSeat;
        public bool IsMyTurn => CurrentSeat == Viewer;
        public int DrawPileCount { get; }
        public int DiscardCount { get; }

        /// <summary>The card you just drew and may play now, or -1. Only set for the player whose turn it is.</summary>
        public int DrawnCard { get; }

        public IReadOnlyList<int> Winners { get; }
        public int Turn { get; }
        public IReadOnlyList<PrismEvent> LastEvents { get; }

        public int Next(int seat, int steps = 1)
        {
            int n = PlayerCount;
            return (((seat + Direction * steps) % n) + n) % n;
        }

        /// <summary>Can this card go on the pile right now? (Ignores whose turn it is.)</summary>
        public bool CanPlay(int card) => PrismModule.IsPlayable(card, TopCard, ActiveColor, PendingDraw);
    }
}
