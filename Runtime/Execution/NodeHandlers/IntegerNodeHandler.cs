using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetInt and RandomInt nodes. GetInt and arithmetic/comparison nodes
    /// are evaluated lazily by StoryFlowEvaluator.
    /// </summary>
    public static class IntegerNodeHandler
    {
        public static void HandleSet(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the integer input (use inline value as fallback when no input edge)
            int fallback = node.GetDataInt("value");
            int value = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, StoryFlowHandles.In_Integer, fallback);

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.SetInt(value);
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetInt: variable '{variableId}' not found (node {node.Id}).");
            }

            // Try to follow the flow output edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            // No outgoing edge fallthrough
            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        public static void HandleRandomInt(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate min and max from input edges or node data
            int min = StoryFlowEvaluator.EvaluateInteger(context, node.Id, StoryFlowHandles.In_Integer1);
            int max = StoryFlowEvaluator.EvaluateInteger(context, node.Id, StoryFlowHandles.In_Integer2);

            // Ensure min <= max
            if (min > max)
            {
                int temp = min;
                min = max;
                max = temp;
            }

            int result = UnityEngine.Random.Range(min, max + 1);

            // Store result in node runtime state for lazy evaluation
            var runtimeState = context.GetNodeRuntimeState(node.Id);
            runtimeState.CachedOutput = StoryFlowVariant.Int(result);

            // Try to follow the integer output edge
            component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_Integer);
        }
    }
}
