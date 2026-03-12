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

            var variableId = sourceNode.GetData("variableId");
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
                // Array-producing nodes: GetXxxArray, SetXxxArray
                var variableId = node.GetData("variableId");
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
    }
}
