using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Static display aircraft parked on the apron: the owner's licensed F-16,
    /// Spitfire and Beechcraft Model 17 models (owner decision, 1 November 2026:
    /// display only, real names, flat colours because the source textures were not
    /// supplied). They are scenery: not flyable, no flight model, no labels in game.
    ///
    /// Each model is baked by Editor code into one static mesh asset with one submesh
    /// per colour role (paint, secondary, metal, glass, tyre, dark, red and green
    /// lights), so it costs a few draw calls instead of dozens. Its size comes from the
    /// real aircraft's wingspan, its nose direction from a part at the nose (pitot tube
    /// or propeller), and it stands on the apron surface. A box around the fuselage
    /// stops a taxiing aircraft; wings and tail have no collision. Each is culled by
    /// screen size (LODGroup), so the graphics preset's LOD bias sets its distance.
    /// </summary>
    public static class DisplayAircraft
    {
        public const string ModelsFolder = "Assets/_Game/Art/Models/Aircraft/Display";
        const string MeshesFolder = "Assets/_Game/Art/Meshes/Display";
        /// <summary>Culled below this share of the screen height (about 850 m for the F-16 at LOD bias 1).</summary>
        const float CullScreenHeight = 0.015f;

        /// <summary>Colour roles: one submesh and one material each.</summary>
        public enum Role
        {
            Paint = 0,
            Secondary,
            Metal,
            Glass,
            Tyre,
            Dark,
            RedLight,
            GreenLight,
        }

        public const int RoleCount = 8;

        public sealed class Spec
        {
            public string File;
            public string Name;
            /// <summary>Wingspan of the real aircraft, m (approximate public figure; the model is scaled to it).</summary>
            public float SpanMetres;
            /// <summary>Length of the real aircraft, m (approximate; only a sanity check of the model's proportions).</summary>
            public float LengthMetres;
            /// <summary>Name of a part at the nose (pitot tube, propeller, spinner): it gives the nose direction.</summary>
            public string NoseMarker;
            /// <summary>Alternative parts for the same item (parked first, in flight second): only one of each pair is kept.</summary>
            public string[][] Alternatives = new string[0][];
            public Vector3 Position;
            /// <summary>Nose direction on the apron, degrees from north (+Z), clockwise.</summary>
            public float HeadingDegrees;
        }

        /// <summary>
        /// The three display aircraft, nose east towards the runway, in a row on the apron's
        /// open half (clear of the taxiway lane at z = ±9 m and of the hangars at x ≤ -136 m).
        /// </summary>
        public static readonly Spec[] Specs =
        {
            new Spec
            {
                File = "F-16.fbx", Name = "F-16", SpanMetres = 9.96f, LengthMetres = 15.06f, NoseMarker = "Pitot tube",
                Alternatives = new[]
                {
                    new[] { "Doors open", "Doors inflight" },
                    new[] { "Flaps down", "Flaps inflight" },
                    new[] { "slots down", "slots inflight" },
                },
                Position = new Vector3(-105f, 0f, -62f), HeadingDegrees = 90f,
            },
            new Spec
            {
                File = "Spitfire.fbx", Name = "Spitfire", SpanMetres = 11.23f, LengthMetres = 9.12f, NoseMarker = "pervane",
                Position = new Vector3(-102f, 0f, -34f), HeadingDegrees = 90f,
            },
            new Spec
            {
                File = "Beechcraft_Model_17.fbx", Name = "Beechcraft Model 17", SpanMetres = 9.75f, LengthMetres = 7.98f, NoseMarker = "Front_Cone",
                Position = new Vector3(-102f, 0f, 40f), HeadingDegrees = 90f,
            },
        };

        /// <summary>Colour role of a source material, from its name (the models' own material names, several languages).</summary>
        public static Role RoleOf(string materialName)
        {
            string n = (materialName ?? string.Empty).ToLowerInvariant();
            if (Has(n, "glass", "window", "canopy") || IsTurkishGlass(n)) return Role.Glass;
            if (Has(n, "tutucu", "tucu")) return Role.Metal;                              // wheel holder
            if (Has(n, "tire", "tyre", "lastik", "teker", "rubber")) return Role.Tyre;
            if (Has(n, "jant", "chrom", "alumin", "metal", "steel", "rim")) return Role.Metal;
            if (Has(n, "red")) return Role.RedLight;
            if (Has(n, "green")) return Role.GreenLight;
            if (Has(n, "black", "pervane", "propel", "zevkcubugu", "interior", "koltuk", "seat", "dash")) return Role.Dark;
            if (Has(n, "parca", "gray", "grey", "lambert", "default")) return Role.Secondary;
            return Role.Paint; // body paint: Paint1, Plane_SHD, gövde
        }

        /// <summary>"cam" is Turkish for glass ("cam", "cam2"); not "camouflage".</summary>
        static bool IsTurkishGlass(string n)
        {
            if (!n.StartsWith("cam", StringComparison.Ordinal)) return false;
            for (int i = 3; i < n.Length; i++)
                if (!char.IsDigit(n[i])) return false;
            return true;
        }

        static bool Has(string name, params string[] parts)
        {
            foreach (var part in parts)
                if (name.Contains(part)) return true;
            return false;
        }

        /// <summary>
        /// Which of two alternative parts to keep: the one the model's author left visible if
        /// only one is; otherwise the parked one, unless it is detached from the airframe (some
        /// models keep the unused variant away from the aircraft). 0 = parked, 1 = in flight.
        /// </summary>
        public static int ChooseAlternative(bool parkedVisible, bool inFlightVisible, bool parkedOnAirframe, bool inFlightOnAirframe)
        {
            if (parkedVisible != inFlightVisible) return parkedVisible ? 0 : 1;
            return !parkedOnAirframe && inFlightOnAirframe ? 1 : 0;
        }

        /// <summary>
        /// Nose axis (±X or ±Z) of a model from its bounds centre and the nose marker, and the
        /// uniform scale that gives it the real wingspan.
        /// </summary>
        public static void Orientation(Bounds bounds, Vector3 noseMarker, float spanMetres, out Vector3 forward, out float span, out float length,
            out float scale)
        {
            Vector3 d = noseMarker - bounds.center;
            bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
            forward = alongX ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
            length = alongX ? bounds.size.x : bounds.size.z;
            span = alongX ? bounds.size.z : bounds.size.x;
            scale = span > 1e-4f ? spanMetres / span : 1f;
        }

        /// <summary>Bakes, places and returns the display aircraft (missing models are skipped with a warning).</summary>
        public static List<GameObject> Create(Material[] roleMaterials, Material[] paints)
        {
            var created = new List<GameObject>();
            var root = new GameObject("DisplayAircraft").transform;
            for (int i = 0; i < Specs.Length; i++)
            {
                var spec = Specs[i];
                var paint = paints != null && i < paints.Length ? paints[i] : roleMaterials[(int)Role.Paint];
                var go = Place(spec, root, roleMaterials, paint);
                if (go != null) created.Add(go);
            }
            return created;
        }

        static GameObject Place(Spec spec, Transform parent, Material[] roleMaterials, Material paint)
        {
            string path = ModelsFolder + "/" + spec.File;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning($"[OpenSkies] Display aircraft model missing: {path} (Git LFS not fetched?). Skipped.");
                return null;
            }

            var mesh = Bake(model, spec, out bool[] used, out Bounds bounds, out Vector3 nose, out string problem);
            if (mesh == null)
            {
                Debug.LogWarning($"[OpenSkies] {spec.Name}: {problem}. Skipped.");
                return null;
            }
            Orientation(bounds, nose, spec.SpanMetres, out var forward, out float span, out float length, out float scale);
            float proportion = length / Mathf.Max(1e-4f, span);
            float expected = spec.LengthMetres / spec.SpanMetres;
            if (Mathf.Abs(proportion / expected - 1f) > 0.25f)
                Debug.LogWarning($"[OpenSkies] {spec.Name}: length/span {proportion:0.00} differs from the real aircraft's {expected:0.00}; check the scale.");
            mesh = SaveMesh(mesh, spec);

            var go = new GameObject(spec.Name);
            go.transform.SetParent(parent, false);
            var heading = Quaternion.Euler(0f, spec.HeadingDegrees, 0f) * Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up));
            go.transform.localScale = Vector3.one * scale;
            // Standing on the apron: the lowest point (a wheel) on the surface.
            go.transform.position = new Vector3(spec.Position.x, spec.Position.y - bounds.min.y * scale, spec.Position.z);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new List<Material>();
            for (int r = 0; r < RoleCount; r++)
                if (used[r]) materials.Add(r == (int)Role.Paint ? paint : roleMaterials[r]);
            renderer.sharedMaterials = materials.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On;

            // The fuselage stops a taxiing aircraft; the wings do not (no damage model).
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            bool alongX = Mathf.Abs(forward.x) > 0.5f;
            float core = span * 0.16f;
            box.size = alongX ? new Vector3(length, bounds.size.y, core) : new Vector3(core, bounds.size.y, length);

            var lod = go.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.None;
            lod.SetLODs(new[] { new LOD(CullScreenHeight, new Renderer[] { renderer }) });
            lod.RecalculateBounds();
            return go;
        }

        /// <summary>
        /// One mesh in world space (Y up) from every visible renderer of the model, one submesh
        /// per colour role in use. Skinned parts are baked in their rest pose. Returns null with
        /// a reason if nothing can be baked.
        /// </summary>
        public static Mesh Bake(GameObject model, Spec spec, out bool[] used, out Bounds bounds, out Vector3 nose, out string problem)
        {
            used = new bool[RoleCount];
            bounds = default;
            nose = Vector3.zero;
            problem = null;
            var instance = Object.Instantiate(model);
            var temporary = new List<Object>();
            try
            {
                instance.transform.position = Vector3.zero;
                HideAlternatives(instance, spec);

                var groups = new List<CombineInstance>[RoleCount];
                for (int r = 0; r < RoleCount; r++) groups[r] = new List<CombineInstance>();
                bool foundNose = false;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled) continue;
                    Mesh source;
                    Matrix4x4 matrix;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (skinned.sharedMesh == null) continue;
                        source = new Mesh { indexFormat = IndexFormat.UInt32 };
                        skinned.BakeMesh(source, true);
                        temporary.Add(source);
                        var t = skinned.transform;
                        matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                    }
                    else if (renderer is MeshRenderer)
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null) continue;
                        source = filter.sharedMesh;
                        matrix = renderer.transform.localToWorldMatrix;
                    }
                    else continue;

                    if (!foundNose && renderer.name.Trim() == spec.NoseMarker.Trim())
                    {
                        nose = renderer.bounds.center;
                        foundNose = true;
                    }
                    var materials = renderer.sharedMaterials;
                    for (int s = 0; s < source.subMeshCount; s++)
                    {
                        var material = s < materials.Length ? materials[s] : null;
                        var role = RoleOf(material != null ? material.name : null);
                        groups[(int)role].Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = matrix });
                        used[(int)role] = true;
                    }
                }
                if (!foundNose)
                {
                    problem = $"nose marker '{spec.NoseMarker}' not found";
                    return null;
                }

                var parts = new List<CombineInstance>();
                for (int r = 0; r < RoleCount; r++)
                {
                    if (groups[r].Count == 0) continue;
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(groups[r].ToArray(), true, true);
                    temporary.Add(part);
                    parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                }
                if (parts.Count == 0)
                {
                    problem = "no visible meshes";
                    return null;
                }
                var mesh = new Mesh { name = spec.Name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), false, true); // one submesh per role, in role order
                // Flat colours need positions and normals only: drop the rest (smaller asset and build).
                mesh.uv = null;
                mesh.uv2 = null;
                mesh.tangents = null;
                mesh.colors = null;
                mesh.RecalculateBounds();
                bounds = mesh.bounds;
                return mesh;
            }
            finally
            {
                Object.DestroyImmediate(instance);
                foreach (var o in temporary) Object.DestroyImmediate(o);
            }
        }

        /// <summary>Keeps one part of each alternative pair (parked first, unless it is detached from the airframe).</summary>
        static void HideAlternatives(GameObject instance, Spec spec)
        {
            if (spec.Alternatives.Length == 0) return;
            var airframe = Airframe(instance);
            foreach (var pair in spec.Alternatives)
            {
                var parked = Find(instance, pair[0]);
                var inFlight = Find(instance, pair[1]);
                if (parked == null || inFlight == null) continue;
                int keep = ChooseAlternative(parked.enabled, inFlight.enabled, OnAirframe(parked, airframe), OnAirframe(inFlight, airframe));
                (keep == 0 ? inFlight : parked).enabled = false;
                (keep == 0 ? parked : inFlight).enabled = true;
            }
        }

        /// <summary>Bounds of the largest part (the fuselage), slightly enlarged.</summary>
        static Bounds Airframe(GameObject instance)
        {
            Bounds best = default;
            float bestVolume = -1f;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                var b = r.bounds;
                float volume = b.size.x * b.size.y * b.size.z;
                if (volume > bestVolume)
                {
                    bestVolume = volume;
                    best = b;
                }
            }
            best.Expand(best.size * 0.1f);
            return best;
        }

        static bool OnAirframe(Renderer renderer, Bounds airframe) => airframe.Contains(renderer.bounds.center);

        static Renderer Find(GameObject instance, string name)
        {
            foreach (var r in instance.GetComponentsInChildren<Renderer>(false))
                if (r.name.Trim() == name.Trim()) return r;
            return null;
        }

        /// <summary>Saves (or updates in place, keeping its GUID) the baked mesh asset.</summary>
        static Mesh SaveMesh(Mesh mesh, Spec spec)
        {
            EditorFolders.Ensure(MeshesFolder);
            string path = $"{MeshesFolder}/{Path.GetFileNameWithoutExtension(spec.File)}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
            MeshUtility.Optimize(mesh);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = spec.Name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }

    /// <summary>
    /// Import settings for the display aircraft models: geometry and material names only
    /// (no animation, cameras, lights or blend shapes); readable so the scene builder can
    /// bake them. The source meshes are not referenced by the scene, so they do not enter
    /// the build; the baked meshes do.
    /// </summary>
    sealed class DisplayAircraftImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(DisplayAircraft.ModelsFolder + "/", StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = true; // parts the author hid stay hidden
            importer.isReadable = true;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }
    }
}
