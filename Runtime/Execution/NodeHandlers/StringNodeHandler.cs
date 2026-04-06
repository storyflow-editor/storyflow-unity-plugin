using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetString nodes. GetString, ConcatenateString, EqualString, ContainsString,
    /// ToUpperCase, ToLowerCase are evaluated lazily by StoryFlowEvaluator.
    /// </summary>
    public static class StringNodeHandler
    {
        public static void HandleSet(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the string input (use inline value as fallback when no input edge)
            string fallback = node.GetData("value");
            string value = StoryFlowEvaluator.EvaluateStringWithDefault(context, node.Id, StoryFlowHandles.In_String, fallback);

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.SetString(value);
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={value}");
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetString: variable '{variableId}' not found (node {node.Id}).");
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
    }
}
