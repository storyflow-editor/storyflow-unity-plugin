using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Adds a StoryFlow Connect/Sync button to the Scene view toolbar.
    /// Shows "Connect" when disconnected, "Sync" when connected.
    /// </summary>
    [Overlay(typeof(SceneView), "StoryFlow", defaultDisplay = true)]
    [Icon("Packages/com.storyflow.unity/Editor/Resources/storyflow_logo.png")]
    public class StoryFlowToolbarOverlay : ToolbarOverlay
    {
        public StoryFlowToolbarOverlay() : base(StoryFlowToolbarButton.Id) { }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class StoryFlowToolbarButton : EditorToolbarButton
    {
        public const string Id = "StoryFlow/SyncButton";
        private bool lastKnownState;

        public StoryFlowToolbarButton()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/com.storyflow.unity/Editor/Resources/storyflow_logo.png");
            if (icon != null)
                this.icon = icon;

            text = "Connect";
            tooltip = "Connect to StoryFlow Editor";
            clicked += OnClick;

            // Poll connection state using UIElements scheduler (reliable in toolbar)
            schedule.Execute(PollState).Every(500);
        }

        private void PollState()
        {
            bool connected = EditorPrefs.GetBool("StoryFlow_Toolbar_Connected", false);
            if (connected != lastKnownState)
            {
                lastKnownState = connected;
                text = connected ? "Sync" : "Connect";
                tooltip = connected
                    ? "Sync project from StoryFlow Editor"
                    : "Connect to StoryFlow Editor";
            }
        }

        private void OnClick()
        {
            var window = EditorWindow.GetWindow<StoryFlowLiveSyncServer>("StoryFlow Live Sync");
            if (lastKnownState)
                window.RequestSyncFromToolbar();
            else
                window.ConnectFromToolbar();
        }
    }
}
