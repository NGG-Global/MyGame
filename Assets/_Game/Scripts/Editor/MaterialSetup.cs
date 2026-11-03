using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Sets up URP Lit and Unlit materials from code the way the shader GUI would:
    /// surface type and blending, keywords for maps and emission, detail maps.
    /// Every call marks the material dirty so the scene build saves it.
    /// </summary>
    public static class MaterialSetup
    {
        /// <summary>Alpha-blended transparency (URP Lit or Unlit), optionally drawn from both sides.</summary>
        public static void MakeTransparent(Material material, bool twoSided = false, bool receiveShadows = false)
        {
            material.SetFloat("_Surface", 1f);     // transparent
            material.SetFloat("_Blend", 0f);       // alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            material.SetFloat("_ReceiveShadows", receiveShadows ? 1f : 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (!receiveShadows) material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        /// <summary>Opaque, optionally drawn from both sides (thin leaves, flags).</summary>
        public static void MakeOpaque(Material material, bool twoSided = false)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
        }

        /// <summary>Alpha-tested cut-out (opaque with holes): foliage cards, fences.</summary>
        public static void MakeCutout(Material material, float cutoff = 0.5f, bool twoSided = true)
        {
            MakeOpaque(material, twoSided);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", cutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
        }

        public static void SetBaseMap(Material material, Texture2D map, Vector2 tiling)
        {
            material.SetTexture("_BaseMap", map);
            material.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(material);
        }

        public static void SetNormalMap(Material material, Texture2D map, float strength)
        {
            material.SetTexture("_BumpMap", map);
            material.SetFloat("_BumpScale", strength);
            if (map != null) material.EnableKeyword("_NORMALMAP");
            else material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }

        /// <summary>Detail albedo (multiply x2) and normal at their own tiling (both share the detail tiling).</summary>
        public static void SetDetail(Material material, Texture2D albedo, Texture2D normal, Vector2 tiling, float albedoScale = 1f, float normalScale = 1f)
        {
            material.SetTexture("_DetailAlbedoMap", albedo);
            material.SetTexture("_DetailNormalMap", normal);
            material.SetTextureScale("_DetailAlbedoMap", tiling);
            material.SetFloat("_DetailAlbedoMapScale", albedoScale);
            material.SetFloat("_DetailNormalMapScale", normalScale);
            if (albedo != null || normal != null) material.EnableKeyword("_DETAIL_MULX2");
            else material.DisableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(material);
        }

        /// <summary>Emissive colour (HDR); the material glows on its own and feeds bloom.</summary>
        public static void SetEmission(Material material, Color colour)
        {
            material.SetColor("_EmissionColor", colour);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
        }

        public static void SetSurface(Material material, float smoothness, float metallic, bool specularHighlights = true, bool environmentReflections = true)
        {
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_SpecularHighlights", specularHighlights ? 1f : 0f);
            material.SetFloat("_EnvironmentReflections", environmentReflections ? 1f : 0f);
            if (specularHighlights) material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (environmentReflections) material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            else material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(material);
        }
    }
}
