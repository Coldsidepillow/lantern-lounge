using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Ui
{
    /// <summary>
    /// Draws one Tidebound creature with vector shapes (no image files needed).
    /// Every creature is drawn in a 100 × 80 design box that is scaled to the element's size.
    /// </summary>
    internal sealed class CreatureArt : VisualElement
    {
        private static readonly Color Ink = Theme.Hex("#13283D");
        private static readonly Color Glint = Color.white;
        private static readonly Color Blush = Theme.Hex("#FF9C84");

        private int creature;
        private Rect box;
        private float scale;

        public CreatureArt(int creature, float width, float height)
        {
            this.creature = creature;
            style.width = width;
            style.height = height;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public int Creature
        {
            get => creature;
            set
            {
                if (creature == value) return;
                creature = value;
                MarkDirtyRepaint();
            }
        }

        public static Color BodyColor(int creature)
        {
            switch (creature)
            {
                case 0: return Theme.Hex("#FF8A6B");
                case 1: return Theme.Hex("#FFB59F");
                case 2: return Theme.Hex("#B7A3FF");
                case 3: return Theme.Hex("#D6CCFF");
                case 4: return Theme.Hex("#6EDBC8");
                case 5: return Theme.Hex("#A8EBDD");
                case 6: return Theme.Hex("#FFC233");
                case 7: return Theme.Hex("#FFE08A");
                case 8: return Theme.Hex("#8ED48F");
                case 9: return Theme.Hex("#6FA8FF");
                case 10: return Theme.Hex("#FF9E5E");
                case 11: return Theme.Hex("#9FD9C0");
                case 12: return Theme.Hex("#FF8FB1");
                case 13: return Theme.Hex("#C59BFF");
                case 14: return Theme.Hex("#5FC48A");
                default: return Theme.Hex("#4F78C9");
            }
        }

        private Vector2 P(float x, float y) => new Vector2(box.x + x * scale, box.y + y * scale);
        private float S(float v) => v * scale;

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 2f || rect.height < 2f) return;

            // Fit the 100 × 80 design box inside the element, centred.
            scale = Mathf.Min(rect.width / 100f, rect.height / 80f);
            float w = 100f * scale, h = 80f * scale;
            box = new Rect(rect.x + (rect.width - w) / 2f, rect.y + (rect.height - h) / 2f, w, h);

            var p = context.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = Ink;
            p.lineWidth = Mathf.Max(1.5f, S(3f));
            Color body = BodyColor(creature);

            switch (creature)
            {
                case 0: Crab(p, body); break;
                case 1: Shrimp(p, body); break;
                case 2: Jelly(p, body); break;
                case 3: Wisp(p, body); break;
                case 4: Ray(p, body); break;
                case 5: Seahare(p, body); break;
                case 6: Snail(p, body); break;
                case 7: Squid(p, body); break;
                case 8: Owlet(p, body); break;
                case 9: Eel(p, body); break;
                case 10: Puffer(p, body); break;
                case 11: Turtle(p, body); break;
                case 12: Starfish(p, body); break;
                case 13: Urchin(p, body); break;
                case 14: Octopus(p, body); break;
                default: Angler(p, body); break;
            }
        }

        // ---------- Shape helpers ----------

        private void Ellipse(Painter2D p, float cx, float cy, float rx, float ry, Color fill, bool stroke = true)
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

        private void Circle(Painter2D p, float cx, float cy, float r, Color fill, bool stroke = true)
        {
            Ellipse(p, cx, cy, r, r, fill, stroke);
        }

        private void Line(Painter2D p, float x1, float y1, float x2, float y2, float width = 3f)
        {
            p.lineWidth = Mathf.Max(1.5f, S(width));
            p.BeginPath();
            p.MoveTo(P(x1, y1));
            p.LineTo(P(x2, y2));
            p.Stroke();
            p.lineWidth = Mathf.Max(1.5f, S(3f));
        }

        private void Curve(Painter2D p, float x1, float y1, float cx, float cy, float x2, float y2, float width = 3f)
        {
            p.lineWidth = Mathf.Max(1.5f, S(width));
            p.BeginPath();
            p.MoveTo(P(x1, y1));
            p.QuadraticCurveTo(P(cx, cy), P(x2, y2));
            p.Stroke();
            p.lineWidth = Mathf.Max(1.5f, S(3f));
        }

        /// <summary>Two friendly eyes with white glints, plus rosy cheeks.</summary>
        private void Face(Painter2D p, float lx, float ly, float rx, float ry, float size = 4.5f, bool cheeks = true)
        {
            if (cheeks)
            {
                Circle(p, lx - 4f, ly + 8f, 3f, Blush, false);
                Circle(p, rx + 4f, ry + 8f, 3f, Blush, false);
            }
            Circle(p, lx, ly, size, Ink, false);
            Circle(p, rx, ry, size, Ink, false);
            Circle(p, lx - size * 0.35f, ly - size * 0.35f, size * 0.35f, Glint, false);
            Circle(p, rx - size * 0.35f, ry - size * 0.35f, size * 0.35f, Glint, false);
        }

        private void Smile(Painter2D p, float cx, float cy, float width = 10f)
        {
            Curve(p, cx - width / 2f, cy, cx, cy + width / 2.5f, cx + width / 2f, cy, 2.5f);
        }

        // ---------- The sixteen creatures ----------

        private void Crab(Painter2D p, Color body)
        {
            // Coral crown
            Line(p, 38, 36, 38, 20); Line(p, 50, 34, 50, 12); Line(p, 62, 36, 62, 20);
            Line(p, 38, 25, 32, 20); Line(p, 50, 19, 57, 14); Line(p, 62, 26, 68, 21);
            // Claws and arms
            Line(p, 28, 50, 20, 42); Line(p, 72, 50, 80, 42);
            Circle(p, 17, 36, 10, body); Circle(p, 83, 36, 10, body);
            Line(p, 12, 30, 19, 37, 2.5f); Line(p, 88, 30, 81, 37, 2.5f);
            // Legs
            Line(p, 30, 62, 22, 72); Line(p, 40, 66, 36, 76); Line(p, 60, 66, 64, 76); Line(p, 70, 62, 78, 72);
            Ellipse(p, 50, 54, 27, 17, body);
            Face(p, 42, 51, 58, 51);
            Smile(p, 50, 59);
        }

        private void Shrimp(Painter2D p, Color body)
        {
            // Antennae
            Curve(p, 70, 26, 86, 6, 96, 14, 2f); Curve(p, 68, 24, 78, 2, 90, 4, 2f);
            // Curled body
            p.BeginPath();
            p.MoveTo(P(74, 30));
            p.BezierCurveTo(P(86, 46), P(70, 72), P(44, 70));
            p.BezierCurveTo(P(26, 68), P(20, 54), P(28, 46));
            p.BezierCurveTo(P(36, 56), P(52, 58), P(58, 46));
            p.BezierCurveTo(P(62, 38), P(64, 32), P(74, 30));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            // Tail fan
            Ellipse(p, 22, 44, 8, 5, body); Ellipse(p, 24, 34, 5, 8, body);
            // Segments and legs
            Curve(p, 56, 64, 52, 56, 58, 50, 2f); Curve(p, 66, 58, 62, 50, 66, 44, 2f);
            Line(p, 50, 70, 48, 78, 2f); Line(p, 60, 68, 60, 76, 2f);
            Face(p, 70, 36, 70, 36, 4f, false);
            Circle(p, 74, 44, 2.5f, Blush, false);
        }

        private void Jelly(Painter2D p, Color body)
        {
            // Tentacles
            Curve(p, 32, 50, 26, 62, 32, 74); Curve(p, 44, 52, 50, 64, 44, 78);
            Curve(p, 56, 52, 50, 64, 56, 78); Curve(p, 68, 50, 74, 62, 68, 74);
            // Dome
            p.BeginPath();
            p.MoveTo(P(20, 48));
            p.BezierCurveTo(P(20, 10), P(80, 10), P(80, 48));
            p.QuadraticCurveTo(P(72, 54), P(65, 48));
            p.QuadraticCurveTo(P(57, 54), P(50, 48));
            p.QuadraticCurveTo(P(43, 54), P(35, 48));
            p.QuadraticCurveTo(P(28, 54), P(20, 48));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            Circle(p, 34, 26, 4, new Color(1f, 1f, 1f, 0.7f), false);
            Face(p, 41, 34, 59, 34);
            Smile(p, 50, 41);
        }

        private void Wisp(Painter2D p, Color body)
        {
            // Flowing fins
            Curve(p, 30, 56, 14, 66, 8, 54); Curve(p, 34, 62, 22, 78, 12, 72);
            // Teardrop body
            p.BeginPath();
            p.MoveTo(P(84, 40));
            p.BezierCurveTo(P(84, 18), P(46, 14), P(34, 34));
            p.BezierCurveTo(P(26, 46), P(30, 64), P(50, 66));
            p.BezierCurveTo(P(70, 68), P(84, 58), P(84, 40));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            // Top fin
            Curve(p, 54, 22, 60, 4, 70, 18);
            Face(p, 56, 38, 72, 38);
            Smile(p, 64, 48, 9f);
        }

        private void Ray(Painter2D p, Color body)
        {
            // Tail
            Curve(p, 50, 64, 56, 72, 50, 79, 3f);
            // Wings
            p.BeginPath();
            p.MoveTo(P(50, 18));
            p.BezierCurveTo(P(62, 30), P(86, 32), P(94, 44));
            p.BezierCurveTo(P(80, 48), P(64, 52), P(50, 66));
            p.BezierCurveTo(P(36, 52), P(20, 48), P(6, 44));
            p.BezierCurveTo(P(14, 32), P(38, 30), P(50, 18));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            // Sun cap
            Circle(p, 50, 20, 8, Theme.Hex("#FFC233"));
            Circle(p, 28, 44, 2.5f, Color.white, false); Circle(p, 72, 44, 2.5f, Color.white, false);
            Face(p, 43, 42, 57, 42, 4f);
            Smile(p, 50, 50, 8f);
        }

        private void Seahare(Painter2D p, Color body)
        {
            // Ripples
            Curve(p, 6, 70, 14, 64, 22, 70, 2f); Curve(p, 78, 72, 86, 66, 94, 72, 2f);
            // Ears
            Ellipse(p, 38, 22, 5, 12, body); Ellipse(p, 60, 22, 5, 12, body);
            Ellipse(p, 50, 50, 32, 22, body);
            // Spots
            Circle(p, 30, 56, 3f, Color.white, false); Circle(p, 70, 54, 3.5f, Color.white, false); Circle(p, 62, 64, 2.5f, Color.white, false);
            Face(p, 42, 46, 58, 46);
            Smile(p, 50, 54);
        }

        private void Snail(Painter2D p, Color body)
        {
            // Foot
            p.BeginPath();
            p.MoveTo(P(10, 70));
            p.QuadraticCurveTo(P(10, 58), P(26, 58));
            p.LineTo(P(76, 58));
            p.QuadraticCurveTo(P(90, 60), P(90, 70));
            p.ClosePath();
            p.fillColor = Theme.Hex("#FFF4E0");
            p.Fill();
            p.Stroke();
            // Shell with spiral
            Circle(p, 58, 38, 20, body);
            p.BeginPath();
            p.Arc(P(58, 38), S(11), Angle.Degrees(-90), Angle.Degrees(200));
            p.Stroke();
            p.BeginPath();
            p.Arc(P(58, 38), S(5), Angle.Degrees(-90), Angle.Degrees(160));
            p.Stroke();
            // Lantern antenna
            Curve(p, 26, 58, 18, 40, 26, 26, 3f);
            Circle(p, 27, 22, 6, Theme.Hex("#FFF4C2"));
            Face(p, 22, 64, 30, 64, 2.8f, false);
        }

        private void Squid(Painter2D p, Color body)
        {
            // Tentacles
            Curve(p, 38, 56, 30, 70, 36, 78); Curve(p, 46, 58, 44, 72, 48, 79);
            Curve(p, 54, 58, 56, 72, 52, 79); Curve(p, 62, 56, 70, 70, 64, 78);
            // Mantle
            p.BeginPath();
            p.MoveTo(P(50, 6));
            p.BezierCurveTo(P(72, 14), P(74, 40), P(70, 56));
            p.LineTo(P(30, 56));
            p.BezierCurveTo(P(26, 40), P(28, 14), P(50, 6));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            // Star freckles
            Circle(p, 42, 20, 2f, Color.white, false); Circle(p, 58, 24, 2.5f, Color.white, false); Circle(p, 50, 14, 1.8f, Color.white, false);
            Face(p, 42, 40, 58, 40);
            Smile(p, 50, 48, 8f);
        }

        private void Owlet(Painter2D p, Color body)
        {
            // Ear tufts
            p.BeginPath();
            p.MoveTo(P(30, 30)); p.LineTo(P(26, 12)); p.LineTo(P(42, 24)); p.ClosePath();
            p.fillColor = body; p.Fill(); p.Stroke();
            p.BeginPath();
            p.MoveTo(P(70, 30)); p.LineTo(P(74, 12)); p.LineTo(P(58, 24)); p.ClosePath();
            p.Fill(); p.Stroke();
            Circle(p, 50, 46, 27, body);
            // Big eyes
            Circle(p, 40, 42, 10, Color.white);
            Circle(p, 60, 42, 10, Color.white);
            Circle(p, 41, 43, 5, Ink, false); Circle(p, 59, 43, 5, Ink, false);
            Circle(p, 39, 41, 1.8f, Color.white, false); Circle(p, 57, 41, 1.8f, Color.white, false);
            // Beak
            p.BeginPath();
            p.MoveTo(P(46, 52)); p.LineTo(P(54, 52)); p.LineTo(P(50, 59)); p.ClosePath();
            p.fillColor = Theme.Hex("#FFC233"); p.Fill(); p.Stroke();
            // Barnacles
            Circle(p, 34, 64, 3f, Color.white); Circle(p, 64, 66, 2.5f, Color.white);
        }

        private void EelPath(Painter2D p)
        {
            p.BeginPath();
            p.MoveTo(P(10, 52));
            p.BezierCurveTo(P(26, 26), P(40, 70), P(56, 46));
            p.BezierCurveTo(P(66, 32), P(80, 34), P(88, 40));
        }

        private void Eel(Painter2D p, Color body)
        {
            // Body: a thick wavy ribbon (outline then fill colour on top)
            p.lineWidth = S(18);
            p.strokeColor = Ink;
            EelPath(p);
            p.Stroke();
            p.lineWidth = S(12);
            p.strokeColor = body;
            EelPath(p);
            p.Stroke();
            p.strokeColor = Ink;
            p.lineWidth = Mathf.Max(1.5f, S(3f));
            // Leafy fins
            Ellipse(p, 34, 64, 7, 4, Theme.Hex("#8ED48F"));
            Ellipse(p, 62, 30, 7, 4, Theme.Hex("#8ED48F"));
            Face(p, 84, 37, 84, 37, 3.5f, false);
            Circle(p, 88, 44, 2.2f, Blush, false);
        }
            private void Puffer(Painter2D p, Color body)
        {
            // Spikes all around
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Line(p, 48 + Mathf.Cos(a) * 24f, 42 + Mathf.Sin(a) * 24f, 48 + Mathf.Cos(a) * 32f, 42 + Mathf.Sin(a) * 30f, 2.5f);
            }
            // Tail and fins
            p.BeginPath();
            p.MoveTo(P(20, 42)); p.LineTo(P(6, 32)); p.LineTo(P(6, 52)); p.ClosePath();
            p.fillColor = body; p.Fill(); p.Stroke();
            Circle(p, 48, 42, 25, body);
            Ellipse(p, 50, 52, 14, 8, Theme.Hex("#FFE3C8"), false);
            // Little beak
            Ellipse(p, 72, 46, 5, 4, Theme.Hex("#FFC233"));
            Face(p, 50, 36, 64, 36);
            Smile(p, 57, 45, 7f);
        }

        private void Turtle(Painter2D p, Color body)
        {
            // Flippers
            Ellipse(p, 26, 30, 10, 6, Theme.Hex("#B9E6B4")); Ellipse(p, 26, 60, 10, 6, Theme.Hex("#B9E6B4"));
            Ellipse(p, 64, 28, 9, 5, Theme.Hex("#B9E6B4")); Ellipse(p, 64, 62, 9, 5, Theme.Hex("#B9E6B4"));
            // Head
            Circle(p, 80, 44, 12, Theme.Hex("#B9E6B4"));
            // Shell with patches
            Ellipse(p, 44, 45, 28, 20, body);
            Circle(p, 44, 45, 7, Theme.Hex("#C9F0DE"));
            Circle(p, 30, 42, 4.5f, Theme.Hex("#C9F0DE")); Circle(p, 58, 42, 4.5f, Theme.Hex("#C9F0DE"));
            Circle(p, 40, 32, 4f, Theme.Hex("#C9F0DE")); Circle(p, 48, 58, 4f, Theme.Hex("#C9F0DE"));
            // Sleepy eyes
            Curve(p, 78, 40, 81, 43, 84, 40, 2.5f);
            Curve(p, 84, 47, 87, 51, 90, 46, 2.5f);
            Circle(p, 86, 52, 2.2f, Blush, false);
        }

        private void Starfish(Painter2D p, Color body)
        {
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = -Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? 36f : 15f;
                var point = P(50 + Mathf.Cos(a) * r, 44 + Mathf.Sin(a) * r * 0.95f);
                if (i == 0) p.MoveTo(point);
                else p.LineTo(point);
            }
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            // Dotty texture
            Circle(p, 50, 18, 2f, Color.white, false); Circle(p, 24, 38, 2f, Color.white, false);
            Circle(p, 76, 38, 2f, Color.white, false); Circle(p, 36, 66, 2f, Color.white, false); Circle(p, 64, 66, 2f, Color.white, false);
            Face(p, 44, 42, 56, 42, 3.8f);
            Smile(p, 50, 49, 8f);
        }

        private void Urchin(Painter2D p, Color body)
        {
            // Glowing spines with colored tips
            Color[] tips = { Theme.Hex("#FFC233"), Theme.Hex("#6EDBC8"), Theme.Hex("#FF8FB1"), Theme.Hex("#B7A3FF") };
            for (int i = 0; i < 14; i++)
            {
                float a = -Mathf.PI + i * Mathf.PI / 13f;
                float x2 = 50 + Mathf.Cos(a) * 36f, y2 = 56 + Mathf.Sin(a) * 36f;
                Line(p, 50 + Mathf.Cos(a) * 18f, 56 + Mathf.Sin(a) * 18f, x2, y2, 2.5f);
                Circle(p, x2, y2, 3.2f, tips[i % tips.Length]);
            }
            // Round body sitting on the sand
            p.BeginPath();
            p.MoveTo(P(24, 64));
            p.BezierCurveTo(P(24, 30), P(76, 30), P(76, 64));
            p.ClosePath();
            p.fillColor = body;
            p.Fill();
            p.Stroke();
            Face(p, 42, 52, 58, 52, 4f);
            Smile(p, 50, 59, 8f);
        }

        private void Octopus(Painter2D p, Color body)
        {
            // Curly arms
            Curve(p, 30, 50, 14, 62, 20, 74, 4f); Curve(p, 40, 56, 34, 72, 42, 78, 4f);
            Curve(p, 60, 56, 66, 72, 58, 78, 4f); Curve(p, 70, 50, 86, 62, 80, 74, 4f);
            p.strokeColor = body;
            Curve(p, 30, 50, 14, 62, 20, 74, 2f); Curve(p, 40, 56, 34, 72, 42, 78, 2f);
            Curve(p, 60, 56, 66, 72, 58, 78, 2f); Curve(p, 70, 50, 86, 62, 80, 74, 2f);
            p.strokeColor = Ink;
            // Head
            Ellipse(p, 50, 38, 24, 22, body);
            // Leafy cap
            Ellipse(p, 42, 16, 10, 5, Theme.Hex("#B9E6B4"));
            Ellipse(p, 58, 15, 9, 5, Theme.Hex("#8ED48F"));
            Face(p, 42, 38, 58, 38);
            Smile(p, 50, 46, 8f);
        }

        private void Angler(Painter2D p, Color body)
        {
            // Lantern on a stalk
            Curve(p, 54, 22, 60, 2, 78, 8, 2.5f);
            Circle(p, 80, 12, 7, Theme.Hex("#FFF4C2"));
            Circle(p, 80, 12, 3, Theme.Hex("#FFC233"), false);
            // Tail
            p.BeginPath();
            p.MoveTo(P(18, 44)); p.LineTo(P(4, 30)); p.LineTo(P(4, 58)); p.ClosePath();
            p.fillColor = body; p.Fill(); p.Stroke();
            // Body
            Ellipse(p, 48, 44, 30, 22, body);
            // Big friendly mouth with two tiny teeth
            p.BeginPath();
            p.MoveTo(P(60, 52));
            p.QuadraticCurveTo(P(70, 62), P(78, 50));
            p.ClosePath();
            p.fillColor = Theme.Hex("#13283D");
            p.Fill();
            p.Stroke();
            Circle(p, 66, 54, 1.8f, Color.white, false); Circle(p, 72, 54, 1.8f, Color.white, false);
            Face(p, 56, 36, 56, 36, 5f, false);
            Circle(p, 30, 40, 2.2f, Color.white, false); Circle(p, 36, 52, 1.8f, Color.white, false);
        }
    }
}
