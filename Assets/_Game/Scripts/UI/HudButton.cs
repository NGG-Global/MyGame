using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>What a tap on a button sounds (and, for confirmations, feels) like.</summary>
    public enum UiFeedback
    {
        None = 0,
        /// <summary>A short, quiet tick.</summary>
        Tap = 1,
        /// <summary>An important choice: a confirming sound and, where enabled, a light haptic pulse.</summary>
        Confirm = 2,
    }

    /// <summary>
    /// Tap button for HUD actions (action rail, SYSTEMS, HUD profile). It fires
    /// only for a pointer that was pressed on it and released on it, so a finger
    /// that started on a flight control (and is owned by it) can never trigger
    /// it by sliding across. Shows pressed, engaged (system on) and disabled
    /// states without flashing. A press shrinks it slightly at once (no animation,
    /// so feedback never delays the action), and every click reports its
    /// <see cref="UiFeedback"/> through <see cref="AnyClicked"/> for sound and haptics.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HudButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        const int NoPointer = int.MinValue;

        public Image background;
        public Text label;
        public Color idleColor = HudStyle.ControlSurface;
        public Color pressedColor = HudStyle.ControlPressed;
        public Color engagedColor = HudStyle.Selected;
        public Color labelColor = HudStyle.TextPrimary;
        public Color engagedLabelColor = HudStyle.TextOnAccent;
        [Range(0f, 1f)] public float disabledAlpha = 0.4f;
        [Tooltip("Corner radius of the background, canvas units.")]
        public float cornerRadius = HudStyle.ControlRadius;
        [Tooltip("Scale while pressed (1 = none).")]
        [Range(0.8f, 1f)] public float pressScale = HudStyle.PressScale;
        public UiFeedback feedback = UiFeedback.Tap;

        int _pressedPointer = NoPointer;
        bool _interactable = true;
        bool _engaged;

        public event Action Clicked;

        /// <summary>Any HUD button was clicked (sound and haptics listen here; never flight input).</summary>
        public static event Action<UiFeedback> AnyClicked;

        public bool IsPressed => _pressedPointer != NoPointer;
        public bool Interactable => _interactable;
        public bool Engaged => _engaged;

        void Awake()
        {
            if (background != null && background.sprite == null) HudStyle.Round(background, cornerRadius);
            Refresh();
        }

        void OnDisable()
        {
            _pressedPointer = NoPointer;
            Refresh();
        }

        public void SetInteractable(bool interactable)
        {
            if (interactable == _interactable) return;
            _interactable = interactable;
            if (!interactable) _pressedPointer = NoPointer;
            Refresh();
        }

        public void SetEngaged(bool engaged)
        {
            if (engaged == _engaged) return;
            _engaged = engaged;
            Refresh();
        }

        /// <summary>Colour used while engaged (e.g. red while a hazardous choice awaits confirmation).</summary>
        public void SetEngagedColor(Color color)
        {
            if (engagedColor == color) return;
            engagedColor = color;
            Refresh();
        }

        /// <summary>Changes the caption; no work when it is the same string.</summary>
        public void SetLabel(string text)
        {
            if (label != null && !ReferenceEquals(label.text, text)) label.text = text;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable || IsPressed) return;
            _pressedPointer = eventData.pointerId;
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pressedPointer) return;
            _pressedPointer = NoPointer;
            Refresh();
        }

        /// <summary>uGUI sends this only when the press and the release were both on this button.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_interactable) return;
            Clicked?.Invoke();
            if (feedback != UiFeedback.None) AnyClicked?.Invoke(feedback);
        }

        void Refresh()
        {
            float scale = IsPressed ? pressScale : 1f;
            if (transform.localScale.x != scale) transform.localScale = new Vector3(scale, scale, 1f);
            float alpha = _interactable ? 1f : disabledAlpha;
            if (background != null)
            {
                Color c = IsPressed ? pressedColor : (_engaged ? engagedColor : idleColor);
                c.a *= alpha;
                background.color = c;
            }
            if (label != null)
            {
                Color c = _engaged && !IsPressed ? engagedLabelColor : labelColor;
                c.a *= alpha;
                label.color = c;
            }
        }
    }
}
