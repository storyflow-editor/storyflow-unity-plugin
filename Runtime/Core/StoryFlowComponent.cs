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
    /// Built-in UI style for the auto-created fallback dialogue UI.
    /// </summary>
    public enum BuiltInUIStyle
    {
        /// <summary>Default panel with character portrait inside the dialogue box.</summary>
        Default,
        /// <summary>Large character portrait anchored to bottom center of screen, separate from the dialogue panel.</summary>
        Portrait,
        /// <summary>No fallback UI is created. Use when handling dialogue rendering yourself via C# events on StoryFlowComponent.</summary>
        None,
    }

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

        [Tooltip("The script to run when StartDialogue() is called. If not set, uses the project's startup script.")]
        public StoryFlowScriptAsset Script;

        [Tooltip("Language code for localized string lookup (e.g. \"en\", \"fr\", \"ja\").")]
        public string LanguageCode = "en";

        [Header("UI")]
        [Tooltip("Optional dialogue UI handler. Receives dialogue state updates for rendering. When not assigned, a built-in UI is auto-created based on the selected UI Style (or skipped entirely when UIStyle is set to None).")]
        public StoryFlowDialogueUI DialogueUI;

        [Tooltip("Built-in UI style used when no DialogueUI is assigned. Default shows the character portrait inside the dialogue panel. Portrait shows a large character image anchored to the bottom center of the screen. None disables the fallback so you can handle rendering yourself via C# events.")]
        public BuiltInUIStyle UIStyle = BuiltInUIStyle.Default;

        [Header("Debugging")]
        [Tooltip("Enable execution trace logging ([SF-TRACE] prefix) for cross-runtime comparison.")]
        public bool TraceEnabled = true;

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

        /// <summary>
        /// Fired once per tag when a tagged dialogue node is freshly entered, in authored (array) order.
        /// Parameter: the raw tag string. Fires only on a new dialogue node — re-rendering the same
        /// current line (variable-change refresh, input change, Set* fallthrough) never re-fires;
        /// revisiting the node later fires again. Untagged dialogue fires nothing.
        /// </summary>
        public event Action<string> OnDialogueTagReached;

        /// <summary>Fired when the dialogue session ends.</summary>
        public event Action OnDialogueEnded;

        /// <summary>Fired when a variable value changes during execution. Parameters: the variable, isGlobal flag.</summary>
        public event Action<StoryFlowVariable, bool> OnVariableChanged;

        /// <summary>
        /// Fired when a character variable changes during execution. Parameters: characterPath, variableName, value.
        /// Fires for the built-in "Name" and "Image" fields as well as custom character variables, so non-speaker
        /// UIs can react to SetCharacterVar mutations without inspecting the dialogue state.
        /// </summary>
        public event Action<string, string, StoryFlowVariant> OnCharacterVariableChanged;

        /// <summary>
        /// Fired when a SetCharacterVar node changes a character's built-in "Image" field.
        /// Parameters: characterPath, the resolved portrait Sprite (the new key's sprite, or the
        /// character's baked default; null only when the character has no resolvable image at all).
        /// Fires alongside <see cref="OnCharacterVariableChanged"/>, but carries the resolved Sprite
        /// directly so non-speaker UIs (background characters, portraits that change without
        /// dialogue) can update without a Dialogue node firing or a manual
        /// <see cref="GetCharacterPortrait"/> call.
        /// </summary>
        public event Action<string, Sprite> OnCharacterImageChanged;

        /// <summary>
        /// Fired when a synchronous run of nodes settles — the processing loop reaches a
        /// dialogue awaiting input, an End node, or a dead end. Because Set* nodes execute
        /// synchronously in a single batch, this marks the point where a whole sequence of
        /// variable/character changes has finished, letting listeners (save systems, debug
        /// overlays, character reactions) react once instead of per-change mid-sequence. Fires
        /// once per drain of the loop, which also includes public array/map setters that
        /// re-render the current dialogue.
        /// </summary>
        public event Action OnExecutionSettled;

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

        [Tooltip("Fired once per tag when a tagged dialogue node is freshly entered, in authored order. Passes the raw tag string.")]
        public UnityEvent<string> OnDialogueTagReachedEvent;

        public UnityEvent OnDialogueEndedEvent;

        // =====================================================================
        // Internal State
        // =====================================================================

        private StoryFlowExecutionContext _context;
        private AudioSource _dialogueAudioSource;
        private bool _isDialogueActive;
        private bool _waitingForAudioAdvance;
        private bool _audioAdvanceAllowSkip;
        private bool _isRefreshingDialogue;

        /// <summary>
        /// The audio clip currently assigned to the dialogue audio source.
        /// Used to detect changes and avoid restarting the same clip on re-render
        /// (matches editor behavior where audio only changes when the reference changes).
        /// </summary>
        public AudioClip CurrentDialogueAudioClip { get; private set; }

        // =====================================================================
        // Unity Lifecycle
        // =====================================================================

        private void Update()
        {
            // Poll for audio finish (Unity AudioSource has no finished callback for non-looped clips)
            if (_waitingForAudioAdvance && _dialogueAudioSource != null && !_dialogueAudioSource.isPlaying)
            {
                _waitingForAudioAdvance = false;
                _audioAdvanceAllowSkip = false;
                AdvanceDialogue();
            }
        }

        // =====================================================================
        // Public Control Methods
        // =====================================================================

        /// <summary>
        /// Starts dialogue using the assigned Script asset, or the project's startup script
        /// if none is assigned.
        /// </summary>
        public void StartDialogue()
        {
            // Use assigned script asset, or fall back to project's startup script
            var script = Script;
            if (script == null)
            {
                var project = GetProject();
                if (project == null)
                {
                    BroadcastError(NoProjectFoundMessage());
                    return;
                }

                script = project.StartupScript;

                if (script == null)
                {
                    BroadcastError("Cannot start dialogue: no script assigned and no startup script in project.");
                    return;
                }
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
                BroadcastError(NoProjectFoundMessage());
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
            if (_isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] Starting new dialogue while one is already active. Stopping previous dialogue.");
                StopDialogue();
            }

            var manager = StoryFlowManager.Instance;
            if (manager == null || !manager.HasProject())
            {
                BroadcastError(NoProjectFoundMessage());
                return;
            }

            // Reset audio tracking so the first dialogue node's audio always plays
            CurrentDialogueAudioClip = null;

            // Create and initialize the execution context
            _context = new StoryFlowExecutionContext();
            _context.Project = GetProject();
            _context.LanguageCode = LanguageCode;
            _context.TraceEnabled = TraceEnabled;
            _context.Initialize(
                script,
                manager.GlobalVariables,
                manager.RuntimeCharacters,
                manager.UsedOnceOnlyOptions
            );

            _isDialogueActive = true;
            _context.IsExecuting = true;

            // Notify manager
            manager.NotifyDialogueStarted();

            // Auto-create fallback UI if none assigned (unless explicitly disabled via UIStyle = None)
            if (DialogueUI == null && UIStyle != BuiltInUIStyle.None)
            {
                CreateFallbackUI();
            }

            // Auto-initialize UI binding if not already bound
            if (DialogueUI != null && !DialogueUI.IsBoundTo(this))
                DialogueUI.InitializeWithComponent(this);

            // Fire events (after UI is created and bound so it receives them)
            OnDialogueStarted?.Invoke();
            OnDialogueStartedEvent?.Invoke();

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
            if (!_isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] SelectOption called but no dialogue is active.");
                return;
            }

            if (_context == null || !_context.IsWaitingForInput)
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
            if (_context.CurrentDialogueState?.Options != null)
            {
                foreach (var option in _context.CurrentDialogueState.Options)
                {
                    if (option.Id == optionId && option.IsOnceOnly)
                    {
                        var onceOnlyKey = _context.CurrentDialogueState.NodeId + "-" + optionId;
                        _context.MarkOnceOnlyUsed(onceOnlyKey);
                        break;
                    }
                }
            }

            _context.IsWaitingForInput = false;
            _context.ShouldPause = false;

            // Clear node runtime caches for fresh evaluation
            _context.ClearNodeRuntimeStates();

            // Find the next node from this option's edge
            var handle = StoryFlowHandles.SourceOption(_context.CurrentDialogueState.NodeId, optionId);
            _context.LastSourceHandle = handle;
            var edge = _context.CurrentScript?.FindEdgeBySourceHandle(handle);
            if (edge == null) return;

            var targetNode = _context.CurrentScript.GetNode(edge.Target);
            if (targetNode == null) return;

            if (targetNode.Type == Data.StoryFlowNodeType.Dialogue)
                _context.EnteringDialogueViaEdge = true;

            ProcessNode(targetNode);
        }

        /// <summary>
        /// Advances the dialogue when no options are present (simple continue/next).
        /// Only works if the current dialogue state has CanAdvance set to true.
        /// </summary>
        public void AdvanceDialogue()
        {
            if (!_isDialogueActive)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but no dialogue is active.");
                return;
            }

            if (_context == null || !_context.IsWaitingForInput)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but dialogue is not waiting for input.");
                return;
            }

            if (_context.CurrentDialogueState == null || !_context.CurrentDialogueState.CanAdvance)
            {
                Debug.LogWarning("[StoryFlow] AdvanceDialogue called but current dialogue cannot advance.");
                return;
            }

            // Audio advance-on-end: block manual advance if skip is not allowed
            if (_waitingForAudioAdvance && !_audioAdvanceAllowSkip)
                return;

            // Audio advance-on-end with skip: stop audio and proceed
            if (_waitingForAudioAdvance && _audioAdvanceAllowSkip)
            {
                StopDialogueAudio();
                _waitingForAudioAdvance = false;
                _audioAdvanceAllowSkip = false;
            }

            _context.IsWaitingForInput = false;
            _context.ShouldPause = false;

            // Clear node runtime caches for fresh evaluation
            _context.ClearNodeRuntimeStates();

            // Find the next node from the default output edge
            var handle = StoryFlowHandles.Source(_context.CurrentDialogueState.NodeId, "");
            _context.LastSourceHandle = handle;
            var edge = _context.CurrentScript?.FindEdgeBySourceHandle(handle);
            if (edge == null) return;

            var targetNode = _context.CurrentScript.GetNode(edge.Target);
            if (targetNode == null) return;

            if (targetNode.Type == Data.StoryFlowNodeType.Dialogue)
                _context.EnteringDialogueViaEdge = true;

            ProcessNode(targetNode);
        }

        /// <summary>
        /// Immediately stops the current dialogue session and cleans up.
        /// </summary>
        public void StopDialogue()
        {
            if (!_isDialogueActive)
                return;

            _waitingForAudioAdvance = false;
            _audioAdvanceAllowSkip = false;

            // Stop audio
            if (StopAudioOnDialogueEnd)
                StopDialogueAudio();

            _isDialogueActive = false;

            if (_context != null)
            {
                _context.IsExecuting = false;
                _context.IsWaitingForInput = false;
                _context.ShouldPause = true;
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
            if (_context != null)
                _context.IsPaused = true;
        }

        /// <summary>Resumes dialogue execution after a pause.</summary>
        public void ResumeDialogue()
        {
            if (_context != null)
                _context.IsPaused = false;
        }

        /// <summary>
        /// Pauses node execution after the current node completes.
        /// The next node will be held until ResumeExecution() is called.
        /// </summary>
        public void PauseExecution()
        {
            if (_context != null)
                _context.ShouldPause = true;
        }

        /// <summary>
        /// Resumes execution from where it was paused.
        /// </summary>
        public void ResumeExecution()
        {
            if (_context == null || !_isDialogueActive) return;
            if (_context.NextNode != null)
            {
                _context.ShouldPause = false;
                ProcessNode(_context.NextNode);
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
            if (!_isDialogueActive || _context == null)
            {
                Debug.LogWarning("[StoryFlow] InputChanged called but no dialogue is active.");
                return;
            }

            if (string.IsNullOrEmpty(optionId))
            {
                Debug.LogWarning("[StoryFlow] InputChanged called with null or empty option ID.");
                return;
            }

            if (_context.CurrentDialogueState == null)
                return;

            // Store the input value on the node's runtime state
            var nodeState = _context.GetNodeRuntimeState(_context.CurrentDialogueState.NodeId);
            nodeState.OutputValues[optionId] = value ?? new StoryFlowVariant();

            // Clear caches so downstream nodes re-evaluate with the new input
            _context.ClearNodeRuntimeStates();

            // Broadcast updated dialogue state (text may re-interpolate based on new value)
            BroadcastDialogueUpdate();

            // Check if there's an "on change" edge from this option
            var onChangeHandle = StoryFlowHandles.SourceOption(_context.CurrentDialogueState.NodeId, optionId);
            var onChangeEdge = _context.CurrentScript?.FindEdgeBySourceHandle(onChangeHandle);

            if (onChangeEdge != null)
            {
                // Execute the on-change flow without leaving the dialogue
                var targetNode = _context.CurrentScript?.GetNode(onChangeEdge.Target);
                if (targetNode != null)
                {
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
            return _context?.CurrentDialogueState;
        }

        /// <summary>Returns true if a dialogue session is currently active on this component.</summary>
        public bool IsDialogueActive()
        {
            return _isDialogueActive;
        }

        /// <summary>Returns true if the dialogue is currently waiting for player input.</summary>
        public bool IsWaitingForInput()
        {
            return _context?.IsWaitingForInput ?? false;
        }

        /// <summary>Returns true if the dialogue is currently paused.</summary>
        public bool IsPaused()
        {
            return _context?.IsPaused ?? false;
        }

        /// <summary>Returns the internal execution context. Use with care.</summary>
        internal StoryFlowExecutionContext GetContext()
        {
            return _context;
        }

        /// <summary>
        /// Returns the project asset from the StoryFlowManager singleton.
        /// </summary>
        public StoryFlowProjectAsset GetProject()
        {
            // Manager accessor retries discovery while no project is assigned
            return StoryFlowManager.Instance != null ? StoryFlowManager.Instance.GetProject() : null;
        }

        // =====================================================================
        // Variable Access (by display name)
        // =====================================================================

        /// <summary>
        /// Finds a variable by display name, falling back to the manager's global
        /// variables when no dialogue is active (context is null).
        /// </summary>
        private StoryFlowVariable FindVariableByName(string name, bool global)
        {
            // During dialogue, the context has everything wired up
            if (_context != null)
            {
                return global
                    ? _context.FindVariableByName(name, false, true)
                    : _context.FindVariableByName(name, true, true);
            }

            // Outside dialogue: local variables aren't available
            if (!global)
            {
                Debug.LogWarning($"[StoryFlow] Cannot access local variable \"{name}\" outside of active dialogue.");
                return null;
            }

            // Outside dialogue: scan manager's globals by display name
            var manager = StoryFlowManager.Instance;
            if (manager != null)
            {
                foreach (var kvp in manager.GlobalVariables)
                {
                    if (kvp.Value.Name == name)
                        return kvp.Value;
                }
            }

            Debug.LogWarning($"[StoryFlow] Global variable \"{name}\" not found.");
            return null;
        }

        /// <summary>
        /// Finds a runtime character by path, falling back to the manager when
        /// no dialogue is active.
        /// </summary>
        private StoryFlowCharacterData FindCharacter(string charPath)
        {
            if (string.IsNullOrEmpty(charPath)) return null;

            // During dialogue, the context has characters wired up
            if (_context != null)
                return _context.FindCharacter(charPath);

            // Outside dialogue: look up directly from the manager
            var manager = StoryFlowManager.Instance;
            if (manager != null)
            {
                var normalizedPath = StoryFlowPathNormalizer.NormalizeCharacterPath(charPath);
                if (manager.RuntimeCharacters.TryGetValue(normalizedPath, out var character))
                    return character;
            }

            return null;
        }

        /// <summary>
        /// Resolves a string table key to localized text using the component's LanguageCode.
        /// Works both during and outside of active dialogue.
        /// </summary>
        private string ResolveString(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            var fullKey = LanguageCode + "." + key;

            // During dialogue, context handles script + global string lookup
            if (_context != null)
                return _context.GetString(fullKey) ?? key;

            // Outside dialogue, resolve through the project's global strings
            var project = GetProject();
            if (project != null)
                return project.GetGlobalString(fullKey) ?? key;

            return key;
        }

        /// <summary>Gets a boolean variable by its display name. When global is true, searches only global; otherwise searches local first then global.</summary>
        public bool GetBoolVariable(string name, bool global = false)
        {
            var v = FindVariableByName(name, global);
            return v?.Value.GetBool() ?? false;
        }

        /// <summary>Sets a boolean variable by its display name. When global is true, targets only global scope.</summary>
        public void SetBoolVariable(string name, bool value, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v != null)
            {
                v.Value.SetBool(value);
                bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(v.Id);
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
            var v = FindVariableByName(name, global);
            return v?.Value.GetInt() ?? 0;
        }

        /// <summary>Sets an integer variable by its display name. When global is true, targets only global scope.</summary>
        public void SetIntVariable(string name, int value, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v != null)
            {
                v.Value.SetInt(value);
                bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(v.Id);
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
            var v = FindVariableByName(name, global);
            return v?.Value.GetFloat() ?? 0f;
        }

        /// <summary>Sets a float variable by its display name. When global is true, targets only global scope.</summary>
        public void SetFloatVariable(string name, float value, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v != null)
            {
                v.Value.SetFloat(value);
                bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(v.Id);
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
            var v = FindVariableByName(name, global);
            var raw = v?.Value.GetString() ?? "";
            return ResolveString(raw);
        }

        /// <summary>Sets a string variable by its display name. When global is true, targets only global scope.</summary>
        public void SetStringVariable(string name, string value, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v != null)
            {
                v.Value.SetString(value);
                bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(v.Id);
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
            var v = FindVariableByName(name, global);
            return v?.Value.GetEnum() ?? "";
        }

        /// <summary>Sets an enum variable by its display name. When global is true, targets only global scope.</summary>
        public void SetEnumVariable(string name, string value, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v != null)
            {
                v.Value.SetEnum(value);
                bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(v.Id);
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
            var characterData = FindCharacter(charPath);
            if (characterData == null)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterVariable: character at \"{charPath}\" not found.");
                return null;
            }

            // Handle built-in "Name" field (stored as string table key — resolve it)
            if (string.Equals(varName, "Name", System.StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolveString(characterData.Name);
                var result = new StoryFlowVariant();
                result.SetString(resolved);
                return result;
            }

            // Handle built-in "Image" field (current portrait asset key)
            if (string.Equals(varName, "Image", System.StringComparison.OrdinalIgnoreCase))
            {
                var result = new StoryFlowVariant();
                result.SetString(characterData.ImageAssetKey ?? "");
                return result;
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
            var characterData = FindCharacter(charPath);
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
        /// Returns the live runtime character data for a path, or null if no character is
        /// registered there. Useful when game code needs to read several fields without making
        /// a separate <see cref="GetCharacterVariable"/> call per field.
        /// </summary>
        public StoryFlowCharacterData GetCharacter(string characterPath)
        {
            return FindCharacter(characterPath);
        }

        /// <summary>
        /// Returns the names of all custom variables defined on a character.
        /// Does not include the built-in "Name" and "Image" fields, which are always
        /// available via <see cref="GetCharacterVariable"/> regardless of declaration.
        /// Returns an empty list if the character is missing.
        /// </summary>
        public List<string> GetCharacterVariables(string characterPath)
        {
            var out_ = new List<string>();
            var characterData = FindCharacter(characterPath);
            if (characterData == null) return out_;

            if (characterData.VariablesList != null)
            {
                foreach (var v in characterData.VariablesList)
                {
                    if (v != null && !string.IsNullOrEmpty(v.Name))
                        out_.Add(v.Name);
                }
            }
            else if (characterData.Variables != null)
            {
                foreach (var kvp in characterData.Variables)
                    out_.Add(kvp.Key);
            }
            return out_;
        }

        /// <summary>
        /// Resolves a character's portrait to a Sprite.
        /// When <paramref name="assetKey"/> is empty (default), uses the character's current
        /// <see cref="StoryFlowCharacterData.ImageAssetKey"/>, which reflects any runtime mutations
        /// from SetCharacterVar("Image", ...). Pass a non-empty asset key to resolve an alternate
        /// pose, e.g. one stored in a custom image-typed character variable.
        /// Walks the standard three asset pools in priority order: character → script → project.
        /// For the CURRENT portrait (empty assetKey, or an explicit key equal to the character's
        /// current ImageAssetKey), falls back to the character asset's baked <c>ResolvedImage</c>
        /// when no pool holds the key, so it stays reliable even when the portrait isn't in a pool.
        /// Returns null when the character is unknown, when the current portrait has no baked
        /// default, or when an explicit alternate key fails to resolve.
        /// </summary>
        public Sprite GetCharacterPortrait(string characterPath, string assetKey = "")
        {
            var characterData = FindCharacter(characterPath);
            if (characterData == null) return null;

            var key = !string.IsNullOrEmpty(assetKey) ? assetKey : characterData.ImageAssetKey;

            var project = GetProject();
            StoryFlowCharacterAsset characterAsset = null;
            if (project != null)
            {
                var normalizedPath = StoryFlowPathNormalizer.NormalizeCharacterPath(characterPath);
                characterAsset = project.GetCharacterAsset(normalizedPath);
            }

            if (!string.IsNullOrEmpty(key))
            {
                // 1. Check the character asset's own resolved-asset pool first. Currently the
                //    importer registers character-scoped assets into the PROJECT pool (step 2),
                //    not this per-character pool, so this tier is a reserved extension point and
                //    normally empty — the project cascade below does the real work.
                if (characterAsset != null &&
                    characterAsset.ResolvedAssets != null &&
                    characterAsset.ResolvedAssets.TryGetValue(key, out var charAsset) &&
                    charAsset is Sprite charSprite)
                {
                    return charSprite;
                }

                // 2. Fall back to the standard script → project cascade.
                var resolved = ResolveAsset<Sprite>(key);
                if (resolved != null) return resolved;
            }

            // 3. Last resort for the character's CURRENT portrait: the baked default sprite
            //    the importer stores directly on the asset (ResolvedImage). This keeps the API
            //    reliable for characters whose portrait isn't also registered in a resolved-asset
            //    pool. Skipped for an explicit alternate key that genuinely failed to resolve.
            if (string.IsNullOrEmpty(assetKey) || key == characterData.ImageAssetKey)
                return characterAsset != null ? characterAsset.ResolvedImage : null;

            return null;
        }

        /// <summary>
        /// Gets an array variable by its display name.
        /// Returns a list of StoryFlowVariant elements — call GetString(), GetInt(), etc. on each.
        /// String and enum values are resolved through the string table automatically.
        /// When global is true, searches only global; otherwise searches local first then global.
        /// </summary>
        public List<StoryFlowVariant> GetArrayVariable(string name, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v == null || !v.IsArray) return new List<StoryFlowVariant>();
            var array = v.Value.GetArray();

            // Return resolved copies so we don't mutate the stored variants
            var result = new List<StoryFlowVariant>(array.Count);
            foreach (var item in array)
            {
                var copy = new StoryFlowVariant(item);
                if (copy.Type == StoryFlowVariableType.String ||
                    copy.Type == StoryFlowVariableType.Image ||
                    copy.Type == StoryFlowVariableType.Audio ||
                    copy.Type == StoryFlowVariableType.Character)
                    copy.StringValue = ResolveString(copy.StringValue);
                else if (copy.Type == StoryFlowVariableType.Enum)
                    copy.EnumValue = ResolveString(copy.EnumValue);
                result.Add(copy);
            }
            return result;
        }

        /// <summary>
        /// Gets a map variable by its display name.
        /// Returns the entries as a list of <see cref="StoryFlowMapEntry"/> in insertion
        /// order (entry order is contractual — it matches the editor and mapKeys/mapValues
        /// projection). Call GetString(), GetInt(), etc. on each entry's Key/Value.
        /// String, enum, image, audio, and character VALUES are resolved through the
        /// string table automatically, mirroring <see cref="GetArrayVariable"/>; map KEYS
        /// are raw identifiers and are never localized (the runtime-wide map rule).
        /// When global is true, searches only global; otherwise searches local first then global.
        /// </summary>
        /// <remarks>
        /// The returned list and its entries are DEEP COPIES, never the live storage:
        /// map variables can share storage with each other (setMap aliasing), so handing
        /// out the live entry list would let game code corrupt every aliased variable at
        /// once. Returns an empty list if the variable is missing or is not a map.
        /// </remarks>
        public List<StoryFlowMapEntry> GetMapVariable(string name, bool global = false)
        {
            var v = FindVariableByName(name, global);
            if (v == null || v.Type != StoryFlowVariableType.Map) return new List<StoryFlowMapEntry>();
            var entries = v.Value.GetMap();

            var result = new List<StoryFlowMapEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var copy = new StoryFlowMapEntry
                {
                    Key = entry.Key != null ? new StoryFlowVariant(entry.Key) : new StoryFlowVariant(),
                    Value = entry.Value != null ? new StoryFlowVariant(entry.Value) : new StoryFlowVariant()
                };
                if (copy.Value.Type == StoryFlowVariableType.String ||
                    copy.Value.Type == StoryFlowVariableType.Image ||
                    copy.Value.Type == StoryFlowVariableType.Audio ||
                    copy.Value.Type == StoryFlowVariableType.Character)
                    copy.Value.StringValue = ResolveString(copy.Value.StringValue);
                else if (copy.Value.Type == StoryFlowVariableType.Enum)
                    copy.Value.EnumValue = ResolveString(copy.Value.EnumValue);
                result.Add(copy);
            }
            return result;
        }

        // =====================================================================
        // Typed map get/set accessors (developer-facing)
        //
        // Lets game code read and write StoryFlow map variables as native, typed C#
        // dictionaries, following the typed array setters. The key space collapses to
        // 2 native key types (string — also covers enum keys, which are strings — and
        // int) x 4 native value types (bool, int, float, string — the last also covers
        // enum/image/audio/character values, all stored as strings) = 8 typed
        // signatures, plus GetMapKeysInOrder.
        //
        // ORDERING CAVEAT: a C# Dictionary<TKey,TValue> does not contractually preserve
        // insertion order, yet StoryFlow map entry order IS contractual (it matches the
        // editor and the mapKeys/mapValues/forEachMap projection). The getters below build
        // a Dictionary from the ordered storage for ergonomic typed access, but that view
        // MAY reorder entries. Use GetMapKeysInOrder for the authoritative ordered key list.
        // =====================================================================

        /// <summary>
        /// Shared find + map-type guard for the typed map accessors. Resolves the variable by
        /// display name (local first then global unless <paramref name="global"/>), warns and
        /// returns null when it is missing or not a map. Per-accessor key/value-family validation
        /// happens at the call site.
        /// </summary>
        private StoryFlowVariable FindMapVariableForAccess(string accessor, string variableName, bool global)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] {accessor}: variable \"{variableName}\" not found.");
                return null;
            }
            if (variable.Type != StoryFlowVariableType.Map)
            {
                Debug.LogWarning($"[StoryFlow] {accessor}: variable \"{variableName}\" is not a map.");
                return null;
            }
            return variable;
        }

        // True when the map's key type is one of the string-native families (string or enum;
        // enum keys are stored as strings and read back verbatim).
        private static bool IsStringKeyFamily(StoryFlowVariable v)
            => v.KeyType == StoryFlowVariableType.String || v.KeyType == StoryFlowVariableType.Enum;

        // True when the value type is one of the string-native families. NOTE: StoryFlowVariant
        // stores enum text in a SEPARATE EnumValue field (not the StringValue field), so enum
        // keys/values must be read via GetEnum() — see MapVariantText below.
        private static bool IsStringValueFamily(StoryFlowVariableType vt)
            => vt == StoryFlowVariableType.String || vt == StoryFlowVariableType.Enum ||
               vt == StoryFlowVariableType.Image || vt == StoryFlowVariableType.Audio ||
               vt == StoryFlowVariableType.Character;

        // Reads a variant's text regardless of whether it is enum-typed (EnumValue) or one of
        // the other string-family types (StringValue). Used for string-family map keys and
        // values so enum entries — stored via the enum path — read back correctly.
        private static string MapVariantText(StoryFlowVariant variant)
        {
            if (variant == null) return "";
            return variant.Type == StoryFlowVariableType.Enum ? variant.GetEnum() : variant.GetString();
        }

        /// <summary>
        /// Reads a string-keyed, boolean-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// Keys are returned raw (never localized). On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve the contractual map insertion order — use
        /// <see cref="GetMapKeysInOrder"/> for the ordered key list. When global is true, searches
        /// only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<string, bool> GetStringToBoolMap(string variableName, bool global = false)
        {
            var result = new Dictionary<string, bool>();
            var v = FindMapVariableForAccess(nameof(GetStringToBoolMap), variableName, global);
            if (v == null) return result;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] GetStringToBoolMap: map \"{variableName}\" does not have string keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Boolean)
            {
                Debug.LogWarning($"[StoryFlow] GetStringToBoolMap: map \"{variableName}\" does not have boolean values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[MapVariantText(entry.Key)] = entry.Value?.GetBool() ?? false;
            }
            return result;
        }

        /// <summary>
        /// Reads a string-keyed, integer-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// Keys are returned raw. On a missing/non-map/wrong-key/wrong-value variable, logs a
        /// warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<string, int> GetStringToIntMap(string variableName, bool global = false)
        {
            var result = new Dictionary<string, int>();
            var v = FindMapVariableForAccess(nameof(GetStringToIntMap), variableName, global);
            if (v == null) return result;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] GetStringToIntMap: map \"{variableName}\" does not have string keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetStringToIntMap: map \"{variableName}\" does not have integer values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[MapVariantText(entry.Key)] = entry.Value?.GetInt() ?? 0;
            }
            return result;
        }

        /// <summary>
        /// Reads a string-keyed, float-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// Keys are returned raw. On a missing/non-map/wrong-key/wrong-value variable, logs a
        /// warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<string, float> GetStringToFloatMap(string variableName, bool global = false)
        {
            var result = new Dictionary<string, float>();
            var v = FindMapVariableForAccess(nameof(GetStringToFloatMap), variableName, global);
            if (v == null) return result;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] GetStringToFloatMap: map \"{variableName}\" does not have string keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Float)
            {
                Debug.LogWarning($"[StoryFlow] GetStringToFloatMap: map \"{variableName}\" does not have float values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[MapVariantText(entry.Key)] = entry.Value?.GetFloat() ?? 0f;
            }
            return result;
        }

        /// <summary>
        /// Reads a string-keyed, string-family-valued map variable (string/enum/image/audio/character
        /// values) as a <see cref="Dictionary{TKey,TValue}"/>. String and enum values are resolved
        /// through the string table (mirroring <see cref="GetArrayVariable"/>); image/audio/character
        /// values are returned raw (asset keys / character paths). Keys are returned raw. On a
        /// missing/non-map/wrong-key/wrong-value variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<string, string> GetStringToStringMap(string variableName, bool global = false)
        {
            var result = new Dictionary<string, string>();
            var v = FindMapVariableForAccess(nameof(GetStringToStringMap), variableName, global);
            if (v == null) return result;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] GetStringToStringMap: map \"{variableName}\" does not have string keys.");
                return result;
            }
            if (!IsStringValueFamily(v.ValueType))
            {
                Debug.LogWarning($"[StoryFlow] GetStringToStringMap: map \"{variableName}\" does not have string-family values.");
                return result;
            }
            bool resolve = v.ValueType == StoryFlowVariableType.String || v.ValueType == StoryFlowVariableType.Enum;
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                var raw = MapVariantText(entry.Value);
                result[MapVariantText(entry.Key)] = resolve ? ResolveString(raw) : raw;
            }
            return result;
        }

        /// <summary>
        /// Reads an integer-keyed, boolean-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// On a missing/non-map/wrong-key/wrong-value variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<int, bool> GetIntToBoolMap(string variableName, bool global = false)
        {
            var result = new Dictionary<int, bool>();
            var v = FindMapVariableForAccess(nameof(GetIntToBoolMap), variableName, global);
            if (v == null) return result;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToBoolMap: map \"{variableName}\" does not have integer keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Boolean)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToBoolMap: map \"{variableName}\" does not have boolean values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[entry.Key.GetInt()] = entry.Value?.GetBool() ?? false;
            }
            return result;
        }

        /// <summary>
        /// Reads an integer-keyed, integer-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// On a missing/non-map/wrong-key/wrong-value variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<int, int> GetIntToIntMap(string variableName, bool global = false)
        {
            var result = new Dictionary<int, int>();
            var v = FindMapVariableForAccess(nameof(GetIntToIntMap), variableName, global);
            if (v == null) return result;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToIntMap: map \"{variableName}\" does not have integer keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToIntMap: map \"{variableName}\" does not have integer values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[entry.Key.GetInt()] = entry.Value?.GetInt() ?? 0;
            }
            return result;
        }

        /// <summary>
        /// Reads an integer-keyed, float-valued map variable as a <see cref="Dictionary{TKey,TValue}"/>.
        /// On a missing/non-map/wrong-key/wrong-value variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<int, float> GetIntToFloatMap(string variableName, bool global = false)
        {
            var result = new Dictionary<int, float>();
            var v = FindMapVariableForAccess(nameof(GetIntToFloatMap), variableName, global);
            if (v == null) return result;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToFloatMap: map \"{variableName}\" does not have integer keys.");
                return result;
            }
            if (v.ValueType != StoryFlowVariableType.Float)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToFloatMap: map \"{variableName}\" does not have float values.");
                return result;
            }
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                result[entry.Key.GetInt()] = entry.Value?.GetFloat() ?? 0f;
            }
            return result;
        }

        /// <summary>
        /// Reads an integer-keyed, string-family-valued map variable (string/enum/image/audio/character
        /// values) as a <see cref="Dictionary{TKey,TValue}"/>. String and enum values are resolved
        /// through the string table; image/audio/character values are returned raw. On a
        /// missing/non-map/wrong-key/wrong-value variable, logs a warning and returns an empty dictionary.
        /// </summary>
        /// <remarks>
        /// The returned dictionary MAY NOT preserve insertion order — use <see cref="GetMapKeysInOrder"/>.
        /// When global is true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public Dictionary<int, string> GetIntToStringMap(string variableName, bool global = false)
        {
            var result = new Dictionary<int, string>();
            var v = FindMapVariableForAccess(nameof(GetIntToStringMap), variableName, global);
            if (v == null) return result;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] GetIntToStringMap: map \"{variableName}\" does not have integer keys.");
                return result;
            }
            if (!IsStringValueFamily(v.ValueType))
            {
                Debug.LogWarning($"[StoryFlow] GetIntToStringMap: map \"{variableName}\" does not have string-family values.");
                return result;
            }
            bool resolve = v.ValueType == StoryFlowVariableType.String || v.ValueType == StoryFlowVariableType.Enum;
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                var raw = MapVariantText(entry.Value);
                result[entry.Key.GetInt()] = resolve ? ResolveString(raw) : raw;
            }
            return result;
        }

        /// <summary>
        /// Returns a map variable's keys as a <see cref="List{T}"/> of strings in the contractual
        /// insertion order (matching the editor and the mapKeys/forEachMap projection). String and
        /// enum keys come back verbatim; integer keys are stringified via <see cref="int.ToString()"/>.
        /// This is the ordered escape hatch for the typed Get*Map getters, whose Dictionary view does
        /// not contractually preserve order.
        /// </summary>
        /// <remarks>
        /// On a missing or non-map variable, logs a warning and returns an empty list. When global is
        /// true, searches only the global scope; otherwise searches local first then global.
        /// </remarks>
        public List<string> GetMapKeysInOrder(string variableName, bool global = false)
        {
            var result = new List<string>();
            var v = FindMapVariableForAccess(nameof(GetMapKeysInOrder), variableName, global);
            if (v == null) return result;
            bool integerKey = v.KeyType == StoryFlowVariableType.Integer;
            foreach (var entry in v.Value.GetMap())
            {
                if (entry?.Key == null) continue;
                // String/enum keys come back verbatim (enum via EnumValue); integer keys stringified.
                result.Add(integerKey ? entry.Key.GetInt().ToString() : MapVariantText(entry.Key));
            }
            return result;
        }

        /// <summary>
        /// Sets a string-keyed, boolean-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// Entries are stored as an ordered list in the dictionary's enumeration order. A null
        /// dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value variable,
        /// logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// When global is true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetStringToBoolMap(string variableName, Dictionary<string, bool> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetStringToBoolMap), variableName, global);
            if (v == null) return;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] SetStringToBoolMap: map \"{variableName}\" does not have string keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Boolean)
            {
                Debug.LogWarning($"[StoryFlow] SetStringToBoolMap: map \"{variableName}\" does not have boolean values.");
                return;
            }
            bool enumKey = v.KeyType == StoryFlowVariableType.Enum;
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = MakeStringKey(pair.Key, enumKey), Value = StoryFlowVariant.Bool(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets a string-keyed, integer-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetStringToIntMap(string variableName, Dictionary<string, int> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetStringToIntMap), variableName, global);
            if (v == null) return;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] SetStringToIntMap: map \"{variableName}\" does not have string keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetStringToIntMap: map \"{variableName}\" does not have integer values.");
                return;
            }
            bool enumKey = v.KeyType == StoryFlowVariableType.Enum;
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = MakeStringKey(pair.Key, enumKey), Value = StoryFlowVariant.Int(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets a string-keyed, float-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetStringToFloatMap(string variableName, Dictionary<string, float> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetStringToFloatMap), variableName, global);
            if (v == null) return;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] SetStringToFloatMap: map \"{variableName}\" does not have string keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Float)
            {
                Debug.LogWarning($"[StoryFlow] SetStringToFloatMap: map \"{variableName}\" does not have float values.");
                return;
            }
            bool enumKey = v.KeyType == StoryFlowVariableType.Enum;
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = MakeStringKey(pair.Key, enumKey), Value = StoryFlowVariant.Float(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets a string-keyed, string-family-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// Values are stored verbatim via the value type's storage path (enum values via the enum path,
        /// string/image/audio/character via the string path), matching the typed array setters and the
        /// editor importer; resolution happens later at read time. A null dictionary is treated as an
        /// empty map. On a missing/non-map/wrong-key/wrong-value variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// <para>
        /// Localization: for string/enum values, pass strings-table keys to keep them translatable;
        /// raw display text bypasses the table and will not be re-translated. Image/audio/character
        /// values are asset keys / character paths.
        /// </para>
        /// </remarks>
        public void SetStringToStringMap(string variableName, Dictionary<string, string> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetStringToStringMap), variableName, global);
            if (v == null) return;
            if (!IsStringKeyFamily(v))
            {
                Debug.LogWarning($"[StoryFlow] SetStringToStringMap: map \"{variableName}\" does not have string keys.");
                return;
            }
            if (!IsStringValueFamily(v.ValueType))
            {
                Debug.LogWarning($"[StoryFlow] SetStringToStringMap: map \"{variableName}\" does not have string-family values.");
                return;
            }
            bool enumKey = v.KeyType == StoryFlowVariableType.Enum;
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry
                    {
                        Key = MakeStringKey(pair.Key, enumKey),
                        Value = MakeStringValue(pair.Value, v.ValueType)
                    });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets an integer-keyed, boolean-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetIntToBoolMap(string variableName, Dictionary<int, bool> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetIntToBoolMap), variableName, global);
            if (v == null) return;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToBoolMap: map \"{variableName}\" does not have integer keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Boolean)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToBoolMap: map \"{variableName}\" does not have boolean values.");
                return;
            }
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = StoryFlowVariant.Int(pair.Key), Value = StoryFlowVariant.Bool(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets an integer-keyed, integer-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetIntToIntMap(string variableName, Dictionary<int, int> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetIntToIntMap), variableName, global);
            if (v == null) return;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToIntMap: map \"{variableName}\" does not have integer keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToIntMap: map \"{variableName}\" does not have integer values.");
                return;
            }
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = StoryFlowVariant.Int(pair.Key), Value = StoryFlowVariant.Int(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets an integer-keyed, float-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetIntToFloatMap(string variableName, Dictionary<int, float> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetIntToFloatMap), variableName, global);
            if (v == null) return;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToFloatMap: map \"{variableName}\" does not have integer keys.");
                return;
            }
            if (v.ValueType != StoryFlowVariableType.Float)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToFloatMap: map \"{variableName}\" does not have float values.");
                return;
            }
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry { Key = StoryFlowVariant.Int(pair.Key), Value = StoryFlowVariant.Float(pair.Value) });
            }
            ApplyMapSet(v, entries, global);
        }

        /// <summary>
        /// Sets an integer-keyed, string-family-valued map variable from a <see cref="Dictionary{TKey,TValue}"/>.
        /// Values are stored verbatim via the value type's storage path (enum via the enum path,
        /// string/image/audio/character via the string path); resolution happens later at read time.
        /// A null dictionary is treated as an empty map. On a missing/non-map/wrong-key/wrong-value
        /// variable, logs a warning and no-ops.
        /// </summary>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node. When global is
        /// true, targets only the global scope; otherwise searches local first then global.
        /// </remarks>
        public void SetIntToStringMap(string variableName, Dictionary<int, string> values, bool global = false)
        {
            var v = FindMapVariableForAccess(nameof(SetIntToStringMap), variableName, global);
            if (v == null) return;
            if (v.KeyType != StoryFlowVariableType.Integer)
            {
                Debug.LogWarning($"[StoryFlow] SetIntToStringMap: map \"{variableName}\" does not have integer keys.");
                return;
            }
            if (!IsStringValueFamily(v.ValueType))
            {
                Debug.LogWarning($"[StoryFlow] SetIntToStringMap: map \"{variableName}\" does not have string-family values.");
                return;
            }
            var entries = new List<StoryFlowMapEntry>();
            if (values != null)
            {
                foreach (var pair in values)
                    entries.Add(new StoryFlowMapEntry
                    {
                        Key = StoryFlowVariant.Int(pair.Key),
                        Value = MakeStringValue(pair.Value, v.ValueType)
                    });
            }
            ApplyMapSet(v, entries, global);
        }

        // Builds a key variant for the string key family: enum keys go through the enum path,
        // plain string keys through the string path (matching the importer/array-setter split).
        private static StoryFlowVariant MakeStringKey(string key, bool enumKey)
            => enumKey ? StoryFlowVariant.Enum(key ?? "") : StoryFlowVariant.String(key ?? "");

        // Builds a string-family value variant typed to the declared value type. Enum values use
        // the enum path; string/image/audio/character all store the text in StringValue.
        private static StoryFlowVariant MakeStringValue(string value, StoryFlowVariableType valueType)
        {
            if (valueType == StoryFlowVariableType.Enum)
                return StoryFlowVariant.Enum(value ?? "");
            return new StoryFlowVariant { Type = valueType, StringValue = value ?? "" };
        }

        // Shared tail for the typed map setters: assigns the ordered entry list, recomputes the
        // global/local scope, broadcasts the change, and refreshes the current dialogue — exactly
        // like the typed array setters' tail (variable.Value.ArrayValue = ...; broadcast; refresh).
        private void ApplyMapSet(StoryFlowVariable variable, List<StoryFlowMapEntry> entries, bool global)
        {
            variable.Value.SetMap(entries);
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);
            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Reads a script or global variable of type character-array and returns the character
        /// paths stored in it. Each path is suitable for <see cref="GetCharacter"/>,
        /// <see cref="GetCharacterVariable"/>, and <see cref="GetCharacterPortrait"/>.
        /// </summary>
        /// <remarks>
        /// This reads a <em>script variable whose element type is character</em>, which is distinct
        /// from <see cref="GetCharacterVariable"/>, which reads a variable that lives <em>on</em> a
        /// character. Returns an empty list with a warning when the variable is missing, not an
        /// array, or not a character array.
        /// </remarks>
        public List<string> GetCharacterArrayVariable(string variableName)
        {
            var out_ = new List<string>();
            var variable = FindVariableByName(variableName, false);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterArrayVariable: variable \"{variableName}\" not found.");
                return out_;
            }
            if (!variable.IsArray)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterArrayVariable: variable \"{variableName}\" is not an array.");
                return out_;
            }
            if (variable.Type != StoryFlowVariableType.Character)
            {
                Debug.LogWarning($"[StoryFlow] GetCharacterArrayVariable: variable \"{variableName}\" is not a character array.");
                return out_;
            }

            var array = variable.Value?.ArrayValue;
            if (array == null) return out_;
            foreach (var item in array)
            {
                if (item != null)
                    out_.Add(item.StringValue ?? "");
            }
            return out_;
        }

        /// <summary>
        /// Sets a boolean array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="values">New element values. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetBoolArrayVariable(string variableName, List<bool> values, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetBoolArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (values != null)
            {
                foreach (var b in values)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Boolean,
                        BoolValue = b
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets an integer array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="values">New element values. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetIntArrayVariable(string variableName, List<int> values, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetIntArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (values != null)
            {
                foreach (var i in values)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Integer,
                        IntValue = i
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets a float array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="values">New element values. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetFloatArrayVariable(string variableName, List<float> values, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetFloatArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (values != null)
            {
                foreach (var f in values)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Float,
                        FloatValue = f
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets a string array variable by its display name. Pass raw runtime strings — they are
        /// stored as-is and round-tripped cleanly through <see cref="GetArrayVariable"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="values">New element values. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// <para>
        /// Localization: stored values are language-locked. They bypass the strings table and will
        /// not be re-translated when <see cref="LanguageCode"/> changes. Prefer this for IDs, flags,
        /// or player input; for translatable display text, set the value at the editor variable
        /// default and leave it for the strings table to resolve.
        /// </para>
        /// </remarks>
        public void SetStringArrayVariable(string variableName, List<string> values, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetStringArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (values != null)
            {
                foreach (var s in values)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.String,
                        StringValue = s ?? ""
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets an enum array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="values">New element values (enum option strings). A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// <para>
        /// Localization: stored values are language-locked. They bypass the strings table and will
        /// not be re-translated when <see cref="LanguageCode"/> changes. Enum values are typically
        /// code-like identifiers, so this rarely matters in practice.
        /// </para>
        /// </remarks>
        public void SetEnumArrayVariable(string variableName, List<string> values, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetEnumArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (values != null)
            {
                foreach (var e in values)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Enum,
                        EnumValue = e ?? ""
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets an image array variable by its display name. Each entry is the asset key used by
        /// the runtime to resolve a Unity sprite via <see cref="ResolveAsset{T}"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="assetKeys">New asset keys. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetImageArrayVariable(string variableName, List<string> assetKeys, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetImageArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (assetKeys != null)
            {
                foreach (var key in assetKeys)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Image,
                        StringValue = key ?? ""
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets an audio array variable by its display name. Each entry is the asset key used by
        /// the runtime to resolve a Unity AudioClip via <see cref="ResolveAsset{T}"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="assetKeys">New asset keys. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetAudioArrayVariable(string variableName, List<string> assetKeys, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetAudioArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (assetKeys != null)
            {
                foreach (var key in assetKeys)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Audio,
                        StringValue = key ?? ""
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Sets a character array variable by its display name. Each entry is the character path
        /// used by the runtime to resolve character data via <see cref="GetCharacterVariable"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="characterPaths">New character paths. A null list is treated as an empty array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void SetCharacterArrayVariable(string variableName, List<string> characterPaths, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterArrayVariable: variable \"{variableName}\" not found.");
                return;
            }

            var newArray = new List<StoryFlowVariant>();
            if (characterPaths != null)
            {
                foreach (var path in characterPaths)
                {
                    newArray.Add(new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.Character,
                        StringValue = path ?? ""
                    });
                }
            }

            variable.Value.ArrayValue = newArray;
            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends a boolean to a boolean array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="value">Element to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddBoolArrayElement(string variableName, bool value, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddBoolArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Boolean,
                BoolValue = value
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends an integer to an integer array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="value">Element to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddIntArrayElement(string variableName, int value, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddIntArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Integer,
                IntValue = value
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends a float to a float array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="value">Element to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddFloatArrayElement(string variableName, float value, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddFloatArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Float,
                FloatValue = value
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends a string to a string array variable by its display name. Pass a raw runtime
        /// string — it is stored as-is and round-tripped cleanly through <see cref="GetArrayVariable"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="value">Element to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// <para>
        /// Localization: stored values are language-locked. They bypass the strings table and will
        /// not be re-translated when <see cref="LanguageCode"/> changes. Prefer this for IDs, flags,
        /// or player input; for translatable display text, set the value at the editor variable
        /// default and leave it for the strings table to resolve.
        /// </para>
        /// </remarks>
        public void AddStringArrayElement(string variableName, string value, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddStringArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.String,
                StringValue = value ?? ""
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends an enum option to an enum array variable by its display name.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="value">Element to append at the end of the array (enum option string).</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// <para>
        /// Localization: stored values are language-locked. They bypass the strings table and will
        /// not be re-translated when <see cref="LanguageCode"/> changes. Enum values are typically
        /// code-like identifiers, so this rarely matters in practice.
        /// </para>
        /// </remarks>
        public void AddEnumArrayElement(string variableName, string value, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddEnumArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Enum,
                EnumValue = value ?? ""
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends an asset key to an image array variable by its display name. The key is the
        /// identifier the runtime uses to resolve a Unity sprite via <see cref="ResolveAsset{T}"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="assetKey">Asset key to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddImageArrayElement(string variableName, string assetKey, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddImageArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Image,
                StringValue = assetKey ?? ""
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends an asset key to an audio array variable by its display name. The key is the
        /// identifier the runtime uses to resolve a Unity AudioClip via <see cref="ResolveAsset{T}"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="assetKey">Asset key to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddAudioArrayElement(string variableName, string assetKey, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddAudioArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Audio,
                StringValue = assetKey ?? ""
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        /// <summary>
        /// Appends a character path to a character array variable by its display name. The path
        /// is the identifier the runtime uses to resolve character data via <see cref="GetCharacterVariable"/>.
        /// </summary>
        /// <param name="variableName">Display name of the array variable.</param>
        /// <param name="characterPath">Character path to append at the end of the array.</param>
        /// <param name="global">When true, targets only the global scope; otherwise searches local first then global.</param>
        /// <remarks>
        /// Broadcasts <c>OnVariableChanged</c> and re-renders the current dialogue node so option
        /// visibility and text interpolation refresh — callers do not need to trigger anything manually.
        /// </remarks>
        public void AddCharacterArrayElement(string variableName, string characterPath, bool global = false)
        {
            var variable = FindVariableByName(variableName, global);
            if (variable == null)
            {
                Debug.LogWarning($"[StoryFlow] AddCharacterArrayElement: variable \"{variableName}\" not found.");
                return;
            }

            if (variable.Value.ArrayValue == null)
                variable.Value.ArrayValue = new List<StoryFlowVariant>();

            variable.Value.ArrayValue.Add(new StoryFlowVariant
            {
                Type = StoryFlowVariableType.Character,
                StringValue = characterPath ?? ""
            });

            bool isGlobal = _context == null || !_context.LocalVariables.ContainsKey(variable.Id);
            BroadcastVariableChanged(variable, isGlobal);

            RefreshCurrentDialogueAfterArraySet();
        }

        // Re-renders the current dialogue node after a public Set*ArrayVariable / Add*ArrayElement
        // call so that option visibility and text interpolation pick up the new array values.
        // Mirrors the fallthrough in BooleanNodeHandler.SetNodeFallthrough but calls ProcessNode
        // directly because the public setter runs outside the iterative ProcessNode loop. The
        // re-entry guard prevents recursion when the setter is called from inside OnDialogueUpdated.
        // Clears all node CachedOutput values so option-visibility conditions that read array
        // length or findIn*Array indices via integer/float comparison chains see fresh values
        // rather than caches from the previous render. ProcessBooleanChain only walks boolean
        // chains, so without this clear, integer/float caches downstream of comparisons leak
        // stale results between renders.
        private void RefreshCurrentDialogueAfterArraySet()
        {
            if (_isRefreshingDialogue) return;
            if (_context == null || string.IsNullOrEmpty(_context.LastDialogueNodeId)) return;

            var dialogueNode = _context.CurrentScript?.GetNode(_context.LastDialogueNodeId);
            if (dialogueNode == null) return;

            _context.ClearNodeRuntimeStates();
            _context.EnteringDialogueViaEdge = false;
            _isRefreshingDialogue = true;
            try { ProcessNode(dialogueNode); }
            finally { _isRefreshingDialogue = false; }
        }

        /// <summary>
        /// Clears all cached node runtime states and re-initializes local variables
        /// from the current script asset.
        /// </summary>
        public void ResetVariables()
        {
            _context?.ClearNodeRuntimeStates();

            // Re-initialize local variables from the current script
            if (_context?.CurrentScript != null)
            {
                var script = _context.CurrentScript;
                if (script.Variables != null)
                {
                    foreach (var kvp in script.Variables)
                    {
                        if (_context.LocalVariables.TryGetValue(kvp.Key, out var localVar))
                        {
                            localVar.Value = new StoryFlowVariant(kvp.Value.Value);
                        }
                    }
                    _context.InvalidateLocalNameIndex();
                }
            }
        }

        /// <summary>
        /// Looks up a localized string using the component's LanguageCode.
        /// </summary>
        public string GetLocalizedString(string key)
        {
            return ResolveString(key);
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

            if (!_isDialogueActive || _context == null) return;
            if (_context.IsPaused) return;

            _context.NextNode = node;
            _context.ShouldPause = false;
            int iterationCount = 0;

            while (_context.NextNode != null && !_context.ShouldPause)
            {
                var current = _context.NextNode;
                _context.NextNode = null;

                iterationCount++;
                if (iterationCount > StoryFlowExecutionContext.MaxProcessingDepth)
                {
                    BroadcastError($"Processing depth exceeded {StoryFlowExecutionContext.MaxProcessingDepth}. " +
                                   "Possible infinite loop detected. Stopping dialogue.");
                    StopDialogue();
                    return;
                }

                _context.CurrentNodeId = current.Id;

                // Trace: log every node that enters the processing loop.
                // Trace parity: the HTML runtime never processes start nodes — every
                // entry point (initial load, runScript, flows) follows the edge out of
                // node "0" and processes its TARGET directly, so HTML traces contain no
                // start hop. Unity routes through the start node; suppress its NODE line
                // (and the matching EDGE line in ProcessNextNode) so traces diff 1:1
                // against the cross-runtime map-trace-fixture.
                if (TraceEnabled && current.Type != Data.StoryFlowNodeType.Start)
                {
                    var typeName = !string.IsNullOrEmpty(current.RawType) ? current.RawType : current.Type.ToString();
                    Trace($"NODE {current.Id} {typeName}");
                }

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

            // The loop has drained: execution paused at a dialogue awaiting input, ended, or
            // hit a dead end. Signal once so listeners can react to the completed batch of
            // synchronous Set*/character changes instead of per-change mid-sequence.
            BroadcastExecutionSettled();
        }

        /// <summary>
        /// Finds the edge connected to the given source handle and sets the next node
        /// for the iterative loop to pick up.
        /// </summary>
        internal void ProcessNextNode(string sourceHandle)
        {
            if (_context == null || _context.CurrentScript == null) return;

            _context.LastSourceHandle = sourceHandle;
            var edge = _context.CurrentScript.FindEdgeBySourceHandle(sourceHandle);
            if (edge == null) return;

            var targetNodeId = edge.Target;
            if (string.IsNullOrEmpty(targetNodeId)) return;

            var targetNode = _context.CurrentScript.GetNode(targetNodeId);
            if (targetNode == null)
            {
                BroadcastError($"Target node not found: {targetNodeId}");
                return;
            }

            // Trace: log edge traversal.
            // Trace parity: suppress the edge OUT of a start node — the HTML runtime
            // follows it without tracing (see the matching NODE gate in ProcessNode).
            if (TraceEnabled)
            {
                var edgeSourceNode = _context.CurrentScript.GetNode(edge.Source);
                if (edgeSourceNode == null || edgeSourceNode.Type != Data.StoryFlowNodeType.Start)
                {
                    // Extract source node ID from the source handle (format: "source-{nodeId}-{suffix}")
                    var sourceNodeId = _context.CurrentNodeId ?? "";
                    Trace($"EDGE {sourceNodeId}:{sourceHandle} -> {targetNodeId}");
                }
            }

            // Mark fresh entry when following an edge into a dialogue node
            if (targetNode.Type == Data.StoryFlowNodeType.Dialogue)
                _context.EnteringDialogueViaEdge = true;

            _context.NextNode = targetNode;
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
            if (_context?.CurrentDialogueState == null) return;

            OnDialogueUpdated?.Invoke(_context.CurrentDialogueState);
            OnDialogueUpdatedEvent?.Invoke(_context.CurrentDialogueState);
        }

        /// <summary>
        /// Broadcasts a single dialogue tag. Called once per tag, in authored order, when a
        /// tagged dialogue node is freshly entered. The tag string is passed through untouched.
        /// </summary>
        internal void BroadcastDialogueTag(string tag)
        {
            OnDialogueTagReached?.Invoke(tag);
            OnDialogueTagReachedEvent?.Invoke(tag);
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
        /// Broadcasts a character variable change notification. Fires alongside (not instead of)
        /// <see cref="OnVariableChanged"/> when a SetCharacterVar node mutates the built-in
        /// "Name"/"Image" fields or a custom character variable.
        /// </summary>
        internal void BroadcastCharacterVariableChanged(string characterPath, string variableName, StoryFlowVariant value)
        {
            OnCharacterVariableChanged?.Invoke(characterPath, variableName, value);
        }

        /// <summary>
        /// Broadcasts a character portrait change with the resolved Sprite. Called after a
        /// SetCharacterVar node mutates the built-in "Image" field and the new asset key has
        /// been resolved via <see cref="GetCharacterPortrait"/>.
        /// </summary>
        internal void BroadcastCharacterImageChanged(string characterPath, Sprite sprite)
        {
            OnCharacterImageChanged?.Invoke(characterPath, sprite);
        }

        /// <summary>
        /// Broadcasts that a synchronous run of nodes has settled (paused at a dialogue,
        /// ended, or hit a dead end). Fired once per drain of the processing loop.
        /// </summary>
        internal void BroadcastExecutionSettled()
        {
            OnExecutionSettled?.Invoke();
        }

        /// <summary>
        /// Error text for a missing project, with guidance matching where it can
        /// actually be fixed: the import menu in the editor, project settings or an
        /// asset reference for player builds (editor menus don't exist there).
        /// </summary>
        private static string NoProjectFoundMessage()
        {
#if UNITY_EDITOR
            return "Cannot start dialogue: no StoryFlow project found. " +
                   "Import a project via Tools > StoryFlow > Import Project.";
#else
            return "Cannot start dialogue: no StoryFlow project found in this build. " +
                   "Assign Default Project in Edit > Project Settings > StoryFlow and rebuild " +
                   "(plugin 1.2.2+ sets it automatically on import), reference the project asset " +
                   "from a scene, or call StoryFlowManager.SetProject() before starting dialogue.";
#endif
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

        /// <summary>
        /// Logs an execution trace message with the [SF-TRACE] prefix.
        /// Only emits when TraceEnabled is true on this component.
        /// </summary>
        internal void Trace(string message)
        {
            if (TraceEnabled)
                Debug.Log("[SF-TRACE] " + message);
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
            if (_dialogueAudioSource == null)
            {
                _dialogueAudioSource = gameObject.AddComponent<AudioSource>();
                _dialogueAudioSource.playOnAwake = false;
                _dialogueAudioSource.spatialBlend = 0f; // 2D audio
            }

            // Stop any currently playing audio
            if (_dialogueAudioSource.isPlaying)
                _dialogueAudioSource.Stop();

            // Configure
            _dialogueAudioSource.clip = clip;
            _dialogueAudioSource.loop = loop;
            _dialogueAudioSource.outputAudioMixerGroup = DialogueAudioMixerGroup;
            _dialogueAudioSource.volume = DialogueVolumeMultiplier;

            _dialogueAudioSource.Play();
            CurrentDialogueAudioClip = clip;
        }

        /// <summary>
        /// Stops the dialogue audio source if it is currently playing.
        /// </summary>
        public void StopDialogueAudio()
        {
            if (_dialogueAudioSource != null && _dialogueAudioSource.isPlaying)
            {
                _dialogueAudioSource.Stop();
                _dialogueAudioSource.clip = null;
            }
            CurrentDialogueAudioClip = null;
            _waitingForAudioAdvance = false;
            _audioAdvanceAllowSkip = false;
        }

        /// <summary>Returns true if dialogue audio is currently playing.</summary>
        public bool IsDialogueAudioPlaying()
        {
            return _dialogueAudioSource != null && _dialogueAudioSource.isPlaying;
        }

        /// <summary>Sets the audio advance-on-end state flags.</summary>
        public void SetAudioAdvanceState(bool waiting, bool allowSkip)
        {
            _waitingForAudioAdvance = waiting;
            _audioAdvanceAllowSkip = allowSkip;
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
            if (_context?.CurrentScript != null &&
                _context.CurrentScript.ResolvedAssets != null &&
                _context.CurrentScript.ResolvedAssets.TryGetValue(assetKey, out var scriptAsset))
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
            if (_isDialogueActive)
            {
                StopDialogue();
            }
        }

        private void OnDestroy()
        {
            if (_isDialogueActive)
                StopDialogue();

            // Clear all C# event subscribers to prevent memory leaks
            OnDialogueStarted = null;
            OnDialogueUpdated = null;
            OnDialogueTagReached = null;
            OnDialogueEnded = null;
            OnVariableChanged = null;
            OnCharacterVariableChanged = null;
            OnCharacterImageChanged = null;
            OnExecutionSettled = null;
            OnError = null;
            OnAudioPlayRequested = null;
            OnBackgroundImageChanged = null;
            OnScriptStarted = null;
            OnScriptEnded = null;
        }

        // =====================================================================
        // Fallback UI
        // =====================================================================

        private void CreateFallbackUI()
        {
            string typeName = UIStyle == BuiltInUIStyle.Portrait
                ? "StoryFlow.UI.StoryFlowRuntimeUIPortrait"
                : "StoryFlow.UI.StoryFlowRuntimeUI";

            // Use System.Type lookup to avoid hard reference (allows build verify to exclude the UI file)
            var uiType = System.Type.GetType(typeName + ", StoryFlow.Runtime");
            if (uiType == null) uiType = System.Type.GetType(typeName);
            if (uiType != null)
            {
                var runtimeUI = gameObject.AddComponent(uiType);
                uiType.GetMethod("Build")?.Invoke(runtimeUI, null);
                DialogueUI = runtimeUI as StoryFlow.UI.StoryFlowDialogueUI;
            }
        }
    }
}
