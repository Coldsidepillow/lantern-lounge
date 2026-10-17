using System;
using System.Text;
using LanternLounge.Client.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Online
{
    /// <summary>
    /// Play a game online with friends: type your name, then create a room (and share its code)
    /// or join one with a code. The waiting room shows who's here; the host starts the game and
    /// bots fill any empty seats.
    /// </summary>
    internal sealed class OnlineScreen
    {
        private const string NameKey = "lanternlounge.onlineName";
        private static readonly Color[] SeatColors =
        {
            Theme.Hex("#E5484D"), Theme.Hex("#F5C518"), Theme.Hex("#3E8EF7"), Theme.Hex("#2FB86B")
        };

        private readonly LoungeApp app;
        private readonly OnlineSession session;
        private readonly string gameId;
        private readonly string gameName;

        private VisualElement screen;
        private VisualElement body;
        private TextField nameField;
        private TextField codeField;
        private Label statusLabel;
        private Button createButton;
        private Button joinButton;
        private bool opening;
        private bool showingRoom;

        public OnlineScreen(LoungeApp app, OnlineSession session, string gameId, string gameName)
        {
            this.app = app;
            this.session = session;
            this.gameId = gameId;
            this.gameName = gameName;
        }

        public VisualElement Build()
        {
            screen = Theme.Column();
            screen.style.backgroundColor = Theme.Ground;
            Theme.Padding(screen, 0, 16);

            var header = Theme.Row();
            Theme.Margin(header, 14, 0, 0, 0);
            var back = Theme.SecondaryButton("<", Back);
            back.style.width = 52;
            back.tooltip = "Back to the lounge";
            header.Add(back);
            var title = Theme.Label($"{gameName} online", 22, Theme.Text, bold: true);
            Theme.Margin(title, 0, 0, 0, 12);
            title.style.flexShrink = 1;
            header.Add(title);
            screen.Add(header);

            body = Theme.Column();
            body.style.flexGrow = 1;
            Theme.Margin(body, 16, 0, 16, 0);
            screen.Add(body);

            session.RoomChanged += OnRoom;
            session.Error += OnError;
            session.StatusChanged += OnStatus;
            session.Left += OnLeft;
            screen.RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());

            if (session.InRoom && session.Room.Game == gameId) ShowRoom(session.Room);
            else
            {
                ShowStart();
                if (!string.IsNullOrEmpty(session.LastLeaveReason)) SetStatus(session.LastLeaveReason);
            }
            return screen;
        }

        private void Unsubscribe()
        {
            session.RoomChanged -= OnRoom;
            session.Error -= OnError;
            session.StatusChanged -= OnStatus;
            session.Left -= OnLeft;
        }

        // ---------- Step 1: name, create or join ----------

        private void ShowStart()
        {
            showingRoom = false;
            body.Clear();

            var intro = Theme.Label("Play with friends on their own phones. One person makes a room and shares the code; everyone else joins with it. Bots fill any empty seats.", 14, Theme.Muted);
            body.Add(intro);

            body.Add(Heading("Your name"));
            nameField = new TextField { maxLength = 16, value = PlayerPrefs.GetString(NameKey, "") };
            nameField.textEdition.placeholder = "e.g. Sam";
            StyleField(nameField);
            body.Add(nameField);

            createButton = Theme.PrimaryButton("Create a room", Create);
            Theme.Margin(createButton, 16, 0, 0, 0);
            body.Add(createButton);

            var or = Theme.Label("or join a friend's room", 13, Theme.Muted);
            or.style.unityTextAlign = TextAnchor.MiddleCenter;
            Theme.Margin(or, 18, 0, 6, 0);
            body.Add(or);

            var joinRow = Theme.Row();
            codeField = new TextField { maxLength = 5 };
            codeField.textEdition.placeholder = "CODE";
            StyleField(codeField);
            codeField.style.flexGrow = 1;
            codeField.style.fontSize = 22;
            codeField.RegisterValueChangedCallback(e =>
            {
                string upper = CleanCode(e.newValue);
                if (upper != e.newValue) codeField.SetValueWithoutNotify(upper);
            });
            joinRow.Add(codeField);
            joinButton = Theme.SecondaryButton("Join", Join);
            joinButton.style.width = 96;
            Theme.Margin(joinButton, 0, 0, 0, 8);
            joinRow.Add(joinButton);
            body.Add(joinRow);

            statusLabel = Theme.Label("", 14, Theme.Amber, bold: true);
            Theme.Margin(statusLabel, 16, 0, 0, 0);
            body.Add(statusLabel);
            UpdateButtons();
        }

        private void Create()
        {
            if (opening) return;
            string name = SaveName();
            opening = true;
            UpdateButtons();
            SetStatus("Connecting... If nobody has played for a while, the server takes up to a minute to wake up.");
            session.Create(gameId, name);
        }

        private void Join()
        {
            if (opening) return;
            string code = CleanCode(codeField.value);
            if (code.Length != 5)
            {
                SetStatus("Room codes have 5 letters and numbers.");
                return;
            }
            string name = SaveName();
            opening = true;
            UpdateButtons();
            SetStatus("Connecting... If nobody has played for a while, the server takes up to a minute to wake up.");
            session.Join(code, name);
        }

        private string SaveName()
        {
            string name = (nameField?.value ?? "").Trim();
            PlayerPrefs.SetString(NameKey, name);
            PlayerPrefs.Save();
            return name;
        }

        private void UpdateButtons()
        {
            if (createButton != null) Theme.SetButtonEnabled(createButton, !opening);
            if (joinButton != null) Theme.SetButtonEnabled(joinButton, !opening);
        }

        // ---------- Step 2: the waiting room ----------

        private void ShowRoom(RoomSnapshot room)
        {
            showingRoom = true;
            body.Clear();

            body.Add(Theme.Label("Room code", 14, Theme.Muted));
            var codeRow = Theme.Row();
            Theme.Margin(codeRow, 6, 0, 4, 0);
            foreach (char c in room.Code)
            {
                var box = Theme.Label(c.ToString(), 34, Theme.Ink, bold: true);
                box.style.width = 54;
                box.style.height = 64;
                box.style.unityTextAlign = TextAnchor.MiddleCenter;
                box.style.backgroundColor = Theme.Amber;
                Theme.Radius(box, 12);
                Theme.Margin(box, 0, 6, 0, 0);
                codeRow.Add(box);
            }
            body.Add(codeRow);
            body.Add(Theme.Label("Tell your friends this code so they can join.", 13, Theme.Muted));

            body.Add(Heading("Players"));
            foreach (var seat in room.Seats)
            {
                var row = Theme.Card();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                Theme.Margin(row, 0, 0, 8, 0);
                var dot = new VisualElement();
                Theme.Circle(dot, 22);
                dot.style.backgroundColor = SeatColors[seat.Seat % SeatColors.Length];
                row.Add(dot);

                string label;
                Color color = Theme.Text;
                if (seat.IsHuman)
                {
                    label = seat.Name + (seat.Seat == room.You ? " (you)" : "") + (seat.IsHost ? " · host" : "");
                    if (!seat.Connected)
                    {
                        label += " · reconnecting";
                        color = Theme.Muted;
                    }
                }
                else
                {
                    label = "Empty: a bot will play";
                    color = Theme.Muted;
                }
                var text = Theme.Label(label, 15, color, bold: seat.IsHuman);
                Theme.Margin(text, 0, 0, 0, 10);
                text.style.flexShrink = 1;
                row.Add(text);
                body.Add(row);
            }

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            body.Add(spacer);

            if (room.IsHost)
            {
                var start = Theme.PrimaryButton("Start the game", () =>
                {
                    Sfx.Play(Sound.Select);
                    session.StartGame();
                });
                body.Add(start);
            }
            else
            {
                var wait = Theme.Label("Waiting for the host to start the game...", 15, Theme.Text, bold: true);
                wait.style.unityTextAlign = TextAnchor.MiddleCenter;
                body.Add(wait);
            }

            statusLabel = Theme.Label("", 14, Theme.Amber, bold: true);
            Theme.Margin(statusLabel, 8, 0, 0, 0);
            body.Add(statusLabel);

            var leave = Theme.SecondaryButton("Leave room", Back);
            Theme.Margin(leave, 10, 0, 0, 0);
            body.Add(leave);
        }

        // ---------- Session events ----------

        private void OnRoom(RoomSnapshot room)
        {
            opening = false;
            if (room.InGame)
            {
                app.ShowOnlineGame(room.Game);
                return;
            }
            ShowRoom(room);
        }

        private void OnError(string request, string code, string message)
        {
            opening = false;
            if (!session.InRoom && showingRoom) ShowStart();
            UpdateButtons();
            SetStatus(message);
        }

        private void OnStatus(OnlineStatus status)
        {
            if (status == OnlineStatus.Offline && opening)
            {
                opening = false;
                UpdateButtons();
                SetStatus("Lost the connection. Please try again.");
                return;
            }
            if (status == OnlineStatus.Reconnecting) SetStatus("Connection lost. Reconnecting...");
            else if (status == OnlineStatus.Connected && session.InRoom) SetStatus("");
        }

        private void OnLeft()
        {
            opening = false;
            ShowStart();
            if (!string.IsNullOrEmpty(session.LastLeaveReason)) SetStatus(session.LastLeaveReason);
        }

        private void Back()
        {
            Unsubscribe();
            if (session.InRoom || opening || session.Status != OnlineStatus.Offline) session.Leave();
            app.ShowLounge();
        }

        // ---------- Helpers ----------

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.text = text;
        }

        private static Label Heading(string text)
        {
            var label = Theme.Label(text, 16, Theme.Text, bold: true);
            Theme.Margin(label, 18, 0, 6, 0);
            return label;
        }

        private static void StyleField(TextField field)
        {
            field.style.height = 52;
            field.style.fontSize = 18;
            Theme.Margin(field, 0, 0, 0, 0);
        }

        private static string CleanCode(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in (text ?? "").ToUpperInvariant())
            {
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                if (sb.Length == 5) break;
            }
            return sb.ToString();
        }
    }
}
