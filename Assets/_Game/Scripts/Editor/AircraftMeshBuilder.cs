using System;
using System.Collections.Generic;
using OpenSkies.Cockpit;
using OpenSkies.Gameplay;
using OpenSkies.Simulation;
using UnityEngine;

namespace OpenSkies.Editor
{
    /// <summary>Materials of the flown aircraft's model.</summary>
    public sealed class AircraftMaterials
    {
        /// <summary>The painted airframe: base map is the generated livery atlas.</summary>
        public Material Paint;
        /// <summary>Struts, gear legs and small fittings.</summary>
        public Material Accent;
        public Material Glass;
        public Material Interior;
        public Material Metal;
        public Material Tyre;
        public Material Dark;
        /// <summary>The translucent propeller disc (unlit, two-sided).</summary>
        public Material PropellerDisc;
    }

    /// <summary>
    /// The fictional four-seat high-wing trainer, modelled from code: a lofted
    /// fuselage with real window openings and a cabin interior behind them, a
    /// cambered wing with flaps and ailerons in their bays, stabiliser with elevator,
    /// fin with rudder, cowling, spinner and propeller, spring gear with wheel
    /// fairings, and a generated livery atlas. Wheels sit where the aircraft data
    /// puts them, so visuals and physics agree. Meshes are saved as assets and
    /// updated in place; the moving parts are wired into an <see cref="AirframeView"/>.
    /// Dimensions are in metres in the aircraft frame (+Z nose, +Y up, +X right,
    /// origin at wheel-contact level with the gear extended).
    /// </summary>
    public static class AircraftMeshBuilder
    {
        const string MeshesFolder = "Assets/_Game/Art/Meshes/Aircraft";
        public const string LiveryPath = "Assets/_Game/Art/Textures/Aircraft/TrainerLivery.png";
        public const string PropellerDiscPath = "Assets/_Game/Art/Textures/Aircraft/PropellerDisc.png";
        const int LiverySize = 1024;

        // ---- Fuselage -----------------------------------------------------------------------------------

        public const float NoseZ = 3.15f;
        public const float TailZ = -4.55f;
        const int FuselagePoints = 32;

        /// <summary>One fuselage station: a rounded section at z.</summary>
        public struct Station
        {
            public float Z, HalfWidth, HalfHeight, CentreY, Squareness;

            public Station(float z, float halfWidth, float halfHeight, float centreY, float squareness)
            {
                Z = z;
                HalfWidth = halfWidth;
                HalfHeight = halfHeight;
                CentreY = centreY;
                Squareness = squareness;
            }
        }

        /// <summary>Nose to tail. The cabin roof peaks at 2.09 m, the belly is at 0.75 m.</summary>
        public static readonly Station[] Stations =
        {
            new Station(3.15f, 0.24f, 0.24f, 1.25f, 2.0f),
            new Station(2.95f, 0.40f, 0.38f, 1.27f, 2.4f),
            new Station(2.55f, 0.52f, 0.50f, 1.31f, 3.0f),
            new Station(2.30f, 0.58f, 0.54f, 1.33f, 3.2f),
            new Station(2.05f, 0.61f, 0.58f, 1.36f, 3.4f),
            new Station(1.80f, 0.62f, 0.63f, 1.39f, 3.5f),
            new Station(1.45f, 0.62f, 0.66f, 1.41f, 3.6f),
            new Station(0.60f, 0.62f, 0.67f, 1.42f, 3.6f),
            new Station(-0.30f, 0.61f, 0.66f, 1.42f, 3.5f),
            new Station(-0.90f, 0.56f, 0.60f, 1.44f, 3.2f),
            new Station(-1.60f, 0.46f, 0.50f, 1.48f, 2.9f),
            new Station(-2.40f, 0.36f, 0.40f, 1.52f, 2.6f),
            new Station(-3.20f, 0.27f, 0.32f, 1.56f, 2.3f),
            new Station(-3.90f, 0.19f, 0.26f, 1.60f, 2.1f),
            new Station(-4.55f, 0.10f, 0.18f, 1.66f, 2.0f),
        };

        /// <summary>Texture coordinate along the fuselage: 0 at the nose, 1 at the tail.</summary>
        public static float UOf(float z) => (NoseZ - z) / (NoseZ - TailZ);

        /// <summary>
        /// Window openings as rectangles of (u along the fuselage, v around it: 0.25 right,
        /// 0.5 top, 0.75 left): windscreen, front and rear side windows, rear window.
        /// </summary>
        public static readonly Rect[] Windows =
        {
            new Rect(UOf(2.30f), 0.33f, UOf(1.45f) - UOf(2.30f), 0.34f),   // windscreen
            new Rect(UOf(1.45f), 0.27f, UOf(0.35f) - UOf(1.45f), 0.13f),   // right door window
            new Rect(UOf(1.45f), 0.60f, UOf(0.35f) - UOf(1.45f), 0.13f),   // left door window
            new Rect(UOf(0.20f), 0.27f, UOf(-0.80f) - UOf(0.20f), 0.13f),  // right rear window
            new Rect(UOf(0.20f), 0.60f, UOf(-0.80f) - UOf(0.20f), 0.13f),  // left rear window
            new Rect(UOf(-0.95f), 0.37f, UOf(-1.50f) - UOf(-0.95f), 0.26f), // rear window
        };

        public static bool IsWindow(float u, float v)
        {
            foreach (var w in Windows)
                if (u >= w.xMin && u <= w.xMax && v >= w.yMin && v <= w.yMax) return true;
            return false;
        }

        // ---- Wing and tail ------------------------------------------------------------------------------

        public const float WingY = 2.17f;
        public const float WingRootLeadingEdgeZ = 1.22f;
        public const float WingRootChord = 1.65f;
        public const float WingTipChord = 1.15f;
        public const float WingSemiSpan = 5.5f;
        public const float WingTaperStart = 0.58f;
        public const float WingDihedralDegrees = 1.5f;
        public const float WingHinge = 0.75f;
        public static readonly Vector2 FlapSpan = new Vector2(0.13f, 0.57f);
        public static readonly Vector2 AileronSpan = new Vector2(0.61f, 0.95f);
        const float WingThickness = 0.12f, WingCamber = 0.02f, WingCamberPosition = 0.4f;

        const float StabY = 1.66f, StabHingeZ = -4.22f, StabRootChord = 0.98f, StabTipChord = 0.72f, StabSemiSpan = 1.8f, StabHinge = 0.58f;
        static readonly Vector2 ElevatorSpan = new Vector2(0.08f, 0.95f);
        const float FinBaseY = 1.72f, FinRootLeadingEdgeZ = -3.5f, FinRootChord = 1.35f, FinTipChord = 0.7f, FinHeight = 1.4f, FinTipLeadingEdgeZ = -4.25f;
        const float FinHinge = 0.55f;
        static readonly Vector2 RudderSpan = new Vector2(0.04f, 0.95f);
        const float TailThickness = 0.10f;
        const int AirfoilSamples = 14;

        /// <summary>Wing chord at a span fraction (constant inboard, tapering outboard).</summary>
        public static float WingChord(float f) => f <= WingTaperStart ? WingRootChord
            : Mathf.Lerp(WingRootChord, WingTipChord, (f - WingTaperStart) / (1f - WingTaperStart));

        /// <summary>Where the exterior lights sit on this model: landing light in the left wing's leading edge, navigation lights at the tips and the fin.</summary>
        public static CockpitFactory.ExteriorLightLayout ExteriorLights => new CockpitFactory.ExteriorLightLayout
        {
            LandingLight = new Vector3(-1.7f, WingYAt(1.7f / WingSemiSpan) + 0.01f, WingRootLeadingEdgeZ - 0.02f),
            NavigationLeft = new Vector3(-WingSemiSpan - 0.02f, WingYAt(1f) + 0.01f, WingRootLeadingEdgeZ - 0.62f * WingTipChord - 0.1f),
            NavigationRight = new Vector3(WingSemiSpan + 0.02f, WingYAt(1f) + 0.01f, WingRootLeadingEdgeZ - 0.62f * WingTipChord - 0.1f),
            Tail = new Vector3(0f, FinBaseY + FinHeight - 0.08f, FinTipLeadingEdgeZ - 0.55f * FinTipChord),
        };

        // ---- Atlas regions (the livery texture) -----------------------------------------------------

        public static readonly Rect FuselageRegion = new Rect(0f, 0.5f, 1f, 0.5f);
        public static readonly Rect WingRegion = new Rect(0f, 0.25f, 1f, 0.25f);
        public static readonly Rect TailRegion = new Rect(0.5f, 0f, 0.5f, 0.25f);
        public static readonly Rect SurfaceRegion = new Rect(0f, 0f, 0.5f, 0.25f);

        static readonly Color PaintWhite = new Color(0.93f, 0.94f, 0.95f);
        static readonly Color PaintBlue = new Color(0.10f, 0.27f, 0.58f);
        static readonly Color PaintRed = new Color(0.78f, 0.12f, 0.12f);
        static readonly Color AntiGlare = new Color(0.17f, 0.18f, 0.2f);
        static readonly Color Walkway = new Color(0.36f, 0.37f, 0.38f);
        static readonly Color Seam = new Color(0.66f, 0.68f, 0.7f);

        // ---- Build ----------------------------------------------------------------------------------------

        /// <summary>Models the aircraft under <paramref name="body"/> and returns the view of its moving parts.</summary>
        public static AirframeView Build(Transform body, AircraftConfiguration configuration, AircraftMaterials m)
        {
            var view = body.gameObject.AddComponent<AirframeView>();

            // The painted airframe: fuselage, wings, tail planes and wheel fairings in one mesh and one draw call.
            var airframe = new MeshData();
            BuildFuselage(airframe, 0);
            var rightWing = BuildWing();
            airframe.Append(rightWing, Matrix4x4.identity);
            airframe.Append(rightWing, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)));
            var stab = BuildStabiliser();
            airframe.Append(stab, Matrix4x4.identity);
            airframe.Append(stab, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)));
            airframe.Append(BuildFin(), FinTransform);
            foreach (var wheel in configuration.wheels)
                airframe.Append(BuildWheelFairing(), Matrix4x4.Translate(WheelCentre(wheel)));
            Part("Airframe", body, airframe, m.Paint, Vector3.zero, Quaternion.identity);

            var glass = new MeshData();
            MeshKit.Loft(glass, FuselageRings(1f), 0, (segment, point, u, v) => !IsWindow(u, v));
            Part("Glass", body, glass, m.Glass, Vector3.zero, Quaternion.identity);

            var interior = new MeshData();
            BuildInterior(interior, 0);
            Part("Interior", body, interior, m.Interior, Vector3.zero, Quaternion.identity);

            // Spinner and propeller on a hub that turns about the nose axis.
            var nose = Stations[0];
            var hub = new GameObject("PropellerHub").transform;
            hub.SetParent(body, false);
            hub.localPosition = new Vector3(0f, nose.CentreY, nose.Z + 0.03f);
            view.propeller = hub;
            var spinner = new MeshData();
            MeshKit.Revolve(spinner, new[] { new Vector2(0.235f, 0f), new Vector2(0.225f, 0.12f), new Vector2(0.17f, 0.3f), new Vector2(0.09f, 0.42f), new Vector2(0f, 0.48f) },
                24, 0, capBottom: true, capTop: false);
            Part("Spinner", hub, spinner, m.Metal, Vector3.zero, Quaternion.Euler(90f, 0f, 0f)); // the revolve's +Y becomes +Z
            var blades = BuildPropeller();
            view.propellerBlades = Part("Blades", hub, blades, m.Dark, new Vector3(0f, 0f, 0.09f), Quaternion.identity).GetComponent<Renderer>();
            var disc = new MeshData();
            MeshKit.Disc(disc, 0.98f, 48, 0);
            view.propellerDisc = Part("Disc", hub, disc, m.PropellerDisc, new Vector3(0f, 0f, 0.1f), Quaternion.identity).GetComponent<Renderer>();
            view.propellerDisc.enabled = false;

            // Control surfaces: right wing hinges point outboard (+X), left wing hinges inboard (also +X),
            // so a positive turn about local X raises every trailing edge (AirframeView's convention).
            view.flapRight = Surface("FlapRight", body, BuildWingSurface(FlapSpan, out var flapStart, out var flapEnd), m.Paint, flapStart, flapEnd, mirror: false);
            view.flapLeft = Surface("FlapLeft", body, BuildWingSurface(FlapSpan, out _, out _), m.Paint, flapStart, flapEnd, mirror: true);
            view.aileronRight = Surface("AileronRight", body, BuildWingSurface(AileronSpan, out var aileronStart, out var aileronEnd), m.Paint, aileronStart, aileronEnd, false);
            view.aileronLeft = Surface("AileronLeft", body, BuildWingSurface(AileronSpan, out _, out _), m.Paint, aileronStart, aileronEnd, true);

            // The elevator is one piece across both sides on a straight hinge.
            var elevatorHalf = BuildTailSurface(ElevatorSpan, StabHingeZ, StabY, StabRootChord, StabTipChord, StabSemiSpan, StabHinge, out var elevatorStart, out var elevatorEnd);
            var elevator = new MeshData();
            elevator.Append(elevatorHalf, Matrix4x4.identity);
            elevator.Append(elevatorHalf, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)));
            view.elevator = Surface("Elevator", body, elevator, m.Paint, elevatorStart, elevatorEnd, false, hingeAxis: Vector3.right);

            // The rudder is built in the fin's wing space (span along +X), then stood up with the fin.
            var rudder = BuildFinSurface(out var rudderStart, out var rudderEnd);
            view.rudder = Surface("Rudder", body, rudder, m.Paint, rudderStart, rudderEnd, false, hingeAxis: Vector3.up, standUp: FinTransform);

            BuildGear(body, configuration, m, view);
            BuildFittings(body, m);
            return view;
        }

        static readonly Matrix4x4 FinTransform = Matrix4x4.TRS(new Vector3(0f, FinBaseY, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);

        // ---- Fuselage, glass, interior -----------------------------------------------------------------

        /// <summary>The fuselage rings from the tail to the nose (the loft advances along +Z), scaled about each station's centre.</summary>
        static List<MeshKit.Ring> FuselageRings(float scale)
        {
            var rings = new List<MeshKit.Ring>(Stations.Length);
            for (int i = Stations.Length - 1; i >= 0; i--)
            {
                var s = Stations[i];
                var section = MeshKit.RoundedSection(FuselagePoints, s.HalfWidth * scale, s.HalfHeight * scale, s.CentreY, s.Squareness);
                rings.Add(MeshKit.Place(section, s.Z, UOf(s.Z)));
            }
            return rings;
        }

        static void BuildFuselage(MeshData mesh, int submesh)
        {
            int first = mesh.VertexCount;
            MeshKit.Loft(mesh, FuselageRings(1f), submesh, (segment, point, u, v) => IsWindow(u, v), capStart: true, capEnd: true);
            mesh.MapUv(FuselageRegion, first);
        }

        /// <summary>The cabin: an inward-facing shell with the same window openings, a floor and four seats.</summary>
        static void BuildInterior(MeshData mesh, int submesh)
        {
            var rings = new List<MeshKit.Ring>();
            foreach (var s in Stations)
            {
                if (s.Z < -1.65f || s.Z > 2.1f) continue;
                var section = MeshKit.RoundedSection(FuselagePoints, s.HalfWidth * 0.93f, s.HalfHeight * 0.93f, s.CentreY, s.Squareness);
                rings.Add(MeshKit.Place(section, s.Z, UOf(s.Z)));
            }
            rings.Reverse(); // tail to nose: the loft advances along +Z
            MeshKit.Loft(mesh, rings, submesh, (segment, point, u, v) => IsWindow(u, v), capStart: true, capEnd: true, inside: true);
            MeshKit.Box(mesh, new Vector3(0f, 0.86f, 0.45f), new Vector3(1.05f, 0.04f, 2.6f), submesh); // cabin floor
            foreach (float x in new[] { -0.33f, 0.33f })
            foreach (float z in new[] { 0.75f, -0.25f })
            {
                MeshKit.Box(mesh, new Vector3(x, 0.98f, z), new Vector3(0.46f, 0.12f, 0.5f), submesh);           // cushion
                MeshKit.Box(mesh, new Vector3(x, 1.32f, z - 0.27f), new Vector3(0.46f, 0.62f, 0.08f), submesh);  // backrest
            }
            MeshKit.Box(mesh, new Vector3(0f, 1.0f, -1.1f), new Vector3(1.0f, 0.3f, 0.5f), submesh); // baggage shelf
        }

        // ---- Wing and tail planes --------------------------------------------------------------------------

        /// <summary>A section ring placed in wing space: span along +X, chord along -Z from the leading edge, thickness up.</summary>
        static MeshKit.Ring WingRing(Vector2[] section, float chord, Vector3 leadingEdge, float u)
        {
            var points = new Vector3[section.Length];
            for (int i = 0; i < section.Length; i++)
                points[i] = leadingEdge + Vector3.back * (section[i].x * chord) + Vector3.up * (section[i].y * chord);
            return new MeshKit.Ring(points, u);
        }

        static float WingYAt(float f) => WingY + f * WingSemiSpan * Mathf.Tan(WingDihedralDegrees * Mathf.Deg2Rad);

        /// <summary>The right wing with the flap and aileron bays cut, in the wing atlas region.</summary>
        static MeshData BuildWing()
        {
            var mesh = new MeshData();
            var whole = MeshKit.Airfoil(AirfoilSamples, WingThickness, WingCamber, WingCamberPosition);
            var cut = MeshKit.AirfoilFront(AirfoilSamples, WingThickness, WingCamber, WingCamberPosition, WingHinge);
            var rings = new List<MeshKit.Ring>();
            float gap = 0.006f;
            float[] stations =
            {
                0f, FlapSpan.x - gap, FlapSpan.x + gap, FlapSpan.y - gap, FlapSpan.y + gap, WingTaperStart,
                AileronSpan.x - gap, AileronSpan.x + gap, AileronSpan.y - gap, AileronSpan.y + gap,
            };
            foreach (float f in stations)
            {
                bool inBay = (f > FlapSpan.x && f < FlapSpan.y) || (f > AileronSpan.x && f < AileronSpan.y);
                rings.Add(WingRing(inBay ? cut : whole, WingChord(f), new Vector3(f * WingSemiSpan, WingYAt(f), WingRootLeadingEdgeZ), f));
            }
            // Rounded tip: the chord shrinks and the leading edge moves aft; the last ring is nearly a point.
            float tipChord = WingChord(1f);
            foreach (var tip in new[] { new Vector3(0.975f, 0.85f, 0.10f), new Vector3(0.993f, 0.55f, 0.35f), new Vector3(1f, 0.2f, 0.62f) })
            {
                var thin = MeshKit.Airfoil(AirfoilSamples, WingThickness * Mathf.Lerp(1f, 0.3f, (tip.x - 0.95f) / 0.05f), WingCamber, WingCamberPosition);
                rings.Add(WingRing(thin, tipChord * tip.y, new Vector3(tip.x * WingSemiSpan, WingYAt(tip.x), WingRootLeadingEdgeZ - tipChord * tip.z), tip.x));
            }
            MeshKit.Loft(mesh, rings, 0, null, capStart: false, capEnd: true);
            mesh.MapUv(WingRegion);
            return mesh;
        }

        /// <summary>A flap or aileron of the right wing, in wing space, and its hinge line ends.</summary>
        static MeshData BuildWingSurface(Vector2 span, out Vector3 hingeStart, out Vector3 hingeEnd)
        {
            var section = MeshKit.AirfoilSurface(AirfoilSamples, WingThickness, WingCamber, WingCamberPosition, WingHinge);
            var mesh = new MeshData();
            var rings = new List<MeshKit.Ring>();
            foreach (float f in new[] { span.x, Mathf.Lerp(span.x, span.y, 0.5f), span.y })
            {
                float chord = WingChord(f);
                var hinge = new Vector3(f * WingSemiSpan, WingYAt(f), WingRootLeadingEdgeZ - WingHinge * chord);
                rings.Add(WingRing(section, chord, hinge, (f - span.x) / (span.y - span.x)));
            }
            hingeStart = new Vector3(span.x * WingSemiSpan, WingYAt(span.x), WingRootLeadingEdgeZ - WingHinge * WingChord(span.x));
            hingeEnd = new Vector3(span.y * WingSemiSpan, WingYAt(span.y), WingRootLeadingEdgeZ - WingHinge * WingChord(span.y));
            MeshKit.Loft(mesh, rings, 0, null, capStart: true, capEnd: true);
            mesh.MapUv(SurfaceRegion);
            return mesh;
        }

        /// <summary>The right half of the stabiliser (straight hinge line, elevator bay cut).</summary>
        static MeshData BuildStabiliser()
        {
            var mesh = new MeshData();
            var whole = MeshKit.Airfoil(AirfoilSamples, TailThickness, 0f, 0f);
            var cut = MeshKit.AirfoilFront(AirfoilSamples, TailThickness, 0f, 0f, StabHinge);
            var rings = new List<MeshKit.Ring>();
            float gap = 0.01f;
            foreach (float f in new[] { 0f, ElevatorSpan.x - gap, ElevatorSpan.x + gap, ElevatorSpan.y - gap, ElevatorSpan.y + gap })
            {
                float chord = Mathf.Lerp(StabRootChord, StabTipChord, f);
                bool inBay = f > ElevatorSpan.x && f < ElevatorSpan.y;
                rings.Add(WingRing(inBay ? cut : whole, chord, new Vector3(f * StabSemiSpan, StabY, StabHingeZ + StabHinge * chord), f));
            }
            foreach (var tip in new[] { new Vector3(0.985f, 0.7f, 0.2f), new Vector3(1f, 0.3f, 0.5f) })
            {
                var thin = MeshKit.Airfoil(AirfoilSamples, TailThickness * 0.6f, 0f, 0f);
                float chord = StabTipChord * tip.y;
                rings.Add(WingRing(thin, chord, new Vector3(tip.x * StabSemiSpan, StabY, StabHingeZ + StabHinge * StabTipChord - StabTipChord * tip.z), tip.x));
            }
            MeshKit.Loft(mesh, rings, 0, null, capStart: false, capEnd: true);
            mesh.MapUv(TailRegion);
            return mesh;
        }

        /// <summary>An elevator half (or any tail surface on a straight hinge), in wing space.</summary>
        static MeshData BuildTailSurface(Vector2 span, float hingeZ, float y, float rootChord, float tipChord, float semiSpan, float hinge,
            out Vector3 hingeStart, out Vector3 hingeEnd)
        {
            var section = MeshKit.AirfoilSurface(AirfoilSamples, TailThickness, 0f, 0f, hinge);
            var mesh = new MeshData();
            var rings = new List<MeshKit.Ring>();
            foreach (float f in new[] { span.x, span.y })
                rings.Add(WingRing(section, Mathf.Lerp(rootChord, tipChord, f), new Vector3(f * semiSpan, y, hingeZ), (f - span.x) / (span.y - span.x)));
            hingeStart = new Vector3(span.x * semiSpan, y, hingeZ);
            hingeEnd = new Vector3(span.y * semiSpan, y, hingeZ);
            MeshKit.Loft(mesh, rings, 0, null, capStart: true, capEnd: true);
            mesh.MapUv(SurfaceRegion);
            return mesh;
        }

        /// <summary>The fin in wing space (span along +X, stood up by <see cref="FinTransform"/>), rudder bay cut.</summary>
        static MeshData BuildFin()
        {
            var mesh = new MeshData();
            var whole = MeshKit.Airfoil(AirfoilSamples, TailThickness, 0f, 0f);
            var cut = MeshKit.AirfoilFront(AirfoilSamples, TailThickness, 0f, 0f, FinHinge);
            var rings = new List<MeshKit.Ring>();
            float gap = 0.01f;
            foreach (float f in new[] { 0f, RudderSpan.x - gap, RudderSpan.x + gap, RudderSpan.y - gap, RudderSpan.y + gap })
            {
                bool inBay = f > RudderSpan.x && f < RudderSpan.y;
                rings.Add(WingRing(inBay ? cut : whole, FinChord(f), new Vector3(f * FinHeight, 0f, FinLeadingEdgeZ(f)), f));
            }
            foreach (var tip in new[] { new Vector3(0.985f, 0.7f, 0.2f), new Vector3(1f, 0.3f, 0.5f) })
            {
                var thin = MeshKit.Airfoil(AirfoilSamples, TailThickness * 0.6f, 0f, 0f);
                rings.Add(WingRing(thin, FinTipChord * tip.y, new Vector3(tip.x * FinHeight, 0f, FinLeadingEdgeZ(1f) - FinTipChord * tip.z), tip.x));
            }
            // A dorsal fillet in front of the fin root.
            MeshKit.Loft(mesh, rings, 0, null, capStart: false, capEnd: true);
            mesh.MapUv(TailRegion);
            return mesh;
        }

        static float FinChord(float f) => Mathf.Lerp(FinRootChord, FinTipChord, f);
        static float FinLeadingEdgeZ(float f) => Mathf.Lerp(FinRootLeadingEdgeZ, FinTipLeadingEdgeZ, f);

        static MeshData BuildFinSurface(out Vector3 hingeStart, out Vector3 hingeEnd)
        {
            var section = MeshKit.AirfoilSurface(AirfoilSamples, TailThickness, 0f, 0f, FinHinge);
            var mesh = new MeshData();
            var rings = new List<MeshKit.Ring>();
            foreach (float f in new[] { RudderSpan.x, RudderSpan.y })
                rings.Add(WingRing(section, FinChord(f), new Vector3(f * FinHeight, 0f, FinLeadingEdgeZ(f) - FinHinge * FinChord(f)), (f - RudderSpan.x) / (RudderSpan.y - RudderSpan.x)));
            hingeStart = new Vector3(RudderSpan.x * FinHeight, 0f, FinLeadingEdgeZ(RudderSpan.x) - FinHinge * FinChord(RudderSpan.x));
            hingeEnd = new Vector3(RudderSpan.y * FinHeight, 0f, FinLeadingEdgeZ(RudderSpan.y) - FinHinge * FinChord(RudderSpan.y));
            MeshKit.Loft(mesh, rings, 0, null, capStart: true, capEnd: true);
            mesh.MapUv(SurfaceRegion);
            return mesh;
        }

        /// <summary>
        /// Places a hinged surface: its mesh is moved into a frame whose origin is the hinge start
        /// and whose <paramref name="hingeAxis"/> (local X or Y) runs along the hinge, so the view
        /// turns it with one local rotation. <paramref name="mirror"/> builds the left-hand twin
        /// with the hinge pointing inboard, keeping the same sign convention.
        /// </summary>
        static Transform Surface(string name, Transform body, MeshData wingSpace, Material material, Vector3 hingeStart, Vector3 hingeEnd, bool mirror,
            Vector3? hingeAxis = null, Matrix4x4? standUp = null)
        {
            Vector3 axis = hingeAxis ?? Vector3.right;
            var world = standUp ?? Matrix4x4.identity;
            if (mirror)
            {
                var m = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
                wingSpace.Transform(m);
                hingeStart = m.MultiplyPoint3x4(hingeStart);
                hingeEnd = m.MultiplyPoint3x4(hingeEnd);
            }
            Vector3 direction = (hingeEnd - hingeStart).normalized;
            if (mirror) direction = -direction; // inboard (+X): a positive turn still raises the trailing edge
            Vector3 origin = world.MultiplyPoint3x4(hingeStart);
            Vector3 worldDirection = world.MultiplyVector(direction).normalized;
            var rotation = Quaternion.FromToRotation(axis, worldDirection);
            // Local mesh: world-space points relative to the origin, turned back so the hinge lies on the axis.
            var toLocal = Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * Matrix4x4.Translate(-origin) * world;
            wingSpace.Transform(toLocal);
            var go = Part(name, body, wingSpace, material, origin, rotation);
            return go.transform;
        }

        // ---- Propeller --------------------------------------------------------------------------------------

        /// <summary>Two blades about +Z, symmetric section, twisted from the root to the tip, in the plane of rotation.</summary>
        static MeshData BuildPropeller()
        {
            var blade = new MeshData();
            var section = MeshKit.Airfoil(10, 0.09f, 0f, 0f);
            var rings = new List<MeshKit.Ring>();
            // radius, chord, twist (degrees)
            foreach (var s in new[] { new Vector3(0.16f, 0.09f, 42f), new Vector3(0.35f, 0.13f, 28f), new Vector3(0.6f, 0.14f, 19f), new Vector3(0.82f, 0.12f, 14f), new Vector3(0.96f, 0.06f, 12f) })
            {
                // The blade at +X moves towards -Y (clockwise seen from the cockpit): leading edge at -Y, pitched so the trailing edge trails aft.
                var twist = Quaternion.Euler(-s.z, 0f, 0f);
                Vector3 chordDir = twist * Vector3.up, thicknessDir = twist * Vector3.forward;
                var points = new Vector3[section.Length];
                Vector3 leadingEdge = new Vector3(s.x, 0f, 0f) - chordDir * (s.y * 0.4f);
                for (int i = 0; i < section.Length; i++) points[i] = leadingEdge + chordDir * (section[i].x * s.y) + thicknessDir * (section[i].y * s.y);
                rings.Add(new MeshKit.Ring(points, (s.x - 0.16f) / 0.8f));
            }
            MeshKit.Loft(blade, rings, 0, null, capStart: true, capEnd: true);
            var mesh = new MeshData();
            mesh.Append(blade, Matrix4x4.identity);
            mesh.Append(blade, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)));
            return mesh;
        }

        // ---- Gear and fittings ---------------------------------------------------------------------------

        public static Vector3 WheelCentre(in WheelDefinition wheel) => wheel.attachPoint + Vector3.down * wheel.suspensionLength;

        /// <summary>A tyre with a hub, revolved about +Y (turned to the axle by the caller).</summary>
        static MeshData BuildWheel(float radius, float width)
        {
            var mesh = new MeshData();
            float h = width * 0.5f;
            MeshKit.Revolve(mesh, new[]
            {
                new Vector2(0f, -h * 0.6f), new Vector2(radius * 0.45f, -h * 0.6f), new Vector2(radius * 0.82f, -h), new Vector2(radius * 0.96f, -h * 0.8f),
                new Vector2(radius, -h * 0.3f), new Vector2(radius, h * 0.3f), new Vector2(radius * 0.96f, h * 0.8f), new Vector2(radius * 0.82f, h),
                new Vector2(radius * 0.45f, h * 0.6f), new Vector2(0f, h * 0.6f),
            }, 20, 0);
            return mesh;
        }

        /// <summary>A teardrop fairing around a wheel (long end forward), open underneath where the tyre shows.</summary>
        static MeshData BuildWheelFairing()
        {
            var mesh = new MeshData();
            var rings = new List<MeshKit.Ring>();
            // z, half width, half height, centre y
            foreach (var s in new[] { new Vector4(-0.45f, 0.02f, 0.08f, 0.16f), new Vector4(-0.32f, 0.13f, 0.22f, 0.12f), new Vector4(-0.1f, 0.18f, 0.3f, 0.1f),
                         new Vector4(0.12f, 0.18f, 0.3f, 0.1f), new Vector4(0.36f, 0.14f, 0.24f, 0.13f), new Vector4(0.58f, 0.02f, 0.08f, 0.2f) })
                rings.Add(MeshKit.Place(MeshKit.RoundedSection(20, s.y, s.z, s.w, 2.4f), s.x, (s.x + 0.45f) / 1.03f));
            // The underside stays open (the tyre is there); openings are the bottom fifth of each ring.
            MeshKit.Loft(mesh, rings, 0, (segment, point, u, v) => (v < 0.1f || v > 0.9f) && segment >= 1 && segment <= 3, capStart: true, capEnd: true);
            mesh.MapUv(SurfaceRegion);
            return mesh;
        }

        static void BuildGear(Transform body, AircraftConfiguration configuration, AircraftMaterials m, AirframeView view)
        {
            var legRoot = new GameObject("Gear").transform;
            legRoot.SetParent(body, false);
            var mainWheel = Saved("WheelMain", BuildWheel(0.25f, 0.17f));
            var noseWheel = Saved("WheelNose", BuildWheel(0.25f, 0.15f));
            var fairing = Saved("WheelFairing", BuildWheelFairing());
            foreach (var wheel in configuration.wheels)
            {
                Vector3 centre = WheelCentre(wheel);
                bool nose = Mathf.Abs(wheel.attachPoint.x) < 0.3f;
                float wheelScale = wheel.wheelRadius / 0.25f;
                if (nose)
                {
                    // The nose gear steers: a pivot at the attach point turns about +Y.
                    var pivot = new GameObject("NoseGear").transform;
                    pivot.SetParent(legRoot, false);
                    pivot.localPosition = wheel.attachPoint;
                    view.noseWheel = pivot;
                    Vector3 local = centre - wheel.attachPoint;
                    Part("Wheel", pivot, noseWheel, m.Tyre, local, Quaternion.Euler(0f, 0f, 90f)).transform.localScale = Vector3.one * wheelScale;
                    Part("Fairing", pivot, fairing, m.Paint, local, Quaternion.identity).transform.localScale = Vector3.one * wheelScale;
                    // The oleo from inside the cowling belly down to the axle.
                    Strut(pivot, m.Accent, new Vector3(0f, 0.25f, 0f), local + Vector3.up * (wheel.wheelRadius * 0.5f), 0.04f, "Strut");
                }
                else
                {
                    Part("Wheel", legRoot, mainWheel, m.Tyre, centre, Quaternion.Euler(0f, 0f, 90f)).transform.localScale = Vector3.one * wheelScale;
                    Part("Fairing", legRoot, fairing, m.Paint, centre, Quaternion.identity).transform.localScale = Vector3.one * wheelScale;
                    // A flat spring leg from the belly down and out to the axle: wide fore-and-aft, thin across.
                    float side = Mathf.Sign(wheel.attachPoint.x);
                    var top = new Vector3(side * 0.42f, 0.82f, wheel.attachPoint.z - 0.05f);
                    var axle = new Vector3(wheel.attachPoint.x - side * 0.1f, centre.y + 0.08f, wheel.attachPoint.z);
                    Leg(legRoot, m.Accent, top, axle, 0.12f, 0.03f);
                }
            }
        }

        static void BuildFittings(Transform body, AircraftMaterials m)
        {
            // Wing struts from the lower fuselage to the wing underside, each with a jury strut.
            foreach (float side in new[] { -1f, 1f })
            {
                float f = 2.75f / WingSemiSpan;
                var under = new Vector3(side * 2.75f, WingYAt(f) - 0.055f * WingChord(f), 0.95f);
                var root = new Vector3(side * 0.6f, 0.98f, 1.0f);
                Strut(body, m.Accent, root, under, 0.035f, "WingStrut");
                Strut(body, m.Accent, Vector3.Lerp(root, under, 0.6f), new Vector3(side * 1.9f, WingYAt(1.9f / WingSemiSpan) - 0.06f * WingRootChord, 0.95f), 0.015f, "JuryStrut");
            }
            // Exhaust under the cowling, a pitot tube under the left wing and an antenna on the roof.
            Strut(body, m.Metal, new Vector3(0.28f, 0.98f, 2.45f), new Vector3(0.3f, 0.84f, 2.1f), 0.03f, "Exhaust");
            Strut(body, m.Metal, new Vector3(-2.2f, WingYAt(0.4f) - 0.09f, 1.15f), new Vector3(-2.2f, WingYAt(0.4f) - 0.09f, 1.55f), 0.012f, "Pitot");
            Strut(body, m.Accent, new Vector3(0f, 2.1f, -0.9f), new Vector3(0f, 2.34f, -1.2f), 0.012f, "Antenna");
        }

        /// <summary>A round strut between two points (a cylinder primitive scaled and turned).</summary>
        static void Strut(Transform parent, Material material, Vector3 from, Vector3 to, float radius, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            go.transform.localScale = new Vector3(radius * 2f, (to - from).magnitude * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>A flat leg between two points: a cube primitive, <paramref name="depth"/> fore-and-aft (+Z), <paramref name="thickness"/> across.</summary>
        static void Leg(Transform parent, Material material, Vector3 from, Vector3 to, float depth, float thickness)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Leg";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.forward, to - from); // local Y along the leg, local Z forward
            go.transform.localScale = new Vector3(thickness, (to - from).magnitude, depth);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Saves the geometry as a mesh asset named after the part (updated in place on every build).</summary>
        static Mesh Saved(string name, MeshData data) => GeneratedAssets.SaveMesh(data, name, $"{MeshesFolder}/{name}.asset");

        static GameObject Part(string name, Transform parent, MeshData data, Material material, Vector3 localPosition, Quaternion localRotation) =>
            Part(name, parent, Saved(name, data), material, localPosition, localRotation);

        static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        // ---- Textures ------------------------------------------------------------------------------------------

        /// <summary>The livery atlas: white with a blue cheat line and red pinstripe, dark anti-glare panel, blue nose and tips, door seams.</summary>
        public static Color32[] LiveryPixels()
        {
            int size = LiverySize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                pixels[y * size + x] = LiveryColour(u, v);
            }
            return pixels;
        }

        /// <summary>The livery colour at an atlas coordinate (pure, so tests can check the design).</summary>
        public static Color LiveryColour(float u, float v)
        {
            if (FuselageRegion.Contains(new Vector2(u, v)))
            {
                float a = (v - FuselageRegion.yMin) / FuselageRegion.height; // around: 0.25 right, 0.5 top, 0.75 left
                float side = a <= 0.5f ? a : 1f - a;                          // mirrored: 0 bottom .. 0.5 top
                float sweep = 0.235f + 0.07f * ProceduralNoise.SmoothStep(0.5f, 0.97f, u);
                if (u < 0.045f) return PaintBlue;                                                   // nose cowl ring
                if (u < 0.17f && side > 0.40f) return AntiGlare;                                    // anti-glare panel on the cowling
                if (Mathf.Abs(side - sweep) < 0.026f) return PaintBlue;                             // cheat line
                if (Mathf.Abs(side - (sweep - 0.042f)) < 0.006f) return PaintRed;                   // pinstripe under it
                bool doorU = u > 0.21f && u < 0.37f, doorV = side > 0.12f && side < 0.27f;
                bool doorEdge = (doorU && doorV) && (Mathf.Abs(u - 0.21f) < 0.0025f || Mathf.Abs(u - 0.37f) < 0.0025f || Mathf.Abs(side - 0.12f) < 0.0025f);
                if (doorEdge) return Seam;                                                           // door outline
                if (u > 0.985f) return PaintBlue;                                                   // tail cone cap
                return PaintWhite;
            }
            if (WingRegion.Contains(new Vector2(u, v)))
            {
                float around = (v - WingRegion.yMin) / WingRegion.height; // 0 trailing edge, 0.5 leading edge (upper surface first)
                if (u > 0.935f) return PaintBlue;                                                   // tip band
                if (u > 0.905f && u < 0.92f) return PaintRed;                                       // pinstripe inboard of it
                if (u > 0.05f && u < 0.2f && around > 0.04f && around < 0.3f) return Walkway;       // walkway at the root, upper surface
                if (Mathf.Abs(around - 0.5f) < 0.03f) return Seam * 0.5f + PaintWhite * 0.5f;        // leading edge: a touch darker
                return PaintWhite;
            }
            if (TailRegion.Contains(new Vector2(u, v)))
            {
                float span = (u - TailRegion.xMin) / TailRegion.width;
                if (span > 0.66f) return PaintBlue;                                                 // top of the fin, stabiliser tips
                if (span > 0.62f) return PaintRed;
                return PaintWhite;
            }
            return PaintWhite; // control surfaces
        }

        /// <summary>The propeller disc: translucent dark grey, clear at the hub, a faint lighter ring where the tips smear.</summary>
        public static Color32[] PropellerDiscPixels(int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centre .. 1 rim
                float alpha = ProceduralNoise.SmoothStep(0.14f, 0.3f, r) * (1f - ProceduralNoise.SmoothStep(0.93f, 1f, r)) * 0.3f;
                float tip = 0.12f * Mathf.Exp(-Mathf.Pow((r - 0.9f) / 0.04f, 2f));
                byte g = (byte)Mathf.RoundToInt(255f * (0.18f + tip * 3f));
                pixels[y * size + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(alpha + tip)));
            }
            return pixels;
        }
    }
}
