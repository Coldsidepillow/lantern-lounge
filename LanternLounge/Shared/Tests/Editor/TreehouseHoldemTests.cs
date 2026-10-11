using System;
using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.TreehouseHoldem;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class TreehouseHoldemTests
    {
        private const Suit H = Suit.Hearts, D = Suit.Diamonds, C = Suit.Clubs, S = Suit.Spades;
        private static int Card(int rank, Suit suit) => HoldemCards.Id(rank, suit);

        private HoldemModule module;

        [SetUp]
        public void SetUp()
        {
            module = new HoldemModule();
        }

        /// <summary>These cards first (top first), then every other card in id order.</summary>
        private static int[] Deck(params int[] top)
        {
            var deck = new List<int>(top);
            for (int c = 0; c < HoldemCards.DeckSize; c++)
            {
                if (!deck.Contains(c)) deck.Add(c);
            }
            return deck.ToArray();
        }

        private static HandValue Eval(params int[] cards) => HandEvaluator.Evaluate(cards);

        private static int Sum(IReadOnlyList<int> values)
        {
            int total = 0;
            foreach (int v in values) total += v;
            return total;
        }

        private HoldemState Play(HoldemState state, int seat, HoldemMove move)
        {
            var check = module.Validate(state, seat, move);
            Assert.IsTrue(check.Ok, check.ToString());
            return module.Apply(state, seat, move);
        }

        private HoldemState CheckUntilRoundEnds(HoldemState state)
        {
            int guard = 0;
            while (state.Phase == Phase.Betting && guard++ < 100) state = Play(state, state.CurrentSeat, HoldemMove.Check);
            return state;
        }

        // ---------- Hand rankings ----------

        [Test]
        public void Evaluator_NamesEveryCategory()
        {
            Assert.AreEqual(HandCategory.RoyalFlush, Eval(Card(14, H), Card(13, H), Card(12, H), Card(11, H), Card(10, H)).Category);
            Assert.AreEqual(HandCategory.StraightFlush, Eval(Card(9, S), Card(8, S), Card(7, S), Card(6, S), Card(5, S)).Category);
            Assert.AreEqual(HandCategory.FourOfAKind, Eval(Card(9, S), Card(9, H), Card(9, D), Card(9, C), Card(5, S)).Category);
            Assert.AreEqual(HandCategory.FullHouse, Eval(Card(9, S), Card(9, H), Card(9, D), Card(5, C), Card(5, S)).Category);
            Assert.AreEqual(HandCategory.Flush, Eval(Card(2, D), Card(9, D), Card(11, D), Card(5, D), Card(7, D)).Category);
            Assert.AreEqual(HandCategory.Straight, Eval(Card(10, D), Card(9, C), Card(8, D), Card(7, H), Card(6, D)).Category);
            Assert.AreEqual(HandCategory.ThreeOfAKind, Eval(Card(10, D), Card(10, C), Card(10, H), Card(7, H), Card(6, D)).Category);
            Assert.AreEqual(HandCategory.TwoPair, Eval(Card(10, D), Card(10, C), Card(7, S), Card(7, H), Card(6, D)).Category);
            Assert.AreEqual(HandCategory.Pair, Eval(Card(10, D), Card(10, C), Card(2, S), Card(7, H), Card(6, D)).Category);
            Assert.AreEqual(HandCategory.HighCard, Eval(Card(13, D), Card(10, C), Card(2, S), Card(7, H), Card(6, D)).Category);
        }

        [Test]
        public void Evaluator_LowStraightCountsAceAsOne()
        {
            var wheel = Eval(Card(14, D), Card(2, C), Card(3, S), Card(4, H), Card(5, D));
            var sixHigh = Eval(Card(6, D), Card(2, C), Card(3, S), Card(4, H), Card(5, D));
            Assert.AreEqual(HandCategory.Straight, wheel.Category);
            Assert.AreEqual(5, wheel.TopRank);
            Assert.Greater(sixHigh.Score, wheel.Score);
            Assert.Greater(wheel.Score, Eval(Card(14, D), Card(14, C), Card(14, S), Card(4, H), Card(5, D)).Score);
        }

        [Test]
        public void Evaluator_PicksBestFiveOfSevenAndUsesKickers()
        {
            // Hole A-K, board K-K-7-7-2: Full House, Kings over Sevens.
            var value = Eval(Card(14, S), Card(13, H), Card(13, D), Card(13, C), Card(7, S), Card(7, H), Card(2, D));
            Assert.AreEqual(HandCategory.FullHouse, value.Category);
            Assert.AreEqual(13, value.TopRank);
            Assert.AreEqual(7, value.SecondRank);
            Assert.AreEqual(5, value.Best.Count);
            Assert.AreEqual("Full House: Kings over Sevens", HandEvaluator.Describe(value));

            // Same pair, better kicker wins.
            var aceKicker = Eval(Card(9, S), Card(9, H), Card(14, D), Card(4, C), Card(3, S));
            var kingKicker = Eval(Card(9, D), Card(9, C), Card(13, D), Card(4, H), Card(3, H));
            Assert.Greater(aceKicker.Score, kingKicker.Score);

            // Identical ranks in different suits tie.
            Assert.AreEqual(0, HandEvaluator.Compare(
                new[] { Card(9, S), Card(9, H), Card(14, D), Card(4, C), Card(3, S) },
                new[] { Card(9, D), Card(9, C), Card(14, H), Card(4, H), Card(3, H) }));
        }

        [Test]
        public void Evaluator_TwoCardsBeforeTheFlop()
        {
            Assert.AreEqual(HandCategory.Pair, Eval(Card(8, S), Card(8, H)).Category);
            Assert.AreEqual(HandCategory.HighCard, Eval(Card(8, S), Card(14, H)).Category);
        }

        [Test]
        public void Odds_PocketAcesBeatOneRandomHandMostOfTheTime()
        {
            float aces = HoldemOdds.WinChance(new[] { Card(14, S), Card(14, H) }, Array.Empty<int>(), 1, new SeededRandom(3), 400);
            float junk = HoldemOdds.WinChance(new[] { Card(7, S), Card(2, H) }, Array.Empty<int>(), 1, new SeededRandom(3), 400);
            Assert.Greater(aces, 0.75f);
            Assert.Less(junk, 0.45f);
        }

        // ---------- Starting a round ----------

        [Test]
        public void Setup_CollectsStartCoinsAndDealsTwoSecretCards()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            CollectionAssert.AreEqual(new[] { 98, 98, 98, 98 }, state.Coins);
            Assert.AreEqual(8, state.Pot);
            Assert.AreEqual(0, state.CurrentSeat, "The player left of the dealer goes first.");
            Assert.AreEqual(Street.Start, state.Street);
            Assert.AreEqual(1, state.RoundNumber);
            CollectionAssert.AreEqual(new[] { 0, 4 }, state.HoleCardsOf(0));
            CollectionAssert.AreEqual(new[] { 3, 7 }, state.HoleCardsOf(3));
            Assert.AreEqual(0, state.Board.Count);
        }

        [Test]
        public void View_HidesOtherPlayersCardsUntilTheShowdown()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            var view = module.ViewFor(state, 0);
            Assert.AreEqual(2, view.MyCards.Count);
            for (int s = 1; s < 4; s++) Assert.AreEqual(0, view.CardsOf(s).Count);

            state = CheckUntilRoundEnds(state);
            view = module.ViewFor(state, 0);
            for (int s = 1; s < 4; s++) Assert.AreEqual(2, view.CardsOf(s).Count, "Shown at the showdown.");
        }

        [Test]
        public void CheckingAround_DealsTheFlopTurnAndRiver()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            for (int s = 0; s < 4; s++) state = Play(state, s, HoldemMove.Check);
            Assert.AreEqual(Street.Flop, state.Street);
            CollectionAssert.AreEqual(new[] { 8, 9, 10 }, state.Board);
            Assert.AreEqual(0, state.CurrentSeat);

            for (int s = 0; s < 4; s++) state = Play(state, s, HoldemMove.Check);
            Assert.AreEqual(Street.Turn, state.Street);
            Assert.AreEqual(4, state.Board.Count);

            state = CheckUntilRoundEnds(state);
            Assert.AreEqual(5, state.Board.Count);
            Assert.AreEqual(Phase.RoundOver, state.Phase);
            Assert.AreEqual(400, Sum(state.Coins), "Coins are never created or lost.");
            Assert.AreEqual(0, state.Pot);
        }

        // ---------- Betting ----------

        [Test]
        public void Validate_RefusesSillyMoves()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            Assert.AreEqual(HoldemModule.CheckIsFree, module.Validate(state, 0, HoldemMove.Fold).Code);
            Assert.AreEqual(HoldemModule.NothingToCall, module.Validate(state, 0, HoldemMove.Call).Code);
            Assert.AreEqual(ErrorCodes.NotYourTurn, module.Validate(state, 1, HoldemMove.Check).Code);
            Assert.AreEqual(HoldemModule.RoundNotOver, module.Validate(state, 0, HoldemMove.NextRound).Code);

            state = Play(state, 0, HoldemMove.Raise);
            Assert.AreEqual(HoldemModule.MustMatch, module.Validate(state, 1, HoldemMove.Check).Code);
        }

        [Test]
        public void Raise_AddsFiveCoinsAndEveryoneMustAnswer()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            state = Play(state, 0, HoldemMove.Raise);
            Assert.AreEqual(5, state.CurrentBet);
            Assert.AreEqual(93, state.Coins[0]);

            state = Play(state, 1, HoldemMove.Call);
            state = Play(state, 2, HoldemMove.Fold);
            state = Play(state, 3, HoldemMove.Fold);
            Assert.AreEqual(Street.Flop, state.Street);
            Assert.AreEqual(0, state.CurrentSeat);
            CollectionAssert.AreEqual(new[] { 93, 93, 98, 98 }, state.Coins);
            Assert.AreEqual(18, state.Pot);
            Assert.AreEqual(HoldemModule.BigRaise, HoldemModule.RaiseSizeFor(Street.Turn));
        }

        [Test]
        public void Raise_AtMostThreePerBettingRound()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            state = Play(state, 0, HoldemMove.Raise);
            state = Play(state, 1, HoldemMove.Raise);
            state = Play(state, 2, HoldemMove.Raise);
            Assert.AreEqual(15, state.CurrentBet);
            Assert.IsFalse(module.ViewFor(state, 3).CanRaise);
            Assert.AreEqual(HoldemModule.NoMoreRaises, module.Validate(state, 3, HoldemMove.Raise).Code);
        }

        [Test]
        public void EveryoneFolds_LastPlayerWinsWithoutShowing()
        {
            var state = module.SetupWithDeck(4, Deck(), dealer: 3);
            state = Play(state, 0, HoldemMove.Raise);
            state = Play(state, 1, HoldemMove.Fold);
            state = Play(state, 2, HoldemMove.Fold);
            state = Play(state, 3, HoldemMove.Fold);
            Assert.AreEqual(Phase.RoundOver, state.Phase);
            CollectionAssert.AreEqual(new[] { 106, 98, 98, 98 }, state.Coins);
            for (int s = 0; s < 4; s++) Assert.IsFalse(state.Revealed[s]);

            bool sawWin = false;
            foreach (var e in state.LastEvents)
            {
                if (e.Kind == HoldemEventKind.WonCoins)
                {
                    sawWin = true;
                    Assert.AreEqual(0, e.Seat);
                    Assert.AreEqual(13, e.Amount);
                    Assert.AreEqual(-1, e.Extra);
                }
            }
            Assert.IsTrue(sawWin);
        }

        [Test]
        public void SidePot_AllInPlayerOnlyWinsWhatTheyMatched()
        {
            // Seat 0 (12 coins) has Aces, seat 1 Kings, seat 2 nothing.
            var deck = Deck(Card(14, H), Card(13, H), Card(7, C), Card(14, S), Card(13, S), Card(2, D),
                Card(9, C), Card(4, D), Card(11, H), Card(3, S), Card(8, D));
            var state = module.SetupWithStacks(new[] { 12, 50, 50 }, deck, dealer: 2);
            CollectionAssert.AreEqual(new[] { 10, 48, 48 }, state.Coins);

            state = Play(state, 0, HoldemMove.Raise);
            state = Play(state, 1, HoldemMove.Raise);
            state = Play(state, 2, HoldemMove.Raise);
            state = Play(state, 0, HoldemMove.Call); // all in
            Assert.AreEqual(0, state.Coins[0]);
            state = Play(state, 1, HoldemMove.Call);
            Assert.AreEqual(Street.Flop, state.Street);
            Assert.AreEqual(1, state.CurrentSeat, "The all-in player is skipped.");

            state = CheckUntilRoundEnds(state);
            Assert.AreEqual(Phase.RoundOver, state.Phase);
            CollectionAssert.AreEqual(new[] { 36, 43, 33 }, state.Coins);
        }

        [Test]
        public void ShortStack_AllInFromStartCoinsRunsTheBoardOut()
        {
            var deck = Deck(Card(2, C), Card(14, H), Card(3, D), Card(14, D), Card(13, S), Card(9, C), Card(5, D), Card(8, H), Card(11, C));
            var state = module.SetupWithStacks(new[] { 50, 1 }, deck, dealer: 1);
            Assert.AreEqual(5, state.Board.Count);
            CollectionAssert.AreEqual(new[] { 49, 2 }, state.Coins);
            Assert.AreEqual(Phase.RoundOver, state.Phase);
        }

        // ---------- The whole night ----------

        [Test]
        public void LastRound_EndsGameNightAndMostCoinsWins()
        {
            var deck = Deck(Card(14, H), Card(2, C), Card(14, D), Card(7, D), Card(13, S), Card(9, C), Card(5, D), Card(3, H), Card(11, C));
            var state = module.SetupWithDeck(2, deck, dealer: 1, rounds: 1);
            Assert.AreEqual(0, state.CurrentSeat);
            state = CheckUntilRoundEnds(state);
            Assert.IsTrue(state.IsOver);
            CollectionAssert.AreEqual(new[] { 102, 98 }, state.Coins);
            CollectionAssert.AreEqual(new[] { 0 }, module.Result(state).WinningSeats);
        }

        [Test]
        public void NextRound_MovesTheDealerButton()
        {
            var state = module.SetupWithDeck(3, Deck(), dealer: 0);
            state = CheckUntilRoundEnds(state);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, module.SeatsToAct(state));
            state = Play(state, 2, HoldemMove.NextRound);
            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(1, state.Dealer);
            Assert.AreEqual(2, state.CurrentSeat);
        }

        [Test]
        public void Rematch_StartsAFreshNight()
        {
            var state = module.SetupWithDeck(2, Deck(), dealer: 0, rounds: 1);
            state = CheckUntilRoundEnds(state);
            Assert.IsTrue(state.IsOver);
            var again = module.Rematch(state, 9);
            Assert.AreEqual(1, again.RoundNumber);
            Assert.AreEqual(Phase.Betting, again.Phase);
            Assert.AreEqual(196, Sum(again.Coins));
        }

        [Test]
        public void BotGames_AlwaysFinishAndNeverLoseCoins()
        {
            for (int game = 0; game < 6; game++)
            {
                int players = 2 + game % 3;
                var match = new LocalMatch<HoldemState, HoldemMove, HoldemView>(module,
                    new MatchConfig(players, (ulong)(game * 101 + 7), new Dictionary<string, string> { { "rounds", "5" } }));
                var bots = new HoldemBot[players];
                for (int s = 0; s < players; s++) bots[s] = new HoldemBot((ulong)(game * 13 + s), 0.3f + 0.2f * s, 30);

                int guard = 0;
                while (!match.IsOver && guard++ < 2000)
                {
                    var state = match.State;
                    if (state.Phase == Phase.RoundOver)
                    {
                        Assert.IsTrue(match.TryPlay(0, HoldemMove.NextRound).Ok);
                        continue;
                    }
                    Assert.AreEqual(players * HoldemModule.DefaultCoins, Sum(state.Coins) + state.Pot);
                    int seat = state.CurrentSeat;
                    var move = bots[seat].ChooseMove(module.ViewFor(state, seat));
                    var result = match.TryPlay(seat, move);
                    Assert.IsTrue(result.Ok, result.ToString());
                }
                Assert.IsTrue(match.IsOver, "The night should end.");
                Assert.AreEqual(players * HoldemModule.DefaultCoins, Sum(match.State.Coins));
            }
        }
    }
}
