using UnityEngine;

namespace OpenSkies.Cockpit
{
    /// <summary>Where a cockpit item sits, as a direction from the pilot's eye and a physical size.</summary>
    public struct CockpitPlacement
    {
        public CockpitItem Item;
        /// <summary>Degrees right of the resting view direction.</summary>
        public float Yaw;
        /// <summary>Degrees above the resting view direction (negative: below).</summary>
        public float Pitch;
        /// <summary>Visible face size, metres.</summary>
        public Vector2 Size;
        /// <summary>Pick volume, metres; larger than the face so taps need not be precise.</summary>
        public Vector3 PickSize;

        public CockpitPlacement(CockpitItem item, float yaw, float pitch, Vector2 size, Vector3 pickSize)
        {
            Item = item;
            Yaw = yaw;
            Pitch = pitch;
            Size = size;
            PickSize = pickSize;
        }
    }

    /// <summary>
    /// The cockpit panel, laid out by viewing angle from the pilot's eye, in the
    /// eye anchor's space (x right, y up, z along the resting view, which looks
    /// 12° below the aircraft's nose). Items sit on one flat panel facing the eye.
    ///
    /// The angles are chosen so that, with the resting view and the default field
    /// of view, every instrument and control projects into the part of a 16:9 to
    /// 20:9 screen that no flight control or HUD button covers (right of the
    /// joystick area, left of the action rail and throttle). Tests check this.
    /// </summary>
    public static class CockpitLayout
    {
        /// <summary>Resting view direction below the aircraft's longitudinal axis, degrees.</summary>
        public const float RestingPitchDegrees = 12f;
        /// <summary>Distance from the eye to the panel along its centre direction, metres.</summary>
        public const float PanelDistance = 0.7f;
        /// <summary>The panel faces the eye along this direction (yaw, pitch).</summary>
        public static readonly Vector2 PanelCentreAngles = new Vector2(8f, -16f);
        /// <summary>Panel size, metres.</summary>
        public static readonly Vector2 PanelSize = new Vector2(0.58f, 0.40f);

        static readonly Vector2 InstrumentSize = new Vector2(0.095f, 0.095f);
        static readonly Vector3 InstrumentPick = new Vector3(0.11f, 0.11f, 0.06f);
        static readonly Vector2 SwitchSize = new Vector2(0.03f, 0.045f);
        static readonly Vector3 SwitchPick = new Vector3(0.08f, 0.09f, 0.08f);
        static readonly Vector2 FlapSelectorSize = new Vector2(0.03f, 0.08f);
        static readonly Vector3 FlapSelectorPick = new Vector3(0.08f, 0.11f, 0.08f);

        /// <summary>
        /// Instruments in two rows of three (airspeed, attitude, altimeter; GPS map,
        /// heading, vertical speed), the flap selector right of the vertical speed
        /// indicator, and the switches in a row below. At the default view each
        /// target is at least about 110 × 150 px on a 1080-px-high screen.
        /// </summary>
        public static readonly CockpitPlacement[] Placements =
        {
            new CockpitPlacement(CockpitItem.AirspeedIndicator, -1f, -8f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.AttitudeIndicator, 8f, -8f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.Altimeter, 17f, -8f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.NavigationDisplay, -1f, -18f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.HeadingIndicator, 8f, -18f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.VerticalSpeedIndicator, 17f, -18f, InstrumentSize, InstrumentPick),
            new CockpitPlacement(CockpitItem.FlapSelector, 26.5f, -18f, FlapSelectorSize, FlapSelectorPick),
            new CockpitPlacement(CockpitItem.IgnitionSwitch, -2f, -25.5f, SwitchSize, SwitchPick),
            new CockpitPlacement(CockpitItem.LandingLightSwitch, 5.5f, -25.5f, SwitchSize, SwitchPick),
            new CockpitPlacement(CockpitItem.NavigationLightsSwitch, 13f, -25.5f, SwitchSize, SwitchPick),
            new CockpitPlacement(CockpitItem.ParkingBrakeHandle, 20.5f, -25.5f, SwitchSize, SwitchPick),
        };

        /// <summary>Unit direction from the eye for a yaw/pitch pair, eye-anchor space.</summary>
        public static Vector3 Direction(float yawDegrees, float pitchDegrees)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad, pitch = pitchDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        /// <summary>Panel centre, eye-anchor space.</summary>
        public static Vector3 PanelCentre => Direction(PanelCentreAngles.x, PanelCentreAngles.y) * PanelDistance;

        /// <summary>Unit normal of the panel, pointing towards the eye.</summary>
        public static Vector3 PanelNormal => -Direction(PanelCentreAngles.x, PanelCentreAngles.y);

        /// <summary>Where the line of sight at a direction meets the panel, eye-anchor space.</summary>
        public static Vector3 PointOnPanel(float yawDegrees, float pitchDegrees)
        {
            Vector3 direction = Direction(yawDegrees, pitchDegrees);
            Vector3 normal = PanelNormal;
            float t = Vector3.Dot(PanelCentre, normal) / Vector3.Dot(direction, normal);
            return direction * t;
        }

        public static Vector3 PointOnPanel(in CockpitPlacement placement) => PointOnPanel(placement.Yaw, placement.Pitch);

        public static bool TryGetPlacement(CockpitItem item, out CockpitPlacement placement)
        {
            for (int i = 0; i < Placements.Length; i++)
            {
                if (Placements[i].Item != item) continue;
                placement = Placements[i];
                return true;
            }
            placement = default;
            return false;
        }

        /// <summary>
        /// Pinhole projection of an eye-anchor-space point for a camera looking along
        /// the resting direction: viewport coordinates (0-1, origin bottom left).
        /// Returns false behind the eye.
        /// </summary>
        public static bool ProjectToViewport(Vector3 point, float verticalFieldOfView, float aspect, out Vector2 viewport)
        {
            viewport = default;
            if (point.z <= 1e-4f) return false;
            float tanHalf = Mathf.Tan(0.5f * verticalFieldOfView * Mathf.Deg2Rad);
            viewport = new Vector2(0.5f + point.x / point.z / (2f * tanHalf * aspect), 0.5f + point.y / point.z / (2f * tanHalf));
            return true;
        }
    }
}
