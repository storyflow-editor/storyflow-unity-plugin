using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetFloat and RandomFloat nodes. GetFloat and arithmetic/comparison nodes
    /// are evaluated lazily by StoryFlowEvaluator.
    /// </summary>
    public static class FloatNodeHandler
    {
        public static void HandleSet(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the float input (use inline value as fallback when no input edge)
            float fallback = node.GetDataFloat("value");
            float value = StoryFlowEvaluator.EvaluateFloatWithDefault(context, node.Id, StoryFlowHandles.In_Float, fallback);

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.SetFloat(value);
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetFloat: variable '{variableId}' not found (node {node.Id}).");
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

        public static void HandleRandomFloat(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate min and max from input edges or node data
            float min = StoryFlowEvaluator.EvaluateFloat(context, node.Id, StoryFlowHandles.In_Float1);
            float max = StoryFlowEvaluator.EvaluateFloat(context, node.Id, StoryFlowHandles.In_Float2);

            // Ensure min <= max
            if (min > max)
            {
                float temp = min;
                min = max;
                max = temp;
            }

            float result = UnityEngine.Random.Range(min, max);

            // Store result in node runtime state for lazy evaluation
            var runtimeState = context.GetNodeRuntimeState(node.Id);
            runtimeState.CachedOutput = StoryFlowVariant.Float(result);

            // Try to follow the float output edge
            component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_Float);
        }
    }
}
