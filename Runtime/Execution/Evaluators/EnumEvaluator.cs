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
                // ForEach nodes and map reads — skip evaluation cache (cross-type conflicts /
                // live map storage; see the matching block in BooleanEvaluator for the rationale)
                bool skipCache = EvaluatorHelpers.IsForEachNode(node.Type) ||
                                 EvaluatorHelpers.IsMapReadNode(node.Type) ||
                                 EvaluatorHelpers.IsMultiOutputNode(node.Type);
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!skipCache && state.CachedOutput != null)
                    return state.CachedOutput.GetEnum();

                string result = EvaluateFromNodeInternal(ctx, node);
                if (!skipCache)
                    state.CachedOutput = StoryFlowVariant.Enum(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result}");
                }

                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static string EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            // Forward-compat: warn once per dialogue run when a script wires an
            // unrecognized node type into an enum input.
            if (ctx.MaybeWarnUnknownNode(node))
                return "";

            switch (node.Type)
            {
                case StoryFlowNodeType.GetEnum:
                case StoryFlowNodeType.SetEnum:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    string val = variable?.Value?.GetEnum() ?? "";
                    if (ctx.TraceEnabled && variable != null)
                    {
                        bool isGlobal = !ctx.LocalVariables.ContainsKey(variable.Id);
                        Debug.Log($"[SF-TRACE] VAR GET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={val}");
                    }
                    return val;
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

                // Map op branches on the node's keyType/valueType data (K/V in node data —
                // see the BooleanEvaluator map arms for the pattern note)
                case StoryFlowNodeType.GetMapValue:
                {
                    if (node.GetData("valueType") == "enum")
                    {
                        MapEvaluator.ComputeGetMapValue(ctx, node, out var mapValue);
                        return mapValue?.GetEnum() ?? "";
                    }
                    return "";
                }

                // forEachMap Key/Value (enum) — discriminate by SourceHandle suffix; see
                // the BooleanEvaluator's ForEachMap arm for the full pattern note
                case StoryFlowNodeType.ForEachMap:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    if (sourceHandle.EndsWith("-key") && node.GetData("keyType") == "enum")
                        return runtimeState.LoopKey?.GetEnum() ?? "";
                    if (sourceHandle.EndsWith("-value") && node.GetData("valueType") == "enum")
                        return runtimeState.LoopValue?.GetEnum() ?? "";
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
                    // Validate against the enum values list (resolved downstream, like
                    // IntToEnum): pass a matching value through, otherwise fall back to
                    // the first value so the result is always a valid enum entry.
                    var enumValues = EvaluatorHelpers.GetEnumValuesFromNode(ctx, node);
                    if (enumValues != null && enumValues.Contains(strVal))
                    {
                        return strVal;
                    }
                    return enumValues != null && enumValues.Count > 0 ? enumValues[0] : "";
                }

                default:
                    return "";
            }
        }
    }
}
