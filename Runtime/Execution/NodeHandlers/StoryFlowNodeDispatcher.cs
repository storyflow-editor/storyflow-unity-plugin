using System;
using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Static dispatch table that maps StoryFlowNodeType to the appropriate handler method.
    /// Pure logic-only nodes (evaluated lazily by StoryFlowEvaluator) use a no-op handler.
    ///
    /// Users can extend StoryFlow with custom node types via <see cref="RegisterHandler"/> and
    /// <see cref="RegisterCustomHandler"/> without modifying this source file.
    /// </summary>
    public static class StoryFlowNodeDispatcher
    {
        private static readonly Dictionary<StoryFlowNodeType, Action<StoryFlowComponent, StoryFlowNode>> Handlers;

        /// <summary>
        /// Handlers keyed by raw type string for node types not in the StoryFlowNodeType enum.
        /// The string key must match the node's type field in the JSON data (e.g. "myCustomNode").
        /// </summary>
        private static readonly Dictionary<string, Action<StoryFlowComponent, StoryFlowNode>> CustomHandlers
            = new Dictionary<string, Action<StoryFlowComponent, StoryFlowNode>>();

        /// <summary>
        /// Resets custom handlers on domain reload (handles Enter Play Mode settings with domain reload disabled).
        /// Built-in handlers from the static constructor are not affected since they are readonly.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            CustomHandlers.Clear();
        }

        static StoryFlowNodeDispatcher()
        {
            Handlers = new Dictionary<StoryFlowNodeType, Action<StoryFlowComponent, StoryFlowNode>>();

            // ================================================================
            // Control Flow
            // ================================================================
            Handlers[StoryFlowNodeType.Start] = ControlFlowNodeHandler.HandleStart;
            Handlers[StoryFlowNodeType.End] = ControlFlowNodeHandler.HandleEnd;
            Handlers[StoryFlowNodeType.RunScript] = ControlFlowNodeHandler.HandleRunScript;
            Handlers[StoryFlowNodeType.RunFlow] = ControlFlowNodeHandler.HandleRunFlow;
            Handlers[StoryFlowNodeType.EntryFlow] = ControlFlowNodeHandler.HandleEntryFlow;

            // ================================================================
            // Dialogue
            // ================================================================
            Handlers[StoryFlowNodeType.Dialogue] = DialogueNodeHandler.Handle;

            // ================================================================
            // Branch
            // ================================================================
            Handlers[StoryFlowNodeType.Branch] = BranchNodeHandler.Handle;

            // ================================================================
            // Boolean
            // ================================================================
            Handlers[StoryFlowNodeType.SetBool] = BooleanNodeHandler.HandleSet;
            Handlers[StoryFlowNodeType.GetBool] = NoOp;
            Handlers[StoryFlowNodeType.AndBool] = NoOp;
            Handlers[StoryFlowNodeType.OrBool] = NoOp;
            Handlers[StoryFlowNodeType.NotBool] = NoOp;
            Handlers[StoryFlowNodeType.EqualBool] = NoOp;

            // ================================================================
            // Integer
            // ================================================================
            Handlers[StoryFlowNodeType.SetInt] = IntegerNodeHandler.HandleSet;
            Handlers[StoryFlowNodeType.GetInt] = NoOp;
            Handlers[StoryFlowNodeType.RandomInt] = IntegerNodeHandler.HandleRandomInt;
            Handlers[StoryFlowNodeType.PlusInt] = NoOp;
            Handlers[StoryFlowNodeType.MinusInt] = NoOp;
            Handlers[StoryFlowNodeType.MultiplyInt] = NoOp;
            Handlers[StoryFlowNodeType.DivideInt] = NoOp;
            Handlers[StoryFlowNodeType.GreaterInt] = NoOp;
            Handlers[StoryFlowNodeType.GreaterOrEqualInt] = NoOp;
            Handlers[StoryFlowNodeType.LessInt] = NoOp;
            Handlers[StoryFlowNodeType.LessOrEqualInt] = NoOp;
            Handlers[StoryFlowNodeType.EqualInt] = NoOp;

            // ================================================================
            // Float
            // ================================================================
            Handlers[StoryFlowNodeType.SetFloat] = FloatNodeHandler.HandleSet;
            Handlers[StoryFlowNodeType.GetFloat] = NoOp;
            Handlers[StoryFlowNodeType.RandomFloat] = FloatNodeHandler.HandleRandomFloat;
            Handlers[StoryFlowNodeType.PlusFloat] = NoOp;
            Handlers[StoryFlowNodeType.MinusFloat] = NoOp;
            Handlers[StoryFlowNodeType.MultiplyFloat] = NoOp;
            Handlers[StoryFlowNodeType.DivideFloat] = NoOp;
            Handlers[StoryFlowNodeType.GreaterFloat] = NoOp;
            Handlers[StoryFlowNodeType.GreaterOrEqualFloat] = NoOp;
            Handlers[StoryFlowNodeType.LessFloat] = NoOp;
            Handlers[StoryFlowNodeType.LessOrEqualFloat] = NoOp;
            Handlers[StoryFlowNodeType.EqualFloat] = NoOp;

            // ================================================================
            // String
            // ================================================================
            Handlers[StoryFlowNodeType.SetString] = StringNodeHandler.HandleSet;
            Handlers[StoryFlowNodeType.GetString] = NoOp;
            Handlers[StoryFlowNodeType.ConcatenateString] = NoOp;
            Handlers[StoryFlowNodeType.EqualString] = NoOp;
            Handlers[StoryFlowNodeType.ContainsString] = NoOp;
            Handlers[StoryFlowNodeType.ToUpperCase] = NoOp;
            Handlers[StoryFlowNodeType.ToLowerCase] = NoOp;

            // ================================================================
            // Enum
            // ================================================================
            Handlers[StoryFlowNodeType.SetEnum] = EnumNodeHandler.HandleSet;
            Handlers[StoryFlowNodeType.GetEnum] = NoOp;
            Handlers[StoryFlowNodeType.EqualEnum] = NoOp;
            Handlers[StoryFlowNodeType.SwitchOnEnum] = EnumNodeHandler.HandleSwitchOnEnum;
            Handlers[StoryFlowNodeType.RandomBranch] = EnumNodeHandler.HandleRandomBranch;

            // ================================================================
            // Type Conversions (all lazy-evaluated, no-ops)
            // ================================================================
            Handlers[StoryFlowNodeType.IntToBoolean] = NoOp;
            Handlers[StoryFlowNodeType.FloatToBoolean] = NoOp;
            Handlers[StoryFlowNodeType.IntToString] = NoOp;
            Handlers[StoryFlowNodeType.FloatToString] = NoOp;
            Handlers[StoryFlowNodeType.StringToInt] = NoOp;
            Handlers[StoryFlowNodeType.StringToFloat] = NoOp;
            Handlers[StoryFlowNodeType.IntToFloat] = NoOp;
            Handlers[StoryFlowNodeType.FloatToInt] = NoOp;
            Handlers[StoryFlowNodeType.IntToEnum] = NoOp;
            Handlers[StoryFlowNodeType.BooleanToInt] = NoOp;
            Handlers[StoryFlowNodeType.BooleanToFloat] = NoOp;
            Handlers[StoryFlowNodeType.StringToEnum] = NoOp;
            Handlers[StoryFlowNodeType.EnumToString] = NoOp;
            Handlers[StoryFlowNodeType.LengthString] = NoOp;

            // ================================================================
            // Boolean Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetBoolArray] = NoOp;
            Handlers[StoryFlowNodeType.SetBoolArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Boolean);
            Handlers[StoryFlowNodeType.GetBoolArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetBoolArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Boolean);
            Handlers[StoryFlowNodeType.GetRandomBoolArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddBoolArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Boolean);
            Handlers[StoryFlowNodeType.RemoveBoolArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Boolean);
            Handlers[StoryFlowNodeType.ClearBoolArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Boolean);
            Handlers[StoryFlowNodeType.BoolArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.BoolArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInBoolArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachBoolLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Boolean);

            // ================================================================
            // Integer Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetIntArray] = NoOp;
            Handlers[StoryFlowNodeType.SetIntArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Integer);
            Handlers[StoryFlowNodeType.GetIntArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetIntArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Integer);
            Handlers[StoryFlowNodeType.GetRandomIntArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddIntArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Integer);
            Handlers[StoryFlowNodeType.RemoveIntArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Integer);
            Handlers[StoryFlowNodeType.ClearIntArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Integer);
            Handlers[StoryFlowNodeType.IntArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.IntArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInIntArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachIntLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Integer);

            // ================================================================
            // Float Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetFloatArray] = NoOp;
            Handlers[StoryFlowNodeType.SetFloatArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Float);
            Handlers[StoryFlowNodeType.GetFloatArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetFloatArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Float);
            Handlers[StoryFlowNodeType.GetRandomFloatArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddFloatArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Float);
            Handlers[StoryFlowNodeType.RemoveFloatArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Float);
            Handlers[StoryFlowNodeType.ClearFloatArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Float);
            Handlers[StoryFlowNodeType.FloatArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.FloatArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInFloatArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachFloatLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Float);

            // ================================================================
            // String Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetStringArray] = NoOp;
            Handlers[StoryFlowNodeType.SetStringArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.String);
            Handlers[StoryFlowNodeType.GetStringArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetStringArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.String);
            Handlers[StoryFlowNodeType.GetRandomStringArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddStringArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.String);
            Handlers[StoryFlowNodeType.RemoveStringArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.String);
            Handlers[StoryFlowNodeType.ClearStringArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.String);
            Handlers[StoryFlowNodeType.StringArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.StringArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInStringArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachStringLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.String);

            // ================================================================
            // Image Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetImageArray] = NoOp;
            Handlers[StoryFlowNodeType.SetImageArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Image);
            Handlers[StoryFlowNodeType.GetImageArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetImageArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Image);
            Handlers[StoryFlowNodeType.GetRandomImageArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddImageArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Image);
            Handlers[StoryFlowNodeType.RemoveImageArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Image);
            Handlers[StoryFlowNodeType.ClearImageArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Image);
            Handlers[StoryFlowNodeType.ImageArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.ImageArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInImageArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachImageLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Image);

            // ================================================================
            // Character Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetCharacterArray] = NoOp;
            Handlers[StoryFlowNodeType.SetCharacterArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Character);
            Handlers[StoryFlowNodeType.GetCharacterArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetCharacterArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Character);
            Handlers[StoryFlowNodeType.GetRandomCharacterArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddCharacterArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Character);
            Handlers[StoryFlowNodeType.RemoveCharacterArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Character);
            Handlers[StoryFlowNodeType.ClearCharacterArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Character);
            Handlers[StoryFlowNodeType.CharacterArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.CharacterArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInCharacterArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachCharacterLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Character);

            // ================================================================
            // Audio Arrays
            // ================================================================
            Handlers[StoryFlowNodeType.GetAudioArray] = NoOp;
            Handlers[StoryFlowNodeType.SetAudioArray] = (c, n) => ArrayNodeHandler.HandleSetArray(c, n, StoryFlowVariableType.Audio);
            Handlers[StoryFlowNodeType.GetAudioArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.SetAudioArrayElement] = (c, n) => ArrayNodeHandler.HandleSetArrayElement(c, n, StoryFlowVariableType.Audio);
            Handlers[StoryFlowNodeType.GetRandomAudioArrayElement] = NoOp;
            Handlers[StoryFlowNodeType.AddAudioArrayElement] = (c, n) => ArrayNodeHandler.HandleAddArrayElement(c, n, StoryFlowVariableType.Audio);
            Handlers[StoryFlowNodeType.RemoveAudioArrayElement] = (c, n) => ArrayNodeHandler.HandleRemoveArrayElement(c, n, StoryFlowVariableType.Audio);
            Handlers[StoryFlowNodeType.ClearAudioArray] = (c, n) => ArrayNodeHandler.HandleClearArray(c, n, StoryFlowVariableType.Audio);
            Handlers[StoryFlowNodeType.AudioArrayLength] = NoOp;
            Handlers[StoryFlowNodeType.AudioArrayContains] = NoOp;
            Handlers[StoryFlowNodeType.FindInAudioArray] = NoOp;
            Handlers[StoryFlowNodeType.ForEachAudioLoop] = (c, n) => ArrayNodeHandler.HandleForEachLoop(c, n, StoryFlowVariableType.Audio);

            // ================================================================
            // Media
            // ================================================================
            Handlers[StoryFlowNodeType.GetImage] = NoOp;
            Handlers[StoryFlowNodeType.SetImage] = MediaNodeHandler.HandleSetImage;
            Handlers[StoryFlowNodeType.SetBackgroundImage] = MediaNodeHandler.HandleSetBackgroundImage;
            Handlers[StoryFlowNodeType.GetAudio] = NoOp;
            Handlers[StoryFlowNodeType.SetAudio] = MediaNodeHandler.HandleSetAudio;
            Handlers[StoryFlowNodeType.PlayAudio] = MediaNodeHandler.HandlePlayAudio;
            Handlers[StoryFlowNodeType.GetCharacter] = NoOp;
            Handlers[StoryFlowNodeType.SetCharacter] = MediaNodeHandler.HandleSetCharacter;

            // ================================================================
            // Character Variables
            // ================================================================
            Handlers[StoryFlowNodeType.GetCharacterVar] = NoOp;
            Handlers[StoryFlowNodeType.SetCharacterVar] = CharacterVarNodeHandler.HandleSetCharacterVar;
        }

        /// <summary>
        /// Dispatches processing of a node to the appropriate handler.
        /// </summary>
        public static void ProcessNode(StoryFlowComponent component, StoryFlowNode node)
        {
            if (node == null)
            {
                Debug.LogWarning("[StoryFlow] Attempted to process a null node.");
                return;
            }

            var settings = StoryFlowSettings.Instance;
            if (settings != null && settings.VerboseLogging)
            {
                Debug.Log($"[StoryFlow] Processing node: {node.Type} (id: {node.Id})");
            }

            if (Handlers.TryGetValue(node.Type, out var handler))
            {
                handler(component, node);
            }
            else
            {
                // Fall back to custom string-keyed handlers.
                // Prefer the original JSON type string (RawType) when available,
                // otherwise use the enum name.
                var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                if (CustomHandlers.TryGetValue(typeName, out var customHandler))
                {
                    customHandler(component, node);
                }
                else
                {
                    Debug.LogWarning($"[StoryFlow] No handler registered for node type: {node.Type} (node {node.Id})");
                }
            }
        }

        // ================================================================
        // Public API: Register / Unregister custom handlers
        // ================================================================

        /// <summary>
        /// Registers a custom handler for a node type. Overwrites any existing handler.
        /// Use this to extend StoryFlow with custom node types or override built-in behavior.
        /// </summary>
        /// <param name="type">The node type to handle.</param>
        /// <param name="handler">The handler action receiving the component and node.</param>
        public static void RegisterHandler(StoryFlowNodeType type, Action<StoryFlowComponent, StoryFlowNode> handler)
        {
            if (handler == null)
            {
                Debug.LogWarning($"[StoryFlow] Cannot register null handler for {type}.");
                return;
            }
            Handlers[type] = handler;
        }

        /// <summary>
        /// Unregisters a handler for a node type. The node type will be treated as a no-op.
        /// </summary>
        /// <param name="type">The node type to unregister.</param>
        /// <returns>True if a handler was removed, false if no handler was registered.</returns>
        public static bool UnregisterHandler(StoryFlowNodeType type)
        {
            return Handlers.Remove(type);
        }

        /// <summary>
        /// Checks whether a handler is registered for the given node type.
        /// </summary>
        public static bool HasHandler(StoryFlowNodeType type)
        {
            return Handlers.ContainsKey(type);
        }

        /// <summary>
        /// Registers a handler for a custom node type identified by string name.
        /// Use this when working with node types not in the StoryFlowNodeType enum
        /// (e.g., custom nodes from editor plugins).
        /// The string name must match the node's type field in the JSON data.
        /// </summary>
        /// <param name="typeName">The string type name as it appears in the node data.</param>
        /// <param name="handler">The handler action.</param>
        public static void RegisterCustomHandler(string typeName, Action<StoryFlowComponent, StoryFlowNode> handler)
        {
            if (string.IsNullOrEmpty(typeName) || handler == null) return;
            CustomHandlers[typeName] = handler;
        }

        /// <summary>
        /// Unregisters a custom string-named handler.
        /// </summary>
        public static bool UnregisterCustomHandler(string typeName)
        {
            return CustomHandlers.Remove(typeName);
        }

        /// <summary>
        /// No-op handler for nodes that are evaluated lazily by the StoryFlowEvaluator.
        /// These include all Get* nodes, logic gates, comparisons, conversions,
        /// array contains/length/find, and string operations.
        /// </summary>
        private static void NoOp(StoryFlowComponent component, StoryFlowNode node)
        {
            // Intentionally empty. These nodes are evaluated on demand by StoryFlowEvaluator
            // when another node reads their output, not when they are "executed" in the flow.
        }
    }
}
