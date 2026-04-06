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

            // Handle getCharacterVar nodes that can return arrays
            if (sourceNode.Type == StoryFlowNodeType.GetCharacterVar)
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

            return EvaluateArrayFromNode(ctx, sourceNode, expectedType);
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
                // Handle getCharacterVar nodes that can return arrays
                if (node.Type == StoryFlowNodeType.GetCharacterVar)
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
