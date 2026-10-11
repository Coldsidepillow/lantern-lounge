using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using LanternLounge.Games.PrismRush;
using LanternLounge.Games.TreehouseHoldem;
using LanternLounge.Games.Tidebound;

namespace LanternLounge.Client
{
    /// <summary>
    /// The one place games are added to the app. A new game = one line here plus its module;
    /// nothing else in the client changes.
    /// </summary>
    public static class GameCatalog
    {
        public const string TideboundId = TideboundModule.GameId;
        public const string TreehouseHoldemId = HoldemModule.GameId;
        public const string PrismRushId = PrismModule.GameId;

        public static GameRegistry Create()
        {
            var registry = new GameRegistry();
            registry.Register(new TideboundModule());
            registry.Register(new PrismModule());
            registry.Register(new PebbleDropModule());
            registry.Register(new HoldemModule());
            return registry;
        }

        /// <summary>The short category line under a game's name in the lounge.</summary>
        public static string CategoryOf(string gameId)
        {
            switch (gameId)
            {
                case TideboundId:
                case PrismRushId:
                case TreehouseHoldemId:
                    return "Card game";
                default:
                    return "Strategy";
            }
        }
    }
}
