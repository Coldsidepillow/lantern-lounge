using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.TreehouseHoldem
{
    public enum Suit
    {
        Hearts = 0,
        Diamonds = 1,
        Clubs = 2,
        Spades = 3
    }

    /// <summary>
    /// A standard 52-card deck. Card id = (rank - 2) * 4 + suit, so ranks run 2..14 (Jack 11,
    /// Queen 12, King 13, Ace 14) and suits 0..3 (see <see cref="Suit"/>).
    /// </summary>
    public static class HoldemCards
    {
        public const int DeckSize = 52;
        public const int Jack = 11, Queen = 12, King = 13, Ace = 14;

        public static int Id(int rank, Suit suit) => (rank - 2) * 4 + (int)suit;

        public static int Rank(int card) => card / 4 + 2;

        public static Suit SuitOf(int card) => (Suit)(card % 4);

        public static bool IsValidCard(int card) => card >= 0 && card < DeckSize;

        /// <summary>What goes in the card's corner: 2-10, J, Q, K or A.</summary>
        public static string RankLabel(int rank)
        {
            switch (rank)
            {
                case Jack: return "J";
                case Queen: return "Q";
                case King: return "K";
                case Ace: return "A";
                default: return rank.ToString();
            }
        }

        /// <summary>The rank as a word: "Seven", "Queen", "Ace".</summary>
        public static string RankWord(int rank)
        {
            switch (rank)
            {
                case 2: return "Two";
                case 3: return "Three";
                case 4: return "Four";
                case 5: return "Five";
                case 6: return "Six";
                case 7: return "Seven";
                case 8: return "Eight";
                case 9: return "Nine";
                case 10: return "Ten";
                case Jack: return "Jack";
                case Queen: return "Queen";
                case King: return "King";
                default: return "Ace";
            }
        }

        /// <summary>Plural rank words, for "a pair of Sixes".</summary>
        public static string RankPlural(int rank) => rank == 6 ? "Sixes" : RankWord(rank) + "s";

        /// <summary>For example "Queen of Hearts".</summary>
        public static string Name(int card) => $"{RankWord(Rank(card))} of {SuitOf(card)}";

        /// <summary>A freshly shuffled deck, top card first.</summary>
        public static int[] ShuffledDeck(SeededRandom random)
        {
            var deck = new int[DeckSize];
            for (int i = 0; i < DeckSize; i++) deck[i] = i;
            random.Shuffle(deck);
            return deck;
        }

        internal static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }
    }

    /// <summary>The hand rankings, weakest first.</summary>
    public enum HandCategory
    {
        HighCard = 0,
        Pair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8,
        RoyalFlush = 9
    }

    /// <summary>
    /// How good a hand is. A higher <see cref="Score"/> always beats a lower one; equal scores tie.
    /// <see cref="Best"/> holds the cards that make the hand (up to five).
    /// </summary>
    public readonly struct HandValue
    {
        private readonly IReadOnlyList<int> best;

        internal HandValue(HandCategory category, int score, int[] best)
        {
            Category = category;
            Score = score;
            this.best = Array.AsReadOnly(best);
        }

        public HandCategory Category { get; }
        public int Score { get; }
        public IReadOnlyList<int> Best => best ?? Array.Empty<int>();

        /// <summary>The main rank of the hand, e.g. the pair's rank or a straight's top card.</summary>
        public int TopRank => (Score >> 16) & 0xF;

        /// <summary>The second rank (the low pair in Two Pairs, the pair in a Full House).</summary>
        public int SecondRank => (Score >> 12) & 0xF;
    }

    /// <summary>Finds the best five-card hand from any 1-7 cards.</summary>
    public static class HandEvaluator
    {
        public static string CategoryName(HandCategory category)
        {
            switch (category)
            {
                case HandCategory.HighCard: return "High Card";
                case HandCategory.Pair: return "Pair";
                case HandCategory.TwoPair: return "Two Pairs";
                case HandCategory.ThreeOfAKind: return "Three of a Kind";
                case HandCategory.Straight: return "Straight";
                case HandCategory.Flush: return "Flush";
                case HandCategory.FullHouse: return "Full House";
                case HandCategory.FourOfAKind: return "Four of a Kind";
                case HandCategory.StraightFlush: return "Straight Flush";
                default: return "Royal Flush";
            }
        }

        /// <summary>A friendly description, e.g. "Pair of Kings" or "Full House: Sevens over Twos".</summary>
        public static string Describe(HandValue value)
        {
            int top = value.TopRank, second = value.SecondRank;
            switch (value.Category)
            {
                case HandCategory.HighCard: return $"{HoldemCards.RankWord(top)} high";
                case HandCategory.Pair: return $"Pair of {HoldemCards.RankPlural(top)}";
                case HandCategory.TwoPair: return $"Two Pairs: {HoldemCards.RankPlural(top)} and {HoldemCards.RankPlural(second)}";
                case HandCategory.ThreeOfAKind: return $"Three {HoldemCards.RankPlural(top)}";
                case HandCategory.Straight: return $"Straight up to {HoldemCards.RankWord(top)}";
                case HandCategory.Flush: return $"{HoldemCards.RankWord(top)}-high Flush";
                case HandCategory.FullHouse: return $"Full House: {HoldemCards.RankPlural(top)} over {HoldemCards.RankPlural(second)}";
                case HandCategory.FourOfAKind: return $"Four {HoldemCards.RankPlural(top)}";
                case HandCategory.StraightFlush: return $"Straight Flush up to {HoldemCards.RankWord(top)}";
                default: return "Royal Flush!";
            }
        }

        public static HandValue Evaluate(IReadOnlyList<int> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (cards.Count < 1 || cards.Count > 7) throw new ArgumentException("Evaluate needs 1 to 7 cards.", nameof(cards));
            for (int i = 0; i < cards.Count; i++)
            {
                if (!HoldemCards.IsValidCard(cards[i])) throw new ArgumentOutOfRangeException(nameof(cards));
            }

            if (cards.Count < 5) return EvaluateFew(cards);

            var pick = new int[5];
            var best = default(HandValue);
            bool found = false;
            int n = cards.Count;
            for (int a = 0; a < n - 4; a++)
            for (int b = a + 1; b < n - 3; b++)
            for (int c = b + 1; c < n - 2; c++)
            for (int d = c + 1; d < n - 1; d++)
            for (int e = d + 1; e < n; e++)
            {
                pick[0] = cards[a];
                pick[1] = cards[b];
                pick[2] = cards[c];
                pick[3] = cards[d];
                pick[4] = cards[e];
                var value = EvaluateFive(pick);
                if (!found || value.Score > best.Score)
                {
                    best = value;
                    found = true;
                }
            }
            return best;
        }

        /// <summary>Compares two hands: positive if a wins, negative if b wins, 0 for a tie.</summary>
        public static int Compare(IReadOnlyList<int> a, IReadOnlyList<int> b) => Evaluate(a).Score.CompareTo(Evaluate(b).Score);

        private static int Pack(HandCategory category, int[] ranks)
        {
            int score = (int)category << 20;
            for (int i = 0; i < 5; i++) score |= (i < ranks.Length ? ranks[i] : 0) << (16 - 4 * i);
            return score;
        }

        private static HandValue EvaluateFive(int[] five)
        {
            var sorted = (int[])five.Clone();
            // Highest rank first.
            Array.Sort(sorted, (x, y) => y.CompareTo(x));

            bool flush = true;
            for (int i = 1; i < 5; i++) flush &= HoldemCards.SuitOf(sorted[i]) == HoldemCards.SuitOf(sorted[0]);

            var ranks = new int[5];
            for (int i = 0; i < 5; i++) ranks[i] = HoldemCards.Rank(sorted[i]);

            bool distinct = ranks[0] != ranks[1] && ranks[1] != ranks[2] && ranks[2] != ranks[3] && ranks[3] != ranks[4];
            int straightTop = 0;
            if (distinct && ranks[0] - ranks[4] == 4) straightTop = ranks[0];
            else if (distinct && ranks[0] == HoldemCards.Ace && ranks[1] == 5) straightTop = 5; // A-2-3-4-5

            if (straightTop > 0)
            {
                var order = sorted;
                if (straightTop == 5)
                {
                    // The Ace counts low: put it at the end.
                    order = new[] { sorted[1], sorted[2], sorted[3], sorted[4], sorted[0] };
                }
                HandCategory category = flush
                    ? (straightTop == HoldemCards.Ace ? HandCategory.RoyalFlush : HandCategory.StraightFlush)
                    : HandCategory.Straight;
                return new HandValue(category, Pack(category, new[] { straightTop }), order);
            }

            // Group by rank: bigger groups first, then higher ranks.
            var groups = Groups(sorted);
            int[] groupRanks = new int[groups.Count];
            var bestCards = new List<int>(5);
            for (int i = 0; i < groups.Count; i++)
            {
                groupRanks[i] = HoldemCards.Rank(groups[i][0]);
                bestCards.AddRange(groups[i]);
            }

            HandCategory cat;
            int top = groups[0].Count, next = groups.Count > 1 ? groups[1].Count : 0;
            if (top == 4) cat = HandCategory.FourOfAKind;
            else if (top == 3 && next == 2) cat = HandCategory.FullHouse;
            else if (flush) cat = HandCategory.Flush;
            else if (top == 3) cat = HandCategory.ThreeOfAKind;
            else if (top == 2 && next == 2) cat = HandCategory.TwoPair;
            else if (top == 2) cat = HandCategory.Pair;
            else cat = HandCategory.HighCard;

            if (cat == HandCategory.Flush) return new HandValue(cat, Pack(cat, ranks), sorted);
            return new HandValue(cat, Pack(cat, groupRanks), bestCards.ToArray());
        }

        // Fewer than five cards (e.g. just your two cards before the flop): pairs and high cards only.
        private static HandValue EvaluateFew(IReadOnlyList<int> cards)
        {
            var sorted = new int[cards.Count];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = cards[i];
            Array.Sort(sorted, (x, y) => y.CompareTo(x));
            var groups = Groups(sorted);
            var groupRanks = new int[groups.Count];
            var made = new List<int>();
            for (int i = 0; i < groups.Count; i++)
            {
                groupRanks[i] = HoldemCards.Rank(groups[i][0]);
                if (groups[i].Count >= 2) made.AddRange(groups[i]);
            }
            int top = groups[0].Count, next = groups.Count > 1 ? groups[1].Count : 0;
            HandCategory cat;
            if (top == 4) cat = HandCategory.FourOfAKind;
            else if (top == 3) cat = HandCategory.ThreeOfAKind;
            else if (top == 2 && next == 2) cat = HandCategory.TwoPair;
            else if (top == 2) cat = HandCategory.Pair;
            else cat = HandCategory.HighCard;
            if (made.Count == 0) made.Add(sorted[0]);
            return new HandValue(cat, Pack(cat, groupRanks), made.ToArray());
        }

        /// <summary>Cards grouped by rank; larger groups first, then higher ranks. Input sorted high to low.</summary>
        private static List<List<int>> Groups(int[] sortedHighFirst)
        {
            var groups = new List<List<int>>();
            foreach (int card in sortedHighFirst)
            {
                var last = groups.Count > 0 ? groups[groups.Count - 1] : null;
                if (last != null && HoldemCards.Rank(last[0]) == HoldemCards.Rank(card)) last.Add(card);
                else groups.Add(new List<int> { card });
            }
            groups.Sort((x, y) => x.Count != y.Count ? y.Count.CompareTo(x.Count) : HoldemCards.Rank(y[0]).CompareTo(HoldemCards.Rank(x[0])));
            return groups;
        }
    }

    /// <summary>
    /// Estimates how often a hand wins against random hands, by dealing out the rest many times.
    /// Used by the bots and by the optional "how strong is my hand?" hint. Ties count as half a win.
    /// </summary>
    public static class HoldemOdds
    {
        public static float WinChance(IReadOnlyList<int> hole, IReadOnlyList<int> board, int opponents, SeededRandom random, int trials = 200)
        {
            if (hole == null || hole.Count != 2) throw new ArgumentException("Two hole cards are needed.", nameof(hole));
            if (board == null) board = Array.Empty<int>();
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (board.Count > 5) throw new ArgumentException("There are at most 5 shared cards.", nameof(board));
            if (opponents < 1) return 1f;

            var known = new bool[HoldemCards.DeckSize];
            foreach (int c in hole) Mark(known, c);
            foreach (int c in board) Mark(known, c);
            var rest = new List<int>(HoldemCards.DeckSize);
            for (int c = 0; c < HoldemCards.DeckSize; c++)
            {
                if (!known[c]) rest.Add(c);
            }
            var deck = rest.ToArray();
            int needBoard = 5 - board.Count;

            var mine = new int[7];
            var theirs = new int[7];
            var fullBoard = new int[5];
            double wins = 0;
            for (int t = 0; t < trials; t++)
            {
                // Partial shuffle: only as many cards as we need.
                int need = needBoard + opponents * 2;
                for (int i = 0; i < need; i++)
                {
                    int j = i + random.NextInt(deck.Length - i);
                    int tmp = deck[i];
                    deck[i] = deck[j];
                    deck[j] = tmp;
                }
                for (int i = 0; i < board.Count; i++) fullBoard[i] = board[i];
                for (int i = 0; i < needBoard; i++) fullBoard[board.Count + i] = deck[i];

                mine[0] = hole[0];
                mine[1] = hole[1];
                for (int i = 0; i < 5; i++) mine[2 + i] = fullBoard[i];
                int myScore = HandEvaluator.Evaluate(mine).Score;

                int best = int.MinValue, bestCount = 0;
                for (int o = 0; o < opponents; o++)
                {
                    theirs[0] = deck[needBoard + o * 2];
                    theirs[1] = deck[needBoard + o * 2 + 1];
                    for (int i = 0; i < 5; i++) theirs[2 + i] = fullBoard[i];
                    int score = HandEvaluator.Evaluate(theirs).Score;
                    if (score > best)
                    {
                        best = score;
                        bestCount = 1;
                    }
                    else if (score == best)
                    {
                        bestCount++;
                    }
                }
                if (myScore > best) wins += 1;
                else if (myScore == best) wins += 1.0 / (bestCount + 1);
            }
            return (float)(wins / trials);
        }

        private static void Mark(bool[] known, int card)
        {
            if (!HoldemCards.IsValidCard(card)) throw new ArgumentOutOfRangeException(nameof(card));
            if (known[card]) throw new ArgumentException("A card appears twice.", nameof(card));
            known[card] = true;
        }
    }
}
