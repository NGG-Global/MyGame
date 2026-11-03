using System.Text;
using OpenSkies.Gestures;
using UnityEngine;

namespace OpenSkies.Gameplay
{
    /// <summary>
    /// Development-only counts for comparing interaction models on a device: how
    /// many gestures completed per route, how many were cancelled and why, and the
    /// average time from a pull's start to its commit. Kept in memory and shown in
    /// the gesture inspector; optionally appended to a local CSV in the app's own
    /// data folder, one line per completed or cancelled gesture, with no device or
    /// personal data (route, kind, outcome, duration, session seconds). Nothing
    /// leaves the device; there is no analytics service.
    /// </summary>
    public sealed class InteractionDiary
    {
        const int Routes = InteractionRouter.RouteCount;
        const int Reasons = 6;

        readonly int[] _completed = new int[Routes];
        readonly int[] _cancelled = new int[Routes];
        readonly int[] _cancelReasons = new int[Reasons];
        readonly float[] _began = new float[Routes];
        readonly StringBuilder _summary = new StringBuilder(256);
        float _pullTotal;
        int _pullCount, _quickCancels;
        StringBuilder _csv;
        float _sessionStart = -1f;

        /// <summary>Record lines to a CSV (development builds only); the path is the app's persistent data folder.</summary>
        public bool RecordToFile { get; set; }
        public int Completed(InteractionRoute route) => _completed[(int)route];
        public int Cancelled(InteractionRoute route) => _cancelled[(int)route];
        public int CancelledFor(CancelReason reason) => _cancelReasons[(int)reason];
        /// <summary>Cancellations within 0.3 s of a gesture starting: the likeliest accidental activations.</summary>
        public int QuickCancels => _quickCancels;
        public float AveragePullSeconds => _pullCount > 0 ? _pullTotal / _pullCount : 0f;
        public int TotalCompleted
        {
            get { int n = 0; for (int i = 0; i < Routes; i++) n += _completed[i]; return n; }
        }

        public void Record(InteractionRoute route, in GestureEvent e)
        {
            int r = (int)route;
            if (r < 0 || r >= Routes) return;
            if (_sessionStart < 0f) _sessionStart = e.Time;
            switch (e.Type)
            {
                case GestureEventType.Began:
                    _began[r] = e.Time;
                    break;
                case GestureEventType.Completed:
                    _completed[r]++;
                    if (e.Kind == GestureKind.Pull) { _pullTotal += e.Time - _began[r]; _pullCount++; }
                    Line(route, e, "completed");
                    break;
                case GestureEventType.Cancelled:
                    _cancelled[r]++;
                    int reason = (int)e.Reason;
                    if (reason >= 0 && reason < Reasons) _cancelReasons[reason]++;
                    if (e.Kind != GestureKind.None && e.Time - _began[r] < 0.3f) _quickCancels++;
                    Line(route, e, "cancelled");
                    break;
            }
        }

        void Line(InteractionRoute route, in GestureEvent e, string outcome)
        {
            if (!RecordToFile || !Debug.isDebugBuild) return;
            if (_csv == null) _csv = new StringBuilder(2048);
            _csv.Append((e.Time - _sessionStart).ToString("0.00")).Append(',').Append(route).Append(',').Append(e.Kind).Append(',')
                .Append(outcome).Append(',').Append(e.Reason).Append(',').Append((e.Time - _began[(int)route]).ToString("0.00")).Append('\n');
            if (_csv.Length > 1500) Flush();
        }

        /// <summary>Writes the buffered lines (called on pause, focus loss and quit by the session).</summary>
        public void Flush()
        {
            if (_csv == null || _csv.Length == 0) return;
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Application.persistentDataPath, "interaction-diary.csv"), _csv.ToString());
            }
            catch (System.Exception)
            {
                // A full disk or a read-only folder never breaks the game; the diary is a development aid.
            }
            _csv.Clear();
        }

        /// <summary>One line for the inspector (development only; allocates).</summary>
        public string Summary()
        {
            _summary.Clear().Append("DIARY done ").Append(TotalCompleted)
                .Append("  arc ").Append(_completed[(int)InteractionRoute.SystemsHandle]).Append('/').Append(_cancelled[(int)InteractionRoute.SystemsHandle])
                .Append("  hud ").Append(_completed[(int)InteractionRoute.HudBand]).Append('/').Append(_cancelled[(int)InteractionRoute.HudBand])
                .Append("  instr ").Append(_completed[(int)InteractionRoute.Instrument]).Append('/').Append(_cancelled[(int)InteractionRoute.Instrument])
                .Append("  quick cancels ").Append(_quickCancels)
                .Append("  pull avg ").Append(AveragePullSeconds.ToString("0.00")).Append(" s");
            return _summary.ToString();
        }
    }
}
