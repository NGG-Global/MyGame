using System;

namespace OpenSkies.UI
{
    /// <summary>
    /// Caches the text of each number the first time it is shown, so changing
    /// readouts do not allocate strings every update. Values outside the range
    /// are formatted on the fly (rare).
    /// </summary>
    public sealed class NumberStrings
    {
        readonly int _min;
        readonly string[] _cache;
        readonly Func<int, string> _format;

        public NumberStrings(int min, int max, Func<int, string> format)
        {
            if (max < min) throw new ArgumentException("max < min");
            _min = min;
            _cache = new string[max - min + 1];
            _format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public string Get(int value)
        {
            int index = value - _min;
            if (index < 0 || index >= _cache.Length) return _format(value);
            return _cache[index] ?? (_cache[index] = _format(value));
        }

        /// <summary>"12.3" from 123 tenths; "-0.5" from -5.</summary>
        public static string Tenths(int tenths)
        {
            int whole = Math.Abs(tenths) / 10;
            int fraction = Math.Abs(tenths) % 10;
            return string.Concat(tenths < 0 ? "-" : string.Empty, whole.ToString(), ".", fraction.ToString());
        }

        /// <summary>"+500", "-500", "0".</summary>
        public static string Signed(int value) => value > 0 ? "+" + value.ToString() : value.ToString();

        /// <summary>Compass style: "005", "270", 360 shown as "360".</summary>
        public static string Heading(int degrees) => degrees.ToString("000");
    }
}
