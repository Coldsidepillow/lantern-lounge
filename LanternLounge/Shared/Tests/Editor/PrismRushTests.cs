using System;
using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.PrismRush;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class PrismRushTests
    {
        // Card ids: color c has ids 20c..20c+19: numbers 1-7 twice (20c + 2(n-1) + copy),
        // then Flip (+14, +15), Hop (+16, +17), Pulse (+18, +19). Prisms are 80-83.
        private static int Num(int color, int number, int copy = 0) => 20 * color + 2 * (number - 1) + copy;
        private static int Flip(int color) => 20 * color + 14;
        private static int Hop(int color) => 20 * color + 16;
        private static int Pulse(int color) => 20 * color + 18;
        private const int Prism = 80;

        private const int Cyan = 0, Gold = 1, Rose = 2, Lime = 3;

        // Seat 0: Cyan 1, Cyan 2, Gold 5, Rose 5, Cyan Hop, Gold Pulse, Prism
        private static readonly int[] Hand0 = { Num(Cyan, 1), Num(Cyan, 2), Num(Gold, 5), Num(Rose, 5, 1), Hop(Cyan), Pulse(Gold), Prism };
        // Seat 1: Lime 1-4, Lime Flip, Lime Pulse, Rose 7
        private static readonly int[] Hand1 = { Num(Lime, 1), Num(Lime, 2), Num(Lime, 3), Num(Lime, 4), Flip(Lime), Pulse(Lime), Num(Rose, 7) };
        // Seat 2: Gold 1-4, Gold 6, Gold 7, Rose Hop
        private static readonly int[] Hand2 = { Num(Gold, 1), Num(Gold, 2), Num(Gold, 3), Num(Gold, 4), Num(Gold, 6), Num(Gold, 7), Hop(Rose) };
        // Seat 3: Rose 1-4, Rose 6, Rose Flip, Rose Pulse
        private static readonly int[] Hand3 = { Num(Rose, 1), Num(Rose, 2), Num(Rose, 3), Num(Rose, 4), Num(Rose, 6), Flip(Rose), Pulse(Rose) };

        private PrismModule module;

        [SetUp]
        public void SetUp()
        {
            module = new PrismModule();
        }

        /// <summary>Deals these hands round robin from seat 0, then <paramref name="after"/> (start card first), then the rest.</summary>
        private static int[] DeckFor(int[][] hands, params int[] after)
        {
            var deck = new List<int>();
            for (int round = 0; round < PrismCards.HandSize; round++)
            {
                for (int seat = 0; seat < hands.Length; seat++) deck.Add(hands[seat][round]);
            }
            deck.AddRange(after);
            for (int card = 0; card < PrismCards.DeckSize; card++)
            {
                if (!deck.Contains(card)) deck.Add(card);
            }
            return deck.ToArray();
        }

        /// <summary>Four players, Cyan 5 starting; <paramref name="next"/> are the next cards in the draw deck.</summary>
        private PrismState FourPlayers(params int[] next)
        {
            var after = new List<int> { Num(Cyan, 5) };
            after.AddRange(next);
            return module.SetupWithDeck(4, DeckFor(new[] { Hand0, Hand1, Hand2, Hand3 }, after.ToArray()), 0);
        }

        private PrismState Play(PrismState state, int seat, PrismMove move)
        {
            var check = module.Validate(state, seat, move);
            Assert.IsTrue(check.Ok, $"Seat {seat} {move} should be legal but was {check.Code}.");
            return module.Apply(state, seat, move);
        }

        private static int TotalCards(PrismState state)
        {
            int total = state.DrawPile.Count + state.Discard.Count;
            for (int s = 0; s < state.PlayerCount; s++) total += state.HandCount(s);
            return total;
        }

        [Test]
        public void DeckHasEightyFourCards()
        {
            Assert.AreEqual(84, PrismCards.DeckSize);
            int prisms = 0, pulses = 0, sevens = 0;
            for (int id = 0; id < PrismCards.DeckSize; id++)
            {
                var card = PrismCards.Card(id);
                Assert.AreEqual(id, card.Id);
                if (card.Kind == CardKind.Prism) prisms++;
                if (card.Kind == CardKind.Pulse) pulses++;
                if (card.Kind == CardKind.Number && card.Number == 7) sevens++;
            }
            Assert.AreEqual(4, prisms);
            Assert.AreEqual(8, pulses);
            Assert.AreEqual(8, sevens);
            Assert.AreEqual("Rose 5", PrismCards.Name(Num(Rose, 5)));
            Assert.AreEqual("Gold Hop", PrismCards.Name(Hop(Gold)));
        }

        [Test]
        public void DealsSevenEachAndStartsOnANumberCard()
        {
            var state = FourPlayers();
            for (int s = 0; s < 4; s++) Assert.AreEqual(7, state.HandCount(s));
            Assert.AreEqual(Num(Cyan, 5), state.TopCard);
            Assert.AreEqual(Cyan, state.ActiveColor);
            Assert.AreEqual(55, state.DrawPile.Count);
            Assert.AreEqual(0, state.CurrentSeat);

            for (ulong seed = 1; seed <= 40; seed++)
            {
                var random = module.Setup(new MatchConfig(4, seed));
                Assert.AreEqual(CardKind.Number, PrismCards.Card(random.TopCard).Kind);
                Assert.AreEqual(84, TotalCards(random));
            }
        }

        [Test]
        public void MatchingRule()
        {
            Assert.IsTrue(PrismModule.IsPlayable(Num(Cyan, 1), Num(Cyan, 5), Cyan, 0), "same color");
            Assert.IsTrue(PrismModule.IsPlayable(Num(Gold, 5), Num(Cyan, 5), Cyan, 0), "same number");
            Assert.IsTrue(PrismModule.IsPlayable(Hop(Gold), Hop(Cyan), Cyan, 0), "same action");
            Assert.IsTrue(PrismModule.IsPlayable(Prism, Num(Cyan, 5), Cyan, 0), "Prism goes on anything");
            Assert.IsFalse(PrismModule.IsPlayable(Num(Gold, 4), Num(Cyan, 5), Cyan, 0));
            Assert.IsFalse(PrismModule.IsPlayable(Pulse(Gold), Num(Cyan, 5), Cyan, 0));
            Assert.IsTrue(PrismModule.IsPlayable(Num(Rose, 2), Prism, Rose, 0), "after a Prism, the chosen color counts");
            Assert.IsTrue(PrismModule.IsPlayable(Pulse(Gold), Pulse(Rose), Rose, 2), "a Pulse answers a Pulse");
            Assert.IsFalse(PrismModule.IsPlayable(Num(Rose, 2), Pulse(Rose), Rose, 2), "only a Pulse answers a Pulse");
            Assert.IsFalse(PrismModule.IsPlayable(Prism, Pulse(Rose), Rose, 2));
        }

        [Test]
        public void AChainOfTheSameNumberPlaysTogetherAndTheLastCardSetsTheColor()
        {
            var state = Play(FourPlayers(), 0, PrismMove.Play(Num(Gold, 5), Num(Rose, 5, 1)));
            Assert.AreEqual(5, state.HandCount(0));
            Assert.AreEqual(Num(Rose, 5, 1), state.TopCard);
            Assert.AreEqual(Rose, state.ActiveColor);
            Assert.AreEqual(1, state.CurrentSeat);
        }

        [Test]
        public void ChainsMustShareANumber()
        {
            var state = FourPlayers();
            Assert.AreEqual(PrismModule.BadChain, module.Validate(state, 0, PrismMove.Play(Num(Cyan, 1), Num(Cyan, 2))).Code);
            Assert.AreEqual(PrismModule.NotPlayable, module.Validate(state, 0, PrismMove.Play(Pulse(Gold))).Code);
            Assert.AreEqual(PrismModule.NotInHand, module.Validate(state, 0, PrismMove.Play(Num(Lime, 1))).Code);
            Assert.AreEqual(ErrorCodes.NotYourTurn, module.Validate(state, 1, PrismMove.Draw()).Code);
            Assert.AreEqual(PrismModule.CannotKeep, module.Validate(state, 0, PrismMove.Keep()).Code);
        }

        [Test]
        public void HopSkipsTheNextPlayer()
        {
            var state = Play(FourPlayers(), 0, PrismMove.Play(Hop(Cyan)));
            Assert.AreEqual(2, state.CurrentSeat);
            Assert.AreEqual(PrismEventKind.Hopped, state.LastEvents[1].Kind);
            Assert.AreEqual(1, state.LastEvents[1].Seat);
        }

        [Test]
        public void PrismNeedsAColorAndThenThatColorIsInPlay()
        {
            var state = FourPlayers();
            Assert.AreEqual(PrismModule.ChooseColor, module.Validate(state, 0, PrismMove.Play(Prism)).Code);
            state = Play(state, 0, PrismMove.PlayWithColor(Rose, Prism));
            Assert.AreEqual(Rose, state.ActiveColor);
            Assert.AreEqual(1, state.CurrentSeat);
        }

        [Test]
        public void FlipReversesTheDirection()
        {
            // Seat 2 draws Lime 5 (no match for Rose 7), keeps drawing, and stops at Rose 5.
            var state = FourPlayers(Num(Lime, 5), Num(Rose, 5));
            state = Play(state, 0, PrismMove.Play(Num(Rose, 5, 1)));
            state = Play(state, 1, PrismMove.Play(Num(Rose, 7)));
            state = Play(state, 2, PrismMove.Draw());
            state = Play(state, 2, PrismMove.Keep());
            Assert.AreEqual(3, state.CurrentSeat);
            state = Play(state, 3, PrismMove.Play(Flip(Rose)));

            Assert.AreEqual(-1, state.Direction);
            Assert.AreEqual(2, state.CurrentSeat, "After a Flip, play goes back the other way.");
        }

        [Test]
        public void PulsesStackUntilSomeoneHasToDraw()
        {
            var state = FourPlayers(Num(Lime, 5), Num(Rose, 5));
            state = Play(state, 0, PrismMove.Play(Num(Rose, 5, 1)));
            state = Play(state, 1, PrismMove.Play(Num(Rose, 7)));
            state = Play(state, 2, PrismMove.Draw());
            state = Play(state, 2, PrismMove.Keep());
            state = Play(state, 3, PrismMove.Play(Pulse(Rose)));
            Assert.AreEqual(2, state.PendingDraw);
            state = Play(state, 0, PrismMove.Play(Pulse(Gold)));
            state = Play(state, 1, PrismMove.Play(Pulse(Lime)));
            Assert.AreEqual(6, state.PendingDraw);
            Assert.AreEqual(2, state.CurrentSeat);

            Assert.AreEqual(PrismModule.NotPlayable, module.Validate(state, 2, PrismMove.Play(Num(Gold, 1))).Code);
            state = Play(state, 2, PrismMove.Draw());

            Assert.AreEqual(0, state.PendingDraw);
            Assert.AreEqual(15, state.HandCount(2));
            Assert.AreEqual(3, state.CurrentSeat);
            Assert.AreEqual(PrismEventKind.PulsePenalty, state.LastEvents[0].Kind);
            Assert.AreEqual(6, state.LastEvents[0].Count);
            Assert.AreEqual(84, TotalCards(state));
        }

        [Test]
        public void DrawingAPlayableCardLetsYouPlayItOrKeepIt()
        {
            // Seat 1 has no gold and no 5, so it draws: the second Gold 5.
            var state = FourPlayers(Num(Gold, 5, 1));
            state = Play(state, 0, PrismMove.Play(Num(Gold, 5)));
            state = Play(state, 1, PrismMove.Draw());

            Assert.AreEqual(1, state.CurrentSeat);
            Assert.AreEqual(Num(Gold, 5, 1), state.DrawnCard);
            Assert.AreEqual(8, state.HandCount(1));
            Assert.AreEqual(PrismModule.PlayTheDrawnCard, module.Validate(state, 1, PrismMove.Play(Num(Lime, 1))).Code);
            Assert.AreEqual(PrismModule.PlayOrKeepDrawn, module.Validate(state, 1, PrismMove.Draw()).Code);
            Assert.IsTrue(module.Validate(state, 1, PrismMove.Play(Num(Gold, 5, 1))).Ok);

            // The other players see that a card was drawn, but not which one.
            Assert.AreEqual(PrismEvent.Hidden, module.ViewFor(state, 0).LastEvents[0].Card);
            Assert.AreEqual(Num(Gold, 5, 1), module.ViewFor(state, 1).LastEvents[0].Card);
            Assert.AreEqual(Num(Gold, 5, 1), module.ViewFor(state, 1).DrawnCard);
            Assert.AreEqual(PrismState.NoCard, module.ViewFor(state, 0).DrawnCard);

            state = Play(state, 1, PrismMove.Keep());
            Assert.AreEqual(2, state.CurrentSeat);
            Assert.AreEqual(PrismState.NoCard, state.DrawnCard);
            Assert.AreEqual(8, state.HandCount(1));
        }

        [Test]
        public void DrawingKeepsGoingUntilYouGetAPlayableCard()
        {
            // Gold 5 goes down. Seat 1 has no Gold and no 5, so it draws: Lime 6 and Rose 6
            // don't match, so it keeps drawing and stops at the second Gold 5.
            var state = FourPlayers(Num(Lime, 6), Num(Rose, 6, 1), Num(Gold, 5, 1));
            state = Play(state, 0, PrismMove.Play(Num(Gold, 5)));
            state = Play(state, 1, PrismMove.Draw());

            Assert.AreEqual(1, state.CurrentSeat, "Seat 1 may still play or keep the card that ended the draw.");
            Assert.AreEqual(Num(Gold, 5, 1), state.DrawnCard);
            Assert.AreEqual(10, state.HandCount(1), "Seat 1 keeps all three drawn cards.");
            Assert.AreEqual(1, state.LastEvents.Count);
            Assert.AreEqual(PrismEventKind.Drew, state.LastEvents[0].Kind);
            Assert.AreEqual(3, state.LastEvents[0].Count);
            Assert.AreEqual(PrismEvent.Hidden, module.ViewFor(state, 0).LastEvents[0].Card);
            Assert.AreEqual(3, module.ViewFor(state, 0).LastEvents[0].Count);
            Assert.AreEqual(84, TotalCards(state));

            state = Play(state, 1, PrismMove.Keep());
            Assert.AreEqual(2, state.CurrentSeat);
            Assert.AreEqual(10, state.HandCount(1));
        }

        [Test]
        public void EmptyingYourHandWins()
        {
            // Seat 0 holds the other seven 3s; a Cyan 3 starts, so one chain plays them all.
            int[] threes = { Num(Cyan, 3, 1), Num(Gold, 3), Num(Gold, 3, 1), Num(Rose, 3), Num(Rose, 3, 1), Num(Lime, 3), Num(Lime, 3, 1) };
            int[] other = { 10, 11, 12, 13, 14, 15, 18 };
            var state = module.SetupWithDeck(2, DeckFor(new[] { threes, other }, Num(Cyan, 3)), 0);
            state = Play(state, 0, PrismMove.Play(threes));

            Assert.IsTrue(state.IsOver);
            CollectionAssert.AreEqual(new[] { 0 }, state.Winners);
            CollectionAssert.AreEqual(new[] { 0 }, module.Result(state).WinningSeats);
            Assert.AreEqual(ErrorCodes.GameOver, module.Validate(state, 1, PrismMove.Draw()).Code);
        }

        [Test]
        public void ViewsShowOnlyYourOwnHand()
        {
            var state = FourPlayers();
            var view = module.ViewFor(state, 2);
            CollectionAssert.AreEqual(state.HandOf(2), view.MyHand);
            Assert.AreEqual(7, view.HandCounts[0]);
            Assert.AreEqual(55, view.DrawPileCount);
            Assert.AreEqual(Num(Cyan, 5), view.TopCard);
            Assert.IsFalse(view.IsMyTurn);
        }

        [Test]
        public void TimeoutMoveIsAlwaysLegal()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var state = module.Setup(new MatchConfig(4, seed));
                Assert.IsTrue(module.TryGetTimeoutAction(state, state.CurrentSeat, out var move));
                Assert.IsTrue(module.Validate(state, state.CurrentSeat, move).Ok);
            }
        }

        [Test]
        public void SetupRejectsABadDeckAndBadPlayerCounts()
        {
            Assert.Throws<ArgumentException>(() => module.SetupWithDeck(2, new int[PrismCards.DeckSize], 0));
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(5, 1)));
        }

        [Test]
        public void RematchDealsAgainAndTheNextPlayerStarts()
        {
            var options = new Dictionary<string, string> { { "firstSeat", "3" } };
            var first = module.Setup(new MatchConfig(4, 9, options));
            var rematch = module.Rematch(first, 12);
            Assert.AreEqual(0, rematch.FirstSeat);
            Assert.AreEqual(84, TotalCards(rematch));
        }
    }

    /// <summary>Whole games played by bots, checking the rules hold the whole way through.</summary>
    public class PrismRushPlayoutTests
    {
        [Test]
        public void BotGamesAlwaysFinish([Values(2, 3, 4)] int players)
        {
            var module = new PrismModule();
            for (ulong seed = 1; seed <= 150; seed++)
            {
                var state = module.Setup(new MatchConfig(players, seed));
                int moves = 0;
                while (!state.IsOver)
                {
                    Assert.Less(moves++, 3000, $"Seed {seed} did not finish.");
                    int seat = state.CurrentSeat;
                    var move = PrismBot.ChooseMove(module.ViewFor(state, seat));
                    var check = module.Validate(state, seat, move);
                    Assert.IsTrue(check.Ok, $"Seed {seed}: bot move {move} was illegal ({check.Code}).");
                    state = module.Apply(state, seat, move);

                    int total = state.DrawPile.Count + state.Discard.Count;
                    for (int s = 0; s < players; s++) total += state.HandCount(s);
                    Assert.AreEqual(PrismCards.DeckSize, total, $"Seed {seed}: a card went missing.");
                }
                Assert.Greater(state.Winners.Count, 0);
                Assert.IsTrue(module.Result(state).IsOver);
            }
        }
    }
}
