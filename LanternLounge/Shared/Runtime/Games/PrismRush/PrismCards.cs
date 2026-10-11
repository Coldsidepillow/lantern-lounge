using System;
using System.Collections.Generic;

namespace LanternLounge.Games.PrismRush
{
    /// <summary>The four prism colors. Each also has its own shape, so colors never rely on hue alone.</summary>
    public enum PrismColor
    {
        Cyan = 0,
        Gold = 1,
        Rose = 2,
        Lime = 3
    }

    public enum CardKind
    {
        Number,

        /// <summary>Reverses the direction of play.</summary>
        Flip,

        /// <summary>The next player is skipped.</summary>
        Hop,

        /// <summary>The next player draws 2, unless they answer with a Pulse of their own.</summary>
        Pulse,

        /// <summary>Wild: playable on anything; the player chooses the next color.</summary>
        Prism
    }

    public readonly struct PrismCard
    {
        public const int NoColor = -1;

        internal PrismCard(int id, int color, CardKind kind, int number)
        {
            Id = id;
            Color = color;
            Kind = kind;
            Number = number;
        }

        public int Id { get; }

        /// <summary>0..3 (see <see cref="PrismColor"/>), or <see cref="NoColor"/> for a Prism.</summary>
        public int Color { get; }

        public CardKind Kind { get; }

        /// <summary>1..7 for number cards, 0 otherwise.</summary>
        public int Number { get; }

        public bool IsWild => Kind == CardKind.Prism;
    }

    /// <summary>
    /// The Prism Rush deck: for each of 4 colors, numbers 1-7 twice plus two each of Flip, Hop
    /// and Pulse (20 cards), and 4 Prism wild cards. 84 cards in all.
    /// </summary>
    public static class PrismCards
    {
        public const int ColorCount = 4;
        public const int HighestNumber = 7;
        public const int CardsPerColor = 20;
        public const int PrismCount = 4;
        public const int DeckSize = ColorCount * CardsPerColor + PrismCount;
        public const int HandSize = 7;

        private static readonly PrismCard[] Deck = Build();

        private static PrismCard[] Build()
        {
            var cards = new List<PrismCard>(DeckSize);
            for (int color = 0; color < ColorCount; color++)
            {
                for (int number = 1; number <= HighestNumber; number++)
                {
                    cards.Add(new PrismCard(cards.Count, color, CardKind.Number, number));
                    cards.Add(new PrismCard(cards.Count, color, CardKind.Number, number));
                }
                foreach (var kind in new[] { CardKind.Flip, CardKind.Hop, CardKind.Pulse })
                {
                    cards.Add(new PrismCard(cards.Count, color, kind, 0));
                    cards.Add(new PrismCard(cards.Count, color, kind, 0));
                }
            }
            for (int i = 0; i < PrismCount; i++)
            {
                cards.Add(new PrismCard(cards.Count, PrismCard.NoColor, CardKind.Prism, 0));
            }
            return cards.ToArray();
        }

        public static PrismCard Card(int id)
        {
            if (!IsValidCard(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return Deck[id];
        }

        public static bool IsValidCard(int id) => id >= 0 && id < DeckSize;

        public static string ColorName(int color) => color >= 0 && color < ColorCount ? ((PrismColor)color).ToString() : "Prism";

        /// <summary>The shape that goes with each color: Cyan diamond, Gold sun, Rose star, Lime leaf.</summary>
        public static string ShapeName(int color)
        {
            switch (color)
            {
                case 0: return "diamond";
                case 1: return "sun";
                case 2: return "star";
                case 3: return "leaf";
                default: return "rainbow";
            }
        }

        /// <summary>A short spoken name, e.g. "Rose 5", "Gold Hop" or "Prism".</summary>
        public static string Name(int id)
        {
            var card = Card(id);
            switch (card.Kind)
            {
                case CardKind.Number: return $"{ColorName(card.Color)} {card.Number}";
                case CardKind.Prism: return "Prism";
                default: return $"{ColorName(card.Color)} {card.Kind}";
            }
        }
    }
}
