using System;
using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class PebbleDropTests
    {
        private const int Red = 0, Yellow = 1, Blue = 2, Green = 3;
        private static readonly Dictionary<string, string> RedFirst = new Dictionary<string, string> { { "firstSeat", "0" } };

        private PebbleDropModule module;

        [SetUp]
        public void SetUp()
        {
            module = new PebbleDropModule();
        }

        private PebbleDropState NewGame() => module.Setup(new MatchConfig(4, 1, RedFirst));

        /// <summary>Plays columns in turn order: Red, Yellow, Blue, Green, Red, ...</summary>
        private PebbleDropState Play(params int[] columns)
        {
            var state = NewGame();
            foreach (int column in columns)
            {
                state = module.Apply(state, state.CurrentSeat, new DropAction(column));
            }
            return state;
        }

        [Test]
        public void BoardIsSixteenByTwelveForFourPlayers()
        {
            Assert.AreEqual(16, PebbleDropState.Columns);
            Assert.AreEqual(12, PebbleDropState.Rows);
            Assert.AreEqual(4, module.Info.MinPlayers);
            Assert.AreEqual(4, module.Info.MaxPlayers);
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(2, 1)));
        }

        [Test]
        public void TurnsGoRedYellowBlueGreen()
        {
            var state = NewGame();
            int[] expected = { Red, Yellow, Blue, Green, Red };
            foreach (int seat in expected)
            {
                Assert.AreEqual(seat, state.CurrentSeat);
                state = module.Apply(state, seat, new DropAction(seat * 3));
            }
        }

        [Test]
        public void PebblesLandOnTheLowestEmptyRow()
        {
            var state = Play(5, 5);
            Assert.AreEqual(Red, state.CellAt(11, 5));
            Assert.AreEqual(Yellow, state.CellAt(10, 5));
            Assert.AreEqual(9, state.LandingRow(5));
        }

        [Test]
        public void HorizontalLineWins()
        {
            var state = Play(0, 15, 15, 15, 1, 14, 14, 14, 2, 13, 13, 13, 3);
            Assert.AreEqual(Red, state.Winner);
            Assert.AreEqual(4, state.WinningCells.Count);
            Assert.IsTrue(module.Result(state).IsOver);
            Assert.AreEqual(PebbleDropState.NoSeat, state.CurrentSeat);
        }

        [Test]
        public void VerticalLineWins()
        {
            Assert.AreEqual(Red, Play(0, 15, 14, 13, 0, 15, 14, 13, 0, 15, 14, 13, 0).Winner);
        }

        [Test]
        public void RisingSlantWins()
        {
            var state = Play(0, 1, 2, 3, 1, 2, 3, 9, 2, 3, 10, 11, 3);
            Assert.AreEqual(Red, state.Winner);
            CollectionAssert.AreEquivalent(
                new[] { PebbleDropState.Index(11, 0), PebbleDropState.Index(10, 1), PebbleDropState.Index(9, 2), PebbleDropState.Index(8, 3) },
                state.WinningCells);
        }

        [Test]
        public void FallingSlantWins()
        {
            Assert.AreEqual(Red, Play(15, 14, 13, 12, 14, 13, 12, 6, 13, 12, 5, 4, 12).Winner);
        }

        [Test]
        public void GreenCanWin()
        {
            var state = Play(15, 14, 13, 0, 15, 14, 13, 1, 15, 12, 12, 2, 11, 10, 9, 3);
            Assert.AreEqual(Green, state.Winner);
            CollectionAssert.AreEqual(new[] { Green }, module.Result(state).WinningSeats);
        }

        [Test]
        public void FullBoardWithNoLineIsADraw()
        {
            // Fill row by row, snaking left-right then right-left, so no colour ever lines up four.
            var columns = new List<int>();
            for (int row = 0; row < PebbleDropState.Rows; row++)
            {
                for (int i = 0; i < PebbleDropState.Columns; i++)
                {
                    columns.Add(row % 2 == 0 ? i : PebbleDropState.Columns - 1 - i);
                }
            }
            var state = Play(columns.ToArray());
            Assert.IsTrue(state.IsDraw);
            Assert.AreEqual(PebbleDropState.NoSeat, state.Winner);
            Assert.AreEqual(PebbleDropState.CellCount, state.MoveCount);
            Assert.IsTrue(module.Result(state).IsDraw);
        }

        [Test]
        public void FullColumnIsRefused()
        {
            var state = NewGame();
            for (int i = 0; i < PebbleDropState.Rows; i++) state = module.Apply(state, state.CurrentSeat, new DropAction(7));
            Assert.IsTrue(state.IsColumnFull(7));
            Assert.AreEqual(PebbleDropModule.ColumnFull, module.Validate(state, state.CurrentSeat, new DropAction(7)).Code);
            Assert.AreEqual(PebbleDropModule.ColumnOutOfRange, module.Validate(state, state.CurrentSeat, new DropAction(16)).Code);
        }

        [Test]
        public void WrongSeatAndFinishedGamesAreRefused()
        {
            var state = NewGame();
            Assert.AreEqual(ErrorCodes.NotYourTurn, module.Validate(state, Blue, new DropAction(0)).Code);
            Assert.AreEqual(ErrorCodes.BadSeat, module.Validate(state, 4, new DropAction(0)).Code);

            var over = Play(0, 15, 14, 13, 0, 15, 14, 13, 0, 15, 14, 13, 0);
            Assert.AreEqual(ErrorCodes.GameOver, module.Validate(over, Yellow, new DropAction(5)).Code);
        }

        [Test]
        public void RematchMovesTheFirstPlayerOnOneSeat()
        {
            var state = Play(3);
            var next = module.Rematch(state);
            Assert.AreEqual(Yellow, next.CurrentSeat);
            Assert.AreEqual(0, next.MoveCount);
            Assert.AreEqual(Blue, module.Rematch(next).CurrentSeat);
            Assert.AreEqual(Red, module.NewBoard(Red).CurrentSeat);
        }

        [Test]
        public void TimeoutDropsInTheCentre()
        {
            Assert.IsTrue(module.TryGetTimeoutAction(NewGame(), Red, out var action));
            Assert.AreEqual(8, action.Column);
        }

        [Test]
        public void BotTakesAWin()
        {
            var state = Play(0, 15, 15, 15, 1, 14, 14, 14, 2, 13, 13, 13);
            var move = PebbleDropBot.ChooseMove(state, Red, new SeededRandom(1), BotLevel.Normal);
            Assert.AreEqual(3, move.Column);
        }

        [Test]
        public void BotBlocksTheNextPlayersWin()
        {
            // Yellow has three in a row along the bottom (columns 4-6); it's Red's turn.
            var state = Play(15, 4, 14, 13, 15, 5, 14, 13, 11, 6, 10, 9);
            Assert.AreEqual(Red, state.CurrentSeat);
            var move = PebbleDropBot.ChooseMove(state, Red, new SeededRandom(1), BotLevel.Normal);
            Assert.That(move.Column == 3 || move.Column == 7, $"Expected a block at 3 or 7, got {move.Column}.");
        }

        [Test]
        public void BotGamesAlwaysEnd()
        {
            for (int game = 0; game < 4; game++)
            {
                var random = new SeededRandom((ulong)(game + 1));
                var state = module.Setup(new MatchConfig(4, (ulong)game));
                int guard = 0;
                while (!state.IsOver && guard++ < PebbleDropState.CellCount + 1)
                {
                    var level = game % 2 == 0 ? BotLevel.Normal : BotLevel.Easy;
                    var move = PebbleDropBot.ChooseMove(state, state.CurrentSeat, random, level);
                    state = module.Apply(state, state.CurrentSeat, move);
                }
                Assert.IsTrue(state.IsOver);
            }
        }
    }
}
