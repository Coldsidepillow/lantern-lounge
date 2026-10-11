using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>The lounge home: every game in the registry, with what can be played today.</summary>
    internal sealed class LoungeScreen
    {
        private readonly LoungeApp app;
        private Button motionButton;

        public LoungeScreen(LoungeApp app)
        {
            this.app = app;
        }

        public VisualElement Build()
        {
            var screen = Theme.Column();
            Theme.Padding(screen, 0, 16);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            screen.Add(scroll);

            var title = Theme.Label("Lantern Lounge", 28, Theme.Text, bold: true);
            Theme.Margin(title, 28, 0, 4, 0);
            scroll.Add(title);

            var subtitle = Theme.Label("Pick a table.", 15, Theme.Muted);
            Theme.Margin(subtitle, 0, 0, 16, 0);
            scroll.Add(subtitle);

            foreach (var info in app.Registry.All)
            {
                scroll.Add(BuildTile(info));
            }

            var note = Theme.Label(
                "Online rooms, profiles and playable Tidebound are the next steps. Everything here runs on this device only.",
                12, Theme.Muted);
            Theme.Margin(note, 4, 4, 16, 4);
            scroll.Add(note);

            scroll.Add(BuildSettingsCard());
            return screen;
        }

        private VisualElement BuildTile(GameInfo info)
        {
            var tile = Theme.Card();
            Theme.Margin(tile, 0, 0, 12, 0);
            Theme.Padding(tile, 0, 0);
            tile.style.overflow = Overflow.Hidden;

            var art = new VisualElement();
            art.style.height = 72;
            art.style.backgroundColor = Theme.TileColor(info.ThemeKey);
            art.style.alignItems = Align.Center;
            art.style.justifyContent = Justify.Center;
            art.Add(BuildArt(info.Id));
            tile.Add(art);

            var body = Theme.Column();
            Theme.Padding(body, 12, 14);
            tile.Add(body);

            var nameRow = Theme.Row();
            nameRow.style.justifyContent = Justify.SpaceBetween;
            nameRow.Add(Theme.Label(info.DisplayName, 20, Theme.Text, bold: true));
            nameRow.Add(StatusChip(info));
            body.Add(nameRow);

            string players = info.MinPlayers == info.MaxPlayers
                ? $"{info.MinPlayers} players"
                : $"{info.MinPlayers}–{info.MaxPlayers} players";
            var meta = Theme.Label($"{GameCatalog.CategoryOf(info.Id)} · {players}", 13, Theme.Muted);
            Theme.Margin(meta, 4, 0, 0, 0);
            body.Add(meta);

            if (app.Registry.TryGetModule(info.Id, out _) && info.Id == PebbleDropModule.GameId)
            {
                var buttons = Theme.Row();
                Theme.Margin(buttons, 12, 0, 0, 0);
                var bot = Theme.PrimaryButton("Play the bot", () => app.ShowPebbleDrop(againstBot: true));
                bot.style.flexGrow = 1;
                Theme.Margin(bot, 0, 8, 0, 0);
                var pass = Theme.SecondaryButton("Pass and play", () => app.ShowPebbleDrop(againstBot: false));
                pass.style.flexGrow = 1;
                buttons.Add(bot);
                buttons.Add(pass);
                body.Add(buttons);
            }
            else if (info.Status == GameStatus.Preview)
            {
                var detail = Theme.Label("Designed and coming soon: its rules are being built next.", 13, Theme.Muted);
                Theme.Margin(detail, 8, 0, 0, 0);
                body.Add(detail);
            }

            return tile;
        }

        private static VisualElement StatusChip(GameInfo info)
        {
            string text;
            Color color;
            switch (info.Status)
            {
                case GameStatus.Playable:
                    text = "Playable";
                    color = Theme.Good;
                    break;
                case GameStatus.Preview:
                    text = "Preview";
                    color = Theme.Amber;
                    break;
                default:
                    text = "Coming soon";
                    color = Theme.Muted;
                    break;
            }

            var chip = Theme.Label(text, 12, color, bold: true);
            Theme.Border(chip, 1, color);
            Theme.Radius(chip, 12);
            Theme.Padding(chip, 3, 10);
            return chip;
        }

        // Simple shape art per game, so tiles are told apart by more than color.
        private static VisualElement BuildArt(string gameId)
        {
            var row = Theme.Row();
            switch (gameId)
            {
                case PebbleDropModule.GameId:
                    row.Add(Dot(Theme.Ember, 22));
                    row.Add(Dot(Theme.Moss, 22));
                    row.Add(Dot(Theme.Ember, 22));
                    row.Add(Dot(Theme.Moss, 22));
                    break;
                case GameCatalog.TideboundId:
                    var jelly = new VisualElement();
                    jelly.style.width = 44;
                    jelly.style.height = 28;
                    jelly.style.backgroundColor = Theme.Hex("#B7A3FF");
                    jelly.style.borderTopLeftRadius = 22;
                    jelly.style.borderTopRightRadius = 22;
                    jelly.style.borderBottomLeftRadius = 6;
                    jelly.style.borderBottomRightRadius = 6;
                    row.Add(jelly);
                    break;
                case GameCatalog.FoglineFleetId:
                    var ring = new VisualElement();
                    Theme.Circle(ring, 30);
                    Theme.Border(ring, 3, Theme.Hex("#FF8A3D"));
                    row.Add(ring);
                    break;
                default:
                    row.Add(Card(Theme.Hex("#3FD6FF")));
                    row.Add(Card(Theme.Hex("#FFD23F")));
                    row.Add(Card(Theme.Hex("#FF5FA2")));
                    break;
            }
            return row;
        }

        private static VisualElement Dot(Color color, float size)
        {
            var dot = new VisualElement();
            Theme.Circle(dot, size);
            dot.style.backgroundColor = color;
            Theme.Margin(dot, 0, 4, 0, 4);
            return dot;
        }

        private static VisualElement Card(Color color)
        {
            var card = new VisualElement();
            card.style.width = 22;
            card.style.height = 32;
            card.style.backgroundColor = color;
            Theme.Radius(card, 5);
            Theme.Margin(card, 0, 3, 0, 3);
            return card;
        }

        private VisualElement BuildSettingsCard()
        {
            var card = Theme.Card();
            Theme.Margin(card, 0, 0, 24, 0);
            card.Add(Theme.Label("Accessibility", 16, Theme.Text, bold: true));

            var row = Theme.Row();
            row.style.justifyContent = Justify.SpaceBetween;
            Theme.Margin(row, 8, 0, 0, 0);

            var text = Theme.Column();
            text.style.flexShrink = 1;
            text.Add(Theme.Label("Reduce motion", 14, Theme.Text));
            text.Add(Theme.Label("Pebbles appear instead of falling", 12, Theme.Muted));
            row.Add(text);

            motionButton = Theme.SecondaryButton(MotionLabel(), ToggleMotion);
            motionButton.style.width = 88;
            row.Add(motionButton);

            card.Add(row);
            return card;
        }

        private void ToggleMotion()
        {
            Prefs.ReduceMotion = !Prefs.ReduceMotion;
            motionButton.text = MotionLabel();
        }

        private static string MotionLabel() => Prefs.ReduceMotion ? "On" : "Off";
    }
}
