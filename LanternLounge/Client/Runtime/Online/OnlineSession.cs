using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LanternLounge.Client.Online
{
    internal enum OnlineStatus
    {
        Offline,
        Connecting,
        Connected,
        Reconnecting
    }

    internal sealed class RoomSeat
    {
        public int Seat;
        public string Name;
        public bool IsBot;
        public bool IsHuman;
        public bool Connected;
        public bool IsHost;
    }

    /// <summary>The table as the server last described it, from this player's point of view.</summary>
    internal sealed class RoomSnapshot
    {
        public string Code;
        public string Game;
        public string Status; // "lobby", "playing", "finished"
        public int Match;
        public int You;
        public int HostSeat;
        public readonly List<RoomSeat> Seats = new List<RoomSeat>();
        public int[] SeatsToAct = Array.Empty<int>();
        public bool Over;
        public int[] WinningSeats = Array.Empty<int>();
        public bool IsDraw;
        public Dictionary<string, object> View;

        public bool IsHost => You == HostSeat;
        public bool InLobby => Status == "lobby";
        public bool InGame => Status == "playing" || Status == "finished";

        public RoomSeat SeatAt(int seat) => seat >= 0 && seat < Seats.Count ? Seats[seat] : null;

        public static RoomSnapshot From(Dictionary<string, object> m)
        {
            var room = new RoomSnapshot
            {
                Code = Json.Str(m, "code", ""),
                Game = Json.Str(m, "game", ""),
                Status = Json.Str(m, "status", "lobby"),
                Match = Json.Int(m, "match"),
                You = Json.Int(m, "you"),
                HostSeat = Json.Int(m, "hostSeat"),
                SeatsToAct = Json.Ints(Json.Arr(m, "seatsToAct")),
                View = Json.Obj(m, "view")
            };
            var seats = Json.Arr(m, "seats");
            if (seats != null)
            {
                foreach (var s in seats)
                {
                    room.Seats.Add(new RoomSeat
                    {
                        Seat = Json.Int(s, "seat"),
                        Name = Json.Str(s, "name"),
                        IsBot = Json.Bool(s, "isBot"),
                        IsHuman = Json.Bool(s, "isHuman"),
                        Connected = Json.Bool(s, "connected"),
                        IsHost = Json.Bool(s, "isHost")
                    });
                }
            }
            var result = Json.Obj(m, "result");
            if (result != null)
            {
                room.Over = Json.Bool(result, "over");
                room.WinningSeats = Json.Ints(Json.Arr(result, "winningSeats"));
                room.IsDraw = Json.Bool(result, "isDraw");
            }
            return room;
        }
    }

    /// <summary>
    /// The app's one connection to the online server. Network reads happen in the background;
    /// everything else (and every event) runs on Unity's main thread through <see cref="Pump"/>,
    /// which <see cref="LoungeApp"/> calls every frame.
    /// </summary>
    internal sealed class OnlineSession
    {
        /// <summary>The Render server. Change this if the server moves.</summary>
        public const string ServerUrl = "wss://lantern-lounge.onrender.com/ws";

        private const float PingSeconds = 15f;
        private const float ReconnectSeconds = 3f;
        private const int MaxReconnectAttempts = 20;
        // A sleeping free server takes up to about a minute to wake up.
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(75);

        private readonly ConcurrentQueue<KeyValuePair<int, string>> inbox = new ConcurrentQueue<KeyValuePair<int, string>>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket socket;
        private CancellationTokenSource cancel;
        private bool connecting;
        private bool wantRoom;
        private string roomCode;
        private string roomToken;
        private float nextPing;
        private float nextReconnect;
        private int reconnectAttempts;
        private int socketGeneration;
        private float lastReceived;
        private const float SilenceSeconds = 35f;

        public OnlineStatus Status { get; private set; } = OnlineStatus.Offline;
        public RoomSnapshot Room { get; private set; }
        public int Seat { get; private set; } = -1;

        /// <summary>The whole table changed (sent after every move and every join or leave).</summary>
        public event Action<RoomSnapshot> RoomChanged;

        /// <summary>The server refused something: (request, code, message for the player).</summary>
        public event Action<string, string, string> Error;

        public event Action<OnlineStatus> StatusChanged;

        /// <summary>You are no longer in a room (you left, or your seat is gone).</summary>
        public event Action Left;

        /// <summary>Why you were last removed from a room (null if you left yourself).</summary>
        public string LastLeaveReason { get; private set; }

        public bool InRoom => wantRoom && Room != null;

        // ---------- Requests ----------

        public void Create(string game, string name) =>
            SendWhenConnected(new Dictionary<string, object> { { "type", "create" }, { "game", game }, { "name", name } });

        public void Join(string code, string name) =>
            SendWhenConnected(new Dictionary<string, object> { { "type", "join" }, { "code", code }, { "name", name } });

        public void StartGame() => Send(new Dictionary<string, object> { { "type", "start" } });

        public void Rematch() => Send(new Dictionary<string, object> { { "type", "rematch" } });

        public void Move(Dictionary<string, object> move) =>
            Send(new Dictionary<string, object> { { "type", "move" }, { "move", move } });

        /// <summary>Gives up the seat and closes the connection.</summary>
        public void Leave() => LeaveRoom(null);

        private void LeaveRoom(string reason)
        {
            LastLeaveReason = reason;
            if (Status == OnlineStatus.Connected) Send(new Dictionary<string, object> { { "type", "leave" } });
            wantRoom = false;
            roomCode = null;
            roomToken = null;
            Room = null;
            Seat = -1;
            reconnectAttempts = 0;
            CloseSoon();
            SetStatus(OnlineStatus.Offline);
            Left?.Invoke();
        }

        // ---------- Main-thread pump ----------

        public void Pump()
        {
            while (inbox.TryDequeue(out var item))
            {
                // Ignore anything from a connection we have already closed.
                if (item.Key != socketGeneration) continue;
                lastReceived = Time.realtimeSinceStartup;
                Handle(item.Value);
            }

            float now = Time.realtimeSinceStartup;

            // The server answers every ping; total silence means the connection quietly died.
            if (Status == OnlineStatus.Connected && now - lastReceived > SilenceSeconds)
            {
                CloseSoon();
                Handle("\u0000closed");
            }
            if (Status == OnlineStatus.Connected && now >= nextPing)
            {
                nextPing = now + PingSeconds;
                Send(new Dictionary<string, object> { { "type", "ping" } });
            }

            if (Status == OnlineStatus.Reconnecting && !connecting && now >= nextReconnect)
            {
                if (reconnectAttempts >= MaxReconnectAttempts)
                {
                    string message = $"Lost the connection to room {roomCode}. Check your internet and join again.";
                    Error?.Invoke("rejoin", "gave_up", message);
                    LeaveRoom(message);
                    return;
                }
                reconnectAttempts++;
                nextReconnect = now + ReconnectSeconds;
                _ = ConnectAsync(new Dictionary<string, object> { { "type", "rejoin" }, { "code", roomCode }, { "token", roomToken } });
            }
        }

        private void Handle(string text)
        {
            if (text == "\u0000closed")
            {
                // The background reader saw the connection end.
                if (wantRoom && roomCode != null)
                {
                    SetStatus(OnlineStatus.Reconnecting);
                    nextReconnect = Time.realtimeSinceStartup + 1f;
                }
                else
                {
                    SetStatus(OnlineStatus.Offline);
                }
                return;
            }

            Dictionary<string, object> m;
            try
            {
                m = MiniJson.Parse(text) as Dictionary<string, object>;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Lantern Lounge online: couldn't read a message: " + e.Message);
                return;
            }
            if (m == null) return;

            switch (Json.Str(m, "type"))
            {
                case "joined":
                    roomCode = Json.Str(m, "code");
                    roomToken = Json.Str(m, "token");
                    Seat = Json.Int(m, "seat", -1);
                    wantRoom = true;
                    reconnectAttempts = 0;
                    break;
                case "room":
                    Room = RoomSnapshot.From(m);
                    Seat = Room.You;
                    RoomChanged?.Invoke(Room);
                    break;
                case "error":
                {
                    string request = Json.Str(m, "request");
                    string code = Json.Str(m, "code");
                    string message = Json.Str(m, "message", "Something went wrong.");
                    Error?.Invoke(request, code, message);
                    if (request == "rejoin" && (code == "no_room" || code == "not_seated")) LeaveRoom(message);
                    break;
                }
                case "left":
                    break;
            }
        }

        // ---------- Connection ----------

        private void SendWhenConnected(Dictionary<string, object> message)
        {
            if (Status == OnlineStatus.Connected && socket != null && socket.State == WebSocketState.Open) Send(message);
            else _ = ConnectAsync(message);
        }

        private async Task ConnectAsync(Dictionary<string, object> firstMessage)
        {
            if (connecting) return;
            connecting = true;
            if (Status != OnlineStatus.Reconnecting) SetStatus(OnlineStatus.Connecting);
            CloseSoon();

            var fresh = new ClientWebSocket();
            var source = new CancellationTokenSource();
            int generation = ++socketGeneration;
            try
            {
                using (var timeout = new CancellationTokenSource(ConnectTimeout))
                {
                    await fresh.ConnectAsync(new Uri(ServerUrl), timeout.Token);
                }
                if (generation != socketGeneration)
                {
                    // The player left (or something else closed it) while we were connecting.
                    fresh.Dispose();
                    source.Dispose();
                    return;
                }
                socket = fresh;
                lastReceived = Time.realtimeSinceStartup;
                cancel = source;
                SetStatus(OnlineStatus.Connected);
                nextPing = Time.realtimeSinceStartup + PingSeconds;
                _ = Task.Run(() => ReadLoop(fresh, source.Token, generation));
                if (firstMessage != null) Send(firstMessage);
            }
            catch (Exception e)
            {
                fresh.Dispose();
                source.Dispose();
                Debug.LogWarning("Lantern Lounge online: couldn't connect: " + e.Message);
                if (generation != socketGeneration) return;
                if (wantRoom && roomCode != null)
                {
                    SetStatus(OnlineStatus.Reconnecting);
                }
                else
                {
                    SetStatus(OnlineStatus.Offline);
                    Error?.Invoke(Json.Str(firstMessage, "type"), "cant_connect",
                        "Couldn't reach the game server. Check your internet, then try again.");
                }
            }
            finally
            {
                connecting = false;
            }
        }

        private async Task ReadLoop(ClientWebSocket ws, CancellationToken token, int generation)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();
            try
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    builder.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType == WebSocketMessageType.Text)
                        inbox.Enqueue(new KeyValuePair<int, string>(generation, builder.ToString()));
                }
            }
            catch (Exception)
            {
                // Network dropped; handled below.
            }
            // Pump ignores this if the connection was replaced or closed on purpose.
            if (!token.IsCancellationRequested) inbox.Enqueue(new KeyValuePair<int, string>(generation, "\u0000closed"));
        }

        private async void Send(Dictionary<string, object> message)
        {
            var ws = socket;
            if (ws == null || ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(MiniJson.Serialize(message));
            await sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Lantern Lounge online: send failed: " + e.Message);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private void CloseSoon()
        {
            var ws = socket;
            var source = cancel;
            socket = null;
            cancel = null;
            socketGeneration++;
            if (ws == null)
            {
                if (source != null)
                {
                    try { source.Cancel(); } catch (Exception) { }
                }
                return;
            }
            _ = Task.Run(async () =>
            {
                // Let any message being sent (like "leave") finish, then say goodbye properly.
                await sendLock.WaitAsync();
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                        {
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
                        }
                    }
                }
                catch (Exception) { }
                finally
                {
                    sendLock.Release();
                    if (source != null)
                    {
                        try { source.Cancel(); } catch (Exception) { }
                    }
                    ws.Dispose();
                }
            });
        }

        private void SetStatus(OnlineStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }

        /// <summary>Called when the app quits.</summary>
        public void Shutdown()
        {
            wantRoom = false;
            CloseSoon();
        }
    }
}
