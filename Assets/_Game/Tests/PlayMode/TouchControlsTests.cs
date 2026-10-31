using System.Collections;
using NUnit.Framework;
using OpenSkies.FlightInput;
using OpenSkies.FlightInput.TouchControls;
using OpenSkies.FlightState;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;

namespace OpenSkies.Tests
{
    /// <summary>
    /// Drives the real touch widgets with synthetic pointer events. This proves
    /// the ownership and routing logic; it does NOT prove that a physical device
    /// delivers multitouch events correctly (see docs/TOUCH_CONTROLS.md).
    /// </summary>
    public class TouchControlsTests
    {
        GameObject _root;
        TouchFlightControls _controls;
        FlightInputRouter _router;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("TouchTestRoot");
            new GameObject("EventSystem", typeof(EventSystem)).transform.SetParent(_root.transform);

            // A world-space canvas of exactly 1920×1080 units at the origin: with no
            // camera, screen coordinates equal world coordinates, independent of the
            // test machine's screen size.
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(1920f, 1080f);
            canvasRect.pivot = Vector2.zero;
            canvasRect.position = Vector3.zero;

            var safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safeArea.SetParent(canvasRect, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;

            _controls = TouchControlsFactory.Build(safeArea, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _controls.visibility = TouchControlsVisibility.Always;
            _router = new FlightInputRouter();
            _router.AddProvider(_controls);

            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        static Vector2 ScreenPoint(RectTransform rect, float x, float y)
        {
            Rect r = rect.rect;
            var local = new Vector2(r.xMin + r.width * x, r.yMin + r.height * y);
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));
        }

        static PointerEventData Pointer(int id, Vector2 position) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = position, pressPosition = position };

        static void Drag(TouchControlBase control, PointerEventData pointer, Vector2 offset)
        {
            pointer.position += offset;
            control.OnDrag(pointer);
        }

        RectTransform JoystickRect => (RectTransform)_controls.joystick.transform;
        RectTransform ThrottleRect => (RectTransform)_controls.throttle.transform;

        [Test]
        public void JoystickAndThrottle_WorkSimultaneously()
        {
            var stick = Pointer(1, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            var lever = Pointer(2, ScreenPoint(ThrottleRect, 0.5f, 0.3f));

            _controls.joystick.OnPointerDown(stick);
            _controls.throttle.OnPointerDown(lever);
            Drag(_controls.joystick, stick, new Vector2(0f, -200f)); // pull back: nose up
            Drag(_controls.throttle, lever, new Vector2(0f, 230f));  // half the 460-unit track

            var c = _router.Read(1f); // long step: smoothing fully settled
            Assert.That(c.Pitch, Is.GreaterThan(0.9f), "dragging down pitches up");
            Assert.AreEqual(0f, c.Roll, 0.02f);
            Assert.AreEqual(0.5f, c.Throttle, 0.01f);
            Assert.IsTrue(_controls.joystick.IsEngaged);
            Assert.IsTrue(_controls.throttle.IsEngaged);
        }

        [Test]
        public void ReleasingJoystick_ReturnsToNeutralAndKeepsThrottle()
        {
            var stick = Pointer(1, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            var lever = Pointer(2, ScreenPoint(ThrottleRect, 0.5f, 0.3f));
            _controls.throttle.OnPointerDown(lever);
            Drag(_controls.throttle, lever, new Vector2(0f, 299f)); // 65 %
            _controls.throttle.OnPointerUp(lever);
            _controls.joystick.OnPointerDown(stick);
            Drag(_controls.joystick, stick, new Vector2(150f, 0f));
            Assert.That(_router.Read(1f).Roll, Is.GreaterThan(0.9f));

            _controls.joystick.OnPointerUp(stick);
            var c = _router.Read(1f);

            Assert.AreEqual(0f, c.Pitch, 1e-3f);
            Assert.AreEqual(0f, c.Roll, 1e-3f, "stick returns to neutral");
            Assert.AreEqual(0.65f, c.Throttle, 0.01f, "throttle untouched by joystick release");
            Assert.AreEqual(0.65f, _router.Read(1f).Throttle, 0.01f, "and stays there");
        }

        [Test]
        public void TouchStartingOnJoystick_IsNeverReassigned()
        {
            var finger = Pointer(7, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            _controls.joystick.OnPointerDown(finger);

            // The same finger slides across the screen onto the throttle area.
            finger.position = ScreenPoint(ThrottleRect, 0.5f, 0.5f);
            _controls.joystick.OnDrag(finger);
            _controls.throttle.OnPointerDown(finger); // even if something dispatched it there
            _controls.throttle.OnDrag(finger);

            Assert.AreSame(_controls.joystick, _controls.Ownership.OwnerOf(7));
            Assert.IsFalse(_controls.throttle.IsEngaged);
            var c = _router.Read(1f);
            Assert.That(c.Roll, Is.GreaterThan(0.9f), "still steering: movement outside the region keeps control");
            Assert.AreEqual(0f, c.Throttle, "throttle never moved");
        }

        [Test]
        public void FocusLoss_NeutralisesControlsButKeepsThrottle()
        {
            var lever = Pointer(2, ScreenPoint(ThrottleRect, 0.5f, 0.3f));
            _controls.throttle.OnPointerDown(lever);
            Drag(_controls.throttle, lever, new Vector2(0f, 184f)); // 40 %
            var stick = Pointer(1, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            _controls.joystick.OnPointerDown(stick);
            Drag(_controls.joystick, stick, new Vector2(0f, 150f));
            var brakeFinger = Pointer(3, ScreenPoint((RectTransform)_controls.brake.transform, 0.5f, 0.5f));
            _controls.brake.OnPointerDown(brakeFinger);
            Assert.AreEqual(0.4f, _router.Read(1f).Throttle, 0.01f);

            _controls.SendMessage("OnApplicationFocus", false);

            Assert.AreEqual(0, _controls.Ownership.Count);
            Assert.IsFalse(_controls.joystick.IsEngaged);
            Assert.IsFalse(_controls.brake.IsHeld);
            var c = _router.Read(1f);
            Assert.AreEqual(0f, c.Pitch, 1e-3f);
            Assert.AreEqual(0f, c.Brake);
            Assert.AreEqual(0.4f, c.Throttle, 0.01f);

            // The late release from the lost finger is ignored; a new touch works.
            _controls.joystick.OnPointerUp(stick);
            var again = Pointer(4, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            _controls.joystick.OnPointerDown(again);
            Assert.IsTrue(_controls.joystick.IsEngaged);
        }

        [Test]
        public void Brake_IsHoldToBrakeAndReleasesWhenUnavailable()
        {
            var finger = Pointer(3, ScreenPoint((RectTransform)_controls.brake.transform, 0.5f, 0.5f));
            _controls.brake.OnPointerDown(finger);
            Assert.AreEqual(1f, _router.Read(0.02f).Brake);

            _controls.SetOnGround(false); // lifted off while holding

            Assert.IsFalse(_controls.brake.IsHeld);
            Assert.AreEqual(0f, _router.Read(0.02f).Brake);
            _controls.brake.OnPointerDown(Pointer(5, finger.position));
            Assert.IsFalse(_controls.brake.IsHeld, "not available in the air");

            _controls.SetOnGround(true);
            _controls.brake.OnPointerDown(Pointer(6, finger.position));
            Assert.IsTrue(_controls.brake.IsHeld);
            _controls.brake.OnPointerUp(Pointer(6, finger.position));
            Assert.IsFalse(_controls.brake.IsHeld);
        }

        [Test]
        public void DisablingControlMidGesture_ReleasesPointer()
        {
            var stick = Pointer(1, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            _controls.joystick.OnPointerDown(stick);
            Assert.IsTrue(_controls.Ownership.IsOwned(1));

            _controls.joystick.gameObject.SetActive(false);

            Assert.IsFalse(_controls.Ownership.IsOwned(1));
            Assert.AreEqual(Vector2.zero, _controls.joystick.Deflection);
        }

        [Test]
        public void SecondFingerOnEngagedJoystick_IsIgnored()
        {
            var first = Pointer(1, ScreenPoint(JoystickRect, 0.4f, 0.5f));
            _controls.joystick.OnPointerDown(first);
            Drag(_controls.joystick, first, new Vector2(100f, 0f));

            var second = Pointer(2, ScreenPoint(JoystickRect, 0.2f, 0.2f));
            _controls.joystick.OnPointerDown(second);
            Drag(_controls.joystick, second, new Vector2(-300f, -300f));

            Assert.IsFalse(_controls.Ownership.IsOwned(2), "one finger per control");
            Assert.That(_router.Read(1f).Roll, Is.GreaterThan(0.5f), "still following the first finger");
        }

        [UnityTest]
        public IEnumerator TouchWhoseReleaseNeverArrives_IsRevokedNextFrame()
        {
            // A real touch pointer (as InputSystemUIInputModule sends it) whose finger is not
            // down on any touchscreen: the per-frame liveness check must free it.
            var position = ScreenPoint(JoystickRect, 0.4f, 0.5f);
            var ghost = new ExtendedPointerEventData(EventSystem.current)
            {
                pointerId = 4242,
                pointerType = UIPointerType.Touch,
                touchId = 77,
                position = position,
                pressPosition = position,
            };
            _controls.joystick.OnPointerDown(ghost);
            ghost.position += new Vector2(0f, -150f);
            _controls.joystick.OnDrag(ghost);
            Assert.IsTrue(_controls.joystick.IsEngaged);

            yield return null; // LateUpdate runs the liveness check

            Assert.IsFalse(_controls.joystick.IsEngaged);
            Assert.IsFalse(_controls.Ownership.IsOwned(4242));
            Assert.AreEqual(Vector2.zero, _controls.joystick.Deflection);
        }

        [UnityTest]
        public IEnumerator RegionResize_KeepsJoystickBaseInside()
        {
            var stick = Pointer(1, ScreenPoint(JoystickRect, 0.95f, 0.95f));
            _controls.joystick.OnPointerDown(stick);

            // Simulate a safe-area/orientation change shrinking the region.
            JoystickRect.anchorMax = new Vector2(0.25f, 0.5f);
            yield return null;
            Canvas.ForceUpdateCanvases();

            Rect region = JoystickRect.rect;
            Vector2 basePos = _controls.joystick.visualRoot.anchoredPosition;
            float radius = _controls.joystick.settings.radius;
            Assert.That(basePos.x, Is.InRange(region.xMin + radius - 0.01f, region.xMax - radius + 0.01f));
            Assert.That(basePos.y, Is.InRange(region.yMin + radius - 0.01f, region.yMax - radius + 0.01f));
            Assert.IsTrue(_controls.joystick.IsEngaged, "the gesture survives the layout change");
        }
    }
}
