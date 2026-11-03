using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenSkies.Gestures
{
    /// <summary>
    /// A touch area with a named route (the systems handle, the HUD band). It is
    /// drawn above the elements it covers, so uGUI gives it the press, and it hands
    /// the pointer to the <see cref="InteractionSurface"/> with its route: the
    /// surface, not draw order, decides what happens. It draws nothing itself; a
    /// visual (the handle grip) can sit under it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GestureHotspot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public InteractionSurface surface;
        public InteractionRoute route = InteractionRoute.SystemsHandle;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (surface != null) surface.BeginFromHotspot(route, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (surface != null) surface.OnDrag(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (surface != null) surface.OnPointerUp(eventData);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;
    }
}
