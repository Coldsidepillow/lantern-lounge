using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.Tidebound
{
    /// <summary>
    /// Tidebound: pair up two tides of the same creature to make a pod.
    ///
    /// On your tide, ask another diver for a creature you already hold.
    /// - If they have any, they hand over every one, and you ask again.
    /// - If not: "Dive!" Draw one card from the Current. If it is the creature you asked for,
    ///   show it and ask again; otherwise the tide passes to the next diver.
    /// Any two tides of one creature pair into a pod at once (so each creature makes two pods).
    /// A diver who starts a tide with no cards draws one first. When all 32 pods are claimed,
    /// the most pods wins (ties share it).
    ///
    /// Options read from <see cref="MatchConfig.Options"/>:
    ///   "firstSeat" = a seat number, or "random" (default; uses the match seed).
    /// </summary>
    public sealed class TideboundModule : GameModule<TideboundState, AskAction, TideboundView>
    {
        public const string GameId = "tidebound";

        public const string BadTarget = "bad_target";
        public const string TargetEmpty = "target_empty";
        public const string MustHoldCreature = "must_hold_creature";

        public static readonly GameInfo Definition =
            new GameInfo(GameId, "Tidebound", 2, 4, GameStatus.Playable, "tidebound");

        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        public override GameInfo Info => Definition;

        /// <summary>Cards dealt to each diver: 7, whatever the number of divers.</summary>
        public static int HandSizeFor(int playerCount) => 7;

        protected override TideboundState CreateInitialState(MatchConfig config)
        {
            var random = new SeededRandom(config.Seed);
            var deck = new int[TideboundCards.DeckSize];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            random.Shuffle(deck);

            string first = config.GetOption("firstSeat", "random");
            int firstSeat;
            if (!int.TryParse(first, out firstSeat) || firstSeat < 0 || firstSeat >= config.PlayerCount)
            {
                firstSeat = random.NextInt(config.PlayerCount);
            }
            return Deal(config.PlayerCount, deck, firstSeat);
        }

        /// <summary>
        /// Starts a game from a known deck order (top card first), e.g. for tutorials, replays
        /// and tests. The deck must contain every card 0..63 exactly once.
        /// </summary>
        public TideboundState SetupWithDeck(int playerCount, IReadOnlyList<int> deckTopFirst, int firstSeat)
        {
            if (!Info.SupportsPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (firstSeat < 0 || firstSeat >= playerCount) throw new ArgumentOutOfRangeException(nameof(firstSeat));
            if (deckTopFirst == null || deckTopFirst.Count != TideboundCards.DeckSize)
                throw new ArgumentException($"The deck must have exactly {TideboundCards.DeckSize} cards.", nameof(deckTopFirst));

            var seen = new bool[TideboundCards.DeckSize];
            var deck = new int[TideboundCards.DeckSize];
            for (int i = 0; i < deck.Length; i++)
            {
                int card = deckTopFirst[i];
                if (!TideboundCards.IsValidCard(card) || seen[card])
                    throw new ArgumentException($"The deck must contain each card 0..{TideboundCards.DeckSize - 1} exactly once.", nameof(deckTopFirst));
                seen[card] = true;
                deck[i] = card;
            }
            return Deal(playerCount, deck, firstSeat);
        }

        /// <summary>A new deal for a rematch: next diver starts, new shuffle from the seed.</summary>
        public TideboundState Rematch(TideboundState previous, ulong seed)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var random = new SeededRandom(seed);
            var deck = new int[TideboundCards.DeckSize];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            random.Shuffle(deck);
            return Deal(previous.PlayerCount, deck, (previous.FirstSeat + 1) % previous.PlayerCount);
        }

        private static TideboundState Deal(int playerCount, int[] deck, int firstSeat)
        {
            var table = new Table(playerCount, deck);
            int handSize = HandSizeFor(playerCount);
            for (int round = 0; round < handSize; round++)
            {
                for (int seat = 0; seat < playerCount; seat++)
                {
                    table.AddToHand(seat, table.DrawTop());
                }
            }
            for (int seat = 0; seat < playerCount; seat++) table.FormPods(seat);
            table.Seat = firstSeat;
            table.Normalize();
            return table.Freeze(firstSeat, 0);
        }

        public override ValidationResult Validate(TideboundState state, int seat, AskAction action)
        {
            if (state == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no table.");
            if (state.IsOver) return ValidationResult.Fail(ErrorCodes.GameOver, "Every pod has been claimed.");
            if (seat < 0 || seat >= state.PlayerCount) return ValidationResult.Fail(ErrorCodes.BadSeat, "That seat isn't at this table.");
            if (seat != state.CurrentSeat) return ValidationResult.Fail(ErrorCodes.NotYourTurn, "It isn't your tide yet.");
            if (action.TargetSeat < 0 || action.TargetSeat >= state.PlayerCount || action.TargetSeat == seat)
                return ValidationResult.Fail(BadTarget, "Pick another diver to ask.");
            if (action.Creature < 0 || action.Creature >= TideboundCards.CreatureCount)
                return ValidationResult.Fail(ErrorCodes.InvalidAction, "That creature isn't in Tidebound.");
            if (state.CountInHand(seat, action.Creature) == 0)
                return ValidationResult.Fail(MustHoldCreature, "You can only ask for a creature you already hold.");
            if (state.HandCount(action.TargetSeat) == 0)
                return ValidationResult.Fail(TargetEmpty, "That diver has no cards left. Ask someone else.");
            return ValidationResult.Success;
        }

        protected override TideboundState ApplyValidated(TideboundState state, int seat, AskAction action)
        {
            var table = Table.From(state);
            int given = table.TakeAll(action.TargetSeat, action.Creature, seat);
            table.Events.Add(new TideEvent(TideEventKind.Asked, seat, action.TargetSeat, action.Creature, given));

            if (given > 0)
            {
                table.FormPods(seat);
            }
            else
            {
                // Dive! Draw one card. The asked-for creature means go again; anything else passes the tide.
                if (table.Current.Count == 0)
                {
                    table.Events.Add(new TideEvent(TideEventKind.CurrentEmpty, seat, creature: action.Creature));
                    table.Seat = table.Next(seat);
                }
                else
                {
                    int card = table.DrawTop();
                    table.AddToHand(seat, card);
                    bool lucky = TideboundCards.CreatureOf(card) == action.Creature;
                    if (lucky)
                    {
                        table.Events.Add(new TideEvent(TideEventKind.LuckyDive, seat, creature: action.Creature, count: 1, card: card));
                    }
                    else
                    {
                        table.Events.Add(new TideEvent(TideEventKind.Dove, seat, count: 1));
                    }

                    table.FormPods(seat);
                    if (!lucky) table.Seat = table.Next(seat);
                }
            }

            table.Normalize();
            return table.Freeze(state.FirstSeat, state.Turn + 1);
        }

        public override TideboundView ViewFor(TideboundState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seat < 0 || seat >= state.PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return new TideboundView(state, seat);
        }

        public override MatchResult Result(TideboundState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.IsOver) return MatchResult.InProgress;
            var leaders = state.Leaders();
            if (leaders.Count == state.PlayerCount) return MatchResult.Draw();
            var seats = new int[leaders.Count];
            for (int i = 0; i < seats.Length; i++) seats[i] = leaders[i];
            return MatchResult.Win(seats);
        }

        public override IReadOnlyList<int> SeatsToAct(TideboundState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.IsOver ? NoSeats : new[] { state.CurrentSeat };
        }

        /// <summary>
        /// When a tide timer runs out: ask for the creature you hold most of, from the diver
        /// holding the most cards (nearest clockwise on ties).
        /// </summary>
        public override bool TryGetTimeoutAction(TideboundState state, int seat, out AskAction action)
        {
            action = default;
            if (state == null || state.IsOver || seat != state.CurrentSeat) return false;

            int bestCreature = -1, bestCount = 0;
            for (int c = 0; c < TideboundCards.CreatureCount; c++)
            {
                int n = state.CountInHand(seat, c);
                if (n > bestCount)
                {
                    bestCount = n;
                    bestCreature = c;
                }
            }

            int bestTarget = -1, bestCards = 0;
            for (int step = 1; step < state.PlayerCount; step++)
            {
                int target = (seat + step) % state.PlayerCount;
                int n = state.HandCount(target);
                if (n > bestCards)
                {
                    bestCards = n;
                    bestTarget = target;
                }
            }

            if (bestCreature < 0 || bestTarget < 0) return false;
            action = new AskAction(bestTarget, bestCreature);
            return true;
        }

        /// <summary>A mutable working copy used while resolving one move.</summary>
        private sealed class Table
        {
            private const int MaxNormalizeSteps = 400;

            public readonly List<int>[] Hands;
            public readonly List<int> Current;
            public readonly List<int>[] Pods;
            public readonly List<TideEvent> Events = new List<TideEvent>();
            public int Seat;

            public Table(int playerCount, int[] deckTopFirst)
            {
                Hands = new List<int>[playerCount];
                Pods = new List<int>[playerCount];
                for (int s = 0; s < playerCount; s++)
                {
                    Hands[s] = new List<int>();
                    Pods[s] = new List<int>();
                }
                Current = new List<int>(deckTopFirst);
            }

            public static Table From(TideboundState state)
            {
                var table = new Table(state.PlayerCount, ToArray(state.CurrentCards));
                for (int s = 0; s < state.PlayerCount; s++)
                {
                    table.Hands[s].AddRange(state.HandOf(s));
                    table.Pods[s].AddRange(state.PodsOf(s));
                }
                table.Seat = state.CurrentSeat;
                return table;
            }

            public int PlayerCount => Hands.Length;

            public int Next(int seat) => (seat + 1) % PlayerCount;

            public int DrawTop()
            {
                int card = Current[0];
                Current.RemoveAt(0);
                return card;
            }

            public void AddToHand(int seat, int card)
            {
                var hand = Hands[seat];
                int index = hand.BinarySearch(card);
                hand.Insert(index < 0 ? ~index : index, card);
            }

            /// <summary>Moves every card of the creature from one hand to another. Returns how many.</summary>
            public int TakeAll(int from, int creature, int to)
            {
                int moved = 0;
                var source = Hands[from];
                for (int i = source.Count - 1; i >= 0; i--)
                {
                    int card = source[i];
                    if (TideboundCards.CreatureOf(card) != creature) continue;
                    source.RemoveAt(i);
                    AddToHand(to, card);
                    moved++;
                }
                return moved;
            }

            /// <summary>Pairs up every two cards of the same creature in this hand (lowest cards first).</summary>
            public void FormPods(int seat)
            {
                var hand = Hands[seat];
                for (int creature = 0; creature < TideboundCards.CreatureCount; creature++)
                {
                    while (true)
                    {
                        int first = -1, second = -1;
                        for (int i = 0; i < hand.Count; i++)
                        {
                            if (TideboundCards.CreatureOf(hand[i]) != creature) continue;
                            if (first < 0) first = i;
                            else
                            {
                                second = i;
                                break;
                            }
                        }
                        if (second < 0) break;

                        hand.RemoveAt(second);
                        hand.RemoveAt(first);
                        Pods[seat].Add(creature);
                        Events.Add(new TideEvent(TideEventKind.PodFormed, seat, creature: creature));
                    }
                }
            }

            private int TotalPods()
            {
                int total = 0;
                foreach (var pods in Pods) total += pods.Count;
                return total;
            }

            private bool AnyOpponentHasCards(int seat)
            {
                for (int s = 0; s < PlayerCount; s++)
                {
                    if (s != seat && Hands[s].Count > 0) return true;
                }
                return false;
            }

            /// <summary>
            /// Moves the tide forward until the current diver can make a legal ask, handling
            /// empty hands and tables where nobody else holds cards, or ends the game.
            /// </summary>
            public void Normalize()
            {
                for (int step = 0; step < MaxNormalizeSteps; step++)
                {
                    if (TotalPods() == TideboundCards.PodCount)
                    {
                        EndGame();
                        return;
                    }

                    int seat = Seat;
                    if (Hands[seat].Count == 0)
                    {
                        if (Current.Count > 0)
                        {
                            int card = DrawTop();
                            AddToHand(seat, card);
                            Events.Add(new TideEvent(TideEventKind.Refilled, seat, creature: TideboundCards.CreatureOf(card), count: 1, card: card));
                            FormPods(seat);
                        }
                        else
                        {
                            Seat = Next(seat);
                        }
                        continue;
                    }

                    if (!AnyOpponentHasCards(seat))
                    {
                        if (Current.Count > 0)
                        {
                            int card = DrawTop();
                            AddToHand(seat, card);
                            Events.Add(new TideEvent(TideEventKind.NoOneToAsk, seat, creature: TideboundCards.CreatureOf(card), count: 1, card: card));
                            FormPods(seat);
                            Seat = Next(seat);
                            continue;
                        }

                        // Every remaining card is in this hand, so it is made of complete pods.
                        FormPods(seat);
                        continue;
                    }

                    return;
                }

                // Safety net: should be unreachable, but never leave a match stuck.
                EndGame();
            }

            private void EndGame()
            {
                Seat = TideboundState.NoSeat;
                Events.Add(new TideEvent(TideEventKind.GameOver, TideboundState.NoSeat));
            }

            public TideboundState Freeze(int firstSeat, int turn)
            {
                var hands = new int[PlayerCount][];
                var pods = new int[PlayerCount][];
                for (int s = 0; s < PlayerCount; s++)
                {
                    hands[s] = Hands[s].ToArray();
                    pods[s] = Pods[s].ToArray();
                }
                return new TideboundState(hands, Current.ToArray(), pods, Seat, firstSeat, turn, Events.ToArray());
            }

            private static int[] ToArray(IReadOnlyList<int> list)
            {
                var array = new int[list.Count];
                for (int i = 0; i < array.Length; i++) array[i] = list[i];
                return array;
            }
        }
    }
}
