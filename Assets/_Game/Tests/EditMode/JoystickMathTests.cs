using NUnit.Framework;
using OpenSkies.FlightInput.TouchControls;
using UnityEngine;

namespace OpenSkies.Tests
{
    public class JoystickMathTests
    {
        static JoystickSettings Linear(float deadZone = 0.1f, float sensitivity = 1f) =>
            new JoystickSettings { radius = 100f, deadZone = deadZone, sensitivity = sensitivity, responseExponent = 1f, smoothingTime = 0.04f };

        [Test]
        public void Deflection_IsClampedToUnitCircle()
        {
            Assert.AreEqual(new Vector2(0.5f, 0f), JoystickMath.Deflection(new Vector2(50f, 0f), 100f));
            Assert.AreEqual(1f, JoystickMath.Deflection(new Vector2(300f, 400f), 100f).magnitude, 1e-5f);
            Assert.AreEqual(Vector2.zero, JoystickMath.Deflection(new Vector2(10f, 10f), 0f));
        }

        [Test]
        public void Shape_DeadZoneGivesZeroThenRampsFromZero()
        {
            var s = Linear(deadZone: 0.1f);
            Assert.AreEqual(Vector2.zero, JoystickMath.Shape(new Vector2(0.09f, 0f), s));
            Assert.That(JoystickMath.Shape(new Vector2(0.11f, 0f), s).x, Is.InRange(0f, 0.05f), "no jump at the dead-zone edge");
            Assert.AreEqual(1f, JoystickMath.Shape(new Vector2(1f, 0f), s).x, 1e-5f);
            Assert.AreEqual(0.5f, JoystickMath.Shape(new Vector2(0.55f, 0f), s).x, 1e-4f);
        }

        [Test]
        public void Shape_ResponseCurveSoftensCentre()
        {
            var linear = Linear(deadZone: 0f);
            var curved = Linear(deadZone: 0f);
            curved.responseExponent = 2f;
            float l = JoystickMath.Shape(new Vector2(0.5f, 0f), linear).x;
            float c = JoystickMath.Shape(new Vector2(0.5f, 0f), curved).x;
            Assert.That(c, Is.LessThan(l));
            Assert.AreEqual(1f, JoystickMath.Shape(new Vector2(1f, 0f), curved).x, 1e-5f, "full deflection still reaches full output");
        }

        [Test]
        public void Shape_SensitivityScalesAndClamps()
        {
            var s = Linear(deadZone: 0f, sensitivity: 2f);
            Assert.AreEqual(0.8f, JoystickMath.Shape(new Vector2(0.4f, 0f), s).x, 1e-4f);
            Assert.AreEqual(1f, JoystickMath.Shape(new Vector2(0.9f, 0f), s).x, 1e-5f);
            Assert.AreEqual(-1f, JoystickMath.Shape(new Vector2(-0.9f, 0f), s).x, 1e-5f);
        }

        [Test]
        public void FlightAxes_DragDownPitchesUpAndRightRollsRight()
        {
            JoystickMath.ToFlightAxes(new Vector2(0f, -1f), out float pitch, out float roll);
            Assert.AreEqual(1f, pitch, "dragging down pulls the stick back: nose up");
            Assert.AreEqual(0f, roll);

            JoystickMath.ToFlightAxes(new Vector2(1f, 0f), out pitch, out roll);
            Assert.AreEqual(0f, pitch, 1e-6f);
            Assert.AreEqual(1f, roll);
        }

        [Test]
        public void Smooth_ConvergesWithoutOvershoot()
        {
            var target = new Vector2(1f, -1f);
            Assert.AreEqual(target, JoystickMath.Smooth(Vector2.zero, target, 0f, 0.02f), "no smoothing");
            Assert.AreEqual(Vector2.zero, JoystickMath.Smooth(Vector2.zero, target, 0.04f, 0f), "no time passed");
            var step = JoystickMath.Smooth(Vector2.zero, target, 0.04f, 0.02f);
            Assert.That(step.x, Is.InRange(0.3f, 0.5f));
            Assert.AreEqual(1f, JoystickMath.Smooth(Vector2.zero, target, 0.04f, 1f).x, 1e-4f);
        }

        [Test]
        public void ClampBase_KeepsJoystickInsideRegion()
        {
            var region = new Rect(0f, 0f, 800f, 600f);
            Assert.AreEqual(new Vector2(100f, 100f), JoystickMath.ClampBase(new Vector2(10f, -50f), region, 100f));
            Assert.AreEqual(new Vector2(700f, 500f), JoystickMath.ClampBase(new Vector2(900f, 900f), region, 100f));
            Assert.AreEqual(new Vector2(400f, 300f), JoystickMath.ClampBase(new Vector2(400f, 300f), region, 100f));
            Assert.AreEqual(new Vector2(50f, 300f), JoystickMath.ClampBase(new Vector2(0f, 300f), new Rect(0f, 0f, 100f, 600f), 100f),
                "region narrower than the joystick: centred");
        }

        [Test]
        public void ThrottleDrag_IsRelativeAndProportional()
        {
            Assert.AreEqual(0.5f, ThrottleDragMath.Delta(100f, 330f, 460f), 1e-5f);
            Assert.AreEqual(-0.25f, ThrottleDragMath.Delta(330f, 215f, 460f), 1e-5f);
            Assert.AreEqual(0.25f, ThrottleDragMath.Delta(100f, 330f, 460f, gain: 0.5f), 1e-5f, "lower gain = finer control");
            Assert.AreEqual(0f, ThrottleDragMath.Delta(0f, 100f, 0f));
        }
    }
}
