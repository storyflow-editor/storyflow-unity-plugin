using StoryFlow.Data;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles Branch nodes: evaluates a boolean condition input
    /// and follows the true or false output edge.
    /// </summary>
    public static class BranchNodeHandler
    {
        public static void Handle(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Pre-cache boolean expression results by walking the expression graph
            StoryFlowEvaluator.ProcessBooleanChain(context, node.Id);

            bool conditionValue = StoryFlowEvaluator.EvaluateBoolean(
                context, node.Id, StoryFlowHandles.In_Boolean + "-condition");

            component.Trace($"BRANCH {node.Id} condition={conditionValue.ToString().ToLower()}");

            if (conditionValue)
            {
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_True);
            }
            else
            {
                component.ProcessNextNodeFromSource(node.Id, StoryFlowHandles.Out_False);
            }
        }
    }
}
