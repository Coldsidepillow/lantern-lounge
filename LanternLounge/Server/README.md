# Lantern Lounge server

The online game server for Tidebound, Prism Rush, Pebble Drop and Treehouse Hold'em. Friends play together in rooms with a short join code. Bots fill any empty seats.

- **Built with:** ASP.NET Core 8, using the same game rules as the Unity app (the `Shared/` folder).
- **Rooms are kept in memory:** if the server restarts or goes to sleep, open rooms close.
- **No accounts and no chat:** players type a display name of up to 16 letters and numbers. Each seat gets a private token so a phone can reconnect after its connection drops.

## Deploy on Render (free)

1. **Put this whole folder in a GitHub repository.** The `Dockerfile` must be at the top level of the repo, next to `Shared/`, `DotNet/` and `Server/`.
2. **Create the service.** In Render, choose **New → Web Service**, then **Git Provider → GitHub**, and pick the repository.
3. **Fill in the settings:**
   - **Name:** `lantern-lounge-server` (anything works; it becomes part of the web address)
   - **Language / Runtime:** **Docker**
   - **Branch:** `main`
   - **Region:** the one closest to your players
   - **Instance type:** **Free**
   - **Health check path** (under Advanced): `/health`
4. **Deploy.** Click **Deploy Web Service**. The first build takes a few minutes. When the log says the service is live, open `https://<your-service>.onrender.com/health`. It should show `{"ok":true,"rooms":0}`.

Alternatively, choose **New → Blueprint**: Render reads `render.yaml` and fills in these settings for you.

**Free plan note:** the server sleeps after 15 minutes with nobody connected. The first player to connect after that waits about a minute while it wakes up.

## Endpoints

| Path | What it does |
| --- | --- |
| `GET /` | A plain message saying the server is running |
| `GET /health` | `{ "ok": true, "rooms": N }` |
| `GET /games` | The games on this server and their seat counts |
| `/ws` | The game connection (WebSocket, JSON messages) |

## Protocol

Every message is a JSON object with a `type`.

### App → server

| type | fields | notes |
| --- | --- | --- |
| `create` | `game`, `name` | Makes a room. You get seat 0, you are the host, and you receive the join code. |
| `join` | `code`, `name` | Takes the first free seat. During a game, this takes over a bot's seat. |
| `rejoin` | `code`, `token` | Use after a dropped connection. You get your seat back, even if a bot was covering it. |
| `start` | | Host only, from the lobby. Empty seats become bots. |
| `move` | `move` | A move for your seat (see below). |
| `rematch` | | Host only, after the game ends. |
| `leave` | | Gives up your seat. Mid-game, a bot takes over. |
| `ping` | | The server answers with `pong`. **Send one every 15 seconds:** a connection that stays silent for 45 seconds is dropped. |

### Moves

| game id | move |
| --- | --- |
| `pebble-drop` | `{ "column": 0-15 }` |
| `tidebound` | `{ "target": seat, "creature": 0-15 }` |
| `prism-rush` | `{ "kind": "play", "cards": [ids], "color": 0-3 }` (`color` only for a Prism), `{ "kind": "draw" }`, `{ "kind": "keep" }` |
| `treehouse-holdem` | `{ "kind": "fold" \| "check" \| "call" \| "raise" \| "next" }` |

### Server → app

- **`welcome`**: sent on connect. Carries the protocol version and the list of games.
- **`joined`**:
  - Carries `code`, `game`, `seat` and `token`.
  - Save the token so you can `rejoin` later.
- **`room`**:
  - The whole table, sent to everyone after every change.
  - Fields: `code`, `game`, `status` (`lobby` / `playing` / `finished`), `you`, `hostSeat`, `seats`, `seatsToAct`, `result` and `view`.
  - `seats` lists name, bot, connected and host for each seat.
  - `view` is only what your seat is allowed to see. Other players' hidden cards are never sent.
- **`error`**: `request`, `code`, `message`. Messages are written to show to players.
- **`left`**, **`pong`**.

### Timing

- **Bots:** they move about 1 second after their turn starts.
- **Turn timer:**
  - A person has 90 seconds per turn. After that, the game's safe default move is played for them.
  - Pebble Drop: centre column. Tidebound: a valid ask. Prism Rush: draw or keep. Hold'em: check, or fold if there's a raise to answer.
- **Treehouse Hold'em between rounds:** the next round starts 8 seconds after the coins are handed out, unless someone sends `next` sooner.
- **Dropped connections:** a phone that drops has 60 seconds to `rejoin` before a bot takes its seat. It can still rejoin after that and take the seat back.
- **Empty rooms:** rooms with nobody connected close after 15 minutes.

## Run it on your computer

With the .NET 8 SDK installed, run this from the repository's top folder:

```
dotnet run --project Server
```

Then connect to `ws://localhost:10000/ws`.
