using System.Globalization;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates string values from expression node chains.
    /// Handles concatenation, case conversion, type conversions, and variable lookups.
    /// </summary>
    internal static class StringEvaluator
    {
        /// <summary>
        /// Evaluates the string value arriving at a specific input handle of a node.
        /// </summary>
        internal static string Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return "";

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return "";

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return "";

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            string result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as a string value based on its type.
        /// </summary>
        internal static string EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return "";

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] String evaluation depth exceeded. Possible circular reference.");
                return "";
            }

            try
            {
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (state.CachedOutput != null)
                    return state.CachedOutput.GetString();

                string result = EvaluateFromNodeInternal(ctx, node);
                state.CachedOutput = StoryFlowVariant.String(result);
                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static string EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            switch (node.Type)
            {
                case StoryFlowNodeType.GetString:
                case StoryFlowNodeType.SetString:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    return variable?.Value?.GetString() ?? "";
                }

                case StoryFlowNodeType.ConcatenateString:
                {
                    string a = EvaluatorHelpers.EvaluateStringInput1(ctx, node);
                    string b = EvaluatorHelpers.EvaluateStringInput2(ctx, node);
                    return a + b;
                }

                case StoryFlowNodeType.ToUpperCase:
                {
                    string str = Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.ToUpper() ?? "";
                }

                case StoryFlowNodeType.ToLowerCase:
                {
                    string str = Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.ToLower() ?? "";
                }

                case StoryFlowNodeType.IntToString:
                {
                    int intValue = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    return intValue.ToString();
                }

                case StoryFlowNodeType.FloatToString:
                {
                    float floatValue = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    return floatValue.ToString(CultureInfo.InvariantCulture);
                }

                // GetStringArrayElement
                case StoryFlowNodeType.GetStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    int idx = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetString();
                    return "";
                }

                case StoryFlowNodeType.GetRandomStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    if (arr == null || arr.Count == 0) return "";
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetString();
                }

                case StoryFlowNodeType.ForEachStringLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetString();
                    }
                    return "";
                }

                // Image/Audio/Character variable nodes return string paths
                case StoryFlowNodeType.GetImage:
                case StoryFlowNodeType.SetImage:
                case StoryFlowNodeType.GetAudio:
                case StoryFlowNodeType.SetAudio:
                case StoryFlowNodeType.GetCharacter:
                case StoryFlowNodeType.SetCharacter:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    return variable?.Value?.GetString() ?? "";
                }

                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetString() ?? "";
                }

                // Image/Audio/Character array elements return string paths
                case StoryFlowNodeType.GetImageArrayElement:
                case StoryFlowNodeType.GetAudioArrayElement:
                case StoryFlowNodeType.GetCharacterArrayElement:
                {
                    string arraySuffix = EvaluatorHelpers.GetArrayHandleSuffix(node.Type);
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, arraySuffix);
                    int idx = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetString();
                    return "";
                }

                case StoryFlowNodeType.GetRandomImageArrayElement:
                case StoryFlowNodeType.GetRandomAudioArrayElement:
                case StoryFlowNodeType.GetRandomCharacterArrayElement:
                {
                    string arraySuffix = EvaluatorHelpers.GetArrayHandleSuffix(node.Type);
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, arraySuffix);
                    if (arr == null || arr.Count == 0) return "";
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetString();
                }

                case StoryFlowNodeType.ForEachImageLoop:
                case StoryFlowNodeType.ForEachAudioLoop:
                case StoryFlowNodeType.ForEachCharacterLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetString();
                    }
                    return "";
                }

                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetString() ?? "";
                        }
                    }
                    return "";
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetString() ?? "";
                }

                case StoryFlowNodeType.EnumToString:
                {
                    string enumVal = EnumEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Enum);
                    return enumVal ?? "";
                }

                default:
                    return "";
            }
        }
    }
}
