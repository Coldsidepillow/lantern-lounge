using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.PrismRush
{
    /// <summary>
    /// Prism Rush: be the first to play away every card.
    ///
    /// On your turn, play a card that matches the color in play, matches the top card's number,
    /// or is the same action as the top card. Prism cards go on anything and let you pick the
    /// next color. Chain: play several number cards with the same number at once, in any colors
    /// (the first must be playable; the last sets the color).
    /// Actions: Flip reverses direction (with two players it acts like Hop), Hop skips the next
    /// player, Pulse makes the next player draw 2 unless they answer with their own Pulse
    /// (which passes it on, growing by 2).
    /// Can't play (or don't want to)? Draw, and keep drawing until you pull a card that can be
    /// played (matching the color, the number, the action, or a Prism). You may then play that
    /// card right away or keep it. If the deck runs out first, your turn ends.
    ///
    /// Options: "firstSeat" = a seat number, or "random" (default; uses the match seed).
    /// </summary>
    public sealed class PrismModule : GameModule<PrismState, PrismMove, PrismView>
    {
        public const string GameId = "prism-rush";

        public const string NoCardsChosen = "no_cards";
        public const string NotInHand = "not_in_hand";
        public const string NotPlayable = "not_playable";
        public const string BadChain = "bad_chain";
        public const string ChooseColor = "choose_color";
        public const string PlayOrKeepDrawn = "play_or_keep";
        public const string PlayTheDrawnCard = "play_drawn_card";
        public const string CannotKeep = "cannot_keep";

        public static readonly GameInfo Definition =
            new GameInfo(GameId, "Prism Rush", 2, 4, GameStatus.Playable, "prism-rush");

        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        public override GameInfo Info => Definition;

        /// <summary>The matching rule, shared with views and bots.</summary>
        public static bool IsPlayable(int card, int topCard, int activeColor, int pendingDraw)
        {
            var c = PrismCards.Card(card);
            if (pendingDraw > 0) return c.Kind == CardKind.Pulse;
            if (c.Kind == CardKind.Prism) return true;
            if (c.Color == activeColor) return true;
            var top = PrismCards.Card(topCard);
            if (c.Kind == CardKind.Number && top.Kind == CardKind.Number && c.Number == top.Number) return true;
            if (c.Kind != CardKind.Number && c.Kind == top.Kind) return true;
            return false;
        }

        protected override PrismState CreateInitialState(MatchConfig config)
        {
            var random = new SeededRandom(config.Seed);
            var deck = new int[PrismCards.DeckSize];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            random.Shuffle(deck);

            string first = config.GetOption("firstSeat", "random");
            int firstSeat;
            if (!int.TryParse(first, out firstSeat) || firstSeat < 0 || firstSeat >= config.PlayerCount)
            {
                firstSeat = random.NextInt(config.PlayerCount);
            }
            return Deal(config.PlayerCount, deck, firstSeat, config.Seed ^ 0xABCDEFUL);
        }

        /// <summary>Starts from a known deck order (top first), for tutorials, replays and tests.</summary>
        public PrismState SetupWithDeck(int playerCount, IReadOnlyList<int> deckTopFirst, int firstSeat, ulong reshuffleSeed = 1)
        {
            if (!Info.SupportsPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (firstSeat < 0 || firstSeat >= playerCount) throw new ArgumentOutOfRangeException(nameof(firstSeat));
            if (deckTopFirst == null || deckTopFirst.Count != PrismCards.DeckSize)
                throw new ArgumentException($"The deck must have exactly {PrismCards.DeckSize} cards.", nameof(deckTopFirst));
            var seen = new bool[PrismCards.DeckSize];
            var deck = new int[PrismCards.DeckSize];
            for (int i = 0; i < deck.Length; i++)
            {
                int card = deckTopFirst[i];
                if (!PrismCards.IsValidCard(card) || seen[card])
                    throw new ArgumentException("The deck must contain each card exactly once.", nameof(deckTopFirst));
                seen[card] = true;
                deck[i] = card;
            }
            return Deal(playerCount, deck, firstSeat, reshuffleSeed);
        }

        /// <summary>A fresh deal for a rematch; the next player starts.</summary>
        public PrismState Rematch(PrismState previous, ulong seed)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var random = new SeededRandom(seed);
            var deck = new int[PrismCards.DeckSize];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            random.Shuffle(deck);
            return Deal(previous.PlayerCount, deck, (previous.FirstSeat + 1) % previous.PlayerCount, seed ^ 0xABCDEFUL);
        }

        private static PrismState Deal(int playerCount, int[] deck, int firstSeat, ulong reshuffleSeed)
        {
            var table = new Table(playerCount, deck, reshuffleSeed);
            for (int round = 0; round < PrismCards.HandSize; round++)
            {
                for (int seat = 0; seat < playerCount; seat++) table.Hands[seat].Add(table.TakeTop());
            }
            foreach (var hand in table.Hands) hand.Sort();

            // The starting card is always a number: action and Prism cards go to the bottom.
            while (PrismCards.Card(table.DrawPile[0]).Kind != CardKind.Number)
            {
                table.DrawPile.Add(table.TakeTop());
            }
            int start = table.TakeTop();
            table.Discard.Add(start);
            table.ActiveColor = PrismCards.Card(start).Color;
            table.Seat = firstSeat;
            return table.Freeze(firstSeat, 0);
        }

        public override ValidationResult Validate(PrismState state, int seat, PrismMove move)
        {
            if (state == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no table.");
            if (move == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no move.");
            if (state.IsOver) return ValidationResult.Fail(ErrorCodes.GameOver, "This round is over.");
            if (seat < 0 || seat >= state.PlayerCount) return ValidationResult.Fail(ErrorCodes.BadSeat, "That seat isn't at this table.");
            if (seat != state.CurrentSeat) return ValidationResult.Fail(ErrorCodes.NotYourTurn, "It isn't your turn yet.");

            switch (move.Kind)
            {
                case MoveKind.Keep:
                    return state.DrawnCard >= 0
                        ? ValidationResult.Success
                        : ValidationResult.Fail(CannotKeep, "You can only keep a card you just drew.");
                case MoveKind.Draw:
                    return state.DrawnCard >= 0
                        ? ValidationResult.Fail(PlayOrKeepDrawn, "Play the card you drew, or keep it.")
                        : ValidationResult.Success;
            }

            var cards = move.Cards;
            if (cards.Count == 0) return ValidationResult.Fail(NoCardsChosen, "Pick a card to play.");

            var hand = state.HandOf(seat);
            for (int i = 0; i < cards.Count; i++)
            {
                if (!PrismCards.IsValidCard(cards[i]) || !Contains(hand, cards[i]))
                    return ValidationResult.Fail(NotInHand, "That card isn't in your hand.");
                for (int j = 0; j < i; j++)
                {
                    if (cards[j] == cards[i]) return ValidationResult.Fail(NotInHand, "Each card can only be played once.");
                }
            }

            if (state.DrawnCard >= 0 && (cards.Count != 1 || cards[0] != state.DrawnCard))
                return ValidationResult.Fail(PlayTheDrawnCard, "You can play the card you just drew, or keep it.");

            if (!IsPlayable(cards[0], state.TopCard, state.ActiveColor, state.PendingDraw))
            {
                return state.PendingDraw > 0
                    ? ValidationResult.Fail(NotPlayable, "Only a Pulse can answer a Pulse. Otherwise, draw.")
                    : ValidationResult.Fail(NotPlayable, "That card doesn't match the color, number or action.");
            }

            if (cards.Count > 1)
            {
                var lead = PrismCards.Card(cards[0]);
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = PrismCards.Card(cards[i]);
                    if (lead.Kind != CardKind.Number || c.Kind != CardKind.Number || c.Number != lead.Number)
                        return ValidationResult.Fail(BadChain, "A chain must be number cards that all share the same number.");
                }
            }

            if (PrismCards.Card(cards[cards.Count - 1]).Kind == CardKind.Prism &&
                (move.ChosenColor < 0 || move.ChosenColor >= PrismCards.ColorCount))
            {
                return ValidationResult.Fail(ChooseColor, "Pick the next color for your Prism.");
            }

            return ValidationResult.Success;
        }

        protected override PrismState ApplyValidated(PrismState state, int seat, PrismMove move)
        {
            var t = Table.From(state);
            switch (move.Kind)
            {
                case MoveKind.Keep:
                    t.DrawnCard = PrismState.NoCard;
                    t.Events.Add(new PrismEvent(PrismEventKind.Kept, seat));
                    t.Seat = t.Next(seat);
                    break;
                case MoveKind.Draw:
                    ApplyDraw(t, seat);
                    break;
                default:
                    ApplyPlay(t, seat, move);
                    break;
            }
            return t.Freeze(state.FirstSeat, state.Turn + 1);
        }

        private static void ApplyDraw(Table t, int seat)
        {
            if (t.PendingDraw > 0)
            {
                int owed = t.PendingDraw;
                int got = 0;
                for (int i = 0; i < owed; i++)
                {
                    int card = t.DrawOne();
                    if (card < 0) break;
                    t.AddToHand(seat, card);
                    got++;
                }
                t.Events.Add(new PrismEvent(PrismEventKind.PulsePenalty, seat, got));
                t.PendingDraw = 0;
                t.Passes = 0;
                t.Seat = t.Next(seat);
                return;
            }

            // Keep drawing until a card can be played. Every card drawn stays in the hand.
            int drewCount = 0;
            int drawn = -1;
            bool playable = false;
            while (true)
            {
                int card = t.DrawOne();
                if (card < 0) break;
                t.AddToHand(seat, card);
                drewCount++;
                drawn = card;
                if (IsPlayable(card, t.Top, t.ActiveColor, 0))
                {
                    playable = true;
                    break;
                }
            }

            if (drewCount == 0)
            {
                t.Events.Add(new PrismEvent(PrismEventKind.NothingToDraw, seat));
                t.Passes++;
                if (t.Passes >= t.PlayerCount)
                {
                    t.EndByStalemate();
                    return;
                }
                t.Seat = t.Next(seat);
                return;
            }

            t.Passes = 0;
            // One event for the whole draw: Count cards were taken, and Card is the last one.
            t.Events.Add(new PrismEvent(PrismEventKind.Drew, seat, drewCount, drawn));
            if (playable) t.DrawnCard = drawn;
            else t.Seat = t.Next(seat);
        }

        private static void ApplyPlay(Table t, int seat, PrismMove move)
        {
            foreach (int card in move.Cards)
            {
                t.Hands[seat].Remove(card);
                t.Discard.Add(card);
            }
            t.DrawnCard = PrismState.NoCard;
            t.Passes = 0;

            var top = PrismCards.Card(move.Cards[move.Cards.Count - 1]);
            t.ActiveColor = top.Kind == CardKind.Prism ? move.ChosenColor : top.Color;
            var played = new int[move.Cards.Count];
            for (int i = 0; i < played.Length; i++) played[i] = move.Cards[i];
            t.Events.Add(new PrismEvent(PrismEventKind.Played, seat, played.Length, color: t.ActiveColor, cards: Array.AsReadOnly(played)));

            if (t.Hands[seat].Count == 1) t.Events.Add(new PrismEvent(PrismEventKind.LastLight, seat));
            if (t.Hands[seat].Count == 0)
            {
                t.Winners.Add(seat);
                t.Seat = PrismState.NoSeat;
                t.Events.Add(new PrismEvent(PrismEventKind.Won, seat));
                return;
            }

            switch (top.Kind)
            {
                case CardKind.Flip:
                    t.Direction = -t.Direction;
                    t.Events.Add(new PrismEvent(PrismEventKind.Flipped, seat, t.Direction));
                    t.Seat = t.PlayerCount == 2 ? seat : t.Next(seat);
                    break;
                case CardKind.Hop:
                    t.Events.Add(new PrismEvent(PrismEventKind.Hopped, t.Next(seat)));
                    t.Seat = t.Next(seat, 2);
                    break;
                case CardKind.Pulse:
                    t.PendingDraw += 2;
                    t.Events.Add(new PrismEvent(PrismEventKind.Pulsed, t.Next(seat), t.PendingDraw));
                    t.Seat = t.Next(seat);
                    break;
                default:
                    t.Seat = t.Next(seat);
                    break;
            }
        }

        public override PrismView ViewFor(PrismState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seat < 0 || seat >= state.PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return new PrismView(state, seat);
        }

        public override MatchResult Result(PrismState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.IsOver) return MatchResult.InProgress;
            if (state.Winners.Count == state.PlayerCount) return MatchResult.Draw();
            var seats = new int[state.Winners.Count];
            for (int i = 0; i < seats.Length; i++) seats[i] = state.Winners[i];
            return MatchResult.Win(seats);
        }

        public override IReadOnlyList<int> SeatsToAct(PrismState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.IsOver ? NoSeats : new[] { state.CurrentSeat };
        }

        /// <summary>When a turn timer runs out: keep a drawn card, take a Pulse penalty, play the first playable card, or draw.</summary>
        public override bool TryGetTimeoutAction(PrismState state, int seat, out PrismMove action)
        {
            action = null;
            if (state == null || state.IsOver || seat != state.CurrentSeat) return false;
            if (state.DrawnCard >= 0)
            {
                action = PrismMove.Keep();
                return true;
            }
            if (state.PendingDraw == 0)
            {
                foreach (int card in state.HandOf(seat))
                {
                    if (!IsPlayable(card, state.TopCard, state.ActiveColor, 0)) continue;
                    action = PrismCards.Card(card).Kind == CardKind.Prism ? PrismMove.PlayWithColor(0, card) : PrismMove.Play(card);
                    return true;
                }
            }
            action = PrismMove.Draw();
            return true;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        /// <summary>A mutable working copy used while resolving one move.</summary>
        private sealed class Table
        {
            public readonly List<int>[] Hands;
            public readonly List<int> DrawPile;
            public readonly List<int> Discard;
            public readonly List<int> Winners = new List<int>();
            public readonly List<PrismEvent> Events = new List<PrismEvent>();
            public readonly SeededRandom Random;
            public int ActiveColor;
            public int Direction = 1;
            public int PendingDraw;
            public int Seat;
            public int DrawnCard = PrismState.NoCard;
            public int Passes;

            public Table(int playerCount, int[] deckTopFirst, ulong randomState)
            {
                Hands = new List<int>[playerCount];
                for (int s = 0; s < playerCount; s++) Hands[s] = new List<int>();
                DrawPile = new List<int>(deckTopFirst);
                Discard = new List<int>();
                Random = new SeededRandom(randomState);
            }

            public static Table From(PrismState state)
            {
                var drawPile = new int[state.DrawPile.Count];
                for (int i = 0; i < drawPile.Length; i++) drawPile[i] = state.DrawPile[i];
                var t = new Table(state.PlayerCount, drawPile, 0);
                t.Random.State = state.RandomState;
                for (int s = 0; s < state.PlayerCount; s++) t.Hands[s].AddRange(state.HandOf(s));
                t.Discard.AddRange(state.Discard);
                t.ActiveColor = state.ActiveColor;
                t.Direction = state.Direction;
                t.PendingDraw = state.PendingDraw;
                t.Seat = state.CurrentSeat;
                t.DrawnCard = state.DrawnCard;
                t.Passes = state.Passes;
                t.Winners.AddRange(state.Winners);
                return t;
            }

            public int PlayerCount => Hands.Length;

            public int Top => Discard[Discard.Count - 1];

            public int Next(int seat, int steps = 1)
            {
                int n = PlayerCount;
                return (((seat + Direction * steps) % n) + n) % n;
            }

            public int TakeTop()
            {
                int card = DrawPile[0];
                DrawPile.RemoveAt(0);
                return card;
            }

            /// <summary>Draws the top card, reshuffling the played pile if needed. Returns -1 if there is nothing at all.</summary>
            public int DrawOne()
            {
                if (DrawPile.Count == 0)
                {
                    if (Discard.Count <= 1) return -1;
                    int top = Discard[Discard.Count - 1];
                    Discard.RemoveAt(Discard.Count - 1);
                    var cards = Discard.ToArray();
                    Random.Shuffle(cards);
                    DrawPile.AddRange(cards);
                    Discard.Clear();
                    Discard.Add(top);
                    Events.Add(new PrismEvent(PrismEventKind.Reshuffled, -1, cards.Length));
                }
                return TakeTop();
            }

            public void AddToHand(int seat, int card)
            {
                var hand = Hands[seat];
                int index = hand.BinarySearch(card);
                hand.Insert(index < 0 ? ~index : index, card);
            }

            public void EndByStalemate()
            {
                int fewest = int.MaxValue;
                foreach (var hand in Hands) fewest = Math.Min(fewest, hand.Count);
                Winners.Clear();
                for (int s = 0; s < PlayerCount; s++)
                {
                    if (Hands[s].Count == fewest) Winners.Add(s);
                }
                Seat = PrismState.NoSeat;
                Events.Add(new PrismEvent(PrismEventKind.Stalemate, -1));
            }

            public PrismState Freeze(int firstSeat, int turn)
            {
                var hands = new int[PlayerCount][];
                for (int s = 0; s < PlayerCount; s++) hands[s] = Hands[s].ToArray();
                return new PrismState(hands, DrawPile.ToArray(), Discard.ToArray(), ActiveColor, Direction, PendingDraw,
                    Seat, DrawnCard, Passes, Winners.ToArray(), Random.State, firstSeat, turn, Events.ToArray());
            }
        }
    }
}
