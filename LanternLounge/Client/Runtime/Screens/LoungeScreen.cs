using LanternLounge.Client.Ui;
using LanternLounge.Core;
using LanternLounge.Games.PebbleDrop;
using LanternLounge.Games.TreehouseHoldem;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Screens
{
    /// <summary>The lounge home: every game in the registry, with what can be played today.</summary>
    internal sealed class LoungeScreen
    {
        private readonly LoungeApp app;
        private Button motionButton;
        private Button soundButton;

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
                "Online rooms and profiles are next. Everything here runs on this device only.",
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
            if (info.Id == GameCatalog.TreehouseHoldemId) players = "1–4 players · computer friends fill empty seats";
            if (info.Id == PebbleDropModule.GameId) players = "4 players · 16 × 12 board · people or bots";
            var meta = Theme.Label($"{GameCatalog.CategoryOf(info.Id)} · {players}", 13, Theme.Muted);
            Theme.Margin(meta, 4, 0, 0, 0);
            body.Add(meta);

            if (info.Id == GameCatalog.TideboundId && app.Registry.TryGetModule(info.Id, out _))
            {
                var dive = Theme.PrimaryButton("Dive in · you + 3 Reef Bots", app.ShowTidebound);
                Theme.Margin(dive, 12, 0, 0, 0);
                body.Add(dive);
            }
            else if (info.Id == GameCatalog.TreehouseHoldemId && app.Registry.TryGetModule(info.Id, out _))
            {
                body.Add(BuildHoldemStart());
            }
            else if (info.Id == GameCatalog.PrismRushId && app.Registry.TryGetModule(info.Id, out _))
            {
                var play = Theme.PrimaryButton("Play · you + 3 Prism Pals", app.ShowPrismRush);
                Theme.Margin(play, 12, 0, 0, 0);
                body.Add(play);
            }
            else if (app.Registry.TryGetModule(info.Id, out _) && info.Id == PebbleDropModule.GameId)
            {
                var buttons = Theme.Row();
                Theme.Margin(buttons, 12, 0, 0, 0);
                var bot = Theme.PrimaryButton("You + 3 bots", () => app.ShowPebbleDrop(1));
                bot.style.flexGrow = 1;
                Theme.Margin(bot, 0, 8, 0, 0);
                var pass = Theme.SecondaryButton("4 people", () => app.ShowPebbleDrop(4));
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

        // "How many players?" chips, then a start button. Computer friends fill the other seats.
        private int holdemHumans = 1;

        private VisualElement BuildHoldemStart()
        {
            var box = Theme.Column();
            Theme.Margin(box, 12, 0, 0, 0);

            var ask = Theme.Label("How many people are playing on this device?", 13, Theme.Text);
            box.Add(ask);

            var chips = Theme.Row();
            Theme.Margin(chips, 6, 0, 8, 0);
            var buttons = new Button[4];
            for (int n = 1; n <= 4; n++)
            {
                int count = n;
                var chip = new Button(() =>
                {
                    holdemHumans = count;
                    Sfx.Play(Sound.Tap);
                    StyleHoldemChips(buttons);
                }) { text = n.ToString() };
                chip.style.width = 52;
                chip.style.height = 48;
                chip.style.fontSize = 20;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                Theme.Radius(chip, 14);
                Theme.Margin(chip, 0, 8, 0, 0);
                chip.tooltip = n == 1 ? "Just you, with 3 computer friends" : $"{n} people taking turns on this device";
                buttons[n - 1] = chip;
                chips.Add(chip);
            }
            StyleHoldemChips(buttons);
            box.Add(chips);

            var start = Theme.PrimaryButton("Start game night!", () => app.ShowTreehouseHoldem(holdemHumans));
            box.Add(start);
            return box;
        }

        private void StyleHoldemChips(Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                bool on = i + 1 == holdemHumans;
                buttons[i].style.backgroundColor = on ? Theme.Good : Theme.Surface;
                buttons[i].style.color = on ? Theme.Ink : Theme.Text;
                Theme.Border(buttons[i], 2, on ? Theme.Good : Theme.Muted);
            }
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
                    row.Add(Dot(Theme.Hex("#E5484D"), 22));
                    row.Add(Dot(Theme.Hex("#F5C518"), 22));
                    row.Add(Dot(Theme.Hex("#3E8EF7"), 22));
                    row.Add(Dot(Theme.Hex("#2FB86B"), 22));
                    break;
                case GameCatalog.TideboundId:
                    row.Add(new CreatureArt(0, 66, 54));
                    row.Add(new CreatureArt(2, 66, 54));
                    row.Add(new CreatureArt(8, 66, 54));
                    row.Add(new CreatureArt(4, 66, 54));
                    break;
                case GameCatalog.TreehouseHoldemId:
                {
                    var ace = HoldemCardArt.Face(HoldemCards.Id(HoldemCards.Ace, Suit.Hearts), 38, 54);
                    ace.style.rotate = new StyleRotate(new Rotate(new Angle(-8f, AngleUnit.Degree)));
                    row.Add(ace);
                    var king = HoldemCardArt.Face(HoldemCards.Id(HoldemCards.King, Suit.Spades), 38, 54);
                    king.style.rotate = new StyleRotate(new Rotate(new Angle(8f, AngleUnit.Degree)));
                    king.style.marginLeft = -8;
                    row.Add(king);
                    var coins = CoinArt.Pile(41, 24);
                    Theme.Margin(coins, 0, 0, 0, 12);
                    row.Add(coins);
                    break;
                }
                case GameCatalog.PrismRushId:
                    // A little fan of real Prism Rush cards.
                    int[] cards = { 4, 28, 54, 80 };
                    for (int i = 0; i < cards.Length; i++)
                    {
                        var face = PrismCardArt.Face(cards[i], 36, 52);
                        face.style.marginLeft = i == 0 ? 0 : -8;
                        face.style.rotate = new StyleRotate(new Rotate(new Angle((i - 1.5f) * 9f, AngleUnit.Degree)));
                        row.Add(face);
                    }
                    break;
                default:
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

            var soundRow = Theme.Row();
            soundRow.style.justifyContent = Justify.SpaceBetween;
            Theme.Margin(soundRow, 10, 0, 0, 0);
            var soundText = Theme.Column();
            soundText.style.flexShrink = 1;
            soundText.Add(Theme.Label("Sound effects", 14, Theme.Text));
            soundText.Add(Theme.Label("Plinks, splashes and chimes", 12, Theme.Muted));
            soundRow.Add(soundText);
            soundButton = Theme.SecondaryButton(SoundLabel(), ToggleSound);
            soundButton.style.width = 88;
            soundRow.Add(soundButton);
            card.Add(soundRow);
            return card;
        }

        private void ToggleMotion()
        {
            Prefs.ReduceMotion = !Prefs.ReduceMotion;
            motionButton.text = MotionLabel();
            Sfx.Play(Sound.Tap);
        }

        private void ToggleSound()
        {
            Prefs.SoundOn = !Prefs.SoundOn;
            soundButton.text = SoundLabel();
            Sfx.Play(Sound.Select);
        }

        private static string MotionLabel() => Prefs.ReduceMotion ? "On" : "Off";
        private static string SoundLabel() => Prefs.SoundOn ? "On" : "Off";
    }
}
