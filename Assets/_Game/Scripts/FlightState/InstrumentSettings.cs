using System;

namespace OpenSkies.FlightState
{
    /// <summary>Values the pilot sets on the instruments themselves. They never reach the flight model.</summary>
    public enum InstrumentSetting
    {
        /// <summary>The heading bug on the heading indicator, degrees 1-360. A reference the pilot chooses; nothing steers to it.</summary>
        HeadingBug = 0,
        /// <summary>The altimeter's barometric subscale (QNH), whole hectopascals.</summary>
        BaroHectopascals = 1,
    }

    /// <summary>
    /// The one authoritative state of the instrument settings (heading bug, altimeter
    /// subscale), beside <see cref="AircraftSystems"/>. Every view reads it and every
    /// change, from the cockpit, a gesture or the keyboard, goes through
    /// <see cref="Set"/>, so no view can disagree with another. Plain C#.
    /// </summary>
    public sealed class InstrumentSettings
    {
        public const int StandardPressureHectopascals = 1013;
        public const int MinBaroHectopascals = 950;
        public const int MaxBaroHectopascals = 1050;

        public event Action<InstrumentSetting> Changed;

        public int Version { get; private set; }
        /// <summary>1-360 (360 = north).</summary>
        public int HeadingBugDegrees { get; private set; } = 360;
        public int BaroHectopascals { get; private set; } = StandardPressureHectopascals;

        public int Get(InstrumentSetting setting) => setting == InstrumentSetting.HeadingBug ? HeadingBugDegrees : BaroHectopascals;

        /// <summary>Sets a value, wrapped (heading) or clamped (baro). Returns whether anything changed.</summary>
        public bool Set(InstrumentSetting setting, int value)
        {
            int next = Sanitize(setting, value);
            if (next == Get(setting)) return false;
            if (setting == InstrumentSetting.HeadingBug) HeadingBugDegrees = next;
            else BaroHectopascals = next;
            Version++;
            Changed?.Invoke(setting);
            return true;
        }

        /// <summary>A valid value for a setting: headings wrap to 1-360, pressures are clamped.</summary>
        public static int Sanitize(InstrumentSetting setting, int value)
        {
            if (setting == InstrumentSetting.HeadingBug)
            {
                int wrapped = value % 360;
                if (wrapped <= 0) wrapped += 360;
                return wrapped;
            }
            return value < MinBaroHectopascals ? MinBaroHectopascals : (value > MaxBaroHectopascals ? MaxBaroHectopascals : value);
        }

        /// <summary>
        /// What an altimeter set to <paramref name="baroHectopascals"/> adds to the true altitude
        /// of a sea-level world: about 27 ft per hectopascal above the standard setting (a
        /// standard-atmosphere approximation near sea level, enough for a trainer's subscale).
        /// </summary>
        public static float BaroCorrectionFeet(int baroHectopascals) => (baroHectopascals - 1013.25f) * 27f;

        public void Reset()
        {
            bool changed = HeadingBugDegrees != 360 || BaroHectopascals != StandardPressureHectopascals;
            HeadingBugDegrees = 360;
            BaroHectopascals = StandardPressureHectopascals;
            if (!changed) return;
            Version++;
            Changed?.Invoke(InstrumentSetting.HeadingBug);
        }
    }
}
