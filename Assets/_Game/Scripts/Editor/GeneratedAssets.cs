using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Writes the generated textures and meshes as project assets. Textures are
    /// PNG files created once (delete the file to regenerate it from new code);
    /// meshes are updated in place so their GUIDs, and the scene's references to
    /// them, never change.
    /// </summary>
    public static class GeneratedAssets
    {
        /// <summary>Kind of generated texture: it decides the importer settings.</summary>
        public enum TextureKind
        {
            /// <summary>Colour, sRGB, tiling.</summary>
            Albedo,
            /// <summary>Colour with transparency, sRGB, tiling.</summary>
            AlbedoAlpha,
            /// <summary>Tangent-space normal map (linear), tiling.</summary>
            Normal,
            /// <summary>Colour, sRGB, clamped (an atlas or a single-use map).</summary>
            AlbedoClamped,
            /// <summary>Colour with transparency, sRGB, clamped.</summary>
            AlbedoAlphaClamped,
            /// <summary>A tiny colour palette: clamped, no mipmaps, nearest filtering, uncompressed.</summary>
            Palette,
        }

        /// <summary>
        /// Returns the texture at <paramref name="path"/>, creating it from <paramref name="pixels"/> when
        /// missing. A failure is logged and returns null (materials then fall back to their flat colour),
        /// so one bad texture never stops the scene build.
        /// </summary>
        public static Texture2D LoadOrCreateTexture(string path, int width, int height, Func<Color32[]> pixels, TextureKind kind, int maxSize = 2048)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            try
            {
                return CreateTexture(path, width, height, pixels, kind, maxSize);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[OpenSkies] Could not generate {path}; its material keeps a flat colour.");
                return null;
            }
        }

        static Texture2D CreateTexture(string path, int width, int height, Func<Color32[]> pixels, TextureKind kind, int maxSize)
        {
            EditorFolders.Ensure(Path.GetDirectoryName(path).Replace('\\', '/'));
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels());
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                bool palette = kind == TextureKind.Palette;
                bool clamped = palette || kind == TextureKind.AlbedoClamped || kind == TextureKind.AlbedoAlphaClamped;
                bool alpha = kind == TextureKind.AlbedoAlpha || kind == TextureKind.AlbedoAlphaClamped;
                importer.textureType = kind == TextureKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.wrapMode = clamped ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.mipmapEnabled = !palette;
                importer.filterMode = palette ? FilterMode.Point : FilterMode.Bilinear;
                importer.anisoLevel = palette ? 0 : 4;
                importer.maxTextureSize = maxSize;
                importer.sRGBTexture = kind != TextureKind.Normal;
                importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = alpha;
                importer.textureCompression = palette ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            Debug.Log($"[OpenSkies] Created {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Saves a mesh as an asset, or copies it over the existing asset at the path (same GUID).</summary>
        public static Mesh SaveMesh(Mesh mesh, string path, bool compress = true)
        {
            EditorFolders.Ensure(Path.GetDirectoryName(path).Replace('\\', '/'));
            if (compress) MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
            MeshUtility.Optimize(mesh);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            string name = mesh.name;
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Builds the mesh and saves it (see <see cref="SaveMesh"/>).</summary>
        public static Mesh SaveMesh(MeshData data, string name, string path, bool compress = true) => SaveMesh(data.ToMesh(name), path, compress);

        /// <summary>Tangent-space normal map pixels from a height field (0..1 values, tiling), for a texel of <paramref name="texelMetres"/>.</summary>
        public static Color32[] NormalMapFromHeights(float[] heights, int width, int height, float heightScaleMetres, float texelMetres, bool tile) =>
            NormalMapFromHeights(heights, width, height, heightScaleMetres, texelMetres, texelMetres, tile);

        /// <summary>As above, for texels of different width (x) and height (y) in metres.</summary>
        public static Color32[] NormalMapFromHeights(float[] heights, int width, int height, float heightScaleMetres, float texelX, float texelY, bool tile)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float left = Sample(heights, width, height, x - 1, y, tile), right = Sample(heights, width, height, x + 1, y, tile);
                float down = Sample(heights, width, height, x, y - 1, tile), up = Sample(heights, width, height, x, y + 1, tile);
                float dx = (right - left) * heightScaleMetres / (2f * texelX);
                float dy = (up - down) * heightScaleMetres / (2f * texelY);
                var n = new Vector3(-dx, -dy, 1f).normalized;
                pixels[y * width + x] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            return pixels;
        }

        static float Sample(float[] heights, int width, int height, int x, int y, bool tile)
        {
            if (tile)
            {
                x = ((x % width) + width) % width;
                y = ((y % height) + height) % height;
            }
            else
            {
                x = Mathf.Clamp(x, 0, width - 1);
                y = Mathf.Clamp(y, 0, height - 1);
            }
            return heights[y * width + x];
        }
    }
}
