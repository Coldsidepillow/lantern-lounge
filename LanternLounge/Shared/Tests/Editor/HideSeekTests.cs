using System;
using System.Collections.Generic;
using LanternLounge.Core;
using LanternLounge.Games.HideSeek;
using NUnit.Framework;

namespace LanternLounge.Tests
{
    public class HideSeekTests
    {
        private HideSeekModule module;

        [SetUp]
        public void SetUp()
        {
            module = new HideSeekModule();
        }

        private static HideInput Still(int seq) => new HideInput(0, 0, seq);

        private HideSeekState Tick(HideSeekState state, params HideInput[] inputs)
        {
            var padded = new HideInput[HideSeekRules.Players];
            for (int i = 0; i < inputs.Length && i < padded.Length; i++) padded[i] = inputs[i];
            var tick = new HideSeekTick(HideSeekRules.TickMs, padded);
            var check = module.Validate(state, 0, tick);
            Assert.IsTrue(check.Ok, check.Code);
            return module.Apply(state, 0, tick);
        }

        private static int W => HideSeekRules.Width;
        private static int H => HideSeekRules.Height;

        // ---------------- The maze ----------------

        [Test]
        public void EveryMazeIsFullyConnectedAndHasRoomsSecretsAndHidingPlaces()
        {
            for (ulong seed = 1; seed <= 150; seed++)
            {
                var map = HideSeekMap.Generate(seed);
                Assert.AreEqual(3, map.SecretDoors.Count, $"Seed {seed}: three secret rooms.");
                Assert.GreaterOrEqual(map.Spots.Count, 10, $"Seed {seed}: hiding places.");
                Assert.GreaterOrEqual(map.DeadEnds, 3, $"Seed {seed}: dead ends.");

                int start = HideSeekMap.Index(1, 1);
                var withoutSecrets = HideSeekPaths.Distances(map, start, null, false);
                var withSecrets = HideSeekPaths.Distances(map, start, null, true);
                for (int i = 0; i < W * H; i++)
                {
                    if (map.TileAtIndex(i) != HideSeekMap.Wall) Assert.GreaterOrEqual(withSecrets[i], 0, $"Seed {seed}: tile {i} is cut off.");
                }
                foreach (var spot in map.Spots)
                {
                    int d = withoutSecrets[spot.Y * W + spot.X];
                    if (spot.Secret) Assert.Less(d, 0, $"Seed {seed}: a secret room spot is reachable without its door.");
                    else Assert.GreaterOrEqual(d, 0, $"Seed {seed}: a hallway spot is unreachable.");
                }
                foreach (var s in map.HiderStarts) Assert.GreaterOrEqual(withoutSecrets[s.Y * W + s.X], 0);

                for (int x = 0; x < W; x++)
                {
                    Assert.AreEqual(HideSeekMap.Wall, map.TileAt(x, 0));
                    Assert.AreEqual(HideSeekMap.Wall, map.TileAt(x, H - 1));
                }
                for (int y = 0; y < H; y++)
                {
                    Assert.AreEqual(HideSeekMap.Wall, map.TileAt(0, y));
                    Assert.AreEqual(HideSeekMap.Wall, map.TileAt(W - 1, y));
                }
            }
        }

        [Test]
        public void TheSameSeedBuildsTheSameMaze()
        {
            var a = HideSeekMap.Generate(42);
            var b = HideSeekMap.Generate(42);
            for (int i = 0; i < W * H; i++) Assert.AreEqual(a.TileAtIndex(i), b.TileAtIndex(i));
            var c = HideSeekMap.Generate(43);
            bool differs = false;
            for (int i = 0; i < W * H; i++)
            {
                if (a.TileAtIndex(i) != c.TileAtIndex(i)) differs = true;
            }
            Assert.IsTrue(differs);
        }

        // ---------------- Setup ----------------

        [Test]
        public void SetupPicksTheSeekerAndStartsEveryoneOnAFloorTile()
        {
            var options = new Dictionary<string, string> { { "seeker", "2" } };
            var state = module.Setup(new MatchConfig(4, 5, options));
            Assert.AreEqual(2, state.Seeker);
            Assert.AreEqual(HidePhase.Hiding, state.Phase);
            Assert.AreEqual(3, state.HidersLeft);
            Assert.IsFalse(state.IsOver);
            for (int s = 0; s < 4; s++)
            {
                var p = state.Players[s];
                Assert.AreNotEqual(HideSeekMap.Wall, state.Map.TileAt((int)Math.Floor(p.X), (int)Math.Floor(p.Y)));
            }
            Assert.AreEqual(1.5, state.Players[2].X, 0.0001, "The seeker starts in the top-left corner.");
            Assert.AreEqual(1.5, state.Players[2].Y, 0.0001);
            Assert.IsFalse(module.Result(state).IsOver);
            Assert.AreEqual(4, module.SeatsToAct(state).Count);
        }

        [Test]
        public void SetupRejectsTheWrongNumberOfPlayers()
        {
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(3, 1)));
            Assert.Throws<ArgumentException>(() => module.Setup(new MatchConfig(5, 1)));
        }

        [Test]
        public void RematchMovesTheSeekerOnAndBuildsANewMaze()
        {
            var first = HideSeekModule.Create(1, 3);
            var again = module.Rematch(first, 2);
            Assert.AreEqual(0, again.Seeker);
            Assert.AreEqual(0, again.ElapsedMs);
        }

        // ---------------- Moving ----------------

        [Test]
        public void NobodyEverWalksIntoAWall()
        {
            var state = HideSeekModule.Create(5, 0);
            for (int t = 0; t < 400; t++)
            {
                state = Tick(state, null, new HideInput(100, 37, 0), new HideInput(-80, 100, 0), new HideInput(30, -100, 0));
                for (int s = 1; s < 4; s++)
                {
                    var p = state.Players[s];
                    Assert.IsFalse(state.Map.IsWall((int)Math.Floor(p.X), (int)Math.Floor(p.Y)), "A player ended up inside a wall.");
                }
            }
        }

        [Test]
        public void TheSeekerCannotMoveWhileTheHidersHide()
        {
            var state = HideSeekModule.Create(5, 0);
            var after = Tick(state, new HideInput(100, 0, 0));
            Assert.AreEqual(state.Players[0].X, after.Players[0].X);
            Assert.AreEqual(state.Players[0].Y, after.Players[0].Y);
        }

        [Test]
        public void ASmallThumbWiggleDoesNotMove()
        {
            var state = HideSeekModule.Create(5, 0);
            var after = Tick(state, null, new HideInput(5, 5, 0));
            Assert.AreEqual(state.Players[1].X, after.Players[1].X);
        }

        // ---------------- Hiding, grabbing, winning ----------------

        private static HideSpot AnOpenSpot(HideSeekState state)
        {
            foreach (var s in state.Map.Spots)
            {
                if (!s.Secret) return s;
            }
            throw new InvalidOperationException("No hallway hiding place.");
        }

        [Test]
        public void AHiderCanHideOnASpotAndIsThenInvisibleToTheSeeker()
        {
            var state = HideSeekModule.Create(7, 0);
            var spot = AnOpenSpot(state);
            state = state.Moved(1, spot.X + 0.5, spot.Y + 0.5);
            Assert.IsTrue(module.ViewFor(state, 1).YouCanHide);

            state = Tick(state, null, Still(0)); // the first number only syncs
            state = Tick(state, null, Still(1)); // a press
            Assert.IsTrue(state.Players[1].Hidden);
            Assert.IsTrue(module.ViewFor(state, 1).YouHidden);

            // Round two: seeker is allowed to look, and stands right next to the hiding place.
            state = state.AtTime(HideSeekRules.HideMs).Moved(0, spot.X + 0.5, spot.Y + 0.5);
            var seekerView = module.ViewFor(state, 0);
            foreach (var p in seekerView.Players) Assert.AreNotEqual(1, p.Seat, "A hidden hider must not be shown.");

            // Pressing the button again brings the hider out.
            state = Tick(state, null, Still(2));
            Assert.IsFalse(state.Players[1].Hidden);
        }

        [Test]
        public void HidingOnlyWorksNextToAHidingPlace()
        {
            var state = HideSeekModule.Create(7, 0);
            // Put the hider on a floor tile that isn't a hiding place.
            int x = 1, y = 1;
            Assert.AreNotEqual(HideSeekMap.Spot, state.Map.TileAt(x, y));
            state = state.Moved(1, x + 0.5, y + 0.5);
            Assert.IsFalse(module.ViewFor(state, 1).YouCanHide);
            state = Tick(state, null, Still(0));
            state = Tick(state, null, Still(1));
            Assert.IsFalse(state.Players[1].Hidden);
        }

        [Test]
        public void GrabbingCatchesAHiderInReachButMissesWhenNobodyIsClose()
        {
            var state = HideSeekModule.Create(7, 0);
            var spot = AnOpenSpot(state);
            state = state.Moved(1, spot.X + 0.5, spot.Y + 0.5);
            state = Tick(state, null, Still(0));
            state = Tick(state, null, Still(1));
            Assert.IsTrue(state.Players[1].Hidden);

            state = state.AtTime(HideSeekRules.HideMs).Moved(0, spot.X + 0.5, spot.Y + 0.5);
            state = Tick(state, Still(0));
            state = Tick(state, Still(1));
            Assert.IsTrue(state.Players[1].Caught, "Grab should catch a hidden hider in reach.");
            Assert.IsFalse(state.Players[1].Hidden);
            Assert.Greater(state.Players[0].CooldownMs, 0);

            // Far away: a miss, and a longer wait.
            var fresh = HideSeekModule.Create(7, 0).AtTime(HideSeekRules.HideMs);
            fresh = Tick(fresh, Still(0));
            fresh = Tick(fresh, Still(1));
            for (int s = 0; s < 4; s++) Assert.IsFalse(fresh.Players[s].Caught);
            Assert.Greater(fresh.Players[0].CooldownMs, 1000);
        }

        [Test]
        public void TheSeekerCannotGrabDuringTheHidingPhase()
        {
            var state = HideSeekModule.Create(7, 0);
            state = state.Moved(1, state.Players[0].X + 0.5, state.Players[0].Y);
            state = Tick(state, Still(0));
            state = Tick(state, Still(1));
            Assert.IsFalse(state.Players[1].Caught);
        }

        [Test]
        public void TimeRunningOutGivesTheWinToTheHidersWhoAreStillFree()
        {
            var state = HideSeekModule.Create(7, 2).AtTime(HideSeekRules.HideMs + HideSeekRules.SeekMs - 50);
            state = Tick(state);
            Assert.IsTrue(state.IsOver);
            Assert.IsFalse(state.SeekerWon);
            var result = module.Result(state);
            Assert.IsTrue(result.IsOver);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, result.WinningSeats);
        }

        [Test]
        public void CatchingTheLastHiderWinsForTheSeeker()
        {
            var state = HideSeekModule.Create(7, 1).AtTime(HideSeekRules.HideMs + 10);
            state = state.WithCaught(0).WithCaught(2);
            state = state.Moved(3, state.Players[1].X + 0.4, state.Players[1].Y);
            state = Tick(state, null, Still(0));
            state = Tick(state, null, Still(1));
            Assert.IsTrue(state.IsOver);
            Assert.IsTrue(state.SeekerWon);
            CollectionAssert.AreEqual(new[] { 1 }, module.Result(state).WinningSeats);
            Assert.AreEqual(ErrorCodes.GameOver, module.Validate(state, 0, new HideSeekTick(100, new HideInput[4])).Code);
            Assert.AreEqual(0, module.SeatsToAct(state).Count);
        }

        [Test]
        public void ACaughtHiderCannotMoveAndWatchesThroughTheSeekersEyes()
        {
            var state = HideSeekModule.Create(7, 0).AtTime(HideSeekRules.HideMs + 10).WithCaught(1);
            var after = Tick(state, null, new HideInput(100, 0, 0));
            Assert.AreEqual(state.Players[1].X, after.Players[1].X);

            var ghost = module.ViewFor(after, 1);
            Assert.IsTrue(ghost.Spectating);
            Assert.IsTrue(ghost.YouCaught);
            bool seesSeeker = false;
            foreach (var p in ghost.Players)
            {
                if (p.Seat == 0) seesSeeker = true;
            }
            Assert.IsTrue(seesSeeker);
            Assert.AreEqual(2, module.SeatsToAct(after).Count - 1);
        }

        // ---------------- Seeing ----------------

        [Test]
        public void AWallBlocksTheView()
        {
            var map = HideSeekMap.Generate(3);
            var none = new bool[map.SecretDoors.Count];
            bool found = false;
            for (int y = 1; y < H - 1 && !found; y++)
            {
                for (int x = 2; x < W - 2 && !found; x++)
                {
                    if (map.TileAt(x, y) == HideSeekMap.Wall && map.TileAt(x - 1, y) != HideSeekMap.Wall && map.TileAt(x + 1, y) != HideSeekMap.Wall)
                    {
                        Assert.IsFalse(HideSeekVision.LineOfSight(map, none, x - 0.5, y + 0.5, x + 1.5, y + 0.5));
                        found = true;
                    }
                }
            }
            Assert.IsTrue(found);
        }

        [Test]
        public void YouSeeALongWayDownAStraightHallButNotAroundACorner()
        {
            var state = HideSeekModule.Create(11, 0).AtTime(HideSeekRules.HideMs);
            var map = state.Map;
            // Find a straight horizontal run of at least 5 floor tiles.
            for (int y = 1; y < H - 1; y++)
            {
                for (int x = 1; x + 5 < W; x++)
                {
                    bool run = true;
                    for (int k = 0; k <= 5; k++)
                    {
                        if (map.TileAt(x + k, y) == HideSeekMap.Wall) run = false;
                    }
                    if (!run) continue;
                    var view = module.ViewFor(state.Moved(0, x + 0.5, y + 0.5), 0);
                    Assert.IsTrue(IsLit(view, x + 4, y), "A hall should be lit for several tiles.");
                    return;
                }
            }
            Assert.Fail("No straight hall found.");
        }

        private static bool IsLit(HideSeekView view, int x, int y)
        {
            int i = y * view.Width + x;
            int digit = Convert.ToInt32(view.Visible[i / 4].ToString(), 16);
            return ((digit >> (3 - (i % 4))) & 1) == 1;
        }

        [Test]
        public void TheSeekerIsBlindfoldedWhileTheHidersHideAndHidersSeeEverything()
        {
            var state = HideSeekModule.Create(9, 0);
            var seeker = module.ViewFor(state, 0);
            Assert.AreEqual(HidePhase.Hiding, seeker.Phase);
            for (int i = 0; i < W * H; i++) Assert.IsFalse(IsLit(seeker, i % W, i / W));
            Assert.AreEqual(1, seeker.Players.Count);

            var hider = module.ViewFor(state, 1);
            for (int i = 0; i < W * H; i++) Assert.IsTrue(IsLit(hider, i % W, i / W));
            Assert.AreEqual(4, hider.Players.Count);

            // After the hiding time, hiders have ordinary vision too.
            var later = module.ViewFor(state.AtTime(HideSeekRules.HideMs), 1);
            bool allLit = true;
            for (int i = 0; i < W * H; i++)
            {
                if (!IsLit(later, i % W, i / W)) allLit = false;
            }
            Assert.IsFalse(allLit);
        }

        [Test]
        public void SecretDoorsLookLikeWallUntilSomeoneWalksThroughThem()
        {
            var state = HideSeekModule.Create(3, 0);
            int door = state.Map.SecretDoors[0];
            Assert.AreEqual('S', state.Map.TileAtIndex(door));

            // In the open round the door shows as wall to everyone.
            var seeking = state.AtTime(HideSeekRules.HideMs);
            Assert.AreEqual('#', module.ViewFor(seeking, 2).Map[door]);
            // Hiders get to know about it while they plan.
            Assert.AreEqual('S', module.ViewFor(state, 2).Map[door]);

            var opened = Tick(seeking.Moved(1, door % W + 0.5, door / W + 0.5));
            Assert.IsTrue(opened.Revealed[0]);
            Assert.AreEqual('D', module.ViewFor(opened, 2).Map[door]);
        }

        // ---------------- Bots ----------------

        [Test]
        public void BotRoundsAlwaysFinishAndNobodyIsEverInsideAWall()
        {
            for (ulong seed = 1; seed <= 12; seed++)
            {
                int seeker = (int)(seed % 4);
                var state = HideSeekModule.Create(seed, seeker);
                var bots = new HideSeekBot[4];
                for (int s = 0; s < 4; s++) bots[s] = new HideSeekBot(seed * 11 + (ulong)s);
                int ticks = 0;
                while (!state.IsOver)
                {
                    Assert.Less(ticks++, 2000, $"Seed {seed} never ended.");
                    var inputs = new HideInput[4];
                    for (int s = 0; s < 4; s++) inputs[s] = bots[s].ChooseInput(state, s);
                    state = Tick(state, inputs);
                    for (int s = 0; s < 4; s++)
                    {
                        var p = state.Players[s];
                        Assert.IsFalse(state.Map.IsWall((int)Math.Floor(p.X), (int)Math.Floor(p.Y)), $"Seed {seed}: a bot is in a wall.");
                    }
                }
                Assert.IsTrue(module.Result(state).IsOver);
                Assert.Greater(module.Result(state).WinningSeats.Count, 0);
            }
        }

        [Test]
        public void ViewsNeverShowAHiddenHiderToAnotherPlayer()
        {
            var state = HideSeekModule.Create(7, 0);
            var spot = AnOpenSpot(state);
            state = state.Moved(1, spot.X + 0.5, spot.Y + 0.5);
            state = Tick(state, null, Still(0));
            state = Tick(state, null, Still(1));
            state = state.AtTime(HideSeekRules.HideMs + 1000).Moved(0, spot.X + 1.5, spot.Y + 0.5).Moved(2, spot.X + 0.5, spot.Y + 0.5);
            foreach (int viewer in new[] { 0, 2, 3 })
            {
                foreach (var p in module.ViewFor(state, viewer).Players) Assert.AreNotEqual(1, p.Seat);
            }
        }
    }
}
