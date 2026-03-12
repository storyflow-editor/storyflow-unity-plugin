using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates enum values from expression node chains.
    /// Handles enum variable lookups, conversions, and character variable access.
    /// </summary>
    internal static class EnumEvaluator
    {
        /// <summary>
        /// Evaluates the enum value arriving at a specific input handle of a node.
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
        /// Evaluates a node as an enum value based on its type.
        /// </summary>
        internal static string EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return "";

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Enum evaluation depth exceeded. Possible circular reference.");
                return "";
            }

            try
            {
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (state.CachedOutput != null)
                    return state.CachedOutput.GetEnum();

                string result = EvaluateFromNodeInternal(ctx, node);
                state.CachedOutput = StoryFlowVariant.Enum(result);
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
                case StoryFlowNodeType.GetEnum:
                case StoryFlowNodeType.SetEnum:
                {
                    var variableId = node.GetData("variableId");
                    var variable = ctx.FindVariable(variableId);
                    return variable?.Value?.GetEnum() ?? "";
                }

                case StoryFlowNodeType.IntToEnum:
                {
                    int intValue = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    // Need to find the enum values list — look at the variable connected downstream
                    var enumValues = EvaluatorHelpers.GetEnumValuesFromNode(ctx, node);
                    if (enumValues != null && enumValues.Count > 0)
                    {
                        int clampedIndex = Mathf.Clamp(intValue, 0, enumValues.Count - 1);
                        return enumValues[clampedIndex];
                    }
                    return "";
                }

                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var varType = node.GetData("variableType");
                    if (varType == "enum")
                    {
                        var charPath = node.GetData("characterPath");
                        var varName = node.GetData("variableName");
                        var characterData = ctx.FindCharacter(charPath);
                        if (characterData != null)
                        {
                            if (characterData.Variables != null &&
                                characterData.Variables.TryGetValue(varName, out var charVar))
                            {
                                return charVar.GetEnum();
                            }
                        }
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
                            return kvp.Value?.GetEnum() ?? "";
                        }
                    }
                    return "";
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetEnum() ?? "";
                }

                case StoryFlowNodeType.StringToEnum:
                {
                    string strVal = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return strVal ?? "";
                }

                default:
                    return "";
            }
        }
    }
}
