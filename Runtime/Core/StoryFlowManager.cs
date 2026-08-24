using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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

        // The .sfd Data Asset store (engine contract §3). The SEED is rebuilt from the
        // project and never written to again; the OVERLAY holds this session's script
        // writes and is what a save persists. Both are handed to execution contexts by
        // reference, the same way GlobalVariables and RuntimeCharacters are.
        [NonSerialized] internal Dictionary<string, StoryFlowDataAssetDef> DataAssetSeed = new();
        [NonSerialized] internal Dictionary<string, Dictionary<string, StoryFlowVariant>> DataAssetOverlay = new();

        // The host API's refusal latch, ONE per game so the manager's surface and every
        // component's share it — the same typo reported from both would otherwise be two lines,
        // and the point of latching is that it is one. Re-armed in BuildDataAssetSeed.
        [NonSerialized] internal readonly StoryFlowDataAssetAccess.RefusalLatch DataAssetRefusals = new();

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
            BuildDataAssetSeed();
            UsedOnceOnlyOptions.Clear();

            Debug.Log($"[StoryFlow] Project initialized: \"{Project.Title}\" " +
                      $"({GlobalVariables.Count} global variables, {RuntimeCharacters.Count} characters, " +
                      $"{DataAssetSeed.Count} data assets)");
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

        /// <summary>
        /// Rebuilds the Data Asset seed from the project and drops every session write.
        /// The two always move together: a fresh seed is a fresh session, and an overlay
        /// entry against a seed that no longer carries its asset is unreadable anyway.
        ///
        /// This is also where the host-API refusal latch RE-ARMS, and it is the only place it
        /// needs to: both re-arm points the latch has — a new project (SetProject, and the
        /// discovery retry behind it) and ResetAllState — reach the seed through here. A latch
        /// that survived a project swap would suppress a warning about a variable that now
        /// genuinely does not exist, which is the one moment the author most needs to hear it.
        /// </summary>
        private void BuildDataAssetSeed()
        {
            StoryFlowDataAssetStore.BuildSeed(Project, DataAssetSeed);
            StoryFlowDataAssetStore.ResetOverlay(DataAssetOverlay);
            DataAssetRefusals.Clear();
        }

        /// <summary>
        /// The Data Asset store as one reference, for an execution context to hold. Minted
        /// per call rather than cached, so it can never outlive or disagree with the maps
        /// it points at.
        /// </summary>
        internal StoryFlowDataAssetStoreRef GetDataAssetStore()
        {
            return new StoryFlowDataAssetStoreRef
            {
                Seed = DataAssetSeed,
                Overlay = DataAssetOverlay
            };
        }

        // =====================================================================
        // Data Assets (.sfd) — the typed host API
        // =====================================================================
        //
        // The same surface StoryFlowComponent exposes, and deliberately: the store is
        // MANAGER-GLOBAL, so a shop panel, an inventory screen or a save menu — none of which
        // own a dialogue — would otherwise have to find a StoryFlowComponent just to use it as
        // a proxy for state that was never the component's. Both surfaces route through
        // StoryFlowDataAssetAccess, so neither can answer differently from the other; what each
        // owns is only what genuinely differs (which store, and what to invalidate after a
        // write). See that class for the binding rule, the type gate and the read-any /
        // write-scalar asymmetry.
        //
        // THE SETTERS HERE INVALIDATE NO EVALUATION CACHES, and cannot: a manager has no
        // execution context. If a dialogue is running on some component when one of these
        // writes lands, that component keeps any condition it had already memoized ABOVE a .sfd
        // accessor until its next rebuild, so an option can answer with the pre-write value for
        // a moment. The component's own setters clear their context for exactly this reason and
        // still only reach THEIR context — a second component mid-dialogue is stale either way,
        // so this is the same documented asymmetry, not a worse one. The blast radius is small
        // because the accessors themselves are cache-exempt: only a memoized parent goes stale,
        // never the read. Writing from a manager while a dialogue runs is not the shape this
        // surface is for; a script node is.

        /// <summary>Reads a boolean .sfd variable. <paramref name="found"/> is false for every refusal.</summary>
        public bool GetDataAssetBool(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetBool(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>Reads an integer .sfd variable.</summary>
        public int GetDataAssetInt(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetInt(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>Reads a float .sfd variable.</summary>
        public float GetDataAssetFloat(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetFloat(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>
        /// Reads a string-family .sfd variable: string, image, audio or character. Enum is NOT
        /// reachable here — use <see cref="GetDataAssetEnum"/>.
        /// </summary>
        public string GetDataAssetString(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetString(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>Reads an enum .sfd variable as its value name.</summary>
        public string GetDataAssetEnum(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetEnum(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>
        /// Reads ANY .sfd variable, arrays and maps included, as a DETACHED copy. Read-only:
        /// there is no matching setter (see StoryFlowDataAssetAccess).
        /// </summary>
        public StoryFlowVariant GetDataAssetVariant(
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetVariant(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, out found);
        }

        /// <summary>Writes a boolean .sfd variable at the referenced asset's own level.</summary>
        public bool SetDataAssetBool(StoryFlowDataAssetAsset asset, string variableName, bool value)
        {
            return StoryFlowDataAssetAccess.SetBool(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, value);
        }

        /// <summary>Writes an integer .sfd variable.</summary>
        public bool SetDataAssetInt(StoryFlowDataAssetAsset asset, string variableName, int value)
        {
            return StoryFlowDataAssetAccess.SetInt(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, value);
        }

        /// <summary>Writes a float .sfd variable.</summary>
        public bool SetDataAssetFloat(StoryFlowDataAssetAsset asset, string variableName, float value)
        {
            return StoryFlowDataAssetAccess.SetFloat(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, value);
        }

        /// <summary>
        /// Writes a string-family .sfd variable (string / image / audio / character). Enum
        /// declarations are refused here — use <see cref="SetDataAssetEnum"/>.
        /// </summary>
        public bool SetDataAssetString(StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            return StoryFlowDataAssetAccess.SetString(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, value);
        }

        /// <summary>Writes an enum .sfd variable by value name.</summary>
        public bool SetDataAssetEnum(StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            return StoryFlowDataAssetAccess.SetEnum(
                GetDataAssetStore(), DataAssetRefusals, asset, variableName, value);
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
            return StoryFlowStateSerializer.Serialize(
                GlobalVariables, RuntimeCharacters, UsedOnceOnlyOptions, DataAssetSeed, DataAssetOverlay);
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
        /// ValueType and enum value lists stay as the project asset declared them. Character
        /// name and image are values too, since SetCharacterVar mutates both mid-story.
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

            foreach (var nameEntry in snapshot.CharacterNames)
            {
                if (RuntimeCharacters.TryGetValue(nameEntry.Key, out var characterData))
                {
                    characterData.Name = nameEntry.Value;
                }
            }

            foreach (var imageEntry in snapshot.CharacterImages)
            {
                if (RuntimeCharacters.TryGetValue(imageEntry.Key, out var characterData))
                {
                    // Only the asset key is restored. The Sprite re-resolves from it on the
                    // next dialogue render, exactly as after a SetCharacterVar Image.
                    characterData.ImageAssetKey = imageEntry.Value;
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

            ApplyDataAssetValues(snapshot.DataAssetValues);
        }

        /// <summary>
        /// REPLACES the .sfd session overlay with the saved table (contract §7). Clears FIRST
        /// and unconditionally, so an absent or malformed key restores seed state — which is
        /// exactly the state such a save was made in. Merging instead would let the pre-load
        /// session's writes survive into the loaded game; the once-only options above are the
        /// same shape and the precedent for it, while every other section of a snapshot merges.
        ///
        /// Two kinds of entry are DROPPED rather than restored:
        ///  - an asset the current seed does not carry (deleted since the save). Resolution
        ///    starts its walk at seed[assetId], so the entry can never be read, and keeping it
        ///    would make it ride every subsequent save forever.
        ///  - a variable no level of that asset's chain declares any more (contract §7's
        ///    carve-out). The reference keeps such entries because JS values need no
        ///    declaration; a variant does — with no declaration there is no type to restore it
        ///    AS, and §4.3 already makes it unreadable. Unreal drops them and so does this.
        ///
        /// Values are NOT otherwise re-validated: a stale-TYPED entry degrades at the accessor
        /// via §6.1, exactly as a stale session write does.
        /// </summary>
        private void ApplyDataAssetValues(Dictionary<string, Dictionary<string, JToken>> table)
        {
            StoryFlowDataAssetStore.ResetOverlay(DataAssetOverlay);
            if (table == null) { return; }

            foreach (var assetEntry in table)
            {
                if (!StoryFlowDataAssetStore.HasAsset(DataAssetSeed, assetEntry.Key))
                {
                    // WARNING, where the per-variable drop below is quiet: a whole asset gone
                    // means the save outlived the .sfd, which is a project-shape change worth
                    // surfacing, and it can fire at most once per saved asset. The variable
                    // drop is one line per stale entry and is the expected residue of any
                    // variable rename. Deliberately not the write path's wording either — a
                    // load-time drop and a script write to a dead reference are different
                    // problems with different fixes.
                    Debug.LogWarning($"[StoryFlow] Save load dropped Data Asset \"{assetEntry.Key}\" - " +
                                     "no such asset in this project.");
                    continue;
                }

                Dictionary<string, StoryFlowVariant> values = null;
                foreach (var valueEntry in assetEntry.Value)
                {
                    var declaration = StoryFlowDataAssetStore.FindDeclaration(
                        DataAssetSeed, assetEntry.Key, valueEntry.Key);
                    if (declaration == null)
                    {
                        Debug.Log($"[StoryFlow] Save load dropped Data Asset value " +
                                  $"\"{assetEntry.Key}.{valueEntry.Key}\" - the chain no longer declares it.");
                        continue;
                    }

                    if (values == null) { values = new Dictionary<string, StoryFlowVariant>(); }
                    values[valueEntry.Key] = StoryFlowStateSerializer.BareValueFromJson(
                        valueEntry.Value, declaration);
                }

                // An asset whose every entry was dropped leaves NO entry behind: an empty inner
                // table would ride every subsequent save carrying nothing.
                if (values != null) { DataAssetOverlay[assetEntry.Key] = values; }
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
                StoryFlowSaveHelpers.Save(slotName, GlobalVariables, RuntimeCharacters, UsedOnceOnlyOptions,
                    DataAssetSeed, DataAssetOverlay);
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
            BuildDataAssetSeed();
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
