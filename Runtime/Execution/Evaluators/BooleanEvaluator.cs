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
                // Check cache
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (state.CachedOutput != null)
                    return state.CachedOutput.GetBool();

                bool result = EvaluateFromNodeInternal(ctx, node);

                // Cache result
                state.CachedOutput = StoryFlowVariant.Bool(result);
                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static bool EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            switch (node.Type)
            {
                case StoryFlowNodeType.GetBool:
                case StoryFlowNodeType.SetBool:
                {
                    var variableId = node.GetData("variableId");
                    var variable = ctx.FindVariable(variableId);
                    return variable?.Value?.GetBool() ?? false;
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
                    bool val = Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
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
                    int val = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
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
                    float val = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
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
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
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
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Image);
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
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Character);
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
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Audio);
                    if (arr == null) return false;
                    foreach (var item in arr)
                    {
                        if (item.GetString() == val) return true;
                    }
                    return false;
                }

                // GetBoolArrayElement returns boolean
                case StoryFlowNodeType.GetBoolArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    int idx = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
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

            return EvaluateFromNode(ctx, sourceNode);
        }
    }
}
