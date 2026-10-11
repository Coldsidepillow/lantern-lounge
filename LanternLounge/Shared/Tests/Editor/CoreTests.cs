using System;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void MatchesTheSplitMix64ReferenceSequence()
        {
            // These values are what any correct SplitMix64 produces. If Unity's runtime and the
            // .NET server ever disagree here, shuffles would diverge between client and server.
            var zero = new SeededRandom(0);
            Assert.AreEqual(0xe220a8397b1dcdafUL, zero.NextULong());
            Assert.AreEqual(0x6e789e6aa1b965f4UL, zero.NextULong());
            Assert.AreEqual(0x06c45d188009454fUL, zero.NextULong());

            var fortyTwo = new SeededRandom(42);
            Assert.AreEqual(0xbdd732262feb6e95UL, fortyTwo.NextULong());
        }

        [Test]
        public void SameSeedGivesSameIntegers()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            int[] expected = { 0, 5, 1, 5, 0 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], a.NextInt(6));
                Assert.AreEqual(expected[i], b.NextInt(6));
            }
        }

        [Test]
        public void NextIntStaysInRange()
        {
            var random = new SeededRandom(123);
            for (int i = 0; i < 10000; i++)
            {
                int value = random.NextInt(7);
                Assert.IsTrue(value >= 0 && value < 7);
            }
        }

        [Test]
        public void ShuffleKeepsEveryItem()
        {
            var items = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            new SeededRandom(9).Shuffle(items);
            Array.Sort(items);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, items);
        }

        [Test]
        public void StateCanBeSavedAndResumed()
        {
            var original = new SeededRandom(5);
            original.NextULong();
            var resumed = new SeededRandom(0) { State = original.State };
            Assert.AreEqual(original.NextULong(), resumed.NextULong());
        }
    }

    public class GameRegistryTests
    {
        [Test]
        public void ListsGamesInTheOrderTheyWereAdded()
        {
            var registry = new GameRegistry();
            registry.Register(new PebbleDropModule());
            registry.Announce(new GameInfo("future-game", "Future Game", 2, 2, GameStatus.ComingSoon, "future-game"));

            Assert.AreEqual(2, registry.All.Count);
            Assert.AreEqual("pebble-drop", registry.All[0].Id);
            Assert.AreEqual("future-game", registry.All[1].Id);
        }

        [Test]
        public void OnlyRegisteredGamesHaveModules()
        {
            var registry = new GameRegistry();
            registry.Register(new PebbleDropModule());
            registry.Announce(new GameInfo("prism-rush", "Prism Rush", 2, 4, GameStatus.ComingSoon, "prism-rush"));

            Assert.IsTrue(registry.TryGetModule("pebble-drop", out var module));
            Assert.AreEqual("Pebble Drop", module.Info.DisplayName);
            Assert.IsFalse(registry.TryGetModule("prism-rush", out _));
            Assert.IsTrue(registry.TryGetInfo("prism-rush", out var info));
            Assert.AreEqual(4, info.MaxPlayers);
        }

        [Test]
        public void RejectsDuplicateIds()
        {
            var registry = new GameRegistry();
            registry.Register(new PebbleDropModule());
            Assert.Throws<InvalidOperationException>(() => registry.Register(new PebbleDropModule()));
        }

        [Test]
        public void AnnouncingAPlayableGameWithoutAModuleIsRejected()
        {
            var registry = new GameRegistry();
            var playable = new GameInfo("checkers", "Checkers", 2, 2, GameStatus.Playable, "checkers");
            Assert.Throws<ArgumentException>(() => registry.Announce(playable));
        }

        [Test]
        public void UntypedBridgeRejectsAnotherGamesMoveWithoutThrowing()
        {
            IGameModule module = new PebbleDropModule();
            object state = module.Setup(new MatchConfig(4, 1));
            var check = module.Validate(state, 0, "not a pebble move");
            Assert.IsFalse(check.Ok);
            Assert.AreEqual(ErrorCodes.InvalidAction, check.Code);
        }
    }
}
