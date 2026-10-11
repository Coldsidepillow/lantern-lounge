using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Ui
{
    /// <summary>Lounge colors plus small helpers for building styled elements in code.</summary>
    internal static class Theme
    {
        // Lounge shell
        public static readonly Color Ground = Hex("#17141F");
        public static readonly Color Surface = Hex("#241F30");
        public static readonly Color Line = Hex("#3A3348");
        public static readonly Color Text = Hex("#F5EFE6");
        public static readonly Color Muted = Hex("#BDB3C9");
        public static readonly Color Amber = Hex("#FFB547");
        public static readonly Color Ink = Hex("#17141F");
        public static readonly Color Good = Hex("#8FE3A6");

        // Game tile colors
        public static readonly Color Tidebound = Hex("#0C2F4A");
        public static readonly Color Slate = Hex("#2E3A46");
        public static readonly Color Treehouse = Hex("#2E8B4E");
        public static readonly Color Prism = Hex("#2A1B47");

        // Pebble Drop
        public static readonly Color PebbleGround = Hex("#1E2830");
        public static readonly Color Board = Hex("#22303B");
        public static readonly Color BoardEdge = Hex("#4A5866");
        public static readonly Color Hole = Hex("#15202A");
        public static readonly Color Ember = Hex("#E8734E");
        public static readonly Color EmberLight = Hex("#F09A7C");
        public static readonly Color Moss = Hex("#A7D16B");
        public static readonly Color MossLight = Hex("#C3E39A");
        public static readonly Color Cream = Hex("#FFF4E0");
        public static readonly Color DrawGrey = Hex("#C9D3DC");

        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;
        }

        public static Color TileColor(string themeKey)
        {
            switch (themeKey)
            {
                case "tidebound": return Tidebound;
                case "pebble-drop": return Slate;
                case "treehouse-holdem": return Treehouse;
                case "prism-rush": return Prism;
                default: return Surface;
            }
        }

        public static Label Label(string text, float size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            return label;
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static VisualElement Column()
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement e, float width, Color color)
        {
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            BorderColor(e, color);
        }

        public static void BorderColor(VisualElement e, Color color)
        {
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
        }

        public static void Padding(VisualElement e, float vertical, float horizontal)
        {
            e.style.paddingTop = vertical;
            e.style.paddingBottom = vertical;
            e.style.paddingLeft = horizontal;
            e.style.paddingRight = horizontal;
        }

        public static void Margin(VisualElement e, float top, float right, float bottom, float left)
        {
            e.style.marginTop = top;
            e.style.marginRight = right;
            e.style.marginBottom = bottom;
            e.style.marginLeft = left;
        }

        public static void Circle(VisualElement e, float size)
        {
            e.style.width = size;
            e.style.height = size;
            Radius(e, size / 2f);
        }

        /// <summary>A card-style surface with the lounge border.</summary>
        public static VisualElement Card()
        {
            var card = Column();
            card.style.backgroundColor = Surface;
            Border(card, 1, Line);
            Radius(card, 18);
            Padding(card, 12, 14);
            return card;
        }

        /// <summary>Main call to action: amber, dark text, 56 tall.</summary>
        public static Button PrimaryButton(string text, Action onClick)
        {
            var button = BaseButton(text, onClick, 56, 18);
            button.style.backgroundColor = Amber;
            button.style.color = Ink;
            Border(button, 2, Amber);
            FocusRing(button, Amber, Text);
            return button;
        }

        /// <summary>Quieter action: outlined.</summary>
        public static Button SecondaryButton(string text, Action onClick)
        {
            var button = BaseButton(text, onClick, 52, 15);
            button.style.backgroundColor = new Color(0, 0, 0, 0);
            button.style.color = Text;
            Border(button, 2, Muted);
            FocusRing(button, Muted, Amber);
            return button;
        }

        public static void SetButtonEnabled(Button button, bool enabled)
        {
            button.SetEnabled(enabled);
            button.style.opacity = enabled ? 1f : 0.45f;
        }

        /// <summary>Keyboard and controller users see where focus is: the border changes color.</summary>
        public static void FocusRing(VisualElement e, Color normal, Color focused)
        {
            e.focusable = true;
            e.RegisterCallback<FocusInEvent>(_ => BorderColor(e, focused));
            e.RegisterCallback<FocusOutEvent>(_ => BorderColor(e, normal));
        }

        private static Button BaseButton(string text, Action onClick, float height, float fontSize)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = height;
            button.style.minHeight = 48;
            button.style.fontSize = fontSize;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            Radius(button, 18);
            Padding(button, 0, 16);
            Margin(button, 0, 0, 0, 0);
            return button;
        }
    }
}
