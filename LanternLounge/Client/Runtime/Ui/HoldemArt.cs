using LanternLounge.Games.TreehouseHoldem;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Ui
{
    /// <summary>Treehouse Hold'em colors: a sunny treehouse game night, with one bright color per suit.</summary>
    internal static class HoldemPalette
    {
        public static readonly Color Ink = Theme.Hex("#2B2140");
        public static readonly Color Sky = Theme.Hex("#BFE6FF");
        public static readonly Color Leaf = Theme.Hex("#5CC46A");
        public static readonly Color LeafDark = Theme.Hex("#2E8B4E");
        public static readonly Color Wood = Theme.Hex("#C98B4F");
        public static readonly Color WoodDark = Theme.Hex("#8A5A2E");
        public static readonly Color Mat = Theme.Hex("#E9F7D9");
        public static readonly Color Paper = Theme.Hex("#FFFDF7");
        public static readonly Color Sun = Theme.Hex("#FFD23F");
        public static readonly Color Peach = Theme.Hex("#FFB38A");
        public static readonly Color Clear = new Color(0, 0, 0, 0);

        // Four-color deck: every suit is its own color AND its own shape.
        private static readonly Color[] SuitColors =
        {
            Theme.Hex("#F0386B"), // Hearts   - pink-red
            Theme.Hex("#FF8A1F"), // Diamonds - orange
            Theme.Hex("#22A85A"), // Clubs    - green
            Theme.Hex("#3D6BFF")  // Spades   - blue
        };

        public static Color SuitColor(Suit suit) => SuitColors[(int)suit];

        // Play-money coins: each value has its own color and its number printed on it.
        public static readonly int[] CoinValues = { 25, 10, 5, 1 };

        public static Color CoinColor(int value)
        {
            switch (value)
            {
                case 25: return Theme.Hex("#9B6BFF"); // grape
                case 10: return Theme.Hex("#34B6F0"); // ocean
                case 5: return Theme.Hex("#FF6F91");  // berry
                default: return Theme.Hex("#FFC93C"); // sunny
            }
        }

        public static string CoinName(int value)
        {
            switch (value)
            {
                case 25: return "Grape 25";
                case 10: return "Ocean 10";
                case 5: return "Berry 5";
                default: return "Sunny 1";
            }
        }
    }

    /// <summary>A suit symbol, optionally with a friendly face.</summary>
    internal sealed class SuitArt : PaintedArt
    {
        private readonly Suit suit;
        private readonly bool face;

        public SuitArt(Suit suit, float size, bool face = false) : base(size, size)
        {
            this.suit = suit;
            this.face = face;
        }

        protected override void Paint(Painter2D p)
        {
            Color fill = HoldemPalette.SuitColor(suit);
            p.strokeColor = HoldemPalette.Ink;
            switch (suit)
            {
                case Suit.Hearts:
                    p.BeginPath();
                    p.MoveTo(P(50, 90));
                    p.BezierCurveTo(P(12, 62), P(2, 30), P(28, 16));
                    p.BezierCurveTo(P(42, 10), P(50, 22), P(50, 30));
                    p.BezierCurveTo(P(50, 22), P(58, 10), P(72, 16));
                    p.BezierCurveTo(P(98, 30), P(88, 62), P(50, 90));
                    p.ClosePath();
                    p.fillColor = fill;
                    p.Fill();
                    p.Stroke();
                    break;
                case Suit.Diamonds:
                    Polygon(p, fill, true, V(50, 6), V(84, 50), V(50, 94), V(16, 50));
                    break;
                case Suit.Clubs:
                    Polygon(p, fill, true, V(50, 52), V(36, 94), V(64, 94));
                    Ellipse(p, 50, 30, 19, 19, fill);
                    Ellipse(p, 29, 58, 19, 19, fill);
                    Ellipse(p, 71, 58, 19, 19, fill);
                    // Hide the inner outlines so the three leaves look like one shape.
                    Ellipse(p, 50, 50, 14, 14, fill, false);
                    break;
                default:
                    Polygon(p, fill, true, V(50, 58), V(36, 94), V(64, 94));
                    p.BeginPath();
                    p.MoveTo(P(50, 6));
                    p.BezierCurveTo(P(86, 38), P(98, 60), P(74, 72));
                    p.BezierCurveTo(P(60, 78), P(52, 70), P(50, 64));
                    p.BezierCurveTo(P(48, 70), P(40, 78), P(26, 72));
                    p.BezierCurveTo(P(2, 60), P(14, 38), P(50, 6));
                    p.ClosePath();
                    p.fillColor = fill;
                    p.Fill();
                    p.Stroke();
                    break;
            }

            if (!face) return;
            float eyeY = suit == Suit.Clubs ? 46 : suit == Suit.Spades ? 48 : 46;
            Ellipse(p, 41, eyeY, 4.5f, 5.5f, HoldemPalette.Ink, false);
            Ellipse(p, 59, eyeY, 4.5f, 5.5f, HoldemPalette.Ink, false);
            Ellipse(p, 39.8f, eyeY - 1.8f, 1.6f, 1.6f, Color.white, false);
            Ellipse(p, 57.8f, eyeY - 1.8f, 1.6f, 1.6f, Color.white, false);
            Ellipse(p, 33, eyeY + 9, 4, 2.5f, new Color(1f, 1f, 1f, 0.45f), false);
            Ellipse(p, 67, eyeY + 9, 4, 2.5f, new Color(1f, 1f, 1f, 0.45f), false);
            p.lineWidth = Mathf.Max(1.2f, S(3.5f));
            p.BeginPath();
            p.MoveTo(P(42, eyeY + 10));
            p.QuadraticCurveTo(P(50, eyeY + 18), P(58, eyeY + 10));
            p.Stroke();
        }
    }

    /// <summary>The little pictures on picture cards: a crown (King), a tiara (Queen) and a star (Jack).</summary>
    internal sealed class FaceIcon : PaintedArt
    {
        private readonly int rank;

        public FaceIcon(int rank, float size) : base(size, size)
        {
            this.rank = rank;
        }

        protected override void Paint(Painter2D p)
        {
            Color gold = HoldemPalette.Sun;
            switch (rank)
            {
                case HoldemCards.King:
                    Polygon(p, gold, true, V(12, 78), V(12, 30), V(32, 50), V(50, 18), V(68, 50), V(88, 30), V(88, 78));
                    Ellipse(p, 30, 66, 6, 6, Theme.Hex("#F0386B"));
                    Ellipse(p, 50, 66, 6, 6, Theme.Hex("#3D6BFF"));
                    Ellipse(p, 70, 66, 6, 6, Theme.Hex("#22A85A"));
                    break;
                case HoldemCards.Queen:
                    p.BeginPath();
                    p.MoveTo(P(10, 76));
                    p.LineTo(P(20, 42));
                    p.LineTo(P(36, 58));
                    p.LineTo(P(50, 28));
                    p.LineTo(P(64, 58));
                    p.LineTo(P(80, 42));
                    p.LineTo(P(90, 76));
                    p.QuadraticCurveTo(P(50, 64), P(10, 76));
                    p.ClosePath();
                    p.fillColor = gold;
                    p.Fill();
                    p.Stroke();
                    Ellipse(p, 50, 22, 7, 7, Theme.Hex("#FF6F91"));
                    Ellipse(p, 20, 38, 5, 5, Theme.Hex("#9B6BFF"));
                    Ellipse(p, 80, 38, 5, 5, Theme.Hex("#9B6BFF"));
                    break;
                default:
                {
                    var pts = new Vector2[10];
                    for (int i = 0; i < 10; i++) pts[i] = Polar(50, 52, i % 2 == 0 ? 44 : 19, -90 + i * 36);
                    Polygon(p, gold, true, pts);
                    Ellipse(p, 43, 50, 3.5f, 4.5f, HoldemPalette.Ink, false);
                    Ellipse(p, 57, 50, 3.5f, 4.5f, HoldemPalette.Ink, false);
                    p.lineWidth = Mathf.Max(1f, S(3f));
                    p.BeginPath();
                    p.MoveTo(P(44, 60));
                    p.QuadraticCurveTo(P(50, 66), P(56, 60));
                    p.Stroke();
                    break;
                }
            }
        }
    }

    /// <summary>The card back: a little treehouse on a leafy pattern.</summary>
    internal sealed class TreehouseBackArt : PaintedArt
    {
        public TreehouseBackArt(float size) : base(size, size) { }

        protected override void Paint(Painter2D p)
        {
            Polygon(p, HoldemPalette.WoodDark, true, V(44, 60), V(56, 60), V(60, 94), V(40, 94));
            Ellipse(p, 50, 36, 34, 28, HoldemPalette.Leaf);
            Ellipse(p, 30, 46, 16, 14, HoldemPalette.Leaf, false);
            Ellipse(p, 70, 46, 16, 14, HoldemPalette.Leaf, false);
            Polygon(p, HoldemPalette.Wood, true, V(36, 52), V(64, 52), V(64, 70), V(36, 70));
            Polygon(p, Theme.Hex("#F0386B"), true, V(32, 54), V(50, 38), V(68, 54));
            Polygon(p, HoldemPalette.Sun, true, V(46, 58), V(54, 58), V(54, 66), V(46, 66));
        }
    }

    /// <summary>A play-money coin: its own color, a rim, and its value printed in the middle.</summary>
    internal sealed class CoinArt : VisualElement
    {
        public CoinArt(int value, float size)
        {
            pickingMode = PickingMode.Ignore;
            style.width = size;
            style.height = size;
            Color color = HoldemPalette.CoinColor(value);
            style.backgroundColor = color;
            Theme.Radius(this, size / 2f);
            Theme.Border(this, Mathf.Max(1.5f, size * 0.08f), Color.Lerp(color, HoldemPalette.Ink, 0.45f));
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            tooltip = HoldemPalette.CoinName(value) + " coin";

            var inner = new VisualElement();
            inner.pickingMode = PickingMode.Ignore;
            Theme.Circle(inner, size * 0.66f);
            inner.style.backgroundColor = Color.Lerp(color, Color.white, 0.55f);
            inner.style.alignItems = Align.Center;
            inner.style.justifyContent = Justify.Center;
            Add(inner);

            var label = Theme.Label(value.ToString(), Mathf.Max(8f, size * (value >= 10 ? 0.36f : 0.44f)), HoldemPalette.Ink, bold: true);
            label.pickingMode = PickingMode.Ignore;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            inner.Add(label);
        }

        /// <summary>Splits an amount into coins, biggest first (e.g. 38 = 25 + 10 + 1 + 1 + 1).</summary>
        public static int[] Breakdown(int amount)
        {
            var counts = new int[HoldemPalette.CoinValues.Length];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = amount / HoldemPalette.CoinValues[i];
                amount -= counts[i] * HoldemPalette.CoinValues[i];
            }
            return counts;
        }

        /// <summary>
        /// A little heap of coins that adds up to <paramref name="amount"/> (a few of each value, biggest
        /// on the left), so kids can see piles grow and shrink.
        /// </summary>
        public static VisualElement Pile(int amount, float coinSize, int maxCoins = 9)
        {
            var row = Theme.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.alignItems = Align.FlexEnd;
            if (amount <= 0) return row;
            var counts = Breakdown(amount);
            int shown = 0;
            for (int i = 0; i < counts.Length && shown < maxCoins; i++)
            {
                if (counts[i] == 0) continue;
                var stack = new VisualElement();
                stack.pickingMode = PickingMode.Ignore;
                int inStack = Mathf.Min(counts[i], Mathf.Min(4, maxCoins - shown));
                stack.style.width = coinSize;
                stack.style.height = coinSize + (inStack - 1) * coinSize * 0.22f;
                Theme.Margin(stack, 0, 1, 0, 1);
                for (int k = 0; k < inStack; k++)
                {
                    var coin = new CoinArt(HoldemPalette.CoinValues[i], coinSize);
                    coin.style.position = Position.Absolute;
                    coin.style.bottom = k * coinSize * 0.22f;
                    stack.Add(coin);
                }
                shown += inStack;
                row.Add(stack);
            }
            return row;
        }
    }

    /// <summary>The four animal friends who sit at the treehouse table.</summary>
    internal sealed class AnimalArt : PaintedArt
    {
        public enum Kind
        {
            Bunny,
            Owl,
            Fox,
            Bear
        }

        private readonly Kind kind;

        public AnimalArt(Kind kind, float size) : base(size, size)
        {
            this.kind = kind;
        }

        public static string KindName(Kind kind) => kind.ToString();

        protected override void Paint(Painter2D p)
        {
            Color white = Color.white;
            Color pink = Theme.Hex("#FF9EB5");
            switch (kind)
            {
                case Kind.Bunny:
                {
                    Color fur = Theme.Hex("#F3F0FF");
                    Ellipse(p, 34, 24, 9, 22, fur);
                    Ellipse(p, 66, 24, 9, 22, fur);
                    Ellipse(p, 34, 26, 4, 15, pink, false);
                    Ellipse(p, 66, 26, 4, 15, pink, false);
                    Ellipse(p, 50, 62, 34, 30, fur);
                    Face(p, 62, pink);
                    break;
                }
                case Kind.Owl:
                {
                    Color fur = Theme.Hex("#A47148");
                    Polygon(p, fur, true, V(20, 22), V(36, 36), V(22, 44));
                    Polygon(p, fur, true, V(80, 22), V(64, 36), V(78, 44));
                    Ellipse(p, 50, 58, 36, 34, fur);
                    Ellipse(p, 36, 54, 13, 13, Theme.Hex("#FFF1D6"));
                    Ellipse(p, 64, 54, 13, 13, Theme.Hex("#FFF1D6"));
                    Ellipse(p, 36, 55, 6, 6.5f, HoldemPalette.Ink, false);
                    Ellipse(p, 64, 55, 6, 6.5f, HoldemPalette.Ink, false);
                    Ellipse(p, 34, 53, 2, 2, white, false);
                    Ellipse(p, 62, 53, 2, 2, white, false);
                    Polygon(p, HoldemPalette.Sun, true, V(44, 66), V(56, 66), V(50, 76));
                    break;
                }
                case Kind.Fox:
                {
                    Color fur = Theme.Hex("#FF8C42");
                    Polygon(p, fur, true, V(16, 18), V(42, 36), V(22, 52));
                    Polygon(p, fur, true, V(84, 18), V(58, 36), V(78, 52));
                    Ellipse(p, 50, 60, 34, 30, fur);
                    p.BeginPath();
                    p.MoveTo(P(22, 66));
                    p.QuadraticCurveTo(P(50, 54), P(78, 66));
                    p.QuadraticCurveTo(P(66, 90), P(50, 90));
                    p.QuadraticCurveTo(P(34, 90), P(22, 66));
                    p.ClosePath();
                    p.fillColor = Theme.Hex("#FFF6EC");
                    p.Fill();
                    Face(p, 60, pink);
                    break;
                }
                default:
                {
                    Color fur = Theme.Hex("#8B5E3C");
                    Ellipse(p, 24, 32, 12, 12, fur);
                    Ellipse(p, 76, 32, 12, 12, fur);
                    Ellipse(p, 24, 32, 6, 6, Theme.Hex("#D9A27A"), false);
                    Ellipse(p, 76, 32, 6, 6, Theme.Hex("#D9A27A"), false);
                    Ellipse(p, 50, 60, 36, 32, fur);
                    Ellipse(p, 50, 72, 15, 11, Theme.Hex("#D9A27A"));
                    Face(p, 60, pink);
                    break;
                }
            }
        }

        private void Face(Painter2D p, float eyeY, Color cheek)
        {
            Ellipse(p, 38, eyeY - 4, 4.5f, 5.5f, HoldemPalette.Ink, false);
            Ellipse(p, 62, eyeY - 4, 4.5f, 5.5f, HoldemPalette.Ink, false);
            Ellipse(p, 36.6f, eyeY - 6, 1.6f, 1.6f, Color.white, false);
            Ellipse(p, 60.6f, eyeY - 6, 1.6f, 1.6f, Color.white, false);
            Ellipse(p, 28, eyeY + 6, 5, 3, new Color(cheek.r, cheek.g, cheek.b, 0.7f), false);
            Ellipse(p, 72, eyeY + 6, 5, 3, new Color(cheek.r, cheek.g, cheek.b, 0.7f), false);
            Ellipse(p, 50, eyeY + 6, 4, 3, HoldemPalette.Ink, false);
            p.lineWidth = Mathf.Max(1f, S(3f));
            p.BeginPath();
            p.MoveTo(P(43, eyeY + 12));
            p.QuadraticCurveTo(P(50, eyeY + 18), P(57, eyeY + 12));
            p.Stroke();
            p.lineWidth = Mathf.Max(1.2f, S(4f));
        }
    }

    /// <summary>Builds playing-card faces and backs out of UI Toolkit elements and the painted art above.</summary>
    internal static class HoldemCardArt
    {
        /// <summary>
        /// A bright card with a big number and suit. Small cards show just the big number and suit;
        /// bigger ones add corner marks and a picture (smiley suit, crown, tiara or star).
        /// </summary>
        public static VisualElement Face(int card, float width, float height)
        {
            int rank = HoldemCards.Rank(card);
            var suit = HoldemCards.SuitOf(card);
            Color color = HoldemPalette.SuitColor(suit);
            var face = Frame(width, height, Color.Lerp(color, Color.white, 0.9f));
            face.tooltip = HoldemCards.Name(card);

            if (width < 50f)
            {
                var big = RankLabel(rank, height * 0.4f, color);
                big.style.position = Position.Absolute;
                big.style.left = width * 0.1f;
                big.style.top = height * 0.02f;
                face.Add(big);
                var suitArt = new SuitArt(suit, width * 0.56f);
                suitArt.style.position = Position.Absolute;
                suitArt.style.right = width * 0.07f;
                suitArt.style.bottom = width * 0.07f;
                face.Add(suitArt);
                return face;
            }

            face.Add(Corner(rank, suit, width, height, topLeft: true));
            face.Add(Corner(rank, suit, width, height, topLeft: false));

            var middle = Theme.Column();
            middle.pickingMode = PickingMode.Ignore;
            middle.style.position = Position.Absolute;
            middle.style.left = 0;
            middle.style.right = 0;
            middle.style.top = 0;
            middle.style.bottom = 0;
            middle.style.alignItems = Align.Center;
            middle.style.justifyContent = Justify.Center;
            if (rank >= HoldemCards.Jack && rank <= HoldemCards.King)
            {
                middle.Add(new FaceIcon(rank, width * 0.48f));
                middle.Add(new SuitArt(suit, width * 0.36f, face: false));
            }
            else
            {
                middle.Add(new SuitArt(suit, width * (rank == HoldemCards.Ace ? 0.66f : 0.52f), face: true));
            }
            face.Add(middle);
            return face;
        }

        public static VisualElement Back(float width, float height)
        {
            var back = Frame(width, height, HoldemPalette.Paper);
            float inset = Mathf.Max(2f, width * 0.07f);
            var panel = new VisualElement();
            panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute;
            panel.style.left = inset;
            panel.style.right = inset;
            panel.style.top = inset;
            panel.style.bottom = inset;
            panel.style.backgroundColor = Theme.Hex("#7FD6C2");
            Theme.Radius(panel, width * 0.09f);
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.Center;
            panel.Add(new TreehouseBackArt(Mathf.Min(width, height) * 0.8f));
            back.Add(panel);
            return back;
        }

        /// <summary>An empty spot on the table where a shared card will go.</summary>
        public static VisualElement Slot(float width, float height)
        {
            var slot = new VisualElement();
            slot.pickingMode = PickingMode.Ignore;
            slot.style.width = width;
            slot.style.height = height;
            Theme.Radius(slot, width * 0.12f);
            Theme.Border(slot, 2, new Color(0.17f, 0.13f, 0.25f, 0.25f));
            slot.style.backgroundColor = new Color(1f, 1f, 1f, 0.35f);
            return slot;
        }

        private static VisualElement Frame(float width, float height, Color paper)
        {
            var frame = new VisualElement();
            frame.pickingMode = PickingMode.Ignore;
            frame.style.width = width;
            frame.style.height = height;
            frame.style.backgroundColor = paper;
            Theme.Radius(frame, width * 0.13f);
            Theme.Border(frame, Mathf.Max(1.5f, width * 0.035f), HoldemPalette.Ink);
            frame.style.overflow = Overflow.Hidden;
            return frame;
        }

        private static Label RankLabel(int rank, float size, Color color)
        {
            var label = Theme.Label(HoldemCards.RankLabel(rank), size, color, bold: true);
            label.pickingMode = PickingMode.Ignore;
            label.style.unityTextOutlineWidth = Mathf.Max(0.5f, size * 0.03f);
            label.style.unityTextOutlineColor = HoldemPalette.Ink;
            return label;
        }

        private static VisualElement Corner(int rank, Suit suit, float width, float height, bool topLeft)
        {
            var corner = Theme.Column();
            corner.pickingMode = PickingMode.Ignore;
            corner.style.position = Position.Absolute;
            corner.style.alignItems = Align.Center;
            float edge = width * 0.06f;
            if (topLeft)
            {
                corner.style.left = edge;
                corner.style.top = edge * 0.5f;
            }
            else
            {
                corner.style.right = edge;
                corner.style.bottom = edge * 0.5f;
                corner.style.rotate = new StyleRotate(new Rotate(new Angle(180f, AngleUnit.Degree)));
            }
            corner.Add(RankLabel(rank, height * 0.2f, HoldemPalette.SuitColor(suit)));
            corner.Add(new SuitArt(suit, width * 0.2f));
            return corner;
        }
    }
}
