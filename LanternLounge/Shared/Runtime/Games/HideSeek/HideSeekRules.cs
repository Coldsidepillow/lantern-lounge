namespace LanternLounge.Games.HideSeek
{
    /// <summary>The numbers that shape a round of Hide and Seek. Positions are in tiles (one hallway is one tile wide).</summary>
    public static class HideSeekRules
    {
        public const int Players = 4;

        /// <summary>The maze is a grid of cells; each cell and each wall between cells is one tile.</summary>
        public const int CellsWide = 15;
        public const int CellsHigh = 10;
        public const int Width = CellsWide * 2 + 1;
        public const int Height = CellsHigh * 2 + 1;

        /// <summary>How often the server steps the game.</summary>
        public const int TickMs = 100;

        /// <summary>The seeker waits blindfolded while the hiders run and hide.</summary>
        public const int HideMs = 30000;

        /// <summary>After that the seeker has this long to catch everyone.</summary>
        public const int SeekMs = 150000;

        /// <summary>Half the width of a player, in tiles.</summary>
        public const double Radius = 0.3;

        public const double HiderSpeed = 3.0;
        public const double SeekerSpeed = 3.3;

        /// <summary>How far anyone can see down a hall (walls still block the view).</summary>
        public const double Vision = 5.5;

        /// <summary>How close the seeker must be to grab someone.</summary>
        public const double GrabReach = 1.15;

        public const int GrabMissCooldownMs = 1500;
        public const int GrabHitCooldownMs = 400;

        /// <summary>How close to a hiding place you must stand to hide in it.</summary>
        public const double HideSnap = 0.6;

        /// <summary>
        /// During the first 30 seconds the hiders can see the whole maze (and its secret doors) so they can
        /// plan where to hide. Set to false to give them the same vision as everyone else the whole time.
        /// </summary>
        public static readonly bool HidersSeeWholeMapWhileHiding = true;
    }
}
