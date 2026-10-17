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

    /// <summary>The character this seat picked (games with a choice only), or null.</summary>
    public string Choice { get; set; }

    public bool IsHuman => Token != null;
    public bool IsConnected => Connection != null && Connection.IsOpen;

    private readonly Queue<DateTime> recentChats = new Queue<DateTime>();

    /// <summary>
    /// Chat rate limit: at most 5 messages in any 10 seconds, and not two within half a second.
    /// Records the message when it is allowed.
    /// </summary>
    public bool TryUseChat(DateTime now)
    {
        while (recentChats.Count > 0 && now - recentChats.Peek() > TimeSpan.FromSeconds(10)) recentChats.Dequeue();
        if (recentChats.Count >= 5) return false;
        if (recentChats.Count > 0 && now - recentChats.Last() < TimeSpan.FromMilliseconds(500)) return false;
        recentChats.Enqueue(now);
        return true;
    }
}

/// <summary>One line of table chat. Seat is the sender's seat number.</summary>
public sealed class ChatMessage
{
    public int Id { get; init; }
    public int Seat { get; init; }
    public string Name { get; init; }
    public string Text { get; init; }
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

    public const int MaxChatHistory = 50;
    private readonly List<ChatMessage> chat = new List<ChatMessage>();
    private int nextChatId = 1;

    public Room(string code, GameAdapter game, ILogger log)
    {
        Code = code;
        Game = game;
        this.log = log;
        Seats = Enumerable.Range(0, game.Seats).Select(i => new Seat { Index = i }).ToArray();
        LastActivity = DateTime.UtcNow;
    }

    public string Code { get; }
    public GameAdapter Game { get; private set; }
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
        EnsureChoices();
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
        if (seat.Index < Inputs.Length) Inputs[seat.Index] = null; // old controls from before the drop
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
            seat.Choice = null;
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

    // ---------- Chat and picking the game (call inside Gate) ----------

    /// <summary>The recent chat lines, oldest first. The chat belongs to the room, so it carries over when the game changes.</summary>
    public ChatMessage[] ChatHistory() => chat.ToArray();

    /// <summary>
    /// Adds a chat line. This deliberately does not call <see cref="Touch"/>: chatting must not
    /// bump the version, or it would cancel the bots' and the turn timers.
    /// </summary>
    public ChatMessage AddChat(Seat seat, string text)
    {
        var message = new ChatMessage { Id = nextChatId++, Seat = seat.Index, Name = seat.Name, Text = text };
        chat.Add(message);
        while (chat.Count > MaxChatHistory) chat.RemoveAt(0);
        LastActivity = DateTime.UtcNow;
        return message;
    }

    /// <summary>
    /// Sends the whole group back to the waiting room with a different game (or the same one again).
    /// People keep their seats, tokens and the host; bots are removed until the next Start.
    /// Returns false if the new game needs a different number of seats.
    /// </summary>
    public bool SwitchGame(GameAdapter game)
    {
        if (game.Seats != Seats.Length) return false;
        string from = Game.GameId;
        Game = game;
        State = null;
        Status = RoomStatus.Lobby;
        ScheduledFor = -1;
        foreach (var seat in Seats)
        {
            seat.Bot = null;
            seat.IsBot = false;
            seat.Choice = null;
            if (!seat.IsHuman) seat.Name = null;
        }
        EnsureChoices();
        Inputs = new object[Seats.Length];
        log.LogInformation("Room {Code}: switched from {From} to {To}", Code, from, game.GameId);
        Touch();
        return true;
    }

    // ---------- Characters (call inside Gate) ----------

    /// <summary>Real-time games: each seat's latest controls. People set these; bots are asked every tick.</summary>
    public object[] Inputs { get; private set; } = new object[4];

    /// <summary>True while this room's tick loop is running (only touched inside the Gate).</summary>
    public bool TickerRunning { get; set; }

    /// <summary>Gives every seated person a character if they have none, and takes characters away if the game has none.</summary>
    public void EnsureChoices(bool includeBots = false)
    {
        var choices = Game.Choices;
        if (choices.Count == 0)
        {
            foreach (var s in Seats) s.Choice = null;
            return;
        }
        foreach (var s in Seats)
        {
            if (s.Choice != null && !choices.Contains(s.Choice)) s.Choice = null;
        }
        foreach (var s in Seats)
        {
            if (s.Choice != null || !(s.IsHuman || includeBots)) continue;
            s.Choice = choices.FirstOrDefault(c => !Seats.Any(o => o.Choice == c));
        }
    }

    /// <summary>A person picks a character in the waiting room. Returns an error message, or null on success.</summary>
    public string Choose(Seat seat, string choice)
    {
        if (Status != RoomStatus.Lobby) return "You can pick a character in the waiting room.";
        if (choice == null || !Game.Choices.Contains(choice)) return "That character isn't in this game.";
        if (seat.Choice == choice) return null;
        var owner = Seats.FirstOrDefault(s => s.Choice == choice);
        if (owner != null) return $"{owner.Name} already picked that one.";
        seat.Choice = choice;
        Touch();
        return null;
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
        EnsureChoices(includeBots: true);
        Inputs = new object[Seats.Length];
        State = Game.Module.Setup(Game.Config(seed, State));
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
