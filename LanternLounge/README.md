# Lantern Lounge

A multi-game board and card game app built in Unity 6, with Tidebound as its first featured game.
"Lantern Lounge" is a placeholder name.

## What is in this folder

| Folder | What it is |
| --- | --- |
| `Shared/` | Unity package `com.lanternlounge.shared`: all game rules. It never references Unity, so a future C# server can compile the exact same files. |
| `Shared/Runtime/Core/` | The game-module contract, game registry, match config and results, seeded random, and `LocalMatch` (runs a match on one device). |
| `Shared/Runtime/Games/PebbleDrop/` | Four-player Pebble Drop rules (16 × 12 board, Red/Yellow/Blue/Green), win and draw detection, and a bot. |
| `Shared/Runtime/Games/Tidebound/` | Tidebound rules (deal, ask, dive, pods, game end), hidden-information views, and the Reef Bot. |
| `Shared/Runtime/Games/PrismRush/` | Prism Rush rules (deck, matching, chains, Flip/Hop/Pulse/Prism, draw-one, reshuffle, stalemate), hidden-information views, and the Prism Pal bot. |
| `Shared/Runtime/Games/HideSeek/` | Hide & Seek rules (website/server only): the random maze with dead ends, loops and secret rooms, real-time movement, hiding, grabbing, line-of-sight views, and a bot. |
| `Shared/Runtime/Games/TreehouseHoldem/` | Treehouse Hold'em rules (kid-friendly Texas Hold'em with play-money coins): deck, hand evaluator, win-chance estimator, betting, prize piles and side piles, hidden-information views, and the computer friend. |
| `Shared/Tests/Editor/` | NUnit tests for everything in `Shared/`. |
| `Client/` | Unity package `com.lanternlounge.client`: the app's screens, built in code with UI Toolkit. |
| `DotNet/` | Plain .NET projects that build and test `Shared/` without opening Unity. |

Your Unity project (for example `My project`) lives outside this folder and installs both packages from disk.
Updating = replace the files in this folder; Unity picks up the changes.

## Status

- **Works:** the game contract and registry; Tidebound for 4 divers (you + 3 Reef Bots); Prism Rush for 4 players (you + 3 Prism Pals) at a round table; Treehouse Hold'em for 1–4 people on one device (computer friends fill empty seats, with a pass-the-device screen between people); Pebble Drop for 4 players on a 16 × 12 board (any mix of people and bots, with undo, hints and win highlights); the lounge; a reduce-motion setting.
- **Not built yet:** online rooms, accounts and profiles, the server, the Field Log and achievements.

## Tidebound rules

- 64 Tide Tablets: 16 creatures, each with four tides (Dawn, Noon, Dusk, Night).
- Deal 7 cards to every diver. The rest form the Current, the deck in the middle of the table.
- On your tide, ask another diver for a creature you already hold. If they have any, they hand over all of them and you ask again.
- If not: Dive! Tap the Current to draw one card. Drew the creature you asked for? Show it and ask again. Otherwise the tide passes.
- Any two tides of the same creature pair up into a pod straight away, so each creature makes two pods.
- Starting a tide with no cards? Draw one first.
- When all 32 pods are claimed, the diver with the most pods wins. Ties share the win.
- **Hard mode:** there is no chat log and there are no hints. Nothing records who asked whom for what, so watch the table and remember. You only see the result of your own ask and your own dives.

## Prism Rush rules

- 84 cards: four colors, each with its own shape (Cyan diamond, Gold sun, Rose star, Lime leaf). Each color has numbers 1-7 twice, plus two Flips, two Hops and two Pulses. There are also 4 Prism wild cards.
- Deal 7 cards each. The rest is the draw deck in the middle; one number card starts the played pile.
- On your turn, play a card that matches the color in play, the number, or the action picture.
- Chains: number cards with the same number can go down together (any colors). The last card sets the color.
- Flip reverses the direction. Hop skips the next player. Pulse makes the next player draw 2, unless they answer with a Pulse of their own (it stacks: +4, +6...).
- Prism plays on anything (except a Pulse you owe) and you pick the next color.
- Can't or don't want to play? Keep drawing until you get a card you can play (it matches the color, the number or the action picture, or it is a Prism). Every card you draw stays in your hand. Then you may play that card straight away or keep it. If the deck runs out first, your turn ends.
- One card left: "Last light!" First to empty their hand wins. If nobody can play or draw, the fewest cards wins.

## Pebble Drop rules

- Four players take turns in order: Red, Yellow, Blue, Green. Each turn, drop one pebble into any column that isn't full; it lands on the lowest empty space.
- The first to line up four of their own pebbles across, up and down, or on either diagonal wins. The winning pebbles glow and a line is drawn through them.
- If all 192 spaces fill with no line, it's a draw.
- New Game clears the board and keeps the score (the next player starts). Reset also clears the score and Red starts. Undo takes back the last person's move.
- Tap a player card to switch that seat between a person and a bot. Hints marks columns where the current player can win right now.

## Treehouse Hold'em rules

A kid-friendly Texas Hold'em for a classroom or family game night. Coins are pretend play money: there is nothing to buy, win or cash in.

- 4 seats: 1–4 people take turns on one device; computer friends (Ollie Owl, Fifi Fox, Benny Bear, Bea Bunny) fill the rest.
- Everyone starts with 100 coins. Coins come in four colors with their value printed on them: Sunny 1, Berry 5, Ocean 10, Grape 25.
- Each round: everyone puts 2 coins in the prize pile and gets 2 secret cards. Shared cards come out in the middle: 3, then 1, then 1.
- Before each new shared card (and after the last), everyone takes a turn: Check (stay in free), Call (match), Raise (add 5 coins early, 10 later; at most 3 raises each time) or Fold (sit out the round).
- Best 5-card hand from your 2 cards plus the 5 shared cards wins the prize pile. Ties share it. A player who runs out of coins can only win the coins they matched.
- After 8 rounds (or when only one player has coins), the most coins wins.
- Optional Hints show your best hand, glow the cards that make it, and give a 1–5 star strength meter with friendly advice. The Rules button explains everything, with example cards for every hand.

## Online play

- The game server lives in `Server/` and runs on Render at `wss://lantern-lounge.onrender.com/ws` (see `Server/README.md`).
- The app's connection code is in `Client/Runtime/Online/`. To point the app at a different server, change `OnlineSession.ServerUrl`.
- **Website:** `https://lantern-lounge.onrender.com` plays all five games online in any browser: Tidebound, Prism Rush, Pebble Drop, Treehouse Hold'em and Hide & Seek. Invite links (`/#CODE`) open straight to the join screen, and refreshing the page puts you back in your seat.
- **In the Unity app, works now:** Pebble Drop online. Create a room, share the 5-letter code, friends join from their own devices, the host starts, and bots fill empty seats. The app reconnects automatically after a short drop.
- **Chat and switching games (website):** every game except Pebble Drop has a table chat, and the host can change the game from the waiting room, or with "Change game" during or after a match, without remaking the group. Everyone keeps their seat and the same room code. The Unity app does not have these yet.
- **Hide & Seek (website only):** a real-time, 4-player maze game seen from above. One seeker (blindfolded for the first 30 seconds), three hiders. Players pick a Red Panda, Siamese Cat, Honey Badger or Brown Cat in the waiting room. Controls are a thumb stick plus one button (Hide / Come out / Grab); arrows or WASD and Space work on a keyboard. The seeker wins by catching every hider; hiders still free when the 2:30 seeking time ends win. The seeker role rotates on each rematch. The Unity app does not have this game.
- **Next:** online tables for Tidebound, Prism Rush and Treehouse Hold'em in the Unity app. The server already supports all four.
- **Free server note:** after 15 idle minutes the server sleeps. The first connection then takes up to a minute.

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
