using System;

namespace LanternLounge.Core
{
    /// <summary>How far along a game is. Drives the badge on its library tile.</summary>
    public enum GameStatus
    {
        Playable,
        Preview,
        ComingSoon
    }

    /// <summary>
    /// One entry in the game registry: everything the lounge needs to list and launch a game
    /// without knowing anything about its rules.
    /// </summary>
    public sealed class GameInfo
    {
        public GameInfo(string id, string displayName, int minPlayers, int maxPlayers, GameStatus status, string themeKey)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A game needs an id.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A game needs a display name.", nameof(displayName));
            if (minPlayers < 1) throw new ArgumentOutOfRangeException(nameof(minPlayers), "At least one player is required.");
            if (maxPlayers < minPlayers) throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Max players must be at least min players.");

            Id = id;
            DisplayName = displayName;
            MinPlayers = minPlayers;
            MaxPlayers = maxPlayers;
            Status = status;
            ThemeKey = themeKey ?? id;
        }

        /// <summary>Stable machine id, e.g. "pebble-drop". Never shown to players.</summary>
        public string Id { get; }

        public string DisplayName { get; }
        public int MinPlayers { get; }
        public int MaxPlayers { get; }
        public GameStatus Status { get; }

        /// <summary>Key the client uses to look up the game's art, colors and fonts.</summary>
        public string ThemeKey { get; }

        public bool SupportsPlayerCount(int count) => count >= MinPlayers && count <= MaxPlayers;

        public override string ToString() => $"{DisplayName} ({Id}, {MinPlayers}-{MaxPlayers} players, {Status})";
    }
}
