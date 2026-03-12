using System.IO;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Watches for .json files being imported or modified in the Unity project.
    /// When a StoryFlow-specific JSON file is detected (containing keys like
    /// "scripts", "globalVariables", "startNode", etc.), offers to re-import
    /// the project or auto-reimports if the setting is enabled.
    /// </summary>
    public class StoryFlowAssetPostprocessor : AssetPostprocessor
    {
        // EditorPrefs key for auto-reimport toggle
        private const string AutoReimportPrefKey = "StoryFlow_AutoReimportOnJsonChange";
        private const string WatchFolderPrefKey = "StoryFlow_WatchFolder";
        private const string BuildDirectoryPrefKey = "StoryFlow_LastBuildDirectory";

        /// <summary>
        /// Whether to automatically re-import when a StoryFlow JSON file changes.
        /// </summary>
        public static bool AutoReimportEnabled
        {
            get => EditorPrefs.GetBool(AutoReimportPrefKey, false);
            set => EditorPrefs.SetBool(AutoReimportPrefKey, value);
        }

        /// <summary>
        /// Optional folder filter. Only JSON files within this Assets-relative path
        /// will trigger the postprocessor. Empty means watch all folders.
        /// </summary>
        public static string WatchFolder
        {
            get => EditorPrefs.GetString(WatchFolderPrefKey, "");
            set => EditorPrefs.SetString(WatchFolderPrefKey, value);
        }

        /// <summary>
        /// The last build directory used for import. Stored so re-import can
        /// re-use it without prompting.
        /// </summary>
        public static string LastBuildDirectory
        {
            get => EditorPrefs.GetString(BuildDirectoryPrefKey, "");
            set => EditorPrefs.SetString(BuildDirectoryPrefKey, value);
        }

        /// <summary>
        /// Called by Unity after assets are imported, deleted, or moved.
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Only check imported/changed assets
            if (importedAssets == null || importedAssets.Length == 0)
                return;

            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check watch folder filter
                string watchFolder = WatchFolder;
                if (!string.IsNullOrEmpty(watchFolder) && !assetPath.StartsWith(watchFolder))
                    continue;

                if (!CouldBeStoryFlowFile(assetPath))
                    continue;

                if (!IsStoryFlowJson(assetPath))
                    continue;

                HandleStoryFlowJsonDetected(assetPath);
                // Only handle once per batch (the project.json detection is enough
                // to trigger a full re-import)
                break;
            }
        }

        /// <summary>
        /// Fast filename/path check to skip JSON files that are obviously not StoryFlow exports.
        /// Avoids the expensive read-and-parse in <see cref="IsStoryFlowJson"/> for unrelated JSON.
        /// </summary>
        private static bool CouldBeStoryFlowFile(string path)
        {
            string fileName = System.IO.Path.GetFileName(path);

            // Known StoryFlow export filenames
            if (fileName == "project.json" || fileName == "global-variables.json" || fileName == "characters.json")
                return true;

            // Files in a build/ directory (StoryFlow editor exports here)
            if (path.Replace("\\", "/").Contains("/build/"))
                return true;

            // Files in a StoryFlow output directory
            if (path.Replace("\\", "/").Contains("/StoryFlow/"))
                return true;

            return false;
        }

        /// <summary>
        /// Checks whether a JSON file at the given asset path contains StoryFlow-specific keys.
        /// </summary>
        private static bool IsStoryFlowJson(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;

            try
            {
                string content = File.ReadAllText(fullPath);
                if (string.IsNullOrWhiteSpace(content))
                    return false;

                var json = JObject.Parse(content);

                // project.json pattern: has "version" + "apiVersion" or "startupScript"
                if (json.ContainsKey("apiVersion") &&
                    (json.ContainsKey("startupScript") || json.ContainsKey("version")))
                    return true;

                // global-variables.json pattern: has "variables" at root
                if (json.ContainsKey("variables") && json.ContainsKey("strings"))
                    return true;

                // characters.json pattern: array of character objects
                // (root is an array, not an object, so JObject.Parse would fail;
                // catch it if the file starts with '[')
                if (content.TrimStart().StartsWith("["))
                    return false; // Skip arrays; they may or may not be StoryFlow

                // Script .json pattern: has "startNode" and "nodes"
                if (json.ContainsKey("startNode") && json.ContainsKey("nodes"))
                    return true;

                // Script .json alternate: has "nodes" and "edges"/"connections"
                if (json.ContainsKey("nodes") &&
                    (json.ContainsKey("edges") || json.ContainsKey("connections")))
                    return true;

                return false;
            }
            catch (System.Exception)
            {
                // Not valid JSON or parse error; skip silently
                return false;
            }
        }

        /// <summary>
        /// Handles detection of a StoryFlow JSON file in the project.
        /// Either auto-reimports or prompts the user.
        /// </summary>
        private static void HandleStoryFlowJsonDetected(string assetPath)
        {
            if (AutoReimportEnabled)
            {
                TryAutoReimport(assetPath);
            }
            else
            {
                // Prompt user
                int choice = EditorUtility.DisplayDialogComplex(
                    "StoryFlow JSON Detected",
                    $"A StoryFlow JSON file was detected:\n{assetPath}\n\n" +
                    "Would you like to open the import window to re-import the project?",
                    "Open Import Window",
                    "Ignore",
                    "Ignore (Don't ask again for this session)");

                switch (choice)
                {
                    case 0: // Open Import Window
                        StoryFlowEditorHelpers.OpenImporterWindow();
                        break;
                    case 1: // Ignore
                        break;
                    case 2: // Don't ask again
                        // We cannot permanently suppress without a pref, so just skip
                        Debug.Log("[StoryFlow] Suppressed StoryFlow JSON detection for this editor session. " +
                                  "Enable auto-reimport in Project Settings > StoryFlow if you want automatic imports.");
                        break;
                }
            }
        }

        /// <summary>
        /// Attempts to auto-reimport by finding the build directory from the detected file's location
        /// or from the last known build directory.
        /// </summary>
        private static void TryAutoReimport(string assetPath)
        {
            // Try to find the build directory: look for project.json in the same folder
            string directory = Path.GetDirectoryName(Path.GetFullPath(assetPath));
            string buildDir = FindBuildDirectory(directory);

            if (string.IsNullOrEmpty(buildDir))
            {
                // Fall back to last known build directory
                buildDir = LastBuildDirectory;
            }

            if (string.IsNullOrEmpty(buildDir) || !Directory.Exists(buildDir))
            {
                Debug.LogWarning("[StoryFlow] Auto-reimport: Could not determine build directory. " +
                                 "Open the import window manually.");
                StoryFlowEditorHelpers.OpenImporterWindow();
                return;
            }

            // Determine output path from existing project asset or settings
            string outputPath = DetermineOutputPath();

            try
            {
                EditorUtility.DisplayProgressBar("StoryFlow Auto Re-Import", "Re-importing project...", 0.1f);

                var projectAsset = StoryFlowImporter.ImportProject(buildDir, outputPath);

                LastBuildDirectory = buildDir;

                Debug.Log($"[StoryFlow] Auto re-import successful: {projectAsset.Title} " +
                          $"({projectAsset.ScriptReferences.Count} scripts, " +
                          $"{projectAsset.CharacterReferences.Count} characters, " +
                          $"{projectAsset.GlobalVariableEntries.Count} global variables)");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[StoryFlow] Auto re-import failed: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Walks up from a directory looking for one that contains project.json.
        /// </summary>
        private static string FindBuildDirectory(string startDirectory)
        {
            string current = startDirectory;
            int maxDepth = 5;

            while (!string.IsNullOrEmpty(current) && maxDepth > 0)
            {
                string projectJsonPath = Path.Combine(current, "project.json");
                if (File.Exists(projectJsonPath))
                    return current;

                current = Path.GetDirectoryName(current);
                maxDepth--;
            }

            return null;
        }

        /// <summary>
        /// Determines the output path for auto-reimport by checking existing project assets
        /// and settings.
        /// </summary>
        private static string DetermineOutputPath()
        {
            // Try to find an existing project asset and use its folder
            var existingProject = StoryFlowEditorHelpers.FindProjectAsset();
            if (existingProject != null)
            {
                string folder = StoryFlowEditorHelpers.GetAssetFolderPath(existingProject);
                if (!string.IsNullOrEmpty(folder))
                    return folder;
            }

            // Fall back to settings default
            var settings = StoryFlowEditorHelpers.FindSettings();
            if (settings != null && !string.IsNullOrEmpty(settings.DefaultImportPath))
                return "Assets/" + settings.DefaultImportPath;

            return "Assets/StoryFlow";
        }
    }
}
