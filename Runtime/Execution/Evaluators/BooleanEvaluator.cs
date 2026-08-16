using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates boolean values from expression node chains.
    /// Handles boolean logic nodes, comparisons, conversions, and array-contains checks.
    /// </summary>
    internal static class BooleanEvaluator
    {
        /// <summary>
        /// Evaluates the boolean value arriving at a specific input handle of a node.
        /// Follows the input edge backwards to find the source node and evaluates it.
        /// </summary>
        internal static bool Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return false;

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return false;

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return false;

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            bool result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as a boolean value based on its type.
        /// </summary>
        internal static bool EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return false;

            // Recursion guard
            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Boolean evaluation depth exceeded. Possible circular reference.");
                return false;
            }

            try
            {
                // ForEach nodes — skip evaluation cache to avoid cross-type conflicts.
                // Map reads (getMapValue/hasMapKey/mapSize) are never memoized either: maps
                // resolve to LIVE variable storage and in-place mutations must be observable
                // on the next read (see EvaluatorHelpers.IsMapReadNode). forEachMap key/value
                // reads come from the iteration snapshot, not the live map, but forEachMap
                // is already cache-exempt via IsForEachNode.
                bool skipCache = EvaluatorHelpers.IsForEachNode(node.Type) ||
                                 EvaluatorHelpers.IsMapReadNode(node.Type) ||
                                 EvaluatorHelpers.IsMultiOutputNode(node.Type);
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!skipCache && state.CachedOutput != null)
                    return state.CachedOutput.GetBool();

                bool result = EvaluateFromNodeInternal(ctx, node);

                if (!skipCache)
                    state.CachedOutput = StoryFlowVariant.Bool(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result.ToString().ToLower()}");
                }

                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static bool EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            // Forward-compat: warn once per dialogue run when a script wires an
            // unrecognized node type into a boolean input. Returning the silent default
            // here can flip branch direction; the log gives the author a signal.
            if (ctx.MaybeWarnUnknownNode(node))
                return false;

            switch (node.Type)
            {
                case StoryFlowNodeType.GetBool:
                case StoryFlowNodeType.SetBool:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    bool val = variable?.Value?.GetBool() ?? false;
                    if (ctx.TraceEnabled && variable != null)
                    {
                        bool isGlobal = !ctx.LocalVariables.ContainsKey(variable.Id);
                        Debug.Log($"[SF-TRACE] VAR GET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={val.ToString().ToLower()}");
                    }
                    return val;
                }

                case StoryFlowNodeType.AndBool:
                {
                    bool input1 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean1);
                    bool input2 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean2);
                    // Fall back to node data if no edge connected
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean1) == null)
                        input1 = node.GetDataBool("value1");
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean2) == null)
                        input2 = node.GetDataBool("value2");
                    return input1 && input2;
                }

                case StoryFlowNodeType.OrBool:
                {
                    bool input1 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean1);
                    bool input2 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean2);
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean1) == null)
                        input1 = node.GetDataBool("value1");
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean2) == null)
                        input2 = node.GetDataBool("value2");
                    return input1 || input2;
                }

                case StoryFlowNodeType.NotBool:
                {
                    bool input = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
                    return !input;
                }

                case StoryFlowNodeType.EqualBool:
                {
                    bool input1 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean1);
                    bool input2 = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean2);
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean1) == null)
                        input1 = node.GetDataBool("value1");
                    if (ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean2) == null)
                        input2 = node.GetDataBool("value2");
                    return input1 == input2;
                }

                case StoryFlowNodeType.Branch:
                {
                    // Evaluate the condition input of the branch node
                    return Evaluate(ctx, node.Id, "boolean-condition");
                }

                case StoryFlowNodeType.IntToBoolean:
                {
                    int intValue = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    return intValue != 0;
                }

                case StoryFlowNodeType.FloatToBoolean:
                {
                    float floatValue = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    return floatValue != 0f;
                }

                // Integer comparison nodes produce boolean
                case StoryFlowNodeType.GreaterInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a > b;
                }
                case StoryFlowNodeType.GreaterOrEqualInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a >= b;
                }
                case StoryFlowNodeType.LessInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a < b;
                }
                case StoryFlowNodeType.LessOrEqualInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a <= b;
                }
                case StoryFlowNodeType.EqualInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a == b;
                }

                // Float comparison nodes produce boolean
                case StoryFlowNodeType.GreaterFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a > b;
                }
                case StoryFlowNodeType.GreaterOrEqualFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a >= b;
                }
                case StoryFlowNodeType.LessFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a < b;
                }
                case StoryFlowNodeType.LessOrEqualFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a <= b;
                }
                case StoryFlowNodeType.EqualFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    // Use approximate comparison for floats
                    return Mathf.Approximately(a, b);
                }

                // String comparison nodes produce boolean
                case StoryFlowNodeType.EqualString:
                {
                    string a = EvaluatorHelpers.EvaluateStringInput1(ctx, node);
                    string b = EvaluatorHelpers.EvaluateStringInput2(ctx, node);
                    return a == b;
                }
                case StoryFlowNodeType.ContainsString:
                {
                    string a = EvaluatorHelpers.EvaluateStringInput1(ctx, node);
                    string b = EvaluatorHelpers.EvaluateStringInput2(ctx, node);
                    return a != null && b != null && a.Contains(b);
                }

                // Enum comparison
                case StoryFlowNodeType.EqualEnum:
                {
                    string a = EvaluatorHelpers.EvaluateEnumInput1(ctx, node);
                    string b = EvaluatorHelpers.EvaluateEnumInput2(ctx, node);
                    return a == b;
                }

                // Array contains nodes produce boolean
                case StoryFlowNodeType.BoolArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    bool val = StoryFlowEvaluator.EvaluateBooleanWithDefault(ctx, node.Id, StoryFlowHandles.In_Boolean, node.GetDataBool("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetBool() == val) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.IntArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    int val = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetInt() == val) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.FloatArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    float val = StoryFlowEvaluator.EvaluateFloatWithDefault(ctx, node.Id, StoryFlowHandles.In_Float, node.GetDataFloat("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (Mathf.Approximately(item.GetFloat(), val)) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.StringArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_String, node.GetData("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetString() == val) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.ImageArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_ImageArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Image, node.GetData("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetString() == val) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.CharacterArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_CharacterArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Character, node.GetData("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetString() == val) return true;
                    }
                    return false;
                }
                case StoryFlowNodeType.AudioArrayContains:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_AudioArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Audio, node.GetData("value"));
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetString() == val) return true;
                    }
                    return false;
                }

                // GetBoolArrayElement returns boolean. The export dialect stores the inline
                // index in the "value" field (see the IntegerEvaluator's GetIntArrayElement note).
                case StoryFlowNodeType.GetBoolArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("value"));
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetBool();
                    return false;
                }

                case StoryFlowNodeType.GetRandomBoolArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    if (arr == null || arr.Count == 0) return false;
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetBool();
                }

                case StoryFlowNodeType.ForEachBoolLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetBool();
                    }
                    return false;
                }

                // Map op arms branch on the node's OWN keyType/valueType data strings — a
                // NEW pattern for these evaluators: catalog map ops carry K/V in node data,
                // unlike array ops which encode the element type in the node type itself.
                case StoryFlowNodeType.GetMapValue:
                {
                    // getMapValue exposes two outputs sharing one node:
                    //   "source-{id}-{valueType}-value" and "source-{id}-boolean-isValid".
                    // Discriminate by SourceHandle suffix (precedent: runScript "-out-" parsing).
                    bool found = MapEvaluator.ComputeGetMapValue(ctx, node, out var mapValue);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    if (sourceHandle.EndsWith("-isValid"))
                        return found; // IsValid is always boolean, regardless of valueType
                    if (node.GetData("valueType") == "boolean")
                        return mapValue?.GetBool() ?? false;
                    return false;
                }

                case StoryFlowNodeType.HasMapKey:
                {
                    // Key FIRST, then the map — the Unreal port's pointer-lifetime input
                    // order (HTML resolves map-first; observably equivalent)
                    var key = MapEvaluator.EvaluateMapOpKeyInput(ctx, node, "2");
                    var map = MapEvaluator.EvaluateMapInput(ctx, node, "1");
                    return map != null && MapEvaluator.FindMapEntryByKey(map, node.GetData("keyType"), key) >= 0;
                }

                // forEachMap Key/Value — two outputs share one node:
                //   "source-{id}-{keyType}-key" and "source-{id}-{valueType}-value".
                // Reads come from the iteration SNAPSHOT (LoopKey/LoopValue), which survives
                // the per-iteration cache clear. keyType can't be "boolean" per spec — the
                // key branch is unreachable, included for symmetry with the other evaluators.
                case StoryFlowNodeType.ForEachMap:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    if (sourceHandle.EndsWith("-key") && node.GetData("keyType") == "boolean")
                        return runtimeState.LoopKey?.GetBool() ?? false;
                    if (sourceHandle.EndsWith("-value") && node.GetData("valueType") == "boolean")
                        return runtimeState.LoopValue?.GetBool() ?? false;
                    return false;
                }

                // GetCharacterVar / SetCharacterVar returning boolean
                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetBool() ?? false;
                }

                // Dialogue node — read from input option values
                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetBool() ?? false;
                        }
                    }
                    return false;
                }

                // RunScript output
                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetBool() ?? false;
                }

                default:
                    return false;
            }
        }

        // =====================================================================
        // Boolean Chain Pre-Processing
        // =====================================================================

        /// <summary>
        /// Pre-caches boolean evaluation results for all nodes feeding into a branch.
        /// Walks the expression graph from comparison/logic nodes to ensure their
        /// outputValue fields are populated before the branch reads them.
        /// </summary>
        internal static void ProcessBooleanChain(StoryFlowExecutionContext ctx, string nodeId)
        {
            if (ctx?.CurrentScript == null) return;

            var node = ctx.CurrentScript.GetNode(nodeId);
            if (node == null) return;

            ProcessBooleanChainInternal(ctx, node);
        }

        private static void ProcessBooleanChainInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null) return;

            switch (node.Type)
            {
                case StoryFlowNodeType.NotBool:
                {
                    // Clear cache so we get fresh evaluation
                    ctx.GetNodeRuntimeState(node.Id).ClearCache();
                    // Walk input chain first
                    var inputEdge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean);
                    if (inputEdge != null)
                    {
                        var sourceNode = ctx.CurrentScript.GetNode(inputEdge.Source);
                        if (sourceNode != null) ProcessBooleanChainInternal(ctx, sourceNode);
                    }
                    // Evaluate and cache
                    EvaluateFromNode(ctx, node);
                    break;
                }

                case StoryFlowNodeType.Branch:
                {
                    var condEdge = ctx.CurrentScript.FindInputEdge(node.Id, "boolean-condition");
                    if (condEdge != null)
                    {
                        var sourceNode = ctx.CurrentScript.GetNode(condEdge.Source);
                        if (sourceNode != null) ProcessBooleanChainInternal(ctx, sourceNode);
                    }
                    break;
                }

                case StoryFlowNodeType.AndBool:
                case StoryFlowNodeType.OrBool:
                case StoryFlowNodeType.EqualBool:
                {
                    var edge1 = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean1);
                    if (edge1 != null)
                    {
                        var src1 = ctx.CurrentScript.GetNode(edge1.Source);
                        if (src1 != null) ProcessBooleanChainInternal(ctx, src1);
                    }
                    var edge2 = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Boolean2);
                    if (edge2 != null)
                    {
                        var src2 = ctx.CurrentScript.GetNode(edge2.Source);
                        if (src2 != null) ProcessBooleanChainInternal(ctx, src2);
                    }
                    // Evaluate to populate cache
                    ctx.GetNodeRuntimeState(node.Id).ClearCache();
                    EvaluateFromNode(ctx, node);
                    break;
                }

                // Comparison nodes produce boolean — just evaluate them to populate cache
                case StoryFlowNodeType.GreaterInt:
                case StoryFlowNodeType.GreaterOrEqualInt:
                case StoryFlowNodeType.LessInt:
                case StoryFlowNodeType.LessOrEqualInt:
                case StoryFlowNodeType.EqualInt:
                case StoryFlowNodeType.GreaterFloat:
                case StoryFlowNodeType.GreaterOrEqualFloat:
                case StoryFlowNodeType.LessFloat:
                case StoryFlowNodeType.LessOrEqualFloat:
                case StoryFlowNodeType.EqualFloat:
                case StoryFlowNodeType.EqualString:
                case StoryFlowNodeType.ContainsString:
                case StoryFlowNodeType.EqualEnum:
                case StoryFlowNodeType.IntToBoolean:
                case StoryFlowNodeType.FloatToBoolean:
                case StoryFlowNodeType.BoolArrayContains:
                case StoryFlowNodeType.IntArrayContains:
                case StoryFlowNodeType.FloatArrayContains:
                case StoryFlowNodeType.StringArrayContains:
                {
                    ctx.GetNodeRuntimeState(node.Id).ClearCache();
                    EvaluateFromNode(ctx, node);
                    break;
                }

                default:
                    // For variable getter nodes etc., just evaluate to cache
                    ctx.GetNodeRuntimeState(node.Id).ClearCache();
                    EvaluateFromNode(ctx, node);
                    break;
            }
        }

        // =====================================================================
        // Option Visibility Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the visibility of a dialogue option. Returns true if visible.
        /// Looks for an input edge with suffix "boolean-{optionId}".
        /// If no visibility edge exists, the option is visible by default.
        /// </summary>
        internal static bool EvaluateOptionVisibility(StoryFlowExecutionContext ctx, string nodeId, string optionId)
        {
            if (ctx?.CurrentScript == null) return true;

            string visibilitySuffix = $"boolean-{optionId}";
            var edge = ctx.CurrentScript.FindInputEdge(nodeId, visibilitySuffix);
            if (edge == null) return true; // No visibility edge = always visible

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return true;

            // The condition edge's source handle must reach the evaluator — multi-output
            // nodes (runScript "-out-{id}", forEachMap "-value", getMapValue "-isValid")
            // discriminate WHICH output is read by parsing it. Without this, the stale flow
            // handle from ProcessNextPath sent runScript reads to the first-output fallback
            // and map reads to the wrong arm. Save/restore mirrors Evaluate() above.
            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;

            // Pre-cache boolean chain before evaluating (matches Godot/Unreal behavior)
            ProcessBooleanChain(ctx, sourceNode.Id);
            bool result = EvaluateFromNode(ctx, sourceNode);

            ctx.LastSourceHandle = prevHandle;
            return result;
        }
    }
}
