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

        /// <summary>
        /// True when we are entering a dialogue node via a normal execution edge
        /// (as opposed to returning from a Set* node with no outgoing edge).
        /// </summary>
        public bool IsEnteringDialogueViaEdge { get; set; }

        /// <summary>The currently built dialogue state for display.</summary>
        public StoryFlowDialogueState CurrentDialogueState { get; set; }

        /// <summary>Persistent background image set by SetBackgroundImage nodes.</summary>
        public Sprite PersistentBackgroundImage { get; set; }

        /// <summary>
        /// Transient field set by the evaluator before calling FromNode methods.
        /// Holds the SourceHandle of the edge that led to the current evaluation,
        /// allowing RunScript output resolution to extract the variable identifier.
        /// </summary>
        public string LastSourceHandle { get; set; }

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

        /// <summary>Reference to shared set tracking once-only option usage.</summary>
        private HashSet<string> externalUsedOnceOnlyOptions;

        /// <summary>Per-node cached runtime states.</summary>
        private readonly Dictionary<string, NodeRuntimeState> nodeRuntimeStates = new();

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
            HashSet<string> usedOnceOnlyOptions)
        {
            CurrentScript = script;
            CurrentNodeId = script != null ? script.StartNodeId : "0";

            externalGlobalVariables = globalVars ?? new Dictionary<string, StoryFlowVariable>();
            externalCharacters = characters ?? new Dictionary<string, StoryFlowCharacterData>();
            externalUsedOnceOnlyOptions = usedOnceOnlyOptions ?? new HashSet<string>();

            // Deep copy script's local variables so mutations don't affect the asset
            localVariables = new Dictionary<string, StoryFlowVariable>();
            if (script != null && script.Variables != null)
            {
                foreach (var kvp in script.Variables)
                {
                    localVariables[kvp.Key] = new StoryFlowVariable(kvp.Value);
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
            IsEnteringDialogueViaEdge = false;
            EvaluationDepth = 0;
            NextNode = null;
            ShouldPause = false;
            LastDialogueNodeId = null;
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

            // Deep copy current local variables
            foreach (var kvp in localVariables)
                frame.SavedLocalVariables[kvp.Key] = new StoryFlowVariable(kvp.Value);

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
                localVariables[kvp.Key] = new StoryFlowVariable(kvp.Value);

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

        // =====================================================================
        // Character Lookup
        // =====================================================================

        /// <summary>
        /// Finds runtime character data by its path. Normalizes the path before lookup.
        /// </summary>
        public StoryFlowCharacterData FindCharacter(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var normalizedPath = StoryFlowPathNormalizer.NormalizeCharacterPath(path);

            if (externalCharacters != null && externalCharacters.TryGetValue(normalizedPath, out var character))
                return character;

            return null;
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
            IsEnteringDialogueViaEdge = false;
            EvaluationDepth = 0;
            NextNode = null;
            ShouldPause = false;
            LastDialogueNodeId = null;
            PersistentBackgroundImage = null;

            CurrentDialogueState = new StoryFlowDialogueState();

            callStack.Clear();
            flowCallStack.Clear();
            loopStack.Clear();
            localVariables.Clear();
            nodeRuntimeStates.Clear();

            localVariableNameIndex = null;
            globalVariableNameIndex = null;

            // Note: external references (globalVars, characters, onceOnly) are not cleared —
            // they are owned by the manager and may be shared across contexts.
        }
    }
}
