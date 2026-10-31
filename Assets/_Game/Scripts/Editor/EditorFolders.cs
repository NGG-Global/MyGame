using UnityEditor;

namespace OpenSkies.Editor
{
    /// <summary>
    /// Creates asset folders through the AssetDatabase, so assets can be created in
    /// them straight away (a folder made only on disk is not yet known to Unity).
    /// </summary>
    public static class EditorFolders
    {
        /// <param name="assetPath">Folder path starting with "Assets", forward slashes.</param>
        public static void Ensure(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            int slash = assetPath.LastIndexOf('/');
            if (slash <= 0) return; // "Assets" itself always exists
            string parent = assetPath.Substring(0, slash);
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, assetPath.Substring(slash + 1));
        }
    }
}
