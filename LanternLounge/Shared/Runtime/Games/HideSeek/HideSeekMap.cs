using System;
using System.Collections.Generic;
using LanternLounge.Core;

namespace LanternLounge.Games.HideSeek
{
    /// <summary>A hiding place: a bush, crate or closet you can duck into.</summary>
    public readonly struct HideSpot
    {
        public HideSpot(int x, int y, bool secret)
        {
            X = x;
            Y = y;
            Secret = secret;
        }

        public int X { get; }
        public int Y { get; }

        /// <summary>True for spots inside a secret room.</summary>
        public bool Secret { get; }
    }

    /// <summary>A tile position.</summary>
    public readonly struct TilePos
    {
        public TilePos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
    }

    /// <summary>
    /// A maze, built from a seed. Tiles: '#' wall, '.' floor, 'H' hiding place, 'S' secret door
    /// (you can walk through it, but it looks like wall until someone does). The same seed always
    /// gives the same maze. The map never changes during a round, so states share it.
    /// </summary>
    public sealed class HideSeekMap
    {
        public const char Wall = '#';
        public const char Floor = '.';
        public const char Spot = 'H';
        public const char Secret = 'S';

        private readonly char[] tiles;
        private readonly int[] secretIndexByTile;

        private HideSeekMap(char[] tiles, List<HideSpot> spots, List<int> secrets, TilePos seekerStart, TilePos[] hiderStarts, int deadEnds)
        {
            this.tiles = tiles;
            Spots = spots.ToArray();
            SecretDoors = secrets.ToArray();
            SeekerStart = seekerStart;
            HiderStarts = hiderStarts;
            DeadEnds = deadEnds;
            secretIndexByTile = new int[tiles.Length];
            for (int i = 0; i < secretIndexByTile.Length; i++) secretIndexByTile[i] = -1;
            for (int i = 0; i < SecretDoors.Count; i++) secretIndexByTile[SecretDoors[i]] = i;
        }

        public int Width => HideSeekRules.Width;
        public int Height => HideSeekRules.Height;

        public IReadOnlyList<HideSpot> Spots { get; }

        /// <summary>Tile indexes (y * Width + x) of the secret doors.</summary>
        public IReadOnlyList<int> SecretDoors { get; }

        public TilePos SeekerStart { get; }

        /// <summary>Three starting places for the hiders, near the middle.</summary>
        public IReadOnlyList<TilePos> HiderStarts { get; }

        public int DeadEnds { get; }

        public static int Index(int x, int y) => y * HideSeekRules.Width + x;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public char TileAt(int x, int y) => InBounds(x, y) ? tiles[Index(x, y)] : Wall;

        public char TileAtIndex(int index) => tiles[index];

        /// <summary>Which secret door sits on this tile, or -1.</summary>
        public int SecretIndexAt(int tileIndex) => secretIndexByTile[tileIndex];

        public bool IsWall(int x, int y) => TileAt(x, y) == Wall;

        // ---------------- Building a maze ----------------

        public static HideSeekMap Generate(ulong seed)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                ulong s = unchecked(seed + (ulong)attempt * 7919UL);
                var map = TryGenerate(new SeededRandom(s));
                if (map != null) return map;
            }
            throw new InvalidOperationException("Could not build a maze.");
        }

        private sealed class RoomPlan
        {
            public int Cx;
            public int Cy;
            public bool Secret;
        }

        private sealed class DoorPlan
        {
            public int Dx, Dy, Ix, Iy, Side;
        }

        private static readonly int[] StepX = { 1, -1, 0, 0 };
        private static readonly int[] StepY = { 0, 0, 1, -1 };

        private static HideSeekMap TryGenerate(SeededRandom rnd)
        {
            int cw = HideSeekRules.CellsWide, ch = HideSeekRules.CellsHigh, w = HideSeekRules.Width, h = HideSeekRules.Height;
            var tiles = new char[w * h];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = Wall;
            var isRoom = new bool[cw * ch];
            var rooms = new List<RoomPlan>();

            // Five rooms of 2 x 2 cells, kept apart, and away from the seeker's corner.
            for (int tries = 0; tries < 400 && rooms.Count < 5; tries++)
            {
                int cx = rnd.NextInt(cw - 1), cy = rnd.NextInt(ch - 1);
                if (cx <= 2 && cy <= 2) continue;
                bool ok = true;
                foreach (var r in rooms)
                {
                    if (Math.Abs(cx - r.Cx) < 3 && Math.Abs(cy - r.Cy) < 3) ok = false;
                }
                if (!ok) continue;
                rooms.Add(new RoomPlan { Cx = cx, Cy = cy, Secret = rooms.Count >= 2 });
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++) isRoom[(cy + dy) * cw + cx + dx] = true;
                }
            }
            if (rooms.Count < 5) return null;

            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    if (!isRoom[cy * cw + cx]) tiles[Index(2 * cx + 1, 2 * cy + 1)] = Floor;
                }
            }

            // Winding halls: a depth-first walk joins every cell that isn't in a room.
            var visited = (bool[])isRoom.Clone();
            var stack = new List<int> { 0 };
            visited[0] = true;
            var options = new List<int>(4);
            while (stack.Count > 0)
            {
                int cur = stack[stack.Count - 1];
                int cx = cur % cw, cy = cur / cw;
                options.Clear();
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + StepX[d], ny = cy + StepY[d];
                    if (nx < 0 || ny < 0 || nx >= cw || ny >= ch || visited[ny * cw + nx]) continue;
                    options.Add(d);
                }
                if (options.Count == 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                int pick = options[rnd.NextInt(options.Count)];
                int mx = cx + StepX[pick], my = cy + StepY[pick];
                tiles[Index(cx + mx + 1, cy + my + 1)] = Floor;
                visited[my * cw + mx] = true;
                stack.Add(my * cw + mx);
            }
            for (int i = 0; i < visited.Length; i++)
            {
                if (!visited[i]) return null;
            }

            // Connecting halls: knock a few extra doorways through so there are loops, not just one way round.
            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    if (isRoom[cy * cw + cx]) continue;
                    for (int d = 0; d < 2; d++)
                    {
                        int nx = cx + (d == 0 ? 1 : 0), ny = cy + (d == 1 ? 1 : 0);
                        if (nx >= cw || ny >= ch || isRoom[ny * cw + nx]) continue;
                        int t = Index(cx + nx + 1, cy + ny + 1);
                        if (tiles[t] == Wall && rnd.NextInt(100) < 9) tiles[t] = Floor;
                    }
                }
            }

            // Rooms: the first two have open doorways on two sides; the other three have one secret door.
            var spots = new List<HideSpot>();
            var secrets = new List<int>();
            foreach (var r in rooms)
            {
                for (int y = 2 * r.Cy + 1; y <= 2 * r.Cy + 3; y++)
                {
                    for (int x = 2 * r.Cx + 1; x <= 2 * r.Cx + 3; x++) tiles[Index(x, y)] = Floor;
                }

                var cand = new List<DoorPlan>();
                for (int i = 0; i < 2; i++)
                {
                    if (r.Cy - 1 >= 0) cand.Add(new DoorPlan { Dx = 2 * (r.Cx + i) + 1, Dy = 2 * r.Cy, Ix = 2 * (r.Cx + i) + 1, Iy = 2 * r.Cy + 1, Side = 0 });
                    if (r.Cy + 2 < ch) cand.Add(new DoorPlan { Dx = 2 * (r.Cx + i) + 1, Dy = 2 * r.Cy + 4, Ix = 2 * (r.Cx + i) + 1, Iy = 2 * r.Cy + 3, Side = 1 });
                    if (r.Cx - 1 >= 0) cand.Add(new DoorPlan { Dx = 2 * r.Cx, Dy = 2 * (r.Cy + i) + 1, Ix = 2 * r.Cx + 1, Iy = 2 * (r.Cy + i) + 1, Side = 2 });
                    if (r.Cx + 2 < cw) cand.Add(new DoorPlan { Dx = 2 * r.Cx + 4, Dy = 2 * (r.Cy + i) + 1, Ix = 2 * r.Cx + 3, Iy = 2 * (r.Cy + i) + 1, Side = 3 });
                }

                var doors = new List<DoorPlan>();
                var first = cand[rnd.NextInt(cand.Count)];
                doors.Add(first);
                if (!r.Secret)
                {
                    var others = new List<DoorPlan>();
                    foreach (var c in cand)
                    {
                        if (c.Side != first.Side) others.Add(c);
                    }
                    if (others.Count > 0) doors.Add(others[rnd.NextInt(others.Count)]);
                }

                var doorInside = new HashSet<int>();
                foreach (var d in doors)
                {
                    if (r.Secret)
                    {
                        tiles[Index(d.Dx, d.Dy)] = Secret;
                        secrets.Add(Index(d.Dx, d.Dy));
                    }
                    else
                    {
                        tiles[Index(d.Dx, d.Dy)] = Floor;
                    }
                    doorInside.Add(Index(d.Ix, d.Iy));
                }

                var inside = new List<int>();
                for (int y = 2 * r.Cy + 1; y <= 2 * r.Cy + 3; y++)
                {
                    for (int x = 2 * r.Cx + 1; x <= 2 * r.Cx + 3; x++)
                    {
                        if (!doorInside.Contains(Index(x, y))) inside.Add(Index(x, y));
                    }
                }
                var insideArray = inside.ToArray();
                rnd.Shuffle(insideArray);
                for (int k = 0; k < 2; k++)
                {
                    tiles[insideArray[k]] = Spot;
                    spots.Add(new HideSpot(insideArray[k] % w, insideArray[k] / w, r.Secret));
                }
            }

            // Where everyone starts: the seeker in the top-left corner, the hiders spread round the middle.
            var seekerStart = new TilePos(1, 1);
            var free = new List<int>();
            for (int cy = 0; cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    if (!isRoom[cy * cw + cx]) free.Add(cy * cw + cx);
                }
            }
            free.Sort((a, b) =>
            {
                int da = Math.Abs(2 * (a % cw) - (cw - 1)) + Math.Abs(2 * (a / cw) - (ch - 1));
                int db = Math.Abs(2 * (b % cw) - (cw - 1)) + Math.Abs(2 * (b / cw) - (ch - 1));
                return da != db ? da.CompareTo(db) : a.CompareTo(b);
            });
            var hiderCells = new List<int>();
            foreach (int c in free)
            {
                if (hiderCells.Count == 3) break;
                bool far = true;
                foreach (int o in hiderCells)
                {
                    if (Math.Abs(o % cw - c % cw) + Math.Abs(o / cw - c / cw) < 3) far = false;
                }
                if (far) hiderCells.Add(c);
            }
            if (hiderCells.Count < 3) return null;
            var hiderStarts = new TilePos[3];
            var startTiles = new HashSet<int> { Index(1, 1) };
            for (int i = 0; i < 3; i++)
            {
                int tx = 2 * (hiderCells[i] % cw) + 1, ty = 2 * (hiderCells[i] / cw) + 1;
                hiderStarts[i] = new TilePos(tx, ty);
                startTiles.Add(Index(tx, ty));
            }

            // Hiding places in the halls: most dead ends, plus a few spread through the maze.
            var deadEnds = new List<int>();
            var middles = new List<int>();
            foreach (int c in free)
            {
                int cx = c % cw, cy = c / cw;
                int t = Index(2 * cx + 1, 2 * cy + 1);
                if (startTiles.Contains(t)) continue;
                int degree = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + StepX[d], ny = cy + StepY[d];
                    if (nx < 0 || ny < 0 || nx >= cw || ny >= ch) continue;
                    if (tiles[Index(cx + nx + 1, cy + ny + 1)] != Wall) degree++;
                }
                if (degree == 1) deadEnds.Add(t);
                else if (degree >= 2) middles.Add(t);
            }
            foreach (int t in deadEnds)
            {
                if (rnd.NextInt(100) < 65)
                {
                    tiles[t] = Spot;
                    spots.Add(new HideSpot(t % w, t / w, false));
                }
            }
            var middleArray = middles.ToArray();
            rnd.Shuffle(middleArray);
            int added = 0;
            foreach (int t in middleArray)
            {
                if (added >= 4) break;
                if (Math.Abs(t % w - 1) + Math.Abs(t / w - 1) < 8) continue;
                tiles[t] = Spot;
                spots.Add(new HideSpot(t % w, t / w, false));
                added++;
            }
            if (spots.Count < 10) return null;

            var map = new HideSeekMap(tiles, spots, secrets, seekerStart, hiderStarts, deadEnds.Count);

            // Every floor tile must be reachable (the secret rooms through their doors).
            var dist = HideSeekPaths.Distances(map, Index(1, 1), null, true);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] != Wall && dist[i] < 0) return null;
            }
            return map;
        }
    }

    /// <summary>Shortest-path helpers for the computer players.</summary>
    public static class HideSeekPaths
    {
        private static readonly int[] StepX = { 1, -1, 0, 0 };
        private static readonly int[] StepY = { 0, 0, 1, -1 };

        /// <summary>
        /// How many steps from tile <paramref name="from"/> to every tile (-1 if unreachable). Secret doors count as
        /// walkable if <paramref name="knowsSecrets"/> is true or the door has been revealed.
        /// </summary>
        public static int[] Distances(HideSeekMap map, int from, IReadOnlyList<bool> revealed, bool knowsSecrets)
        {
            int w = map.Width, h = map.Height;
            var dist = new int[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new int[w * h];
            int head = 0, tail = 0;
            queue[tail++] = from;
            dist[from] = 0;
            while (head < tail)
            {
                int cur = queue[head++];
                int x = cur % w, y = cur / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + StepX[d], ny = y + StepY[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (dist[n] >= 0) continue;
                    char c = map.TileAtIndex(n);
                    if (c == HideSeekMap.Wall) continue;
                    if (c == HideSeekMap.Secret)
                    {
                        bool open = knowsSecrets;
                        if (!open && revealed != null)
                        {
                            int k = map.SecretIndexAt(n);
                            open = k >= 0 && revealed[k];
                        }
                        if (!open) continue;
                    }
                    dist[n] = dist[cur] + 1;
                    queue[tail++] = n;
                }
            }
            return dist;
        }
    }
}
