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

            // Also check legacy "characterId" field
            if (string.IsNullOrEmpty(characterPath))
            {
                characterPath = node.GetData("characterId");
            }

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

            // Evaluate the new value based on variable type
            switch (variableType)
            {
                case "boolean":
                {
                    bool val = StoryFlowEvaluator.EvaluateBoolean(context, node.Id, StoryFlowHandles.In_Boolean);
                    targetVar.Value.SetBool(val);
                    break;
                }
                case "integer":
                {
                    int val = StoryFlowEvaluator.EvaluateInteger(context, node.Id, StoryFlowHandles.In_Integer);
                    targetVar.Value.SetInt(val);
                    break;
                }
                case "float":
                {
                    float val = StoryFlowEvaluator.EvaluateFloat(context, node.Id, StoryFlowHandles.In_Float);
                    targetVar.Value.SetFloat(val);
                    break;
                }
                case "string":
                {
                    string val = StoryFlowEvaluator.EvaluateString(context, node.Id, StoryFlowHandles.In_String);
                    targetVar.Value.SetString(val);
                    break;
                }
                case "enum":
                {
                    string val = StoryFlowEvaluator.EvaluateEnum(context, node.Id, StoryFlowHandles.In_Enum);
                    targetVar.Value.SetEnum(val);
                    break;
                }
                default:
                {
                    string val = StoryFlowEvaluator.EvaluateString(context, node.Id, StoryFlowHandles.In_String);
                    targetVar.Value.SetString(val);
                    break;
                }
            }

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
