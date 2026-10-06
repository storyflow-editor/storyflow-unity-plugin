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
            long failures = context.ResolutionFailures;
            var inputArray = elementType == StoryFlowVariableType.DataAsset
                ? ArrayEvaluator.EvaluateTypedArray(context, node.Id, GetArrayInputSuffix(elementType), elementType)
                : StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType));

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = elementType == StoryFlowVariableType.DataAsset
                ? DataReferenceEvaluator.ReadVariable(context, node, true) : context.FindVariable(node);
            if (variable != null && (elementType != StoryFlowVariableType.DataAsset || failures == context.ResolutionFailures))
            {
                // With nothing connected the variable keeps its value (HTML updateArrayVariable)
                var inputEdge = context.CurrentScript.FindInputEdge(node.Id, GetArrayInputSuffix(elementType));
                if (inputEdge != null && context.CurrentScript.GetNode(inputEdge.Source) != null)
                    variable.Value.ArrayValue = inputArray != null ? new List<StoryFlowVariant>(inputArray) : new List<StoryFlowVariant>();
                else if (variable.Value.ArrayValue == null)
                    variable.Value.ArrayValue = new List<StoryFlowVariant>();
                bool isGlobal = elementType == StoryFlowVariableType.DataAsset
                    ? node.GetDataBool("isGlobal") : context.IsGlobal(variable);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value=[{variable.Value.ArrayValue.Count} elements]");
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
            var array = elementType == StoryFlowVariableType.DataAsset
                ? new List<StoryFlowVariant>(ArrayEvaluator.EvaluateTypedArray(context, node.Id, GetArrayInputSuffix(elementType, "2"), elementType))
                : StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            // The op works on its own copy and writes it back (HTML evaluateArrayFromNode slices)
            array = array != null ? new List<StoryFlowVariant>(array) : new List<StoryFlowVariant>();

            // Get the index. The export dialect renames the inline fallbacks: the .sfe
            // "index" is exported as "value1" and "value" as "value2" on set*ArrayElement
            // (json-export-strategy.ts), and the importer flattens fields verbatim.
            int index = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, "integer-3", node.GetDataInt("value1"));

            // Get the value to set
            var value = EvaluateElementValue(context, node, elementType, "4", "value2");

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
            var array = elementType == StoryFlowVariableType.DataAsset
                ? new List<StoryFlowVariant>(ArrayEvaluator.EvaluateTypedArray(context, node.Id, GetArrayInputSuffix(elementType, "2"), elementType))
                : StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            // The op works on its own copy and writes it back (HTML evaluateArrayFromNode slices)
            array = array != null ? new List<StoryFlowVariant>(array) : new List<StoryFlowVariant>();

            // Get the value to add (the export writes addTo*Array's inline value as "value")
            var value = EvaluateElementValue(context, node, elementType, "3", "value");

            array.Add(value);

            // Store result array in cached output so downstream nodes (e.g., RunScript array params)
            // connected to this node's output can read it (matches HTML's setNodeOutputValue)
            var runtimeState = context.GetNodeRuntimeState(node.Id);
            runtimeState.CachedOutput = new StoryFlowVariant { ArrayValue = new List<StoryFlowVariant>(array) };
            runtimeState.HasExecutionOutput = true;

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
            var array = elementType == StoryFlowVariableType.DataAsset
                ? new List<StoryFlowVariant>(ArrayEvaluator.EvaluateTypedArray(context, node.Id, GetArrayInputSuffix(elementType, "2"), elementType))
                : StoryFlowEvaluator.EvaluateArray(context, node.Id, GetArrayInputSuffix(elementType, "2"));
            // The op works on its own copy and writes it back (HTML evaluateArrayFromNode slices)
            array = array != null ? new List<StoryFlowVariant>(array) : new List<StoryFlowVariant>();

            // Get the index (the export renames removeFrom*Array's inline "index" to "value")
            int index = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, "integer-3", node.GetDataInt("value"));

            // Remove the element if index is valid
            if (index >= 0 && index < array.Count)
            {
                array.RemoveAt(index);
            }

            // Store result in cached output for downstream consumers
            var runtimeState = context.GetNodeRuntimeState(node.Id);
            runtimeState.CachedOutput = new StoryFlowVariant { ArrayValue = new List<StoryFlowVariant>(array) };
            runtimeState.HasExecutionOutput = true;

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

            // A .sfd accessor on the array input clears INTO the session overlay, and is
            // resolved FIRST — ahead of this node's own "variable" field, not just ahead of
            // the edge fallback — because the accessor is what the author wired the op to,
            // and a name lookup that happened to hit would clear the wrong array. The output
            // stamp and the flow tail below are shared with the routed case, which is why the
            // op node is passed as null: this path stamps for itself a few lines down, and the
            // router's own re-stamp would only be overwritten by it.
            var clearInputEdge = context.CurrentScript.FindInputEdge(
                node.Id, GetArrayInputSuffix(elementType, "2"));
            var clearInputSource = clearInputEdge != null
                ? context.CurrentScript.GetNode(clearInputEdge.Source)
                : null;
            if (!DataAssetNodeHandler.TryRouteArrayOpToDataAsset(
                    component, context, null, clearInputSource, new List<StoryFlowVariant>()))
            {
                if (elementType == StoryFlowVariableType.DataAsset)
                {
                    UpdateConnectedArrayVariable(context, component, node, elementType, new List<StoryFlowVariant>());
                }
                else
                {
                    // Find the connected array source variable and clear it
                    var variableId = node.GetData("variable");
                    var variable = context.FindVariable(node);
                    if (variable != null)
                    {
                        variable.Value.ArrayValue = new List<StoryFlowVariant>();
                        bool isGlobal = context.IsGlobal(variable);
                        component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value=[0 elements]");
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
                                var sourceVar = context.FindVariable(sourceNode);
                                if (sourceVar != null)
                                {
                                    sourceVar.Value.ArrayValue = new List<StoryFlowVariant>();
                                    bool isGlobal = context.IsGlobal(sourceVar);
                                    component.Trace($"VAR SET \"{sourceVar.Name}\" global={isGlobal.ToString().ToLower()} value=[0 elements]");
                                    component.BroadcastVariableChanged(sourceVar, isGlobal);
                                }
                            }
                        }
                    }
                }
            }

            // Store result in cached output for downstream consumers
            var clearRtState = context.GetNodeRuntimeState(node.Id);
            clearRtState.CachedOutput = new StoryFlowVariant { ArrayValue = new List<StoryFlowVariant>() };
            clearRtState.HasExecutionOutput = true;

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
                // A copy taken at loop start: body writes to the array neither extend nor shorten the loop
                var array = StoryFlowEvaluator.EvaluateArray(context, node.Id, arraySuffix);
                runtimeState.LoopArray = array != null ? new List<StoryFlowVariant>(array) : new List<StoryFlowVariant>();
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

                // Trace: log each loop iteration
                component.Trace($"LOOP {node.Id} index={currentIndex} value={loopArray[currentIndex]}");

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

                // Follow completed edge. With nothing connected, ProcessNextNode continues the
                // enclosing loop
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_LoopCompleted);
            }
        }

        /// <summary>
        /// Continues the next iteration of a forEach loop (array or map — the dispatcher
        /// re-routes the loop node to the right handler by node type).
        /// Called when loop body execution reaches a node with nothing connected to the
        /// output it takes, or when the loop body naturally completes back to the forEach node.
        /// </summary>
        public static void ContinueForEachLoop(StoryFlowComponent component, string loopNodeId)
        {
            var context = component.GetContext();
            var runtimeState = context.GetNodeRuntimeState(loopNodeId);

            if (runtimeState.LoopArray == null && runtimeState.LoopMapEntries == null)
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
            context.LoopSteps++;

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
                StoryFlowVariableType.DataAsset => "dataAsset",
                StoryFlowVariableType.Audio => "audio",
                _ => "string"
            };
        }

        /// <summary>
        /// Evaluates an element value from the appropriate typed input handle.
        /// inlineValueKey is the node-data field holding the inline (unwired) fallback —
        /// the export dialect writes addTo*Array's value as "value" but set*ArrayElement's
        /// as "value2" (json-export-strategy.ts), so each caller passes its own key.
        /// </summary>
        private static StoryFlowVariant EvaluateElementValue(
            StoryFlowExecutionContext context, StoryFlowNode node,
            StoryFlowVariableType elementType, string handleIndex, string inlineValueKey)
        {
            switch (elementType)
            {
                case StoryFlowVariableType.Boolean:
                {
                    bool val = StoryFlowEvaluator.EvaluateBooleanWithDefault(
                        context, node.Id, "boolean-" + handleIndex, node.GetDataBool(inlineValueKey));
                    return StoryFlowVariant.Bool(val);
                }
                case StoryFlowVariableType.Integer:
                {
                    int val = StoryFlowEvaluator.EvaluateIntegerWithDefault(
                        context, node.Id, "integer-" + handleIndex, node.GetDataInt(inlineValueKey));
                    return StoryFlowVariant.Int(val);
                }
                case StoryFlowVariableType.Float:
                {
                    float val = StoryFlowEvaluator.EvaluateFloatWithDefault(
                        context, node.Id, "float-" + handleIndex, node.GetDataFloat(inlineValueKey));
                    return StoryFlowVariant.Float(val);
                }
                case StoryFlowVariableType.String:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "string-" + handleIndex, node.GetData(inlineValueKey));
                    return new StoryFlowVariant
                    {
                        Type = StoryFlowVariableType.String, StringValue = val, IsLiteralString = true
                    };
                }
                case StoryFlowVariableType.Image:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "image-" + handleIndex, node.GetData(inlineValueKey));
                    var variant = new StoryFlowVariant();
                    variant.Type = StoryFlowVariableType.Image;
                    variant.StringValue = val ?? "";
                    return variant;
                }
                case StoryFlowVariableType.DataAsset:
                    return new StoryFlowVariant { Type = elementType, StringValue = DataReferenceEvaluator.Evaluate(context, node.Id, "dataAsset-" + handleIndex, node.GetData(inlineValueKey)) };
                case StoryFlowVariableType.Character:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "character-" + handleIndex, node.GetData(inlineValueKey));
                    var variant = new StoryFlowVariant();
                    variant.Type = StoryFlowVariableType.Character;
                    variant.StringValue = val ?? "";
                    return variant;
                }
                case StoryFlowVariableType.Audio:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(
                        context, node.Id, "audio-" + handleIndex, node.GetData(inlineValueKey));
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

            // A .sfd accessor on the far end routes the whole op into the session overlay,
            // and outranks EVERY name-based lookup below (see TryRouteArrayOpToDataAsset).
            if (DataAssetNodeHandler.TryRouteArrayOpToDataAsset(
                    component, context, node, sourceNode, newArray))
                return;

            if (elementType == StoryFlowVariableType.DataAsset)
            {
                // Only the immediate source is writable. Modifier outputs are snapshots;
                // following their input edges would mutate the original variable again.
                if (DataReferenceEvaluator.IsArrayVariableNode(sourceNode))
                {
                    var variable = DataReferenceEvaluator.ReadVariable(context, sourceNode, true);
                    if (variable != null)
                    {
                        variable.Value.ArrayValue = newArray;
                        component.BroadcastVariableChanged(variable, sourceNode.GetDataBool("isGlobal"));
                    }
                }
                else if ((sourceNode.Type == StoryFlowNodeType.GetCharacterVar ||
                          sourceNode.Type == StoryFlowNodeType.SetCharacterVar) &&
                         sourceNode.GetData("variableType") == "dataAsset" && sourceNode.GetDataBool("isArray"))
                {
                    var characterPath = EvaluatorHelpers.ResolveCharacterPath(context, sourceNode);
                    var character = context.FindCharacter(characterPath);
                    var variable = character?.FindVariableByName(sourceNode.GetData("variableName"));
                    if (variable != null && variable.Type == StoryFlowVariableType.DataAsset && variable.IsArray)
                    {
                        variable.Value.ArrayValue = newArray;
                        character.Variables[variable.Name] = variable.Value;
                        component.BroadcastVariableChanged(variable, false);
                        component.BroadcastCharacterVariableChanged(characterPath, variable.Name, variable.Value);
                    }
                }
                return;
            }

            var sourceVarId = sourceNode.GetData("variable");
            var sourceVar = context.FindVariable(sourceNode);
            if (sourceVar != null)
            {
                sourceVar.Value.ArrayValue = newArray;
                bool isGlobal = context.IsGlobal(sourceVar);
                component.Trace($"VAR SET \"{sourceVar.Name}\" global={isGlobal.ToString().ToLower()} value=[{newArray.Count} elements]");
                component.BroadcastVariableChanged(sourceVar, isGlobal);
            }
        }
    }
}
