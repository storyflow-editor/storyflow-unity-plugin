using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Integrates StoryFlowSettings into Unity's Project Settings window
    /// (Edit > Project Settings > StoryFlow).
    /// </summary>
    static class StoryFlowSettingsProvider
    {
        private const string SettingsPath = "Project/StoryFlow";
        private const string ResourcesFolder = "Assets/Resources";
        private const string SettingsAssetPath = "Assets/Resources/StoryFlowSettings.asset";

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "StoryFlow",
                keywords = new[] { "StoryFlow", "Dialogue", "Story", "Visual Novel" },

                guiHandler = DrawSettingsGUI,
                activateHandler = (searchContext, rootElement) =>
                {
                    // Ensure settings asset exists when the settings page is opened
                    GetOrCreateSettings();
                }
            };

            return provider;
        }

        private static void DrawSettingsGUI(string searchContext)
        {
            var settings = GetOrCreateSettings();
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find or create the StoryFlowSettings asset.\n" +
                    "Try creating one manually: Assets > Create > StoryFlow > Settings",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.Space(8);

            // Use a SerializedObject so changes are tracked by Undo and saved properly
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.Update();

            // --- Default Project ---
            EditorGUILayout.LabelField("Default Project", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var defaultProjectProp = serializedSettings.FindProperty("DefaultProject");
            EditorGUILayout.PropertyField(defaultProjectProp,
                new GUIContent("Project Asset",
                    "Default project asset to auto-load. Leave empty for manual loading."));

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // --- Import ---
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var importPathProp = serializedSettings.FindProperty("DefaultImportPath");
            EditorGUILayout.PropertyField(importPathProp,
                new GUIContent("Default Import Path",
                    "Default output path for imported assets (relative to Assets/)."));

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // --- Debug ---
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var verboseProp = serializedSettings.FindProperty("VerboseLogging");
            EditorGUILayout.PropertyField(verboseProp,
                new GUIContent("Verbose Logging",
                    "Enable verbose logging for node execution."));

            var logVarProp = serializedSettings.FindProperty("LogVariableChanges");
            EditorGUILayout.PropertyField(logVarProp,
                new GUIContent("Log Variable Changes",
                    "Log variable changes at runtime."));

            EditorGUI.indentLevel--;

            if (serializedSettings.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(settings);
            }

            EditorGUILayout.Space(16);

            // --- Utility buttons ---
            EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);

            if (GUILayout.Button("Open Import Window", GUILayout.Height(24)))
            {
                EditorWindow.GetWindow<StoryFlowImporterWindow>("StoryFlow Importer");
            }

            if (GUILayout.Button("Open Live Sync Window", GUILayout.Height(24)))
            {
                EditorWindow.GetWindow<StoryFlowLiveSyncServer>("StoryFlow Live Sync");
            }
        }

        /// <summary>
        /// Finds the existing StoryFlowSettings asset in Resources, or creates one if missing.
        /// </summary>
        private static StoryFlowSettings GetOrCreateSettings()
        {
            // Try loading from Resources first (matches StoryFlowSettings.Instance)
            var settings = Resources.Load<StoryFlowSettings>("StoryFlowSettings");
            if (settings != null)
                return settings;

            // Try loading from the known asset path
            settings = AssetDatabase.LoadAssetAtPath<StoryFlowSettings>(SettingsAssetPath);
            if (settings != null)
                return settings;

            // Create the asset
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            settings = ScriptableObject.CreateInstance<StoryFlowSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[StoryFlow] Created StoryFlowSettings asset at: " + SettingsAssetPath);
            return settings;
        }
    }
}
