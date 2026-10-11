using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using LanternLounge.Core;

namespace LanternLounge.Server;

/// <summary>One seat at a table: a person (who may be briefly disconnected) or a bot.</summary>
public sealed class Seat
{
    public int Index { get; init; }
    public string Name { get; set; }
    public string Token { get; set; }
    public Connection Connection { get; set; }
    public bool IsBot { get; set; }
    public BotPlayer Bot { get; set; }
    public DateTime DisconnectedAt { get; set; }

    public bool IsHuman => Token != null;
    public bool IsConnected => Connection != null && Connection.IsOpen;
}

public enum RoomStatus
{
    Lobby,
    Playing,
    Finished
}

/// <summary>
/// A room: up to four seats, a join code, and one match at a time. Every change happens
/// inside <see cref="Gate"/>, so moves are applied one after another in order.
/// </summary>
public sealed class Room
{
    public static readonly TimeSpan BotDelay = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(60);

    private readonly ILogger log;
    private int version; // bumps on every change, so stale timers do nothing

    public Room(string code, GameAdapter game, ILogger log)
    {
        Code = code;
        Game = game;
        this.log = log;
        Seats = Enumerable.Range(0, game.Seats).Select(i => new Seat { Index = i }).ToArray();
        LastActivity = DateTime.UtcNow;
    }

    public string Code { get; }
    public GameAdapter Game { get; }
    public Seat[] Seats { get; }
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);
    public RoomStatus Status { get; private set; } = RoomStatus.Lobby;
    public int HostSeat { get; private set; }
    public object State { get; private set; }
    public DateTime LastActivity { get; private set; }
    public int MatchNumber { get; private set; }

    public bool HasHumans => Seats.Any(s => s.IsHuman);
    public bool HasConnectedHumans => Seats.Any(s => s.IsHuman && s.IsConnected);

    // ---------- Seats (call inside Gate) ----------

    /// <summary>Puts a person in the first free seat. Returns null if the table is full.</summary>
    public Seat Sit(string name, Connection connection)
    {
        var seat = Seats.FirstOrDefault(s => !s.IsHuman);
        if (seat == null) return null;
        seat.Name = name;
        seat.Token = NewToken();
        seat.Connection = connection;
        seat.IsBot = false;
        seat.Bot = null;
        if (!Seats.Any(s => s.IsHuman && s != seat)) HostSeat = seat.Index;
        Touch();
        return seat;
    }

    public Seat FindByToken(string token) =>
        token == null ? null : Seats.FirstOrDefault(s => s.Token != null && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(s.Token), System.Text.Encoding.UTF8.GetBytes(token)));

    /// <summary>A person comes back (same phone, new connection): they get their seat back from the bot.</summary>
    public void Reconnect(Seat seat, Connection connection)
    {
        var old = seat.Connection;
        if (old != null && old != connection)
        {
            // The old connection no longer speaks for this seat.
            old.Room = null;
            old.Seat = null;
        }
        seat.Connection = connection;
        if (seat.IsBot && Status != RoomStatus.Lobby)
        {
            seat.IsBot = false;
            seat.Bot = null;
        }
        Touch();
    }

    /// <summary>A person leaves for good. In a game a bot takes over their seat.</summary>
    public void Leave(Seat seat)
    {
        string name = seat.Name;
        seat.Token = null;
        seat.Connection = null;
        if (Status == RoomStatus.Lobby)
        {
            seat.Name = null;
        }
        else
        {
            seat.Name = BotName(seat.Index);
            MakeBot(seat);
        }
        if (HostSeat == seat.Index)
        {
            var next = Seats.FirstOrDefault(s => s.IsHuman);
            if (next != null) HostSeat = next.Index;
        }
        log.LogInformation("Room {Code}: {Name} left seat {Seat}", Code, name, seat.Index);
        Touch();
    }

    /// <summary>A person whose connection dropped and didn't come back in time is replaced by a bot.</summary>
    public bool ReplaceStaleSeats(DateTime now)
    {
        bool changed = false;
        foreach (var seat in Seats)
        {
            // Only seats whose connection has really closed (DisconnectedAt is set at that moment).
            if (!seat.IsHuman || seat.Connection != null || seat.IsBot) continue;
            if (now - seat.DisconnectedAt < ReconnectGrace) continue;
            if (Status == RoomStatus.Lobby)
            {
                Leave(seat);
            }
            else
            {
                // Keep their token so they can still come back and take the seat again.
                MakeBot(seat);
                if (HostSeat == seat.Index)
                {
                    var next = Seats.FirstOrDefault(s => s.IsHuman && s.IsConnected);
                    if (next != null) HostSeat = next.Index;
                }
            }
            changed = true;
        }
        if (changed) Touch();
        return changed;
    }

    // ---------- The match (call inside Gate) ----------

    public void Start()
    {
        var seed = RandomSeed();
        foreach (var seat in Seats)
        {
            seat.Bot = null;
            if (!seat.IsHuman)
            {
                seat.Name = BotName(seat.Index);
                seat.IsBot = true;
            }
        }
        State = Game.Module.Setup(Game.Config(seed));
        Status = RoomStatus.Playing;
        MatchNumber++;
        foreach (var seat in Seats.Where(s => s.IsBot)) seat.Bot = Game.CreateBot(seat.Index, seed + (ulong)seat.Index * 7919UL);
        ObserveAll();
        log.LogInformation("Room {Code}: {Game} match {Match} started", Code, Game.GameId, MatchNumber);
        Touch();
    }

    /// <summary>Validates and applies a move. Returns null on success, or the reason it was refused.</summary>
    public ValidationResult? TryMove(int seat, object action)
    {
        if (Status != RoomStatus.Playing || State == null)
            return ValidationResult.Fail(ErrorCodes.GameOver, "The game isn't running.");
        var check = Game.Module.Validate(State, seat, action);
        if (!check.Ok) return check;
        State = Game.Module.Apply(State, seat, action);
        ObserveAll();
        if (Game.Module.Result(State).IsOver) Status = RoomStatus.Finished;
        Touch();
        return null;
    }

    public int Version => version;

    /// <summary>The version a timer was last set for, so each change gets exactly one timer.</summary>
    public int ScheduledFor { get; set; } = -1;

    private void ObserveAll()
    {
        foreach (var seat in Seats)
        {
            if (seat.IsBot && seat.Bot != null) seat.Bot.Observe(State);
        }
    }

    private void MakeBot(Seat seat)
    {
        seat.IsBot = true;
        if (State != null && seat.Bot == null)
        {
            seat.Bot = Game.CreateBot(seat.Index, RandomSeed());
            seat.Bot.Observe(State);
        }
    }

    private void Touch()
    {
        LastActivity = DateTime.UtcNow;
        Interlocked.Increment(ref version);
    }

    public static string BotName(int seat) => $"Bot {seat + 1}";

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static ulong RandomSeed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8), 0);
}

/// <summary>All rooms on this server, by join code. Rooms live in memory only.</summary>
public sealed class RoomManager
{
    // No 0/O, 1/I/L, so codes are easy to read out loud.
    private const string CodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;
    public const int MaxRooms = 2000;

    private readonly ConcurrentDictionary<string, Room> rooms = new ConcurrentDictionary<string, Room>();
    private readonly ILogger<RoomManager> log;

    public RoomManager(ILogger<RoomManager> log)
    {
        this.log = log;
    }

    public int Count => rooms.Count;

    public IEnumerable<Room> All => rooms.Values;

    public Room Create(GameAdapter game)
    {
        if (rooms.Count >= MaxRooms) return null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var code = new string(Enumerable.Range(0, CodeLength)
                .Select(_ => CodeLetters[RandomNumberGenerator.GetInt32(CodeLetters.Length)]).ToArray());
            var room = new Room(code, game, log);
            if (rooms.TryAdd(code, room))
            {
                log.LogInformation("Room {Code} created for {Game}", code, game.GameId);
                return room;
            }
        }
        return null;
    }

    public bool TryGet(string code, out Room room)
    {
        room = null;
        if (string.IsNullOrWhiteSpace(code)) return false;
        return rooms.TryGetValue(code.Trim().ToUpperInvariant(), out room);
    }

    public void Remove(Room room)
    {
        if (rooms.TryRemove(room.Code, out _)) log.LogInformation("Room {Code} closed", room.Code);
    }
}
