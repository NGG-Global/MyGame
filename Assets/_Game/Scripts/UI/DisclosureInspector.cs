using System.Text;
using OpenSkies.Awareness;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Development-only view of the disclosure system: detected phase and
    /// confidence, the challenging phase, HUD profile and view, what is visible
    /// and why, active warnings and the latest phase changes with their reasons.
    /// Hidden in release builds. Tap it (or press F3) to collapse it to one line.
    /// Refreshes a few times per second; small allocations are acceptable here
    /// because it never ships.
    /// </summary>
    public sealed class DisclosureInspector : MonoBehaviour, IPointerClickHandler
    {
        public Text text;
        [Tooltip("Show in Editor and development builds. Never shown in release builds.")]
        public bool show = true;
        public bool collapsed;
        public float refreshSeconds = 0.25f;
        [Tooltip("How many recent phase changes to list.")]
        [Range(0, FlightPhaseEstimator.HistoryCapacity)] public int historyLines = 4;

        readonly StringBuilder _builder = new StringBuilder(1024);
        float _nextRefresh;

        public bool Visible => DisclosurePolicy.ShowDevelopmentOverlay(show, Debug.isDebugBuild);

        public void Toggle()
        {
            collapsed = !collapsed;
            _nextRefresh = 0f;
        }

        public void OnPointerClick(PointerEventData eventData) => Toggle();

        public void Present(FlightPhaseEstimator phase, FlightWarningMonitor warnings, DisclosureController disclosure,
            HudProfile profile, ViewMode view)
        {
            if (text == null) return;
            bool visible = Visible;
            if (text.enabled != visible) text.enabled = visible;
            if (!visible || phase == null || disclosure == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshSeconds;

            var p = phase.Current;
            var b = _builder.Clear();
            b.Append("PHASE ").Append(FlightPhases.Label(p.Phase))
                .Append("  conf ").Append(p.Confidence.ToString("0.00"))
                .Append("  ").Append(p.TimeInPhase.ToString("0")).Append(" s");
            if (collapsed)
            {
                text.text = b.Append("   [tap]").ToString();
                return;
            }

            b.Append("\n  why  ").Append(FlightPhases.Describe(p.Reason));
            if (p.HasCandidate)
                b.Append("\n  next ").Append(FlightPhases.Label(p.Candidate))
                    .Append(' ').Append(Mathf.RoundToInt(p.CandidateProgress * 100f)).Append("%  score ")
                    .Append(phase.Score(p.Candidate).ToString("0.00"));
            b.Append("\n  gnd ").Append(p.OnGround ? "yes" : "no")
                .Append("  vs~ ").Append(Mathf.RoundToInt(phase.SmoothedVerticalSpeed))
                .Append("  acc~ ").Append(phase.SmoothedAcceleration.ToString("0.0"));

            b.Append("\nHUD ").Append(profile.ToString().ToUpperInvariant())
                .Append("  ").Append(view == ViewMode.Cockpit ? "cockpit" : "external");
            if (disclosure.IsStableFlight) b.Append("  stable");
            if (disclosure.RailExpanded) b.Append("  systems open");

            b.Append("\nSHOW");
            int shown = 0;
            for (int i = 0; i < HudElements.Count; i++)
            {
                var e = (HudElement)i;
                var prominence = disclosure.GetProminence(e);
                if (prominence == Prominence.Hidden) continue;
                b.Append(' ').Append(HudElements.ShortLabel(e));
                if (prominence == Prominence.Quiet) b.Append("(q)");
                b.Append('[').Append(DisclosureReasons.Describe(disclosure.GetReason(e)));
                if (disclosure.IsHeld(e)) b.Append(",held");
                b.Append(']');
                shown++;
            }
            if (shown == 0) b.Append(" none");

            b.Append("\nACT ");
            int actions = 0;
            for (int i = 0; i < ContextActions.Count; i++)
            {
                var a = (ContextAction)i;
                if (!disclosure.IsActionVisible(a)) continue;
                b.Append(' ').Append(ContextActions.ShortLabel(a));
                if (disclosure.IsActionEngaged(a)) b.Append("(on)");
                if (!disclosure.IsActionEnabled(a)) b.Append("(off)");
                b.Append('[').Append(DisclosureReasons.Describe(disclosure.GetActionReason(a))).Append(']');
                actions++;
            }
            if (actions == 0) b.Append(" none");

            b.Append("\nWARN");
            if (warnings == null || warnings.ActiveCount == 0) b.Append(" none");
            else
                for (int i = 0; i < warnings.ActiveCount; i++)
                    b.Append(' ').Append(FlightWarnings.Label(warnings.ActiveAt(i)));

            int lines = Mathf.Min(historyLines, phase.TransitionCount);
            for (int i = 0; i < lines; i++)
            {
                var t = phase.GetTransition(i);
                b.Append(i == 0 ? "\nLOG " : "\n    ")
                    .Append(t.Time.ToString("0.0")).Append("s ")
                    .Append(FlightPhases.Label(t.From)).Append(" > ").Append(FlightPhases.Label(t.To))
                    .Append(": ").Append(FlightPhases.Describe(t.Reason));
            }
            text.text = b.ToString();
        }
    }
}
