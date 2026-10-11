using System;
using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.Tidebound;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class TideboundTests
    {
        private TideboundModule module;

        [SetUp]
        public void SetUp()
        {
            module = new TideboundModule();
        }

        /// <summary>
        /// Builds a deck that deals exactly these hands (round robin from seat 0), then puts
        /// <paramref name="currentTop"/> on top of the Current, then every other card in order.
        /// </summary>
        private static int[] DeckFor(int[][] hands, params int[] currentTop)
        {
            var deck = new List<int>();
            for (int round = 0; round < hands[0].Length; round++)
            {
                for (int seat = 0; seat < hands.Length; seat++) deck.Add(hands[seat][round]);
            }
            deck.AddRange(currentTop);
            for (int card = 0; card < TideboundCards.DeckSize; card++)
            {
                if (!deck.Contains(card)) deck.Add(card);
            }
            return deck.ToArray();
        }

        private static int Card(int creature, Tide tide) => TideboundCards.CardId(creature, tide);

        private static int TotalCards(TideboundState state)
        {
            int total = state.CurrentCount;
            for (int s = 0; s < state.PlayerCount; s++)
            {
                total += state.HandCount(s) + state.PodsOf(s).Count * TideboundCards.PodSize;
            }
            return total;
        }

        // Seat 0 holds one each of creatures 0-6; seat 1 holds one each of creatures 0-5 and 7.
        private static readonly int[][] TwoDivers =
        {
            new[] { 0, 4, 8, 12, 16, 20, 24 },
            new[] { 1, 5, 9, 13, 17, 21, 29 }
        };

        [Test]
        public void DeckHasSixteenCreaturesOfFourTidesMakingThirtyTwoPods()
        {
            Assert.AreEqual(16, TideboundCards.Creatures.Count);
            Assert.AreEqual(64, TideboundCards.DeckSize);
            Assert.AreEqual(2, TideboundCards.PodSize);
            Assert.AreEqual(32, TideboundCards.PodCount);
            for (int i = 0; i < TideboundCards.CreatureCount; i++) Assert.AreEqual(i, TideboundCards.Creature(i).Id);
            Assert.AreEqual(Tide.Night, TideboundCards.TideOf(Card(3, Tide.Night)));
            Assert.AreEqual(3, TideboundCards.CreatureOf(Card(3, Tide.Night)));
            Assert.AreEqual("Glimmerjelly", TideboundCards.Creature(2).Name);
        }

        [Test]
        public void DealsSevenCardsToEveryDiver()
        {
            var ordered = new int[TideboundCards.DeckSize];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = i;

            AssertDealt(module.SetupWithDeck(2, ordered, 0), 7, 50);
            AssertDealt(module.SetupWithDeck(3, ordered, 0), 7, 43);
            AssertDealt(module.SetupWithDeck(4, ordered, 0), 7, 36);
        }

        private static void AssertDealt(TideboundState state, int handSize, int current)
        {
            Assert.AreEqual(current, state.CurrentCount);
            for (int s = 0; s < state.PlayerCount; s++)
            {
                // Pairs dealt together become pods straight away.
                Assert.AreEqual(handSize, state.HandCount(s) + state.PodsOf(s).Count * TideboundCards.PodSize);
            }
        }

        [Test]
        public void RandomDealKeepsEveryCardAndNoHandHoldsAnUnpairedDuplicate()
        {
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var state = module.Setup(new MatchConfig(4, seed));
                Assert.AreEqual(TideboundCards.DeckSize, TotalCards(state));
                for (int s = 0; s < 4; s++) AssertNoDuplicates(state.HandOf(s));
            }
        }

        private static void AssertNoDuplicates(IReadOnlyList<int> hand)
        {
            for (int i = 1; i < hand.Count; i++)
            {
                Assert.IsTrue(hand[i - 1] < hand[i], "Hands stay sorted.");
                Assert.AreNotEqual(TideboundCards.CreatureOf(hand[i - 1]), TideboundCards.CreatureOf(hand[i]),
                    "Two of the same creature should already be a pod.");
            }
        }

        [Test]
        public void ThreeOfACreatureMakeOnePodWithOneLeftAndFourMakeTwoPods()
        {
            var three = module.SetupWithDeck(2, DeckFor(new[]
            {
                new[] { 0, 1, 2, 8, 12, 16, 20 },
                new[] { 4, 9, 13, 17, 21, 25, 29 }
            }), 0);
            CollectionAssert.AreEqual(new[] { 0 }, three.PodsOf(0));
            CollectionAssert.AreEqual(new[] { 2, 8, 12, 16, 20 }, three.HandOf(0));

            var four = module.SetupWithDeck(2, DeckFor(new[]
            {
                new[] { 0, 1, 2, 3, 12, 16, 20 },
                new[] { 4, 9, 13, 17, 21, 25, 29 }
            }), 0);
            CollectionAssert.AreEqual(new[] { 0, 0 }, four.PodsOf(0));
            Assert.AreEqual(3, four.HandCount(0));
        }

        [Test]
        public void GettingTheMatchingCardMakesAPodAndKeepsYourTide()
        {
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers), 0);
            state = module.Apply(state, 0, new AskAction(1, 0));

            CollectionAssert.AreEqual(new[] { 0 }, state.PodsOf(0));
            Assert.AreEqual(0, state.CountInHand(0, 0));
            Assert.AreEqual(6, state.HandCount(0));
            Assert.AreEqual(6, state.HandCount(1));
            Assert.AreEqual(0, state.CurrentSeat);
            Assert.AreEqual(TideEventKind.Asked, state.LastEvents[0].Kind);
            Assert.AreEqual(1, state.LastEvents[0].Count);
            Assert.AreEqual(TideEventKind.PodFormed, state.LastEvents[1].Kind);
        }

        [Test]
        public void ADiveDrawsOneCardAndAMissPassesTheTide()
        {
            // The Current starts with 36 (Kelpwhisk), not the Lanternmoss seat 0 asks for.
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers, 36, 37, 26), 0);
            state = module.Apply(state, 0, new AskAction(1, 6));

            Assert.AreEqual(1, state.CurrentSeat, "A miss passes the tide.");
            Assert.AreEqual(8, state.HandCount(0));
            Assert.AreEqual(1, state.CountInHand(0, 9));
            Assert.AreEqual(0, state.PodsOf(0).Count);
            Assert.AreEqual(49, state.CurrentCount, "Only one card is drawn.");
            Assert.AreEqual(2, state.LastEvents.Count);
            Assert.AreEqual(TideEventKind.Dove, state.LastEvents[1].Kind);
            Assert.AreEqual(1, state.LastEvents[1].Count);
        }

        [Test]
        public void DrawingTheAskedCreatureIsShownToEveryoneAndYouGoAgain()
        {
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers, 25), 0);
            state = module.Apply(state, 0, new AskAction(1, 6));

            Assert.AreEqual(0, state.CurrentSeat);
            CollectionAssert.AreEqual(new[] { 6 }, state.PodsOf(0));
            Assert.AreEqual(49, state.CurrentCount);
            Assert.AreEqual(TideEventKind.LuckyDive, state.LastEvents[1].Kind);
            Assert.AreEqual(25, module.ViewFor(state, 1).LastEvents[1].Card);
        }

        [Test]
        public void ADrawThatMakesADifferentPodStillPassesTheTide()
        {
            // Card 3 is a Brinekin, which pairs with seat 0's Brinekin, but seat 0 asked for Lanternmoss.
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers, 3), 0);
            state = module.Apply(state, 0, new AskAction(1, 6));

            CollectionAssert.AreEqual(new[] { 0 }, state.PodsOf(0));
            Assert.AreEqual(6, state.HandCount(0));
            Assert.AreEqual(1, state.CurrentSeat);
            Assert.AreEqual(TideEventKind.PodFormed, state.LastEvents[2].Kind);
        }

        [Test]
        public void IllegalAsksAreRefused()
        {
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers), 0);
            Assert.AreEqual(ErrorCodes.NotYourTurn, module.Validate(state, 1, new AskAction(0, 0)).Code);
            Assert.AreEqual(TideboundModule.BadTarget, module.Validate(state, 0, new AskAction(0, 0)).Code);
            Assert.AreEqual(TideboundModule.BadTarget, module.Validate(state, 0, new AskAction(5, 0)).Code);
            Assert.AreEqual(TideboundModule.MustHoldCreature, module.Validate(state, 0, new AskAction(1, 9)).Code);
            Assert.AreEqual(ErrorCodes.InvalidAction, module.Validate(state, 0, new AskAction(1, 16)).Code);
            Assert.IsTrue(module.Validate(state, 0, new AskAction(1, 0)).Ok);
        }

        [Test]
        public void EmptyingYourHandMeansYouDrawOne_AndNobodyCanAskAnEmptyHand()
        {
            // Seat 0 and seat 2 hold the same seven creatures, so seat 0 pairs everything with seat 2.
            var hands = new[]
            {
                new[] { 0, 4, 8, 12, 16, 20, 24 },
                new[] { 28, 32, 36, 40, 44, 48, 52 },
                new[] { 1, 5, 9, 13, 17, 21, 25 }
            };
            var state = module.SetupWithDeck(3, DeckFor(hands, 29), 0);
            for (int creature = 0; creature < 7; creature++)
            {
                state = module.Apply(state, 0, new AskAction(2, creature));
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 }, state.PodsOf(0));
            Assert.AreEqual(0, state.HandCount(2));
            CollectionAssert.AreEqual(new[] { 29 }, state.HandOf(0), "An empty hand draws one card.");
            Assert.AreEqual(0, state.CurrentSeat);

            var last = state.LastEvents[state.LastEvents.Count - 1];
            Assert.AreEqual(TideEventKind.Refilled, last.Kind);
            Assert.AreEqual(0, last.Seat);
            Assert.AreEqual(TideEvent.Hidden, module.ViewFor(state, 1).LastEvents[state.LastEvents.Count - 1].Card);

            Assert.AreEqual(TideboundModule.TargetEmpty, module.Validate(state, 0, new AskAction(2, 7)).Code);
            Assert.IsTrue(module.Validate(state, 0, new AskAction(1, 7)).Ok);
        }

        [Test]
        public void ViewsHideOtherHandsAndWhichCardWasDrawn()
        {
            var state = module.SetupWithDeck(2, DeckFor(TwoDivers, 36), 0);
            state = module.Apply(state, 0, new AskAction(1, 6));

            var diver = module.ViewFor(state, 0);
            var other = module.ViewFor(state, 1);

            CollectionAssert.AreEqual(state.HandOf(1), other.MyHand);
            CollectionAssert.AreEqual(state.HandOf(0), diver.MyHand);
            CollectionAssert.Contains(diver.MyHand, 36, "The diver sees the drawn card in their own hand.");
            Assert.AreEqual(8, other.HandCounts[0]);
            Assert.AreEqual(49, other.CurrentCount);

            // Everyone learns a card was drawn, but never which one.
            Assert.AreEqual(1, other.LastEvents[1].Count);
            Assert.AreEqual(TideEvent.Hidden, other.LastEvents[1].Card);
            Assert.AreEqual(-1, other.LastEvents[1].Creature, "The drawn creature must stay hidden.");

            Assert.IsFalse(diver.IsMyTide);
            Assert.IsTrue(other.IsMyTide);
        }

        [Test]
        public void ApplyNeverChangesThePreviousState()
        {
            var before = module.SetupWithDeck(2, DeckFor(TwoDivers), 0);
            module.Apply(before, 0, new AskAction(1, 0));
            Assert.AreEqual(7, before.HandCount(1));
            Assert.AreEqual(1, before.CountInHand(0, 0));
            Assert.AreEqual(0, before.PodsOf(0).Count);
        }

        [Test]
        public void SetupRejectsABadDeckAndBadPlayerCounts()
        {
            var duplicate = new int[TideboundCards.DeckSize];
            Assert.Throws<ArgumentException>(() => module.SetupWithDeck(2, duplicate, 0));
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(5, 1)));
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(1, 1)));
        }

        [Test]
        public void TimeoutAskIsAlwaysLegal()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var state = module.Setup(new MatchConfig(4, seed));
                Assert.IsTrue(module.TryGetTimeoutAction(state, state.CurrentSeat, out var ask));
                Assert.IsTrue(module.Validate(state, state.CurrentSeat, ask).Ok);
            }
        }

        [Test]
        public void RematchDealsAFreshTableAndTheNextDiverStarts()
        {
            var options = new Dictionary<string, string> { { "firstSeat", "2" } };
            var first = module.Setup(new MatchConfig(4, 5, options));
            var rematch = module.Rematch(first, 99);
            Assert.AreEqual(3, rematch.FirstSeat);
            Assert.AreEqual(0, rematch.Turn);
            Assert.AreEqual(TideboundCards.DeckSize, TotalCards(rematch));
        }
    }

    /// <summary>Whole games played by Reef Bots, checking the rules never break along the way.</summary>
    public class TideboundPlayoutTests
    {
        private const int MaxAsks = 1000;

        [Test]
        public void BotGamesAlwaysFinishWithEveryPodClaimed([Values(2, 3, 4)] int players)
        {
            var module = new TideboundModule();
            for (ulong seed = 1; seed <= 150; seed++)
            {
                var state = module.Setup(new MatchConfig(players, seed));
                var bots = new TideboundBot[players];
                for (int s = 0; s < players; s++)
                {
                    bots[s] = new TideboundBot(s, players, new SeededRandom(seed * 31 + (ulong)s));
                    bots[s].Observe(module.ViewFor(state, s));
                }

                int asks = 0;
                while (!state.IsOver)
                {
                    Assert.Less(asks++, MaxAsks, $"Game with seed {seed} did not finish.");
                    int seat = state.CurrentSeat;
                    Assert.Greater(state.HandCount(seat), 0, "The diver on tide must hold cards.");

                    var ask = bots[seat].ChooseAsk(module.ViewFor(state, seat));
                    var check = module.Validate(state, seat, ask);
                    Assert.IsTrue(check.Ok, $"Seed {seed}: bot made an illegal ask ({check.Code}).");

                    state = module.Apply(state, seat, ask);
                    for (int s = 0; s < players; s++) bots[s].Observe(module.ViewFor(state, s));

                    int total = state.CurrentCount;
                    for (int s = 0; s < players; s++)
                    {
                        total += state.HandCount(s) + TideboundCards.PodSize * state.PodsOf(s).Count;
                        var hand = state.HandOf(s);
                        for (int i = 1; i < hand.Count; i++)
                        {
                            Assert.AreNotEqual(TideboundCards.CreatureOf(hand[i - 1]), TideboundCards.CreatureOf(hand[i]),
                                $"Seed {seed}: an unpaired duplicate was left in a hand.");
                        }
                    }
                    Assert.AreEqual(TideboundCards.DeckSize, total, $"Seed {seed}: a card went missing.");
                }

                Assert.AreEqual(TideboundCards.PodCount, state.TotalPods);
                Assert.IsTrue(module.Result(state).IsOver);
                Assert.Greater(module.ViewFor(state, 0).Winners.Count, 0);
            }
        }
    }
}
