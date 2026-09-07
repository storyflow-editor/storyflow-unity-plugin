using System;
using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Core runtime state machine for StoryFlow dialogue execution.
    /// One instance per StoryFlowComponent. Manages script switching, variable lookup,
    /// call/flow stacks, loop contexts, and per-node evaluation caching.
    /// </summary>
    public class StoryFlowExecutionContext
    {
        // =====================================================================
        // Constants
        // =====================================================================

        public const int MaxCallDepth = 20;
        public const int MaxFlowDepth = 50;
        public const int MaxEvaluationDepth = 100;
        public const int MaxProcessingDepth = 1000;

        // =====================================================================
        // Public Properties
        // =====================================================================

        /// <summary>The script asset currently being executed.</summary>
        public StoryFlowScriptAsset CurrentScript { get; set; }

        /// <summary>The project asset that owns all scripts and global data.</summary>
        public StoryFlowProjectAsset Project { get; set; }

        /// <summary>ID of the node currently being processed or waiting at.</summary>
        public string CurrentNodeId { get; set; }

        /// <summary>True when execution is paused waiting for user input (dialogue choice).</summary>
        public bool IsWaitingForInput { get; set; }

        /// <summary>True while the engine is actively processing nodes.</summary>
        public bool IsExecuting { get; set; }

        /// <summary>True when execution is paused by external request.</summary>
        public bool IsPaused { get; set; }

        /// <summary>The currently built dialogue state for display.</summary>
        public StoryFlowDialogueState CurrentDialogueState { get; set; }

        /// <summary>Persistent background image set by SetBackgroundImage nodes.</summary>
        public Sprite PersistentBackgroundImage { get; set; }

        /// <summary>
        /// Language code for localized string lookup (e.g. "en"). Set by the owning StoryFlowComponent.
        ///
        /// PRE-LOCALIZATION ONLY. Once the loaded project ships a localization.json the language
        /// is the PLAYER'S and game-wide, StoryFlowManager.SetLanguage owns it, and this field
        /// is ignored — see <see cref="ActiveLanguageCode"/>.
        /// </summary>
        public string LanguageCode { get; set; } = "en";

        /// <summary>
        /// Transient field set by the evaluator before calling FromNode methods.
        /// Holds the SourceHandle of the edge that led to the current evaluation,
        /// allowing RunScript output resolution to extract the variable identifier.
        /// </summary>
        public string LastSourceHandle { get; set; }

        /// <summary>
        /// Execution trace logging enabled. Set from the owning StoryFlowComponent's TraceEnabled field.
        /// When true, all execution events are logged with the [SF-TRACE] prefix for cross-runtime comparison.
        /// </summary>
        public bool TraceEnabled { get; set; }

        // =====================================================================
        // Internal State
        // =====================================================================

        private readonly List<CallFrame> callStack = new();
        private readonly List<FlowFrame> flowCallStack = new();
        private readonly List<LoopContext> loopStack = new();

        /// <summary>Deep copy of the current script's local variables.</summary>
        private Dictionary<string, StoryFlowVariable> localVariables = new();

        /// <summary>Reference to shared global variables from the manager.</summary>
        private Dictionary<string, StoryFlowVariable> externalGlobalVariables;

        /// <summary>Reference to shared character data from the manager.</summary>
        private Dictionary<string, StoryFlowCharacterData> externalCharacters;

        /// <summary>
        /// Reference to the manager's character id bridge (characters engine contract §3):
        /// character file id → <see cref="externalCharacters"/> key. Empty on a pre-P4
        /// import, so id lookups fall back to paths.
        /// </summary>
        private Dictionary<string, string> externalCharacterIdBridge;

        /// <summary>Reference to shared set tracking once-only option usage.</summary>
        private HashSet<string> externalUsedOnceOnlyOptions;

        /// <summary>Per-node cached runtime states.</summary>
        private readonly Dictionary<string, NodeRuntimeState> nodeRuntimeStates = new();

        /// <summary>
        /// Tracks source node IDs for which an "unsupported node type" warning has already
        /// been logged during the current dialogue run. Prevents log spam when the same
        /// unknown node is read by an evaluator multiple times per pass.
        /// Reset by <see cref="Initialize"/> and <see cref="Reset"/>.
        /// </summary>
        private readonly HashSet<string> warnedUnknownNodes = new();

        /// <summary>
        /// Tracks map op node IDs already warned about missing keyType/valueType data
        /// during the current dialogue run (same dedup pattern as <see cref="warnedUnknownNodes"/>).
        /// Reset by <see cref="Initialize"/> and <see cref="Reset"/>.
        /// </summary>
        private readonly HashSet<string> warnedMapNodes = new();

        /// <summary>
        /// Degraded data-asset accessors already warned about, keyed (nodeId, reason) so a
        /// node with two problems reports both once and a re-broken node stays quiet until the
        /// next run. Reset by <see cref="Initialize"/> and <see cref="Reset"/> — that is the
        /// re-arm the contract's "once per node, re-armed on game reset/restart" asks for
        /// (§6), and the reference latch (node.data._sfdWarned, cleared by resetGame) does the
        /// same thing on its own graph.
        /// </summary>
        private readonly HashSet<(string NodeId, string Reason)> warnedDataAssetNodes = new();

        /// <summary>
        /// Degraded character-id resolutions already warned about, keyed (id, reason) with
        /// the reasons "dangling" (an id with no bridge entry) and "unloaded" (a bridge hit
        /// whose record is missing from the runtime character table). Same latch shape and
        /// re-arm points as <see cref="warnedDataAssetNodes"/>: reset by
        /// <see cref="Initialize"/> and <see cref="Reset"/>.
        /// </summary>
        private readonly HashSet<(string Id, string Reason)> warnedCharacterIds = new();

        /// <summary>
        /// How many data-asset warnings this context has actually EMITTED. A test seam, and a
        /// necessary one: the latch set alone cannot tell "warned once, then suppressed" from
        /// "latched but never emitted", which is exactly the mutation once-ness tests must
        /// kill. Deliberately NOT reset by Initialize/Reset — it counts emissions over the
        /// context's life, so a test can watch the latch re-arm and see the counter move again.
        /// </summary>
        internal int DataAssetWarningsEmitted;

        /// <summary>
        /// How many character-id warnings this context has actually EMITTED — the same test
        /// seam as <see cref="DataAssetWarningsEmitted"/>, for the same reason: the latch
        /// set alone cannot tell "warned once, then suppressed" from "latched but never
        /// emitted". Deliberately NOT reset by Initialize/Reset, so a test can watch the
        /// latch re-arm and see the counter move again.
        /// </summary>
        internal int CharacterWarningsEmitted;

        /// <summary>
        /// The manager-owned .sfd Data Asset store (seed + overlay), or null when there is
        /// none — a context built without a manager, or one that has been Reset. Data-asset
        /// accessors treat a null or invalid store as a dead reference and degrade, so this
        /// never needs a null object.
        /// </summary>
        internal StoryFlowDataAssetStoreRef DataAssetStore { get; private set; }

        /// <summary>Current recursion depth for expression evaluation.</summary>
        public int EvaluationDepth { get; set; }

        /// <summary>The next node to process. Set by handlers to continue the iterative loop.</summary>
        [NonSerialized] public StoryFlowNode NextNode;

        /// <summary>When true, the iterative loop pauses (dialogue waiting, end reached, error).</summary>
        [NonSerialized] public bool ShouldPause;

        /// <summary>Lazy-built index: variable name -> variable id for local variables.</summary>
        private Dictionary<string, string> localVariableNameIndex;

        /// <summary>Lazy-built index: variable name -> variable id for global variables.</summary>
        private Dictionary<string, string> globalVariableNameIndex;

        /// <summary>
        /// Tracks the last dialogue node ID visited. Used by Set* nodes with no outgoing
        /// edge to know which dialogue to return to for re-rendering.
        /// </summary>
        public string LastDialogueNodeId { get; set; }

        /// <summary>
        /// Set by ProcessNextNode when the target is a Dialogue node (entered via flow edge).
        /// When false, the dialogue is being re-rendered from a Set* fallthrough — audio should
        /// not restart. Matches Godot's entering_dialogue_via_edge approach.
        /// </summary>
        public bool EnteringDialogueViaEdge { get; set; }

        // =====================================================================
        // Initialization
        // =====================================================================

        /// <summary>
        /// Initializes the execution context for a given script.
        /// Deep-copies local variables and stores references to shared global state.
        /// </summary>
        public void Initialize(
            StoryFlowScriptAsset script,
            Dictionary<string, StoryFlowVariable> globalVars,
            Dictionary<string, StoryFlowCharacterData> characters,
            HashSet<string> usedOnceOnlyOptions,
            StoryFlowDataAssetStoreRef dataAssetStore = null,
            Dictionary<string, string> characterIdBridge = null)
        {
            CurrentScript = script;
            DataAssetStore = dataAssetStore;
            CurrentNodeId = script != null ? script.StartNodeId : "0";

            externalGlobalVariables = globalVars ?? new Dictionary<string, StoryFlowVariable>();
            externalCharacters = characters ?? new Dictionary<string, StoryFlowCharacterData>();
            externalCharacterIdBridge = characterIdBridge ?? new Dictionary<string, string>();
            externalUsedOnceOnlyOptions = usedOnceOnlyOptions ?? new HashSet<string>();

            // Deep copy script's local variables so mutations don't affect the asset
            localVariables = new Dictionary<string, StoryFlowVariable>();
            if (script != null && script.Variables != null)
            {
                foreach (var kvp in script.Variables)
                {
                    var copy = new StoryFlowVariable(kvp.Value);
                    // Re-hydrate array from DefaultValueJson if ArrayValue was lost
                    // (e.g. after Unity serialization round-trip, since ArrayValue is [NonSerialized])
                    if (copy.IsArray && copy.Value.ArrayValue == null && !string.IsNullOrEmpty(copy.DefaultValueJson))
                    {
                        copy.Value = StoryFlowVariant.DeserializeArrayFromJson(copy.Type, copy.DefaultValueJson);
                    }
                    // Same for maps (MapValue is [NonSerialized] too)
                    if (copy.Type == StoryFlowVariableType.Map && copy.Value.MapValue == null && !string.IsNullOrEmpty(copy.DefaultValueJson))
                    {
                        copy.Value = StoryFlowVariant.DeserializeMapFromJson(copy.KeyType, copy.ValueType, copy.DefaultValueJson);
                    }
                    localVariables[kvp.Key] = copy;
                }
            }

            // Invalidate name indices (will be rebuilt lazily)
            localVariableNameIndex = null;
            globalVariableNameIndex = null;

            // Ensure connection indices are built
            if (script != null)
                script.BuildIndices();

            CurrentDialogueState = new StoryFlowDialogueState();
            IsWaitingForInput = false;
            IsExecuting = false;
            IsPaused = false;
            EvaluationDepth = 0;
            NextNode = null;
            ShouldPause = false;
            LastDialogueNodeId = null;
            EnteringDialogueViaEdge = false;
            warnedUnknownNodes.Clear();
            warnedMapNodes.Clear();
            warnedDataAssetNodes.Clear();
            warnedCharacterIds.Clear();
        }

        // =====================================================================
        // Variable Lookup
        // =====================================================================

        /// <summary>
        /// Finds a variable by its ID. Checks local variables first, then global.
        /// </summary>
        public StoryFlowVariable FindVariable(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (localVariables.TryGetValue(id, out var localVar))
                return localVar;

            if (externalGlobalVariables != null && externalGlobalVariables.TryGetValue(id, out var globalVar))
                return globalVar;

            return null;
        }

        /// <summary>
        /// Finds a variable by its Name field. Uses lazy-built name-to-id indices for performance.
        /// </summary>
        public StoryFlowVariable FindVariableByName(string name, bool searchLocal = true, bool searchGlobal = true)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (searchLocal)
            {
                if (localVariableNameIndex == null)
                    RebuildLocalNameIndex();

                if (localVariableNameIndex.TryGetValue(name, out var localId) &&
                    localVariables.TryGetValue(localId, out var localVar))
                {
                    return localVar;
                }
            }

            if (searchGlobal)
            {
                if (globalVariableNameIndex == null)
                    RebuildGlobalNameIndex();

                if (globalVariableNameIndex.TryGetValue(name, out var globalId) &&
                    externalGlobalVariables.TryGetValue(globalId, out var globalVar))
                {
                    return globalVar;
                }
            }

            return null;
        }

        /// <summary>Rebuilds the local variable name-to-id index.</summary>
        public void RebuildLocalNameIndex()
        {
            localVariableNameIndex = new Dictionary<string, string>();
            foreach (var kvp in localVariables)
            {
                if (!string.IsNullOrEmpty(kvp.Value.Name))
                    localVariableNameIndex[kvp.Value.Name] = kvp.Key;
            }
        }

        /// <summary>Rebuilds the global variable name-to-id index.</summary>
        public void RebuildGlobalNameIndex()
        {
            globalVariableNameIndex = new Dictionary<string, string>();
            if (externalGlobalVariables == null) return;

            foreach (var kvp in externalGlobalVariables)
            {
                if (!string.IsNullOrEmpty(kvp.Value.Name))
                    globalVariableNameIndex[kvp.Value.Name] = kvp.Key;
            }
        }

        /// <summary>
        /// Invalidates the local name index so it will be rebuilt on next lookup.
        /// Call this after modifying local variables.
        /// </summary>
        public void InvalidateLocalNameIndex()
        {
            localVariableNameIndex = null;
        }

        /// <summary>
        /// Invalidates the global name index so it will be rebuilt on next lookup.
        /// Call this after modifying global variables.
        /// </summary>
        public void InvalidateGlobalNameIndex()
        {
            globalVariableNameIndex = null;
        }

        /// <summary>Gets the local variables dictionary (current script's deep-copied variables).</summary>
        public Dictionary<string, StoryFlowVariable> LocalVariables => localVariables;

        /// <summary>Gets the external global variables dictionary reference.</summary>
        public Dictionary<string, StoryFlowVariable> GlobalVariables => externalGlobalVariables;

        /// <summary>Gets the external characters dictionary reference.</summary>
        public Dictionary<string, StoryFlowCharacterData> Characters => externalCharacters;

        // =====================================================================
        // Node Runtime State
        // =====================================================================

        /// <summary>
        /// Gets or creates the runtime state for a specific node.
        /// </summary>
        public NodeRuntimeState GetNodeRuntimeState(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return new NodeRuntimeState();

            if (!nodeRuntimeStates.TryGetValue(nodeId, out var state))
            {
                state = new NodeRuntimeState();
                nodeRuntimeStates[nodeId] = state;
            }

            return state;
        }

        /// <summary>
        /// Clears all cached node evaluation results.
        /// Should be called at the start of each processing pass.
        /// </summary>
        public void ClearNodeRuntimeStates()
        {
            foreach (var kvp in nodeRuntimeStates)
                kvp.Value.ClearCache();
        }

        /// <summary>
        /// Logs a one-shot warning when an evaluator is asked to read a value from a node
        /// whose type the plugin does not recognize (<see cref="StoryFlowNodeType.Unknown"/>).
        /// Subsequent reads of the same node within the current dialogue run are silent so
        /// the console does not flood. Returns true if the node was Unknown (the caller
        /// should then fall through to its default-value path), false otherwise.
        /// </summary>
        public bool MaybeWarnUnknownNode(StoryFlowNode node)
        {
            if (node == null || node.Type != StoryFlowNodeType.Unknown)
                return false;

            if (warnedUnknownNodes.Add(node.Id))
            {
                var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                Debug.LogWarning($"[StoryFlow] Unsupported node type '{typeName}' at node {node.Id}, returning default value");
            }

            return true;
        }

        /// <summary>
        /// Logs a one-shot warning when a map op node is missing its keyType/valueType
        /// data (the map input handle cannot be built without them, so resolution fails
        /// to defaults). Deduped per node per dialogue run. Returns true if the warning
        /// condition holds (caller should fall through to its default-value path).
        /// </summary>
        public bool MaybeWarnMissingMapTypes(StoryFlowNode node)
        {
            if (node == null) return true;

            if (warnedMapNodes.Add(node.Id))
            {
                var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                Debug.LogWarning($"[StoryFlow] Map node '{typeName}' at node {node.Id} is missing keyType/valueType data, returning default value");
            }

            return true;
        }

        // =====================================================================
        // Data Assets (.sfd) — the store forward and the degraded ladder
        // =====================================================================

        /// <summary>
        /// Records a session write through this context's store (contract §5), reporting
        /// whether it landed. No store means no write, quietly — the caller reached here past
        /// a ladder that already refused and warned for that case.
        /// </summary>
        internal bool TrySetDataAsset(string assetId, string variableId, StoryFlowVariant value)
        {
            if (DataAssetStore == null || !DataAssetStore.IsValid) return false;
            return StoryFlowDataAssetStore.TrySet(
                DataAssetStore.Seed, DataAssetStore.Overlay, assetId, variableId, value);
        }

        /// <summary>
        /// Claims the ONE warning a degraded data-asset accessor gets per node per reason
        /// (contract §6): true the first time, false forever after, until the latch re-arms on
        /// the next Initialize/Reset.
        ///
        /// The caller EMITS. This returns a bool rather than taking a message because these
        /// nodes are read from render paths — a dialogue's option conditions re-evaluate on
        /// every render — so the suppressed call is the common one by orders of magnitude, and
        /// a message parameter means building an interpolated string on every one of them to
        /// throw it away here. Building it inside the `if` costs nothing when nothing is logged.
        ///
        /// The trade that buys is real: the counter moves on the CLAIM while the log lives in
        /// the caller, so a caller that claims and then forgets to log leaves the counter saying
        /// a warning happened that the console never saw. Nothing here can catch that — what
        /// does is DegradedFixtureReadsAndWarnOnce, which asserts the counter delta AND the
        /// captured log lines for every one of the fixture's cases, so the two halves cannot
        /// drift apart unnoticed. Add a rung to this ladder and add it to that test.
        ///
        /// The reason is part of the key because the reasons have different FIXES; the tokens
        /// themselves are informational (§9.1), so nothing matches on them.
        /// </summary>
        internal bool ShouldWarnDataAsset(string nodeId, string reason)
        {
            if (!warnedDataAssetNodes.Add((nodeId, reason))) return false;
            DataAssetWarningsEmitted++;
            return true;
        }

        /// <summary>
        /// The character id bridge this context resolves through: character file id →
        /// <see cref="externalCharacters"/> key. Never null after Initialize.
        /// </summary>
        internal Dictionary<string, string> CharacterIdBridge => externalCharacterIdBridge;

        /// <summary>
        /// The character-id twin of <see cref="ShouldWarnDataAsset"/>: same claim-then-log
        /// contract, same trade (the counter moves on the CLAIM while the log lives in the
        /// caller). Reasons are "dangling" and "unloaded" — see
        /// <see cref="warnedCharacterIds"/>.
        /// </summary>
        internal bool ShouldWarnCharacterId(string id, string reason)
        {
            if (!warnedCharacterIds.Add((id, reason))) return false;
            CharacterWarningsEmitted++;
            return true;
        }

        /// <summary>
        /// The assetId an accessor reads and writes through: the <c>assetId</c> of the
        /// getDataAsset PILL wired into its dataAsset pin, or empty when there is nothing
        /// usable upstream (nothing wired, an unbound pill, or a wire from a node that is not
        /// a pill).
        ///
        /// SINGLE HOP is sufficient, not a limitation: the editor collapses reroute elbows
        /// before export, so a wire that ran through elbows on the canvas arrives here as a
        /// direct pill -> accessor edge. The node-type check keeps that honest — anything else
        /// on the far end degrades instead of having an "assetId" field speculatively read off
        /// it (contract §6 row 1).
        /// </summary>
        private string ResolveDataAssetId(StoryFlowNode accessor)
        {
            if (CurrentScript == null) return "";

            var edge = CurrentScript.FindInputEdge(accessor.Id, StoryFlowHandles.In_DataAssetRef);
            if (edge == null) return "";

            var source = CurrentScript.GetNode(edge.Source);
            if (source == null || source.Type != StoryFlowNodeType.GetDataAsset) return "";

            return source.GetData("assetId");
        }

        /// <summary>
        /// THE degradation ladder both accessor arms walk (contract §6), in order: nodata,
        /// unwired, deadref, missing, changed. Each rung latches its warning and answers false;
        /// success hands back the assetId the caller writes through.
        ///
        /// This is the WRITE half — see <see cref="TryReadDataAssetBinding"/> for the read half,
        /// which is the same ladder with the value picked up on the same walk.
        ///
        /// ONE ladder for Get and Set, ON PURPOSE. A reason honored on the read path but not
        /// the write path gives you an accessor that reads the declared default while its twin
        /// writes an overlay entry SHADOWING that default for the rest of the session (and
        /// cascading to every descendant, if it landed on a base).
        ///
        /// The DECLARATION is deliberately not handed back. Past the declMatches rung the
        /// accessor's own snapshot (variableType / isArray / keyType / valueType) is by
        /// definition the chain's declared shape, so every caller already holds it — and the
        /// declaration is a live reference into the seed, which is a thing to hand around as
        /// little as possible (see the store's class header). The degraded TYPE DEFAULT is the
        /// snapshot's too, never the declaration's: a node spawned against a string reads ""
        /// when the chain has moved to integer.
        /// </summary>
        internal bool TryResolveDataAssetBinding(StoryFlowNode accessor, out string assetId)
        {
            assetId = "";
            if (!TryBindDataAssetAccessor(accessor, out var resolvedId, out var variableId)) return false;

            var status = StoryFlowDataAssetStore.CheckBound(
                DataAssetStore.Seed, resolvedId, variableId, PinShapeOf(accessor));

            if (!ReportDataAssetBinding(accessor, resolvedId, variableId, status)) return false;

            assetId = resolvedId;
            return true;
        }

        /// <summary>
        /// The READ half of the ladder: the same five reasons, and on success the resolved
        /// value, taken from the SAME chain walk that settled the ladder rather than from a
        /// second one behind it.
        ///
        /// THE VALUE COMES OUT OF THE LOCALIZATION GATE, the same one the host mirrors read
        /// through (StoryFlowDataAssetStore.ReadOut, reached from here via ReadBound and from
        /// them via TryRead): a .sfd value read by graph code is read by a PLAYER, so a DECLARED
        /// string one resolves through the string tables while an override and a session write
        /// are handed back verbatim (localization spec §2's amendment of 2026-08-27). The
        /// language is <see cref="ActiveLanguageCode"/> — the LIVE one, so a mid-session
        /// SetLanguage lands on the very next .sfd read rather than the next dialogue.
        ///
        /// The store consults the PROJECT's ladder directly, not this context's
        /// <see cref="LookUpLocalized"/>: that one probes the current SCRIPT's table first, and
        /// a .sfd id is keyed by data-assets.json, which the importer merges into the project
        /// globals. So a .sfd value reads the same inside a dialogue and outside one.
        /// </summary>
        internal bool TryReadDataAssetBinding(
            StoryFlowNode accessor, out string assetId, out StoryFlowVariant value)
        {
            assetId = "";
            value = null;
            if (!TryBindDataAssetAccessor(accessor, out var resolvedId, out var variableId)) return false;

            var status = StoryFlowDataAssetStore.ReadBound(
                DataAssetStore.Seed, DataAssetStore.Overlay, Project, ActiveLanguageCode,
                resolvedId, variableId, PinShapeOf(accessor), out var resolved);

            if (!ReportDataAssetBinding(accessor, resolvedId, variableId, status)) return false;

            assetId = resolvedId;
            value = resolved;
            return true;
        }

        /// <summary>
        /// The variable NAMES the asset wired into a Get Variable Names node's dataAsset
        /// pin declares (contract §11.1): the store's chain enumeration, root-first,
        /// declarations only, reached over the same single-hop pill walk the accessor
        /// ladder uses (<see cref="ResolveDataAssetId"/> — the wire is the binding, §2.2).
        ///
        /// Every degraded path — an unwired pin, a non-pill source, a dead ref, an asset
        /// the seed does not carry, an absent store — answers an EMPTY list with NO
        /// warning, latched or otherwise: unlike the bound accessors this node has no
        /// per-variable binding to be wrong about, an empty list IS the family's degraded
        /// answer, and §11.1 adds no warning tokens.
        /// </summary>
        internal List<string> ReadDataAssetVariableNames(StoryFlowNode node)
        {
            if (node == null || DataAssetStore == null || !DataAssetStore.IsValid)
                return new List<string>();

            return StoryFlowDataAssetStore.VariableNames(
                DataAssetStore.Seed, ResolveDataAssetId(node));
        }

        /// <summary>
        /// The spawn-time declared shape an accessor's pins were built from, read off the node
        /// in ONE place so the read and write halves of the ladder cannot come to hold different
        /// field names for the same four values.
        /// </summary>
        private static StoryFlowDataAssetPinShape PinShapeOf(StoryFlowNode accessor)
        {
            return new StoryFlowDataAssetPinShape(
                accessor.GetData("variableType"),
                accessor.GetDataBool("isArray"),
                accessor.GetData("keyType"),
                accessor.GetData("valueType"));
        }

        /// <summary>
        /// The ladder's first three rungs, which are GRAPH questions and so are settled before
        /// there is anything to walk: the node's own binding data, the wire to its pill, and
        /// whether this context has a store at all.
        /// </summary>
        private bool TryBindDataAssetAccessor(
            StoryFlowNode accessor, out string resolvedId, out string variableId)
        {
            resolvedId = "";
            variableId = "";
            if (accessor == null) return false;

            variableId = accessor.GetData("variableId");
            if (string.IsNullOrEmpty(variableId))
            {
                if (ShouldWarnDataAsset(accessor.Id, "nodata"))
                {
                    Debug.LogWarning(
                        $"[StoryFlow] Data Asset accessor has no variable binding: node {accessor.Id}");
                }
                return false;
            }

            resolvedId = ResolveDataAssetId(accessor);
            if (string.IsNullOrEmpty(resolvedId))
            {
                if (ShouldWarnDataAsset(accessor.Id, "unwired"))
                {
                    Debug.LogWarning(
                        $"[StoryFlow] Data Asset accessor has no Data Asset connected: node {accessor.Id}");
                }
                return false;
            }

            // No store at all is latched as a DEAD REFERENCE: from the node's point of view its
            // asset is not there, and the alternative — a silent false — reads exactly like a
            // healthy miss.
            if (DataAssetStore == null || !DataAssetStore.IsValid)
            {
                if (ShouldWarnDataAsset(accessor.Id, "deadref"))
                {
                    Debug.LogWarning(
                        $"[StoryFlow] Data Asset store unavailable: {resolvedId} (node {accessor.Id})");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Names the walk's answer for the author, once per node per reason, and reports
        /// whether the binding is usable.
        /// </summary>
        private bool ReportDataAssetBinding(
            StoryFlowNode accessor, string resolvedId, string variableId,
            StoryFlowDataAssetBinding status)
        {
            switch (status)
            {
                case StoryFlowDataAssetBinding.Ok:
                    return true;

                case StoryFlowDataAssetBinding.DeadRef:
                    if (ShouldWarnDataAsset(accessor.Id, "deadref"))
                    {
                        Debug.LogWarning(
                            $"[StoryFlow] Data Asset not found: {resolvedId} (node {accessor.Id})");
                    }
                    return false;

                case StoryFlowDataAssetBinding.Missing:
                    if (ShouldWarnDataAsset(accessor.Id, "missing"))
                    {
                        Debug.LogWarning("[StoryFlow] Data Asset variable not found: " +
                                         $"{resolvedId}.{variableId} (node {accessor.Id})");
                    }
                    return false;

                case StoryFlowDataAssetBinding.Changed:
                    if (ShouldWarnDataAsset(accessor.Id, "changed"))
                    {
                        Debug.LogWarning(
                            "[StoryFlow] Data Asset variable type changed since this node was made: " +
                            $"{resolvedId}.{variableId} (node {accessor.Id})");
                    }
                    return false;

                // Every named rung is spelled out above so this one stays UNREACHABLE. A member
                // added to the enum without a case here would otherwise land on whichever arm
                // happened to be the default and be reported as something it is not — a type
                // change, when it might be anything. Refusing under its own name says the ladder
                // grew and this switch did not.
                default:
                    if (ShouldWarnDataAsset(accessor.Id, "unrecognised"))
                    {
                        Debug.LogWarning(
                            $"[StoryFlow] Data Asset binding refused ({status}): " +
                            $"{resolvedId}.{variableId} (node {accessor.Id})");
                    }
                    return false;
            }
        }

        // =====================================================================
        // Call Stack (RunScript — cross-script calls with return)
        // =====================================================================

        /// <summary>
        /// Pushes the current script state onto the call stack before entering a new script.
        /// </summary>
        public bool PushCallFrame(string returnNodeId)
        {
            if (callStack.Count >= MaxCallDepth)
            {
                Debug.LogWarning($"[StoryFlow] Call stack overflow: max depth {MaxCallDepth} exceeded.");
                return false;
            }

            var frame = new CallFrame
            {
                ScriptPath = CurrentScript != null ? CurrentScript.ScriptPath : "",
                ReturnNodeId = returnNodeId,
                Script = CurrentScript != null
                    ? new System.WeakReference<StoryFlowScriptAsset>(CurrentScript)
                    : null,
            };

            // Deep copy current local variables. Map storage deliberately SHARES the LIVE
            // entry list instead: HTML call frames hold live variable references
            // (runtime-core.js pushCallStack saves gameState.variables.slice()), so map
            // aliasing established before a runScript call must survive the call and restore
            // (the Unreal port shares map storage the same way via TSharedPtr). Scalars and
            // arrays keep the pre-existing deep-copy semantics. The live list is detached
            // before the copy ctor runs so it does not deep-copy entries the share would
            // immediately discard.
            foreach (var kvp in localVariables)
            {
                StoryFlowVariable copy;
                if (kvp.Value.Type == StoryFlowVariableType.Map)
                {
                    var liveMap = kvp.Value.Value.MapValue;
                    kvp.Value.Value.MapValue = null;
                    copy = new StoryFlowVariable(kvp.Value);
                    kvp.Value.Value.MapValue = liveMap;
                    copy.Value.MapValue = liveMap;
                }
                else
                {
                    copy = new StoryFlowVariable(kvp.Value);
                }
                frame.SavedLocalVariables[kvp.Key] = copy;
            }

            // Save flow call stack
            foreach (var ff in flowCallStack)
                frame.SavedFlowStack.Add(new FlowFrame(ff.FlowId));

            callStack.Add(frame);

            // Clear flow stack for the new script context
            flowCallStack.Clear();

            return true;
        }

        /// <summary>
        /// Pops the most recent call frame and restores previous script state.
        /// Returns the popped frame, or null if the stack is empty.
        /// </summary>
        public CallFrame PopCallFrame()
        {
            if (callStack.Count == 0) return null;

            var frame = callStack[callStack.Count - 1];
            callStack.RemoveAt(callStack.Count - 1);

            // Restore flow call stack
            flowCallStack.Clear();
            foreach (var ff in frame.SavedFlowStack)
                flowCallStack.Add(new FlowFrame(ff.FlowId));

            // Restore local variables
            localVariables.Clear();
            foreach (var kvp in frame.SavedLocalVariables)
            {
                StoryFlowVariable copy;
                if (kvp.Value.Type == StoryFlowVariableType.Map && kvp.Value.Value.MapValue != null)
                {
                    // Restore the SAVED live entry list, not a copy — map aliasing
                    // (with globals or sibling locals) must survive the runScript
                    // round-trip. See the matching share in PushCallFrame. As there,
                    // detach the list around the copy ctor so it does not deep-copy
                    // entries the share would immediately discard.
                    var savedMap = kvp.Value.Value.MapValue;
                    kvp.Value.Value.MapValue = null;
                    copy = new StoryFlowVariable(kvp.Value);
                    kvp.Value.Value.MapValue = savedMap;
                    copy.Value.MapValue = savedMap;
                }
                else
                {
                    copy = new StoryFlowVariable(kvp.Value);
                    // Re-hydrate array from DefaultValueJson if ArrayValue was lost
                    if (copy.IsArray && copy.Value.ArrayValue == null && !string.IsNullOrEmpty(copy.DefaultValueJson))
                    {
                        copy.Value = StoryFlowVariant.DeserializeArrayFromJson(copy.Type, copy.DefaultValueJson);
                    }
                    if (copy.Type == StoryFlowVariableType.Map && !string.IsNullOrEmpty(copy.DefaultValueJson))
                    {
                        // MapValue is [NonSerialized]; re-hydrate like arrays when lost
                        copy.Value = StoryFlowVariant.DeserializeMapFromJson(copy.KeyType, copy.ValueType, copy.DefaultValueJson);
                    }
                }
                localVariables[kvp.Key] = copy;
            }

            // Invalidate name index since we swapped local variables
            localVariableNameIndex = null;

            return frame;
        }

        /// <summary>Current depth of the script call stack.</summary>
        public int CallStackDepth => callStack.Count;

        // =====================================================================
        // Flow Call Stack (RunFlow — in-script jump, depth tracking only)
        // =====================================================================

        /// <summary>
        /// Pushes a flow frame for depth tracking. Flows are jumps, not calls.
        /// </summary>
        public bool PushFlowFrame(string flowId)
        {
            if (flowCallStack.Count >= MaxFlowDepth)
            {
                Debug.LogWarning($"[StoryFlow] Flow stack overflow: max depth {MaxFlowDepth} exceeded.");
                return false;
            }

            flowCallStack.Add(new FlowFrame(flowId));
            return true;
        }

        /// <summary>
        /// Pops the most recent flow frame.
        /// </summary>
        public void PopFlowFrame()
        {
            if (flowCallStack.Count > 0)
                flowCallStack.RemoveAt(flowCallStack.Count - 1);
        }

        /// <summary>
        /// Returns the most recent flow frame without removing it, or null if the stack is empty.
        /// </summary>
        public FlowFrame PeekFlowFrame()
        {
            return flowCallStack.Count > 0 ? flowCallStack[flowCallStack.Count - 1] : null;
        }

        /// <summary>Current depth of the flow call stack.</summary>
        public int FlowStackDepth => flowCallStack.Count;

        // =====================================================================
        // Loop Stack (forEach iterations)
        // =====================================================================

        /// <summary>Pushes a new loop context onto the loop stack.</summary>
        public void PushLoop(LoopContext ctx)
        {
            loopStack.Add(ctx);
        }

        /// <summary>Pops and returns the most recent loop context, or null if empty.</summary>
        public LoopContext PopLoop()
        {
            if (loopStack.Count == 0) return null;
            var ctx = loopStack[loopStack.Count - 1];
            loopStack.RemoveAt(loopStack.Count - 1);
            return ctx;
        }

        /// <summary>Returns the current (topmost) loop context without removing it.</summary>
        public LoopContext PeekLoop()
        {
            return loopStack.Count > 0 ? loopStack[loopStack.Count - 1] : null;
        }

        /// <summary>Current depth of the loop stack.</summary>
        public int LoopStackDepth => loopStack.Count;

        /// <summary>Returns the loop context at the given index (0 = bottom), or null if out of range.</summary>
        public LoopContext PeekLoopAt(int index)
        {
            return index >= 0 && index < loopStack.Count ? loopStack[index] : null;
        }

        /// <summary>Clears the entire loop stack. Called when reaching an End node.</summary>
        public void ClearLoopStack()
        {
            loopStack.Clear();
        }

        // =====================================================================
        // String Lookup
        // =====================================================================

        /// <summary>
        /// Looks up a string key, checking the current script's strings first,
        /// then the project's global strings.
        ///
        /// A RAW, EXACT-KEY probe: the caller supplies the full `code.id` table key and no
        /// language tier runs. It is NOT the localized door — <see cref="ResolveStringKey"/>
        /// and <see cref="LookUpLocalized"/> are, and they own the whole ladder (§9). A caller
        /// that builds its own `LanguageCode + "." + key` here is bypassing the overlay and the
        /// source-table fall-through, which is exactly the silent defect
        /// <see cref="LookUpLocalizedIn"/> exists to make impossible.
        /// </summary>
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // Script-local strings
            if (CurrentScript != null)
            {
                var scriptStr = CurrentScript.GetString(key);
                if (scriptStr != null) return scriptStr;
            }

            // Project global strings
            if (Project != null)
            {
                var globalStr = Project.GetGlobalString(key);
                if (globalStr != null) return globalStr;
            }

            return null;
        }

        /// <summary>
        /// THE ONE STRING RESOLUTION LADDER (localization spec §9), shared by both doors this
        /// plugin has — this context's <see cref="ResolveStringKey"/> / <see cref="LookUpLocalized"/>
        /// during dialogue and StoryFlowComponent.ResolveString outside it — for the same
        /// reason <see cref="ResolveCharacterKeyIn"/> is shared: a second lookup that could
        /// drift never exists. The two doors differ ONLY in whether a current script is passed,
        /// so the difference is an argument rather than a second ladder. (The Unreal port keeps
        /// two ladders because its script probe interleaves differently; this one does not, so
        /// sharing is the correct shape here.)
        ///
        /// Returns null when nothing anywhere carries the id — callers apply their own miss
        /// policy (the raw value, or empty for a dialogue field). The tiers:
        ///
        ///  1. THE LOCALIZATION OVERLAY: the sidecar's row for this id in the language being
        ///     read. Absent for a pre-localization export, for the source language and for an
        ///     id the sidecar does not carry — all of which fall through. The tables are FULL
        ///     and PRE-RESOLVED, so nothing here computes a status or compares a hash.
        ///  2. THE KEYING ARTIFACT'S OWN TABLE, current script first then the project globals
        ///     characters.json merges into. The language-prefixed probe comes FIRST and is the
        ///     PRE-LOCALIZATION behavior kept exactly as it was: an artifact strings block may
        ///     itself carry more than one language block and the importer flattens each to
        ///     `code.key`. The source-language probe beside it is the step the sidecar makes
        ///     necessary — every export this editor writes keys its artifact strings by the
        ///     source language alone, so once the language being read is a target language the
        ///     first probe cannot hit and this is the fall-through the contract names
        ///     ("-> the keying artifact's own strings.en"). The two probes are the SAME key
        ///     whenever the codes agree, which is every pre-localization project, so the second
        ///     one is SKIPPED in that case rather than repeated — unlike the Unreal port, which
        ///     computes and probes both keys unconditionally.
        ///  3. the caller's miss policy (never a lookup failure a caller has to test for).
        ///
        /// THE LOOKUP RUNS ON THE AUTHORED TEMPLATE. Every caller that interpolates
        /// `{Variable}` tokens calls StoryFlowInterpolation.Interpolate on the RESULT of this
        /// function, never the other way round — a translated line is authored with the same
        /// tokens as the source line, so interpolating first would hand this lookup a string no
        /// table was ever keyed by. That failure is invisible: the text still renders, in the
        /// source language, and only for lines that happen to carry a token.
        /// </summary>
        internal static string LookUpLocalizedIn(
            StoryFlowProjectAsset project, StoryFlowScriptAsset script, string key, string languageCode)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // TIER 1 — the overlay. The empty-text guard lives inside FindLocalizedString.
            if (project != null)
            {
                var localized = project.FindLocalizedString(key, languageCode);
                if (localized != null) return localized;
            }

            // TIER 2 — the artifact's own table, legacy prefixed probe first.
            var direct = LookUpExactIn(project, script, languageCode + "." + key);
            if (direct != null) return direct;

            var sourceLanguage = project != null ? project.SourceLanguage : null;
            if (!string.IsNullOrEmpty(sourceLanguage) && sourceLanguage != languageCode)
            {
                var fromSource = LookUpExactIn(project, script, sourceLanguage + "." + key);
                if (fromSource != null) return fromSource;
            }

            // Older localized exports always put source text in strings.en, even when
            // sourceLanguage named another language. New artifacts use the actual code.
            if (project != null && project.HasLocalization && languageCode != "en" && sourceLanguage != "en")
                return LookUpExactIn(project, script, "en." + key);

            return null;
        }

        /// <summary>One exact table key, current script before project globals.</summary>
        private static string LookUpExactIn(
            StoryFlowProjectAsset project, StoryFlowScriptAsset script, string exactKey)
        {
            if (script != null)
            {
                var scriptStr = script.GetString(exactKey);
                if (scriptStr != null) return scriptStr;
            }
            if (project != null)
            {
                var globalStr = project.GetGlobalString(exactKey);
                if (globalStr != null) return globalStr;
            }
            return null;
        }

        /// <summary>
        /// THE LANGUAGE any lookup runs in (localization spec §9), for a given project.
        ///
        /// The MANAGER owns it whenever the loaded project carries a localization sidecar,
        /// because a language is the player's and game-wide, not a per-context or per-actor
        /// setting. Without a sidecar there is nothing to switch to and the caller's own
        /// pre-localization LanguageCode keeps its old meaning, so a project exported before
        /// localization existed behaves EXACTLY as it did — the presence of the file is the
        /// only branch, never a key count.
        /// </summary>
        internal static string ActiveLanguageCodeFor(StoryFlowProjectAsset project, string fallback)
        {
            if (project != null && project.HasLocalization)
            {
                var manager = StoryFlowManager.Instance;
                if (manager != null && manager.GetProject() == project)
                    return manager.GetLanguage();
                return project.SourceLanguage;
            }
            return fallback;
        }

        /// <summary>The language this context's lookups run in — see <see cref="ActiveLanguageCodeFor"/>.</summary>
        public string ActiveLanguageCode => ActiveLanguageCodeFor(Project, LanguageCode);

        /// <summary>
        /// The ladder above in this context's language, or null when nothing carries the id.
        /// The dialogue node handler's door: a dialogue field that resolves nowhere renders
        /// EMPTY rather than echoing its id, which is this engine's long-standing shape.
        /// </summary>
        public string LookUpLocalized(string key)
        {
            return LookUpLocalizedIn(Project, CurrentScript, key, ActiveLanguageCode);
        }

        /// <summary>
        /// Resolves a string key through the localized strings dictionary.
        /// The JSON export stores all string-type values (variable defaults, inline node values,
        /// array elements) as keys into the strings table. This method resolves a key to its
        /// actual text, falling back to the raw value if the key is not found.
        ///
        /// NEVER null and never an accidental empty string: a value that keyed no table
        /// anywhere is its own text.
        /// </summary>
        public string ResolveStringKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            return LookUpLocalized(key) ?? key;
        }

        internal string ResolveArrayString(StoryFlowVariant value)
        {
            if (value == null) return "";
            var text = value.GetString();
            return value.IsLiteralString ? text : ResolveStringKey(text);
        }

        // =====================================================================
        // Character Lookup
        // =====================================================================

        /// <summary>
        /// Finds runtime character data by character FILE id or path — every input runs
        /// through <see cref="ResolveCharacterKey"/>, THE resolution point, so no caller
        /// can resolve differently from another.
        /// </summary>
        public StoryFlowCharacterData FindCharacter(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;

            var recordKey = ResolveCharacterKey(idOrPath);

            if (externalCharacters != null && externalCharacters.TryGetValue(recordKey, out var character))
                return character;

            return null;
        }

        /// <summary>
        /// THE ONE character resolution point (contract §3/§4), over this context's bridge,
        /// character table and warn latch. See <see cref="ResolveCharacterKeyIn"/> for the
        /// ladder itself.
        /// </summary>
        internal string ResolveCharacterKey(string idOrPath)
        {
            return ResolveCharacterKeyIn(externalCharacterIdBridge, externalCharacters, idOrPath, this);
        }

        /// <summary>
        /// The static core of the resolution point, shared with the component's
        /// outside-dialogue lane so a second lookup that could drift never exists:
        ///
        ///  - id-shaped input (case-sensitive da_ prefix — ids preserve case, unlike the
        ///    authored names next door in StoryFlowCharacterTokens): a bridge hit whose
        ///    record is loaded answers the record key VERBATIM — never re-normalized, the
        ///    bridge value IS a store key by the import's one normalization pass, and a
        ///    normalize here could only mask an exporter that broke that guarantee. A
        ///    bridge hit whose record is MISSING from the character table warns once
        ///    ("unloaded") and misses whole; an id with no bridge entry warns once
        ///    ("dangling") and misses. A miss falls through to path treatment of the id
        ///    below (which normally misses too), so the caller's path field
        ///    (<see cref="ResolveCharacterRef"/>) or the existing missing-character
        ///    behavior takes over — never a crash.
        ///  - anything else: NormalizeCharacterPath, exactly as before P4.
        ///
        /// A null <paramref name="warnLatch"/> logs EVERY time — the DA access layer's
        /// null-latch precedent: out of dialogue there is no context to own the
        /// once-per-run latch, and failing open keeps the diagnostic loud.
        /// </summary>
        internal static string ResolveCharacterKeyIn(
            Dictionary<string, string> bridge,
            Dictionary<string, StoryFlowCharacterData> characters,
            string idOrPath,
            StoryFlowExecutionContext warnLatch)
        {
            if (StoryFlowCharacterTokens.IsCharacterIdRef(idOrPath))
            {
                if (bridge != null && bridge.TryGetValue(idOrPath, out var recordKey))
                {
                    if (characters != null && characters.ContainsKey(recordKey))
                        return recordKey;

                    if (warnLatch == null || warnLatch.ShouldWarnCharacterId(idOrPath, "unloaded"))
                    {
                        Debug.LogWarning($"[StoryFlow] Character id {idOrPath} maps to '{recordKey}', " +
                                         "which is not among the loaded runtime characters - " +
                                         "falling back to path resolution.");
                    }
                }
                else if (warnLatch == null || warnLatch.ShouldWarnCharacterId(idOrPath, "dangling"))
                {
                    Debug.LogWarning($"[StoryFlow] Character id {idOrPath} is not in this project's " +
                                     "character index - falling back to path resolution.");
                }
            }

            return StoryFlowPathNormalizer.NormalizeCharacterPath(idOrPath);
        }

        /// <summary>
        /// The wire-vocabulary entry: a node's id field first, its path sibling as the
        /// fall-back (contract §4). The path comes back VERBATIM, not normalized — pre-P4
        /// content must flow byte-identically through the path lane, warnings included
        /// (they print the authored spelling, as they always have);
        /// <see cref="FindCharacter"/> normalizes on lookup exactly as before.
        /// </summary>
        internal string ResolveCharacterRef(string characterId, string characterPath)
        {
            if (!string.IsNullOrEmpty(characterId))
            {
                var recordKey = ResolveCharacterKey(characterId);
                if (externalCharacters != null && externalCharacters.ContainsKey(recordKey))
                    return recordKey;
                // Dangling or unloaded id (warned once inside ResolveCharacterKey): the
                // path field is the contract's fall-back.
            }
            return characterPath;
        }

        // =====================================================================
        // Once-Only Options
        // =====================================================================

        /// <summary>Marks a once-only option as used so it won't appear again.</summary>
        public void MarkOnceOnlyUsed(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            externalUsedOnceOnlyOptions?.Add(key);
        }

        /// <summary>Returns true if the given once-only option has already been used.</summary>
        public bool IsOnceOnlyUsed(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return externalUsedOnceOnlyOptions != null && externalUsedOnceOnlyOptions.Contains(key);
        }

        // =====================================================================
        // Reset
        // =====================================================================

        /// <summary>
        /// Fully resets the execution context, clearing all stacks, caches, and state.
        /// </summary>
        public void Reset()
        {
            CurrentScript = null;
            CurrentNodeId = null;
            IsWaitingForInput = false;
            IsExecuting = false;
            IsPaused = false;
            EvaluationDepth = 0;
            NextNode = null;
            ShouldPause = false;
            LastDialogueNodeId = null;
            EnteringDialogueViaEdge = false;
            PersistentBackgroundImage = null;
            // Unlike globalVars/characters/onceOnly below, this is a REFERENCE HOLDER minted
            // per Initialize rather than a manager-owned collection, so dropping it here
            // costs nothing and keeps a reset context from pointing at a store it has no
            // business reading through.
            DataAssetStore = null;

            CurrentDialogueState = new StoryFlowDialogueState();

            callStack.Clear();
            flowCallStack.Clear();
            loopStack.Clear();
            localVariables.Clear();
            nodeRuntimeStates.Clear();
            warnedUnknownNodes.Clear();
            warnedMapNodes.Clear();
            warnedDataAssetNodes.Clear();
            warnedCharacterIds.Clear();

            localVariableNameIndex = null;
            globalVariableNameIndex = null;

            // Note: external references (globalVars, characters, characterIdBridge,
            // onceOnly) are not cleared — they are owned by the manager and may be shared
            // across contexts.
        }
    }
}
