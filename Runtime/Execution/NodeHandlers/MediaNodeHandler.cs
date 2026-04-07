using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles media-related Set* nodes: SetImage, SetBackgroundImage, SetAudio, PlayAudio, SetCharacter.
    /// Get* media nodes are evaluated lazily (no-ops in dispatcher).
    /// </summary>
    public static class MediaNodeHandler
    {
        // =====================================================================
        // SetImage — store an image asset key in a variable
        // =====================================================================

        public static void HandleSetImage(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the image input (asset key string)
            string imageKey = StoryFlowEvaluator.EvaluateStringWithDefault(
                context, node.Id, StoryFlowHandles.In_Image, node.GetData("value"));

            component.Trace($"IMAGE \"{imageKey ?? ""}\"");

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.Type = StoryFlowVariableType.Image;
                variable.Value.StringValue = imageKey ?? "";
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={imageKey ?? ""}");
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetImage: variable '{variableId}' not found (node {node.Id}).");
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
        // SetBackgroundImage — set the persistent background image
        // =====================================================================

        public static void HandleSetBackgroundImage(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the image input
            string imageKey = StoryFlowEvaluator.EvaluateStringWithDefault(
                context, node.Id, StoryFlowHandles.In_ImageInput, node.GetData("value"));

            component.Trace($"IMAGE \"{imageKey ?? ""}\"");

            if (!string.IsNullOrEmpty(imageKey))
            {
                var sprite = component.ResolveAsset<Sprite>(imageKey);
                if (sprite != null)
                {
                    context.PersistentBackgroundImage = sprite;
                    component.BroadcastBackgroundImageChanged(sprite);
                }
                else
                {
                    // Asset key present but could not be resolved
                    Debug.LogWarning($"[StoryFlow] SetBackgroundImage: could not resolve sprite for key '{imageKey}' (node {node.Id}).");
                }
            }
            else
            {
                // Clear the background image
                context.PersistentBackgroundImage = null;
                component.BroadcastBackgroundImageChanged(null);
            }

            // Follow the output edge
            var outputHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Output);
            var outputEdge = context.CurrentScript.FindEdgeBySourceHandle(outputHandle);
            if (outputEdge != null)
            {
                component.ProcessNextNode(outputHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // SetAudio — store an audio asset key in a variable
        // =====================================================================

        public static void HandleSetAudio(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the audio input (asset key string)
            string audioKey = StoryFlowEvaluator.EvaluateStringWithDefault(
                context, node.Id, StoryFlowHandles.In_Audio, node.GetData("value"));

            component.Trace($"AUDIO \"{audioKey ?? ""}\"");

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.Type = StoryFlowVariableType.Audio;
                variable.Value.StringValue = audioKey ?? "";
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={audioKey ?? ""}");
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetAudio: variable '{variableId}' not found (node {node.Id}).");
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
        // PlayAudio — resolve an audio asset and trigger playback
        // =====================================================================

        public static void HandlePlayAudio(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the audio input
            string audioKey = StoryFlowEvaluator.EvaluateStringWithDefault(
                context, node.Id, StoryFlowHandles.In_AudioInput, node.GetData("value"));

            bool audioLoop = node.GetDataBool("audioLoop");

            component.Trace($"AUDIO \"{audioKey ?? ""}\"");

            if (!string.IsNullOrEmpty(audioKey))
            {
                var clip = component.ResolveAsset<AudioClip>(audioKey);
                if (clip != null)
                {
                    // Play audio directly (same as dialogue audio handling)
                    component.PlayDialogueAudio(clip, audioLoop);
                    // Also fire event for game code that wants to handle it
                    component.BroadcastAudioPlayRequested(clip, audioLoop);
                }
                else
                {
                    Debug.LogWarning($"[StoryFlow] PlayAudio: could not resolve audio clip for key '{audioKey}' (node {node.Id}).");
                }
            }

            // Follow the output edge
            var outputHandle = StoryFlowHandles.Source(node.Id, StoryFlowHandles.Out_Output);
            var outputEdge = context.CurrentScript.FindEdgeBySourceHandle(outputHandle);
            if (outputEdge != null)
            {
                component.ProcessNextNode(outputHandle);
                return;
            }

            BooleanNodeHandler.SetNodeFallthrough(component, context, node);
        }

        // =====================================================================
        // SetCharacter — store a character path in a variable
        // =====================================================================

        public static void HandleSetCharacter(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Evaluate the character input (path string)
            string characterPath = StoryFlowEvaluator.EvaluateStringWithDefault(
                context, node.Id, StoryFlowHandles.In_Character, node.GetData("value"));

            // Find and update the variable
            var variableId = node.GetData("variable");
            var variable = context.FindVariable(variableId);
            if (variable != null)
            {
                variable.Value.Type = StoryFlowVariableType.Character;
                variable.Value.StringValue = characterPath ?? "";
                bool isGlobal = !context.LocalVariables.ContainsKey(variable.Id);
                component.Trace($"VAR SET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={characterPath ?? ""}");
                component.BroadcastVariableChanged(variable, isGlobal);
            }
            else
            {
                Debug.LogWarning($"[StoryFlow] SetCharacter: variable '{variableId}' not found (node {node.Id}).");
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
    }
}
