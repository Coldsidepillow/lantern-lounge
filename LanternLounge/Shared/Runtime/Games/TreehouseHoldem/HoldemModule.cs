using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.TreehouseHoldem
{
    /// <summary>
    /// Treehouse Hold'em: a kid-friendly Texas Hold'em played with play-money coins.
    /// Simple fixed betting: everyone puts in 2 coins to start each round, raises are always
    /// 5 coins (first two betting rounds) or 10 coins (last two), at most 3 raises per betting round.
    /// The night lasts a set number of rounds; whoever has the most coins at the end wins.
    /// Options: "rounds" (default 8), "coins" (default 100), "dealer" (seat, default random).
    /// </summary>
    public sealed class HoldemModule : GameModule<HoldemState, HoldemMove, HoldemView>
    {
        public const string GameId = "treehouse-holdem";
        public const int DefaultRounds = 8;
        public const int DefaultCoins = 100;
        public const int Ante = 2;
        public const int SmallRaise = 5;
        public const int BigRaise = 10;
        public const int MaxRaisesPerStreet = 3;

        public const string NotBetting = "not_betting";
        public const string CheckIsFree = "check_is_free";
        public const string NothingToCall = "nothing_to_call";
        public const string MustMatch = "must_match";
        public const string NoMoreRaises = "no_more_raises";
        public const string NotEnoughCoins = "not_enough_coins";
        public const string RoundNotOver = "round_not_over";

        public static readonly GameInfo Definition =
            new GameInfo(GameId, "Treehouse Hold'em", 2, 4, GameStatus.Playable, "treehouse-holdem");

        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        public override GameInfo Info => Definition;

        public static int RaiseSizeFor(Street street) => street <= Street.Flop ? SmallRaise : BigRaise;

        protected override HoldemState CreateInitialState(MatchConfig config)
        {
            int rounds = ReadInt(config, "rounds", DefaultRounds, 1, 99);
            int coins = ReadInt(config, "coins", DefaultCoins, Ante, 100000);
            var random = new SeededRandom(config.Seed);
            int dealer = ReadInt(config, "dealer", -1, 0, config.PlayerCount - 1);
            if (dealer < 0) dealer = random.NextInt(config.PlayerCount);
            var t = new HoldemTable(config.PlayerCount, coins, rounds, random.State);
            // StartRound moves the button one seat on, so start one seat behind.
            t.Dealer = (dealer - 1 + config.PlayerCount) % config.PlayerCount;
            t.StartRound(null);
            return t.Freeze();
        }

        /// <summary>
        /// Starts with a known deck for the first round (top first), for tutorials and tests.
        /// Later rounds shuffle with <paramref name="seed"/>.
        /// </summary>
        public HoldemState SetupWithDeck(int playerCount, IReadOnlyList<int> deckTopFirst, int dealer, ulong seed = 1,
            int rounds = DefaultRounds, int coins = DefaultCoins)
        {
            if (playerCount < 1) throw new ArgumentOutOfRangeException(nameof(playerCount));
            var stacks = new int[playerCount];
            for (int s = 0; s < playerCount; s++) stacks[s] = coins;
            return SetupWithStacks(stacks, deckTopFirst, dealer, seed, rounds);
        }

        /// <summary>Like <see cref="SetupWithDeck"/>, but each seat starts with its own number of coins.</summary>
        public HoldemState SetupWithStacks(IReadOnlyList<int> stacks, IReadOnlyList<int> deckTopFirst, int dealer, ulong seed = 1,
            int rounds = DefaultRounds)
        {
            if (stacks == null) throw new ArgumentNullException(nameof(stacks));
            int playerCount = stacks.Count;
            if (!Info.SupportsPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(stacks));
            int most = 0;
            for (int s = 0; s < playerCount; s++)
            {
                if (stacks[s] <= 0) throw new ArgumentOutOfRangeException(nameof(stacks), "Every seat needs at least 1 coin.");
                most = Math.Max(most, stacks[s]);
            }
            if (dealer < 0 || dealer >= playerCount) throw new ArgumentOutOfRangeException(nameof(dealer));
            if (deckTopFirst == null || deckTopFirst.Count != HoldemCards.DeckSize)
                throw new ArgumentException($"The deck must have exactly {HoldemCards.DeckSize} cards.", nameof(deckTopFirst));
            var seen = new bool[HoldemCards.DeckSize];
            var deck = new int[HoldemCards.DeckSize];
            for (int i = 0; i < deck.Length; i++)
            {
                int card = deckTopFirst[i];
                if (!HoldemCards.IsValidCard(card) || seen[card])
                    throw new ArgumentException("The deck must contain each card exactly once.", nameof(deckTopFirst));
                seen[card] = true;
                deck[i] = card;
            }
            var t = new HoldemTable(playerCount, most, Math.Max(1, rounds), seed);
            for (int s = 0; s < playerCount; s++) t.Coins[s] = stacks[s];
            t.Dealer = (dealer - 1 + playerCount) % playerCount;
            t.StartRound(deck);
            return t.Freeze();
        }

        /// <summary>A brand-new game night with the same players and settings (every seat gets the starting coins again).</summary>
        public HoldemState Rematch(HoldemState previous, ulong seed)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var t = new HoldemTable(previous.PlayerCount, previous.StartingCoins, previous.Rounds, seed);
            t.Dealer = previous.Dealer;
            t.StartRound(null);
            return t.Freeze();
        }

        public override ValidationResult Validate(HoldemState state, int seat, HoldemMove move)
        {
            if (state == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no table.");
            if (move == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no move.");
            if (state.IsOver) return ValidationResult.Fail(ErrorCodes.GameOver, "Game night is over.");
            if (seat < 0 || seat >= state.PlayerCount) return ValidationResult.Fail(ErrorCodes.BadSeat, "That seat isn't at this table.");

            if (move.Kind == MoveKind.NextRound)
            {
                return state.Phase == Phase.RoundOver
                    ? ValidationResult.Success
                    : ValidationResult.Fail(RoundNotOver, "Finish this round first.");
            }

            if (state.Phase != Phase.Betting) return ValidationResult.Fail(NotBetting, "Nobody is betting right now.");
            if (seat != state.CurrentSeat) return ValidationResult.Fail(ErrorCodes.NotYourTurn, "It isn't your turn yet.");

            int cost = state.CurrentBet - state.RoundBet[seat];
            switch (move.Kind)
            {
                case MoveKind.Fold:
                    return cost > 0 ? ValidationResult.Success : ValidationResult.Fail(CheckIsFree, "No need to fold: you can check for free!");
                case MoveKind.Check:
                    return cost == 0 ? ValidationResult.Success : ValidationResult.Fail(MustMatch, "Someone raised. Call, raise or fold.");
                case MoveKind.Call:
                    return cost > 0 ? ValidationResult.Success : ValidationResult.Fail(NothingToCall, "There's nothing to match. You can check.");
                case MoveKind.Raise:
                    if (state.RaisesThisStreet >= MaxRaisesPerStreet)
                        return ValidationResult.Fail(NoMoreRaises, "That's 3 raises already this time. Call or fold.");
                    if (state.Coins[seat] <= cost)
                        return ValidationResult.Fail(NotEnoughCoins, "You only have enough coins to call.");
                    if (!SomeoneCanAnswer(state, seat))
                        return ValidationResult.Fail(NoMoreRaises, "Everyone else is all in, so there's nobody to raise against.");
                    return ValidationResult.Success;
                default:
                    return ValidationResult.Fail(ErrorCodes.InvalidAction, "That isn't a move in this game.");
            }
        }

        protected override HoldemState ApplyValidated(HoldemState state, int seat, HoldemMove move)
        {
            var t = HoldemTable.From(state);
            if (move.Kind == MoveKind.NextRound)
            {
                t.StartRound(null);
                return t.Freeze();
            }

            int cost = t.CurrentBet - t.RoundBet[seat];
            switch (move.Kind)
            {
                case MoveKind.Fold:
                    t.Folded[seat] = true;
                    t.Events.Add(new HoldemEvent(HoldemEventKind.Folded, seat));
                    break;
                case MoveKind.Check:
                    t.Events.Add(new HoldemEvent(HoldemEventKind.Checked, seat));
                    break;
                case MoveKind.Call:
                {
                    int paid = t.Pay(seat, cost);
                    t.Events.Add(new HoldemEvent(HoldemEventKind.Called, seat, paid));
                    if (t.Coins[seat] == 0) t.Events.Add(new HoldemEvent(HoldemEventKind.AllIn, seat));
                    break;
                }
                case MoveKind.Raise:
                {
                    int target = t.CurrentBet + RaiseSizeFor(t.Street);
                    int paid = t.Pay(seat, target - t.RoundBet[seat]);
                    if (t.RoundBet[seat] > t.CurrentBet)
                    {
                        t.CurrentBet = t.RoundBet[seat];
                        t.Raises++;
                        // Everyone else gets another chance to answer the raise.
                        for (int s = 0; s < t.Players; s++)
                        {
                            if (s != seat) t.Acted[s] = false;
                        }
                    }
                    t.Events.Add(new HoldemEvent(HoldemEventKind.Raised, seat, paid, t.CurrentBet));
                    if (t.Coins[seat] == 0) t.Events.Add(new HoldemEvent(HoldemEventKind.AllIn, seat));
                    break;
                }
            }
            t.Acted[seat] = true;
            t.Progress(seat);
            return t.Freeze();
        }

        public override HoldemView ViewFor(HoldemState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seat < 0 || seat >= state.PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return new HoldemView(state, seat);
        }

        public override MatchResult Result(HoldemState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.IsOver) return MatchResult.InProgress;
            if (state.Winners.Count == state.PlayerCount) return MatchResult.Draw();
            var seats = new int[state.Winners.Count];
            for (int i = 0; i < seats.Length; i++) seats[i] = state.Winners[i];
            return MatchResult.Win(seats);
        }

        public override IReadOnlyList<int> SeatsToAct(HoldemState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            switch (state.Phase)
            {
                case Phase.Betting:
                    return new[] { state.CurrentSeat };
                case Phase.RoundOver:
                {
                    var all = new int[state.PlayerCount];
                    for (int s = 0; s < all.Length; s++) all[s] = s;
                    return all;
                }
                default:
                    return NoSeats;
            }
        }

        /// <summary>When a turn timer runs out: check if free, otherwise fold; between rounds, go on.</summary>
        public override bool TryGetTimeoutAction(HoldemState state, int seat, out HoldemMove action)
        {
            action = null;
            if (state == null || state.IsOver) return false;
            if (state.Phase == Phase.RoundOver)
            {
                action = HoldemMove.NextRound;
                return true;
            }
            if (seat != state.CurrentSeat) return false;
            action = state.CurrentBet - state.RoundBet[seat] == 0 ? HoldemMove.Check : HoldemMove.Fold;
            return true;
        }

        /// <summary>Is there another player still in this round who has coins to answer a raise?</summary>
        internal static bool SomeoneCanAnswer(HoldemState state, int seat)
        {
            for (int s = 0; s < state.PlayerCount; s++)
            {
                if (s != seat && state.InRound[s] && !state.Folded[s] && state.Coins[s] > 0) return true;
            }
            return false;
        }

        private static int ReadInt(MatchConfig config, string key, int fallback, int min, int max)
        {
            string text = config.GetOption(key, null);
            if (text == null || !int.TryParse(text, out int value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }

    /// <summary>A mutable working copy of the table used while resolving one move.</summary>
    internal sealed class HoldemTable
    {
        public readonly int Players;
        public readonly int[] Coins;
        public readonly List<int>[] Hole;
        public readonly List<int> Board = new List<int>();
        public readonly List<int> Deck = new List<int>();
        public readonly bool[] InRound;
        public readonly bool[] Folded;
        public readonly bool[] Revealed;
        public readonly bool[] Acted;
        public readonly int[] RoundBet;
        public readonly int[] TotalIn;
        public readonly List<int> Winners = new List<int>();
        public readonly List<HoldemEvent> Events = new List<HoldemEvent>();
        public readonly SeededRandom Random;
        public int CurrentBet;
        public int Raises;
        public Street Street;
        public Phase Phase;
        public int Dealer;
        public int Seat = HoldemState.NoSeat;
        public int RoundNumber;
        public readonly int Rounds;
        public readonly int StartingCoins;

        public HoldemTable(int players, int startingCoins, int rounds, ulong randomState)
        {
            Players = players;
            StartingCoins = startingCoins;
            Rounds = rounds;
            Coins = new int[players];
            for (int s = 0; s < players; s++) Coins[s] = startingCoins;
            Hole = new List<int>[players];
            for (int s = 0; s < players; s++) Hole[s] = new List<int>(2);
            InRound = new bool[players];
            Folded = new bool[players];
            Revealed = new bool[players];
            Acted = new bool[players];
            RoundBet = new int[players];
            TotalIn = new int[players];
            Random = new SeededRandom(randomState);
        }

        public static HoldemTable From(HoldemState s)
        {
            var t = new HoldemTable(s.PlayerCount, s.StartingCoins, s.Rounds, s.RandomState);
            for (int i = 0; i < t.Players; i++)
            {
                t.Coins[i] = s.Coins[i];
                t.Hole[i].AddRange(s.HoleCardsOf(i));
                t.InRound[i] = s.InRound[i];
                t.Folded[i] = s.Folded[i];
                t.Revealed[i] = s.Revealed[i];
                t.Acted[i] = s.Acted[i];
                t.RoundBet[i] = s.RoundBet[i];
                t.TotalIn[i] = s.TotalIn[i];
            }
            t.Board.AddRange(s.Board);
            t.Deck.AddRange(s.Deck);
            t.Winners.AddRange(s.Winners);
            t.CurrentBet = s.CurrentBet;
            t.Raises = s.RaisesThisStreet;
            t.Street = s.Street;
            t.Phase = s.Phase;
            t.Dealer = s.Dealer;
            t.Seat = s.CurrentSeat;
            t.RoundNumber = s.RoundNumber;
            return t;
        }

        public HoldemState Freeze() => new HoldemState(this, Events.ToArray());

        public int Next(int seat) => (seat + 1) % Players;

        /// <summary>Can still choose moves this round: in, not folded, and has coins left.</summary>
        private bool CanBet(int s) => InRound[s] && !Folded[s] && Coins[s] > 0;

        private bool IsLive(int s) => InRound[s] && !Folded[s];

        /// <summary>Moves up to <paramref name="amount"/> coins from a seat into the middle; returns what was paid.</summary>
        public int Pay(int seat, int amount)
        {
            int paid = Math.Max(0, Math.Min(amount, Coins[seat]));
            Coins[seat] -= paid;
            RoundBet[seat] += paid;
            TotalIn[seat] += paid;
            return paid;
        }

        private int TakeTop()
        {
            int card = Deck[0];
            Deck.RemoveAt(0);
            return card;
        }

        /// <summary>Starts the next round: move the dealer button, collect start coins, deal two cards each.</summary>
        public void StartRound(int[] fixedDeck)
        {
            int withCoins = 0;
            for (int s = 0; s < Players; s++)
            {
                if (Coins[s] > 0) withCoins++;
            }
            if (withCoins < 2)
            {
                // Never start a one-player round (the night normally ends before this).
                EndGame();
                return;
            }

            RoundNumber++;
            Board.Clear();
            Deck.Clear();
            Deck.AddRange(fixedDeck ?? HoldemCards.ShuffledDeck(Random));
            for (int s = 0; s < Players; s++)
            {
                Hole[s].Clear();
                InRound[s] = Coins[s] > 0;
                Folded[s] = false;
                Revealed[s] = false;
                Acted[s] = false;
                RoundBet[s] = 0;
                TotalIn[s] = 0;
            }
            CurrentBet = 0;
            Raises = 0;
            Street = Street.Start;
            Phase = Phase.Betting;

            // The dealer button moves to the next player who still has coins.
            int dealer = Next(Dealer);
            for (int i = 0; i < Players && !InRound[dealer]; i++) dealer = Next(dealer);
            Dealer = dealer;
            Events.Add(new HoldemEvent(HoldemEventKind.RoundStarted, Dealer, RoundNumber));

            for (int s = 0; s < Players; s++)
            {
                if (!InRound[s]) continue;
                int paid = Pay(s, HoldemModule.Ante);
                Events.Add(new HoldemEvent(HoldemEventKind.Ante, s, paid));
            }
            // Start coins are not part of the first betting round's bets.
            for (int s = 0; s < Players; s++) RoundBet[s] = 0;

            for (int card = 0; card < 2; card++)
            {
                int s = Next(Dealer);
                for (int i = 0; i < Players; i++, s = Next(s))
                {
                    if (InRound[s]) Hole[s].Add(TakeTop());
                }
            }
            for (int s = 0; s < Players; s++)
            {
                if (InRound[s]) Events.Add(new HoldemEvent(HoldemEventKind.Dealt, s, 2));
            }

            Progress(Dealer);
        }

        /// <summary>
        /// After a move by <paramref name="lastSeat"/>: find who acts next, or finish the betting
        /// round, deal shared cards, and settle the round when it is over.
        /// </summary>
        public void Progress(int lastSeat)
        {
            while (true)
            {
                int live = 0, lastLive = -1, bettors = 0;
                for (int s = 0; s < Players; s++)
                {
                    if (IsLive(s))
                    {
                        live++;
                        lastLive = s;
                    }
                    if (CanBet(s)) bettors++;
                }

                if (live == 1)
                {
                    AwardToLastPlayer(lastLive);
                    return;
                }

                int next = NeedsToAct(lastSeat, bettors);
                if (next >= 0)
                {
                    Seat = next;
                    return;
                }

                if (Street == Street.River)
                {
                    Showdown();
                    return;
                }

                DealNextStreet();
                lastSeat = Dealer;
            }
        }

        /// <summary>The next seat after <paramref name="from"/> that still has to act this betting round, or -1.</summary>
        private int NeedsToAct(int from, int bettors)
        {
            int s = Next(from);
            for (int i = 0; i < Players; i++, s = Next(s))
            {
                if (!CanBet(s)) continue;
                bool behind = RoundBet[s] < CurrentBet;
                // With nobody left to bet against, there is nothing to decide unless you owe coins.
                if (behind || (!Acted[s] && bettors > 1)) return s;
            }
            return -1;
        }

        private void DealNextStreet()
        {
            int count = Street == Street.Start ? 3 : 1;
            Street = Street + 1;
            var cards = new int[count];
            for (int i = 0; i < count; i++)
            {
                cards[i] = TakeTop();
                Board.Add(cards[i]);
            }
            for (int s = 0; s < Players; s++)
            {
                RoundBet[s] = 0;
                Acted[s] = false;
            }
            CurrentBet = 0;
            Raises = 0;
            Events.Add(new HoldemEvent(HoldemEventKind.SharedCards, -1, count, (int)Street, Array.AsReadOnly(cards)));
        }

        private void AwardToLastPlayer(int seat)
        {
            int pot = 0;
            for (int s = 0; s < Players; s++) pot += TotalIn[s];
            Coins[seat] += pot;
            Events.Add(new HoldemEvent(HoldemEventKind.WonCoins, seat, pot, -1));
            EndRound();
        }

        private void Showdown()
        {
            var scores = new int[Players];
            var categories = new int[Players];
            var all = new List<int>(7);
            for (int s = 0; s < Players; s++)
            {
                if (!IsLive(s)) continue;
                Revealed[s] = true;
                all.Clear();
                all.AddRange(Hole[s]);
                all.AddRange(Board);
                var value = HandEvaluator.Evaluate(all);
                scores[s] = value.Score;
                categories[s] = (int)value.Category;
            }
            Events.Add(new HoldemEvent(HoldemEventKind.Showdown, -1));

            // Split the middle into pots by how much each live player put in, so a player who
            // ran out of coins can only win from the coins they matched.
            var levels = new List<int>();
            for (int s = 0; s < Players; s++)
            {
                if (IsLive(s) && TotalIn[s] > 0 && !levels.Contains(TotalIn[s])) levels.Add(TotalIn[s]);
            }
            levels.Sort();

            var won = new int[Players];
            int previous = 0;
            for (int li = 0; li < levels.Count; li++)
            {
                int level = levels[li];
                int amount = 0;
                for (int s = 0; s < Players; s++) amount += Math.Min(TotalIn[s], level) - Math.Min(TotalIn[s], previous);
                if (li == levels.Count - 1)
                {
                    // Coins above the top level (from players who folded) go in the last pot.
                    for (int s = 0; s < Players; s++) amount += TotalIn[s] - Math.Min(TotalIn[s], level);
                }
                previous = level;

                int best = int.MinValue;
                for (int s = 0; s < Players; s++)
                {
                    if (IsLive(s) && TotalIn[s] >= level && scores[s] > best) best = scores[s];
                }
                var winners = new List<int>();
                int seat = Next(Dealer);
                for (int i = 0; i < Players; i++, seat = Next(seat))
                {
                    if (IsLive(seat) && TotalIn[seat] >= level && scores[seat] == best) winners.Add(seat);
                }
                int share = amount / winners.Count, extra = amount % winners.Count;
                for (int i = 0; i < winners.Count; i++) won[winners[i]] += share + (i < extra ? 1 : 0);
            }

            for (int s = 0; s < Players; s++)
            {
                if (won[s] <= 0) continue;
                Coins[s] += won[s];
                Events.Add(new HoldemEvent(HoldemEventKind.WonCoins, s, won[s], categories[s]));
            }
            EndRound();
        }

        private void EndRound()
        {
            Seat = HoldemState.NoSeat;
            Phase = Phase.RoundOver;
            // Every coin in the middle has been handed out.
            for (int s = 0; s < Players; s++)
            {
                RoundBet[s] = 0;
                TotalIn[s] = 0;
            }
            CurrentBet = 0;
            int withCoins = 0;
            for (int s = 0; s < Players; s++)
            {
                if (Coins[s] > 0) withCoins++;
                else if (InRound[s]) Events.Add(new HoldemEvent(HoldemEventKind.OutOfCoins, s));
            }
            if (RoundNumber >= Rounds || withCoins <= 1) EndGame();
        }

        private void EndGame()
        {
            Seat = HoldemState.NoSeat;
            Phase = Phase.GameOver;
            int most = 0;
            for (int s = 0; s < Players; s++) most = Math.Max(most, Coins[s]);
            Winners.Clear();
            for (int s = 0; s < Players; s++)
            {
                if (Coins[s] == most) Winners.Add(s);
            }
            Events.Add(new HoldemEvent(HoldemEventKind.GameOver, -1));
        }
    }
}
