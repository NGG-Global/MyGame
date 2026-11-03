using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// The one flap lever interaction, whether the finger arrives from the
    /// systems arc (FLAPS segment) or from the cockpit's flap lever: the same
    /// model, the same view under the thumb, the same detents from the aircraft
    /// data, the same preview colours and ticks, and one commit on release through
    /// <see cref="FlightSession.RequestSystemChange"/>. Cancelling applies nothing.
    /// </summary>
    public sealed class FlapsLeverInteraction
    {
        readonly FlightSession _flight;
        readonly FlapsLeverView _view;
        readonly GestureHud _hud;
        readonly FlapsLeverModel _model;

        public FlapsLeverInteraction(FlightSession flight, FlapsLeverView view, GestureHud hud)
        {
            _flight = flight;
            _view = view;
            _hud = hud;
            _model = new FlapsLeverModel(view != null ? view.settings : null);
            if (_view != null && flight != null && flight.Systems != null)
            {
                int detents = flight.Systems.FlapDetentCount;
                var labels = new string[detents];
                for (int i = 0; i < detents; i++) labels[i] = i == 0 ? "UP" : Mathf.RoundToInt(flight.Systems.FlapDetentDegrees(i)) + "°";
                _view.SetDetentLabels(labels);
            }
        }

        public FlapsLeverModel Model => _model;
        public bool IsActive => _model.IsActive;
        public bool Changed => _model.Changed;
        public bool Cancelled => _model.Cancelled;
        /// <summary>The lever can be shown (the gesture HUD exists).</summary>
        public bool Available => _view != null && _hud != null;

        /// <summary>The lever appears with the aircraft's current detent under the finger.</summary>
        public void Begin(Vector2 screenPoint)
        {
            var systems = _flight != null ? _flight.Systems : null;
            if (systems == null) return;
            Vector2 local = _hud != null ? _hud.ToLocal(screenPoint) : screenPoint;
            _model.Begin(systems.FlapDetentCount, systems.FlapDetent, local);
            _view?.Show(_model, local, Bounds());
            HudButton.Report(UiFeedback.Confirm);
        }

        /// <summary>The finger moved: the preview follows, with a tick per detent and when the cancel state changes.</summary>
        public void Move(Vector2 screenPoint)
        {
            if (!_model.IsActive) return;
            int before = _model.Preview;
            bool cancelledBefore = _model.Cancelled;
            _model.Update(_hud != null ? _hud.ToLocal(screenPoint) : screenPoint);
            if (_model.Preview != before && !_model.Cancelled) HudButton.Report(UiFeedback.Tick);
            if (_model.Cancelled != cancelledBefore) HudButton.Report(UiFeedback.Tick);
        }

        /// <summary>The committed detent changed under the lever (keyboard, cockpit): keep the preview honest. Call per frame.</summary>
        public void Sync()
        {
            if (!_model.IsActive || _flight == null || _flight.Systems == null) return;
            _model.SetCurrent(_flight.Systems.FlapDetent);
            _view?.Present(_model);
        }

        /// <summary>Released: the previewed detent is requested (one change, through the gate); unchanged or cancelled applies nothing.</summary>
        public SystemChangeGate.Outcome Commit()
        {
            var outcome = SystemChangeGate.Outcome.Unchanged;
            if (_model.IsActive && _model.Changed && _flight != null)
            {
                outcome = _flight.RequestSystemChange(new SystemChange(AircraftSystem.Flaps, _model.Preview));
                HudButton.Report(outcome == SystemChangeGate.Outcome.Applied ? UiFeedback.Confirm : UiFeedback.Tap);
            }
            End();
            return outcome;
        }

        /// <summary>Revoked or cancelled: nothing changes.</summary>
        public void Cancel() => End();

        void End()
        {
            if (!_model.IsActive) return;
            _model.End();
            _view?.Hide();
        }

        Rect Bounds()
        {
            if (_hud == null) return new Rect(0f, 0f, 1920f, 1080f);
            return _hud.FreeBounds(TouchControlsFactory.JoystickRegionRight, TouchControlsFactory.ThrottleAreaSize.x - TouchControlsFactory.ThrottleOffset.x,
                ContextualHudFactory.RailBase.y, ContextualHudFactory.TopMargin + HudStyle.MinTouchTarget + HudStyle.Gap);
        }
    }
}
