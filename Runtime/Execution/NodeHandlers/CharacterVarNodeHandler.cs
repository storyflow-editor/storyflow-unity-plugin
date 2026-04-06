using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles SetCharacterVar nodes. GetCharacterVar is evaluated lazily (no-op in dispatcher).
    /// </summary>
    public static class CharacterVarNodeHandler
    {
        public static void HandleSetCharacterVar(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Resolve character path (supports connected character input edge)
            string characterPath = EvaluatorHelpers.ResolveCharacterPath(context, node);

            if (string.IsNullOrEmpty(characterPath))
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVar: no character path specified (node {node.Id}).");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Find the character
            var characterData = context.FindCharacter(characterPath);
            if (characterData == null)
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVar: character not found for path '{characterPath}' (node {node.Id}).");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Get the variable name and type
            var variableName = node.GetData("variableName");
            var variableType = node.GetData("variableType");

            if (string.IsNullOrEmpty(variableName))
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVar: no variable name specified (node {node.Id}).");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Handle built-in "Name" field
            if (string.Equals(variableName, "Name", System.StringComparison.OrdinalIgnoreCase))
            {
                string val = StoryFlowEvaluator.EvaluateString(context, node.Id, StoryFlowHandles.In_String);
                characterData.Name = val;
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Handle built-in "Image" field
            if (string.Equals(variableName, "Image", System.StringComparison.OrdinalIgnoreCase))
            {
                string val = StoryFlowEvaluator.EvaluateString(context, node.Id, StoryFlowHandles.In_String);
                characterData.ImageAssetKey = val;
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Find the variable in the character's variables list
            StoryFlowVariable targetVar = characterData.FindVariableByName(variableName);

            if (targetVar == null)
            {
                Debug.LogWarning($"[StoryFlow] SetCharacterVar: variable '{variableName}' not found on character '{characterPath}' (node {node.Id}).");
                FollowFlowOrFallthrough(component, context, node);
                return;
            }

            // Evaluate the new value based on variable type.
            // Pass the node's inline value as the fallback default when no input edge is connected.
            switch (variableType)
            {
                case "boolean":
                {
                    bool fallback = node.GetDataBool("value", targetVar.Value.GetBool());
                    bool val = StoryFlowEvaluator.EvaluateBooleanWithDefault(context, node.Id, StoryFlowHandles.In_Boolean, fallback);
                    targetVar.Value.SetBool(val);
                    break;
                }
                case "integer":
                {
                    int fallback = node.GetDataInt("value", targetVar.Value.GetInt());
                    int val = StoryFlowEvaluator.EvaluateIntegerWithDefault(context, node.Id, StoryFlowHandles.In_Integer, fallback);
                    targetVar.Value.SetInt(val);
                    break;
                }
                case "float":
                {
                    float fallback = node.GetDataFloat("value", targetVar.Value.GetFloat());
                    float val = StoryFlowEvaluator.EvaluateFloatWithDefault(context, node.Id, StoryFlowHandles.In_Float, fallback);
                    targetVar.Value.SetFloat(val);
                    break;
                }
                case "string":
                {
                    string fallback = node.GetData("value", targetVar.Value.GetString());
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(context, node.Id, StoryFlowHandles.In_String, fallback);
                    targetVar.Value.SetString(val);
                    break;
                }
                case "enum":
                {
                    string fallback = node.GetData("value", targetVar.Value.GetEnum());
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(context, node.Id, StoryFlowHandles.In_Enum, fallback);
                    targetVar.Value.SetEnum(val);
                    break;
                }
                default:
                {
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(context, node.Id, StoryFlowHandles.In_String, node.GetData("value"));
                    targetVar.Value.SetString(val);
                    break;
                }
            }

            component.Trace($"VAR SET \"{characterPath}.{variableName}\" global=false value={targetVar.Value}");
            component.BroadcastVariableChanged(targetVar, false);

            FollowFlowOrFallthrough(component, context, node);
        }

        private static void FollowFlowOrFallthrough(StoryFlowComponent component, StoryFlowExecutionContext context, StoryFlowNode node)
        {
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
