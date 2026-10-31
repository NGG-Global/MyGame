using System.Collections.Generic;
using OpenSkies.FlightState;

namespace OpenSkies.FlightInput
{
    /// <summary>
    /// Merges every enabled input provider into one standard
    /// <see cref="FlightControls"/> per physics step. It owns the single throttle
    /// lever, so keyboard and touch always agree on throttle position, and
    /// nothing but an explicit throttle input (or a reset) moves it.
    /// </summary>
    public sealed class FlightInputRouter : IFlightControlSource
    {
        readonly List<IFlightInputProvider> _providers = new List<IFlightInputProvider>(4);
        readonly ThrottleLever _throttle = new ThrottleLever();

        public float Throttle => _throttle.Position;
        public FlightControls Current { get; private set; }

        public void AddProvider(IFlightInputProvider provider)
        {
            if (provider != null && !_providers.Contains(provider)) _providers.Add(provider);
        }

        public void RemoveProvider(IFlightInputProvider provider) => _providers.Remove(provider);

        public FlightControls Read(float deltaTime)
        {
            var frame = new FlightInputFrame();
            for (int i = 0; i < _providers.Count; i++) _providers[i].Contribute(ref frame, deltaTime);

            _throttle.Nudge(frame.ThrottleDelta);
            Current = new FlightControls(frame.Pitch, frame.Roll, frame.Yaw, _throttle.Position, frame.Brake);
            return Current;
        }

        public void ResetControls()
        {
            _throttle.Set(0f);
            for (int i = 0; i < _providers.Count; i++) _providers[i].ResetInput();
            Current = FlightControls.Neutral;
        }
    }
}
