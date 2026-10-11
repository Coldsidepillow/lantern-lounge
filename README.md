# Lantern Lounge

A multi-game board and card game app built in Unity 6, with Tidebound as its first featured game.
"Lantern Lounge" is a placeholder name.

## What is in this folder

| Folder | What it is |
| --- | --- |
| `Shared/` | Unity package `com.lanternlounge.shared`: all game rules. It never references Unity, so a future C# server can compile the exact same files. |
| `Shared/Runtime/Core/` | The game-module contract, game registry, match config and results, seeded random, and `LocalMatch` (runs a match on one device). |
| `Shared/Runtime/Games/PebbleDrop/` | Pebble Drop rules, win and draw detection, and a practice bot. |
| `Shared/Tests/Editor/` | NUnit tests for everything in `Shared/`. |
| `Client/` | Unity package `com.lanternlounge.client`: the app's screens, built in code with UI Toolkit. |
| `DotNet/` | Plain .NET projects that build and test `Shared/` without opening Unity. |

Your Unity project (for example `My project`) lives outside this folder and installs both packages from disk.
Updating = replace the files in this folder; Unity picks up the changes.

## Status

- **Works:** the game contract and registry, Pebble Drop (rules, bot, pass-and-play, against the bot), the lounge screen, a reduce-motion setting.
- **Not built yet:** Tidebound's rules and table, online rooms, accounts, the server, Fogline Fleet, Prism Rush.

## Set up (once)

1. In Unity: **Window > Package Manager > + > Install package from disk...** and pick `Shared/package.json`.
2. Do the same for `Client/package.json`.
3. To run the tests in Unity, add this to your project's `Packages/manifest.json`, after the `dependencies` block:

   ```json
   "testables": ["com.lanternlounge.shared"]
   ```

   Then use **Window > General > Test Runner > EditMode > Run All**.

## Play

Press **Play** in any scene. The app starts by itself: there is nothing to add to the scene.
On PC, use the mouse, or Tab and Enter. On a phone, tap.

## Run the tests without Unity

With the .NET 8 SDK installed, from this folder:

```
dotnet test DotNet/Tests
```

## Rules for code in Shared/

- No `UnityEngine` or `UnityEditor` references. The assembly definitions enforce this (`noEngineReferences`).
- Use `SeededRandom`, never `System.Random` or `UnityEngine.Random`. Those give different sequences in Unity and on the server.
- States are immutable: `Apply` returns a new state.
- C# 9 at most, and no `record` or `init` (Unity's runtime lacks the support type).

## Adding a game

1. Create `Shared/Runtime/Games/<Name>/` with its own assembly definition referencing `LanternLounge.Core`.
2. Derive a module from `GameModule<TState, TAction, TView>` and add tests.
3. Register it in `Client/Runtime/GameCatalog.cs` and give it a screen. No existing game's code changes.
