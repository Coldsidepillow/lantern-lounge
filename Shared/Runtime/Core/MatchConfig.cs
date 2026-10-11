using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>
    /// What a room hands to a game when a match starts. The seed always comes from the server
    /// in online play, so shuffles and coin flips can be replayed and can't be chosen by a client.
    /// </summary>
    public sealed class MatchConfig
    {
        private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

        public MatchConfig(int playerCount, ulong seed, IReadOnlyDictionary<string, string> options = null)
        {
            if (playerCount < 1) throw new ArgumentOutOfRangeException(nameof(playerCount));
            PlayerCount = playerCount;
            Seed = seed;
            Options = options ?? NoOptions;
        }

        public int PlayerCount { get; }
        public ulong Seed { get; }

        /// <summary>Game-specific settings, e.g. "firstSeat" or "turnSeconds". Each game decides what it reads.</summary>
        public IReadOnlyDictionary<string, string> Options { get; }

        public string GetOption(string key, string fallback)
        {
            return Options.TryGetValue(key, out var value) ? value : fallback;
        }
    }
}
