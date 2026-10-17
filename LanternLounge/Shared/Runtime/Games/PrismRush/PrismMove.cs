using System;
using System.Collections.Generic;

namespace LanternLounge.Games.PrismRush
{
    public enum MoveKind
    {
        /// <summary>Play one card, or a chain of number cards that all share the same number.</summary>
        Play,

        /// <summary>Draw until you get a card you can play (or take the Pulse penalty).</summary>
        Draw,

        /// <summary>After drawing a card you could play, keep it instead and end your turn.</summary>
        Keep
    }

    /// <summary>A Prism Rush move. Build one with the static helpers.</summary>
    public sealed class PrismMove
    {
        private static readonly IReadOnlyList<int> NoCards = Array.Empty<int>();

        private PrismMove(MoveKind kind, IReadOnlyList<int> cards, int chosenColor)
        {
            Kind = kind;
            Cards = cards;
            ChosenColor = chosenColor;
        }

        public MoveKind Kind { get; }

        /// <summary>The cards to play, in the order they go onto the pile. The last one is the new top card.</summary>
        public IReadOnlyList<int> Cards { get; }

        /// <summary>The color chosen when the top card played is a Prism, otherwise -1.</summary>
        public int ChosenColor { get; }

        public static PrismMove Play(params int[] cards) => PlayWithColor(-1, cards);

        public static PrismMove PlayWithColor(int chosenColor, params int[] cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            return new PrismMove(MoveKind.Play, Array.AsReadOnly((int[])cards.Clone()), chosenColor);
        }

        public static PrismMove PlayCards(IEnumerable<int> cards, int chosenColor = -1)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            return PlayWithColor(chosenColor, new List<int>(cards).ToArray());
        }

        public static PrismMove Draw() => new PrismMove(MoveKind.Draw, NoCards, -1);

        public static PrismMove Keep() => new PrismMove(MoveKind.Keep, NoCards, -1);

        public override string ToString() =>
            Kind == MoveKind.Play ? $"Play {string.Join(", ", Cards)}" + (ChosenColor >= 0 ? $" (choose {ChosenColor})" : "") : Kind.ToString();
    }

    public enum PrismEventKind
    {
        /// <summary>Seat played Cards; Color is the color now in play.</summary>
        Played,

        /// <summary>Seat drew Count cards, stopping at the first playable one (Card, hidden from others, is the last card drawn).</summary>
        Drew,

        /// <summary>Seat drew Count cards for a Pulse they could not answer.</summary>
        PulsePenalty,

        /// <summary>Seat kept the playable card they just drew.</summary>
        Kept,

        /// <summary>Direction reversed. Count is the new direction (+1 or -1).</summary>
        Flipped,

        /// <summary>Seat (the skipped player) loses their turn.</summary>
        Hopped,

        /// <summary>A Pulse was played; Count is how many cards the next player now owes.</summary>
        Pulsed,

        /// <summary>The played pile (except its top card) was shuffled back into the draw deck.</summary>
        Reshuffled,

        /// <summary>Seat has one card left.</summary>
        LastLight,

        /// <summary>Seat could not draw because no cards were left anywhere.</summary>
        NothingToDraw,

        /// <summary>Seat emptied their hand and wins.</summary>
        Won,

        /// <summary>Nobody could play or draw; fewest cards wins.</summary>
        Stalemate
    }

    public readonly struct PrismEvent
    {
        public const int Hidden = -1;
        private static readonly IReadOnlyList<int> NoCards = Array.Empty<int>();

        public PrismEvent(PrismEventKind kind, int seat, int count = 0, int card = Hidden, int color = -1, IReadOnlyList<int> cards = null)
        {
            Kind = kind;
            Seat = seat;
            Count = count;
            Card = card;
            Color = color;
            Cards = cards ?? NoCards;
        }

        public PrismEventKind Kind { get; }
        public int Seat { get; }
        public int Count { get; }

        /// <summary>The drawn card for <see cref="PrismEventKind.Drew"/>, or <see cref="Hidden"/> for other viewers.</summary>
        public int Card { get; }

        public int Color { get; }

        /// <summary>The cards played, for <see cref="PrismEventKind.Played"/>.</summary>
        public IReadOnlyList<int> Cards { get; }

        internal PrismEvent WithCardHidden() => new PrismEvent(Kind, Seat, Count, Hidden, Color, Cards);
    }
}
