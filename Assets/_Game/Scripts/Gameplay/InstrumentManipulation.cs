using System;
using OpenSkies.Awareness;
using OpenSkies.Cockpit;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
using OpenSkies.Missions;
using OpenSkies.UI;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Direct manipulation of the enlarged instrument in the cockpit focus panel,
    /// with the same vocabulary as the HUD gestures: drag around the heading
    /// indicator to set the heading bug, drag around the altimeter to set its
    /// subscale, drag the map to pan and pinch it to zoom, double tap the map to
    /// recentre, hold any instrument to explain it, tap it to close. The preview is
    /// drawn on the face in cyan; the setting changes only on release, through
    /// <see cref="FlightSession.RequestInstrumentSetting"/>. Moving into the dial's
    /// centre cancels. The flight is never touched.
    /// </summary>
    public sealed class InstrumentManipulation : IDisposable
    {
        public enum Doing
        {
            Nothing = 0,
            HeadingBug,
            Baro,
            MapPan,
            Explain,
        }

        const string BugNote = "Drag around the dial to set the bug · release to set · centre to cancel";
        const string BaroNote = "Drag around the dial to set the subscale · release to set · centre to cancel";
        const string MapNote = "Drag to pan · pinch to zoom · double tap to recentre";
        const string CancelNote = "Release to cancel";
        const string ReleaseNote = "Release to set";

        readonly FlightSession _flight;
        readonly InteractionSurface _surface;
        readonly CockpitSession _cockpit;
        readonly CockpitFocusPanel _panel;
        readonly InstrumentSettings _instruments;
        readonly GestureHud _hud;
        readonly HudExplanations _explanations;
        readonly RotaryModel _rotary;
        readonly MapViewModel _map;
        Doing _doing;
        Vector2 _lastFacePoint;
        bool _reducedMotion;
        GestureHints _hints;

        public void SetHints(GestureHints hints) => _hints = hints;

        public InstrumentManipulation(FlightSession flight, InteractionSurface surface, CockpitSession cockpit, CockpitFocusPanel panel,
            InstrumentSettings instruments, GestureHud hud, HudExplanations explanations)
        {
            _flight = flight ?? throw new ArgumentNullException(nameof(flight));
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _cockpit = cockpit;
            _panel = panel;
            _instruments = instruments;
            _hud = hud;
            _explanations = explanations;
            _rotary = new RotaryModel(panel != null ? panel.rotary : null);
            _map = new MapViewModel(panel != null ? panel.mapView : null);
            if (panel != null)
            {
                var face = panel.faces[(int)InstrumentKind.Navigation];
                if (face != null) _map.SetRadius(face.mapRadius);
            }
            _surface.GestureRaised += OnGesture;
        }

        public void Dispose() => _surface.GestureRaised -= OnGesture;

        public Doing Current => _doing;
        public RotaryModel Rotary => _rotary;
        public MapViewModel Map => _map;
        public void SetReducedMotion(bool reduced) => _reducedMotion = reduced;

        /// <summary>Per frame, after the cockpit session has drawn the committed values: the preview goes over them.</summary>
        public void Update()
        {
            if (_panel == null) return;
            if (!_panel.IsShowingInstrument(out var kind))
            {
                if (_doing != Doing.Nothing) EndGesture();
                return;
            }
            var face = _panel.faces[(int)kind];
            if (face == null) return;
            switch (_doing)
            {
                case Doing.HeadingBug:
                    face.SetHeadingBug(Mathf.RoundToInt(_rotary.Preview), !_rotary.Cancelled);
                    break;
                case Doing.Baro:
                    face.SetBaro(Mathf.RoundToInt(_rotary.Preview), !_rotary.Cancelled);
                    break;
            }
            if (kind == InstrumentKind.Navigation) face.SetMapView(_map.Pan, _map.RangeNm);
        }

        /// <summary>A new flight or a closed panel: the map view returns to automatic, previews end.</summary>
        public void OnReset()
        {
            EndGesture();
            _map.Recenter();
        }

        void OnGesture(InteractionRoute route, GestureEvent e, int target)
        {
            if (route != InteractionRoute.Instrument || _panel == null) return;
            if (!_panel.IsShowingInstrument(out var kind)) return;
            switch (e.Kind)
            {
                case GestureKind.Drag: OnDrag(e, kind); break;
                case GestureKind.Pinch:
                    if (kind == InstrumentKind.Navigation && e.Type == GestureEventType.Changed)
                    {
                        var face = _panel.faces[(int)kind];
                        _map.Pinch(e.PinchDelta, face != null ? face.MapRangeNm : 5f);
                        _panel.SetManipulation(MapNote, true);
                    }
                    if (e.Type == GestureEventType.Completed || e.Type == GestureEventType.Cancelled) _panel.SetManipulation(null, false);
                    break;
                case GestureKind.DoubleTap:
                    if (kind == InstrumentKind.Navigation && e.Type == GestureEventType.Completed)
                    {
                        _map.Recenter();
                        HudButton.Report(UiFeedback.Tap);
                    }
                    break;
                case GestureKind.Tap:
                    if (e.Type == GestureEventType.Completed && kind != InstrumentKind.Navigation) _cockpit?.Close(); // the map's tap recentres on a double; a single tap is nothing
                    break;
                case GestureKind.LongPress: OnLongPress(e, kind); break;
            }
        }

        void OnDrag(in GestureEvent e, InstrumentKind kind)
        {
            switch (e.Type)
            {
                case GestureEventType.Began:
                    BeginDrag(e, kind);
                    break;
                case GestureEventType.Changed:
                    MoveDrag(e);
                    break;
                case GestureEventType.Completed:
                    CommitDrag();
                    break;
                case GestureEventType.Cancelled:
                    EndGesture();
                    break;
            }
        }

        void BeginDrag(in GestureEvent e, InstrumentKind kind)
        {
            EndGesture();
            if (!_panel.TryGetFacePoint(e.Origin, out var origin)) return;
            _lastFacePoint = origin;
            switch (kind)
            {
                case InstrumentKind.Heading:
                    if (_instruments == null) return;
                    _doing = Doing.HeadingBug;
                    _rotary.Begin(_instruments.HeadingBugDegrees, 1f, 360f, wrap: true, origin);
                    _panel.SetManipulation(BugNote, true);
                    break;
                case InstrumentKind.Altimeter:
                    if (_instruments == null) return;
                    _doing = Doing.Baro;
                    _rotary.Begin(_instruments.BaroHectopascals, InstrumentSettings.MinBaroHectopascals, InstrumentSettings.MaxBaroHectopascals, wrap: false, origin);
                    _panel.SetManipulation(BaroNote, true);
                    break;
                case InstrumentKind.Navigation:
                    _doing = Doing.MapPan;
                    _panel.SetManipulation(MapNote, true);
                    break;
                default:
                    return; // airspeed, attitude and vertical speed have nothing to set
            }
            MoveDrag(e);
        }

        void MoveDrag(in GestureEvent e)
        {
            if (_doing == Doing.Nothing || !_panel.TryGetFacePoint(e.Position, out var point)) return;
            if (_doing == Doing.MapPan)
            {
                _map.Drag(point - _lastFacePoint);
                _lastFacePoint = point;
                return;
            }
            bool cancelledBefore = _rotary.Cancelled;
            _rotary.Update(point);
            int ticks = _rotary.TakeTicks();
            if (ticks > 0 && !_rotary.Cancelled) HudButton.Report(UiFeedback.Tick);
            if (_rotary.Cancelled != cancelledBefore) HudButton.Report(UiFeedback.Tick);
            _panel.SetManipulation(_rotary.Cancelled ? CancelNote : (_rotary.Changed ? ReleaseNote : (_doing == Doing.HeadingBug ? BugNote : BaroNote)), !_rotary.Cancelled);
        }

        void CommitDrag()
        {
            if ((_doing == Doing.HeadingBug || _doing == Doing.Baro) && _rotary.Changed)
            {
                var setting = _doing == Doing.HeadingBug ? InstrumentSetting.HeadingBug : InstrumentSetting.BaroHectopascals;
                bool applied = _flight.RequestInstrumentSetting(setting, Mathf.RoundToInt(_rotary.Preview));
                HudButton.Report(applied ? UiFeedback.Confirm : UiFeedback.Tap);
                _hints?.Learned(GestureId.RotaryAdjust, Time.unscaledTime);
                _hints?.Learned(GestureId.HoldInstrument, Time.unscaledTime);
            }
            EndGesture();
        }

        void OnLongPress(in GestureEvent e, InstrumentKind kind)
        {
            if (_hud == null || _hud.plate == null) return;
            if (e.Type == GestureEventType.Began)
            {
                EndGesture();
                _doing = Doing.Explain;
                string title, text;
                var element = ElementFor(kind);
                if (element >= 0 && _explanations != null && _explanations.TryGet(element, out title, out text)) { }
                else
                {
                    var item = (CockpitItem)(int)kind;
                    title = CockpitItems.DisplayName(item);
                    text = CockpitItems.AccessibilityHint(item);
                }
                var b = _hud.Bounds;
                var anchor = _panel.faceHost != null ? _hud.LocalRect(_panel.faceHost) : new Rect(_hud.ToLocal(e.Position) - new Vector2(40f, 40f), new Vector2(80f, 80f));
                _hud.plate.Show(title, text, anchor, Rect.MinMaxRect(b.xMin + HudStyle.ScreenMargin, b.yMin + HudStyle.ScreenMargin, b.xMax - HudStyle.ScreenMargin, b.yMax - HudStyle.ScreenMargin));
                HudButton.Report(UiFeedback.Tap);
                _hints?.Learned(GestureId.HoldInstrument, Time.unscaledTime);
            }
            else if ((e.Type == GestureEventType.Completed || e.Type == GestureEventType.Cancelled) && _doing == Doing.Explain) EndGesture();
        }

        /// <summary>The HUD element whose explanation an instrument shares, or -1.</summary>
        static int ElementFor(InstrumentKind kind)
        {
            switch (kind)
            {
                case InstrumentKind.Airspeed: return (int)HudElement.Airspeed;
                case InstrumentKind.Altimeter: return (int)HudElement.Altitude;
                case InstrumentKind.VerticalSpeed: return (int)HudElement.VerticalSpeed;
                case InstrumentKind.Heading: return (int)HudElement.Heading;
                case InstrumentKind.Navigation: return (int)HudElement.NavigationGuide;
                default: return -1;
            }
        }

        void EndGesture()
        {
            if (_doing == Doing.Explain && _hud != null && _hud.plate != null) _hud.plate.Hide();
            if (_doing == Doing.HeadingBug || _doing == Doing.Baro)
            {
                _rotary.End();
                // The committed value comes back at once; the cockpit session draws it next frame.
                if (_panel != null && _panel.IsShowingInstrument(out var kind) && _instruments != null)
                {
                    var face = _panel.faces[(int)kind];
                    if (face != null && kind == InstrumentKind.Heading) face.SetHeadingBug(_instruments.HeadingBugDegrees, false);
                    if (face != null && kind == InstrumentKind.Altimeter) face.SetBaro(_instruments.BaroHectopascals, false);
                }
            }
            if (_panel != null && _doing != Doing.Nothing) _panel.SetManipulation(null, false);
            _doing = Doing.Nothing;
        }
    }
}
