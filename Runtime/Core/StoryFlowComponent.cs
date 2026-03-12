using System;
using System.Collections;
using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Execution;
using StoryFlow.Execution.NodeHandlers;
using StoryFlow.UI;
using StoryFlow.Utilities;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

namespace StoryFlow
{
    /// <summary>
    /// Main per-actor runtime component for StoryFlow dialogue execution.
    /// Attach to any GameObject that needs to run dialogue scripts.
    /// Communicates with StoryFlowManager for shared state (global variables, characters).
    /// </summary>
    [AddComponentMenu("StoryFlow/StoryFlow Component")]
    [DisallowMultipleComponent]
    public class StoryFlowComponent : MonoBehaviour
    {
        // =====================================================================
        // Inspector Fields
        // =====================================================================

        [Header("Project")]
        [Tooltip("Optional project override. If null, uses StoryFlowManager.Instance.Project.")]
        public StoryFlowProjectAsset Project;

        [Tooltip("Path of the script to run when StartDialogue() is called with no arguments.")]
        public string ScriptPath;

        [Tooltip("Language code for localized string lookup (e.g. \"en\", \"fr\", \"ja\").")]
        public string LanguageCode = "en";

        [Header("UI")]
        [Tooltip("Optional dialogue UI handler. Receives dialogue state updates for rendering.")]
        public StoryFlowDialogueUI DialogueUI;

        [Header("Audio")]
        [Tooltip("When true, stops any playing dialogue audio when the dialogue session ends.")]
        public bool StopAudioOnDialogueEnd = true;

        [Tooltip("Audio mixer group for dialogue audio playback.")]
        public AudioMixerGroup DialogueAudioMixerGroup;

        [Range(0f, 2f)]
        [Tooltip("Volume multiplier applied to dialogue audio playback.")]
        public float DialogueVolumeMultiplier = 1f;

        // =====================================================================
        // C# Events
        // =====================================================================

        /// <summary>Fired when a dialogue session starts.</summary>
        public event Action OnDialogueStarted;

        /// <summary>Fired when the dialogue state is updated (new node reached, text changed, etc.).</summary>
        public event Action<StoryFlowDialogueState> OnDialogueUpdated;

        /// <summary>Fired when the dialogue session ends.</summary>
        public event Action OnDialogueEnded;

        /// <summary>Fired when a variable value changes during execution. Parameters: the variable, isGlobal flag.</summary>
        public event Action<StoryFlowVariable, bool> OnVariableChanged;

        /// <summary>Fired when execution enters a new script via RunScript. Parameter: script path.</summary>
        public event Action<string> OnScriptStarted;

        /// <summary>Fired when execution returns from a script via RunScript. Parameter: script path.</summary>
        public event Action<string> OnScriptEnded;

        /// <summary>Fired when an execution error occurs. Parameter: error message.</summary>
        public event Action<string> OnError;

        /// <summary>Fired when a SetBackgroundImage node sets a new background. Parameter: the sprite.</summary>
        public event Action<Sprite> OnBackgroundImageChanged;

        /// <summary>Fired when a PlayAudio node requests audio playback. Parameters: the clip, loop flag.</summary>
        public event Action<AudioClip, bool> OnAudioPlayRequested;

        // =====================================================================
        // UnityEvents (Inspector-assignable)
        // =====================================================================

        [Header("Events")]
        public UnityEvent OnDialogueStartedEvent;
        public UnityEvent<StoryFlowDialogueState> OnDialogueUpdatedEvent;
        public UnityEvent OnDialogueEndedEvent;

        // =====================================================================
        // Internal State
        // =====================================================================

        private StoryFlowExecutionContext context;
        private AudioSource dialogueAudioSource;
        private bool isDialogueActive;

        // =====================================================================
        // Public Control Methods
        // =====================================================================

        /// <summary>
        /// Starts dialogue using the configured ScriptPath, or the project's StartupScriptPath
        /// if ScriptPath is not set.
        /// </summary>
        public void StartDialogue()
        {
            var project = GetProject();
            if (project == null)
            {
                BroadcastError("Cannot start dialogue: no project assigned and no StoryFlowManager project available.");
                return;
            }

            var path = !string.IsNullOrEmpty(ScriptPath) ? ScriptPath : project.StartupScriptPath;

            if (string.IsNullOrEmpty(path))
            {
                BroadcastError("Cannot start dialogue: no script path configured and no startup script in project.");
                return;
            }

            var script = project.GetScriptByPath(path);
            if (script == null)
            {
                BroadcastError($"Cannot start dialogue: script not found at path \"{path}\".");
                return;
            }

            StartDialogueInternal(script);
        }

        /// <summary>
        /// Starts dialogue using the specified script path.
        /// </summary>
        public void StartDialogue(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath))
            {
                BroadcastError("Cannot start dialogue: script path is null or empty.");
                return;
            }

            var project = GetProject();
            if (project == null)
            {
                BroadcastError("Cannot start dialogue: no project available.");
                return;
            }

            var script = project.GetScriptByPath(scriptPath);
            if (script == null)
            {
                BroadcastError($"Cannot start dialogue: script not found at path \"{scriptPath}\".");
                return;
            }

            StartDialogueInternal(script);
        }

        /// <summary>
        /// Starts dialogue using a direct reference to a script asset.
        /// </summary>
        public void StartDialogue(StoryFlowScriptAsset script)
        {
            if (script == null)
            {
                BroadcastError("Cannot start dialogue: script asset is null.");
                return;
            }

            StartDialogueInternal(script);
        }

        private void StartDialogueInternal(StoryFlowScriptAsset script)
        {
            // If a dialogue is already active, stop it first
            if (isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] Starting new dialogue while one is already active. Stopping previous dialogue.");
                StopDialogue();
            }

            var manager = StoryFlowManager.Instance;
            if (manager == null || !manager.HasProject())
            {
                BroadcastError("Cannot start dialogue: StoryFlowManager is not initialized or has no project.");
                return;
            }

            // Create and initialize the execution context
            context = new StoryFlowExecutionContext();
            context.Project = GetProject();
            context.Initialize(
                script,
                manager.GlobalVariables,
                manager.RuntimeCharacters,
                manager.UsedOnceOnlyOptions
            );

            isDialogueActive = true;
            context.IsExecuting = true;

            // Notify manager
            manager.NotifyDialogueStarted();

            // Fire events
            OnDialogueStarted?.Invoke();
            OnDialogueStartedEvent?.Invoke();

            // Auto-initialize UI binding if not already bound
            if (DialogueUI != null && !DialogueUI.IsBoundTo(this))
                DialogueUI.InitializeWithComponent(this);

            NotifyScriptStarted(script.ScriptPath);

            // Begin execution from the start node
            var startNode = script.GetNode(script.StartNodeId ?? "0");
            if (startNode == null)
            {
                BroadcastError($"Cannot start dialogue: start node \"{script.StartNodeId ?? "0"}\" not found in script \"{script.ScriptPath}\".");
                StopDialogue();
                return;
            }

            ProcessNode(startNode);
        }

        /// <summary>
        /// Selects a dialogue option by its ID, advancing execution along the option's edge.
        /// </summary>
        public void SelectOption(string optionId)
        {
            if (!isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] SelectOption called but no dialogue is active.");
                return;
            }

            if (context == null || !context.IsWaitingForInput)
            {
                Debug.LogWarning("[StoryFlow] SelectOption called but dialogue is not waiting for input.");
                return;
            }

            if (string.IsNullOrEmpty(optionId))
            {
                Debug.LogWarning("[StoryFlow] SelectOption called with null or empty option ID.");
                return;
            }

            // Check if this option is once-only and mark it
            if (context.CurrentDialogueState?.Options != null)
            {
                foreach (var option in context.CurrentDialogueState.Options)
                {
                    if (option.Id == optionId && option.IsOnceOnly)
                    {
                        var onceOnlyKey = context.CurrentDialogueState.NodeId + "-" + optionId;
                        context.MarkOnceOnlyUsed(onceOnlyKey);
                        break;
                    }
                }
            }

            context.IsWaitingForInput = false;
            context.IsEnteringDialogueViaEdge = true;
            context.ShouldPause = false;

            // Clear node runtime caches for fresh evaluation
            context.ClearNodeRuntimeStates();

            // Find the next node from this option's edge
            var handle = StoryFlowHandles.SourceOption(context.CurrentDialogueState.NodeId, optionId);
            context.LastSourceHandle = handle;
            var edge = context.CurrentScript?.FindEdgeBySourceHandle(handle);
            if (edge == null) return;

            var targetNode = context.CurrentScript.GetNode(edge.Target);
            if (targetNode == null) return;

            ProcessNode(targetNode);
        }

        /// <summary>
        /// Advances the dialogue when no options are present (simple continue/next).
        /// Only works if the current dialogue state has CanAdvance set to true.
        /// </summary>
        public void AdvanceDialogue()
        {
            if (!isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but no dialogue is active.");
                return;
            }

            if (context == null || !context.IsWaitingForInput)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but dialogue is not waiting for input.");
                return;
            }

            if (context.CurrentDialogueState == null || !context.CurrentDialogueState.CanAdvance)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but current dialogue cannot advance.");
                return;
            }

            context.IsWaitingForInput = false;
            context.IsEnteringDialogueViaEdge = true;
            context.ShouldPause = false;

            // Clear node runtime caches for fresh evaluation
            context.ClearNodeRuntimeStates();

            // Find the next node from the default output edge
            var handle = StoryFlowHandles.Source(context.CurrentDialogueState.NodeId, "");
            context.LastSourceHandle = handle;
            var edge = context.CurrentScript?.FindEdgeBySourceHandle(handle);
            if (edge == null) return;

            var targetNode = context.CurrentScript.GetNode(edge.Target);
            if (targetNode == null) return;

            ProcessNode(targetNode);
        }

        /// <summary>
        /// Immediately stops the current dialogue session and cleans up.
        /// </summary>
        public void StopDialogue()
        {
            if (!isDialogueActive)
                return;

            // Stop audio
            if (StopAudioOnDialogueEnd)
                StopDialogueAudio();

            isDialogueActive = false;

            if (context != null)
            {
                context.IsExecuting = false;
                context.IsWaitingForInput = false;
                context.ShouldPause = true;
            }

            // Notify manager
            StoryFlowManager.Instance?.NotifyDialogueEnded();

            // Fire events
            OnDialogueEnded?.Invoke();
            OnDialogueEndedEvent?.Invoke();
        }

        /// <summary>Pauses dialogue execution. Node processing will be blocked until resumed.</summary>
        public void PauseDialogue()
        {
            if (context != null)
                context.IsPaused = true;
        }

        /// <summary>Resumes dialogue execution after a pause.</summary>
        public void ResumeDialogue()
        {
            if (context != null)
                context.IsPaused = false;
        }

        /// <summary>
        /// Pauses node execution after the current node completes.
        /// The next node will be held until ResumeExecution() is called.
        /// </summary>
        public void PauseExecution()
        {
            if (context != null)
                context.ShouldPause = true;
        }

        /// <summary>
        /// Resumes execution from where it was paused.
        /// </summary>
        public void ResumeExecution()
        {
            if (context == null || !isDialogueActive) return;
            if (context.NextNode != null)
            {
                context.ShouldPause = false;
                ProcessNode(context.NextNode);
            }
        }

        /// <summary>
        /// Pauses execution for the given duration, then resumes automatically.
        /// </summary>
        public Coroutine PauseExecutionFor(float seconds)
        {
            PauseExecution();
            return StartCoroutine(ResumeAfterDelay(seconds));
        }

        private IEnumerator ResumeAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            ResumeExecution();
        }

        // =====================================================================
        // Input Option Handling
        // =====================================================================

        /// <summary>
        /// Called when a dialogue input option value changes (string overload).
        /// Wraps the string in a StoryFlowVariant and delegates to the typed overload.
        /// </summary>
        public void InputChanged(string optionId, string value)
        {
            InputChanged(optionId, StoryFlowVariant.String(value));
        }

        /// <summary>
        /// Called when a dialogue input option (string, integer, float, boolean, enum)
        /// has its value changed by the player. Stores the value on the current dialogue
        /// node, re-interpolates text, broadcasts the update, and optionally follows
        /// the option's "on change" edge.
        /// </summary>
        public void InputChanged(string optionId, StoryFlowVariant value)
        {
            if (!isDialogueActive || context == null)
            {
                Debug.LogWarning("[StoryFlow] InputChanged called but no dialogue is active.");
                return;
            }

            if (string.IsNullOrEmpty(optionId))
            {
                Debug.LogWarning("[StoryFlow] InputChanged called with null or empty option ID.");
                return;
            }

            if (context.CurrentDialogueState == null)
                return;

            // Store the input value on the node's runtime state
            var nodeState = context.GetNodeRuntimeState(context.CurrentDialogueState.NodeId);
            nodeState.OutputValues[optionId] = value ?? new StoryFlowVariant();

            // Clear caches so downstream nodes re-evaluate with the new input
            context.ClearNodeRuntimeStates();

            // Broadcast updated dialogue state (text may re-interpolate based on new value)
            BroadcastDialogueUpdate();

            // Check if there's an "on change" edge from this option
            var onChangeHandle = StoryFlowHandles.SourceOption(context.CurrentDialogueState.NodeId, optionId);
            var onChangeEdge = context.CurrentScript?.FindEdgeBySourceHandle(onChangeHandle);

            if (onChangeEdge != null)
            {
                // Execute the on-change flow without leaving the dialogue
                var targetNode = context.CurrentScript?.GetNode(onChangeEdge.Target);
                if (targetNode != null)
                {
                    context.IsEnteringDialogueViaEdge = false;
                    ProcessNode(targetNode);
                }
            }
        }

        // =====================================================================
        // State Accessors
        // =====================================================================

        /// <summary>Returns the current dialogue state, or null if no dialogue is active.</summary>
        public StoryFlowDialogueState GetCurrentDialogue()
        {
            return context?.CurrentDialogueState;
        }

        /// <summary>Returns true if a dialogue session is currently active on this component.</summary>
        public bool IsDialogueActive()
        {
            return isDialogueActive;
        }

        /// <summary>Returns true if the dialogue is currently waiting for player input.</summary>
        public bool IsWaitingForInput()
        {
            return context?.IsWaitingForInput ?? false;
        }

        /// <summary>Returns true if the dialogue is currently paused.</summary>
        public bool IsPaused()
        {
            return context?.IsPaused ?? false;
        }

        /// <summary>Returns the internal execution context. Use with care.</summary>
        internal StoryFlowExecutionContext GetContext()
        {
            return context;
        }

        /// <summary>
        /// Returns the project asset. Checks this component's override first,
        /// then falls back to StoryFlowManager.Instance.Project.
        /// </summary>
        public StoryFlowProjectAsset GetProject()
        {
            return Project != null ? Project : StoryFlowManager.Instance?.Project;
        }

        // =====================================================================
        // Variable Access (by display name)
        // =====================================================================

        /// <summary>Gets a boolean variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public bool GetBoolVariable(string name, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            return v?.Value.GetBool() ?? false;
        }

        /// <summary>Sets a boolean variable by its display name. When global is true, targets only global scope.</summary>
        public void SetBoolVariable(string name, bool value, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            if (v != null)
            {
                v.Value.SetBool(value);
                bool isGlobal = context != null && !context.LocalVariables.ContainsKey(v.Id);
                BroadcastVariableChanged(v, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetBoolVariable: variable \"{name}\" not found.");
            }
        }

        /// <summary>Gets an integer variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public int GetIntVariable(string name, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            return v?.Value.GetInt() ?? 0;
        }

        /// <summary>Sets an integer variable by its display name. When global is true, targets only global scope.</summary>
        public void SetIntVariable(string name, int value, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            if (v != null)
            {
                v.Value.SetInt(value);
                bool isGlobal = context != null && !context.LocalVariables.ContainsKey(v.Id);
                BroadcastVariableChanged(v, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetIntVariable: variable \"{name}\" not found.");
            }
        }

        /// <summary>Gets a float variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public float GetFloatVariable(string name, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            return v?.Value.GetFloat() ?? 0f;
        }

        /// <summary>Sets a float variable by its display name. When global is true, targets only global scope.</summary>
        public void SetFloatVariable(string name, float value, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            if (v != null)
            {
                v.Value.SetFloat(value);
                bool isGlobal = context != null && !context.LocalVariables.ContainsKey(v.Id);
                BroadcastVariableChanged(v, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetFloatVariable: variable \"{name}\" not found.");
            }
        }

        /// <summary>Gets a string variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public string GetStringVariable(string name, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            return v?.Value.GetString() ?? "";
        }

        /// <summary>Sets a string variable by its display name. When global is true, targets only global scope.</summary>
        public void SetStringVariable(string name, string value, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            if (v != null)
            {
                v.Value.SetString(value);
                bool isGlobal = context != null && !context.LocalVariables.ContainsKey(v.Id);
                BroadcastVariableChanged(v, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetStringVariable: variable \"{name}\" not found.");
            }
        }

        /// <summary>Gets an enum variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public string GetEnumVariable(string name, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            return v?.Value.GetEnum() ?? "";
        }

        /// <summary>Sets an enum variable by its display name. When global is true, targets only global scope.</summary>
        public void SetEnumVariable(string name, string value, bool global = false)
        {
            var v = global
                ? context?.FindVariableByName(name, false, true)
                : context?.FindVariableByName(name, true, true);
            if (v != null)
            {
                v.Value.SetEnum(value);
                bool isGlobal = context != null && !context.LocalVariables.ContainsKey(v.Id);
                BroadcastVariableChanged(v, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetEnumVariable: variable \"{name}\" not found.");
            }
        }

        /// <summary>
        /// Gets a character variable value by character path and variable name.
        /// Reads from the deep-copied runtime character data, not the original asset.
        /// </summary>
        public StoryFlowVariant GetCharacterVariable(string charPath, string varName)
        {
            if (context == null) return null;

            var characterData = context.FindCharacter(charPath);
            if (characterData == null)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterVariable: character at \"{charPath}\" not found.");
                return null;
            }

            var v = characterData.FindVariableByName(varName);
            if (v != null)
                return v.Value;

            Debug.LogWarning($"[StoryFlow] GetCharacterVariable: variable \"{varName}\" not found on character \"{charPath}\".");
            return null;
        }

        /// <summary>
        /// Sets a character variable value by character path and variable name.
        /// Mutates the deep-copied runtime character data, not the original asset.
        /// </summary>
        public void SetCharacterVariable(string charPath, string varName, StoryFlowVariant value)
        {
            if (context == null) return;

            var characterData = context.FindCharacter(charPath);
            if (characterData == null)
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVariable: character at \"{charPath}\" not found.");
                return;
            }

            var v = characterData.FindVariableByName(varName);
            if (v != null)
            {
                v.Value = value ?? new StoryFlowVariant();
                // Also update the quick-lookup dictionary
                characterData.Variables[varName] = v.Value;
                BroadcastVariableChanged(v, false);
                return;
            }

            Debug.LogWarning($"[StoryFlow] SetCharacterVariable: variable \"{varName}\" not found on character \"{charPath}\".");
        }

        /// <summary>
        /// Clears all cached node runtime states and re-initializes local variables
        /// from the current script asset.
        /// </summary>
        public void ResetVariables()
        {
            context?.ClearNodeRuntimeStates();

            // Re-initialize local variables from the current script
            if (context?.CurrentScript != null)
            {
                var script = context.CurrentScript;
                if (script.Variables != null)
                {
                    foreach (var kvp in script.Variables)
                    {
                        if (context.LocalVariables.TryGetValue(kvp.Key, out var localVar))
                        {
                            localVar.Value = new StoryFlowVariant(kvp.Value.Value);
                        }
                    }
                    context.InvalidateLocalNameIndex();
                }
            }
        }

        /// <summary>
        /// Looks up a localized string using the component's LanguageCode.
        /// </summary>
        public string GetLocalizedString(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            var fullKey = LanguageCode + "." + key;
            return context?.GetString(fullKey) ?? key;
        }

        // =====================================================================
        // Internal Node Processing (called by node handlers / dispatcher)
        // =====================================================================

        /// <summary>
        /// Iterative entry point for node processing. Runs a while-loop that
        /// dispatches nodes one at a time until a handler signals a pause
        /// (dialogue waiting, end reached, error) or no next node is set.
        /// </summary>
        internal void ProcessNode(StoryFlowNode node)
        {
            if (node == null)
            {
                BroadcastError("ProcessNode called with null node.");
                return;
            }

            if (!isDialogueActive || context == null) return;
            if (context.IsPaused) return;

            context.NextNode = node;
            context.ShouldPause = false;
            int iterationCount = 0;

            while (context.NextNode != null && !context.ShouldPause)
            {
                var current = context.NextNode;
                context.NextNode = null;

                iterationCount++;
                if (iterationCount > StoryFlowExecutionContext.MaxProcessingDepth)
                {
                    BroadcastError($"Processing depth exceeded {StoryFlowExecutionContext.MaxProcessingDepth}. " +
                                   "Possible infinite loop detected. Stopping dialogue.");
                    StopDialogue();
                    return;
                }

                context.CurrentNodeId = current.Id;

                try
                {
                    StoryFlowNodeDispatcher.ProcessNode(this, current);
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    BroadcastError($"Exception in node handler ({current.Type}, id: {current.Id}): {ex.Message}");
                    StopDialogue();
                    return;
                }
            }
        }

        /// <summary>
        /// Finds the edge connected to the given source handle and sets the next node
        /// for the iterative loop to pick up.
        /// </summary>
        internal void ProcessNextNode(string sourceHandle)
        {
            if (context == null || context.CurrentScript == null) return;

            context.LastSourceHandle = sourceHandle;
            var edge = context.CurrentScript.FindEdgeBySourceHandle(sourceHandle);
            if (edge == null) return;

            var targetNodeId = edge.Target;
            if (string.IsNullOrEmpty(targetNodeId)) return;

            var targetNode = context.CurrentScript.GetNode(targetNodeId);
            if (targetNode == null)
            {
                BroadcastError($"Target node not found: {targetNodeId}");
                return;
            }

            context.NextNode = targetNode;
        }

        /// <summary>
        /// Convenience method: builds a source handle from a node ID and optional suffix,
        /// then sets the next node for the iterative loop.
        /// </summary>
        internal void ProcessNextNodeFromSource(string nodeId, string suffix = "")
        {
            var handle = string.IsNullOrEmpty(suffix)
                ? StoryFlowHandles.Source(nodeId, "")
                : StoryFlowHandles.Source(nodeId, suffix);
            ProcessNextNode(handle);
        }

        // =====================================================================
        // Broadcast Methods (called by node handlers)
        // =====================================================================

        /// <summary>
        /// Broadcasts the current dialogue state to all listeners and the UI handler.
        /// </summary>
        internal void BroadcastDialogueUpdate()
        {
            if (context?.CurrentDialogueState == null) return;

            OnDialogueUpdated?.Invoke(context.CurrentDialogueState);
            OnDialogueUpdatedEvent?.Invoke(context.CurrentDialogueState);
        }

        /// <summary>
        /// Broadcasts a variable change notification.
        /// </summary>
        internal void BroadcastVariableChanged(StoryFlowVariable variable, bool isGlobal)
        {
            var settings = StoryFlowSettings.Instance;
            if (settings != null && settings.LogVariableChanges && variable != null)
            {
                var scope = isGlobal ? "global" : "local";
                Debug.Log($"[StoryFlow] Variable changed [{scope}]: \"{variable.Name}\" = {variable.Value}");
            }

            OnVariableChanged?.Invoke(variable, isGlobal);
        }

        /// <summary>
        /// Logs an error and broadcasts it to listeners.
        /// </summary>
        internal void BroadcastError(string message)
        {
            Debug.LogError("[StoryFlow] " + message);
            OnError?.Invoke(message);
        }

        /// <summary>
        /// Broadcasts a background image change.
        /// </summary>
        internal void BroadcastBackgroundImageChanged(Sprite image)
        {
            OnBackgroundImageChanged?.Invoke(image);
        }

        /// <summary>
        /// Broadcasts an audio play request.
        /// </summary>
        internal void BroadcastAudioPlayRequested(AudioClip clip, bool loop)
        {
            OnAudioPlayRequested?.Invoke(clip, loop);
        }

        /// <summary>Fires the OnScriptStarted event.</summary>
        internal void NotifyScriptStarted(string path)
        {
            if (!string.IsNullOrEmpty(path))
                OnScriptStarted?.Invoke(path);
        }

        /// <summary>Fires the OnScriptEnded event.</summary>
        internal void NotifyScriptEnded(string path)
        {
            if (!string.IsNullOrEmpty(path))
                OnScriptEnded?.Invoke(path);
        }

        // =====================================================================
        // Audio
        // =====================================================================

        /// <summary>
        /// Plays a dialogue audio clip. Creates an AudioSource on demand if needed.
        /// </summary>
        public void PlayDialogueAudio(AudioClip clip, bool loop)
        {
            if (clip == null) return;

            // Create AudioSource on demand
            if (dialogueAudioSource == null)
            {
                dialogueAudioSource = gameObject.AddComponent<AudioSource>();
                dialogueAudioSource.playOnAwake = false;
                dialogueAudioSource.spatialBlend = 0f; // 2D audio
            }

            // Stop any currently playing audio
            if (dialogueAudioSource.isPlaying)
                dialogueAudioSource.Stop();

            // Configure
            dialogueAudioSource.clip = clip;
            dialogueAudioSource.loop = loop;
            dialogueAudioSource.outputAudioMixerGroup = DialogueAudioMixerGroup;
            dialogueAudioSource.volume = DialogueVolumeMultiplier;

            dialogueAudioSource.Play();
        }

        /// <summary>
        /// Stops the dialogue audio source if it is currently playing.
        /// </summary>
        public void StopDialogueAudio()
        {
            if (dialogueAudioSource != null && dialogueAudioSource.isPlaying)
            {
                dialogueAudioSource.Stop();
                dialogueAudioSource.clip = null;
            }
        }

        /// <summary>Returns true if dialogue audio is currently playing.</summary>
        public bool IsDialogueAudioPlaying()
        {
            return dialogueAudioSource != null && dialogueAudioSource.isPlaying;
        }

        // =====================================================================
        // Asset Resolution
        // =====================================================================

        /// <summary>
        /// Resolves a Unity asset by its key. Checks the current script's resolved assets
        /// first, then falls back to the project's resolved assets.
        /// </summary>
        public T ResolveAsset<T>(string assetKey) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetKey))
                return null;

            // Check current script's resolved assets
            if (context?.CurrentScript != null &&
                context.CurrentScript.ResolvedAssets != null &&
                context.CurrentScript.ResolvedAssets.TryGetValue(assetKey, out var scriptAsset))
            {
                if (scriptAsset is T typedScriptAsset)
                    return typedScriptAsset;
            }

            // Check project's resolved assets
            var project = GetProject();
            if (project != null &&
                project.ResolvedAssets != null &&
                project.ResolvedAssets.TryGetValue(assetKey, out var projectAsset))
            {
                if (projectAsset is T typedProjectAsset)
                    return typedProjectAsset;
            }

            return null;
        }

        // =====================================================================
        // Cleanup
        // =====================================================================

        private void OnDisable()
        {
            if (isDialogueActive)
            {
                StopDialogue();
            }
        }

        private void OnDestroy()
        {
            if (isDialogueActive)
                StopDialogue();

            // Clear all C# event subscribers to prevent memory leaks
            OnDialogueStarted = null;
            OnDialogueUpdated = null;
            OnDialogueEnded = null;
            OnVariableChanged = null;
            OnError = null;
            OnAudioPlayRequested = null;
            OnBackgroundImageChanged = null;
            OnScriptStarted = null;
            OnScriptEnded = null;
        }
    }
}
