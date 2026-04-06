using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles Start, End, RunScript, RunFlow, and EntryFlow nodes.
    /// </summary>
    public static class ControlFlowNodeHandler
    {
        // =====================================================================
        // Start
        // =====================================================================

        public static void HandleStart(StoryFlowComponent component, StoryFlowNode node)
        {
            component.ProcessNextNodeFromSource(node.Id);
        }

        // =====================================================================
        // End
        // =====================================================================

        public static void HandleEnd(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Clear the loop stack when reaching an End node
            context.ClearLoopStack();

            // Pop flow call stack for depth tracking; check if the flow is an exit route
            string exitFlowId = null;
            if (context.FlowStackDepth > 0)
            {
                // Before popping, peek to get the flow id and check if it's an exit flow
                var flowFrame = context.PeekFlowFrame();
                if (flowFrame != null)
                {
                    // Check if this flow is an exit flow by looking up its definition
                    bool isExitFlow = false;
                    if (context.CurrentScript?.Flows != null)
                    {
                        foreach (var flow in context.CurrentScript.Flows)
                        {
                            if (flow.Id == flowFrame.FlowId)
                            {
                                isExitFlow = flow.IsExit;
                                break;
                            }
                        }
                    }

                    if (isExitFlow)
                    {
                        exitFlowId = flowFrame.FlowId;
                    }
                }

                context.PopFlowFrame();
            }

            // Check script call stack
            if (context.CallStackDepth > 0)
            {
                // Gather output variable values BEFORE popping (still in called script)
                var outputValues = new Dictionary<string, StoryFlowVariant>();
                foreach (var kvp in context.LocalVariables)
                {
                    var variable = kvp.Value;
                    if (variable.IsOutput)
                    {
                        outputValues[variable.Name] = new StoryFlowVariant(variable.Value);
                    }
                }

                var scriptPath = context.CurrentScript != null ? context.CurrentScript.ScriptPath : "";

                // Pop call frame - restores previous script and local variables
                var callFrame = context.PopCallFrame();
                if (callFrame == null)
                {
                    Debug.LogError("[StoryFlow] Failed to pop call stack.");
                    context.IsExecuting = false;
                    context.ShouldPause = true;
                    return;
                }

                // Restore the previous script asset
                if (callFrame.Script != null && callFrame.Script.TryGetTarget(out var previousScript))
                {
                    context.CurrentScript = previousScript;
                    previousScript.BuildIndices();
                }
                else
                {
                    // Fallback: try to get it from the project
                    var project = component.GetProject();
                    if (project != null && !string.IsNullOrEmpty(callFrame.ScriptPath))
                    {
                        var restored = project.GetScriptByPath(callFrame.ScriptPath);
                        if (restored != null)
                        {
                            context.CurrentScript = restored;
                            restored.BuildIndices();
                        }
                    }
                }

                // Store outputs on the RunScript node in the calling script
                if (!string.IsNullOrEmpty(callFrame.ReturnNodeId) && outputValues.Count > 0)
                {
                    var runtimeState = context.GetNodeRuntimeState(callFrame.ReturnNodeId);
                    foreach (var kvp in outputValues)
                    {
                        runtimeState.OutputValues[kvp.Key] = kvp.Value;
                    }
                }

                component.Trace($"SCRIPT RETURN \"{scriptPath}\"");
                component.NotifyScriptEnded(scriptPath);

                // Continue from the return node's output edge
                if (!string.IsNullOrEmpty(callFrame.ReturnNodeId))
                {
                    // Check for exit flow handle first
                    if (!string.IsNullOrEmpty(exitFlowId))
                    {
                        var exitHandle = StoryFlowHandles.SourceExit(callFrame.ReturnNodeId, exitFlowId);
                        var exitEdge = context.CurrentScript.FindEdgeBySourceHandle(exitHandle);
                        if (exitEdge != null)
                        {
                            component.ProcessNextNode(exitHandle);
                            return;
                        }
                    }

                    // Default output edge
                    var outputHandle = StoryFlowHandles.Source(callFrame.ReturnNodeId, "output");
                    var outputEdge = context.CurrentScript.FindEdgeBySourceHandle(outputHandle);
                    if (outputEdge != null)
                    {
                        component.ProcessNextNode(outputHandle);
                    }
                }
            }
            else if (context.FlowStackDepth > 0)
            {
                // Exiting a flow - the flow stack was already popped above.
                // Nothing else to do for flow exits since flows are jumps, not calls.
            }
            else
            {
                // No more frames - dialogue is finished
                component.StopDialogue();
            }
        }

        // =====================================================================
        // RunScript
        // =====================================================================

        public static void HandleRunScript(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();
            var project = component.GetProject();

            // Get the target script path
            var scriptId = node.GetData("script");
            if (string.IsNullOrEmpty(scriptId))
            {
                scriptId = node.GetData("value");
            }

            if (string.IsNullOrEmpty(scriptId))
            {
                Debug.LogWarning($"[StoryFlow] RunScript node {node.Id} has no script selected.");
                return;
            }

            if (project == null)
            {
                Debug.LogError("[StoryFlow] No project loaded for RunScript.");
                return;
            }

            var targetScript = project.GetScriptByPath(scriptId);
            if (targetScript == null)
            {
                Debug.LogError($"[StoryFlow] Script not found: {scriptId}");
                component.BroadcastError($"Script not found: {scriptId}");
                return;
            }

            // Check call stack depth
            if (context.CallStackDepth >= StoryFlowExecutionContext.MaxCallDepth)
            {
                Debug.LogError($"[StoryFlow] Call stack overflow: max depth {StoryFlowExecutionContext.MaxCallDepth} exceeded.");
                component.BroadcastError("Call stack overflow: too many nested script calls.");
                return;
            }

            // Evaluate input parameters while still in calling script's context
            var paramValues = new Dictionary<string, StoryFlowVariant>();
            var scriptInterfaceJson = node.GetData("scriptInterface");
            if (!string.IsNullOrEmpty(scriptInterfaceJson))
            {
                try
                {
                    var si = JObject.Parse(scriptInterfaceJson);
                    var parameters = si["parameters"] as JArray;
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            var paramId = param.Value<string>("id") ?? "";
                            var paramType = param.Value<string>("type") ?? "";
                            var isArray = param.Value<bool?>("isArray") ?? false;

                            StoryFlowVariant value;
                            if (isArray)
                            {
                                var handleSuffix = paramType + "-array-param-" + paramId;
                                value = StoryFlowEvaluator.EvaluateTypedArray(context, node.Id, handleSuffix, paramType);
                            }
                            else
                            {
                                var handleSuffix = paramType + "-param-" + paramId;
                                value = StoryFlowEvaluator.EvaluateTyped(context, node.Id, handleSuffix, paramType);
                            }

                            if (value != null)
                            {
                                paramValues[paramId] = value;
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[StoryFlow] Failed to parse scriptInterface for node {node.Id}: {e.Message}");
                }
            }

            // Push call frame (saves current state)
            if (!context.PushCallFrame(node.Id))
            {
                return; // Stack overflow already logged
            }

            // Switch to the new script
            context.CurrentScript = targetScript;
            targetScript.BuildIndices();

            // Deep copy the new script's local variables
            var newLocals = new Dictionary<string, StoryFlowVariable>();
            if (targetScript.Variables != null)
            {
                foreach (var kvp in targetScript.Variables)
                {
                    var copy = new StoryFlowVariable(kvp.Value);
                    // Re-hydrate array from DefaultValueJson if ArrayValue was lost
                    // (e.g. after Unity serialization round-trip, since ArrayValue is [NonSerialized])
                    if (copy.IsArray && copy.Value.ArrayValue == null && !string.IsNullOrEmpty(copy.DefaultValueJson))
                    {
                        copy.Value = StoryFlowVariant.DeserializeArrayFromJson(copy.Type, copy.DefaultValueJson);
                    }
                    newLocals[kvp.Key] = copy;
                }
            }

            // Apply input parameters by matching scriptInterface param names to variable names.
            // ScriptInterface param IDs differ from variable IDs, so we match by name.
            // Build a name→paramId lookup from the scriptInterface
            var paramNameToValue = new Dictionary<string, StoryFlowVariant>();
            if (!string.IsNullOrEmpty(scriptInterfaceJson))
            {
                try
                {
                    var si2 = JObject.Parse(scriptInterfaceJson);
                    var params2 = si2["parameters"] as JArray;
                    if (params2 != null)
                    {
                        foreach (var p in params2)
                        {
                            var pId = p.Value<string>("id") ?? "";
                            var pName = p.Value<string>("name") ?? "";
                            if (!string.IsNullOrEmpty(pName) && paramValues.ContainsKey(pId))
                            {
                                paramNameToValue[pName] = paramValues[pId];
                            }
                        }
                    }
                }
                catch { /* already logged above */ }
            }

            foreach (var kvp in paramNameToValue)
            {
                foreach (var localKvp in newLocals)
                {
                    var localVar = localKvp.Value;
                    if (localVar.IsInput && localVar.Name == kvp.Key)
                    {
                        localVar.Value = new StoryFlowVariant(kvp.Value);
                        break;
                    }
                }
            }

            // Replace context's local variables with the new script's
            context.LocalVariables.Clear();
            foreach (var kvp in newLocals)
            {
                context.LocalVariables[kvp.Key] = kvp.Value;
            }
            context.InvalidateLocalNameIndex();

            // Clear node runtime states since we're in a new script
            context.ClearNodeRuntimeStates();

            component.Trace($"SCRIPT CALL \"{scriptId}\"");
            component.NotifyScriptStarted(scriptId);

            // Process the start node
            var startNode = targetScript.GetNode(targetScript.StartNodeId);
            if (startNode != null)
            {
                context.NextNode = startNode;
            }
            else
            {
                Debug.LogError($"[StoryFlow] Start node not found in script: {scriptId}");
            }
        }

        // =====================================================================
        // RunFlow
        // =====================================================================

        public static void HandleRunFlow(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            var flowId = node.GetData("flowId");
            if (string.IsNullOrEmpty(flowId))
            {
                Debug.LogWarning($"[StoryFlow] RunFlow node {node.Id} has no flowId.");
                return;
            }

            // Check flow stack depth
            if (context.FlowStackDepth >= StoryFlowExecutionContext.MaxFlowDepth)
            {
                Debug.LogError($"[StoryFlow] Flow stack overflow: max depth {StoryFlowExecutionContext.MaxFlowDepth} exceeded.");
                component.BroadcastError("Flow stack overflow: too many nested flow calls.");
                return;
            }

            // Push flow frame for depth tracking
            if (!context.PushFlowFrame(flowId))
            {
                return; // Stack overflow already logged
            }

            // Special case: flowId == "start" means process the start node
            if (flowId == "start")
            {
                var startNode = context.CurrentScript.GetNode(context.CurrentScript.StartNodeId);
                if (startNode != null)
                {
                    context.NextNode = startNode;
                }
                return;
            }

            // Check if this is an exit flow — if so, immediately trigger End processing
            if (context.CurrentScript.Flows != null)
            {
                foreach (var flow in context.CurrentScript.Flows)
                {
                    if (flow.Id == flowId && flow.IsExit)
                    {
                        // Exit flows immediately trigger end processing
                        HandleEnd(component, node);
                        return;
                    }
                }
            }

            // Find the EntryFlow node matching the flowId
            if (context.CurrentScript.Flows != null)
            {
                foreach (var flow in context.CurrentScript.Flows)
                {
                    if (flow.Id == flowId && !string.IsNullOrEmpty(flow.EntryNodeId))
                    {
                        var entryNode = context.CurrentScript.GetNode(flow.EntryNodeId);
                        if (entryNode != null)
                        {
                            context.NextNode = entryNode;
                            return;
                        }
                    }
                }
            }

            // Fallback: search all nodes for an EntryFlow node with matching flowId in data
            if (context.CurrentScript.Nodes != null)
            {
                foreach (var kvp in context.CurrentScript.Nodes)
                {
                    var n = kvp.Value;
                    if (n.Type == StoryFlowNodeType.EntryFlow && n.GetData("flowId") == flowId)
                    {
                        context.NextNode = n;
                        return;
                    }
                }
            }

            Debug.LogWarning($"[StoryFlow] EntryFlow node for flowId '{flowId}' not found.");
        }

        // =====================================================================
        // EntryFlow
        // =====================================================================

        public static void HandleEntryFlow(StoryFlowComponent component, StoryFlowNode node)
        {
            component.ProcessNextNodeFromSource(node.Id);
        }
    }
}
