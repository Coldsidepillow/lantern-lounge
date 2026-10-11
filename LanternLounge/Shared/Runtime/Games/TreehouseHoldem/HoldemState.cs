using System;
using System.Collections.Generic;

namespace LanternLounge.Games.TreehouseHoldem
{
    public enum Street
    {
        /// <summary>Everyone has two secret cards; nothing in the middle yet.</summary>
        Start = 0,

        /// <summary>Three shared cards in the middle.</summary>
        Flop = 1,

        /// <summary>A fourth shared card.</summary>
        Turn = 2,

        /// <summary>The fifth and last shared card.</summary>
        River = 3
    }

    public enum Phase
    {
        /// <summary>Someone is choosing Check, Call, Raise or Fold.</summary>
        Betting,

        /// <summary>The round is over and coins are handed out; anyone may press "Next round".</summary>
        RoundOver,

        /// <summary>Game night is over.</summary>
        GameOver
    }

    public enum MoveKind
    {
        /// <summary>Give up this round's cards (only when there is something to match).</summary>
        Fold,

        /// <summary>Stay in for free (when nobody has raised).</summary>
        Check,

        /// <summary>Match the coins someone else raised.</summary>
        Call,

        /// <summary>Add more coins: 5 in the first two rounds of betting, 10 in the last two.</summary>
        Raise,

        /// <summary>Start the next round once everyone has seen who won.</summary>
        NextRound
    }

    /// <summary>A Treehouse Hold'em move. Use the static instances.</summary>
    public sealed class HoldemMove
    {
        public static readonly HoldemMove Fold = new HoldemMove(MoveKind.Fold);
        public static readonly HoldemMove Check = new HoldemMove(MoveKind.Check);
        public static readonly HoldemMove Call = new HoldemMove(MoveKind.Call);
        public static readonly HoldemMove Raise = new HoldemMove(MoveKind.Raise);
        public static readonly HoldemMove NextRound = new HoldemMove(MoveKind.NextRound);

        private HoldemMove(MoveKind kind)
        {
            Kind = kind;
        }

        public MoveKind Kind { get; }

        public override string ToString() => Kind.ToString();
    }

    public enum HoldemEventKind
    {
        /// <summary>A new round began. Seat = dealer, Amount = round number.</summary>
        RoundStarted,

        /// <summary>Seat put Amount coins in the middle to start the round.</summary>
        Ante,

        /// <summary>Seat got their two secret cards (not shown here).</summary>
        Dealt,

        Folded,
        Checked,

        /// <summary>Seat matched the bet with Amount coins.</summary>
        Called,

        /// <summary>Seat added Amount coins; Extra = the new amount to match this betting round.</summary>
        Raised,

        /// <summary>Seat has put in all their coins.</summary>
        AllIn,

        /// <summary>Shared cards went in the middle. Extra = the Street; Cards = the new cards.</summary>
        SharedCards,

        /// <summary>Everyone still in shows their cards.</summary>
        Showdown,

        /// <summary>Seat won Amount coins. Extra = their HandCategory, or -1 if everyone else folded.</summary>
        WonCoins,

        /// <summary>Seat ran out of coins and will cheer from the side.</summary>
        OutOfCoins,

        GameOver
    }

    public readonly struct HoldemEvent
    {
        private static readonly IReadOnlyList<int> NoCards = Array.Empty<int>();

        public HoldemEvent(HoldemEventKind kind, int seat, int amount = 0, int extra = 0, IReadOnlyList<int> cards = null)
        {
            Kind = kind;
            Seat = seat;
            Amount = amount;
            Extra = extra;
            Cards = cards ?? NoCards;
        }

        public HoldemEventKind Kind { get; }
        public int Seat { get; }
        public int Amount { get; }
        public int Extra { get; }
        public IReadOnlyList<int> Cards { get; }
    }

    /// <summary>
    /// The full, authoritative table, including everyone's secret cards and the deck order.
    /// Never show this to a player: use <see cref="HoldemView"/> from <see cref="HoldemModule.ViewFor"/>.
    /// Immutable once built.
    /// </summary>
    public sealed class HoldemState
    {
        public const int NoSeat = -1;

        private readonly IReadOnlyList<int>[] hole;

        internal HoldemState(HoldemTable t, HoldemEvent[] events)
        {
            PlayerCount = t.Players;
            Coins = Copy(t.Coins);
            hole = new IReadOnlyList<int>[PlayerCount];
            for (int s = 0; s < PlayerCount; s++) hole[s] = Array.AsReadOnly(t.Hole[s].ToArray());
            Board = Array.AsReadOnly(t.Board.ToArray());
            Deck = Array.AsReadOnly(t.Deck.ToArray());
            InRound = Copy(t.InRound);
            Folded = Copy(t.Folded);
            Revealed = Copy(t.Revealed);
            Acted = Copy(t.Acted);
            RoundBet = Copy(t.RoundBet);
            TotalIn = Copy(t.TotalIn);
            CurrentBet = t.CurrentBet;
            RaisesThisStreet = t.Raises;
            Street = t.Street;
            Phase = t.Phase;
            Dealer = t.Dealer;
            CurrentSeat = t.Seat;
            RoundNumber = t.RoundNumber;
            Rounds = t.Rounds;
            StartingCoins = t.StartingCoins;
            RandomState = t.Random.State;
            Winners = Array.AsReadOnly(t.Winners.ToArray());
            LastEvents = Array.AsReadOnly(events);
        }

        public int PlayerCount { get; }
        public IReadOnlyList<int> Coins { get; }
        public IReadOnlyList<int> Board { get; }

        /// <summary>The rest of the deck, top first. Server/authority use only.</summary>
        public IReadOnlyList<int> Deck { get; }

        /// <summary>Was this seat dealt in this round (it had coins)?</summary>
        public IReadOnlyList<bool> InRound { get; }
        public IReadOnlyList<bool> Folded { get; }

        /// <summary>Cards shown at the showdown.</summary>
        public IReadOnlyList<bool> Revealed { get; }
        public IReadOnlyList<bool> Acted { get; }

        /// <summary>Coins each seat has put in during this betting round.</summary>
        public IReadOnlyList<int> RoundBet { get; }

        /// <summary>Coins each seat has put in the middle this whole round (including the start coins).</summary>
        public IReadOnlyList<int> TotalIn { get; }

        /// <summary>How many coins everyone must have put in this betting round to stay in.</summary>
        public int CurrentBet { get; }
        public int RaisesThisStreet { get; }
        public Street Street { get; }
        public Phase Phase { get; }
        public int Dealer { get; }

        /// <summary>Whose turn it is to bet, or <see cref="NoSeat"/> between rounds.</summary>
        public int CurrentSeat { get; }

        /// <summary>1 for the first round of the night.</summary>
        public int RoundNumber { get; }
        public int Rounds { get; }
        public int StartingCoins { get; }
        public ulong RandomState { get; }

        /// <summary>Set when the game is over: the seats with the most coins.</summary>
        public IReadOnlyList<int> Winners { get; }
        public IReadOnlyList<HoldemEvent> LastEvents { get; }

        public bool IsOver => Phase == Phase.GameOver;

        public int Pot
        {
            get
            {
                int pot = 0;
                for (int s = 0; s < PlayerCount; s++) pot += TotalIn[s];
                return pot;
            }
        }

        public IReadOnlyList<int> HoleCardsOf(int seat)
        {
            if (seat < 0 || seat >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(seat));
            return hole[seat];
        }

        private static IReadOnlyList<T> Copy<T>(T[] values) => Array.AsReadOnly((T[])values.Clone());
    }

    /// <summary>
    /// What one player may see: their own two cards, the shared cards, everyone's coins and bets,
    /// and other players' cards only once they are shown at the showdown.
    /// </summary>
    public sealed class HoldemView
    {
        private static readonly IReadOnlyList<int> Hidden = Array.Empty<int>();
        private readonly IReadOnlyList<int>[] shown;

        internal HoldemView(HoldemState state, int viewer)
        {
            Viewer = viewer;
            PlayerCount = state.PlayerCount;
            shown = new IReadOnlyList<int>[PlayerCount];
            for (int s = 0; s < PlayerCount; s++)
            {
                shown[s] = s == viewer || state.Revealed[s] ? state.HoleCardsOf(s) : Hidden;
            }
            MyCards = shown[viewer];
            Board = state.Board;
            Coins = state.Coins;
            InRound = state.InRound;
            Folded = state.Folded;
            Revealed = state.Revealed;
            RoundBet = state.RoundBet;
            TotalIn = state.TotalIn;
            CurrentBet = state.CurrentBet;
            Street = state.Street;
            Phase = state.Phase;
            Dealer = state.Dealer;
            CurrentSeat = state.CurrentSeat;
            RoundNumber = state.RoundNumber;
            Rounds = state.Rounds;
            Winners = state.Winners;
            LastEvents = state.LastEvents;
            Pot = state.Pot;

            bool myTurn = state.Phase == Phase.Betting && state.CurrentSeat == viewer;
            CostToCall = Math.Max(0, state.CurrentBet - state.RoundBet[viewer]);
            RaiseSize = HoldemModule.RaiseSizeFor(state.Street);
            CanCheck = myTurn && CostToCall == 0;
            CanCall = myTurn && CostToCall > 0;
            CanFold = myTurn && CostToCall > 0;
            CanRaise = myTurn && state.RaisesThisStreet < HoldemModule.MaxRaisesPerStreet && state.Coins[viewer] > CostToCall &&
                       HoldemModule.SomeoneCanAnswer(state, viewer);
        }

        public int Viewer { get; }
        public int PlayerCount { get; }
        public IReadOnlyList<int> MyCards { get; }
        public IReadOnlyList<int> Board { get; }
        public IReadOnlyList<int> Coins { get; }
        public IReadOnlyList<bool> InRound { get; }
        public IReadOnlyList<bool> Folded { get; }
        public IReadOnlyList<bool> Revealed { get; }
        public IReadOnlyList<int> RoundBet { get; }
        public IReadOnlyList<int> TotalIn { get; }
        public int CurrentBet { get; }
        public Street Street { get; }
        public Phase Phase { get; }
        public int Dealer { get; }
        public int CurrentSeat { get; }
        public int RoundNumber { get; }
        public int Rounds { get; }
        public IReadOnlyList<int> Winners { get; }
        public IReadOnlyList<HoldemEvent> LastEvents { get; }
        public int Pot { get; }

        /// <summary>Coins you would need to put in to call.</summary>
        public int CostToCall { get; }

        /// <summary>How many coins a raise adds in this betting round.</summary>
        public int RaiseSize { get; }
        public bool CanCheck { get; }
        public bool CanCall { get; }
        public bool CanFold { get; }
        public bool CanRaise { get; }

        public bool IsMyTurn => Phase == Phase.Betting && CurrentSeat == Viewer;
        public bool IsOver => Phase == Phase.GameOver;

        /// <summary>Still in this round: dealt in and not folded.</summary>
        public bool IsLive(int seat) => InRound[seat] && !Folded[seat];

        /// <summary>A seat's cards: always your own, others' only after they are shown.</summary>
        public IReadOnlyList<int> CardsOf(int seat) => shown[seat];

        /// <summary>How many other players are still in this round.</summary>
        public int LiveOpponents
        {
            get
            {
                int n = 0;
                for (int s = 0; s < PlayerCount; s++)
                {
                    if (s != Viewer && IsLive(s)) n++;
                }
                return n;
            }
        }

        /// <summary>Your best hand right now from your cards and the shared cards, if you have cards.</summary>
        public bool TryGetMyBestHand(out HandValue value)
        {
            value = default;
            if (MyCards.Count == 0) return false;
            var all = new List<int>(MyCards);
            all.AddRange(Board);
            value = HandEvaluator.Evaluate(all);
            return true;
        }
    }
}
