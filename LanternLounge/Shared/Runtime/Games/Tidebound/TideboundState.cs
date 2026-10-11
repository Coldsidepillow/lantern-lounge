using System;
using System.Collections.Generic;

namespace LanternLounge.Games.Tidebound
{
    /// <summary>A move: ask another diver for every card of one creature you already hold.</summary>
    public readonly struct AskAction
    {
        public AskAction(int targetSeat, int creature)
        {
            TargetSeat = targetSeat;
            Creature = creature;
        }

        public int TargetSeat { get; }
        public int Creature { get; }

        public override string ToString() => $"Ask seat {TargetSeat} for creature {Creature}";
    }

    /// <summary>
    /// The full, authoritative Tidebound table, including every hand and the order of the Current.
    /// Never send this to a player: send <see cref="TideboundView"/> from
    /// <see cref="TideboundModule.ViewFor"/> instead. Immutable once built.
    /// </summary>
    public sealed class TideboundState
    {
        public const int NoSeat = -1;

        private readonly IReadOnlyList<int>[] hands;
        private readonly IReadOnlyList<int> current;
        private readonly IReadOnlyList<int>[] pods;

        internal TideboundState(int[][] hands, int[] current, int[][] pods, int currentSeat, int firstSeat, int turn, TideEvent[] lastEvents)
        {
            PlayerCount = hands.Length;
            this.hands = new IReadOnlyList<int>[PlayerCount];
            this.pods = new IReadOnlyList<int>[PlayerCount];
            int podTotal = 0;
            for (int s = 0; s < PlayerCount; s++)
            {
                this.hands[s] = Array.AsReadOnly(hands[s]);
                this.pods[s] = Array.AsReadOnly(pods[s]);
                podTotal += pods[s].Length;
            }
            this.current = Array.AsReadOnly(current);
            CurrentSeat = currentSeat;
            FirstSeat = firstSeat;
            Turn = turn;
            TotalPods = podTotal;
            LastEvents = Array.AsReadOnly(lastEvents);
        }

        public int PlayerCount { get; }

        /// <summary>The diver whose tide it is, or <see cref="NoSeat"/> once the game is over.</summary>
        public int CurrentSeat { get; }

        public int FirstSeat { get; }

        /// <summary>How many asks have been made.</summary>
        public int Turn { get; }

        public int TotalPods { get; }

        public bool IsOver => CurrentSeat == NoSeat;

        /// <summary>What the last move (or the deal) caused, unredacted.</summary>
        public IReadOnlyList<TideEvent> LastEvents { get; }

        /// <summary>A diver's hand, sorted by card number. Server/authority use only.</summary>
        public IReadOnlyList<int> HandOf(int seat) => hands[CheckSeat(seat)];

        public int HandCount(int seat) => hands[CheckSeat(seat)].Count;

        /// <summary>Creature ids whose pods this diver has completed, in the order completed.</summary>
        public IReadOnlyList<int> PodsOf(int seat) => pods[CheckSeat(seat)];

        public int CurrentCount => current.Count;

        /// <summary>The Current, top card first. Server/authority use only.</summary>
        public IReadOnlyList<int> CurrentCards => current;

        public int CountInHand(int seat, int creature)
        {
            int count = 0;
            foreach (int card in hands[CheckSeat(seat)])
            {
                if (TideboundCards.CreatureOf(card) == creature) count++;
            }
            return count;
        }

        /// <summary>Seats with the most pods. Only meaningful once the game is over.</summary>
        public IReadOnlyList<int> Leaders()
        {
            int best = -1;
            var leaders = new List<int>();
            for (int s = 0; s < PlayerCount; s++)
            {
                int n = pods[s].Count;
                if (n > best)
                {
                    best = n;
                    leaders.Clear();
                    leaders.Add(s);
                }
                else if (n == best)
                {
                    leaders.Add(s);
                }
            }
            return leaders;
        }

        private int CheckSeat(int seat)
        {
            if (seat < 0 || seat >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return seat;
        }
    }

    /// <summary>
    /// What one diver is allowed to see: their own hand, how many cards everyone else holds,
    /// every completed pod, and how many cards remain in the Current, but never which ones.
    /// </summary>
    public sealed class TideboundView
    {
        internal TideboundView(TideboundState state, int viewer)
        {
            Viewer = viewer;
            PlayerCount = state.PlayerCount;
            CurrentSeat = state.CurrentSeat;
            CurrentCount = state.CurrentCount;
            Turn = state.Turn;
            MyHand = state.HandOf(viewer);

            var counts = new int[PlayerCount];
            var pods = new IReadOnlyList<int>[PlayerCount];
            for (int s = 0; s < PlayerCount; s++)
            {
                counts[s] = state.HandCount(s);
                pods[s] = state.PodsOf(s);
            }
            HandCounts = Array.AsReadOnly(counts);
            Pods = Array.AsReadOnly(pods);

            var events = new TideEvent[state.LastEvents.Count];
            for (int i = 0; i < events.Length; i++)
            {
                var e = state.LastEvents[i];
                events[i] = e.IsCardPrivate && e.Seat != viewer ? e.WithCardHidden() : e;
            }
            LastEvents = Array.AsReadOnly(events);
            Winners = state.IsOver ? state.Leaders() : Array.Empty<int>();
        }

        public int Viewer { get; }
        public int PlayerCount { get; }
        public int CurrentSeat { get; }
        public bool IsOver => CurrentSeat == TideboundState.NoSeat;
        public bool IsMyTide => CurrentSeat == Viewer;
        public int Turn { get; }
        public int CurrentCount { get; }

        /// <summary>The viewer's own cards, sorted by card number.</summary>
        public IReadOnlyList<int> MyHand { get; }

        public IReadOnlyList<int> HandCounts { get; }
        public IReadOnlyList<IReadOnlyList<int>> Pods { get; }

        /// <summary>The last move's events, with other divers' drawn cards hidden.</summary>
        public IReadOnlyList<TideEvent> LastEvents { get; }

        /// <summary>Seats with the most pods once the game is over; empty before then.</summary>
        public IReadOnlyList<int> Winners { get; }

        public int CountInMyHand(int creature)
        {
            int count = 0;
            foreach (int card in MyHand)
            {
                if (TideboundCards.CreatureOf(card) == creature) count++;
            }
            return count;
        }

        /// <summary>Distinct creatures in the viewer's hand, lowest id first.</summary>
        public IReadOnlyList<int> MyCreatures()
        {
            var result = new List<int>();
            foreach (int card in MyHand)
            {
                int creature = TideboundCards.CreatureOf(card);
                if (result.Count == 0 || result[result.Count - 1] != creature) result.Add(creature);
            }
            return result;
        }
    }
}
