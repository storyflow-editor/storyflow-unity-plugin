using System;
using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow
{
    /// <summary>
    /// Singleton manager that holds shared state across all StoryFlowComponent instances.
    /// Auto-creates itself at runtime and auto-discovers the project asset.
    /// Persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    [AddComponentMenu("StoryFlow/StoryFlow Manager")]
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class StoryFlowManager : MonoBehaviour
    {
        public static StoryFlowManager Instance { get; private set; }

        [Header("Project")]
        [Tooltip("The StoryFlow project asset. Auto-discovered if not assigned.")]
        public StoryFlowProjectAsset Project;

        // Shared mutable state (runtime copies)
        [NonSerialized] internal Dictionary<string, StoryFlowVariable> GlobalVariables = new();
        [NonSerialized] internal Dictionary<string, StoryFlowCharacterData> RuntimeCharacters = new();
        [NonSerialized] internal HashSet<string> UsedOnceOnlyOptions = new();

        // Dialogue tracking
        private int _activeDialogueCount;

        // =====================================================================
        // Auto-Creation
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;

            // Check if one already exists in the scene
#if UNITY_2023_1_OR_NEWER
            var existing = UnityEngine.Object.FindFirstObjectByType<StoryFlowManager>();
#else
            var existing = UnityEngine.Object.FindObjectOfType<StoryFlowManager>();
#endif
            if (existing != null) return;

            // Auto-create
            var go = new GameObject("[StoryFlow Manager]");
            go.AddComponent<StoryFlowManager>();
        }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A scene-placed manager losing the race to the auto-created instance
                // hands over its explicitly assigned project before being destroyed.
                if (Project != null && !Instance.HasProject())
                    Instance.SetProject(Project);

                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Auto-discover project if not assigned
            if (Project == null)
                Project = FindProjectAsset();

            if (Project != null)
                InitializeProject();
            else
                LogDiscoveryFailure();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Finds the StoryFlowProjectAsset in the project. Checks the explicit
        /// StoryFlowSettings.DefaultProject assignment first (works in player builds;
        /// the importer sets it automatically), then Resources, then all loaded assets.
        /// In the editor, uses AssetDatabase as a final fallback to find assets that
        /// aren't currently loaded in memory.
        /// </summary>
        private static StoryFlowProjectAsset FindProjectAsset()
        {
            // Explicit assignment via settings (the settings asset lives in Resources,
            // so it ships in builds and anchors the project reference)
            var settings = StoryFlowSettings.Instance;
            if (settings != null && settings.DefaultProject != null)
                return settings.DefaultProject;

            // Try Resources folder (fast)
            var fromResources = Resources.Load<StoryFlowProjectAsset>("Project");
            if (fromResources != null) return fromResources;

            // Scan all loaded ScriptableObjects (works for assets loaded via addressables or direct reference)
            var all = Resources.FindObjectsOfTypeAll<StoryFlowProjectAsset>();
            if (all.Length > 0) return all[0];

#if UNITY_EDITOR
            // Editor fallback: use AssetDatabase to find unloaded assets
            var guids = UnityEditor.AssetDatabase.FindAssets("t:StoryFlowProjectAsset");
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(path);
                if (asset != null) return asset;
            }
#endif

            return null;
        }

        /// <summary>
        /// Logs a warning when startup discovery finds no project, with guidance
        /// matching where the problem can actually be fixed. In the editor the
        /// AssetDatabase fallback means this only fires when nothing is imported;
        /// in a player it usually means no build-visible anchor exists.
        /// </summary>
        private static void LogDiscoveryFailure()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[StoryFlow] No StoryFlow project asset found at startup. " +
                             "Import a project via Tools > StoryFlow > Import Project. " +
                             "Discovery retries when a dialogue starts.");
#else
            Debug.LogWarning("[StoryFlow] No StoryFlow project found in this build. " +
                             "Assign Default Project in Edit > Project Settings > StoryFlow and rebuild " +
                             "(plugin 1.2.2+ sets it automatically on import), reference the project " +
                             "asset from a scene, or call StoryFlowManager.SetProject(). " +
                             "Discovery retries when a dialogue starts.");
#endif
        }

        // =====================================================================
        // Project Initialization
        // =====================================================================

        /// <summary>
        /// Assigns a new project asset and reinitializes all shared state.
        /// </summary>
        public void SetProject(StoryFlowProjectAsset project)
        {
            if (project == null)
            {
                Debug.LogWarning("[StoryFlow] SetProject called with null project.");
                return;
            }

            Project = project;
            InitializeProject();
        }

        /// <summary>
        /// Initializes (or re-initializes) shared runtime state from the assigned project.
        /// Deep-copies global variables and character data so runtime mutations
        /// do not affect the source ScriptableObject assets.
        /// </summary>
        private void InitializeProject()
        {
            DeepCopyGlobalVariables();
            DeepCopyRuntimeCharacters();
            UsedOnceOnlyOptions.Clear();

            Debug.Log($"[StoryFlow] Project initialized: \"{Project.Title}\" " +
                      $"({GlobalVariables.Count} global variables, {RuntimeCharacters.Count} characters)");
        }

        private void DeepCopyGlobalVariables()
        {
            GlobalVariables.Clear();

            if (Project == null) return;

            foreach (var kvp in Project.GlobalVariables)
            {
                GlobalVariables[kvp.Key] = new StoryFlowVariable(kvp.Value);
            }
        }

        private void DeepCopyRuntimeCharacters()
        {
            RuntimeCharacters.Clear();

            if (Project == null) return;

            foreach (var kvp in Project.Characters)
            {
                // Deep copy the character asset into runtime data so mutations
                // do not affect the source ScriptableObject.
                RuntimeCharacters[kvp.Key] = kvp.Value.CreateRuntimeData();
            }
        }

        // =====================================================================
        // Public Accessors
        // =====================================================================

        /// <summary>
        /// Returns the current project asset, or null if none could be found.
        /// While no project is assigned, retries auto-discovery so projects that
        /// become loadable after startup (scene references, addressables) are found.
        /// </summary>
        public StoryFlowProjectAsset GetProject()
        {
            RetryDiscoveryIfNeeded();
            return Project;
        }

        /// <summary>Returns true if a project asset is assigned or discoverable.</summary>
        public bool HasProject()
        {
            RetryDiscoveryIfNeeded();
            return Project != null;
        }

        /// <summary>
        /// Re-runs auto-discovery while no project is assigned. Cheap when a project
        /// is present (single null check); only scans while the project is missing.
        /// </summary>
        private void RetryDiscoveryIfNeeded()
        {
            if (Project != null) return;

            Project = FindProjectAsset();
            if (Project != null)
                InitializeProject();
        }

        /// <summary>
        /// Gets a script asset by its path from the current project.
        /// Returns null if the project is not set or the path is not found.
        /// </summary>
        public StoryFlowScriptAsset GetScript(string path)
        {
            return Project != null ? Project.GetScriptByPath(path) : null;
        }

        /// <summary>
        /// Returns a list of all script paths registered in the current project.
        /// </summary>
        public List<string> GetAllScriptPaths()
        {
            return Project != null ? Project.GetAllScriptPaths() : new List<string>();
        }

        // =====================================================================
        // Save / Load
        // =====================================================================

        /// <summary>
        /// Serializes current story state (global variables, character variables, once-only
        /// options) to a JSON string in the unified cross-engine format.
        ///
        /// Intended for hosts that persist state themselves, for example alongside their own
        /// save data in a cloud provider. The returned string is the same payload SaveToSlot
        /// writes to disk.
        /// </summary>
        public string ExportState()
        {
            return StoryFlowStateSerializer.Serialize(GlobalVariables, RuntimeCharacters, UsedOnceOnlyOptions);
        }

        /// <summary>
        /// Applies story state previously produced by <see cref="ExportState"/>.
        ///
        /// Never throws and never partially applies: the blob is parsed in full before
        /// anything is committed. The merge is lenient. Values overwrite matching entries,
        /// unknown ids are ignored, and entries absent from the blob keep their current
        /// value, so a blob saved before a story update still loads after it. The project
        /// asset stays authoritative for schema; only values come from the blob.
        ///
        /// Refuses while a dialogue is active. Returns false on empty, null or malformed input.
        /// </summary>
        public bool ImportState(string stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson))
            {
                Debug.LogWarning("[StoryFlow] ImportState called with null or empty state.");
                return false;
            }

            if (_activeDialogueCount > 0)
            {
                Debug.LogError("[StoryFlow] Cannot import state while dialogue is active. " +
                               "Stop all dialogues before importing.");
                return false;
            }

            // Stage first. A truncated blob must never leave story state half applied.
            var snapshot = StoryFlowStateSerializer.Deserialize(stateJson);
            if (snapshot == null)
            {
                Debug.LogWarning("[StoryFlow] ImportState could not parse the supplied state.");
                return false;
            }

            ApplySnapshot(snapshot);
            return true;
        }

        /// <summary>
        /// Commits a parsed snapshot onto live state. Only values are taken; Type, KeyType,
        /// ValueType and enum value lists stay as the project asset declared them.
        /// </summary>
        private void ApplySnapshot(StoryFlowStateSnapshot snapshot)
        {
            foreach (var kvp in snapshot.GlobalValues)
            {
                if (GlobalVariables.TryGetValue(kvp.Key, out var existing))
                {
                    existing.Value = kvp.Value;
                }
            }

            foreach (var charEntry in snapshot.CharacterValues)
            {
                if (!RuntimeCharacters.TryGetValue(charEntry.Key, out var characterData)) { continue; }

                foreach (var varEntry in charEntry.Value)
                {
                    // The unified format keys character variables by name.
                    foreach (var charVar in characterData.VariablesList)
                    {
                        if (charVar.Name != varEntry.Key) { continue; }
                        charVar.Value = varEntry.Value;
                        characterData.Variables[charVar.Name] = charVar.Value;
                        break;
                    }
                }
            }

            UsedOnceOnlyOptions.Clear();
            foreach (var key in snapshot.UsedOnceOnlyOptions)
            {
                UsedOnceOnlyOptions.Add(key);
            }
        }

        /// <summary>
        /// Saves the current global state (variables, characters, once-only options)
        /// to the specified save slot. Returns true if the save succeeded, false otherwise.
        /// </summary>
        public bool SaveToSlot(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                Debug.LogWarning("[StoryFlow] SaveToSlot called with null or empty slot name.");
                return false;
            }

            try
            {
                StoryFlowSaveHelpers.Save(slotName, GlobalVariables, RuntimeCharacters, UsedOnceOnlyOptions);
                Debug.Log($"[StoryFlow] State saved to slot \"{slotName}\".");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryFlow] Save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Loads global state from the specified save slot.
        /// Returns true if the load was successful, false otherwise.
        /// Loading while dialogue is active is not allowed.
        /// </summary>
        public bool LoadFromSlot(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                Debug.LogWarning("[StoryFlow] LoadFromSlot called with null or empty slot name.");
                return false;
            }

            if (_activeDialogueCount > 0)
            {
                Debug.LogError("[StoryFlow] Cannot load save while dialogue is active. " +
                               "Stop all dialogues before loading.");
                return false;
            }

            var snapshot = StoryFlowSaveHelpers.Load(slotName);
            if (snapshot == null)
            {
                Debug.LogWarning($"[StoryFlow] Save slot \"{slotName}\" not found or could not be loaded.");
                return false;
            }

            ApplySnapshot(snapshot);
            Debug.Log($"[StoryFlow] State loaded from slot \"{slotName}\".");
            return true;
        }

        /// <summary>Returns true if a save exists at the specified slot.</summary>
        public bool DoesSaveExist(string slotName)
        {
            return StoryFlowSaveHelpers.Exists(slotName);
        }

        /// <summary>Deletes the save at the specified slot, if it exists.</summary>
        public void DeleteSave(string slotName)
        {
            StoryFlowSaveHelpers.Delete(slotName);
        }

        // =====================================================================
        // Reset
        // =====================================================================

        /// <summary>
        /// Re-initializes global variables from the project asset, discarding all runtime changes.
        /// </summary>
        public void ResetGlobalVariables()
        {
            if (Project == null)
            {
                Debug.LogWarning("[StoryFlow] Cannot reset global variables: no project assigned.");
                return;
            }

            DeepCopyGlobalVariables();
            Debug.Log("[StoryFlow] Global variables reset to project defaults.");
        }

        /// <summary>
        /// Re-initializes runtime characters from the project asset, discarding all runtime changes.
        /// </summary>
        public void ResetRuntimeCharacters()
        {
            if (Project == null)
            {
                Debug.LogWarning("[StoryFlow] Cannot reset runtime characters: no project assigned.");
                return;
            }

            DeepCopyRuntimeCharacters();
            Debug.Log("[StoryFlow] Runtime characters reset to project defaults.");
        }

        /// <summary>
        /// Resets all shared state: global variables, runtime characters, and once-only options.
        /// </summary>
        public void ResetAllState()
        {
            if (Project == null)
            {
                Debug.LogWarning("[StoryFlow] Cannot reset state: no project assigned.");
                return;
            }

            DeepCopyGlobalVariables();
            DeepCopyRuntimeCharacters();
            UsedOnceOnlyOptions.Clear();
            Debug.Log("[StoryFlow] All shared state reset to project defaults.");
        }

        // =====================================================================
        // Dialogue Tracking
        // =====================================================================

        /// <summary>Called by StoryFlowComponent when a dialogue session starts.</summary>
        public void NotifyDialogueStarted()
        {
            _activeDialogueCount++;
        }

        /// <summary>Called by StoryFlowComponent when a dialogue session ends.</summary>
        public void NotifyDialogueEnded()
        {
            _activeDialogueCount = Mathf.Max(0, _activeDialogueCount - 1);
        }

        /// <summary>Returns true if any StoryFlowComponent currently has an active dialogue.</summary>
        public bool IsDialogueActive()
        {
            return _activeDialogueCount > 0;
        }
    }
}
