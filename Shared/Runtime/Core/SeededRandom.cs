using System;

namespace LanternLounge.Core
{
    /// <summary>
    /// A small deterministic random number generator (SplitMix64).
    /// Use this instead of System.Random or UnityEngine.Random in rules code: those produce
    /// different sequences on Unity's runtime and on the .NET server, which would make the
    /// client and server disagree about a shuffle.
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong state;

        public SeededRandom(ulong seed)
        {
            state = seed;
        }

        /// <summary>The internal state, so a match can be saved and resumed mid-game.</summary>
        public ulong State
        {
            get => state;
            set => state = value;
        }

        public ulong NextULong()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>A uniform integer in [0, maxExclusive), without modulo bias.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            unchecked
            {
                uint bound = (uint)maxExclusive;
                uint threshold = (0u - bound) % bound;
                while (true)
                {
                    uint r = NextUInt();
                    if (r >= threshold) return (int)(r % bound);
                }
            }
        }

        /// <summary>Fisher-Yates shuffle in place.</summary>
        public void Shuffle<T>(T[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
