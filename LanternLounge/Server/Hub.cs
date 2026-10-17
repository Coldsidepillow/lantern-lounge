using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanternLounge.Core;

namespace LanternLounge.Server;

/// <summary>One phone's WebSocket connection.</summary>
public sealed class Connection
{
    private readonly WebSocket socket;
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    public Connection(WebSocket socket)
    {
        this.socket = socket;
    }

    /// <summary>Phones send a ping every 15 seconds; one that's silent for this long is treated as gone.</summary>
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(45);

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsOpen => socket.State == WebSocketState.Open && DateTime.UtcNow - LastSeen < SilenceLimit;
    public Room Room { get; set; }

    /// <summary>Drops a connection that has gone silent, so the server notices straight away.</summary>
    public void Abort()
    {
        try { socket.Abort(); } catch (Exception) { }
    }
    public Seat Seat { get; set; }

    public async Task SendAsync(object message)
    {
        if (!IsOpen) return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, Hub.Json);
        await sendLock.WaitAsync();
        try
        {
            if (!IsOpen) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        }
        catch (Exception)
        {
            // A phone that can't keep up or has gone away; the receive loop will clean up.
        }
        finally
        {
            sendLock.Release();
        }
    }
}

/// <summary>
/// Handles every message from the apps. Protocol (JSON over one WebSocket at /ws):
///   create  { game, name }            make a room; you get seat 0 and a join code
///   join    { code, name }            sit in the first free seat (or take over a bot mid-game)
///   rejoin  { code, token }           come back after the connection dropped
///   start   { }                       host only: empty seats become bots and the game begins
///   move    { move: { ... } }         a move for your seat (see each game's adapter)
///   rematch { }                       host only, after a game ends
///   leave   { }                       give up your seat
///   chat    { text }                  say something at the table (not in Pebble Drop)
///   setgame { game }                  host only: send everyone back to the waiting room with another game
///   choose  { choice }                pick your character in the waiting room (Hide & Seek)
///   ping    { }
/// The server answers with "joined", "room" (the whole table, sent to everyone after every
/// change, each with only what that player may see), "error" and "pong".
/// </summary>
public sealed class Hub
{
    public const int ProtocolVersion = 1;
    private const int MaxMessageBytes = 8 * 1024;
    private const int MaxNameLength = 16;
    private const int MaxChatLength = 200;

    public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly RoomManager rooms;
    private readonly ILogger<Hub> log;

    /// <summary>Every open connection, so the janitor can drop ones that went silent.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<Connection, byte> Live { get; } =
        new System.Collections.Concurrent.ConcurrentDictionary<Connection, byte>();

    public Hub(RoomManager rooms, ILogger<Hub> log)
    {
        this.rooms = rooms;
        this.log = log;
    }

    public async Task RunAsync(WebSocket socket, CancellationToken stopping)
    {
        var connection = new Connection(socket);
        Live.TryAdd(connection, 0);
        await connection.SendAsync(new
        {
            type = "welcome",
            protocol = ProtocolVersion,
            games = GameCatalog.All.Select(g => new { id = g.GameId, name = g.Module.Info.DisplayName, seats = g.Seats, chat = g.ChatEnabled, realtime = g.IsRealtime, choices = g.Choices })
        });

        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open && !stopping.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, stopping);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", stopping);
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) break;
                connection.LastSeen = DateTime.UtcNow;
                if (result.MessageType != WebSocketMessageType.Text) continue;
                await HandleAsync(connection, message.ToArray());
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            Live.TryRemove(connection, out _);
            await DisconnectedAsync(connection);
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None); }
                catch (Exception) { }
            }
        }
    }

    private async Task HandleAsync(Connection connection, byte[] bytes)
    {
        JsonElement root;
        string type;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object)
            {
                await ErrorAsync(connection, null, "bad_json", "Messages must be JSON objects.");
                return;
            }
            type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException)
        {
            await ErrorAsync(connection, null, "bad_json", "That message wasn't valid JSON.");
            return;
        }

        try
        {
            switch (type)
            {
                case "ping": await connection.SendAsync(new { type = "pong" }); break;
                case "create": await CreateAsync(connection, root); break;
                case "join": await JoinAsync(connection, root); break;
                case "rejoin": await RejoinAsync(connection, root); break;
                case "start": await StartAsync(connection); break;
                case "move": await MoveAsync(connection, root); break;
                case "rematch": await RematchAsync(connection); break;
                case "leave": await LeaveAsync(connection); break;
                case "chat": await ChatAsync(connection, root); break;
                case "setgame": await SetGameAsync(connection, root); break;
                case "choose": await ChooseAsync(connection, root); break;
                default: await ErrorAsync(connection, type, "unknown_type", "Unknown message type."); break;
            }
        }
        catch (Exception e)
        {
            log.LogError(e, "Error handling a {Type} message", type);
            await ErrorAsync(connection, type, "server_error", "Something went wrong on the server. Please try again.");
        }
    }

    // ---------- Requests ----------

    private async Task CreateAsync(Connection connection, JsonElement root)
    {
        if (connection.Room != null) await LeaveAsync(connection);
        if (!GameCatalog.TryGet(GetString(root, "game"), out var game))
        {
            await ErrorAsync(connection, "create", "unknown_game", "That game isn't on this server.");
            return;
        }
        var room = rooms.Create(game);
        if (room == null)
        {
            await ErrorAsync(connection, "create", "server_full", "The server is busy. Try again in a minute.");
            return;
        }
        await InRoomAsync(room, async () =>
        {
            var seat = room.Sit(CleanName(GetString(root, "name"), 0), connection);
            connection.Room = room;
            connection.Seat = seat;
            await SendJoinedAsync(connection, room, seat);
        });
    }

    private async Task JoinAsync(Connection connection, JsonElement root)
    {
        if (!rooms.TryGet(GetString(root, "code"), out var room))
        {
            await ErrorAsync(connection, "join", "no_room", "No room has that code. Check the letters and try again.");
            return;
        }
        if (connection.Room != null && connection.Room != room) await LeaveAsync(connection);
        await InRoomAsync(room, async () =>
        {
            if (connection.Room == room) return;
            int index = room.Seats.FirstOrDefault(s => !s.IsHuman)?.Index ?? 0;
            var seat = room.Sit(CleanName(GetString(root, "name"), index), connection);
            if (seat == null)
            {
                await ErrorAsync(connection, "join", "room_full", "That room is full.");
                return;
            }
            connection.Room = room;
            connection.Seat = seat;
            await SendJoinedAsync(connection, room, seat);
        });
    }

    private async Task RejoinAsync(Connection connection, JsonElement root)
    {
        if (!rooms.TryGet(GetString(root, "code"), out var room))
        {
            await ErrorAsync(connection, "rejoin", "no_room", "That room has closed.");
            return;
        }
        if (connection.Room != null && connection.Room != room) await LeaveAsync(connection);
        await InRoomAsync(room, async () =>
        {
            if (connection.Seat != null && connection.Seat.Connection == connection)
            {
                // Already sitting somewhere in this room: let that seat go quiet first.
                connection.Seat.Connection = null;
                connection.Seat.DisconnectedAt = DateTime.UtcNow;
            }
            var seat = room.FindByToken(GetString(root, "token"));
            if (seat == null)
            {
                await ErrorAsync(connection, "rejoin", "not_seated", "Your seat in that room is gone. Join again with the code.");
                return;
            }
            room.Reconnect(seat, connection);
            connection.Room = room;
            connection.Seat = seat;
            await SendJoinedAsync(connection, room, seat);
        });
    }

    private async Task StartAsync(Connection connection)
    {
        var room = connection.Room;
        if (room == null)
        {
            await ErrorAsync(connection, "start", "not_in_room", "Join a room first.");
            return;
        }
        await InRoomAsync(room, async () =>
        {
            if (!Owns(connection))
            {
                await ErrorAsync(connection, "start", "not_seated", "You're not sitting at this table.");
                return;
            }
            if (connection.Seat.Index != room.HostSeat)
            {
                await ErrorAsync(connection, "start", "not_host", "Only the host can start the game.");
                return;
            }
            if (room.Status != RoomStatus.Lobby)
            {
                await ErrorAsync(connection, "start", "already_started", "The game has already started.");
                return;
            }
            room.Start();
        });
    }

    private async Task RematchAsync(Connection connection)
    {
        var room = connection.Room;
        if (room == null) return;
        await InRoomAsync(room, async () =>
        {
            if (room.Status != RoomStatus.Finished)
            {
                await ErrorAsync(connection, "rematch", "not_finished", "Finish this game first.");
                return;
            }
            if (!Owns(connection) || connection.Seat.Index != room.HostSeat)
            {
                await ErrorAsync(connection, "rematch", "not_host", "Only the host can start a new game.");
                return;
            }
            room.Start();
        });
    }

    private async Task MoveAsync(Connection connection, JsonElement root)
    {
        var room = connection.Room;
        if (room == null)
        {
            await ErrorAsync(connection, "move", "not_in_room", "Join a room first.");
            return;
        }
        if (!root.TryGetProperty("move", out var moveElement) || !room.Game.TryParseMove(moveElement, out var action, out var parseError))
        {
            await ErrorAsync(connection, "move", "bad_move", "That move couldn't be read.");
            return;
        }
        if (room.Game.IsRealtime)
        {
            // Controls are just remembered; the room's clock picks them up on its next tick.
            // Nothing is sent back, and no timers are touched.
            var seatNow = connection.Seat;
            if (seatNow != null && Owns(connection) && !seatNow.IsBot && room.Status == RoomStatus.Playing)
            {
                var inputs = room.Inputs;
                if (seatNow.Index < inputs.Length) inputs[seatNow.Index] = action;
            }
            return;
        }
        await InRoomAsync(room, async () =>
        {
            if (!Owns(connection) || connection.Seat.IsBot)
            {
                await ErrorAsync(connection, "move", "not_seated", "A bot is playing your seat. Rejoin to take it back.");
                return;
            }
            var refused = room.TryMove(connection.Seat.Index, action);
            if (refused.HasValue) await ErrorAsync(connection, "move", refused.Value.Code, refused.Value.Message);
        });
    }

    /// <summary>
    /// A chat line. This runs inside the room's lock but does not go through <see cref="InRoomAsync"/>,
    /// because chatting must not restart the turn timers or resend the whole table.
    /// </summary>
    private async Task ChatAsync(Connection connection, JsonElement root)
    {
        var room = connection.Room;
        if (room == null)
        {
            await ErrorAsync(connection, "chat", "not_in_room", "Join a room first.");
            return;
        }
        string text = CleanChat(GetString(root, "text"));
        if (text == null) return; // nothing worth sending

        ChatMessage message = null;
        await room.Gate.WaitAsync();
        try
        {
            if (!Owns(connection))
            {
                await ErrorAsync(connection, "chat", "not_seated", "You're not sitting at this table.");
                return;
            }
            if (!room.Game.ChatEnabled)
            {
                await ErrorAsync(connection, "chat", "chat_off", "Chat is turned off in this game.");
                return;
            }
            if (!connection.Seat.TryUseChat(DateTime.UtcNow))
            {
                await ErrorAsync(connection, "chat", "chat_slow", "Slow down a little!");
                return;
            }
            message = room.AddChat(connection.Seat, text);
            await BroadcastChatAsync(room, message);
        }
        catch (Exception e)
        {
            log.LogError(e, "Room {Code}: error while sending a chat line", room.Code);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    /// <summary>Host only: back to the waiting room with a different game, keeping the same group.</summary>
    private async Task SetGameAsync(Connection connection, JsonElement root)
    {
        var room = connection.Room;
        if (room == null)
        {
            await ErrorAsync(connection, "setgame", "not_in_room", "Join a room first.");
            return;
        }
        if (!GameCatalog.TryGet(GetString(root, "game"), out var game))
        {
            await ErrorAsync(connection, "setgame", "unknown_game", "That game isn't on this server.");
            return;
        }
        await InRoomAsync(room, async () =>
        {
            if (!Owns(connection))
            {
                await ErrorAsync(connection, "setgame", "not_seated", "You're not sitting at this table.");
                return;
            }
            if (connection.Seat.Index != room.HostSeat)
            {
                await ErrorAsync(connection, "setgame", "not_host", "Only the host can pick the game.");
                return;
            }
            // Picking the game that is already chosen in the waiting room changes nothing.
            if (room.Status == RoomStatus.Lobby && room.Game.GameId == game.GameId) return;
            if (!room.SwitchGame(game))
                await ErrorAsync(connection, "setgame", "seat_mismatch", "That game needs a different number of seats.");
        });
    }

    /// <summary>Pick a character in the waiting room.</summary>
    private async Task ChooseAsync(Connection connection, JsonElement root)
    {
        var room = connection.Room;
        if (room == null)
        {
            await ErrorAsync(connection, "choose", "not_in_room", "Join a room first.");
            return;
        }
        string choice = GetString(root, "choice");
        await InRoomAsync(room, async () =>
        {
            if (!Owns(connection))
            {
                await ErrorAsync(connection, "choose", "not_seated", "You're not sitting at this table.");
                return;
            }
            string problem = room.Choose(connection.Seat, choice);
            if (problem != null) await ErrorAsync(connection, "choose", "bad_choice", problem);
        });
    }

    private async Task LeaveAsync(Connection connection)
    {
        var room = connection.Room;
        if (room == null) return;
        await InRoomAsync(room, () =>
        {
            if (connection.Seat != null && connection.Seat.Connection == connection) room.Leave(connection.Seat);
            connection.Room = null;
            connection.Seat = null;
            if (!room.HasHumans) rooms.Remove(room);
            return Task.CompletedTask;
        });
        await connection.SendAsync(new { type = "left" });
    }

    private async Task DisconnectedAsync(Connection connection)
    {
        var room = connection.Room;
        if (room == null) return;
        await InRoomAsync(room, () =>
        {
            var seat = connection.Seat;
            if (seat != null && seat.Connection == connection)
            {
                seat.Connection = null;
                seat.DisconnectedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        });
    }

    // ---------- Running a room ----------

    /// <summary>Runs a change inside the room's lock, then tells everyone and schedules what comes next.</summary>
    public async Task InRoomAsync(Room room, Func<Task> change)
    {
        await room.Gate.WaitAsync();
        try
        {
            await change();
            ScheduleNext(room);
            await BroadcastAsync(room);
        }
        catch (Exception e)
        {
            room.ScheduledFor = -1;
            log.LogError(e, "Room {Code}: error while handling a change", room.Code);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    /// <summary>Bots move after a short pause; people get a turn timer; some games move on by themselves.</summary>
    private void ScheduleNext(Room room)
    {
        if (room.Status != RoomStatus.Playing || room.State == null || !room.HasHumans) return;
        if (room.Game.IsRealtime)
        {
            StartTicker(room);
            return;
        }
        int version = room.Version;
        if (room.ScheduledFor == version) return;
        room.ScheduledFor = version;
        var state = room.State;

        var auto = room.Game.AutoAdvanceDelay(state);
        if (auto.HasValue)
        {
            Later(room, version, auto.Value, () => PlayTimeoutMove(room));
            return;
        }

        var toAct = room.Game.Module.SeatsToAct(state);
        if (toAct.Count == 0) return;
        var seat = room.Seats[toAct[0]];
        if (seat.IsBot) Later(room, version, Room.BotDelay, () => PlayBotMove(room, seat));
        else Later(room, version, Room.TurnTimeout, () => PlayTimeoutMove(room));
    }

    /// <summary>
    /// The clock of a real-time game. Every tick it collects everyone's controls, advances the game one step and
    /// sends each person what they can see. It stops by itself when the round ends or nobody is left.
    /// Only started or stopped inside the room's Gate, which is also held during each tick.
    /// </summary>
    private void StartTicker(Room room)
    {
        if (room.TickerRunning) return;
        room.TickerRunning = true;
        _ = Task.Run(async () =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long due = 0;
            while (true)
            {
                int tickMs = room.Game.TickMs;
                due += tickMs;
                long wait = due - clock.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay((int)wait);
                else if (wait < -1000) due = clock.ElapsedMilliseconds; // fell far behind: don't try to catch up

                await room.Gate.WaitAsync();
                try
                {
                    if (room.Status != RoomStatus.Playing || room.State == null || !room.Game.IsRealtime || !room.HasHumans)
                    {
                        room.TickerRunning = false;
                        return;
                    }
                    var inputs = new object[room.Seats.Length];
                    foreach (var seat in room.Seats)
                    {
                        if (seat.IsBot)
                        {
                            try { inputs[seat.Index] = seat.Bot?.ChooseMove(room.State); }
                            catch (Exception e) { log.LogWarning(e, "Room {Code}: bot in seat {Seat} failed to choose", room.Code, seat.Index); }
                        }
                        else if (seat.IsConnected && seat.Index < room.Inputs.Length)
                        {
                            inputs[seat.Index] = room.Inputs[seat.Index];
                        }
                    }
                    var refused = room.TryMove(0, room.Game.MakeTick(inputs, tickMs));
                    if (refused.HasValue) log.LogWarning("Room {Code}: a tick was refused: {Why}", room.Code, refused.Value);
                    await BroadcastAsync(room);
                    if (room.Status != RoomStatus.Playing)
                    {
                        room.TickerRunning = false;
                        return;
                    }
                }
                catch (Exception e)
                {
                    room.TickerRunning = false;
                    log.LogError(e, "Room {Code}: error in the game clock", room.Code);
                    return;
                }
                finally
                {
                    room.Gate.Release();
                }
            }
        });
    }

    private void Later(Room room, int version, TimeSpan delay, Action act)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            await room.Gate.WaitAsync();
            try
            {
                // Something else happened in the meantime: this timer is out of date.
                if (room.Version != version || room.Status != RoomStatus.Playing || !room.HasHumans) return;
                act();
                if (room.Version == version)
                {
                    // Nothing could be played; try again later instead of stalling.
                    log.LogWarning("Room {Code}: a timed move changed nothing", room.Code);
                    room.ScheduledFor = -1;
                    Later(room, version, Room.TurnTimeout, act);
                }
                else
                {
                    ScheduleNext(room);
                }
                await BroadcastAsync(room);
            }
            catch (Exception e)
            {
                room.ScheduledFor = -1;
                log.LogError(e, "Room {Code}: error in a timed move", room.Code);
            }
            finally
            {
                room.Gate.Release();
            }
        });
    }

    private void PlayBotMove(Room room, Seat seat)
    {
        if (!seat.IsBot || seat.Bot == null) return;
        object move;
        try
        {
            move = seat.Bot.ChooseMove(room.State);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Room {Code}: bot in seat {Seat} failed to choose", room.Code, seat.Index);
            move = null;
        }
        if (move == null || room.TryMove(seat.Index, move).HasValue) PlayTimeoutMove(room);
    }

    /// <summary>The always-legal move for whoever must act (when a timer runs out or a bot gets stuck).</summary>
    private void PlayTimeoutMove(Room room)
    {
        var toAct = room.Game.Module.SeatsToAct(room.State);
        if (toAct.Count == 0) return;
        int seat = toAct[0];
        if (room.Game.Module.TryGetTimeoutAction(room.State, seat, out var action)) room.TryMove(seat, action);
    }

    /// <summary>Sends the whole table to every connected person, each with only what they may see.</summary>
    public async Task BroadcastAsync(Room room)
    {
        var result = room.State != null ? room.Game.Module.Result(room.State) : null;
        var seatsToAct = room.State != null ? room.Game.Module.SeatsToAct(room.State) : Array.Empty<int>();
        var seats = room.Seats.Select(s => new
        {
            seat = s.Index,
            name = s.Name,
            isBot = s.IsBot || (!s.IsHuman && room.Status != RoomStatus.Lobby),
            isHuman = s.IsHuman,
            connected = s.IsConnected,
            isHost = s.Index == room.HostSeat && s.IsHuman,
            choice = s.Choice
        }).ToArray();

        var sends = new List<Task>();
        foreach (var seat in room.Seats)
        {
            var connection = seat.Connection;
            if (connection == null || !connection.IsOpen) continue;
            sends.Add(connection.SendAsync(new
            {
                type = "room",
                code = room.Code,
                game = room.Game.GameId,
                chatEnabled = room.Game.ChatEnabled,
                realtime = room.Game.IsRealtime,
                choices = room.Game.Choices,
                status = room.Status,
                match = room.MatchNumber,
                you = seat.Index,
                hostSeat = room.HostSeat,
                seats,
                seatsToAct,
                result = result == null ? null : new { over = result.IsOver, winningSeats = result.WinningSeats, isDraw = result.IsDraw },
                view = room.State != null ? room.Game.ViewFor(room.State, seat.Index) : null
            }));
        }
        await Task.WhenAll(sends);
    }

    /// <summary>Sends one chat line to every connected person at the table (including the sender).</summary>
    private static async Task BroadcastChatAsync(Room room, ChatMessage message)
    {
        var sends = new List<Task>();
        foreach (var seat in room.Seats)
        {
            var connection = seat.Connection;
            if (connection == null || !connection.IsOpen) continue;
            sends.Add(connection.SendAsync(new { type = "chat", message }));
        }
        await Task.WhenAll(sends);
    }

    // ---------- Helpers ----------

    /// <summary>Does this connection still hold its seat? (A newer connection may have taken it over.)</summary>
    private static bool Owns(Connection connection) =>
        connection.Seat != null && connection.Seat.Connection == connection;

    /// <summary>Tells a person where they sit, then catches them up on the chat so far.</summary>
    private static async Task SendJoinedAsync(Connection connection, Room room, Seat seat)
    {
        await connection.SendAsync(new { type = "joined", code = room.Code, game = room.Game.GameId, seat = seat.Index, token = seat.Token });
        await connection.SendAsync(new { type = "chatHistory", messages = room.ChatHistory() });
    }

    private static Task ErrorAsync(Connection connection, string request, string code, string message) =>
        connection.SendAsync(new { type = "error", request, code, message });

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Short, printable names only.</summary>
    private static string CleanName(string name, int seat)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"Player {seat + 1}";
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            if (sb.Length >= MaxNameLength) break;
        }
        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? $"Player {seat + 1}" : clean;
    }

    /// <summary>
    /// Makes a chat line safe to show: no control or invisible formatting characters (so no text-direction
    /// tricks), runs of spaces squeezed, and at most 200 characters. Returns null if nothing is left.
    /// </summary>
    private static string CleanChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder();
        bool lastSpace = true;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            if (char.IsControl(c)) continue;
            // Invisible formatting characters are dropped, except the joiner emoji sequences need.
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format && c != '\u200D') continue;
            sb.Append(c);
            lastSpace = false;
            if (sb.Length >= MaxChatLength) break;
        }
        // Don't cut an emoji (a surrogate pair) in half.
        if (sb.Length > 0 && char.IsHighSurrogate(sb[sb.Length - 1])) sb.Length--;
        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? null : clean;
    }
}

/// <summary>Every 10 seconds: bots take over seats whose phone went quiet, and empty rooms close.</summary>
public sealed class Janitor : BackgroundService
{
    private static readonly TimeSpan IdleRoomLifetime = TimeSpan.FromMinutes(15);
    private readonly RoomManager rooms;
    private readonly Hub hub;
    private readonly ILogger<Janitor> log;

    public Janitor(RoomManager rooms, Hub hub, ILogger<Janitor> log)
    {
        this.rooms = rooms;
        this.hub = hub;
        this.log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), stopping); }
            catch (OperationCanceledException) { return; }

            var now = DateTime.UtcNow;

            // Phones that stopped sending anything (lost signal, app killed) are dropped.
            foreach (var connection in hub.Live.Keys.ToList())
            {
                if (now - connection.LastSeen > Connection.SilenceLimit) connection.Abort();
            }

            foreach (var room in rooms.All.ToList())
            {
                try
                {
                    bool remove = false, needsUpdate = false;
                    await room.Gate.WaitAsync(stopping);
                    try
                    {
                        bool fresh = now - room.LastActivity < TimeSpan.FromSeconds(30);
                        bool idle = !room.HasConnectedHumans && now - room.LastActivity > IdleRoomLifetime;
                        remove = !fresh && (!room.HasHumans || idle);
                        if (!remove) needsUpdate = room.ReplaceStaleSeats(now);
                    }
                    finally
                    {
                        room.Gate.Release();
                    }
                    if (remove) rooms.Remove(room);
                    else if (needsUpdate) await hub.InRoomAsync(room, () => Task.CompletedTask);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    log.LogError(e, "Janitor: error while checking room {Code}", room.Code);
                }
            }
        }
    }
}
