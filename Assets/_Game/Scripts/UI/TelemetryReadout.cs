using System.Text;
using OpenSkies.FlightState;
using UnityEngine;

namespace OpenSkies.UI
{
    /// <summary>
    /// Formats the development overlay line. Values are rounded to what a pilot
    /// would read, and the text is rebuilt only when a rounded value changes.
    /// </summary>
    public sealed class TelemetryReadout
    {
        readonly StringBuilder _builder = new StringBuilder(96);
        int _speed = int.MinValue, _altitude = int.MinValue, _vs = int.MinValue, _power = int.MinValue, _aoa = int.MinValue;
        bool _stall;
        string _text = string.Empty;

        /// <summary>Returns true when the text changed. Allocates only on change.</summary>
        public bool Update(in FlightTelemetry t, out string text)
        {
            int speed = Mathf.RoundToInt(t.IndicatedAirspeedKnots);
            int altitude = Mathf.RoundToInt(t.AltitudeFeet / 10f) * 10;             // altimeter-like 10 ft steps
            int vs = Mathf.RoundToInt(t.VerticalSpeedFeetPerMinute / 50f) * 50;     // VSI-like 50 fpm steps
            int power = Mathf.RoundToInt(t.Throttle * 100f);
            int aoa = Mathf.RoundToInt(t.AngleOfAttackDegrees);
            bool stall = t.StallWarning;

            if (speed == _speed && altitude == _altitude && vs == _vs && power == _power && aoa == _aoa && stall == _stall)
            {
                text = _text;
                return false;
            }

            _speed = speed; _altitude = altitude; _vs = vs; _power = power; _aoa = aoa; _stall = stall;
            _builder.Clear()
                .Append("IAS ").Append(speed).Append(" kt   ALT ").Append(altitude)
                .Append(" ft   VS ").Append(vs > 0 ? "+" : string.Empty).Append(vs)
                .Append(" fpm   PWR ").Append(power).Append("%   AOA ").Append(aoa).Append('°');
            if (stall) _builder.Append("   STALL");
            _text = _builder.ToString();
            text = _text;
            return true;
        }
    }
}
