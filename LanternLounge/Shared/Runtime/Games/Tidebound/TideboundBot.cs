using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.Tidebound
{
    /// <summary>
    /// A Reef Bot. It decides only from its own <see cref="TideboundView"/>, the same thing a
    /// human player sees, so it never peeks at hidden hands or the Current. It remembers what
    /// other divers have asked for, because asking reveals that you hold that creature.
    ///
    /// Call <see cref="Observe"/> with the bot's view after every move (and after the deal),
    /// then <see cref="ChooseAsk"/> when it is the bot's tide.
    /// </summary>
    public sealed class TideboundBot
    {
        private readonly int seat;
        private readonly SeededRandom random;
        private readonly bool[,] knownToHold;
        private int lastObservedTurn = -1;

        public TideboundBot(int seat, int playerCount, SeededRandom random)
        {
            if (playerCount < 2) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (seat < 0 || seat >= playerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            this.seat = seat;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            knownToHold = new bool[playerCount, TideboundCards.CreatureCount];
        }

        public int Seat => seat;

        /// <summary>Learns from the public events of the latest move. Safe to call more than once per move.</summary>
        public void Observe(TideboundView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.Viewer != seat) throw new ArgumentException("That view belongs to another seat.", nameof(view));
            if (view.Turn == lastObservedTurn) return;
            lastObservedTurn = view.Turn;

            foreach (var e in view.LastEvents)
            {
                switch (e.Kind)
                {
                    case TideEventKind.Asked:
                        // Asking proves the asker holds that creature.
                        knownToHold[e.Seat, e.Creature] = true;
                        // Either way, the asked diver no longer holds any.
                        knownToHold[e.TargetSeat, e.Creature] = false;
                        break;
                    case TideEventKind.LuckyDive:
                        knownToHold[e.Seat, e.Creature] = true;
                        break;
                    case TideEventKind.PodFormed:
                        // The pair left that diver's hand; the creature's other pair may still be out there.
                        knownToHold[e.Seat, e.Creature] = false;
                        break;
                }
            }

            // A diver with no cards holds nothing.
            for (int s = 0; s < view.PlayerCount; s++)
            {
                if (view.HandCounts[s] != 0) continue;
                for (int c = 0; c < TideboundCards.CreatureCount; c++) knownToHold[s, c] = false;
            }
        }

        public AskAction ChooseAsk(TideboundView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!view.IsMyTide) throw new InvalidOperationException("It isn't this bot's tide.");
            Observe(view);

            var mine = view.MyCreatures();
            if (mine.Count == 0) throw new InvalidOperationException("The bot has no cards to ask with.");

            // 1. Ask someone known to hold a creature we also hold, preferring our biggest sets.
            var byCount = new List<int>(mine);
            byCount.Sort((a, b) => view.CountInMyHand(b).CompareTo(view.CountInMyHand(a)));
            foreach (int creature in byCount)
            {
                for (int step = 1; step < view.PlayerCount; step++)
                {
                    int target = (seat + step) % view.PlayerCount;
                    if (view.HandCounts[target] > 0 && knownToHold[target, creature])
                        return new AskAction(target, creature);
                }
            }

            // 2. Otherwise: our most-held creature (random among ties), from a random diver with cards.
            int top = view.CountInMyHand(byCount[0]);
            var best = new List<int>();
            foreach (int creature in byCount)
            {
                if (view.CountInMyHand(creature) == top) best.Add(creature);
            }

            var targets = new List<int>();
            for (int s = 0; s < view.PlayerCount; s++)
            {
                if (s != seat && view.HandCounts[s] > 0) targets.Add(s);
            }
            if (targets.Count == 0) throw new InvalidOperationException("No diver has cards to ask for.");

            return new AskAction(targets[random.NextInt(targets.Count)], best[random.NextInt(best.Count)]);
        }
    }
}
