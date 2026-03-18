using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates integer values from expression node chains.
    /// Handles arithmetic, conversions, array operations, and variable lookups.
    /// </summary>
    internal static class IntegerEvaluator
    {
        /// <summary>
        /// Evaluates the integer value arriving at a specific input handle of a node.
        /// </summary>
        internal static int Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return 0;

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return 0;

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return 0;

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            int result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as an integer value based on its type.
        /// </summary>
        internal static int EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return 0;

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Integer evaluation depth exceeded. Possible circular reference.");
                return 0;
            }

            try
            {
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (state.CachedOutput != null)
                    return state.CachedOutput.GetInt();

                int result = EvaluateFromNodeInternal(ctx, node);
                state.CachedOutput = StoryFlowVariant.Int(result);
                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static int EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            switch (node.Type)
            {
                case StoryFlowNodeType.GetInt:
                case StoryFlowNodeType.SetInt:
                {
                    var variableId = node.GetData("variableId");
                    var variable = ctx.FindVariable(variableId);
                    return variable?.Value?.GetInt() ?? 0;
                }

                case StoryFlowNodeType.PlusInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a + b;
                }

                case StoryFlowNodeType.MinusInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a - b;
                }

                case StoryFlowNodeType.MultiplyInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a * b;
                }

                case StoryFlowNodeType.DivideInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    if (b == 0)
                    {
                        Debug.LogWarning("[StoryFlow] Integer division by zero in node " + node.Id);
                        return 0;
                    }
                    return a / b;
                }

                case StoryFlowNodeType.RandomInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    int min = Mathf.Min(a, b);
                    int max = Mathf.Max(a, b);
                    return Random.Range(min, max + 1);
                }

                case StoryFlowNodeType.StringToInt:
                {
                    string str = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return int.TryParse(str, out int val) ? val : 0;
                }

                case StoryFlowNodeType.FloatToInt:
                {
                    float f = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    return Mathf.FloorToInt(f);
                }

                // Array length nodes
                case StoryFlowNodeType.BoolArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.IntArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.FloatArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.StringArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.ImageArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_ImageArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.CharacterArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_CharacterArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.AudioArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_AudioArray);
                    return arr?.Count ?? 0;
                }

                // FindInArray nodes return index
                case StoryFlowNodeType.FindInBoolArray:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    bool val = BooleanEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetBool() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInIntArray:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    int val = Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetInt() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInFloatArray:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    float val = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (Mathf.Approximately(arr[i].GetFloat(), val)) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInStringArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInImageArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_ImageArray);
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Image);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInCharacterArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_CharacterArray);
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Character);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInAudioArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_AudioArray);
                    string val = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Audio);
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }

                // GetIntArrayElement
                case StoryFlowNodeType.GetIntArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    int idx = Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetInt();
                    return 0;
                }

                case StoryFlowNodeType.GetRandomIntArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    if (arr == null || arr.Count == 0) return 0;
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetInt();
                }

                // ForEach loop — returns current index or element depending on context
                case StoryFlowNodeType.ForEachBoolLoop:
                case StoryFlowNodeType.ForEachIntLoop:
                case StoryFlowNodeType.ForEachFloatLoop:
                case StoryFlowNodeType.ForEachStringLoop:
                case StoryFlowNodeType.ForEachImageLoop:
                case StoryFlowNodeType.ForEachCharacterLoop:
                case StoryFlowNodeType.ForEachAudioLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    return runtimeState.LoopIndex;
                }

                // GetCharacterVar / SetCharacterVar returning integer
                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetInt() ?? 0;
                }

                // Dialogue input option value
                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetInt() ?? 0;
                        }
                    }
                    return 0;
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetInt() ?? 0;
                }

                case StoryFlowNodeType.BooleanToInt:
                {
                    bool boolVal = BooleanEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
                    return boolVal ? 1 : 0;
                }

                case StoryFlowNodeType.LengthString:
                {
                    string str = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.Length ?? 0;
                }

                default:
                    return 0;
            }
        }
    }
}
