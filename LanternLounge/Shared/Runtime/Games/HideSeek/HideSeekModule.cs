using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.HideSeek
{
    /// <summary>
    /// Hide and Seek: four players in a maze seen from above. One is the seeker; the other three hide.
    ///
    /// The seeker waits blindfolded for the first 30 seconds while the hiders run off. Hiders see the whole
    /// maze while they hide. Then everyone has normal vision: you see down a hall until a wall blocks the view.
    /// A hider can duck into a hiding place and vanish from sight; the seeker finds hidden players by standing
    /// close and pressing Grab. Secret doors look like wall until someone walks through. The seeker wins by
    /// catching every hider; the hiders who are still free when time runs out win.
    ///
    /// This game runs in real time. Instead of turns, the server sends one <see cref="HideSeekTick"/> action every
    /// tenth of a second, carrying everyone's controls.
    ///
    /// Options read from <see cref="MatchConfig.Options"/>:
    ///   "seeker" = a seat number, or "random" (default; uses the match seed).
    /// </summary>
    public sealed class HideSeekModule : GameModule<HideSeekState, HideSeekTick, HideSeekView>
    {
        public const string GameId = "hide-seek";

        public static readonly GameInfo Definition =
            new GameInfo(GameId, "Hide & Seek", HideSeekRules.Players, HideSeekRules.Players, GameStatus.Playable, "hide-seek");

        private static readonly IReadOnlyList<int> NoSeats = Array.Empty<int>();

        public override GameInfo Info => Definition;

        protected override HideSeekState CreateInitialState(MatchConfig config)
        {
            string text = config.GetOption("seeker", "random");
            if (!int.TryParse(text, out int seeker) || seeker < 0 || seeker >= HideSeekRules.Players)
            {
                seeker = new SeededRandom(config.Seed ^ 0x5EEC3AUL).NextInt(HideSeekRules.Players);
            }
            return Create(config.Seed, seeker);
        }

        /// <summary>A fresh maze with a chosen seeker.</summary>
        public static HideSeekState Create(ulong seed, int seeker)
        {
            if (seeker < 0 || seeker >= HideSeekRules.Players) throw new ArgumentOutOfRangeException(nameof(seeker));
            var map = HideSeekMap.Generate(seed);
            var players = new HidePlayer[HideSeekRules.Players];
            int hider = 0;
            for (int s = 0; s < players.Length; s++)
            {
                var start = s == seeker ? map.SeekerStart : map.HiderStarts[hider++];
                players[s] = new HidePlayer(start.X + 0.5, start.Y + 0.5, 0, 1, false, false, -1, 0, -1);
            }
            return new HideSeekState(map, players, new bool[map.SecretDoors.Count], 0, seeker, false, false);
        }

        /// <summary>A new round for a rematch: new maze, and the next seat is the seeker.</summary>
        public HideSeekState Rematch(HideSeekState previous, ulong seed)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            return Create(seed, (previous.Seeker + 1) % HideSeekRules.Players);
        }

        public override ValidationResult Validate(HideSeekState state, int seat, HideSeekTick action)
        {
            if (state == null) return ValidationResult.Fail(ErrorCodes.InvalidAction, "There is no round.");
            if (action == null || action.Inputs == null || action.Inputs.Length != HideSeekRules.Players || action.DtMs < 0)
                return ValidationResult.Fail(ErrorCodes.InvalidAction, "That isn't a valid tick.");
            if (state.IsOver) return ValidationResult.Fail(ErrorCodes.GameOver, "This round is over.");
            return ValidationResult.Success;
        }

        private sealed class Mutable
        {
            public double X, Y, Fx, Fy;
            public bool Hidden, Caught;
            public int CaughtAt, Cooldown, ActSeq;
        }

        protected override HideSeekState ApplyValidated(HideSeekState state, int seat, HideSeekTick tick)
        {
            var map = state.Map;
            int dt = tick.DtMs;
            bool seeking = state.ElapsedMs >= HideSeekRules.HideMs;
            int elapsed = state.ElapsedMs + dt;

            var ps = new Mutable[HideSeekRules.Players];
            for (int s = 0; s < ps.Length; s++)
            {
                var p = state.Players[s];
                ps[s] = new Mutable
                {
                    X = p.X, Y = p.Y, Fx = p.FacingX, Fy = p.FacingY, Hidden = p.Hidden, Caught = p.Caught,
                    CaughtAt = p.CaughtAtMs, Cooldown = p.CooldownMs, ActSeq = p.ActSeq
                };
            }
            var revealed = new bool[map.SecretDoors.Count];
            for (int i = 0; i < revealed.Length; i++) revealed[i] = state.Revealed[i];

            for (int s = 0; s < ps.Length; s++)
            {
                var p = ps[s];
                var input = tick.Inputs[s];
                p.Cooldown = Math.Max(0, p.Cooldown - dt);
                if (p.Caught) continue;

                // A press of the button is a change in its counter. The first number seen only syncs up.
                bool act = false;
                if (input != null)
                {
                    if (p.ActSeq == -1) p.ActSeq = input.ActSeq;
                    else if (input.ActSeq != p.ActSeq)
                    {
                        p.ActSeq = input.ActSeq;
                        act = true;
                    }
                }

                if (s == state.Seeker)
                {
                    if (!seeking) continue; // blindfolded and counting
                    if (input != null) MoveBy(map, p, input, HideSeekRules.SeekerSpeed, dt);
                    if (act && p.Cooldown <= 0) Grab(map, ps, s, revealed, elapsed);
                }
                else
                {
                    if (p.Hidden)
                    {
                        if (act) p.Hidden = false;
                        continue;
                    }
                    if (input != null) MoveBy(map, p, input, HideSeekRules.HiderSpeed, dt);
                    if (act) TryHide(map, ps, s);
                }
            }

            // Walking through a secret door opens it for everyone.
            for (int s = 0; s < ps.Length; s++)
            {
                if (ps[s].Caught) continue;
                int tx = (int)Math.Floor(ps[s].X), ty = (int)Math.Floor(ps[s].Y);
                if (!map.InBounds(tx, ty)) continue;
                int k = map.SecretIndexAt(HideSeekMap.Index(tx, ty));
                if (k >= 0) revealed[k] = true;
            }

            int hidersLeft = 0;
            for (int s = 0; s < ps.Length; s++)
            {
                if (s != state.Seeker && !ps[s].Caught) hidersLeft++;
            }
            bool over = false, seekerWon = false;
            if (hidersLeft == 0)
            {
                over = true;
                seekerWon = true;
            }
            else if (elapsed >= HideSeekRules.HideMs + HideSeekRules.SeekMs)
            {
                over = true;
            }

            var players = new HidePlayer[ps.Length];
            for (int s = 0; s < ps.Length; s++)
            {
                var p = ps[s];
                players[s] = new HidePlayer(p.X, p.Y, p.Fx, p.Fy, p.Hidden, p.Caught, p.CaughtAt, p.Cooldown, p.ActSeq);
            }
            return new HideSeekState(map, players, revealed, elapsed, state.Seeker, over, seekerWon);
        }

        private static bool Blocked(HideSeekMap map, double x, double y)
        {
            int x0 = (int)Math.Floor(x - HideSeekRules.Radius), x1 = (int)Math.Floor(x + HideSeekRules.Radius);
            int y0 = (int)Math.Floor(y - HideSeekRules.Radius), y1 = (int)Math.Floor(y + HideSeekRules.Radius);
            for (int ty = y0; ty <= y1; ty++)
            {
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (map.IsWall(tx, ty)) return true;
                }
            }
            return false;
        }

        private static void MoveBy(HideSeekMap map, Mutable p, HideInput input, double speed, int dtMs)
        {
            double ix = input.Dx / 100.0, iy = input.Dy / 100.0;
            double len = Math.Sqrt(ix * ix + iy * iy);
            if (len < 0.12) return; // a resting thumb isn't a push
            double ux = ix / len, uy = iy / len;
            if (len > 1) len = 1;
            double step = speed * len * dtMs / 1000.0;
            p.Fx = ux;
            p.Fy = uy;
            double nx = p.X + ux * step;
            if (!Blocked(map, nx, p.Y)) p.X = nx;
            double ny = p.Y + uy * step;
            if (!Blocked(map, p.X, ny)) p.Y = ny;
        }

        private static void Grab(HideSeekMap map, Mutable[] ps, int seeker, bool[] revealed, int elapsed)
        {
            var me = ps[seeker];
            int best = -1;
            double bestD = HideSeekRules.GrabReach + 1;
            for (int h = 0; h < ps.Length; h++)
            {
                if (h == seeker || ps[h].Caught) continue;
                double dx = ps[h].X - me.X, dy = ps[h].Y - me.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= HideSeekRules.GrabReach && d < bestD && HideSeekVision.LineOfSight(map, revealed, me.X, me.Y, ps[h].X, ps[h].Y))
                {
                    best = h;
                    bestD = d;
                }
            }
            if (best >= 0)
            {
                ps[best].Caught = true;
                ps[best].Hidden = false;
                ps[best].CaughtAt = elapsed;
                me.Cooldown = HideSeekRules.GrabHitCooldownMs;
            }
            else
            {
                me.Cooldown = HideSeekRules.GrabMissCooldownMs;
            }
        }

        private static void TryHide(HideSeekMap map, Mutable[] ps, int seat)
        {
            var p = ps[seat];
            int best = -1;
            double bestD = HideSeekRules.HideSnap;
            for (int i = 0; i < map.Spots.Count; i++)
            {
                double dx = map.Spots[i].X + 0.5 - p.X, dy = map.Spots[i].Y + 0.5 - p.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= bestD)
                {
                    best = i;
                    bestD = d;
                }
            }
            if (best < 0) return;
            double sx = map.Spots[best].X + 0.5, sy = map.Spots[best].Y + 0.5;
            for (int o = 0; o < ps.Length; o++)
            {
                if (o != seat && ps[o].Hidden && Math.Abs(ps[o].X - sx) < 0.1 && Math.Abs(ps[o].Y - sy) < 0.1) return; // taken
            }
            p.Hidden = true;
            p.X = sx;
            p.Y = sy;
        }

        public override HideSeekView ViewFor(HideSeekState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (seat < 0 || seat >= HideSeekRules.Players) throw new ArgumentOutOfRangeException(nameof(seat));
            return new HideSeekView(state, seat);
        }

        public override MatchResult Result(HideSeekState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!state.IsOver) return MatchResult.InProgress;
            var winners = new List<int>();
            if (state.SeekerWon) winners.Add(state.Seeker);
            else
            {
                for (int s = 0; s < state.Players.Count; s++)
                {
                    if (s != state.Seeker && !state.Players[s].Caught) winners.Add(s);
                }
            }
            return MatchResult.Win(winners.ToArray());
        }

        /// <summary>Everyone still in the round acts at once.</summary>
        public override IReadOnlyList<int> SeatsToAct(HideSeekState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsOver) return NoSeats;
            var seats = new List<int>();
            for (int s = 0; s < state.Players.Count; s++)
            {
                if (!state.Players[s].Caught) seats.Add(s);
            }
            return seats;
        }
    }
}
