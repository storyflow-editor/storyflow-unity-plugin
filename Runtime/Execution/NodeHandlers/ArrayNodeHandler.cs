using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles all array operations for all types: Set*Array, Set*ArrayElement,
    /// Add*ArrayElement, Remove*ArrayElement, Clear*Array, ForEach*Loop.
    /// Get*Array, Get*ArrayElement, GetRandom*ArrayElement, *ArrayLength,
    /// *ArrayContains, FindIn*Array are evaluated lazily (no-ops).
    /// </summary>
    public static class ArrayNodeHandler
    {
        // =====================================================================
        // SetArray — overwrite the entire array variable
        // =====================================================================

        public static void HandleSetArray(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();

            // Evaluate the array input
            var inputArray = StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType));

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.ArrayValue = inputArray != null ? new List<StoryFlowVariant>(inputArray) : new List<StoryFlowVariant>();
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetArray: variable '{variableId}' not found (node {node.Id}).");
            }

            // Follow flow edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // SetArrayElement — modify a single element by index
        // =====================================================================

        public static void HandleSetArrayElement(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();

            // Get the array from input
            var array = StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            if (array == null) array = new List<StoryFlowVariant>();

            // Get the index
            int index = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, "integer-3", node.GetDataInt("index"));

            // Get the value to set
            var value = EvaluateElementValue(context, node, elementType, "4");

            // Set the element if index is valid
            if (index >= 0 && index < array.Count)
            {
                array[index] = value;
            }

            // Update the source array variable
            UpdateConnectedArrayVariable(context, component, node, elementType, array);

            // Follow flow edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // AddArrayElement — append an element
        // =====================================================================

        public static void HandleAddArrayElement(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();

            // Get the array from input
            var array = StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            if (array == null) array = new List<StoryFlowVariant>();

            // Get the value to add
            var value = EvaluateElementValue(context, node, elementType, "3");

            array.Add(value);

            // Update the source array variable
            UpdateConnectedArrayVariable(context, component, node, elementType, array);

            // Follow flow edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // RemoveArrayElement — remove element at index
        // =====================================================================

        public static void HandleRemoveArrayElement(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();

            // Get the array from input
            var array = StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            if (array == null) array = new List<StoryFlowVariant>();

            // Get the index
            int index = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, "integer-3", node.GetDataInt("index"));

            // Remove the element if index is valid
            if (index >= 0 && index < array.Count)
            {
                array.RemoveAt(index);
            }

            // Update the source array variable
            UpdateConnectedArrayVariable(context, component, node, elementType, array);

            // Follow flow edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // ClearArray — clear all elements
        // =====================================================================

        public static void HandleClearArray(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();

            // Find the connected array source variable and clear it
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.ArrayValue = new List<StoryFlowVariant>();
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                // Try to clear via connected array input
                var arraySuffix = GetArrayInputSuffix(elementType, "2");
                var inputEdge = context.CurrentScript.FindInputEdge(node.Id, arraySuffix);
                if (inputEdge != null)
                {
                    var sourceNode = context.CurrentScript.GetNode(inputEdge.Source);
                    if (sourceNode != null)
                    {
                        var sourceVarId = sourceNode.GetData("variable");
                        var sourceVar = context.FindVariable(sourceVarId);
                        if (sourceVar != null)
                        {
                            sourceVar.Value.ArrayValue = new List<StoryFlowVariant>();
                            bool isGlobal = !context.LocalVariables.ContainsKey(sourceVar.Id);
                            component.BroadcastVariableChanged(sourceVar, isGlobal);
                        }
                    }
                }
            }

            // Follow flow edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // ForEachLoop — iterate over array elements
        // =====================================================================

        public static void HandleForEachLoop(StoryFlowComponent component, StoryFlowNode node, StoryFlowVariableType elementType)
        {
            var context = component.GetContext();
            var runtimeState = context.GetNodeRuntimeState(node.Id);

            // Initialize loop on first entry (LoopArray not set yet)
            if (runtimeState.LoopArray == null)
            {
                var arraySuffix = GetArrayInputSuffix(elementType);
                var array = StoryFlowEvaluator.EvaluateArray(context, node.Id, arraySuffix);
                runtimeState.LoopArray = array ?? new List<StoryFlowVariant>();
                runtimeState.LoopIndex = 0;
            }

            int currentIndex = runtimeState.LoopIndex;
            var loopArray = runtimeState.LoopArray;

            if (currentIndex < loopArray.Count)
            {
                // Clear evaluation caches from previous iteration so boolean chains re-evaluate
                context.ClearNodeRuntimeStates();

                // Push loop context for this iteration
                var loopType = GetElementTypeName(elementType);
                context.PushLoop(new LoopContext(node.Id, currentIndex, loopType));

                // Execute loop body
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopBody);
            }
            else
            {
                // Loop completed — clean up
                runtimeState.LoopArray = null;
                runtimeState.LoopIndex = 0;
                runtimeState.CachedOutput = null;

                // Only pop if the top frame belongs to this loop
                var top = context.PeekLoop();
                if (top != null && top.NodeId == node.Id)
                    context.PopLoop();

                // Follow completed edge
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopCompleted);
            }
        }

        /// <summary>
        /// Continues the next iteration of a forEach loop.
        /// Called when loop body execution reaches a Set* node with no outgoing edge,
        /// or when the loop body naturally completes back to the forEach node.
        /// </summary>
        public static void ContinueForEachLoop(StoryFlowComponent component, string loopNodeId)
        {
            var context = component.GetContext();
            var runtimeState = context.GetNodeRuntimeState(loopNodeId);

            if (runtimeState.LoopArray == null)
            {
                Debug.LogWarning($"[StoryFlow] ContinueForEachLoop: no active loop for node {loopNodeId}.");
                return;
            }

            // Pop the loop context for this iteration (only if it matches this loop)
            var top = context.PeekLoop();
            if (top != null && top.NodeId == loopNodeId)
                context.PopLoop();

            // Increment index
            runtimeState.LoopIndex++;

            // Re-process the forEach node to continue or complete
            var loopNode = context.CurrentScript.GetNode(loopNodeId);
            if (loopNode != null)
            {
                component.GetContext().NextNode = loopNode;
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Gets the target handle suffix for the array input of a given element type.
        /// </summary>
        private static string GetArrayInputSuffix(StoryFlowVariableType elementType, string indexSuffix = "")
        {
            string typeName = GetElementTypeName(elementType);
            string suffix = typeName + "-array";
            if (!string.IsNullOrEmpty(indexSuffix))
                suffix += "-" + indexSuffix;
            return suffix;
        }

        /// <summary>
        /// Maps StoryFlowVariableType to the handle name used in the editor.
        /// </summary>
        private static string GetElementTypeName(StoryFlowVariableType type)
        {
            return type switch
            {
                StoryFlowVariableType.Boolean => "boolean",
                StoryFlowVariableType.Integer => "integer",
                StoryFlowVariableType.Float => "float",
                StoryFlowVariableType.String => "string",
                StoryFlowVariableType.Image => "image",
                StoryFlowVariableType.Character => "character",
                StoryFlowVariableType.Audio => "audio",
                _ => "string"
            };
        }

        /// <summary>
        /// Evaluates an element value from the appropriate typed input handle.
        /// </summary>
        private static StoryFlowVariant EvaluateElementValue(
            StoryFlowExecutionContext context, StoryFlowNode node,
            StoryFlowVariableType elementType, string handleIndex)
        {
            switch (elementType)
            {
                case StoryFlowVariableType.Boolean:
                {
                    bool val = StoryFlowEvaluator.EvaluateBooleanWithDefault(
                        context, node.Id, "boolean-" + handleIndex, node.GetDataBool("value"));
                    return StoryFlowVariant.Bool(val);
                }
                case StoryFlowVariableType.Integer:
                {
                    int val = StoryFlowEvaluator.EvaluateIntegerWithDefault(
                        context, node.Id, "integer-" + handleIndex, node.GetDataInt("value"));
                    return StoryFlowVariant.Int(val);
                }
                case StoryFlowVariableType.Float:
                {
                    float val = StoryFlowEvaluator.EvaluateFloatWithDefault(
                        context, node.Id, "float-" + handleIndex, node.GetDataFloat("value"));
                    return StoryFlowVariant.Float(val);
                }
                case StoryFlowVariableType.String:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "string-" + handleIndex, node.GetData("value"));
                    return StoryFlowVariant.String(val);
                }
                case StoryFlowVariableType.Image:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "image-" + handleIndex, node.GetData("value"));
                    var variant = new StoryFlowVariant();
                    variant.Type = StoryFlowVariableType.Image;
                    variant.StringValue = val ?? "";
                    return variant;
                }
                case StoryFlowVariableType.Character:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "character-" + handleIndex, node.GetData("value"));
                    var variant = new StoryFlowVariant();
                    variant.Type = StoryFlowVariableType.Character;
                    variant.StringValue = val ?? "";
                    return variant;
                }
                case StoryFlowVariableType.Audio:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "audio-" + handleIndex, node.GetData("value"));
                    var variant = new StoryFlowVariant();
                    variant.Type = StoryFlowVariableType.Audio;
                    variant.StringValue = val ?? "";
                    return variant;
                }
                default:
                    return new StoryFlowVariant();
            }
        }

        /// <summary>
        /// Updates the array variable that is connected to this node's array input.
        /// Traces the input edge back to the source Get*Array/Set*Array node and updates its variable.
        /// </summary>
        private static void UpdateConnectedArrayVariable(
            StoryFlowExecutionContext context, StoryFlowComponent component,
            StoryFlowNode node, StoryFlowVariableType elementType,
            List<StoryFlowVariant> newArray)
        {
            // Try to find the source variable through the array input edge
            var arraySuffix = GetArrayInputSuffix(elementType, "2");
            var inputEdge = context.CurrentScript.FindInputEdge(node.Id, arraySuffix);
            if (inputEdge == null) return;

            var sourceNode = context.CurrentScript.GetNode(inputEdge.Source);
            if (sourceNode == null) return;

            var sourceVarId = sourceNode.GetData("variable");
            var sourceVar = context.FindVariable(sourceVarId);
            if (sourceVar != null)
            {
                sourceVar.Value.ArrayValue = newArray;
                bool isGlobal = !context.LocalVariables.ContainsKey(sourceVar.Id);
                component.BroadcastVariableChanged(sourceVar, isGlobal);
            }
        }
    }
}
