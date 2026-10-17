using System;
using System.Collections.Generic;
using System.Text;

namespace LanternLounge.Games.HideSeek
{
    /// <summary>Line-of-sight, used for what players can see and for who can be grabbed.</summary>
    public static class HideSeekVision
    {
        /// <summary>Walls and secret doors that nobody has opened yet block the view.</summary>
        public static bool Opaque(HideSeekMap map, IReadOnlyList<bool> revealed, int x, int y)
        {
            if (!map.InBounds(x, y)) return true;
            char c = map.TileAt(x, y);
            if (c == HideSeekMap.Wall) return true;
            if (c == HideSeekMap.Secret)
            {
                int k = map.SecretIndexAt(HideSeekMap.Index(x, y));
                return k < 0 || !revealed[k];
            }
            return false;
        }

        /// <summary>Can you see from one point to another without a wall in between?</summary>
        public static bool LineOfSight(HideSeekMap map, IReadOnlyList<bool> revealed, double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(dist / 0.2));
            int tx = (int)Math.Floor(x1), ty = (int)Math.Floor(y1);
            for (int i = 1; i <= steps; i++)
            {
                int cx = (int)Math.Floor(x0 + dx * i / steps);
                int cy = (int)Math.Floor(y0 + dy * i / steps);
                if (cx == tx && cy == ty) return true;
                if (Opaque(map, revealed, cx, cy)) return false;
            }
            return true;
        }
    }

    /// <summary>A player as one viewer sees them.</summary>
    public sealed class HideViewPlayer
    {
        public HideViewPlayer(int seat, double x, double y, double fx, double fy, bool hidden, bool caught, bool isSeeker)
        {
            Seat = seat;
            X = x;
            Y = y;
            FacingX = fx;
            FacingY = fy;
            Hidden = hidden;
            Caught = caught;
            IsSeeker = isSeeker;
        }

        public int Seat { get; }
        public double X { get; }
        public double Y { get; }
        public double FacingX { get; }
        public double FacingY { get; }

        /// <summary>Only ever true for yourself (or for everyone once the round is over).</summary>
        public bool Hidden { get; }

        public bool Caught { get; }
        public bool IsSeeker { get; }
    }

    /// <summary>
    /// What one player is allowed to see right now: the map, which tiles are lit, and only the players they
    /// can actually see. Hidden hiders never appear to anyone else, and a caught hider watches through the
    /// seeker's eyes.
    /// </summary>
    public sealed class HideSeekView
    {
        internal HideSeekView(HideSeekState state, int seat)
        {
            var map = state.Map;
            var me = state.Players[seat];
            bool isSeeker = seat == state.Seeker;
            Phase = state.Phase;
            Spectating = !isSeeker && me.Caught && Phase != HidePhase.Over;
            int source = Spectating ? state.Seeker : seat;
            var sp = state.Players[source];

            int w = map.Width, h = map.Height;
            var vis = new bool[w * h];
            var players = new List<HideViewPlayer>();
            bool showSecrets = false;

            if (Phase == HidePhase.Over)
            {
                for (int i = 0; i < vis.Length; i++) vis[i] = true;
                showSecrets = true;
                for (int s = 0; s < state.Players.Count; s++) players.Add(Show(state, s, true));
            }
            else if (Phase == HidePhase.Hiding && isSeeker)
            {
                // Blindfolded: counting to thirty.
                players.Add(Show(state, seat, true));
            }
            else if (Phase == HidePhase.Hiding && HideSeekRules.HidersSeeWholeMapWhileHiding)
            {
                for (int i = 0; i < vis.Length; i++) vis[i] = true;
                showSecrets = true;
                for (int s = 0; s < state.Players.Count; s++)
                {
                    var p = state.Players[s];
                    if (s == seat) players.Add(Show(state, s, true));
                    else if (!p.Hidden && !p.Caught) players.Add(Show(state, s, false));
                }
            }
            else
            {
                int tx = (int)Math.Floor(sp.X), ty = (int)Math.Floor(sp.Y);
                for (int y = ty - 6; y <= ty + 6; y++)
                {
                    for (int x = tx - 6; x <= tx + 6; x++)
                    {
                        if (!map.InBounds(x, y)) continue;
                        double dx = x + 0.5 - sp.X, dy = y + 0.5 - sp.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) > HideSeekRules.Vision + 0.7) continue;
                        if (HideSeekVision.LineOfSight(map, state.Revealed, sp.X, sp.Y, x + 0.5, y + 0.5)) vis[HideSeekMap.Index(x, y)] = true;
                    }
                }
                if (map.InBounds(tx, ty)) vis[HideSeekMap.Index(tx, ty)] = true;

                if (!Spectating) players.Add(Show(state, seat, true));
                for (int s = 0; s < state.Players.Count; s++)
                {
                    if (s == seat) continue;
                    var o = state.Players[s];
                    if (o.Caught || o.Hidden) continue;
                    double dx = o.X - sp.X, dy = o.Y - sp.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= HideSeekRules.Vision &&
                        HideSeekVision.LineOfSight(map, state.Revealed, sp.X, sp.Y, o.X, o.Y))
                    {
                        players.Add(Show(state, s, false));
                    }
                }
                if (Spectating)
                {
                    // The seeker is always in view when you watch through their eyes.
                    bool listed = false;
                    foreach (var p in players)
                    {
                        if (p.Seat == state.Seeker) listed = true;
                    }
                    if (!listed) players.Add(Show(state, state.Seeker, false));
                }
            }

            var mapText = new StringBuilder(w * h);
            for (int i = 0; i < w * h; i++)
            {
                char c = map.TileAtIndex(i);
                if (c == HideSeekMap.Secret)
                {
                    c = state.Revealed[map.SecretIndexAt(i)] ? 'D' : showSecrets ? 'S' : '#';
                }
                mapText.Append(c);
            }
            Map = mapText.ToString();

            var hex = new StringBuilder(vis.Length / 4 + 1);
            for (int i = 0; i < vis.Length; i += 4)
            {
                int v = (vis[i] ? 8 : 0) + (i + 1 < vis.Length && vis[i + 1] ? 4 : 0) + (i + 2 < vis.Length && vis[i + 2] ? 2 : 0) + (i + 3 < vis.Length && vis[i + 3] ? 1 : 0);
                hex.Append("0123456789abcdef"[v]);
            }
            Visible = hex.ToString();

            Width = w;
            Height = h;
            ElapsedMs = state.ElapsedMs;
            MsLeft = Phase == HidePhase.Hiding
                ? HideSeekRules.HideMs - state.ElapsedMs
                : Math.Max(0, HideSeekRules.HideMs + HideSeekRules.SeekMs - state.ElapsedMs);
            Seeker = state.Seeker;
            HidersLeft = state.HidersLeft;
            SeekerWon = state.SeekerWon;
            Players = players;
            YouSeat = seat;
            YouIsSeeker = isSeeker;
            YouHidden = me.Hidden;
            YouCaught = me.Caught;
            YouCooldownMs = me.CooldownMs;
            YouX = me.X;
            YouY = me.Y;

            bool canHide = false;
            if (!isSeeker && !me.Caught)
            {
                foreach (var s in map.Spots)
                {
                    double dx = s.X + 0.5 - me.X, dy = s.Y + 0.5 - me.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= HideSeekRules.HideSnap) canHide = true;
                }
            }
            YouCanHide = canHide;
        }

        private static HideViewPlayer Show(HideSeekState state, int seat, bool showHidden)
        {
            var p = state.Players[seat];
            return new HideViewPlayer(seat, p.X, p.Y, p.FacingX, p.FacingY, p.Hidden && showHidden, p.Caught, seat == state.Seeker);
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>The maze as text, row after row ('#' wall, '.' floor, 'H' hiding place, 'D' open secret door, 'S' a secret door you may see).</summary>
        public string Map { get; }

        /// <summary>Which tiles are lit right now: one hex digit per four tiles, first tile in the top bit.</summary>
        public string Visible { get; }

        public HidePhase Phase { get; }
        public int ElapsedMs { get; }

        /// <summary>Time left in the hiding phase, then in the seeking phase.</summary>
        public int MsLeft { get; }

        public int Seeker { get; }
        public int HidersLeft { get; }
        public bool SeekerWon { get; }

        /// <summary>True for a caught hider, who now sees what the seeker sees.</summary>
        public bool Spectating { get; }

        public IReadOnlyList<HideViewPlayer> Players { get; }

        public int YouSeat { get; }
        public bool YouIsSeeker { get; }
        public bool YouHidden { get; }
        public bool YouCaught { get; }
        public int YouCooldownMs { get; }
        public bool YouCanHide { get; }
        public double YouX { get; }
        public double YouY { get; }
    }
}
