using UnityEngine;

namespace StoryFlow.Data
{
    public class StoryFlowSettings : ScriptableObject
    {
        [Header("Default Project")]
        [Tooltip("Project asset loaded by StoryFlowManager at runtime. Set automatically on import. Required for player builds unless a scene references the project asset or game code calls SetProject().")]
        public StoryFlowProjectAsset DefaultProject;

        [Header("Import")]
        [Tooltip("Default output path for imported assets (relative to Assets/).")]
        public string DefaultImportPath = "StoryFlow";

        [Header("Debug")]
        [Tooltip("Enable verbose logging for node execution.")]
        public bool VerboseLogging;

        [Tooltip("Log variable changes at runtime.")]
        public bool LogVariableChanges;

        private static StoryFlowSettings _instance;
        private static bool _searched;

        public static StoryFlowSettings Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
                    _instance = Resources.Load<StoryFlowSettings>("StoryFlowSettings");
                    _searched = true;
                    // Settings asset is optional — no warning needed
                }
                return _instance;
            }
        }

        /// <summary>
        /// Explicitly sets the settings instance. Useful for testing or custom initialization.
        /// </summary>
        public static void SetInstance(StoryFlowSettings settings)
        {
            _instance = settings;
            _searched = true;
        }

        /// <summary>
        /// Clears the cached instance, forcing a fresh lookup on next access.
        /// Called automatically on domain reload.
        /// </summary>
        public static void ClearCache()
        {
            _instance = null;
            _searched = false;
        }

        /// <summary>
        /// Resets static state on domain reload (handles Enter Play Mode settings with domain reload disabled).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            ClearCache();
        }
    }
}
