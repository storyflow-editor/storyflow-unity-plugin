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

            // .sfd accessors bound to an array variable. AHEAD of the name lookup at the
            // bottom on purpose: an accessor's "variable" data is the .sfd variable's display
            // NAME, not a script variable id, so falling through would ask FindVariable a
            // question about the wrong namespace. The list is already a deep copy (the store
            // copies out), so mutating callers get their own storage — which is what makes the
            // array ops' read-copy/mutate/write-back routing safe.
            if (EvaluatorHelpers.IsDataAssetAccessor(sourceNode.Type))
            {
                var dataAssetVar = EvaluatorHelpers.EvaluateDataAssetVariable(ctx, sourceNode);
                return dataAssetVar?.ArrayValue ?? new List<StoryFlowVariant>();
            }

            // Get Variable Names (contract §11.1): the names the wired asset's chain
            // declares, as a fresh String-tagged array.
            if (sourceNode.Type == StoryFlowNodeType.GetDataAssetVariableNames)
            {
                return ProjectDataAssetVariableNames(ctx, sourceNode);
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

                // .sfd accessors bound to an array variable (see the twin arm in EvaluateArray
                // for why this sits ahead of the name lookup)
                if (EvaluatorHelpers.IsDataAssetAccessor(node.Type))
                {
                    var dataAssetVar = EvaluatorHelpers.EvaluateDataAssetVariable(ctx, node);
                    return dataAssetVar?.ArrayValue ?? new List<StoryFlowVariant>();
                }

                // Get Variable Names (contract §11.1): the names the wired asset's chain
                // declares, as a fresh String-tagged array.
                if (node.Type == StoryFlowNodeType.GetDataAssetVariableNames)
                {
                    return ProjectDataAssetVariableNames(ctx, node);
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
        /// The Get Variable Names node's output (contract §11.1), projected into
        /// String-tagged elements. The list itself is the STORE's — derived from the same
        /// chain walk the resolver owns, reached through the context's names door — so a
        /// list can never disagree with what an accessor then reads by any of these names.
        /// FRESH per pull, elements and all: the names are strings the store built for
        /// this call, so a mutating consumer (an array op) gets its own storage. Every
        /// degraded path is an empty array with no warning.
        /// </summary>
        private static List<StoryFlowVariant> ProjectDataAssetVariableNames(
            StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            var names = ctx.ReadDataAssetVariableNames(node);
            var result = new List<StoryFlowVariant>(names.Count);
            foreach (var name in names)
            {
                result.Add(StoryFlowVariant.String(name));
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
