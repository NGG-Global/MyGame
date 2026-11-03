using System.Text;
using OpenSkies.FlightState;
using OpenSkies.Gestures;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Development-only view of the gesture system: pointer ownership, the active
    /// route and recognizer state, progress, the target under the finger, the armed
    /// action, the last cancellation and the HUD profile with its preview. Hidden in
    /// release builds. Tap it (or press F4) to switch between the gesture-first and
    /// the legacy interface. Refreshes a few times per second.
    /// </summary>
    public sealed class GestureInspector : MonoBehaviour, IPointerClickHandler
    {
        public Text text;
        [Tooltip("Show in Editor and development builds. Never shown in release builds.")]
        public bool show = true;
        public float refreshSeconds = 0.2f;

        readonly StringBuilder _builder = new StringBuilder(768);
        float _nextRefresh;

        /// <summary>The player tapped the inspector: switch interface mode.</summary>
        public event System.Action ModeToggleRequested;

        /// <summary>What the session knows that the inspector shows.</summary>
        public struct Report
        {
            public bool GestureFirst;
            /// <summary>Pointer ownership, described by the session ("#0>Joystick  #1>Gestures").</summary>
            public string Ownership;
            public string Route;
            public string Armed;
            public string Target;
            public string Mode;       // what the session is doing with the gesture (arc, lever, density, look)
            public string HudProfile;
            public string PreviewProfile;
            /// <summary>The interaction diary's one-line summary (development counts), or null.</summary>
            public string Diary;
        }

        public bool Visible => DisclosurePolicy.ShowDevelopmentOverlay(show, Debug.isDebugBuild);

        public void OnPointerClick(PointerEventData eventData) => ModeToggleRequested?.Invoke();

        public void Present(InteractionSurface surface, in Report report)
        {
            if (text == null) return;
            bool visible = Visible;
            if (text.enabled != visible) text.enabled = visible;
            if (!visible || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshSeconds;

            var b = _builder.Clear();
            b.Append("UI ").Append(report.GestureFirst ? "GESTURE-FIRST" : "LEGACY").Append("   [tap: switch]");
            b.Append("\nOWN ").Append(string.IsNullOrEmpty(report.Ownership) ? "none" : report.Ownership);

            if (surface != null)
            {
                b.Append("\nGESTURE ");
                bool any = false;
                for (int r = 0; r < InteractionRouter.RouteCount; r++)
                {
                    var route = (InteractionRoute)r;
                    var channel = surface.Channel(route);
                    if (channel == null || !channel.IsTracking) continue;
                    any = true;
                    b.Append(route).Append(':').Append(channel.Kind).Append('/').Append(channel.State)
                        .Append(' ').Append(Mathf.RoundToInt(channel.Progress * 100f)).Append('%');
                    if (channel.IsDwelling) b.Append(" dwell");
                    if (channel.HasSecondPointer) b.Append(" 2f");
                    b.Append("  ");
                }
                if (!any) b.Append("idle");
                b.Append("  last cancel: ");
                CancelReason last = CancelReason.None;
                for (int r = 0; r < InteractionRouter.RouteCount; r++)
                {
                    var channel = surface.Channel((InteractionRoute)r);
                    if (channel != null && channel.LastCancel != CancelReason.None) last = channel.LastCancel;
                }
                b.Append(last);
            }
            b.Append("\nDO ").Append(report.Mode ?? "-").Append("   target ").Append(report.Target ?? "-")
                .Append("   armed ").Append(report.Armed ?? "-");
            b.Append("\nHUD ").Append(report.HudProfile ?? "-");
            if (!string.IsNullOrEmpty(report.PreviewProfile)) b.Append("  preview ").Append(report.PreviewProfile);
            if (!string.IsNullOrEmpty(report.Diary)) b.Append('\n').Append(report.Diary);
            text.text = b.ToString();
        }
    }
}
