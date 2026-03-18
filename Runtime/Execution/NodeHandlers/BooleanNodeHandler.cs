using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetBool nodes. GetBool, AndBool, OrBool, NotBool, EqualBool are
    /// evaluated lazily by the StoryFlowEvaluator and registered as no-ops in the dispatcher.
    /// </summary>
    public static class BooleanNodeHandler
    {
        public static void HandleSet(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the boolean input (use inline value as fallback when no input edge)
            bool fallback = node.GetDataBool("value");
            bool value = StoryFlowEvaluator.EvaluateBooleanWithDefault(context, node.Id, StoryFlowHandles.In_Boolean, fallback);

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.SetBool(value);
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetBool: variable '{variableId}' not found (node {node.Id}).");
            }

            // Try to follow the flow output edge
            var flowHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Flow);
            var flowEdge = context.CurrentScript.FindEdgeBySourceHandle(flowHandle);
            if (flowEdge != null)
            {
                component.ProcessNextNode(flowHandle);
                return;
            }

            // No outgoing edge: check for loop continuation
            SetNodeFallthrough(component, context, node);
        }

        /// <summary>
        /// Common fallthrough logic for Set* nodes with no outgoing edge:
        /// 1. If inside a forEach loop, continue the loop iteration.
        /// 2. If there is a last dialogue node, return to it for re-render.
        /// </summary>
        internal static void SetNodeFallthrough(StoryFlowComponent component, StoryFlowExecutionContext context, StoryFlowNode node)
        {
            // Check if inside a forEach loop
            var loop = context.PeekLoop();
            if (loop != null)
            {
                ArrayNodeHandler.ContinueForEachLoop(component, loop.NodeId);
                return;
            }

            // Return to last dialogue node for re-render (variable interpolation update)
            if (!string.IsNullOrEmpty(context.LastDialogueNodeId))
            {
                var dialogueNode = context.CurrentScript.GetNode(context.LastDialogueNodeId);
                if (dialogueNode != null)
                {
                    // Do NOT set IsEnteringDialogueViaEdge = true — we want audio to NOT re-trigger
                    component.GetContext().NextNode = dialogueNode;
                    return;
                }
            }
        }
    }
}
