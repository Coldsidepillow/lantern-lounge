using System;
using System.Collections.Generic;

namespace LanternLounge.Games.PrismRush
{
    /// <summary>
    /// A Prism Rush bot that decides only from its own <see cref="PrismView"/>, the same thing a
    /// human player sees. It plays its biggest chain, saves Prisms for when it is stuck, answers
    /// Pulses when it can, and uses actions to slow down a player who is about to win.
    /// </summary>
    public static class PrismBot
    {
        public static PrismMove ChooseMove(PrismView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!view.IsMyTurn) throw new InvalidOperationException("It isn't the bot's turn.");
            var hand = view.MyHand;

            if (view.DrawnCard >= 0)
            {
                int drawn = view.DrawnCard;
                return PrismCards.Card(drawn).Kind == CardKind.Prism
                    ? PrismMove.PlayWithColor(FavouriteColor(hand), drawn)
                    : PrismMove.Play(drawn);
            }

            if (view.PendingDraw > 0)
            {
                foreach (int card in hand)
                {
                    if (PrismCards.Card(card).Kind == CardKind.Pulse) return PrismMove.Play(card);
                }
                return PrismMove.Draw();
            }

            int next = view.Next(view.Viewer);
            bool danger = view.HandCounts[next] <= 2;
            List<int> best = null;
            int bestScore = int.MinValue;

            foreach (int card in hand)
            {
                if (!view.CanPlay(card)) continue;
                var c = PrismCards.Card(card);
                List<int> candidate;
                int score;
                if (c.Kind == CardKind.Number)
                {
                    candidate = new List<int> { card };
                    foreach (int other in hand)
                    {
                        var o = PrismCards.Card(other);
                        if (other != card && o.Kind == CardKind.Number && o.Number == c.Number) candidate.Add(other);
                    }
                    score = candidate.Count * 10 + (c.Color == view.ActiveColor ? 3 : 0);
                }
                else if (c.Kind == CardKind.Prism)
                {
                    candidate = new List<int> { card };
                    score = -5;
                }
                else
                {
                    candidate = new List<int> { card };
                    score = 8 + (danger ? 15 : 0);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null) return PrismMove.Draw();
            bool wild = PrismCards.Card(best[best.Count - 1]).Kind == CardKind.Prism;
            return PrismMove.PlayCards(best, wild ? FavouriteColor(hand) : -1);
        }

        /// <summary>The color the bot holds most of (lowest color on ties).</summary>
        public static int FavouriteColor(IReadOnlyList<int> hand)
        {
            var counts = new int[PrismCards.ColorCount];
            foreach (int card in hand)
            {
                int color = PrismCards.Card(card).Color;
                if (color >= 0) counts[color]++;
            }
            int best = 0;
            for (int c = 1; c < counts.Length; c++)
            {
                if (counts[c] > counts[best]) best = c;
            }
            return best;
        }
    }
}
