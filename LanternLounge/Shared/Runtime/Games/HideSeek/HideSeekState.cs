using System;
using System.Collections.Generic;

namespace LanternLounge.Games.HideSeek
{
    /// <summary>
    /// What one player is doing with the controls right now: the joystick (-100..100 each way) and a
    /// button counter. Pressing the button adds one to <see cref="ActSeq"/>; the game acts when the number changes.
    /// </summary>
    public sealed class HideInput
    {
        public HideInput(int dx, int dy, int actSeq)
        {
            Dx = Math.Max(-100, Math.Min(100, dx));
            Dy = Math.Max(-100, Math.Min(100, dy));
            ActSeq = actSeq;
        }

        public int Dx { get; }
        public int Dy { get; }
        public int ActSeq { get; }
    }

    /// <summary>
    /// The one "move" of this game: let <see cref="DtMs"/> milliseconds pass, with everyone's latest controls.
    /// The server sends one of these every tick. Entries in <see cref="Inputs"/> may be null (no controls).
    /// </summary>
    public sealed class HideSeekTick
    {
        public HideSeekTick(int dtMs, HideInput[] inputs)
        {
            DtMs = dtMs;
            Inputs = inputs;
        }

        public int DtMs { get; }
        public HideInput[] Inputs { get; }
    }

    public enum HidePhase
    {
        /// <summary>The first 30 seconds: hiders run and hide, the seeker waits.</summary>
        Hiding,

        /// <summary>The seeker is looking.</summary>
        Seeking,

        Over
    }

    /// <summary>One player at one moment. Immutable.</summary>
    public sealed class HidePlayer
    {
        internal HidePlayer(double x, double y, double fx, double fy, bool hidden, bool caught, int caughtAtMs, int cooldownMs, int actSeq)
        {
            X = x;
            Y = y;
            FacingX = fx;
            FacingY = fy;
            Hidden = hidden;
            Caught = caught;
            CaughtAtMs = caughtAtMs;
            CooldownMs = cooldownMs;
            ActSeq = actSeq;
        }

        /// <summary>Position in tiles; the centre of tile (3, 2) is (3.5, 2.5).</summary>
        public double X { get; }
        public double Y { get; }

        /// <summary>The way they last walked (a unit vector).</summary>
        public double FacingX { get; }
        public double FacingY { get; }

        public bool Hidden { get; }
        public bool Caught { get; }
        public int CaughtAtMs { get; }

        /// <summary>Time until the seeker can grab again.</summary>
        public int CooldownMs { get; }

        /// <summary>The last button counter seen from this player, or -1 before the first.</summary>
        public int ActSeq { get; }
    }

    /// <summary>
    /// The whole round as it really is, including where every hider is. Never send this to a player: send a
    /// <see cref="HideSeekView"/>. Immutable once built.
    /// </summary>
    public sealed class HideSeekState
    {
        internal HideSeekState(HideSeekMap map, HidePlayer[] players, bool[] revealed, int elapsedMs, int seeker, bool isOver, bool seekerWon)
        {
            Map = map;
            Players = Array.AsReadOnly(players);
            Revealed = Array.AsReadOnly(revealed);
            ElapsedMs = elapsedMs;
            Seeker = seeker;
            IsOver = isOver;
            SeekerWon = seekerWon;
        }

        public HideSeekMap Map { get; }
        public IReadOnlyList<HidePlayer> Players { get; }

        /// <summary>For each secret door (same order as <see cref="HideSeekMap.SecretDoors"/>), has someone walked through it?</summary>
        public IReadOnlyList<bool> Revealed { get; }

        /// <summary>Time since the round began.</summary>
        public int ElapsedMs { get; }

        /// <summary>The seat that is the seeker. Everyone else hides.</summary>
        public int Seeker { get; }

        public bool IsOver { get; }

        /// <summary>True if the seeker caught every hider.</summary>
        public bool SeekerWon { get; }

        public HidePhase Phase => IsOver ? HidePhase.Over : ElapsedMs < HideSeekRules.HideMs ? HidePhase.Hiding : HidePhase.Seeking;

        // ---- Copies with one change, for tests, tutorials and replays ----

        /// <summary>A copy with one player standing somewhere else.</summary>
        public HideSeekState Moved(int seat, double x, double y)
        {
            var copy = CopyPlayers();
            var p = Players[seat];
            copy[seat] = new HidePlayer(x, y, p.FacingX, p.FacingY, p.Hidden, p.Caught, p.CaughtAtMs, p.CooldownMs, p.ActSeq);
            return new HideSeekState(Map, copy, CopyRevealed(), ElapsedMs, Seeker, IsOver, SeekerWon);
        }

        /// <summary>A copy with a different clock (for example, jump straight to the seeking phase).</summary>
        public HideSeekState AtTime(int elapsedMs)
        {
            return new HideSeekState(Map, CopyPlayers(), CopyRevealed(), elapsedMs, Seeker, IsOver, SeekerWon);
        }

        /// <summary>A copy in which this hider has been caught.</summary>
        public HideSeekState WithCaught(int seat)
        {
            var copy = CopyPlayers();
            var p = Players[seat];
            copy[seat] = new HidePlayer(p.X, p.Y, p.FacingX, p.FacingY, false, true, ElapsedMs, p.CooldownMs, p.ActSeq);
            return new HideSeekState(Map, copy, CopyRevealed(), ElapsedMs, Seeker, IsOver, SeekerWon);
        }

        private HidePlayer[] CopyPlayers()
        {
            var copy = new HidePlayer[Players.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = Players[i];
            return copy;
        }

        private bool[] CopyRevealed()
        {
            var copy = new bool[Revealed.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = Revealed[i];
            return copy;
        }

        public int HidersLeft
        {
            get
            {
                int n = 0;
                for (int s = 0; s < Players.Count; s++)
                {
                    if (s != Seeker && !Players[s].Caught) n++;
                }
                return n;
            }
        }
    }
}
