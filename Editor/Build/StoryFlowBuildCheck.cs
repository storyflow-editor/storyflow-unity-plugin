using StoryFlow.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Warns at build time when no StoryFlow project will be discoverable in the
    /// built player. Runtime discovery reads StoryFlowSettings.DefaultProject
    /// (settings asset in Resources), then a Resources asset named "Project",
    /// then any loaded project asset. Scene references also work but cannot be
    /// verified cheaply here, so this check only warns and never fails the build.
    /// </summary>
    public class StoryFlowBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // No project assets at all: the plugin is installed but unused
            string[] projectGuids = AssetDatabase.FindAssets("t:StoryFlowProjectAsset");
            if (projectGuids.Length == 0)
                return;

            // Anchor 1: settings reachable the way the runtime loads them
            // (Resources.Load), with a project assigned
            var settings = Resources.Load<StoryFlowSettings>("StoryFlowSettings");
            if (settings != null && settings.DefaultProject != null)
                return;

            // Anchor 2: a project asset inside any Resources folder
            foreach (string guid in projectGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (path.Contains("/Resources/"))
                    return;
            }

            Debug.LogWarning(
                "[StoryFlow] Build check: this project contains StoryFlow project assets, but none is " +
                "anchored for player builds. Unless a scene in the build references the project asset, " +
                "dialogue will fail at runtime with \"no StoryFlow project found\". Fix: open " +
                "Edit > Project Settings > StoryFlow and assign Default Project (or re-import your " +
                "StoryFlow project, which assigns it automatically).");
        }
    }
}
