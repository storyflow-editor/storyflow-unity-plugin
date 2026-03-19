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
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Finds the StoryFlowProjectAsset in the project. Searches Resources first,
        /// then falls back to scanning all loaded assets. In the editor, uses AssetDatabase
        /// as a final fallback to find assets that aren't currently loaded in memory.
        /// </summary>
        private static StoryFlowProjectAsset FindProjectAsset()
        {
            // Try Resources folder first (fast)
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

        /// <summary>Returns the current project asset, or null if none is set.</summary>
        public StoryFlowProjectAsset GetProject()
        {
            return Project;
        }

        /// <summary>Returns true if a project asset has been assigned.</summary>
        public bool HasProject()
        {
            return Project != null;
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

            var saveData = StoryFlowSaveHelpers.Load(slotName);
            if (saveData == null)
            {
                Debug.LogWarning($"[StoryFlow] Save slot \"{slotName}\" not found or could not be loaded.");
                return false;
            }

            // Apply saved global variable values (match by ID, update Value only)
            foreach (var savedVar in saveData.GlobalVariables)
            {
                if (GlobalVariables.TryGetValue(savedVar.Id, out var existing))
                {
                    existing.Value = DeserializeVariantFromSave(savedVar.Type, savedVar.ValueJson);
                }
            }

            // Apply saved character variable values
            foreach (var savedChar in saveData.RuntimeCharacters)
            {
                if (RuntimeCharacters.TryGetValue(savedChar.Path, out var characterData))
                {
                    foreach (var savedVar in savedChar.Variables)
                    {
                        // Find matching variable by ID in the character's variable list
                        foreach (var charVar in characterData.VariablesList)
                        {
                            if (charVar.Id == savedVar.Id)
                            {
                                charVar.Value = DeserializeVariantFromSave(savedVar.Type, savedVar.ValueJson);
                                // Also update the quick-lookup dictionary
                                characterData.Variables[charVar.Name] = charVar.Value;
                                break;
                            }
                        }
                    }
                }
            }

            // Apply once-only options
            UsedOnceOnlyOptions.Clear();
            foreach (var optionKey in saveData.UsedOnceOnlyOptions)
            {
                UsedOnceOnlyOptions.Add(optionKey);
            }

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

        private static StoryFlowVariant DeserializeVariantFromSave(StoryFlowVariableType type, string json)
        {
            return StoryFlowVariant.DeserializeFromJson(type, json);
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
