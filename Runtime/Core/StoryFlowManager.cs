using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Execution;
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

        // The character id bridge (characters engine contract §3): character file id ->
        // RuntimeCharacters key, from the project's imported character-index.json. Shared by
        // reference with execution contexts the same way RuntimeCharacters is, and refreshed
        // in DeepCopyRuntimeCharacters — exactly where RuntimeCharacters itself is (re)built,
        // and NEVER at save apply: a save carries character STATE, not the project's id
        // mapping. Empty on a pre-P4 import, so every id lookup falls back to paths.
        [NonSerialized] internal Dictionary<string, string> CharacterIdBridge = new();

        [NonSerialized] internal HashSet<string> UsedOnceOnlyOptions = new();

        // The language every StoryFlow string is read in (localization spec §9). "en" before a
        // project is loaded, which is what every pre-localization export's strings are keyed by;
        // InitializeProject then points it at the project's SOURCE language unless the player
        // has already chosen a language the new project also carries. Unlike the tables it
        // selects, this is not project content but the player's CHOICE, so it lives here rather
        // than on the project asset and no mirror of it exists anywhere else.
        [NonSerialized] private string _currentLanguage = "en";

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
            // THE LANGUAGE FIRST, because everything seeded below is read in it. The player's
            // choice SURVIVES a re-set of a project that still carries it (the HTML runtime's
            // first-wins posture: re-installing content mid-game must not undo a choice); a
            // project that does not carry the current code snaps to that project's source
            // language, so a game can never be left reading a language nothing ships. For a
            // project with no localization sidecar the only code that resolves is its source
            // language, so this is "en" -> "en" and changes nothing.
            var carried = Project.ResolveLanguageCode(_currentLanguage);
            _currentLanguage = string.IsNullOrEmpty(carried) ? Project.SourceLanguage : carried;

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
            CharacterIdBridge.Clear();

            if (Project == null) return;

            foreach (var kvp in Project.Characters)
            {
                // Deep copy the character asset into runtime data so mutations
                // do not affect the source ScriptableObject.
                RuntimeCharacters[kvp.Key] = kvp.Value.CreateRuntimeData();
            }

            // The bridge moves with the characters it keys into: values are plain strings,
            // so a shallow copy is already isolation from the project asset.
            foreach (var kvp in Project.CharacterIdBridge)
            {
                CharacterIdBridge[kvp.Key] = kvp.Value;
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

        /// <summary>
        /// The character tables as one reference, for the access layer's character branch
        /// (P4 contract §3). Minted per call for the same reason as
        /// <see cref="GetDataAssetStore"/> above.
        /// </summary>
        internal StoryFlowCharacterStoreRef GetCharacterStore()
        {
            return new StoryFlowCharacterStoreRef
            {
                Bridge = CharacterIdBridge,
                Characters = RuntimeCharacters
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
        // owns is only what genuinely differs (which store, what to invalidate after a write,
        // and which language a read runs in). See that class for the binding rule, the type
        // gate and the read-any / write-scalar asymmetry.
        //
        // THE LANGUAGE A READ RUNS IN is GetLanguage() here, with no ActiveLanguageCodeFor
        // indirection: that helper exists to decide between the MANAGER'S choice and a
        // component's pre-localization field, and this is the manager. InitializeProject already
        // guarantees the value is a code the loaded project carries (it snaps to the project's
        // source language otherwise), so a project with no sidecar asks in its source language
        // and every .sfd value answers with its own text. What the language does to a read is
        // StoryFlowDataAssetStore.TryRead's business — declarations localize, overrides and
        // session writes never do (localization spec §2's amendment of 2026-08-27).
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
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>Reads an integer .sfd variable.</summary>
        public int GetDataAssetInt(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetInt(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>Reads a float .sfd variable.</summary>
        public float GetDataAssetFloat(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetFloat(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>
        /// Reads a string-family .sfd variable: string, image, audio or character. Enum is NOT
        /// reachable here — use <see cref="GetDataAssetEnum"/>.
        /// </summary>
        public string GetDataAssetString(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetString(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>Reads an enum .sfd variable as its value name.</summary>
        public string GetDataAssetEnum(StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetEnum(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>
        /// Reads ANY .sfd variable, arrays and maps included, as a DETACHED copy. Read-only:
        /// there is no matching setter (see StoryFlowDataAssetAccess).
        /// </summary>
        public StoryFlowVariant GetDataAssetVariant(
            StoryFlowDataAssetAsset asset, string variableName, out bool found)
        {
            return StoryFlowDataAssetAccess.GetVariant(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), Project, GetLanguage(),
                asset, variableName, out found);
        }

        /// <summary>Writes a boolean .sfd variable at the referenced asset's own level.</summary>
        public bool SetDataAssetBool(StoryFlowDataAssetAsset asset, string variableName, bool value)
        {
            return StoryFlowDataAssetAccess.SetBool(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), asset, variableName, value);
        }

        /// <summary>Writes an integer .sfd variable.</summary>
        public bool SetDataAssetInt(StoryFlowDataAssetAsset asset, string variableName, int value)
        {
            return StoryFlowDataAssetAccess.SetInt(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), asset, variableName, value);
        }

        /// <summary>Writes a float .sfd variable.</summary>
        public bool SetDataAssetFloat(StoryFlowDataAssetAsset asset, string variableName, float value)
        {
            return StoryFlowDataAssetAccess.SetFloat(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), asset, variableName, value);
        }

        /// <summary>
        /// Writes a string-family .sfd variable (string / image / audio / character). Enum
        /// declarations are refused here — use <see cref="SetDataAssetEnum"/>.
        /// </summary>
        public bool SetDataAssetString(StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            return StoryFlowDataAssetAccess.SetString(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), asset, variableName, value);
        }

        /// <summary>Writes an enum .sfd variable by value name.</summary>
        public bool SetDataAssetEnum(StoryFlowDataAssetAsset asset, string variableName, string value)
        {
            return StoryFlowDataAssetAccess.SetEnum(
                GetDataAssetStore(), DataAssetRefusals, GetCharacterStore(), asset, variableName, value);
        }

        // =====================================================================
        // Characters (by character FILE id — P4) — the host mirror
        // =====================================================================
        //
        // The same five StoryFlowComponent carries, for game code that owns no dialogue —
        // the same reason the .sfd surface above is mirrored (Unity's V2 posture: the
        // manager carries per-variable APIs). The component's five prefer its live
        // context's tables; these read the manager's own — the SAME dictionaries by
        // reference in a running game, so the two surfaces cannot come apart. Ids resolve
        // through THE resolution point (ResolveCharacterKeyIn) with the null warn latch:
        // there is no context out here to own the once-per-run latch, so a degraded id
        // logs on every call (the established outside-dialogue posture — fail open). An
        // unresolvable id through the variable pair therefore logs TWO unlatched lines
        // per call — the resolution point's reason, then the accessor's not-found.
        // Deliberate: the lines carry different information (why the id degraded; which
        // call refused), and suppressing the second on the id lane would silence the
        // only line that names the call.
        //
        // THREE deliberate postures, each an existing precedent rather than a new rule —
        // the first two asymmetries against the component's five, the third shared WITH
        // them:
        //  - VALUES COME BACK STORED, VERBATIM: a Name that is a string-table key stays a
        //    key, exactly as ExportState and the .sfd surface's character branch answer
        //    it. Language resolution is a component concern (LanguageCode lives there), so
        //    the component's GetCharacterVariable/ById is the language-aware door.
        //  - NO EVENTS: the manager has none to raise, the same way its .sfd setters
        //    invalidate no caches (and amendment A2(b) keeps OnCharacterVariableChanged
        //    node-lane only regardless).
        //  - LIVE VARIANTS out of the variable getter: a declared variable's variant
        //    comes back LIVE — mutating it writes through to the character — and only
        //    the builtin arms build fresh ones. Unlike the .sfd surface above, this
        //    getter does not detach: the component's pre-P4 GetCharacterVariable never
        //    did, and both ById twins inherit that ownership.

        /// <summary>
        /// The live runtime character a character FILE id resolves to, through the id
        /// bridge. <paramref name="found"/> is false for a dangling id (no bridge entry)
        /// and for an id whose record is not among the loaded runtime characters
        /// (amendment A3(a)); <see cref="GetCharacterPathById"/> can still answer in that
        /// second case, because the bridge itself is project-derived.
        /// </summary>
        public StoryFlowCharacterData GetCharacterById(string characterId, out bool found)
        {
            var recordKey = StoryFlowExecutionContext.ResolveCharacterKeyIn(
                CharacterIdBridge, RuntimeCharacters, characterId, null);
            found = RuntimeCharacters.TryGetValue(recordKey, out var character);
            return character;
        }

        /// <summary>
        /// The character record key for a character FILE id — a PURE BRIDGE LOOKUP,
        /// deliberately NOT the resolution point (amendment A3(a)): it answers for any
        /// indexed id whether or not the record is loaded, and never warns. An existence
        /// query is not a degraded resolution, and the resolution point's fall-through
        /// would answer a normalized spelling of the id instead of not-found. The key
        /// comes back verbatim (lowercase, forward-slash — this plugin's store form).
        /// </summary>
        public string GetCharacterPathById(string characterId, out bool found)
        {
            if (!string.IsNullOrEmpty(characterId) &&
                CharacterIdBridge.TryGetValue(characterId, out var recordKey))
            {
                found = true;
                return recordKey;
            }

            found = false;
            return "";
        }

        /// <summary>
        /// Record keys of every LOADED character, in map order (no sort promise) — the
        /// amendment A4 enumeration surface; see the component twin for why by-id
        /// enumeration is deliberately not provided and why in this engine the loaded set
        /// always equals the project's (merge loads never remove).
        /// </summary>
        public List<string> GetCharacterPaths()
        {
            return new List<string>(RuntimeCharacters.Keys);
        }

        /// <summary>
        /// A character variable by character FILE id and variable NAME (amendment A1).
        /// cf_name / cf_image alias the Name / Image builtins (amendment A2(a)); both
        /// answer their STORED value verbatim (see the section header). Null, with a
        /// warning, for an unresolvable id or an undeclared name.
        ///
        /// NOTE the pair's split for a custom variable literally named name/image (any
        /// casing): this getter's case-insensitive builtin arms SHADOW it, while the
        /// setter's cf_-only rewrite still writes it. That is INHERITED per lane from the
        /// component's pre-P4 path APIs — GetCharacterVariable always had the
        /// case-insensitive builtin arms, SetCharacterVariable never did — not an
        /// accident of this mirror (see StoryFlowCharacterTokens' two-tier design).
        ///
        /// OWNERSHIP: a declared variable's variant comes back LIVE — mutating it writes
        /// through to the character — while the builtin arms build fresh variants. Unlike
        /// the .sfd surface above, this getter does not detach (the pre-P4 path API's
        /// ownership, inherited).
        /// </summary>
        public StoryFlowVariant GetCharacterVariableById(string characterId, string variableName)
        {
            var character = GetCharacterById(characterId, out var found);
            if (!found)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterVariableById: character id \"{characterId}\" not found.");
                return null;
            }

            // First-tier aliases (the builtin arms below are case-insensitive like the
            // component's — see StoryFlowCharacterTokens for the two-tier design).
            if (StoryFlowCharacterTokens.IsCharacterNameBuiltin(variableName))
            {
                return StoryFlowVariant.String(character.Name ?? "");
            }
            if (StoryFlowCharacterTokens.IsCharacterImageBuiltin(variableName))
            {
                return StoryFlowVariant.String(character.ImageAssetKey ?? "");
            }

            var v = character.FindVariableByName(variableName);
            if (v != null)
                return v.Value;

            Debug.LogWarning($"[StoryFlow] GetCharacterVariableById: variable \"{variableName}\" not found on character id \"{characterId}\".");
            return null;
        }

        /// <summary>
        /// Sets a character variable by character FILE id and variable NAME. Warns and
        /// no-ops when the character does not declare the variable — a write NEVER creates
        /// one (amendment A3(b)). No builtin write arms, matching the component's
        /// SetCharacterVariable (pre-P4 posture; the .sfd surface's character branch is
        /// the API route that writes Name/Image).
        /// </summary>
        public void SetCharacterVariableById(string characterId, string variableName, StoryFlowVariant value)
        {
            var character = GetCharacterById(characterId, out var found);
            if (!found)
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVariableById: character id \"{characterId}\" not found.");
                return;
            }

            // Second-tier alias (the list search below is case-sensitive): only the
            // reserved cf_ tokens rewrite — see RewriteCfTokensOnly for why.
            variableName = StoryFlowCharacterTokens.RewriteCfTokensOnly(variableName);

            var v = character.FindVariableByName(variableName);
            if (v != null)
            {
                v.Value = value ?? new StoryFlowVariant();
                // Also update the quick-lookup dictionary, as the component's twin does.
                character.Variables[variableName] = v.Value;
                return;
            }

            Debug.LogWarning($"[StoryFlow] SetCharacterVariableById: variable \"{variableName}\" not found on character id \"{characterId}\".");
        }

        // =====================================================================
        // Localization (spec §9) — the player's language, game-wide
        // =====================================================================
        //
        // ONE surface, not the mirrored pair the .sfd and character sections above carry. The
        // language is a single game-wide value rather than per-variable state, so a second door
        // on StoryFlowComponent would be two names for one field — the component keeps only its
        // pre-localization LanguageCode, which a localized project ignores.

        /// <summary>
        /// Switches the language every StoryFlow string is read in. True when the game is now
        /// reading <paramref name="languageCode"/>.
        ///
        /// AN UNKNOWN OR EMPTY CODE IS A NO-OP: it warns, changes nothing and returns false.
        /// Falling back to the default instead would let a typo silently move the player out of
        /// the language they picked, and a caller that wants to know can read
        /// <see cref="GetLanguage"/>. The codes this accepts are exactly the rows
        /// <see cref="GetLanguages"/> returns, matched case-insensitively with the REGISTERED
        /// casing winning; a project with no localization sidecar accepts only its source
        /// language, so this is a no-op there by construction rather than by a special case.
        ///
        /// WHAT MOVES, AND WHEN. Everything this plugin resolves AT READ TIME follows
        /// immediately — dialogue titles, text, text blocks, option labels, string and enum
        /// variable values, character string variables, map and array elements — because this
        /// engine keeps string-table KEYS in its runtime state and resolves them per read. The
        /// seed-time posture the sibling engines document therefore has almost no surface here:
        /// initial values are not pre-resolved at load, so a mid-session switch reaches them
        /// too. ONE EXCEPTION, and it is an IMPORT-time bake rather than a load-time seed: a
        /// character's display NAME is resolved into the imported character asset (see
        /// StoryFlowImporter.ImportCharacter), so a speaker label does not flip until the
        /// project is re-imported. Character string VARIABLES are unaffected and do flip.
        ///
        /// PERSISTENCE IS THE GAME'S. This plugin keeps the choice for the SESSION only, and
        /// deliberately: it has no player-settings lane of its own, and the save envelope
        /// carries story state a slot owns (globals, characters, once-only options, the .sfd
        /// overlay) — a language is not that kind of thing. It must survive with no save file
        /// at all, apply before any save is loaded, and not differ per slot. The HTML runtime
        /// reaches the same conclusion and keeps it beside its volume settings rather than in
        /// the envelope. In Unity that lane already exists and belongs to the game: persist the
        /// code with your own settings (PlayerPrefs or your own save) and call this once at
        /// boot. It also survives <see cref="ResetAllState"/> for the same reason.
        /// </summary>
        public bool SetLanguage(string languageCode)
        {
            var next = Project != null ? Project.ResolveLanguageCode(languageCode) : "";
            if (string.IsNullOrEmpty(next))
            {
                Debug.LogWarning($"[StoryFlow] SetLanguage: unknown language \"{languageCode}\", " +
                                 $"staying on \"{_currentLanguage}\".");
                return false;
            }

            if (next != _currentLanguage)
            {
                _currentLanguage = next;
                Debug.Log($"[StoryFlow] Language set to \"{_currentLanguage}\".");
            }
            return true;
        }

        /// <summary>
        /// The language code every StoryFlow string is currently read in. The loaded project's
        /// source language until set.
        /// </summary>
        public string GetLanguage()
        {
            return _currentLanguage;
        }

        /// <summary>
        /// Every language the player can be switched to: the SOURCE language first, then the
        /// author's registry order — the list a game's own language picker draws.
        ///
        /// The source row's Name is its Code: the registry stores a display label for target
        /// languages only, because the source language's text lives in the documents
        /// themselves. EMPTY for a project with no localization sidecar, which is how a game
        /// asks "is this project localized at all" without reading a key count.
        /// </summary>
        public List<StoryFlowProjectAsset.LanguageEntry> GetLanguages()
        {
            var languages = new List<StoryFlowProjectAsset.LanguageEntry>();
            if (Project == null || !Project.HasLocalization) return languages;

            // Emitted only when there IS a source language, so a hand-edited sidecar with a
            // blank one cannot produce a row a picker would draw and SetLanguage would refuse.
            if (!string.IsNullOrEmpty(Project.SourceLanguage))
            {
                languages.Add(new StoryFlowProjectAsset.LanguageEntry
                {
                    Code = Project.SourceLanguage,
                    Name = Project.SourceLanguage
                });
            }

            foreach (var language in Project.LanguageEntries)
            {
                if (language == null || string.IsNullOrEmpty(language.Code)) continue;
                languages.Add(new StoryFlowProjectAsset.LanguageEntry
                {
                    Code = language.Code,
                    Name = string.IsNullOrEmpty(language.Name) ? language.Code : language.Name
                });
            }

            return languages;
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
            // The active language deliberately SURVIVES, for the reason it survives a save load
            // (spec §9): it is a player SETTING, not session state.
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
