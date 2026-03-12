using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Custom inspector for StoryFlowProjectAsset.
    /// Displays project metadata, scripts, global variables, and characters with
    /// quick-access buttons for re-importing and opening the importer window.
    /// </summary>
    [CustomEditor(typeof(StoryFlowProjectAsset))]
    public class StoryFlowProjectAssetEditor : UnityEditor.Editor
    {
        private bool showScripts = true;
        private bool showVariables = true;
        private bool showCharacters = true;
        private Vector2 scriptsScroll;
        private Vector2 variablesScroll;
        private Vector2 charactersScroll;

        // Re-import state
        private string reimportBuildDirectory = "";
        private bool showReimportSection;

        public override void OnInspectorGUI()
        {
            var project = (StoryFlowProjectAsset)target;

            // --- Metadata ---
            EditorGUILayout.LabelField("Project Metadata", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.TextField("Title", project.Title ?? "(untitled)");
            EditorGUILayout.TextField("Version", project.Version ?? "");
            EditorGUILayout.TextField("API Version", project.ApiVersion ?? "");

            if (!string.IsNullOrEmpty(project.Description))
            {
                EditorGUILayout.LabelField("Description:");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(project.Description, EditorStyles.wordWrappedLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.TextField("Startup Script", project.StartupScriptPath ?? "(none)");

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // --- Scripts ---
            showScripts = EditorGUILayout.Foldout(showScripts,
                $"Scripts ({project.ScriptReferences?.Count ?? 0})", true, EditorStyles.foldoutHeader);

            if (showScripts && project.ScriptReferences != null && project.ScriptReferences.Count > 0)
            {
                EditorGUI.indentLevel++;

                int maxVisible = Mathf.Min(project.ScriptReferences.Count, 15);
                bool needsScroll = project.ScriptReferences.Count > maxVisible;

                if (needsScroll)
                    scriptsScroll = EditorGUILayout.BeginScrollView(scriptsScroll,
                        GUILayout.MaxHeight(maxVisible * EditorGUIUtility.singleLineHeight + 8));

                foreach (var scriptRef in project.ScriptReferences)
                {
                    EditorGUILayout.BeginHorizontal();

                    EditorGUILayout.LabelField(scriptRef.Path ?? "(unknown)", GUILayout.MinWidth(100));

                    if (scriptRef.Asset != null)
                    {
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            Selection.activeObject = scriptRef.Asset;
                            EditorGUIUtility.PingObject(scriptRef.Asset);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("(missing)", EditorStyles.miniLabel, GUILayout.Width(50));
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (needsScroll)
                    EditorGUILayout.EndScrollView();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // --- Global Variables ---
            showVariables = EditorGUILayout.Foldout(showVariables,
                $"Global Variables ({project.GlobalVariableEntries?.Count ?? 0})", true, EditorStyles.foldoutHeader);

            if (showVariables && project.GlobalVariableEntries != null && project.GlobalVariableEntries.Count > 0)
            {
                EditorGUI.indentLevel++;

                int maxVisible = Mathf.Min(project.GlobalVariableEntries.Count, 15);
                bool needsScroll = project.GlobalVariableEntries.Count > maxVisible;

                if (needsScroll)
                    variablesScroll = EditorGUILayout.BeginScrollView(variablesScroll,
                        GUILayout.MaxHeight(maxVisible * (EditorGUIUtility.singleLineHeight * 2) + 8));

                foreach (var entry in project.GlobalVariableEntries)
                {
                    EditorGUILayout.BeginHorizontal();

                    string typeLabel = entry.IsArray ? $"{entry.Type}[]" : entry.Type.ToString();
                    EditorGUILayout.LabelField(entry.Name ?? entry.Id, GUILayout.MinWidth(80));
                    EditorGUILayout.LabelField(typeLabel, EditorStyles.miniLabel, GUILayout.Width(70));
                    EditorGUILayout.LabelField(FormatVariableValue(entry), EditorStyles.miniLabel, GUILayout.MinWidth(60));

                    EditorGUILayout.EndHorizontal();
                }

                if (needsScroll)
                    EditorGUILayout.EndScrollView();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // --- Characters ---
            showCharacters = EditorGUILayout.Foldout(showCharacters,
                $"Characters ({project.CharacterReferences?.Count ?? 0})", true, EditorStyles.foldoutHeader);

            if (showCharacters && project.CharacterReferences != null && project.CharacterReferences.Count > 0)
            {
                EditorGUI.indentLevel++;

                int maxVisible = Mathf.Min(project.CharacterReferences.Count, 15);
                bool needsScroll = project.CharacterReferences.Count > maxVisible;

                if (needsScroll)
                    charactersScroll = EditorGUILayout.BeginScrollView(charactersScroll,
                        GUILayout.MaxHeight(maxVisible * EditorGUIUtility.singleLineHeight + 8));

                foreach (var charRef in project.CharacterReferences)
                {
                    EditorGUILayout.BeginHorizontal();

                    string displayName = charRef.Asset != null
                        ? charRef.Asset.CharacterName ?? charRef.Path
                        : charRef.Path ?? "(unknown)";

                    EditorGUILayout.LabelField(displayName, GUILayout.MinWidth(100));

                    if (charRef.Asset != null)
                    {
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            Selection.activeObject = charRef.Asset;
                            EditorGUIUtility.PingObject(charRef.Asset);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("(missing)", EditorStyles.miniLabel, GUILayout.Width(50));
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (needsScroll)
                    EditorGUILayout.EndScrollView();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8);

            // --- Global Strings ---
            if (project.GlobalStringEntries != null && project.GlobalStringEntries.Count > 0)
            {
                EditorGUILayout.LabelField($"Global Strings: {project.GlobalStringEntries.Count}",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(4);
            }

            // --- Actions ---
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            // Re-import section
            showReimportSection = EditorGUILayout.Foldout(showReimportSection, "Re-Import from Source", true);
            if (showReimportSection)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                reimportBuildDirectory = EditorGUILayout.TextField(reimportBuildDirectory);
                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    string selected = EditorUtility.OpenFolderPanel(
                        "Select StoryFlow Build Directory", reimportBuildDirectory, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        reimportBuildDirectory = selected;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(reimportBuildDirectory))
                {
                    string projectJsonPath = System.IO.Path.Combine(reimportBuildDirectory, "project.json");
                    if (!System.IO.File.Exists(projectJsonPath))
                    {
                        EditorGUILayout.HelpBox(
                            "project.json not found in the selected directory.",
                            MessageType.Warning);
                    }
                }

                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(reimportBuildDirectory));
                if (GUILayout.Button("Re-Import", GUILayout.Height(24)))
                {
                    PerformReimport(project);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Open Import Window", GUILayout.Height(28)))
            {
                StoryFlowEditorHelpers.OpenImporterWindow();
            }

            EditorGUILayout.Space(4);

            // --- Default Inspector (collapsed) ---
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Raw Serialized Data", EditorStyles.boldLabel);
            DrawDefaultInspector();
        }

        private void PerformReimport(StoryFlowProjectAsset project)
        {
            string outputPath = StoryFlowEditorHelpers.GetAssetFolderPath(project);
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = "Assets/StoryFlow";
            }

            try
            {
                EditorUtility.DisplayProgressBar("StoryFlow Re-Import", "Re-importing project...", 0.1f);

                var reimported = StoryFlowImporter.ImportProject(reimportBuildDirectory, outputPath);

                EditorUtility.DisplayProgressBar("StoryFlow Re-Import", "Finalizing...", 0.9f);

                EditorGUIUtility.PingObject(reimported);
                Selection.activeObject = reimported;

                Debug.Log($"[StoryFlow] Re-import successful: {reimported.Title} " +
                          $"({reimported.ScriptReferences.Count} scripts, " +
                          $"{reimported.CharacterReferences.Count} characters, " +
                          $"{reimported.GlobalVariableEntries.Count} global variables)");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Re-Import Failed",
                    $"Failed to re-import project:\n{ex.Message}", "OK");
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string FormatVariableValue(StoryFlowProjectAsset.GlobalVariableEntry entry)
        {
            if (string.IsNullOrEmpty(entry.DefaultValueJson))
                return "(default)";

            return entry.Type switch
            {
                StoryFlowVariableType.Boolean => entry.DefaultValueJson == "true" || entry.DefaultValueJson == "1"
                    ? "true"
                    : "false",
                StoryFlowVariableType.Integer => entry.DefaultValueJson,
                StoryFlowVariableType.Float => entry.DefaultValueJson,
                StoryFlowVariableType.String => $"\"{TruncateString(entry.DefaultValueJson, 30)}\"",
                StoryFlowVariableType.Enum => entry.DefaultValueJson,
                _ => entry.DefaultValueJson
            };
        }

        private static string TruncateString(string value, int maxLength)
        {
            if (value == null) return "";
            return value.Length > maxLength ? value.Substring(0, maxLength) + "..." : value;
        }
    }
}
