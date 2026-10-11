using System.Text.Json;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using LanternLounge.Games.PrismRush;
using LanternLounge.Games.Tidebound;
using LanternLounge.Games.TreehouseHoldem;

namespace LanternLounge.Server;

/// <summary>
/// Connects one game's shared rules to the server: how many seats the online table has,
/// how to read a move from JSON, what each player is allowed to see, and how a bot plays.
/// The rules themselves always come from the game's module, the same code the app runs.
/// </summary>
public abstract class GameAdapter
{
    public abstract string GameId { get; }
    public abstract IGameModule Module { get; }

    /// <summary>Seats at an online table. Empty seats are filled by bots when the game starts.</summary>
    public virtual int Seats => Module.Info.MaxPlayers;

    public virtual MatchConfig Config(ulong seed) => new MatchConfig(Seats, seed);

    /// <summary>Turns the "move" object a player sent into this game's action type.</summary>
    public abstract bool TryParseMove(JsonElement move, out object action, out string error);

    /// <summary>What one seat may see, as an object that serializes to JSON.</summary>
    public abstract object ViewFor(object state, int seat);

    /// <summary>A computer player for one seat.</summary>
    public abstract BotPlayer CreateBot(int seat, ulong seed);

    /// <summary>Some moments move on by themselves (e.g. Treehouse Hold'em between rounds).</summary>
    public virtual TimeSpan? AutoAdvanceDelay(object state) => null;

    protected static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object &&
               obj.TryGetProperty(name, out var p) &&
               p.ValueKind == JsonValueKind.Number &&
               p.TryGetInt32(out value);
    }

    protected static string GetString(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }
}

/// <summary>A computer player sitting in one seat.</summary>
public abstract class BotPlayer
{
    /// <summary>Called after every accepted move, so bots that remember things can learn.</summary>
    public virtual void Observe(object state) { }

    public abstract object ChooseMove(object state);
}

public static class GameCatalog
{
    private static readonly Dictionary<string, GameAdapter> Adapters = new GameAdapter[]
    {
        new PebbleDropAdapter(),
        new TideboundAdapter(),
        new PrismRushAdapter(),
        new HoldemAdapter()
    }.ToDictionary(a => a.GameId);

    public static IEnumerable<GameAdapter> All => Adapters.Values;

    public static bool TryGet(string gameId, out GameAdapter adapter)
    {
        adapter = null;
        return gameId != null && Adapters.TryGetValue(gameId, out adapter);
    }
}

// ---------------- Pebble Drop ----------------

public sealed class PebbleDropAdapter : GameAdapter
{
    private readonly PebbleDropModule module = new PebbleDropModule();
    public override string GameId => PebbleDropModule.GameId;
    public override IGameModule Module => module;

    /// <summary>Move: { "column": 0-15 }</summary>
    public override bool TryParseMove(JsonElement move, out object action, out string error)
    {
        action = null;
        error = null;
        if (!TryGetInt(move, "column", out int column))
        {
            error = "A Pebble Drop move needs a column number.";
            return false;
        }
        action = new DropAction(column);
        return true;
    }

    public override object ViewFor(object state, int seat)
    {
        var s = (PebbleDropState)state;
        var cells = new int[PebbleDropState.CellCount];
        for (int r = 0; r < PebbleDropState.Rows; r++)
        {
            for (int c = 0; c < PebbleDropState.Columns; c++) cells[PebbleDropState.Index(r, c)] = s.CellAt(r, c);
        }
        return new
        {
            columns = PebbleDropState.Columns,
            rows = PebbleDropState.Rows,
            cells,
            currentSeat = s.CurrentSeat,
            firstSeat = s.FirstSeat,
            moveCount = s.MoveCount,
            winner = s.Winner,
            winningCells = s.WinningCells,
            isDraw = s.IsDraw,
            lastCell = s.LastCell
        };
    }

    public override BotPlayer CreateBot(int seat, ulong seed) => new Bot(seat, seed);

    private sealed class Bot : BotPlayer
    {
        private readonly int seat;
        private readonly SeededRandom random;

        public Bot(int seat, ulong seed)
        {
            this.seat = seat;
            random = new SeededRandom(seed);
        }

        public override object ChooseMove(object state) =>
            PebbleDropBot.ChooseMove((PebbleDropState)state, seat, random, BotLevel.Normal);
    }
}

// ---------------- Tidebound ----------------

public sealed class TideboundAdapter : GameAdapter
{
    private readonly TideboundModule module = new TideboundModule();
    public override string GameId => TideboundModule.GameId;
    public override IGameModule Module => module;

    /// <summary>Move: { "target": seat, "creature": 0-15 }</summary>
    public override bool TryParseMove(JsonElement move, out object action, out string error)
    {
        action = null;
        error = null;
        if (!TryGetInt(move, "target", out int target) || !TryGetInt(move, "creature", out int creature))
        {
            error = "A Tidebound move needs a target seat and a creature.";
            return false;
        }
        action = new AskAction(target, creature);
        return true;
    }

    public override object ViewFor(object state, int seat) => module.ViewFor((TideboundState)state, seat);

    public override BotPlayer CreateBot(int seat, ulong seed) => new Bot(module, seat, Seats, seed);

    private sealed class Bot : BotPlayer
    {
        private readonly TideboundModule module;
        private readonly TideboundBot bot;
        private readonly int seat;

        public Bot(TideboundModule module, int seat, int players, ulong seed)
        {
            this.module = module;
            this.seat = seat;
            bot = new TideboundBot(seat, players, new SeededRandom(seed));
        }

        public override void Observe(object state) => bot.Observe(module.ViewFor((TideboundState)state, seat));

        public override object ChooseMove(object state) => bot.ChooseAsk(module.ViewFor((TideboundState)state, seat));
    }
}

// ---------------- Prism Rush ----------------

public sealed class PrismRushAdapter : GameAdapter
{
    private readonly PrismModule module = new PrismModule();
    public override string GameId => PrismModule.GameId;
    public override IGameModule Module => module;

    /// <summary>Move: { "kind": "play", "cards": [ids], "color": 0-3 (Prism only) } or { "kind": "draw" } or { "kind": "keep" }</summary>
    public override bool TryParseMove(JsonElement move, out object action, out string error)
    {
        action = null;
        error = null;
        switch (GetString(move, "kind"))
        {
            case "draw":
                action = PrismMove.Draw();
                return true;
            case "keep":
                action = PrismMove.Keep();
                return true;
            case "play":
                if (!move.TryGetProperty("cards", out var cardsElement) || cardsElement.ValueKind != JsonValueKind.Array ||
                    cardsElement.GetArrayLength() == 0 || cardsElement.GetArrayLength() > PrismCards.DeckSize)
                {
                    error = "Pick at least one card to play.";
                    return false;
                }
                var cards = new List<int>();
                foreach (var c in cardsElement.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out int id))
                    {
                        error = "Cards are sent as numbers.";
                        return false;
                    }
                    cards.Add(id);
                }
                int color = TryGetInt(move, "color", out int chosen) ? chosen : -1;
                action = PrismMove.PlayCards(cards, color);
                return true;
            default:
                error = "A Prism Rush move is play, draw or keep.";
                return false;
        }
    }

    public override object ViewFor(object state, int seat) => module.ViewFor((PrismState)state, seat);

    public override BotPlayer CreateBot(int seat, ulong seed) => new Bot(module, seat);

    private sealed class Bot : BotPlayer
    {
        private readonly PrismModule module;
        private readonly int seat;

        public Bot(PrismModule module, int seat)
        {
            this.module = module;
            this.seat = seat;
        }

        public override object ChooseMove(object state) => PrismBot.ChooseMove(module.ViewFor((PrismState)state, seat));
    }
}

// ---------------- Treehouse Hold'em ----------------

public sealed class HoldemAdapter : GameAdapter
{
    private readonly HoldemModule module = new HoldemModule();
    public override string GameId => HoldemModule.GameId;
    public override IGameModule Module => module;

    /// <summary>Move: { "kind": "fold" | "check" | "call" | "raise" | "next" }</summary>
    public override bool TryParseMove(JsonElement move, out object action, out string error)
    {
        action = null;
        error = null;
        switch (GetString(move, "kind"))
        {
            case "fold": action = HoldemMove.Fold; return true;
            case "check": action = HoldemMove.Check; return true;
            case "call": action = HoldemMove.Call; return true;
            case "raise": action = HoldemMove.Raise; return true;
            case "next": action = HoldemMove.NextRound; return true;
            default:
                error = "A Treehouse Hold'em move is fold, check, call, raise or next.";
                return false;
        }
    }

    public override object ViewFor(object state, int seat)
    {
        var view = module.ViewFor((HoldemState)state, seat);
        // Cards each player may see: always your own, others' only once shown at the showdown.
        var cardsBySeat = new IReadOnlyList<int>[view.PlayerCount];
        for (int s = 0; s < view.PlayerCount; s++) cardsBySeat[s] = view.CardsOf(s);
        return new { view, cardsBySeat };
    }

    public override BotPlayer CreateBot(int seat, ulong seed) => new Bot(module, seat, seed);

    /// <summary>Between rounds, the next round starts by itself after a short pause.</summary>
    public override TimeSpan? AutoAdvanceDelay(object state) =>
        ((HoldemState)state).Phase == Phase.RoundOver ? TimeSpan.FromSeconds(8) : null;

    private sealed class Bot : BotPlayer
    {
        private readonly HoldemModule module;
        private readonly HoldemBot bot;
        private readonly int seat;

        public Bot(HoldemModule module, int seat, ulong seed)
        {
            this.module = module;
            this.seat = seat;
            bot = new HoldemBot(seed, 0.35f + 0.1f * seat);
        }

        public override object ChooseMove(object state) => bot.ChooseMove(module.ViewFor((HoldemState)state, seat));
    }
}
