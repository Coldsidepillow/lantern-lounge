using System;
using System.Collections.Generic;

namespace LanternLounge.Games.Tidebound
{
    /// <summary>The six creature families. Each has its own color and symbol in the client.</summary>
    public enum Family
    {
        Reef,
        Drift,
        Shallows,
        Glow,
        Kelpwood,
        Abyss
    }

    /// <summary>The four tides of every creature. Any two of the same creature make a pod.</summary>
    public enum Tide
    {
        Dawn,
        Noon,
        Dusk,
        Night
    }

    public sealed class CreatureInfo
    {
        internal CreatureInfo(int id, string name, Family family, string trait)
        {
            Id = id;
            Name = name;
            Family = family;
            Trait = trait;
        }

        public int Id { get; }
        public string Name { get; }
        public Family Family { get; }

        /// <summary>The one-line fact shown on the card and in the Field Log.</summary>
        public string Trait { get; }
    }

    /// <summary>
    /// The Tidebound deck: 16 creatures × 4 tides = 64 Tide Tablets.
    /// A card is a number 0..63: creature = card / 4, tide = card % 4.
    /// </summary>
    public static class TideboundCards
    {
        public const int TidesPerCreature = 4;
        public const int CreatureCount = 16;
        public const int DeckSize = CreatureCount * TidesPerCreature;

        /// <summary>Two tides of the same creature make a pod.</summary>
        public const int PodSize = 2;

        /// <summary>Pods in a full game: 64 cards / 2 = 32 (two per creature).</summary>
        public const int PodCount = DeckSize / PodSize;

        private static readonly CreatureInfo[] AllCreatures =
        {
            new CreatureInfo(0, "Brinekin", Family.Reef, "Grows its own coral crown."),
            new CreatureInfo(1, "Pinsnap Shrimp", Family.Reef, "Snaps its claws to say hello."),
            new CreatureInfo(2, "Glimmerjelly", Family.Drift, "Glows a little brighter every time it giggles."),
            new CreatureInfo(3, "Mistfin Wisp", Family.Drift, "Drifts wherever the current hums."),
            new CreatureInfo(4, "Puffcap Ray", Family.Shallows, "Wears a sun-yellow cap."),
            new CreatureInfo(5, "Ripple Seahare", Family.Shallows, "Leaves rings of ripples wherever it hops."),
            new CreatureInfo(6, "Lanternmoss", Family.Glow, "A snail carrying its own night-light."),
            new CreatureInfo(7, "Starspeck Squid", Family.Glow, "Freckled with tiny stars."),
            new CreatureInfo(8, "Barnacle Owlet", Family.Kelpwood, "A wise, sleepy kelp-forest lookout."),
            new CreatureInfo(9, "Kelpwhisk", Family.Abyss, "A ribbon eel with leafy fins."),
            new CreatureInfo(10, "Bubblebeak Puffer", Family.Reef, "Puffs up round when it laughs."),
            new CreatureInfo(11, "Cloudfin Turtle", Family.Drift, "Naps while the current carries it."),
            new CreatureInfo(12, "Sandsprite Starfish", Family.Shallows, "Waves hello with all five arms."),
            new CreatureInfo(13, "Twinkle Urchin", Family.Glow, "Every spine glows a different color."),
            new CreatureInfo(14, "Kelpcap Octopus", Family.Kelpwood, "Wears a leafy cap and gives eight-armed hugs."),
            new CreatureInfo(15, "Glowbell Angler", Family.Abyss, "Carries a lantern to light the deep.")
        };

        private static readonly IReadOnlyList<CreatureInfo> ReadOnlyCreatures = Array.AsReadOnly(AllCreatures);

        public static IReadOnlyList<CreatureInfo> Creatures => ReadOnlyCreatures;

        public static CreatureInfo Creature(int creatureId)
        {
            if (creatureId < 0 || creatureId >= CreatureCount) throw new ArgumentOutOfRangeException(nameof(creatureId));
            return AllCreatures[creatureId];
        }

        public static int CardId(int creatureId, Tide tide)
        {
            if (creatureId < 0 || creatureId >= CreatureCount) throw new ArgumentOutOfRangeException(nameof(creatureId));
            return creatureId * TidesPerCreature + (int)tide;
        }

        public static int CreatureOf(int card) => card / TidesPerCreature;

        public static Tide TideOf(int card) => (Tide)(card % TidesPerCreature);

        public static bool IsValidCard(int card) => card >= 0 && card < DeckSize;
    }
}
