using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// EditorWindow for importing StoryFlow projects from a build directory.
    /// Accessible via the menu: StoryFlow > Import Project.
    /// </summary>
    public class StoryFlowImporterWindow : EditorWindow
    {
        [MenuItem("StoryFlow/Import Project")]
        private static void ShowWindow()
        {
            var window = GetWindow<StoryFlowImporterWindow>("StoryFlow Importer");
            window.minSize = new Vector2(400, 250);
        }

        private string buildDirectory = "";
        private string outputPath = "Assets/StoryFlow";
        private Vector2 scrollPos;
        private string statusMessage = "";
        private MessageType statusType = MessageType.None;
        private bool isImporting;

        private new void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // --- Header ---
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("StoryFlow Project Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Select the build directory exported from StoryFlow Editor " +
                "(the folder containing project.json) and choose where to place the imported assets.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            // --- Build directory ---
            EditorGUILayout.LabelField("Build Directory", EditorStyles.label);
            EditorGUILayout.BeginHorizontal();
            {
                buildDirectory = EditorGUILayout.TextField(buildDirectory);

                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    string selected = EditorUtility.OpenFolderPanel(
                        "Select StoryFlow Build Directory", buildDirectory, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        buildDirectory = selected;
                        GUI.FocusControl(null);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Validation hint
            if (!string.IsNullOrEmpty(buildDirectory))
            {
                string projectJsonPath = System.IO.Path.Combine(buildDirectory, "project.json");
                if (!System.IO.File.Exists(projectJsonPath))
                {
                    EditorGUILayout.HelpBox(
                        "project.json not found in the selected directory. " +
                        "Make sure you select the build output folder.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.Space(4);

            // --- Output path ---
            EditorGUILayout.LabelField("Output Path (relative to Assets/)", EditorStyles.label);
            outputPath = EditorGUILayout.TextField(outputPath);

            EditorGUILayout.Space(12);

            // --- Import button ---
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(buildDirectory) || isImporting);
            {
                if (GUILayout.Button(isImporting ? "Importing..." : "Import Project", GUILayout.Height(32)))
                {
                    PerformImport();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);

            // --- Status message ---
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            EditorGUILayout.EndScrollView();
        }

        private void PerformImport()
        {
            isImporting = true;
            statusMessage = "";
            statusType = MessageType.None;

            try
            {
                EditorUtility.DisplayProgressBar("StoryFlow Import", "Importing project...", 0.1f);

                var projectAsset = StoryFlowImporter.ImportProject(buildDirectory, outputPath);

                EditorUtility.DisplayProgressBar("StoryFlow Import", "Finalizing...", 0.9f);

                statusMessage = $"Import successful!\n\n" +
                                $"Project: {projectAsset.Title}\n" +
                                $"Scripts: {projectAsset.ScriptReferences.Count}\n" +
                                $"Characters: {projectAsset.CharacterReferences.Count}\n" +
                                $"Global Variables: {projectAsset.GlobalVariableEntries.Count}\n\n" +
                                $"Assets created in: {outputPath}";
                statusType = MessageType.Info;

                // Ping the created asset in the Project window
                EditorGUIUtility.PingObject(projectAsset);
                Selection.activeObject = projectAsset;
            }
            catch (System.Exception ex)
            {
                statusMessage = $"Import failed:\n{ex.Message}";
                statusType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                isImporting = false;
            }
        }
    }
}
