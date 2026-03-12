using System.Collections.Generic;
using StoryFlow.Data;
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
        // Dual-input evaluation with fallback to node data
        // =====================================================================

        internal static int EvaluateIntegerInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Integer1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                    return IntegerEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                    return IntegerEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                    return FloatEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                    return FloatEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                    return StringEvaluator.EvaluateFromNode(ctx, sourceNode);
            }
            return node.GetData("value1");
        }

        internal static string EvaluateStringInput2(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_String2);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                    return StringEvaluator.EvaluateFromNode(ctx, sourceNode);
            }
            return node.GetData("value2");
        }

        internal static string EvaluateEnumInput1(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var edge = ctx.CurrentScript.FindInputEdge(node.Id, StoryFlowHandles.In_Enum1);
            if (edge != null)
            {
                var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
                if (sourceNode != null)
                    return EnumEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                    return EnumEvaluator.EvaluateFromNode(ctx, sourceNode);
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
                            var varId = targetNode.GetData("variableId");
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
            var runtimeState = ctx.GetNodeRuntimeState(node.Id);
            if (runtimeState.OutputValues == null || runtimeState.OutputValues.Count == 0)
                return null;

            string sourceHandle = ctx.LastSourceHandle;
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
    }
}
