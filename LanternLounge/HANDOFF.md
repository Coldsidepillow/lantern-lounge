# Lantern Lounge: start here

A multi-game app with five original games: Tidebound, Prism Rush, Pebble Drop, Treehouse Hold'em and Hide & Seek (the last is website/server only, not in the Unity app). It comes in three parts that share the same game rules:

| Part | Folder | What it is |
|---|---|---|
| Game rules | `Shared/` | Plain C# rules for all four games, plus NUnit tests. No Unity code, so the app and the server both use it. |
| Unity app | `Client/` | Unity 6 package. The lounge and every game screen, built in code with UI Toolkit. Online play works for Pebble Drop. |
| Server + website | `Server/` | ASP.NET Core 8 server for online rooms (join codes, bots, reconnects). `Server/wwwroot/` is the website where all five games can be played online in a browser. |
| .NET projects | `DotNet/` | Builds and tests `Shared/` without Unity (`dotnet test DotNet/Tests`). |
| Hosting | `Dockerfile`, `render.yaml` | How Render builds and runs the server. |

`pebble-drop-4.html` is a separate, offline-only Pebble Drop web page: one file you can open in any browser.

## Open the Unity app
1. Unity 6 (Universal 2D project).
2. **Window > Package Manager > + > Install package from disk**, then pick `Shared/package.json`, then `Client/package.json`.
3. Press Play. The app starts by itself; there is nothing to add to a scene.
4. Optional tests: add `"testables": ["com.lanternlounge.shared"]` to `Packages/manifest.json`, then use **Window > General > Test Runner > EditMode > Run All**.

## The live server
- Deployed on Render (free plan) from the GitHub repo `Coldsidepillow/lantern-lounge`, with Root Directory `LanternLounge`.
- Address: `https://lantern-lounge.onrender.com`. The app connects to `wss://lantern-lounge.onrender.com/ws`. To change it, edit `OnlineSession.ServerUrl`.
- The free plan sleeps after 15 idle minutes; the first connection afterwards takes about a minute.
- Protocol and deploy steps: `Server/README.md`.

## Run the server locally
With the .NET 8 SDK installed, run `dotnet run --project Server` from this folder, then open http://localhost:10000.

## Not done yet
- Online tables in the Unity app for Tidebound, Prism Rush and Treehouse Hold'em. The server and website already support all four.
- Chat and "change game" in the Unity app. The server and website have them (chat in every game except Pebble Drop); the app ignores the new messages, so a Unity player in a room whose host switches to another game would need to leave.
- Splitting the Unity project into four separate store apps (iOS / Android).
- Store listings. Note: poker-style games (Treehouse Hold'em) count as "simulated gambling" on both stores.

More detail: `README.md` (rules for every game) and `Server/README.md`.
