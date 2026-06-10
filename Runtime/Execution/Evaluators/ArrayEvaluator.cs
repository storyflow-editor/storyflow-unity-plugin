using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates array values from expression node chains.
    /// Handles typed array lookups (bool, int, float, string) and array-producing node evaluation.
    /// </summary>
    internal static class ArrayEvaluator
    {
        /// <summary>
        /// Evaluates a boolean array from an input edge.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateBoolArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, StoryFlowVariableType.Boolean);
        }

        /// <summary>
        /// Evaluates an integer array from an input edge.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateIntArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, StoryFlowVariableType.Integer);
        }

        /// <summary>
        /// Evaluates a float array from an input edge.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateFloatArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, StoryFlowVariableType.Float);
        }

        /// <summary>
        /// Evaluates a string array from an input edge.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateStringArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, StoryFlowVariableType.String);
        }

        /// <summary>
        /// Evaluates an untyped array from an input edge. Returns the raw list.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return new List<StoryFlowVariant>();

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return new List<StoryFlowVariant>();

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return new List<StoryFlowVariant>();

            // Forward-compat: warn once per dialogue run when a script wires an
            // unrecognized node type into an array input.
            if (ctx.MaybeWarnUnknownNode(sourceNode))
                return new List<StoryFlowVariant>();

            // Handle RunScript output arrays — resolve via the node's stored output values
            if (sourceNode.Type == StoryFlowNodeType.RunScript)
            {
                var prevHandle = ctx.LastSourceHandle;
                ctx.LastSourceHandle = edge.SourceHandle;
                var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, sourceNode);
                ctx.LastSourceHandle = prevHandle;
                return outputValue?.ArrayValue != null
                    ? new List<StoryFlowVariant>(outputValue.ArrayValue)
                    : new List<StoryFlowVariant>();
            }

            // Handle getCharacterVar/setCharacterVar nodes that can return arrays
            if (sourceNode.Type == StoryFlowNodeType.GetCharacterVar ||
                sourceNode.Type == StoryFlowNodeType.SetCharacterVar)
            {
                var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, sourceNode);
                return charVar?.ArrayValue ?? new List<StoryFlowVariant>();
            }

            // Handle array modify nodes (add/remove/clear) that output their result array.
            // These nodes don't have a 'variable' field — their output is stored in CachedOutput.
            if (IsArrayModifyNode(sourceNode.Type))
            {
                var state = ctx.GetNodeRuntimeState(sourceNode.Id);
                return state?.CachedOutput?.ArrayValue ?? new List<StoryFlowVariant>();
            }

            // mapKeys/mapValues project the resolved map's entries into a FRESH array
            if (sourceNode.Type == StoryFlowNodeType.MapKeys || sourceNode.Type == StoryFlowNodeType.MapValues)
            {
                return ProjectMapEntries(ctx, sourceNode);
            }

            var variableId = sourceNode.GetData("variable");
            if (!string.IsNullOrEmpty(variableId))
            {
                var variable = ctx.FindVariable(variableId);
                if (variable?.Value?.ArrayValue != null)
                    return variable.Value.ArrayValue;
            }

            return new List<StoryFlowVariant>();
        }

        /// <summary>
        /// Evaluates a typed array from an input edge.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateTypedArray(
            StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, StoryFlowVariableType expectedType)
        {
            if (ctx?.CurrentScript == null) return new List<StoryFlowVariant>();

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return new List<StoryFlowVariant>();

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return new List<StoryFlowVariant>();

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            var result = EvaluateArrayFromNode(ctx, sourceNode, expectedType);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates an array-producing node. Looks up the array variable by variableId.
        /// </summary>
        internal static List<StoryFlowVariant> EvaluateArrayFromNode(
            StoryFlowExecutionContext ctx, StoryFlowNode node, StoryFlowVariableType expectedType)
        {
            if (node == null || ctx == null) return new List<StoryFlowVariant>();

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Array evaluation depth exceeded. Possible circular reference.");
                return new List<StoryFlowVariant>();
            }

            try
            {
                // Forward-compat: warn once per dialogue run when a script wires an
                // unrecognized node type into a typed-array input.
                if (ctx.MaybeWarnUnknownNode(node))
                    return new List<StoryFlowVariant>();

                // Handle RunScript output arrays — resolve via the node's stored output values
                if (node.Type == StoryFlowNodeType.RunScript)
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.ArrayValue != null
                        ? new List<StoryFlowVariant>(outputValue.ArrayValue)
                        : new List<StoryFlowVariant>();
                }

                // Handle getCharacterVar/setCharacterVar nodes that can return arrays
                if (node.Type == StoryFlowNodeType.GetCharacterVar ||
                    node.Type == StoryFlowNodeType.SetCharacterVar)
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.ArrayValue ?? new List<StoryFlowVariant>();
                }

                // Handle array modify nodes (add/remove/clear) that output their result array
                if (IsArrayModifyNode(node.Type))
                {
                    var state = ctx.GetNodeRuntimeState(node.Id);
                    return state?.CachedOutput?.ArrayValue ?? new List<StoryFlowVariant>();
                }

                // mapKeys/mapValues project the resolved map's entries into a FRESH array
                if (node.Type == StoryFlowNodeType.MapKeys || node.Type == StoryFlowNodeType.MapValues)
                {
                    return ProjectMapEntries(ctx, node);
                }

                // Array-producing nodes: GetXxxArray, SetXxxArray
                var variableId = node.GetData("variable");
                if (!string.IsNullOrEmpty(variableId))
                {
                    var variable = ctx.FindVariable(variableId);
                    if (variable?.Value?.ArrayValue != null)
                        return variable.Value.ArrayValue;
                }

                return new List<StoryFlowVariant>();
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        /// <summary>
        /// Projects a mapKeys/mapValues node's resolved map (input "1") into a fresh array,
        /// in insertion order. Typed per the node's keyType/valueType: keys come out as
        /// key-typed variants, values as value-typed variants. FRESH per pull — elements are
        /// deep copies, so the projected array never aliases the live map's entry variants,
        /// and the result is never cached (live map mutations must be visible on re-pull).
        /// </summary>
        private static List<StoryFlowVariant> ProjectMapEntries(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var map = MapEvaluator.EvaluateMapInput(ctx, node, "1");
            var result = new List<StoryFlowVariant>(map?.Count ?? 0);
            if (map == null) return result;

            bool keys = node.Type == StoryFlowNodeType.MapKeys;
            foreach (var entry in map)
            {
                var element = keys ? entry.Key : entry.Value;
                result.Add(element != null ? new StoryFlowVariant(element) : new StoryFlowVariant());
            }
            return result;
        }

        /// <summary>
        /// Returns true if the node type is an array modify operation (add/remove/clear/set)
        /// whose output is stored in CachedOutput rather than a variable field.
        /// </summary>
        private static bool IsArrayModifyNode(StoryFlowNodeType type)
        {
            switch (type)
            {
                case StoryFlowNodeType.AddBoolArrayElement:
                case StoryFlowNodeType.AddIntArrayElement:
                case StoryFlowNodeType.AddFloatArrayElement:
                case StoryFlowNodeType.AddStringArrayElement:
                case StoryFlowNodeType.AddImageArrayElement:
                case StoryFlowNodeType.AddCharacterArrayElement:
                case StoryFlowNodeType.AddAudioArrayElement:
                case StoryFlowNodeType.RemoveBoolArrayElement:
                case StoryFlowNodeType.RemoveIntArrayElement:
                case StoryFlowNodeType.RemoveFloatArrayElement:
                case StoryFlowNodeType.RemoveStringArrayElement:
                case StoryFlowNodeType.RemoveImageArrayElement:
                case StoryFlowNodeType.RemoveCharacterArrayElement:
                case StoryFlowNodeType.RemoveAudioArrayElement:
                case StoryFlowNodeType.ClearBoolArray:
                case StoryFlowNodeType.ClearIntArray:
                case StoryFlowNodeType.ClearFloatArray:
                case StoryFlowNodeType.ClearStringArray:
                case StoryFlowNodeType.ClearImageArray:
                case StoryFlowNodeType.ClearCharacterArray:
                case StoryFlowNodeType.ClearAudioArray:
                    return true;
                default:
                    return false;
            }
        }
    }
}
