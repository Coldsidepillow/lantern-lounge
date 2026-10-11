namespace LanternLounge.Games.Tidebound
{
    public enum TideEventKind
    {
        /// <summary>Seat asked TargetSeat for Creature and received Count cards (0 = "Dive!").</summary>
        Asked,

        /// <summary>
        /// Seat dove and drew a card (Count = 1) that was not the creature they asked for.
        /// Which card is never shown in the event (the diver sees it in their own hand).
        /// </summary>
        Dove,

        /// <summary>Seat's dive drew the creature they asked for (Card, shown to all), so they go again.</summary>
        LuckyDive,

        /// <summary>Seat was told "Dive!" but the Current was empty, so the tide passed.</summary>
        CurrentEmpty,

        /// <summary>Seat started a tide with no cards and drew one. Card is hidden from others.</summary>
        Refilled,

        /// <summary>No other diver had cards, so Seat drew one and passed. Card is hidden from others.</summary>
        NoOneToAsk,

        /// <summary>Seat paired two tides of Creature into a pod.</summary>
        PodFormed,

        /// <summary>Every pod has been claimed.</summary>
        GameOver
    }

    /// <summary>Something that happened at the table, in the order it happened.</summary>
    public readonly struct TideEvent
    {
        public const int Hidden = -1;

        public TideEvent(TideEventKind kind, int seat, int targetSeat = -1, int creature = -1, int count = 0, int card = Hidden)
        {
            Kind = kind;
            Seat = seat;
            TargetSeat = targetSeat;
            Creature = creature;
            Count = count;
            Card = card;
        }

        public TideEventKind Kind { get; }
        public int Seat { get; }
        public int TargetSeat { get; }
        public int Creature { get; }
        public int Count { get; }

        /// <summary>The card involved, or <see cref="Hidden"/> when this viewer may not see it.</summary>
        public int Card { get; }

        /// <summary>Hides the drawn card and its creature, which would reveal the card too.</summary>
        internal TideEvent WithCardHidden() => new TideEvent(Kind, Seat, TargetSeat, -1, Count, Hidden);

        /// <summary>Only the diver sees a drawn card, unless it was a lucky dive (shown to everyone).</summary>
        internal bool IsCardPrivate =>
            Kind == TideEventKind.Dove || Kind == TideEventKind.Refilled || Kind == TideEventKind.NoOneToAsk;
    }
}
