using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenSkies.UI
{
    /// <summary>
    /// Tap button for HUD actions (action rail, SYSTEMS, HUD profile). It fires
    /// only for a pointer that was pressed on it and released on it, so a finger
    /// that started on a flight control (and is owned by it) can never trigger
    /// it by sliding across. Shows pressed, engaged (system on) and disabled
    /// states without flashing.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HudButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        const int NoPointer = int.MinValue;

        public Image background;
        public Text label;
        public Color idleColor = new Color(0f, 0f, 0f, 0.35f);
        public Color pressedColor = new Color(1f, 1f, 1f, 0.4f);
        public Color engagedColor = new Color(0.96f, 0.74f, 0.22f, 0.85f);
        public Color labelColor = new Color(1f, 1f, 1f, 0.95f);
        public Color engagedLabelColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        [Range(0f, 1f)] public float disabledAlpha = 0.4f;

        int _pressedPointer = NoPointer;
        bool _interactable = true;
        bool _engaged;

        public event Action Clicked;

        public bool IsPressed => _pressedPointer != NoPointer;
        public bool Interactable => _interactable;
        public bool Engaged => _engaged;

        void Awake()
        {
            if (background != null && background.sprite == null)
            {
                background.sprite = HudSprites.RoundedRect;
                background.type = Image.Type.Sliced;
            }
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
            if (_interactable) Clicked?.Invoke();
        }

        void Refresh()
        {
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
