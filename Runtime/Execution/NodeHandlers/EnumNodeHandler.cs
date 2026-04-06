using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetEnum, SwitchOnEnum, and RandomBranch nodes. GetEnum and EqualEnum
    /// are evaluated lazily by StoryFlowEvaluator.
    /// </summary>
    public static class EnumNodeHandler
    {
        public static void HandleSet(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the enum input (use inline value as fallback when no input edge)
            string fallback = node.GetData("value");
            string value = StoryFlowEvaluator.EvaluateStringWithDefault(context, node.Id, StoryFlowHandles.In_Enum, fallback);

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.SetEnum(value);
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={value}");
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetEnum: variable '{variableId}' not found (node {node.Id}).");
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

        public static void HandleSwitchOnEnum(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Get the enum value from the referenced variable
            var variableId = node.GetData("variable");
            string enumValue = "";

            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                enumValue = variable.Value.GetEnum();
            }
            else
            {
                // Try evaluating from input edge
                enumValue = StoryFlowEvaluator.EvaluateEnum(context, node.Id, StoryFlowHandles.In_Enum);
            }

            // Find and follow the output handle matching the enum value
            var outputHandle = StoryFlowHandles.Source(node.Id, enumValue);
            var edge = context.CurrentScript.FindEdgeBySourceHandle(outputHandle);

            if (edge != null)
            {
                component.ProcessNextNode(outputHandle);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SwitchOnEnum: no output for enum value '{enumValue}' (node {node.Id}).");
            }
        }

        public static void HandleRandomBranch(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Parse options from node data
            var optionsJson = node.GetData("options");
            if (string.IsNullOrEmpty(optionsJson))
            {
                Debug.LogWarning($"[StoryFlow] RandomBranch: no options defined (node {node.Id}).");
                return;
            }

            try
            {
                var options = JArray.Parse(optionsJson);
                if (options.Count == 0)
                {
                    Debug.LogWarning($"[StoryFlow] RandomBranch: empty options array (node {node.Id}).");
                    return;
                }

                // Calculate total weight (resolve connected integer handles per option)
                int totalWeight = 0;
                var resolvedWeights = new int[options.Count];

                for (int i = 0; i < options.Count; i++)
                {
                    var opt = options[i];
                    var optId = opt.Value<string>("id") ?? "";
                    int defaultWeight = opt.Value<int?>("weight") ?? 1;

                    // Try to resolve weight from connected input edge
                    int weight = StoryFlowEvaluator.EvaluateIntegerWithDefault(
                        context, node.Id, "integer-" + optId, defaultWeight);

                    if (weight < 0) weight = 0;
                    resolvedWeights[i] = weight;
                    totalWeight += weight;
                }

                if (totalWeight <= 0)
                {
                    Debug.LogWarning($"[StoryFlow] RandomBranch: total weight is 0 (node {node.Id}).");
                    return;
                }

                // Pick a random value in [0, totalWeight)
                int roll = UnityEngine.Random.Range(0, totalWeight);
                int cumulative = 0;
                string selectedOptionId = null;

                for (int j = 0; j < options.Count; j++)
                {
                    cumulative += resolvedWeights[j];
                    if (roll < cumulative)
                    {
                        selectedOptionId = options[j].Value<string>("id") ?? "";
                        break;
                    }
                }

                if (selectedOptionId == null)
                {
                    selectedOptionId = options[0].Value<string>("id") ?? "";
                }

                var outputHandle = StoryFlowHandles.Source(node.Id, selectedOptionId);
                var edge = context.CurrentScript.FindEdgeBySourceHandle(outputHandle);
                if (edge != null)
                {
                    component.ProcessNextNode(outputHandle);
                }
                else
                {
                    Debug.LogWarning($"[StoryFlow] RandomBranch: no edge connected to selected output '{selectedOptionId}' (node {node.Id}).");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StoryFlow] RandomBranch: failed to parse options (node {node.Id}): {e.Message}");
            }
        }
    }
}
