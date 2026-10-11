using System;
using LanternLounge.Games.PrismRush;
using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Ui
{
    /// <summary>Prism Rush colors. Every color also has its own shape, so no card relies on hue alone.</summary>
    internal static class PrismPalette
    {
        public static readonly Color Ink = Theme.Hex("#1B1030");
        public static readonly Color Paper = Theme.Hex("#FFFBF2");
        public static readonly Color Night = Theme.Hex("#2A1B47");
        public static readonly Color Felt = Theme.Hex("#3B2468");
        public static readonly Color FeltRim = Theme.Hex("#6A47A8");
        public static readonly Color Glow = Theme.Hex("#FFE27A");
        public static readonly Color Clear = new Color(0, 0, 0, 0);

        private static readonly Color[] Colors =
        {
            Theme.Hex("#2EC8F0"), // Cyan  - diamond
            Theme.Hex("#FFC83D"), // Gold  - sun
            Theme.Hex("#FF5FA2"), // Rose  - star
            Theme.Hex("#72D84E")  // Lime  - leaf
        };

        public static Color Of(int color) => color >= 0 && color < Colors.Length ? Colors[color] : Night;

        public static Color Light(int color) => Color.Lerp(Of(color), Color.white, 0.5f);

        public static Color Deep(int color) => Color.Lerp(Of(color), Ink, 0.35f);
    }

    /// <summary>Shared Painter2D helpers. Everything is drawn in a 100 x 100 design box.</summary>
    internal abstract class PaintedArt : VisualElement
    {
        private Rect box;
        private float scale;

        protected PaintedArt(float width, float height)
        {
            style.width = width;
            style.height = height;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
        }

        protected Vector2 P(float x, float y) => new Vector2(box.x + x * scale, box.y + y * scale);
        protected float S(float v) => v * scale;

        private void OnGenerate(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 2f || rect.height < 2f) return;
            scale = Mathf.Min(rect.width, rect.height) / 100f;
            box = new Rect(rect.x + (rect.width - 100f * scale) / 2f, rect.y + (rect.height - 100f * scale) / 2f, 100f * scale, 100f * scale);
            var p = context.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = PrismPalette.Ink;
            p.lineWidth = Mathf.Max(1.2f, S(4f));
            Paint(p);
        }

        protected abstract void Paint(Painter2D p);

        protected void Polygon(Painter2D p, Color fill, bool stroke, params Vector2[] designPoints)
        {
            p.BeginPath();
            p.MoveTo(P(designPoints[0].x, designPoints[0].y));
            for (int i = 1; i < designPoints.Length; i++) p.LineTo(P(designPoints[i].x, designPoints[i].y));
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
            if (stroke) p.Stroke();
        }

        protected void Ellipse(Painter2D p, float cx, float cy, float rx, float ry, Color fill, bool stroke = true)
        {
            const float k = 0.5523f;
            p.BeginPath();
            p.MoveTo(P(cx + rx, cy));
            p.BezierCurveTo(P(cx + rx, cy + ry * k), P(cx + rx * k, cy + ry), P(cx, cy + ry));
            p.BezierCurveTo(P(cx - rx * k, cy + ry), P(cx - rx, cy + ry * k), P(cx - rx, cy));
            p.BezierCurveTo(P(cx - rx, cy - ry * k), P(cx - rx * k, cy - ry), P(cx, cy - ry));
            p.BezierCurveTo(P(cx + rx * k, cy - ry), P(cx + rx, cy - ry * k), P(cx + rx, cy));
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
            if (stroke) p.Stroke();
        }

        /// <summary>A pie slice from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (0 = right, clockwise).</summary>
        protected void Wedge(Painter2D p, float cx, float cy, float r, float fromDeg, float toDeg, Color fill)
        {
            p.BeginPath();
            p.MoveTo(P(cx, cy));
            p.Arc(P(cx, cy), S(r), new Angle(fromDeg, AngleUnit.Degree), new Angle(toDeg, AngleUnit.Degree));
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
        }

        /// <summary>A thick line with a dark outline, like a sticker.</summary>
        protected void OutlinedPath(Painter2D p, Action<Painter2D> build, Color fill, float width)
        {
            p.strokeColor = PrismPalette.Ink;
            p.lineWidth = Mathf.Max(2f, S(width + 6f));
            p.BeginPath();
            build(p);
            p.Stroke();
            p.strokeColor = fill;
            p.lineWidth = Mathf.Max(1f, S(width));
            p.BeginPath();
            build(p);
            p.Stroke();
            p.strokeColor = PrismPalette.Ink;
            p.lineWidth = Mathf.Max(1.2f, S(4f));
        }

        protected static Vector2 V(float x, float y) => new Vector2(x, y);

        protected static Vector2 Polar(float cx, float cy, float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
        }

        /// <summary>The color's own shape: Cyan diamond, Gold sun, Rose star, Lime leaf; a rainbow wheel for wild.</summary>
        protected void ColorShape(Painter2D p, int color, float cx, float cy, float r, Color fill, bool stroke = true)
        {
            switch (color)
            {
                case 0:
                    Polygon(p, fill, stroke, V(cx, cy - r), V(cx + r * 0.72f, cy), V(cx, cy + r), V(cx - r * 0.72f, cy));
                    break;
                case 1:
                {
                    var pts = new Vector2[24];
                    for (int i = 0; i < pts.Length; i++) pts[i] = Polar(cx, cy, i % 2 == 0 ? r : r * 0.72f, -90f + i * 15f);
                    Polygon(p, fill, stroke, pts);
                    break;
                }
                case 2:
                {
                    var pts = new Vector2[10];
                    for (int i = 0; i < pts.Length; i++) pts[i] = Polar(cx, cy, i % 2 == 0 ? r : r * 0.45f, -90f + i * 36f);
                    Polygon(p, fill, stroke, pts);
                    break;
                }
                case 3:
                    p.BeginPath();
                    p.MoveTo(P(cx - r * 0.1f, cy + r));
                    p.BezierCurveTo(P(cx - r * 1.05f, cy + r * 0.25f), P(cx - r * 0.55f, cy - r * 0.75f), P(cx + r * 0.25f, cy - r));
                    p.BezierCurveTo(P(cx + r * 0.95f, cy - r * 0.25f), P(cx + r * 0.55f, cy + r * 0.7f), P(cx - r * 0.1f, cy + r));
                    p.ClosePath();
                    p.fillColor = fill;
                    p.Fill();
                    if (stroke)
                    {
                        p.Stroke();
                        p.BeginPath();
                        p.MoveTo(P(cx - r * 0.1f, cy + r * 0.85f));
                        p.QuadraticCurveTo(P(cx + r * 0.05f, cy), P(cx + r * 0.2f, cy - r * 0.8f));
                        p.Stroke();
                    }
                    break;
                default:
                    for (int c = 0; c < PrismCards.ColorCount; c++)
                    {
                        Wedge(p, cx, cy, r, -90f + c * 90f, c * 90f, PrismPalette.Of(c));
                    }
                    if (stroke) Ellipse(p, cx, cy, r, r, PrismPalette.Clear);
                    break;
            }
        }
    }

    /// <summary>A color's shape on its own, e.g. for the "color in play" chip and the Prism color picker.</summary>
    internal sealed class PrismShape : PaintedArt
    {
        private int color;

        public PrismShape(int color, float size) : base(size, size)
        {
            this.color = color;
        }

        public int ShapeColor
        {
            get => color;
            set
            {
                if (color == value) return;
                color = value;
                MarkDirtyRepaint();
            }
        }

        protected override void Paint(Painter2D p)
        {
            ColorShape(p, color, 50, 50, 44, PrismPalette.Of(color));
        }
    }

    /// <summary>The pictures on action cards: Flip arrows, Hop arc, Pulse bolt, Prism rainbow.</summary>
    internal sealed class PrismIcon : PaintedArt
    {
        private readonly CardKind kind;

        public PrismIcon(CardKind kind, float size) : base(size, size)
        {
            this.kind = kind;
        }

        protected override void Paint(Painter2D p)
        {
            Color white = PrismPalette.Paper;
            switch (kind)
            {
                case CardKind.Flip:
                    // Two arrows chasing each other round a circle.
                    OutlinedPath(p, q =>
                    {
                        q.MoveTo(P(22, 44));
                        q.BezierCurveTo(P(24, 20), P(64, 12), P(76, 34));
                    }, white, 9f);
                    Polygon(p, white, true, V(66, 26), V(88, 30), V(80, 50));
                    OutlinedPath(p, q =>
                    {
                        q.MoveTo(P(78, 56));
                        q.BezierCurveTo(P(76, 80), P(36, 88), P(24, 66));
                    }, white, 9f);
                    Polygon(p, white, true, V(34, 74), V(12, 70), V(20, 50));
                    break;
                case CardKind.Hop:
                    // A frog-like hop over somebody's head.
                    Ellipse(p, 50, 74, 11, 11, PrismPalette.Ink, false);
                    Ellipse(p, 50, 74, 7, 7, white, false);
                    OutlinedPath(p, q =>
                    {
                        q.MoveTo(P(14, 76));
                        q.BezierCurveTo(P(18, 20), P(74, 18), P(80, 64));
                    }, white, 9f);
                    Polygon(p, white, true, V(68, 58), V(92, 58), V(80, 80));
                    break;
                case CardKind.Pulse:
                    Polygon(p, white, true, V(58, 6), V(22, 54), V(46, 54), V(38, 94), V(78, 42), V(54, 42), V(66, 6));
                    break;
                case CardKind.Prism:
                {
                    // White light goes in, four colors come out.
                    OutlinedPath(p, q =>
                    {
                        q.MoveTo(P(4, 58));
                        q.LineTo(P(40, 50));
                    }, white, 6f);
                    for (int c = 0; c < PrismCards.ColorCount; c++)
                    {
                        float y = 34 + c * 12;
                        Color ray = PrismPalette.Of(c);
                        OutlinedPath(p, q =>
                        {
                            q.MoveTo(P(56, 50));
                            q.LineTo(P(96, y));
                        }, ray, 5f);
                    }
                    Polygon(p, new Color(1f, 1f, 1f, 0.92f), true, V(48, 16), V(76, 74), V(20, 74));
                    break;
                }
            }
        }
    }

    /// <summary>The prism-and-rays pattern on the back of every card.</summary>
    internal sealed class PrismBackArt : PaintedArt
    {
        public PrismBackArt(float width, float height) : base(width, height) { }

        protected override void Paint(Painter2D p)
        {
            for (int c = 0; c < PrismCards.ColorCount; c++)
            {
                Wedge(p, 50, 50, 46, -90f + c * 90f, c * 90f, new Color(PrismPalette.Of(c).r, PrismPalette.Of(c).g, PrismPalette.Of(c).b, 0.85f));
            }
            Ellipse(p, 50, 50, 46, 46, PrismPalette.Clear);
            Ellipse(p, 50, 50, 30, 30, PrismPalette.Night);
            Polygon(p, PrismPalette.Paper, true, V(50, 30), V(66, 62), V(34, 62));
        }
    }

    /// <summary>
    /// A Prism Pal: the friendly gem each player is represented by at the table.
    /// It can look happy, think (eyes up) or go "wow" (round mouth).
    /// </summary>
    internal sealed class GemBuddy : PaintedArt
    {
        public enum Mood
        {
            Happy,
            Thinking,
            Wow
        }

        private readonly Color body;
        private Mood mood;

        public GemBuddy(Color body, float size) : base(size, size)
        {
            this.body = body;
        }

        public Mood Face
        {
            get => mood;
            set
            {
                if (mood == value) return;
                mood = value;
                MarkDirtyRepaint();
            }
        }

        protected override void Paint(Painter2D p)
        {
            // Gem body with a lighter top facet.
            Polygon(p, body, true, V(28, 10), V(72, 10), V(94, 36), V(50, 94), V(6, 36));
            Polygon(p, Color.Lerp(body, Color.white, 0.45f), false, V(30, 14), V(70, 14), V(86, 34), V(14, 34));
            p.lineWidth = Mathf.Max(1f, S(2.5f));
            p.BeginPath();
            p.MoveTo(P(8, 36));
            p.LineTo(P(92, 36));
            p.Stroke();
            p.lineWidth = Mathf.Max(1.2f, S(4f));

            float look = mood == Mood.Thinking ? -3f : 0f;
            Ellipse(p, 36, 50, 8, 9, PrismPalette.Paper);
            Ellipse(p, 64, 50, 8, 9, PrismPalette.Paper);
            Ellipse(p, 37, 51 + look, 4, 4.5f, PrismPalette.Ink, false);
            Ellipse(p, 65, 51 + look, 4, 4.5f, PrismPalette.Ink, false);
            Ellipse(p, 35.5f, 49 + look, 1.4f, 1.4f, Color.white, false);
            Ellipse(p, 63.5f, 49 + look, 1.4f, 1.4f, Color.white, false);

            Ellipse(p, 26, 62, 5, 3, new Color(1f, 0.5f, 0.6f, 0.55f), false);
            Ellipse(p, 74, 62, 5, 3, new Color(1f, 0.5f, 0.6f, 0.55f), false);

            switch (mood)
            {
                case Mood.Wow:
                    Ellipse(p, 50, 68, 5, 6, PrismPalette.Ink, false);
                    break;
                case Mood.Thinking:
                    p.BeginPath();
                    p.MoveTo(P(43, 68));
                    p.LineTo(P(57, 66));
                    p.Stroke();
                    break;
                default:
                    p.BeginPath();
                    p.MoveTo(P(40, 64));
                    p.QuadraticCurveTo(P(50, 74), P(60, 64));
                    p.Stroke();
                    break;
            }
        }
    }

    /// <summary>Three curved arrows around the pile that show which way play is going.</summary>
    internal sealed class DirectionRing : PaintedArt
    {
        private int direction = 1;

        public DirectionRing(float size) : base(size, size) { }

        public int Direction
        {
            get => direction;
            set
            {
                if (direction == value) return;
                direction = value;
                MarkDirtyRepaint();
            }
        }

        protected override void Paint(Painter2D p)
        {
            Color arrow = new Color(1f, 1f, 1f, 0.55f);
            const float r = 44f;
            for (int i = 0; i < 3; i++)
            {
                float from = i * 120f - 80f;
                float to = from + 70f;
                p.strokeColor = arrow;
                p.lineWidth = Mathf.Max(1.5f, S(4.5f));
                p.BeginPath();
                p.Arc(P(50, 50), S(r), new Angle(from, AngleUnit.Degree), new Angle(to, AngleUnit.Degree));
                p.Stroke();

                // Arrowhead at the leading end, pointing the way play goes.
                float tipDeg = direction > 0 ? to + 9f : from - 9f;
                float baseDeg = direction > 0 ? to - 1f : from + 1f;
                Polygon(p, arrow, false, Polar(50, 50, r, tipDeg), Polar(50, 50, r + 6f, baseDeg), Polar(50, 50, r - 6f, baseDeg));
            }
            p.strokeColor = PrismPalette.Ink;
            p.lineWidth = Mathf.Max(1.2f, S(4f));
        }
    }

    /// <summary>Builds card faces and backs out of UI Toolkit elements plus the painted art above.</summary>
    internal static class PrismCardArt
    {
        /// <summary>
        /// A card face: color panel, big centre shape with the number or action picture, and corner
        /// marks (number + shape) so the card still reads when cards overlap in a fan.
        /// </summary>
        public static VisualElement Face(int cardId, float width, float height)
        {
            var card = PrismCards.Card(cardId);
            var face = Frame(width, height, PrismPalette.Paper);
            face.tooltip = Describe(cardId);

            float inset = Mathf.Max(3f, width * 0.06f);
            var panel = new VisualElement();
            panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute;
            panel.style.left = inset;
            panel.style.right = inset;
            panel.style.top = inset;
            panel.style.bottom = inset;
            panel.style.backgroundColor = card.IsWild ? PrismPalette.Night : PrismPalette.Of(card.Color);
            Theme.Radius(panel, width * 0.1f);
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.Center;
            panel.style.overflow = Overflow.Hidden;
            face.Add(panel);

            // A soft diagonal shine band makes the card look glossy.
            var shine = new VisualElement();
            shine.pickingMode = PickingMode.Ignore;
            shine.style.position = Position.Absolute;
            shine.style.width = width * 0.5f;
            shine.style.height = height * 1.6f;
            shine.style.left = -width * 0.05f;
            shine.style.top = -height * 0.3f;
            shine.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
            shine.style.rotate = new StyleRotate(new Rotate(new Angle(28f, AngleUnit.Degree)));
            panel.Add(shine);

            float art = Mathf.Min(width, height) * 0.8f;
            if (card.Kind == CardKind.Number)
            {
                var shape = new PrismShape(card.Color, art);
                shape.style.position = Position.Absolute;
                shape.style.opacity = 0.55f;
                panel.Add(new Centered(shape));
                var number = BigText(card.Number.ToString(), height * 0.44f);
                panel.Add(number);
            }
            else
            {
                if (!card.IsWild)
                {
                    var shape = new PrismShape(card.Color, art);
                    shape.style.opacity = 0.4f;
                    panel.Add(new Centered(shape));
                }
                var icon = new PrismIcon(card.Kind, art * 0.82f);
                panel.Add(new Centered(icon));
                if (card.Kind == CardKind.Pulse)
                {
                    var plus = BigText("+2", height * 0.2f);
                    plus.style.position = Position.Absolute;
                    plus.style.bottom = height * 0.08f;
                    plus.style.right = width * 0.1f;
                    panel.Add(plus);
                }
            }

            face.Add(Corner(card, width, height, topLeft: true));
            face.Add(Corner(card, width, height, topLeft: false));
            return face;
        }

        /// <summary>The shared back of every card: night purple with a prism splitting light.</summary>
        public static VisualElement Back(float width, float height)
        {
            var back = Frame(width, height, PrismPalette.Paper);
            float inset = Mathf.Max(2f, width * 0.07f);
            var panel = new VisualElement();
            panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute;
            panel.style.left = inset;
            panel.style.right = inset;
            panel.style.top = inset;
            panel.style.bottom = inset;
            panel.style.backgroundColor = PrismPalette.Night;
            Theme.Radius(panel, width * 0.1f);
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.Center;
            float art = Mathf.Min(width, height) * 0.78f;
            panel.Add(new PrismBackArt(art, art));
            back.Add(panel);
            return back;
        }

        /// <summary>A short description for screen readers and tooltips, e.g. "Rose 5 (star)".</summary>
        public static string Describe(int cardId)
        {
            var card = PrismCards.Card(cardId);
            switch (card.Kind)
            {
                case CardKind.Number: return $"{PrismCards.Name(cardId)} ({PrismCards.ShapeName(card.Color)})";
                case CardKind.Flip: return $"{PrismCards.Name(cardId)}: reverses the direction";
                case CardKind.Hop: return $"{PrismCards.Name(cardId)}: the next player is skipped";
                case CardKind.Pulse: return $"{PrismCards.Name(cardId)}: the next player draws 2";
                default: return "Prism: play on anything and choose the next color";
            }
        }

        public static void Highlight(VisualElement face, Color color, float width)
        {
            Theme.Border(face, width, color);
        }

        private static VisualElement Frame(float width, float height, Color paper)
        {
            var frame = new VisualElement();
            frame.pickingMode = PickingMode.Ignore;
            frame.style.width = width;
            frame.style.height = height;
            frame.style.backgroundColor = paper;
            Theme.Radius(frame, width * 0.14f);
            Theme.Border(frame, Mathf.Max(1.5f, width * 0.035f), PrismPalette.Ink);
            frame.style.overflow = Overflow.Hidden;
            return frame;
        }

        private static Label BigText(string text, float size)
        {
            var label = Theme.Label(text, size, Color.white, bold: true);
            label.pickingMode = PickingMode.Ignore;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityTextOutlineWidth = Mathf.Max(1f, size * 0.09f);
            label.style.unityTextOutlineColor = PrismPalette.Ink;
            return label;
        }

        private static VisualElement Corner(PrismCard card, float width, float height, bool topLeft)
        {
            var corner = Theme.Column();
            corner.pickingMode = PickingMode.Ignore;
            corner.style.position = Position.Absolute;
            corner.style.alignItems = Align.Center;
            float edge = Mathf.Max(3f, width * 0.08f);
            if (topLeft)
            {
                corner.style.left = edge;
                corner.style.top = edge;
            }
            else
            {
                corner.style.right = edge;
                corner.style.bottom = edge;
                corner.style.rotate = new StyleRotate(new Rotate(new Angle(180f, AngleUnit.Degree)));
            }

            float mark = Mathf.Max(8f, width * 0.2f);
            if (card.Kind == CardKind.Number)
            {
                var number = BigText(card.Number.ToString(), Mathf.Max(9f, height * 0.15f));
                number.style.unityTextOutlineWidth = Mathf.Max(0.6f, height * 0.012f);
                corner.Add(number);
                corner.Add(new PrismShape(card.Color, mark));
            }
            else
            {
                corner.Add(new PrismIcon(card.Kind, mark * 1.15f));
            }
            return corner;
        }

        /// <summary>Holds one child dead-centre inside a parent, on top of anything else.</summary>
        private sealed class Centered : VisualElement
        {
            public Centered(VisualElement child)
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0;
                style.right = 0;
                style.top = 0;
                style.bottom = 0;
                style.alignItems = Align.Center;
                style.justifyContent = Justify.Center;
                child.style.position = Position.Relative;
                Add(child);
            }
        }
    }
}
