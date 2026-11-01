using System;
using UnityEngine;

namespace OpenSkies.Audio
{
    /// <summary>The generated placeholder sounds.</summary>
    public enum PlaceholderSound
    {
        EngineLow = 0,
        EngineHigh,
        Airflow,
        Rolling,
        Ambience,
        Touchdown,
        StallHorn,
        WarningTone,
        CautionChime,
        UiTap,
        UiConfirm,
        SwitchClick,
    }

    /// <summary>
    /// PLACEHOLDER sounds generated once from simple formulas (harmonic series,
    /// filtered noise, decaying tones), so the game has convincing-enough audio
    /// without any third-party recording. They are clearly temporary: replace
    /// them by assigning designed or licensed clips in <see cref="GameAudioConfiguration"/>.
    /// Loops are seamless (whole cycles for tones, a crossfaded seam for noise).
    /// Generation is deterministic (fixed seeds) and costs a few milliseconds once
    /// per app run; nothing is synthesised per frame.
    /// </summary>
    public static class PlaceholderAudio
    {
        public const int ToneRate = 22050;
        public const int NoiseRate = 11025;
        public const int Count = 12;
        /// <summary>Clip pitch 1 corresponds to this RPM fraction with the default engine settings.</summary>
        public const float EngineFundamentalHz = 45f;

        static readonly AudioClip[] Clips = new AudioClip[Count];

        public static bool IsLoop(PlaceholderSound sound) =>
            sound == PlaceholderSound.EngineLow || sound == PlaceholderSound.EngineHigh || sound == PlaceholderSound.Airflow
            || sound == PlaceholderSound.Rolling || sound == PlaceholderSound.Ambience || sound == PlaceholderSound.StallHorn;

        /// <summary>The generated clip (created on first use, then shared). Names start with "PLACEHOLDER".</summary>
        public static AudioClip Clip(PlaceholderSound sound)
        {
            int i = (int)sound;
            if (Clips[i] != null) return Clips[i];
            float[] samples = Samples(sound, out int rate);
            var clip = AudioClip.Create("PLACEHOLDER " + sound, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            Clips[i] = clip;
            return clip;
        }

        /// <summary>The samples of a sound (mono, -1..1) and their sample rate. Pure: EditMode-testable.</summary>
        public static float[] Samples(PlaceholderSound sound, out int sampleRate)
        {
            switch (sound)
            {
                case PlaceholderSound.EngineLow: sampleRate = ToneRate; return Engine(bright: false);
                case PlaceholderSound.EngineHigh: sampleRate = ToneRate; return Engine(bright: true);
                case PlaceholderSound.Airflow: sampleRate = NoiseRate; return Airflow();
                case PlaceholderSound.Rolling: sampleRate = NoiseRate; return Rolling();
                case PlaceholderSound.Ambience: sampleRate = NoiseRate; return Ambience();
                case PlaceholderSound.Touchdown: sampleRate = ToneRate; return Touchdown();
                case PlaceholderSound.StallHorn: sampleRate = ToneRate; return StallHorn();
                case PlaceholderSound.WarningTone: sampleRate = ToneRate; return WarningTone();
                case PlaceholderSound.CautionChime: sampleRate = ToneRate; return CautionChime();
                case PlaceholderSound.UiTap: sampleRate = ToneRate; return Tap(1400f, 0.03f, 0.006f);
                case PlaceholderSound.UiConfirm: sampleRate = ToneRate; return Confirm();
                default: sampleRate = ToneRate; return SwitchClick();
            }
        }

        // ---- Loops ---------------------------------------------------------------------------------

        /// <summary>
        /// Four-cylinder engine and two-blade propeller at the reference RPM: firing and
        /// blade-pass fundamental, a half order for cylinder-to-cylinder unevenness, and
        /// exhaust noise pulsed by the firing. The bright layer adds upper harmonics and
        /// propeller buzz for high power.
        /// </summary>
        static float[] Engine(bool bright)
        {
            const float seconds = 2f; // whole cycles of every partial (multiples of 0.5 Hz)
            int n = (int)(ToneRate * seconds);
            var s = new float[n];
            float f0 = EngineFundamentalHz;
            int partials = bright ? 14 : 6;
            var rng = new System.Random(bright ? 211 : 101);
            var phase = new double[partials + 1];
            for (int h = 0; h <= partials; h++) phase[h] = rng.NextDouble() * Math.PI * 2.0;

            float[] noise = LoopedNoise(n, ToneRate, bright ? 1600f : 380f, 60f, bright ? 307 : 409);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double v = 0.3 * Math.Sin(2.0 * Math.PI * f0 * 0.5 * t + phase[0]); // half order
                for (int h = 1; h <= partials; h++)
                {
                    double amp = bright ? 1.0 / Math.Pow(h, 0.75) : 1.0 / (h * h * 0.6 + 0.4);
                    if (bright && h == 2) amp *= 1.5; // propeller blade-pass buzz
                    v += amp * Math.Sin(2.0 * Math.PI * f0 * h * t + phase[h]);
                }
                // Exhaust pulses: noise gated by the firing rhythm, with a slow (0.5 Hz) breathing.
                double pulse = 0.55 + 0.45 * Math.Sin(2.0 * Math.PI * f0 * t);
                double breathe = 0.9 + 0.1 * Math.Sin(2.0 * Math.PI * 0.5 * t);
                v = v * breathe + noise[i] * pulse * (bright ? 1.4 : 0.9);
                s[i] = (float)v;
            }
            Normalise(s, bright ? 0.7f : 0.75f);
            return s;
        }

        /// <summary>Broad airflow rush with slow gusting.</summary>
        static float[] Airflow()
        {
            const float seconds = 4f;
            int n = (int)(NoiseRate * seconds);
            float[] s = LoopedNoise(n, NoiseRate, 900f, 120f, 503);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / NoiseRate;
                s[i] *= (float)(0.85 + 0.1 * Math.Sin(2.0 * Math.PI * 0.5 * t) + 0.05 * Math.Sin(2.0 * Math.PI * 1.25 * t));
            }
            Normalise(s, 0.7f);
            return s;
        }

        /// <summary>Low tyre rumble with a faint wheel rhythm.</summary>
        static float[] Rolling()
        {
            const float seconds = 2f;
            int n = (int)(NoiseRate * seconds);
            float[] s = LoopedNoise(n, NoiseRate, 160f, 25f, 607);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / NoiseRate;
                s[i] *= (float)(0.8 + 0.2 * Math.Sin(2.0 * Math.PI * 8.0 * t));
            }
            Normalise(s, 0.75f);
            return s;
        }

        /// <summary>Outdoor bed: soft wind and slow, distant surf swells.</summary>
        static float[] Ambience()
        {
            const float seconds = 8f;
            int n = (int)(NoiseRate * seconds);
            float[] wind = LoopedNoise(n, NoiseRate, 350f, 40f, 701);
            float[] surf = LoopedNoise(n, NoiseRate, 700f, 150f, 809);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / NoiseRate;
                // Swells every 4 s (two per loop): a slow rise and a longer fall.
                double swell = Math.Pow(0.5 + 0.5 * Math.Sin(2.0 * Math.PI * 0.25 * t), 2.0);
                s[i] = (float)(wind[i] * 0.6 + surf[i] * (0.15 + 0.55 * swell));
            }
            Normalise(s, 0.6f);
            return s;
        }

        /// <summary>Reed-horn buzz at 440 Hz with a slight tremolo, like a light aircraft stall horn.</summary>
        static float[] StallHorn()
        {
            int n = ToneRate; // 1 s: 440 Hz and 6 Hz are whole cycles
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double v = 0;
                for (int h = 1; h <= 9; h += 2) v += Math.Sin(2.0 * Math.PI * 440.0 * h * t) / h;
                s[i] = (float)(v * (0.85 + 0.15 * Math.Sin(2.0 * Math.PI * 6.0 * t)));
            }
            Normalise(s, 0.6f);
            return s;
        }

        // ---- One-shots -------------------------------------------------------------------------------

        /// <summary>Tyres and gear meeting the runway: a low thump, a short crunch and a faint chirp.</summary>
        static float[] Touchdown()
        {
            int n = (int)(ToneRate * 0.45f);
            var s = new float[n];
            float[] crunch = Noise(n, ToneRate, 700f, 60f, 907);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double thumpFreq = 70.0 - 25.0 * Math.Min(1.0, t / 0.12);
                double thump = Math.Sin(2.0 * Math.PI * thumpFreq * t) * Math.Exp(-t / 0.07);
                double chirp = t < 0.08 ? Math.Sin(2.0 * Math.PI * 1650.0 * t) * Math.Exp(-t / 0.025) * 0.18 : 0.0;
                s[i] = (float)(thump + crunch[i] * Math.Exp(-t / 0.05) * 0.9 + chirp);
            }
            Fade(s, 0.002f, 0.04f, ToneRate);
            Normalise(s, 0.9f);
            return s;
        }

        /// <summary>Two short 1 kHz beeps: the attention tone for red warnings.</summary>
        static float[] WarningTone()
        {
            int n = (int)(ToneRate * 0.42f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double gate = Gate(t, 0.0, 0.13) + Gate(t, 0.21, 0.34);
                s[i] = (float)(gate * (Math.Sin(2.0 * Math.PI * 1000.0 * t) + 0.25 * Math.Sin(2.0 * Math.PI * 2000.0 * t)));
            }
            Normalise(s, 0.7f);
            return s;
        }

        /// <summary>One soft chime for cautions.</summary>
        static float[] CautionChime()
        {
            int n = (int)(ToneRate * 0.8f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double env = Math.Exp(-t / 0.22) * Math.Min(1.0, t / 0.004);
                s[i] = (float)(env * (Math.Sin(2.0 * Math.PI * 880.0 * t) + 0.35 * Math.Sin(2.0 * Math.PI * 1760.0 * t)
                                      + 0.12 * Math.Sin(2.0 * Math.PI * 2640.0 * t)));
            }
            Fade(s, 0f, 0.05f, ToneRate);
            Normalise(s, 0.6f);
            return s;
        }

        /// <summary>A short, soft tick.</summary>
        static float[] Tap(float frequency, float seconds, float decay)
        {
            int n = (int)(ToneRate * seconds);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                s[i] = (float)(Math.Sin(2.0 * Math.PI * frequency * t) * Math.Exp(-t / decay) * Math.Min(1.0, t / 0.0008));
            }
            Fade(s, 0f, 0.005f, ToneRate);
            Normalise(s, 0.5f);
            return s;
        }

        /// <summary>Two quick rising tones: a choice confirmed.</summary>
        static float[] Confirm()
        {
            int n = (int)(ToneRate * 0.11f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                double first = Gate(t, 0.0, 0.045) * Math.Sin(2.0 * Math.PI * 900.0 * t);
                double second = Gate(t, 0.05, 0.11) * Math.Sin(2.0 * Math.PI * 1350.0 * t);
                s[i] = (float)(first + second);
            }
            Normalise(s, 0.5f);
            return s;
        }

        /// <summary>A small mechanical switch: a noise snap and a high ping.</summary>
        static float[] SwitchClick()
        {
            int n = (int)(ToneRate * 0.03f);
            var s = new float[n];
            float[] snap = Noise(n, ToneRate, 6000f, 1500f, 1009);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / ToneRate;
                s[i] = (float)(snap[i] * Math.Exp(-t / 0.002) + 0.5 * Math.Sin(2.0 * Math.PI * 2500.0 * t) * Math.Exp(-t / 0.004));
            }
            Fade(s, 0f, 0.004f, ToneRate);
            Normalise(s, 0.6f);
            return s;
        }

        // ---- Building blocks ---------------------------------------------------------------------------

        /// <summary>Band-limited noise (one-pole low and high pass), deterministic.</summary>
        static float[] Noise(int n, int rate, float lowpassHz, float highpassHz, int seed)
        {
            var rng = new System.Random(seed);
            var s = new float[n];
            float lp = OnePole(lowpassHz, rate), hp = OnePole(highpassHz, rate);
            float low = 0f, slow = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (white - low) * lp;           // low pass
                slow += (low - slow) * hp;           // its own low pass, subtracted: high pass
                s[i] = low - slow;
            }
            Normalise(s, 1f);
            return s;
        }

        /// <summary>Filtered noise whose end runs into its start: the last <c>fade</c> samples crossfade into the beginning.</summary>
        static float[] LoopedNoise(int n, int rate, float lowpassHz, float highpassHz, int seed)
        {
            int fade = Mathf.Min(n / 4, rate / 10); // 0.1 s seam
            float[] y = Noise(n + fade, rate, lowpassHz, highpassHz, seed);
            var s = new float[n];
            Array.Copy(y, s, n);
            for (int i = 0; i < fade; i++)
            {
                // At i = 0 the sample continues y[n - 1] (the loop's last sample); by i = fade it is y[i] again.
                // Equal-power weights: the two noise streams are uncorrelated.
                float w = (float)i / fade;
                s[i] = y[n + i] * Mathf.Sqrt(1f - w) + y[i] * Mathf.Sqrt(w);
            }
            return s;
        }

        static float OnePole(float cutoffHz, int rate) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / rate);

        /// <summary>1 inside [start, end] with 5 ms ramps.</summary>
        static double Gate(double t, double start, double end)
        {
            const double ramp = 0.005;
            if (t < start || t > end) return 0.0;
            return Math.Min(1.0, Math.Min((t - start) / ramp, (end - t) / ramp));
        }

        static void Fade(float[] s, float inSeconds, float outSeconds, int rate)
        {
            int fadeIn = (int)(inSeconds * rate), fadeOut = (int)(outSeconds * rate);
            for (int i = 0; i < fadeIn && i < s.Length; i++) s[i] *= (float)i / fadeIn;
            for (int i = 0; i < fadeOut && i < s.Length; i++) s[s.Length - 1 - i] *= (float)i / fadeOut;
        }

        static void Normalise(float[] s, float peak)
        {
            float max = 0f;
            for (int i = 0; i < s.Length; i++) max = Mathf.Max(max, Mathf.Abs(s[i]));
            if (max <= 0f) return;
            float k = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= k;
        }
    }
}
