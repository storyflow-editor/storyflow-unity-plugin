using System.IO;
using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Static utility methods shared across StoryFlow editor scripts.
    /// Provides helpers for finding assets, ensuring folder structure,
    /// and opening editor windows.
    /// </summary>
    public static class StoryFlowEditorHelpers
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string SettingsAssetPath = "Assets/Resources/StoryFlowSettings.asset";

        #region Find Assets

        /// <summary>
        /// Finds the first StoryFlowProjectAsset in the project.
        /// Returns null if none exists.
        /// </summary>
        public static StoryFlowProjectAsset FindProjectAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:StoryFlowProjectAsset");
            if (guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(path);
        }

        /// <summary>
        /// Finds all StoryFlowProjectAsset instances in the project.
        /// </summary>
        public static StoryFlowProjectAsset[] FindAllProjectAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:StoryFlowProjectAsset");
            var assets = new StoryFlowProjectAsset[guids.Length];

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                assets[i] = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(path);
            }

            return assets;
        }

        /// <summary>
        /// Finds the StoryFlowSettings asset. Uses Resources.Load first (matching
        /// the runtime singleton pattern), then falls back to AssetDatabase search.
        /// Returns null if none exists.
        /// </summary>
        public static StoryFlowSettings FindSettings()
        {
            // Match the runtime singleton path first
            var settings = Resources.Load<StoryFlowSettings>("StoryFlowSettings");
            if (settings != null)
                return settings;

            // Fall back to asset path
            settings = AssetDatabase.LoadAssetAtPath<StoryFlowSettings>(SettingsAssetPath);
            if (settings != null)
                return settings;

            // Search the entire project
            string[] guids = AssetDatabase.FindAssets("t:StoryFlowSettings");
            if (guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<StoryFlowSettings>(path);
        }

        /// <summary>
        /// Finds or creates the StoryFlowSettings asset.
        /// Creates it at Assets/Resources/StoryFlowSettings.asset if missing.
        /// </summary>
        public static StoryFlowSettings FindOrCreateSettings()
        {
            var settings = FindSettings();
            if (settings != null)
                return settings;

            // Create the asset
            EnsureFolderExists(ResourcesFolder);

            settings = ScriptableObject.CreateInstance<StoryFlowSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[StoryFlow] Created StoryFlowSettings asset at: " + SettingsAssetPath);
            return settings;
        }

        /// <summary>
        /// Finds a StoryFlowScriptAsset by its script path within a project.
        /// </summary>
        public static StoryFlowScriptAsset FindScriptAsset(string scriptPath)
        {
            string[] guids = AssetDatabase.FindAssets("t:StoryFlowScriptAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<StoryFlowScriptAsset>(path);
                if (asset != null && asset.ScriptPath == scriptPath)
                    return asset;
            }

            return null;
        }

        /// <summary>
        /// Finds a StoryFlowCharacterAsset by character name.
        /// </summary>
        public static StoryFlowCharacterAsset FindCharacterAsset(string characterName)
        {
            string[] guids = AssetDatabase.FindAssets("t:StoryFlowCharacterAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<StoryFlowCharacterAsset>(path);
                if (asset != null && asset.CharacterName == characterName)
                    return asset;
            }

            return null;
        }

        #endregion

        #region Asset Paths

        /// <summary>
        /// Gets the Assets-relative folder path containing the given Unity Object.
        /// Returns null if the object is not a persistent asset.
        /// </summary>
        public static string GetAssetFolderPath(Object asset)
        {
            if (asset == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            return Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        }

        /// <summary>
        /// Gets the full absolute path of the folder containing the given asset.
        /// </summary>
        public static string GetAssetAbsoluteFolderPath(Object asset)
        {
            string relativePath = GetAssetFolderPath(asset);
            if (string.IsNullOrEmpty(relativePath))
                return null;

            return Path.GetFullPath(relativePath);
        }

        /// <summary>
        /// Returns the default StoryFlow output path, pulling from settings if available.
        /// </summary>
        public static string GetDefaultOutputPath()
        {
            var settings = FindSettings();
            if (settings != null && !string.IsNullOrEmpty(settings.DefaultImportPath))
                return "Assets/" + settings.DefaultImportPath;

            return "Assets/StoryFlow";
        }

        #endregion

        #region Folder Management

        /// <summary>
        /// Ensures that a folder path exists in the Asset Database,
        /// creating intermediate directories as needed.
        /// </summary>
        /// <param name="folderPath">
        /// Assets-relative path, e.g. "Assets/StoryFlow/Scripts".
        /// </param>
        public static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            // Normalize separators
            folderPath = folderPath.Replace('\\', '/');

            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            // Split into parts and create incrementally
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // Should be "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        /// <summary>
        /// Ensures the standard StoryFlow folder structure exists within a given base path.
        /// Creates: Scripts/, Characters/, Media/Images/, Media/Audio/ subdirectories.
        /// </summary>
        /// <param name="basePath">
        /// Assets-relative base path, e.g. "Assets/StoryFlow".
        /// </param>
        public static void EnsureStoryFlowFolderStructure(string basePath)
        {
            EnsureFolderExists(basePath);
            EnsureFolderExists(basePath + "/Scripts");
            EnsureFolderExists(basePath + "/Characters");
            EnsureFolderExists(basePath + "/Media");
            EnsureFolderExists(basePath + "/Media/Images");
            EnsureFolderExists(basePath + "/Media/Audio");
        }

        #endregion

        #region Editor Windows

        /// <summary>
        /// Opens the StoryFlow Importer window.
        /// </summary>
        public static void OpenImporterWindow()
        {
            EditorWindow.GetWindow<StoryFlowImporterWindow>("StoryFlow Importer");
        }

        /// <summary>
        /// Opens the StoryFlow Live Sync window.
        /// </summary>
        public static void OpenLiveSyncWindow()
        {
            EditorWindow.GetWindow<StoryFlowLiveSyncServer>("StoryFlow Live Sync");
        }

        /// <summary>
        /// Opens the Unity Project Settings window focused on the StoryFlow section.
        /// </summary>
        public static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings("Project/StoryFlow");
        }

        #endregion

        #region Selection Helpers

        /// <summary>
        /// Selects and pings an asset in the Unity Project window.
        /// </summary>
        public static void SelectAndPing(Object asset)
        {
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Selects a StoryFlowProjectAsset in the Project window.
        /// If no specific asset is given, finds the first one in the project.
        /// </summary>
        public static void SelectProjectAsset(StoryFlowProjectAsset asset = null)
        {
            asset ??= FindProjectAsset();
            if (asset != null)
                SelectAndPing(asset);
            else
                Debug.LogWarning("[StoryFlow] No StoryFlowProjectAsset found in the project.");
        }

        #endregion
    }
}
