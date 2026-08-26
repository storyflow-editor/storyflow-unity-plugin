using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Shared helper methods used by multiple domain evaluators.
    /// Includes dual-input evaluation with fallback to node data,
    /// RunScript output resolution, and utility lookups.
    /// </summary>
    internal static class EvaluatorHelpers
    {
        // =====================================================================
        // ForEach node detection
        // =====================================================================

        /// <summary>
        /// Returns true if the node type is a forEach loop variant.
        /// ForEach nodes should not participate in general evaluation caching
        /// because cross-type cache writes (e.g., int index vs string element) conflict.
        /// </summary>
        internal static bool IsForEachNode(StoryFlowNodeType type)
        {
            switch (type)
            {
                case StoryFlowNodeType.ForEachBoolLoop:
                case StoryFlowNodeType.ForEachIntLoop:
                case StoryFlowNodeType.ForEachFloatLoop:
                case StoryFlowNodeType.ForEachStringLoop:
                case StoryFlowNodeType.ForEachImageLoop:
                case StoryFlowNodeType.ForEachCharacterLoop:
                case StoryFlowNodeType.ForEachAudioLoop:
                case StoryFlowNodeType.ForEachMap:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true if the node type is a pure map read (getMapValue/hasMapKey/mapSize).
        /// Map reads are never memoized: maps resolve to LIVE variable storage and in-place
        /// mutations (setMapValue/removeMapKey/clearMap) must be observable on the next read.
        /// The HTML runtime recomputes map reads inline the same way. mapKeys/mapValues flow
        /// through ArrayEvaluator, which has no cache, so they need no entry here.
        /// </summary>
        internal static bool IsMapReadNode(StoryFlowNodeType type)
        {
            switch (type)
            {
                case StoryFlowNodeType.GetMapValue:
                case StoryFlowNodeType.HasMapKey:
                case StoryFlowNodeType.MapSize:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true for nodes exposing MULTIPLE named outputs through one runtime state
        /// (runScript's script outputs). The single-slot CachedOutput cannot represent them:
        /// a boolean read of one output would poison a later different-type read of another
        /// output of the same node with a type-mismatched default. Resolution is a dictionary
        /// lookup on OutputValues, so exemption is cheap — the same trade IsMapReadNode makes.
        /// </summary>
        internal static bool IsMultiOutputNode(StoryFlowNodeType type)
        {
            return type == StoryFlowNodeType.RunScript;
        }

        /// <summary>
        /// Returns true for the two .sfd accessor node types (the Set's pass-through output
        /// answers what its Get twin would, so both read).
        ///
        /// Data-asset reads are never memoized: they resolve through the session OVERLAY,
        /// which a Set node, the public API or a save load can move between two reads of the
        /// same accessor — the same liveness argument <see cref="IsMapReadNode"/> makes for
        /// map storage. Cheap to recompute (a short chain walk), and the alternative is an
        /// option condition that keeps answering with a value the game has already changed.
        /// </summary>
        internal static bool IsDataAssetAccessor(StoryFlowNodeType type)
        {
            return type == StoryFlowNodeType.GetDataAssetVariable ||
                   type == StoryFlowNodeType.SetDataAssetVariable;
        }

        /// <summary>
        /// THE cache-exemption list every typed evaluator asks — one home, so a new exempt
        /// kind cannot land in four of the five and memoize in the fifth (which is invisible
        /// until some graph reads that one type through the stale node).
        ///
        /// Four reasons to skip the per-node CachedOutput memo:
        ///  - ForEach nodes: one runtime state, cross-type outputs (an int index and a string
        ///    element) that would poison each other through the single cache slot.
        ///  - Map reads (getMapValue/hasMapKey/mapSize): maps resolve to LIVE variable
        ///    storage, so an in-place setMapValue/removeMapKey/clearMap must be observable on
        ///    the next read. The HTML runtime recomputes them inline the same way. forEachMap
        ///    key/value reads come from the iteration snapshot rather than the live map, but
        ///    forEachMap is already exempt above.
        ///  - Multi-output nodes (runScript): several named outputs behind one cache slot.
        ///  - Data-asset accessors: they read the session OVERLAY, which a Set node, the
        ///    public API or a save load can move between two reads of the same accessor —
        ///    the same liveness argument the map reads make.
        /// </summary>
        internal static bool ShouldSkipCache(StoryFlowNodeType type)
        {
            return IsForEachNode(type) ||
                   IsMapReadNode(type) ||
                   IsMultiOutputNode(type) ||
                   IsDataAssetAccessor(type);
        }

        // =====================================================================
        // Dual-input evaluation with fallback to node data
        // =====================================================================
        // Each helper sets ctx.LastSourceHandle to the resolved edge's source handle
        // (save/restore, mirroring the typed Evaluate entry points) before delegating
        // to the FromNode evaluators. Multi-output source nodes (forEachMap
        // "-key"/"-value", getMapValue "-isValid", runScript "-out-") discriminate
        // their outputs by that handle and would otherwise read a stale flow handle.

        internal static int EvaluateIntegerInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Integer1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    int result = IntegerEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetDataInt("value1");
        }

        internal static int EvaluateIntegerInput2(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Integer2);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    int result = IntegerEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetDataInt("value2");
        }

        internal static float EvaluateFloatInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Float1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    float result = FloatEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetDataFloat("value1");
        }

        internal static float EvaluateFloatInput2(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Float2);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    float result = FloatEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetDataFloat("value2");
        }

        internal static string EvaluateStringInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_String1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    string result = StringEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return ctx.ResolveStringKey(node.GetData("value1"));
        }

        internal static string EvaluateStringInput2(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_String2);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    string result = StringEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return ctx.ResolveStringKey(node.GetData("value2"));
        }

        internal static string EvaluateEnumInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Enum1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    string result = EnumEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetData("value1");
        }

        internal static string EvaluateEnumInput2(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Enum2);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                {
                    var prevHandle = ctx.LastSourceHandle;
                    ctx.LastSourceHandle = edge.SourceHandle;
                    string result = EnumEvaluator.EvaluateFromNode(ctx, sourceNode);
                    ctx.LastSourceHandle = prevHandle;
                    return result;
                }
            }
            return node.GetData("value2");
        }

        // =====================================================================
        // Utility Helpers
        // =====================================================================

        /// <summary>
        /// Gets the enum values list from a node, either from its own variable or
        /// by looking at downstream connected enum nodes.
        /// </summary>
        internal static List<string> GetEnumValuesFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (ctx?.CurrentScript == null) return null;

            // Check downstream: look for an outgoing edge from the enum output
            var edges = ctx.CurrentScript.GetEdgesFromSource(node.Id);
            foreach (var edge in edges)
            {
                if (edge.SourceHandle != null && edge.SourceHandle.Contains("-enum-"))
                {
                    var targetNode = ctx.CurrentScript.GetNode(edge.Target);
                    if (targetNode != null)
                    {
                        // If target is a GetEnum/SetEnum, get enum values from the variable
                        if (targetNode.Type == StoryFlowNodeType.GetEnum ||
                            targetNode.Type == StoryFlowNodeType.SetEnum)
                        {
                            var varId = targetNode.GetData("variable");
                            var variable = ctx.FindVariable(varId);
                            if (variable?.EnumValues != null && variable.EnumValues.Count > 0)
                                return variable.EnumValues;
                        }

                        // Check node data for enumValues
                        var enumValuesStr = targetNode.GetData("enumValues");
                        if (!string.IsNullOrEmpty(enumValuesStr))
                        {
                            var values = new List<string>(enumValuesStr.Split(','));
                            if (values.Count > 0) return values;
                        }
                    }
                }
            }

            // Check node data directly
            var nodeEnumStr = node.GetData("enumValues");
            if (!string.IsNullOrEmpty(nodeEnumStr))
            {
                return new List<string>(nodeEnumStr.Split(','));
            }

            return null;
        }

        /// <summary>
        /// Gets the appropriate array input handle suffix for array-element getter nodes.
        /// </summary>
        internal static string GetArrayHandleSuffix(StoryFlowNodeType nodeType)
        {
            switch (nodeType)
            {
                case StoryFlowNodeType.GetImageArrayElement:
                case StoryFlowNodeType.GetRandomImageArrayElement:
                    return StoryFlowHandles.In_ImageArray;
                case StoryFlowNodeType.GetAudioArrayElement:
                case StoryFlowNodeType.GetRandomAudioArrayElement:
                    return StoryFlowHandles.In_AudioArray;
                case StoryFlowNodeType.GetCharacterArrayElement:
                case StoryFlowNodeType.GetRandomCharacterArrayElement:
                    return StoryFlowHandles.In_CharacterArray;
                default:
                    return StoryFlowHandles.In_StringArray;
            }
        }

        // =====================================================================
        // RunScript Output Resolution
        // =====================================================================

        /// <summary>
        /// Resolves a RunScript node's output value by parsing the source handle to extract the
        /// output variable ID, mapping it to a variable name via scriptInterface data, and looking
        /// up that name in the node's OutputValues dictionary (which is keyed by variable Name).
        /// </summary>
        internal static StoryFlowVariant ResolveRunScriptOutput(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return ResolveRunScriptOutputByHandle(ctx, node, ctx.LastSourceHandle);
        }

        /// <summary>
        /// <see cref="ResolveRunScriptOutput"/> with an EXPLICIT source handle instead of
        /// ctx.LastSourceHandle. Needed by the map resolver's runScript arm, which walks
        /// edges itself and holds the terminal edge (LastSourceHandle may be stale there).
        /// </summary>
        internal static StoryFlowVariant ResolveRunScriptOutputByHandle(
            StoryFlowExecutionContext ctx, StoryFlowNode node, string sourceHandle)
        {
            var runtimeState = ctx.GetNodeRuntimeState(node.Id);
            if (runtimeState.OutputValues == null || runtimeState.OutputValues.Count == 0)
                return null;

            if (string.IsNullOrEmpty(sourceHandle))
            {
                // Fallback: return first output value
                foreach (var kvp in runtimeState.OutputValues)
                    return kvp.Value;
                return null;
            }

            // Source handle format: "source-{nodeId}-{type}-out-{varId}"
            int outIdx = sourceHandle.IndexOf("-out-");
            if (outIdx < 0)
            {
                // Fallback: return first output value
                foreach (var kvp in runtimeState.OutputValues)
                    return kvp.Value;
                return null;
            }

            string varId = sourceHandle.Substring(outIdx + 5);

            // Map the variable ID to its name via scriptInterface outputs
            string varName = null;
            var scriptInterfaceJson = node.GetData("scriptInterface");
            if (!string.IsNullOrEmpty(scriptInterfaceJson))
            {
                try
                {
                    var si = Newtonsoft.Json.Linq.JObject.Parse(scriptInterfaceJson);
                    var outputs = si["outputs"] as Newtonsoft.Json.Linq.JArray;
                    if (outputs != null)
                    {
                        foreach (var output in outputs)
                        {
                            var outputId = output.Value<string>("id") ?? "";
                            if (outputId == varId)
                            {
                                varName = output.Value<string>("name") ?? "";
                                break;
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[StoryFlow] Failed to parse scriptInterface for RunScript output: {e.Message}");
                }
            }

            if (!string.IsNullOrEmpty(varName) && runtimeState.OutputValues.TryGetValue(varName, out var value))
            {
                return value;
            }

            // Fallback: try looking up by ID directly (in case OutputValues was keyed by ID)
            if (runtimeState.OutputValues.TryGetValue(varId, out var fallbackValue))
            {
                return fallbackValue;
            }

            return null;
        }

        // =====================================================================
        // Character Variable Evaluation
        // =====================================================================

        /// <summary>
        /// Resolves the character reference for a GetCharacterVar/SetCharacterVar node.
        /// A connected character input edge is evaluated FIRST and overrides both authored
        /// fields, so a wired-over dangling id never warns; the wired value may itself be
        /// an id — FindCharacter runs every input through the context's one resolution
        /// point, so the same function handles either shape. Unwired, the node's id field
        /// resolves first with the authored path as the fall-back (contract §4).
        /// </summary>
        internal static string ResolveCharacterPath(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var charEdge = ctx.CurrentScript?.FindInputEdge(node.Id, StoryFlowHandles.In_CharacterInput);
            if (charEdge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(charEdge.Source);
                if (sourceNode != null)
                {
                    string evaluated = StringEvaluator.EvaluateFromNode(ctx, sourceNode);
                    if (!string.IsNullOrEmpty(evaluated))
                        return evaluated;
                }
            }

            return ctx.ResolveCharacterRef(node.GetData("characterId"), node.GetData("characterPath"));
        }

        /// <summary>
        /// Evaluates a character variable value, handling built-in "Name" and "Image"
        /// fields as well as custom variables. Returns null if not found.
        /// </summary>
        internal static StoryFlowVariant EvaluateCharacterVariable(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            string charPath = ResolveCharacterPath(ctx, node);
            string varName = node.GetData("variableName");

            if (string.IsNullOrEmpty(charPath) || string.IsNullOrEmpty(varName))
                return null;

            var characterData = ctx.FindCharacter(charPath);
            if (characterData == null)
                return null;

            // Handle built-in "Name" field (or its reserved cf_name id — amendment A1/A2(a))
            if (StoryFlowCharacterTokens.IsCharacterNameBuiltin(varName))
            {
                var v = new StoryFlowVariant();
                v.SetString(characterData.Name ?? "");
                return v;
            }

            // Handle built-in "Image" field (or cf_image — amendment A1/A2(a))
            if (StoryFlowCharacterTokens.IsCharacterImageBuiltin(varName))
            {
                var v = new StoryFlowVariant();
                v.SetString(characterData.ImageAssetKey ?? "");
                return v;
            }

            // Custom variable
            if (characterData.Variables != null &&
                characterData.Variables.TryGetValue(varName, out var charVar))
            {
                return charVar;
            }

            return null;
        }

        /// <summary>
        /// Evaluates the .sfd variable an accessor node is bound to, or null for every
        /// degraded case (contract §6, the ladder on the context) — each typed evaluator then
        /// substitutes ITS OWN type default, which is how the character-variable arms degrade
        /// on a missing character too, and what the degraded fixture pins.
        ///
        /// The value is already a deep copy: the store copies out by contract (§3), so graph
        /// code cannot mutate the seed or the overlay through a read.
        /// </summary>
        internal static StoryFlowVariant EvaluateDataAssetVariable(
            StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (ctx == null || node == null) return null;
            return ctx.TryReadDataAssetBinding(node, out _, out var value) ? value : null;
        }
    }
}
