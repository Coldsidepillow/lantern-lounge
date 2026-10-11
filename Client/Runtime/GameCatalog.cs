using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;

namespace LanternLounge.Client
{
    /// <summary>
    /// The one place games are added to the app. A new game = one line here plus its module;
    /// nothing else in the client changes.
    /// </summary>
    public static class GameCatalog
    {
        public const string TideboundId = "tidebound";
        public const string FoglineFleetId = "fogline-fleet";
        public const string PrismRushId = "prism-rush";

        public static GameRegistry Create()
        {
            var registry = new GameRegistry();

            // Tidebound is the featured game; its rules module is the next thing to build.
            registry.Announce(new GameInfo(TideboundId, "Tidebound", 2, 4, GameStatus.Preview, "tidebound"));
            registry.Register(new PebbleDropModule());
            registry.Announce(new GameInfo(FoglineFleetId, "Fogline Fleet", 2, 2, GameStatus.ComingSoon, "fogline-fleet"));
            registry.Announce(new GameInfo(PrismRushId, "Prism Rush", 2, 4, GameStatus.ComingSoon, "prism-rush"));

            return registry;
        }

        /// <summary>The short category line under a game's name in the lounge.</summary>
        public static string CategoryOf(string gameId)
        {
            switch (gameId)
            {
                case TideboundId:
                case PrismRushId:
                    return "Card game";
                default:
                    return "Strategy";
            }
        }
    }
}
