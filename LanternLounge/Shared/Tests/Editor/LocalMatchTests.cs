using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class LocalMatchTests
    {
        private static LocalMatch<PebbleDropState, DropAction, PebbleDropState> NewMatch(out PebbleDropModule module)
        {
            module = new PebbleDropModule();
            var options = new Dictionary<string, string> { { "firstSeat", "0" } };
            return new LocalMatch<PebbleDropState, DropAction, PebbleDropState>(module, new MatchConfig(4, 1, options));
        }

        [Test]
        public void LegalMoveUpdatesStateAndHistory()
        {
            var match = NewMatch(out _);
            var check = match.TryPlay(0, new DropAction(3));
            Assert.IsTrue(check.Ok);
            Assert.AreEqual(1, match.State.MoveCount);
            Assert.AreEqual(1, match.History.Count);
            Assert.AreEqual(3, match.History[0].Action.Column);
            Assert.IsTrue(match.CanAct(1));
            Assert.IsFalse(match.CanAct(0));
        }

        [Test]
        public void IllegalMoveIsRefusedAndChangesNothing()
        {
            var match = NewMatch(out _);
            var before = match.State;
            var check = match.TryPlay(1, new DropAction(3));
            Assert.IsFalse(check.Ok);
            Assert.AreEqual(ErrorCodes.NotYourTurn, check.Code);
            Assert.AreSame(before, match.State);
            Assert.AreEqual(0, match.History.Count);
        }

        [Test]
        public void ChangedFiresOnlyForAcceptedMoves()
        {
            var match = NewMatch(out _);
            int raised = 0;
            match.Changed += _ => raised++;
            match.TryPlay(1, new DropAction(0));
            match.TryPlay(0, new DropAction(0));
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void ReportsTheEndOfTheMatch()
        {
            var match = NewMatch(out _);
            int[] columns = { 0, 15, 15, 15, 1, 14, 14, 14, 2, 13, 13, 13, 3 };
            foreach (int column in columns)
            {
                Assert.IsTrue(match.TryPlay(match.State.CurrentSeat, new DropAction(column)).Ok);
            }
            Assert.IsTrue(match.IsOver);
            Assert.AreEqual(0, match.SeatsToAct.Count);
            CollectionAssert.AreEqual(new[] { 0 }, match.Result.WinningSeats);
        }

        [Test]
        public void ResetStartsARematchAndClearsHistory()
        {
            var match = NewMatch(out var module);
            match.TryPlay(0, new DropAction(2));
            match.Reset(module.Rematch(match.State));
            Assert.AreEqual(0, match.History.Count);
            Assert.AreEqual(0, match.State.MoveCount);
            Assert.AreEqual(1, match.State.CurrentSeat);
        }
    }
}
