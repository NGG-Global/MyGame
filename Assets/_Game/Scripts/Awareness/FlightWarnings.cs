using System;
using OpenSkies.FlightState;
using UnityEngine;
using static OpenSkies.Awareness.SoftLogic;

namespace OpenSkies.Awareness
{
    /// <summary>Flight warnings. Values are stable: used as array indices and bit positions.</summary>
    public enum FlightWarning
    {
        Stall = 0,
        /// <summary>Descending too fast for the height above ground.</summary>
        SinkRate = 1,
        Overspeed = 2,
        /// <summary>The engine is not running while power is expected (airborne, or throttle open).</summary>
        EngineOff = 3,
    }

    public enum WarningSeverity
    {
        /// <summary>Needs attention soon (amber).</summary>
        Caution = 0,
        /// <summary>Needs action now (red).</summary>
        Warning = 1,
    }

    public static class FlightWarnings
    {
        public const int Count = 4;

        public static int Bit(FlightWarning warning) => 1 << (int)warning;

        public static string Label(FlightWarning warning)
        {
            switch (warning)
            {
                case FlightWarning.Stall: return "STALL";
                case FlightWarning.SinkRate: return "SINK RATE";
                case FlightWarning.Overspeed: return "OVERSPEED";
                case FlightWarning.EngineOff: return "ENGINE OFF";
                default: return "?";
            }
        }

        public static WarningSeverity Severity(FlightWarning warning) =>
            warning == FlightWarning.SinkRate || warning == FlightWarning.EngineOff ? WarningSeverity.Caution : WarningSeverity.Warning;
    }

    /// <summary>
    /// Limits for the warning monitor. PROTOTYPE TUNING VALUES for the fictional
    /// trainer: they are not certified limits or a certified terrain-warning
    /// envelope. The overspeed limit belongs to the aircraft and should move to
    /// the aircraft data when there is more than one aircraft.
    /// </summary>
    [Serializable]
    public sealed class FlightWarningSettings
    {
        [Header("Stall (angle-of-attack warning from the telemetry)")]
        [Tooltip("The stall warning stays on at least this long once triggered, s.")]
        public float stallMinimumSeconds = 1f;

        [Header("Sink rate near terrain")]
        [Tooltip("Inactive below this height above ground (flare and touchdown), ft.")]
        public float sinkRateMinHeightFeet = 30f;
        [Tooltip("Descent rate limit at the minimum height, fpm.")]
        public float sinkRateLimitAtMinHeight = 1000f;
        [Tooltip("Inactive above this height above ground, ft.")]
        public float sinkRateMaxHeightFeet = 2500f;
        [Tooltip("Descent rate limit at the maximum height, fpm. The limit is linear in between.")]
        public float sinkRateLimitAtMaxHeight = 3000f;
        [Tooltip("The descent rate must exceed the limit this long before the warning, s.")]
        public float sinkRateDelaySeconds = 0.8f;
        [Tooltip("The warning clears when the descent rate is this far below the limit, fpm.")]
        public float sinkRateClearMargin = 200f;
        public float sinkRateMinimumSeconds = 2f;

        [Header("Overspeed")]
        [Tooltip("Never-exceed speed, indicated, kt.")]
        public float neverExceedSpeedKnots = 160f;
        [Tooltip("The warning clears this far below the never-exceed speed, kt.")]
        public float overspeedClearMarginKnots = 3f;
        public float overspeedDelaySeconds = 0.3f;
        public float overspeedMinimumSeconds = 1f;

        [Tooltip("Normal operating limit (top of the green arc), indicated, kt. Airspeed indicator marking only.")]
        public float normalOperatingSpeedKnots = 125f;
        [Tooltip("Maximum speed with flaps extended (top of the white arc), indicated, kt. Airspeed indicator marking only.")]
        public float flapExtendedSpeedKnots = 85f;

        [Header("Engine off")]
        [Tooltip("On the ground, the caution appears only when the throttle is opened beyond this (0-1).")]
        public float engineOffThrottle = 0.2f;
        public float engineOffDelaySeconds = 1f;
        public float engineOffMinimumSeconds = 2f;

        [Header("Priority (0 is the most urgent)")]
        public int stallPriority = 0;
        public int sinkRatePriority = 1;
        public int overspeedPriority = 2;
        public int engineOffPriority = 3;

        public int Priority(FlightWarning warning)
        {
            switch (warning)
            {
                case FlightWarning.Stall: return stallPriority;
                case FlightWarning.SinkRate: return sinkRatePriority;
                case FlightWarning.EngineOff: return engineOffPriority;
                default: return overspeedPriority;
            }
        }

        /// <summary>Highest acceptable descent rate at a height above ground, fpm (positive number).</summary>
        public float SinkRateLimit(float heightAboveGroundFeet) =>
            Mathf.Lerp(sinkRateLimitAtMinHeight, sinkRateLimitAtMaxHeight,
                Mathf.InverseLerp(sinkRateMinHeightFeet, sinkRateMaxHeightFeet, heightAboveGroundFeet));
    }

    /// <summary>
    /// Small priority-based warning manager. Each warning has a trigger
    /// condition, a separate (lower) clear condition for hysteresis, an optional
    /// trigger delay and a minimum on-time, so warnings neither flicker nor miss
    /// short real events. Warnings never block input: presentation shows them as
    /// banners, never as modal dialogs. Allocation-free.
    /// </summary>
    public sealed class FlightWarningMonitor
    {
        struct Channel
        {
            public bool Active;
            public float PendingTime;
            public float ActiveTime;
        }

        readonly FlightWarningSettings _settings;
        readonly Channel[] _channels = new Channel[FlightWarnings.Count];
        readonly FlightWarning[] _ordered = new FlightWarning[FlightWarnings.Count];
        int _activeCount;

        public FlightWarningMonitor(FlightWarningSettings settings = null)
        {
            _settings = settings ?? new FlightWarningSettings();
        }

        public FlightWarningSettings Settings => _settings;

        /// <summary>Number of active warnings.</summary>
        public int ActiveCount => _activeCount;

        public bool AnyActive => _activeCount > 0;

        /// <summary>Active warnings by priority, most urgent first.</summary>
        public FlightWarning ActiveAt(int index)
        {
            if (index < 0 || index >= _activeCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _ordered[index];
        }

        public bool IsActive(FlightWarning warning) => _channels[(int)warning].Active;

        /// <summary>One bit per active warning (<see cref="FlightWarnings.Bit"/>).</summary>
        public int ActiveMask
        {
            get
            {
                int mask = 0;
                for (int i = 0; i < FlightWarnings.Count; i++)
                    if (_channels[i].Active) mask |= 1 << i;
                return mask;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _channels.Length; i++) _channels[i] = default;
            _activeCount = 0;
        }

        public void Update(in FlightTelemetry t, float deltaTime)
        {
            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
            var s = _settings;
            bool airborne = !t.WeightOnWheels;

            // Stall: the aircraft's own angle-of-attack warning (already inhibited on the ground).
            Step(FlightWarning.Stall, t.StallWarning, !t.StallWarning, 0f, s.stallMinimumSeconds, deltaTime);

            // Sink rate: descent rate above a height-dependent limit, close to the ground.
            float height = Finite(t.HeightAboveGroundFeet, float.MaxValue);
            float descentRate = -Finite(t.VerticalSpeedFeetPerMinute, 0f);
            bool inBand = airborne && height >= s.sinkRateMinHeightFeet && height <= s.sinkRateMaxHeightFeet;
            float limit = s.SinkRateLimit(height);
            Step(FlightWarning.SinkRate, inBand && descentRate > limit, !inBand || descentRate < limit - s.sinkRateClearMargin,
                s.sinkRateDelaySeconds, s.sinkRateMinimumSeconds, deltaTime);

            // Overspeed: indicated airspeed above the never-exceed speed.
            float ias = Finite(t.IndicatedAirspeedKnots, 0f);
            Step(FlightWarning.Overspeed, ias > s.neverExceedSpeedKnots, ias < s.neverExceedSpeedKnots - s.overspeedClearMarginKnots,
                s.overspeedDelaySeconds, s.overspeedMinimumSeconds, deltaTime);

            // Engine off while power is expected. Starting is not off.
            bool engineOff = t.EngineState == EngineState.Off
                             && (airborne || Finite(t.Throttle, 0f) > s.engineOffThrottle);
            Step(FlightWarning.EngineOff, engineOff, !engineOff, s.engineOffDelaySeconds, s.engineOffMinimumSeconds, deltaTime);

            SortByPriority();
        }

        void Step(FlightWarning warning, bool trigger, bool clear, float delay, float minimumOn, float dt)
        {
            ref Channel c = ref _channels[(int)warning];
            if (c.Active)
            {
                c.ActiveTime += dt;
                if (clear && c.ActiveTime >= minimumOn)
                {
                    c.Active = false;
                    c.ActiveTime = 0f;
                    c.PendingTime = 0f;
                }
                return;
            }

            c.PendingTime = trigger ? c.PendingTime + dt : 0f;
            if (trigger && c.PendingTime + 1e-4f >= delay)
            {
                c.Active = true;
                c.ActiveTime = 0f;
            }
        }

        void SortByPriority()
        {
            _activeCount = 0;
            for (int i = 0; i < FlightWarnings.Count; i++)
            {
                if (!_channels[i].Active) continue;
                var warning = (FlightWarning)i;
                int priority = _settings.Priority(warning);
                int slot = _activeCount++;
                while (slot > 0 && _settings.Priority(_ordered[slot - 1]) > priority)
                {
                    _ordered[slot] = _ordered[slot - 1];
                    slot--;
                }
                _ordered[slot] = warning;
            }
        }
    }
}
