using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.HideSeek
{
    /// <summary>
    /// A computer player for one seat. Which job it does depends on the round: as the seeker it chases anyone it
    /// can see, then checks hiding places one by one (at 80% speed, so a person can outrun it), and after a
    /// while starts leaning on walls to look for secret doors. As a hider it picks a hiding place near it,
    /// goes there and hides, and bolts if the seeker gets close. A hider bot knows the whole maze, like a kid
    /// who lives in the house; a seeker bot only uses what its own view shows.
    /// </summary>
    public sealed class HideSeekBot
    {
        private const double SeekerBotSpeed = 0.8;
        private const int SecretsAfterMs = 70000;

        private static readonly int[] StepX = { 1, -1, 0, 0 };
        private static readonly int[] StepY = { 0, 0, 1, -1 };

        private enum Mode
        {
            Go,
            Hidden,
            Flee
        }

        private readonly SeededRandom random;
        private int actSeq;

        // seeker memory
        private readonly HashSet<int> checkedSpots = new HashSet<int>();
        private readonly HashSet<int> probedDoors = new HashSet<int>();
        private int goal = -1;
        private bool hasWander;
        private int wanderX, wanderY;

        // hider memory
        private Mode mode = Mode.Go;
        private int spot = -1;
        private int fleeUntil;
        private int lastSpot = -1;

        public HideSeekBot(ulong seed)
        {
            random = new SeededRandom(seed);
        }

        private HideInput Idle() => new HideInput(0, 0, actSeq);

        private HideInput Press()
        {
            actSeq++;
            return new HideInput(0, 0, actSeq);
        }

        public HideInput ChooseInput(HideSeekState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsOver || state.Players[seat].Caught) return Idle();
            return seat == state.Seeker ? SeekerInput(state, seat) : HiderInput(state, seat);
        }

        /// <summary>The way to step toward a goal, given every tile's distance to that goal.</summary>
        private static void Toward(HideSeekMap map, int[] dist, HidePlayer p, double goalX, double goalY, double scale, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            int w = map.Width;
            int tx = (int)Math.Floor(p.X), ty = (int)Math.Floor(p.Y);
            int here = dist[ty * w + tx];
            if (here < 0) return;
            double targetX, targetY;
            if (here == 0)
            {
                targetX = goalX;
                targetY = goalY;
            }
            else
            {
                int best = here, bx = tx, by = ty;
                for (int d = 0; d < 4; d++)
                {
                    int nx = tx + StepX[d], ny = ty + StepY[d];
                    if (!map.InBounds(nx, ny)) continue;
                    int nd = dist[ny * w + nx];
                    if (nd >= 0 && nd < best)
                    {
                        best = nd;
                        bx = nx;
                        by = ny;
                    }
                }
                targetX = bx + 0.5;
                targetY = by + 0.5;
            }
            double vx = targetX - p.X, vy = targetY - p.Y;
            double len = Math.Sqrt(vx * vx + vy * vy);
            if (len < 0.02) return;
            dx = (int)Math.Round(vx / len * 100 * scale);
            dy = (int)Math.Round(vy / len * 100 * scale);
        }

        // ---------------- Seeker ----------------

        private HideInput SeekerInput(HideSeekState state, int seat)
        {
            var view = new HideSeekView(state, seat);
            if (view.Phase != HidePhase.Seeking) return Idle();

            var map = state.Map;
            var me = state.Players[seat];
            int w = map.Width;
            int seekMs = state.ElapsedMs - HideSeekRules.HideMs;
            bool knows = seekMs > SecretsAfterMs;

            // 1. Someone in sight: chase, and grab when close.
            HideViewPlayer near = null;
            double nearD = double.MaxValue;
            foreach (var p in view.Players)
            {
                if (p.Seat == seat || p.Caught) continue;
                double d = Math.Sqrt((p.X - me.X) * (p.X - me.X) + (p.Y - me.Y) * (p.Y - me.Y));
                if (d < nearD)
                {
                    nearD = d;
                    near = p;
                }
            }
            if (near != null)
            {
                if (nearD <= HideSeekRules.GrabReach - 0.2 && me.CooldownMs <= 0) return Press();
                var chase = HideSeekPaths.Distances(map, (int)Math.Floor(near.Y) * w + (int)Math.Floor(near.X), state.Revealed, knows);
                Toward(map, chase, me, near.X, near.Y, SeekerBotSpeed, out int cdx, out int cdy);
                return new HideInput(cdx, cdy, actSeq);
            }

            // 2. Check hiding places, nearest few first.
            var fromMe = HideSeekPaths.Distances(map, (int)Math.Floor(me.Y) * w + (int)Math.Floor(me.X), state.Revealed, knows);
            for (int i = 0; i < map.Spots.Count; i++)
            {
                if (checkedSpots.Contains(i)) continue;
                double sx = map.Spots[i].X + 0.5, sy = map.Spots[i].Y + 0.5;
                double d = Math.Sqrt((sx - me.X) * (sx - me.X) + (sy - me.Y) * (sy - me.Y));
                if (d <= HideSeekRules.GrabReach - 0.25 && HideSeekVision.LineOfSight(map, state.Revealed, me.X, me.Y, sx, sy))
                {
                    checkedSpots.Add(i);
                    if (me.CooldownMs <= 0) return Press();
                }
            }

            int targetX = -1, targetY = -1, targetDoor = -1;
            if (goal >= 0 && !checkedSpots.Contains(goal))
            {
                targetX = map.Spots[goal].X;
                targetY = map.Spots[goal].Y;
            }
            else
            {
                var cands = new List<int>();
                for (int i = 0; i < map.Spots.Count; i++)
                {
                    if (checkedSpots.Contains(i)) continue;
                    if (fromMe[map.Spots[i].Y * w + map.Spots[i].X] >= 0) cands.Add(i);
                }
                cands.Sort((a, b) =>
                {
                    int da = fromMe[map.Spots[a].Y * w + map.Spots[a].X];
                    int db = fromMe[map.Spots[b].Y * w + map.Spots[b].X];
                    return da != db ? da.CompareTo(db) : a.CompareTo(b);
                });
                if (cands.Count > 0)
                {
                    goal = cands[random.NextInt(Math.Min(3, cands.Count))];
                    targetX = map.Spots[goal].X;
                    targetY = map.Spots[goal].Y;
                }
                else
                {
                    goal = -1;
                }
            }

            if (targetX < 0 && knows)
            {
                int bestD = int.MaxValue;
                for (int k = 0; k < map.SecretDoors.Count; k++)
                {
                    int t = map.SecretDoors[k];
                    if (probedDoors.Contains(t) || state.Revealed[k]) continue;
                    int d = fromMe[t];
                    if (d >= 0 && d < bestD)
                    {
                        bestD = d;
                        targetX = t % w;
                        targetY = t / w;
                        targetDoor = t;
                    }
                }
            }

            if (targetX < 0)
            {
                if (checkedSpots.Count >= map.Spots.Count) checkedSpots.Clear();
                if (!hasWander || fromMe[wanderY * w + wanderX] < 3)
                {
                    hasWander = false;
                    for (int k = 0; k < 30; k++)
                    {
                        int x = random.NextInt(w), y = random.NextInt(map.Height);
                        if (map.TileAt(x, y) != HideSeekMap.Wall && fromMe[y * w + x] > 6)
                        {
                            wanderX = x;
                            wanderY = y;
                            hasWander = true;
                            break;
                        }
                    }
                }
                if (!hasWander) return Idle();
                targetX = wanderX;
                targetY = wanderY;
            }

            if (targetDoor >= 0 && (int)Math.Floor(me.X) == targetX && (int)Math.Floor(me.Y) == targetY) probedDoors.Add(targetDoor);
            var dist = HideSeekPaths.Distances(map, targetY * w + targetX, state.Revealed, knows);
            Toward(map, dist, me, targetX + 0.5, targetY + 0.5, SeekerBotSpeed, out int dx, out int dy);
            return new HideInput(dx, dy, actSeq);
        }

        // ---------------- Hider ----------------

        private int PickSpot(HideSeekState state, int seat, int[] fromMe, int[] fromSeeker)
        {
            var map = state.Map;
            int w = map.Width;
            var cands = new List<int>();
            for (int i = 0; i < map.Spots.Count; i++)
            {
                var s = map.Spots[i];
                if (i == lastSpot) continue;
                if (s.Secret && random.NextInt(100) >= 25) continue;
                bool taken = false;
                for (int o = 0; o < state.Players.Count; o++)
                {
                    if (o != seat && state.Players[o].Hidden && (int)Math.Floor(state.Players[o].X) == s.X && (int)Math.Floor(state.Players[o].Y) == s.Y) taken = true;
                }
                if (taken) continue;
                if (fromMe[s.Y * w + s.X] < 0) continue;
                if (fromSeeker != null && fromSeeker[s.Y * w + s.X] >= 0 && fromSeeker[s.Y * w + s.X] < 8) continue;
                cands.Add(i);
            }
            if (cands.Count == 0) return -1;
            cands.Sort((a, b) =>
            {
                int da = fromMe[map.Spots[a].Y * w + map.Spots[a].X];
                int db = fromMe[map.Spots[b].Y * w + map.Spots[b].X];
                return da != db ? da.CompareTo(db) : a.CompareTo(b);
            });
            return cands[random.NextInt(Math.Min(5, cands.Count))];
        }

        private HideInput HiderInput(HideSeekState state, int seat)
        {
            var map = state.Map;
            var me = state.Players[seat];
            var seeker = state.Players[state.Seeker];
            int w = map.Width;
            bool hiding = state.ElapsedMs < HideSeekRules.HideMs;

            double ds = Math.Sqrt((seeker.X - me.X) * (seeker.X - me.X) + (seeker.Y - me.Y) * (seeker.Y - me.Y));
            bool sees = ds <= HideSeekRules.Vision && HideSeekVision.LineOfSight(map, state.Revealed, me.X, me.Y, seeker.X, seeker.Y);

            if (me.Hidden)
            {
                if (!hiding && sees && ds < 2.0)
                {
                    mode = Mode.Flee;
                    fleeUntil = state.ElapsedMs + 4000;
                    lastSpot = spot;
                    return Press(); // leave the hiding place and run
                }
                mode = Mode.Hidden;
                return Idle();
            }
            if (mode == Mode.Hidden) mode = Mode.Go;

            var fromMe = HideSeekPaths.Distances(map, (int)Math.Floor(me.Y) * w + (int)Math.Floor(me.X), null, true);

            if (mode == Mode.Flee)
            {
                if (state.ElapsedMs >= fleeUntil)
                {
                    mode = Mode.Go;
                    spot = -1;
                }
                else
                {
                    var fromSeeker = HideSeekPaths.Distances(map, (int)Math.Floor(seeker.Y) * w + (int)Math.Floor(seeker.X), null, true);
                    int tx = (int)Math.Floor(me.X), ty = (int)Math.Floor(me.Y);
                    int best = -1, bx = tx, by = ty;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = tx + StepX[d], ny = ty + StepY[d];
                        if (!map.InBounds(nx, ny) || map.TileAt(nx, ny) == HideSeekMap.Wall) continue;
                        int nd = fromSeeker[ny * w + nx];
                        if (nd > best)
                        {
                            best = nd;
                            bx = nx;
                            by = ny;
                        }
                    }
                    double vx = bx + 0.5 - me.X, vy = by + 0.5 - me.Y;
                    double len = Math.Sqrt(vx * vx + vy * vy);
                    if (len < 0.0001) len = 1;
                    return new HideInput((int)Math.Round(vx / len * 100), (int)Math.Round(vy / len * 100), actSeq);
                }
            }

            if (spot < 0)
            {
                int[] fromSeeker = hiding ? null : HideSeekPaths.Distances(map, (int)Math.Floor(seeker.Y) * w + (int)Math.Floor(seeker.X), null, true);
                spot = PickSpot(state, seat, fromMe, fromSeeker);
                if (spot < 0) return Idle();
            }

            var target = map.Spots[spot];
            double sdx = target.X + 0.5 - me.X, sdy = target.Y + 0.5 - me.Y;
            if (Math.Sqrt(sdx * sdx + sdy * sdy) <= HideSeekRules.HideSnap - 0.15) return Press();
            var dist = HideSeekPaths.Distances(map, target.Y * w + target.X, null, true);
            Toward(map, dist, me, target.X + 0.5, target.Y + 0.5, 1.0, out int dx, out int dy);
            return new HideInput(dx, dy, actSeq);
        }
    }
}
