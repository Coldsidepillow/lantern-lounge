using UnityEngine;
using UnityEngine.UIElements;

namespace LanternLounge.Client.Ui
{
    /// <summary>Small, cheerful animations. Each one does nothing extra when Reduce motion is on.</summary>
    internal static class Fx
    {
        /// <summary>A quick squash-and-stretch bounce, for taps and new cards.</summary>
        public static void Pop(VisualElement element, float amount = 0.14f, float seconds = 0.26f)
        {
            if (element == null || Prefs.ReduceMotion) return;
            float elapsed = 0f;
            element.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / seconds);
                float s = 1f + amount * Mathf.Sin(t * Mathf.PI);
                element.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
            }).Every(16).Until(() => elapsed >= seconds);
        }

        /// <summary>
        /// A big friendly message that pops into the middle of <paramref name="host"/> and floats away,
        /// e.g. "Pod!" or "Your tide!".
        /// </summary>
        public static void Toast(VisualElement host, string text, Color background, Color textColor, float seconds = 1.3f)
        {
            if (host == null) return;

            var toast = Theme.Label(text, 30, textColor, bold: true);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.position = Position.Absolute;
            toast.style.left = 24;
            toast.style.right = 24;
            toast.style.top = Length.Percent(38);
            toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            toast.style.backgroundColor = background;
            Theme.Border(toast, 4, Theme.Hex("#13283D"));
            Theme.Radius(toast, 26);
            Theme.Padding(toast, 14, 16);
            host.Add(toast);

            float elapsed = 0f;
            toast.schedule.Execute(timer =>
            {
                elapsed += timer.deltaTime / 1000f;
                float t = Mathf.Clamp01(elapsed / seconds);
                if (!Prefs.ReduceMotion)
                {
                    // Pop in (overshoot), hold, then drift up while fading.
                    float s = t < 0.18f ? Mathf.Lerp(0.5f, 1.08f, t / 0.18f) : (t < 0.28f ? Mathf.Lerp(1.08f, 1f, (t - 0.18f) / 0.1f) : 1f);
                    toast.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                    float rise = t > 0.7f ? (t - 0.7f) / 0.3f * -40f : 0f;
                    toast.style.translate = new StyleTranslate(new Translate(0, rise));
                }
                toast.style.opacity = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                if (t >= 1f) toast.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= seconds);
        }

        /// <summary>Colorful paper confetti that tumbles down over <paramref name="host"/> for a moment.</summary>
        public static void Confetti(VisualElement host, Color[] colors, int count = 36, float seconds = 1.8f)
        {
            if (host == null || Prefs.ReduceMotion || colors == null || colors.Length == 0) return;
            for (int n = 0; n < count; n++)
            {
                int i = n;
                var bit = new VisualElement();
                bit.pickingMode = PickingMode.Ignore;
                bit.style.position = Position.Absolute;
                float x = (i * 37 % 100);
                bit.style.left = Length.Percent(x);
                bit.style.top = -20;
                bit.style.width = 8 + i % 3 * 3;
                bit.style.height = 12 + i % 4 * 2;
                bit.style.backgroundColor = colors[i % colors.Length];
                Theme.Radius(bit, 2);
                host.Add(bit);

                float delay = (i % 9) * 0.06f;
                float fall = 0.75f + (i % 5) * 0.08f;
                float sway = 10f + i % 7 * 3f;
                float spin = (i % 2 == 0 ? 1f : -1f) * (200f + i % 5 * 60f);
                float elapsed = 0f;
                bit.schedule.Execute(timer =>
                {
                    elapsed += timer.deltaTime / 1000f;
                    float t = Mathf.Clamp01((elapsed - delay) / seconds);
                    float y = t * fall * 900f;
                    float dx = Mathf.Sin((elapsed + i) * 4f) * sway;
                    bit.style.translate = new StyleTranslate(new Translate(dx, y));
                    bit.style.rotate = new StyleRotate(new Rotate(new Angle(spin * elapsed, AngleUnit.Degree)));
                    bit.style.opacity = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
                    if (t >= 1f) bit.RemoveFromHierarchy();
                }).Every(16).Until(() => elapsed >= seconds + delay);
            }
        }

        /// <summary>
        /// A layer of bubbles that drift up behind the screen's content. Add it first so it sits at
        /// the back. With Reduce motion on, the bubbles stay still.
        /// </summary>
        public static VisualElement Bubbles(Color ring, int count = 12)
        {
            var layer = new VisualElement();
            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.right = 0;
            layer.style.top = 0;
            layer.style.bottom = 0;
            layer.style.overflow = Overflow.Hidden;

            // Fixed, hand-picked spots so the layout looks the same every time.
            float[] xs = { 6, 18, 31, 44, 57, 70, 83, 92, 12, 38, 64, 88 };
            float[] sizes = { 10, 16, 8, 14, 20, 9, 13, 18, 7, 11, 15, 8 };
            float[] speeds = { 22, 15, 28, 18, 12, 26, 20, 14, 30, 24, 16, 27 };
            float[] starts = { 0.1f, 0.5f, 0.8f, 0.3f, 0.65f, 0.95f, 0.2f, 0.45f, 0.7f, 0.05f, 0.85f, 0.35f };

            for (int i = 0; i < count && i < xs.Length; i++)
            {
                var bubble = new VisualElement();
                bubble.pickingMode = PickingMode.Ignore;
                bubble.style.position = Position.Absolute;
                bubble.style.left = Length.Percent(xs[i]);
                Theme.Circle(bubble, sizes[i]);
                Theme.Border(bubble, 2, ring);
                bubble.style.opacity = 0.55f;
                layer.Add(bubble);

                float speed = speeds[i];
                float progress = starts[i];
                bubble.style.top = Length.Percent(100f - progress * 100f);
                bubble.schedule.Execute(timer =>
                {
                    if (Prefs.ReduceMotion) return;
                    progress += timer.deltaTime / 1000f / speed;
                    if (progress > 1.08f) progress -= 1.16f;
                    bubble.style.top = Length.Percent(100f - progress * 100f);
                    float wobble = Mathf.Sin(progress * 14f + speed) * 6f;
                    bubble.style.translate = new StyleTranslate(new Translate(wobble, 0));
                }).Every(33);
            }
            return layer;
        }
    }
}
