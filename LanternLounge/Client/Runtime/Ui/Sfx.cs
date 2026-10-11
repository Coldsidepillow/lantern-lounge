using System.Collections.Generic;
using UnityEngine;

namespace LanternLounge.Client.Ui
{
    internal enum Sound
    {
        Tap,
        Select,
        Give,
        Dive,
        Found,
        Pod,
        YourTide,
        Win,
        Drop,
        Card,
        Flip,
        Zap,
        Shuffle,
        Coins
    }

    /// <summary>
    /// Soft, toy-like sound effects synthesized in code (marimba-ish plucks and bubbly glides),
    /// so the app needs no audio files. Respects the Sound setting.
    /// </summary>
    internal static class Sfx
    {
        private const int Rate = 44100;
        private static AudioSource source;
        private static readonly Dictionary<Sound, AudioClip> Clips = new Dictionary<Sound, AudioClip>();

        public static void Init(GameObject host)
        {
            if (source != null) return;
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.6f;

            Clips[Sound.Tap] = Notes("tap", 0.05f, 0.08f, 880f);
            Clips[Sound.Select] = Notes("select", 0.07f, 0.1f, 660f, 990f);
            Clips[Sound.Give] = Notes("give", 0.08f, 0.14f, 523f, 659f, 784f);
            Clips[Sound.Pod] = Notes("pod", 0.09f, 0.22f, 523f, 659f, 784f, 1047f);
            Clips[Sound.YourTide] = Notes("tide", 0.12f, 0.25f, 784f, 1175f);
            Clips[Sound.Win] = Notes("win", 0.12f, 0.35f, 523f, 659f, 784f, 659f, 784f, 1047f);
            Clips[Sound.Dive] = Glide("dive", 0.45f, 700f, 220f);
            Clips[Sound.Found] = Glide("found", 0.35f, 330f, 990f);
            Clips[Sound.Drop] = Glide("drop", 0.18f, 520f, 260f);
            Clips[Sound.Card] = Glide("card", 0.09f, 1400f, 900f);
            Clips[Sound.Flip] = Notes("flip", 0.06f, 0.12f, 988f, 784f, 988f);
            Clips[Sound.Zap] = Glide("zap", 0.3f, 1200f, 300f);
            Clips[Sound.Coins] = Notes("coins", 0.045f, 0.09f, 1568f, 1319f, 1760f, 1568f);
            Clips[Sound.Shuffle] = Notes("shuffle", 0.035f, 0.05f, 700f, 760f, 720f, 800f, 740f, 820f);
        }

        public static void Play(Sound sound)
        {
            if (source == null || !Prefs.SoundOn) return;
            if (Clips.TryGetValue(sound, out var clip)) source.PlayOneShot(clip);
        }

        // A run of soft plucked notes, one after another.
        private static AudioClip Notes(string name, float step, float ring, params float[] hz)
        {
            float length = step * (hz.Length - 1) + ring;
            int samples = Mathf.CeilToInt(length * Rate);
            var data = new float[samples];
            for (int n = 0; n < hz.Length; n++)
            {
                int start = Mathf.FloorToInt(n * step * Rate);
                int count = Mathf.FloorToInt(ring * Rate);
                for (int i = 0; i < count && start + i < samples; i++)
                {
                    float t = i / (float)Rate;
                    float envelope = Mathf.Exp(-t * 18f) * Mathf.Clamp01(t * 400f);
                    float tone = Mathf.Sin(2f * Mathf.PI * hz[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * hz[n] * t);
                    data[start + i] += 0.28f * envelope * tone;
                }
            }
            return Make(name, data);
        }

        // A smooth pitch slide with a little bubbly wobble.
        private static AudioClip Glide(string name, float length, float fromHz, float toHz)
        {
            int samples = Mathf.CeilToInt(length * Rate);
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float hz = Mathf.Lerp(fromHz, toHz, t) * (1f + 0.04f * Mathf.Sin(t * 60f));
                phase += 2f * Mathf.PI * hz / Rate;
                float envelope = Mathf.Sin(Mathf.PI * t);
                data[i] = 0.25f * envelope * Mathf.Sin(phase);
            }
            return Make(name, data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create("ll-" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
