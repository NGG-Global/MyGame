using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OpenSkies.FlightInput.TouchControls
{
    /// <summary>
    /// Shared pointer handling for touch controls. A control claims a pointer on
    /// press and keeps it until release or revocation: uGUI keeps routing drag
    /// and release events to the control that received the press, and the
    /// shared <see cref="PointerOwnership"/> table tells everything else to
    /// ignore that pointer.
    /// </summary>
    public abstract class TouchControlBase : MonoBehaviour, IPointerOwner,
        IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public const int NoPointer = int.MinValue;

        protected PointerOwnership Ownership { get; private set; }
        protected int ActivePointer { get; private set; } = NoPointer;
        public bool IsEngaged => ActivePointer != NoPointer;

        public abstract string OwnerName { get; }

        public void Initialize(PointerOwnership ownership) => Ownership = ownership;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Ownership == null || IsEngaged || !CanEngage()) return; // one finger per control
            Describe(eventData, out var kind, out int touchId);
            if (!Ownership.TryClaim(eventData.pointerId, this, kind, touchId)) return;
            ActivePointer = eventData.pointerId;
            OnEngage(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == ActivePointer) OnMove(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != ActivePointer) return;
            Ownership.Release(ActivePointer, this);
            Disengage();
        }

        /// <summary>React immediately to finger movement: no drag threshold.</summary>
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerRevoked(int pointerId)
        {
            if (pointerId == ActivePointer) Disengage();
        }

        /// <summary>Hidden or disabled mid-gesture: give the pointer back and go neutral.</summary>
        protected virtual void OnDisable() => ForceRelease();

        public void ForceRelease()
        {
            if (!IsEngaged) return;
            if (Ownership != null) Ownership.Release(ActivePointer, this);
            Disengage();
        }

        void Disengage()
        {
            ActivePointer = NoPointer;
            OnRelease();
        }

        protected virtual bool CanEngage() => isActiveAndEnabled;
        protected abstract void OnEngage(PointerEventData eventData);
        protected abstract void OnMove(PointerEventData eventData);
        protected abstract void OnRelease();

        static void Describe(PointerEventData eventData, out PointerKind kind, out int touchId)
        {
            kind = PointerKind.Unknown;
            touchId = 0;
            if (eventData is ExtendedPointerEventData extended)
            {
                if (extended.pointerType == UIPointerType.Touch)
                {
                    kind = PointerKind.Touch;
                    touchId = extended.touchId;
                }
                else if (extended.pointerType == UIPointerType.MouseOrPen)
                {
                    kind = PointerKind.MouseOrPen;
                }
            }
        }

        protected static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            var c = graphic.color;
            c.a = Mathf.Clamp01(alpha);
            graphic.color = c;
        }

        /// <summary>Screen point to this control's local space (overlay canvas: no camera).</summary>
        protected static bool ToLocal(RectTransform rect, PointerEventData eventData, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out local);
    }
}
