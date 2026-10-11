using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>
    /// The catalog the lounge reads. Playable games are registered with their module; games that
    /// are announced but not built yet are listed with just their <see cref="GameInfo"/>.
    /// Registering a new game never requires changing an existing one.
    /// </summary>
    public sealed class GameRegistry
    {
        private readonly List<GameInfo> order = new List<GameInfo>();
        private readonly Dictionary<string, GameInfo> infos = new Dictionary<string, GameInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, IGameModule> modules = new Dictionary<string, IGameModule>(StringComparer.Ordinal);

        /// <summary>All games in the order they were added (the library's default order).</summary>
        public IReadOnlyList<GameInfo> All => order;

        /// <summary>Adds a game that can be played. Its id must be new.</summary>
        public void Register(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            var info = module.Info ?? throw new ArgumentException("The module has no GameInfo.", nameof(module));
            Add(info);
            modules[info.Id] = module;
        }

        /// <summary>Lists a game that has no module yet, such as one marked "Coming soon".</summary>
        public void Announce(GameInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (info.Status == GameStatus.Playable)
                throw new ArgumentException($"{info.DisplayName} is marked Playable, so register its module instead.", nameof(info));
            Add(info);
        }

        public bool TryGetInfo(string id, out GameInfo info) => infos.TryGetValue(id ?? string.Empty, out info);

        /// <summary>The module for a game, if it has one. Announced-only games return false.</summary>
        public bool TryGetModule(string id, out IGameModule module) => modules.TryGetValue(id ?? string.Empty, out module);

        private void Add(GameInfo info)
        {
            if (infos.ContainsKey(info.Id))
                throw new InvalidOperationException($"A game with id \"{info.Id}\" is already in the registry.");
            infos.Add(info.Id, info);
            order.Add(info);
        }
    }
}
