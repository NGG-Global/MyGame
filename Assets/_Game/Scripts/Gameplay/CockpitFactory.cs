using System.Collections.Generic;
using OpenSkies.Cockpit;
using OpenSkies.FlightState;
using OpenSkies.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.Gameplay
{
    /// <summary>Optional materials for the cockpit placeholders. Without them, primitives keep their default material.</summary>
    public sealed class CockpitMaterials
    {
        public Material Panel;
        public Material Frame;
        public Material Switch;
        public Material Indicator;
        public Material LensWhite;
        public Material LensRed;
        public Material LensGreen;
    }

    /// <summary>
    /// Builds the placeholder cockpit (scene builder and tests): an instrument
    /// panel facing the pilot's eye with six instruments on a world-space canvas,
    /// a switch row, a flap selector and a glare shield, laid out by
    /// <see cref="CockpitLayout"/>; and the exterior lights. Primitive shapes and
    /// flat colours only: the modelled interior is unfinished (docs/COCKPIT.md).
    /// </summary>
    public static class CockpitFactory
    {
        /// <summary>Metres per canvas unit on the instrument panel: a 400-unit face is 0.1 m.</summary>
        public const float PanelScale = 0.00025f;

        /// <param name="eyeAnchor">The pilot's eye point; its forward is the resting view direction.</param>
        public static CockpitRig BuildInterior(Transform eyeAnchor, Font font, CockpitMaterials materials = null)
        {
            var rootGo = new GameObject("Cockpit");
            rootGo.SetActive(false); // wire everything before Awake
            var root = rootGo.transform;
            root.SetParent(eyeAnchor, false);
            var rig = rootGo.AddComponent<CockpitRig>();

            Vector3 centre = CockpitLayout.PanelCentre;
            Vector3 normal = CockpitLayout.PanelNormal;                  // towards the eye
            Quaternion panelRotation = Quaternion.LookRotation(-normal, Vector3.up);
            Vector3 panelUp = panelRotation * Vector3.up;
            Vector2 size = CockpitLayout.PanelSize;

            Box("PanelBacking", root, centre - normal * 0.012f, panelRotation, new Vector3(size.x, size.y, 0.02f), materials?.Panel);
            Box("GlareShield", root, centre + panelUp * (0.5f * size.y + 0.012f) + normal * 0.06f, panelRotation,
                new Vector3(size.x + 0.06f, 0.025f, 0.14f), materials?.Frame);

            // Instrument faces and placards on one world-space canvas, just in front of the backing.
            var canvasGo = new GameObject("InstrumentPanel", typeof(RectTransform), typeof(Canvas));
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.SetParent(root, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasRect.sizeDelta = size / PanelScale;
            canvasRect.localPosition = centre + normal * 0.001f;
            canvasRect.localRotation = panelRotation;
            canvasRect.localScale = Vector3.one * PanelScale;
            rig.instrumentPanel = canvas;

            var interactables = new List<CockpitInteractable>();
            var switches = new List<CockpitSwitchView>();
            Quaternion toPanel = Quaternion.Inverse(panelRotation);

            foreach (var placement in CockpitLayout.Placements)
            {
                Vector3 point = CockpitLayout.PointOnPanel(placement);
                Vector3 onCanvas = toPanel * (point - centre) / PanelScale;

                if (CockpitItems.IsInstrument(placement.Item))
                {
                    var kind = (InstrumentKind)(int)placement.Item;
                    var face = InstrumentFaceFactory.Build(kind, canvasRect, font);
                    var faceRect = (RectTransform)face.transform;
                    faceRect.anchoredPosition = new Vector2(onCanvas.x, onCanvas.y);
                    float scale = placement.Size.x / (InstrumentFace.DesignSize * PanelScale);
                    faceRect.localScale = new Vector3(scale, scale, 1f);
                    rig.instruments[(int)kind] = face;
                    interactables.Add(Interactable(placement, root, point + normal * 0.004f, panelRotation));
                }
                else
                {
                    var holder = new GameObject(placement.Item.ToString()).transform;
                    holder.SetParent(root, false);
                    holder.localPosition = point;
                    holder.localRotation = panelRotation;
                    switches.Add(BuildControl(placement, holder, materials));
                    interactables.Add(Interactable(placement, root, point + normal * 0.01f, panelRotation));
                    Placard(canvasRect, font, CockpitItems.Placard(placement.Item),
                        new Vector2(onCanvas.x, onCanvas.y - (0.5f * placement.Size.y + 0.012f) / PanelScale));
                }
            }

            rig.interactables = interactables.ToArray();
            rig.switchViews = switches.ToArray();
            rig.selection = SelectionFrame(rootGo, canvasRect); // last on the canvas: drawn over the faces
            rootGo.SetActive(true);
            return rig;
        }

        /// <summary>Selection colour: the same amber as engaged HUD actions.</summary>
        static readonly Color SelectionColour = new Color(0.96f, 0.74f, 0.22f, 1f);
        const float SelectionLineWidth = 14f; // canvas units: 3.5 mm, about 5 px at the resting view on a 1080-px screen

        /// <summary>A hollow frame (four edges, so the selected instrument stays readable), hidden until something is selected.</summary>
        static CockpitSelectionView SelectionFrame(GameObject rigGo, RectTransform canvas)
        {
            var frame = new GameObject("SelectionFrame", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(canvas, false);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            Edge(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, SelectionLineWidth));
            Edge(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, SelectionLineWidth));
            Edge(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(SelectionLineWidth, 0f));
            Edge(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(SelectionLineWidth, 0f));
            frame.gameObject.SetActive(false);

            var view = rigGo.AddComponent<CockpitSelectionView>();
            view.frame = frame;
            return view;
        }

        static void Edge(RectTransform frame, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var edge = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            edge.SetParent(frame, false);
            edge.anchorMin = anchorMin;
            edge.anchorMax = anchorMax;
            edge.pivot = new Vector2(0.5f, 0.5f);
            edge.anchoredPosition = Vector2.zero;
            edge.sizeDelta = size;
            var image = edge.gameObject.AddComponent<Image>();
            image.color = SelectionColour;
            image.raycastTarget = false;
        }

        /// <summary>Landing light (spot light and lens) and navigation lights on the airframe.</summary>
        public static AircraftLightsView BuildExteriorLights(Transform airframe, CockpitMaterials materials = null)
        {
            var go = new GameObject("ExteriorLights");
            go.transform.SetParent(airframe, false);
            var view = go.AddComponent<AircraftLightsView>();

            var lightGo = new GameObject("LandingLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(-1.6f, 2.0f, 1.25f);   // left wing leading edge
            lightGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);       // slightly down
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 150f;
            spot.spotAngle = 28f;
            spot.intensity = 4f;
            spot.color = new Color(1f, 0.96f, 0.88f);
            spot.shadows = LightShadows.None;
            view.landingLight = spot;
            view.landingLightLenses = new[] { Lens("LandingLightLens", go.transform, new Vector3(-1.6f, 2.0f, 1.22f), 0.1f, materials?.LensWhite) };

            view.navigationLights = new[]
            {
                Lens("NavLeftRed", go.transform, new Vector3(-5.55f, 2.1f, 0.4f), 0.12f, materials?.LensRed),
                Lens("NavRightGreen", go.transform, new Vector3(5.55f, 2.1f, 0.4f), 0.12f, materials?.LensGreen),
                Lens("NavTailWhite", go.transform, new Vector3(0f, 3.0f, -4.62f), 0.1f, materials?.LensWhite),
            };
            return view;
        }

        // ---- Controls ---------------------------------------------------------------------

        static CockpitSwitchView BuildControl(in CockpitPlacement placement, Transform holder, CockpitMaterials materials)
        {
            CockpitItems.TryGetSystem(placement.Item, out var system);
            var view = holder.gameObject.AddComponent<CockpitSwitchView>();
            view.system = system;

            // Holder axes: x right, y up along the panel, z into the panel (away from the eye).
            Box("Base", holder, new Vector3(0f, 0f, -0.002f), Quaternion.identity,
                new Vector3(placement.Size.x + 0.01f, placement.Size.y + 0.01f, 0.006f), materials?.Frame);
            view.indicator = Box("Indicator", holder, new Vector3(0f, 0.5f * placement.Size.y + 0.012f, -0.004f), Quaternion.identity,
                new Vector3(0.008f, 0.008f, 0.006f), materials?.Indicator).GetComponent<Renderer>();

            var moving = new GameObject("Moving").transform;
            moving.SetParent(holder, false);
            view.moving = moving;

            switch (system)
            {
                case AircraftSystem.ParkingBrake:
                    // T-handle: pulled out towards the pilot when set.
                    Box("Shaft", moving, new Vector3(0f, 0f, -0.015f), Quaternion.identity, new Vector3(0.008f, 0.008f, 0.03f), materials?.Switch);
                    Box("Handle", moving, new Vector3(0f, 0f, -0.032f), Quaternion.identity, new Vector3(0.05f, 0.012f, 0.012f), materials?.Switch);
                    view.offLocalPosition = Vector3.zero;
                    view.onLocalPosition = new Vector3(0f, 0f, -0.03f);
                    view.travelSeconds = 0.2f;
                    break;
                case AircraftSystem.Flaps:
                    // Lever in a slot: UP at the top, full flap at the bottom.
                    Box("Slot", holder, new Vector3(0f, 0f, -0.006f), Quaternion.identity, new Vector3(0.01f, placement.Size.y, 0.004f), materials?.Panel);
                    Box("Knob", moving, new Vector3(0f, 0f, -0.012f), Quaternion.identity, new Vector3(0.03f, 0.014f, 0.02f), materials?.Switch);
                    view.offLocalPosition = new Vector3(0f, 0.4f * placement.Size.y, 0f);
                    view.onLocalPosition = new Vector3(0f, -0.4f * placement.Size.y, 0f);
                    view.travelSeconds = 0.3f;
                    break;
                default:
                    // Toggle switch: lever towards the pilot, up = on.
                    Box("Lever", moving, new Vector3(0f, 0f, -0.016f), Quaternion.identity, new Vector3(0.008f, 0.008f, 0.03f), materials?.Switch);
                    view.offLocalEuler = new Vector3(-30f, 0f, 0f);
                    view.onLocalEuler = new Vector3(30f, 0f, 0f);
                    break;
            }
            return view;
        }

        static CockpitInteractable Interactable(in CockpitPlacement placement, Transform root, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(placement.Item + "Target");
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            var interactable = go.AddComponent<CockpitInteractable>();
            interactable.item = placement.Item;
            interactable.pickSize = placement.PickSize;
            interactable.ApplyDefaultLabels();
            return interactable;
        }

        static void Placard(RectTransform canvas, Font font, string text, Vector2 position)
        {
            var go = new GameObject("Placard", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(360f, 70f);
            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = 48;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.92f, 0.92f, 0.88f, 1f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = text;
        }

        static Renderer Lens(string name, Transform parent, Vector3 position, float diameter, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            RemoveCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * diameter;
            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            return renderer;
        }

        static Transform Box(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            RemoveCollider(go); // the cockpit must never touch the physics (picking uses its own math)
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = size;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
    }
}
