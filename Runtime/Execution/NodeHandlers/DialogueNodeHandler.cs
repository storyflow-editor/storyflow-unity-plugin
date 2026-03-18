using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// Handles Dialogue nodes: builds the dialogue state (character, text, options, media)
    /// and pauses execution waiting for user input.
    /// </summary>
    public static class DialogueNodeHandler
    {
        public static void Handle(StoryFlowComponent component, StoryFlowNode node)
        {
            var context = component.GetContext();

            // Dialogue nodes cannot be used inside forEach loops
            var loopCtx = context.PeekLoop();
            if (loopCtx != null)
            {
                component.BroadcastError("Dialogue nodes cannot be used inside ForEach loops.");
                return;
            }

            // Track the last dialogue node so Set* nodes with no outgoing edge can return here
            context.LastDialogueNodeId = node.Id;

            // Determine if this is a fresh entry via edge (audio should play)
            // or a return from a Set* node (audio should not re-trigger)
            bool isFreshEntry = context.IsEnteringDialogueViaEdge;

            // Build dialogue state
            var state = context.CurrentDialogueState;
            state.NodeId = node.Id;

            // 1. Resolve character FIRST (before text interpolation so {Character.Name} works)
            var characterPath = node.GetData("character");
            if (!string.IsNullOrEmpty(characterPath))
            {
                var characterData = context.FindCharacter(characterPath);
                if (characterData != null)
                {
                    state.Character = characterData;
                }
                else
                {
                    state.Character = new StoryFlowCharacterData();
                }
            }
            else
            {
                state.Character = new StoryFlowCharacterData();
            }

            // 2. Resolve title
            var titleKey = node.GetData("title");
            string rawTitle = null;
            if (!string.IsNullOrEmpty(titleKey))
            {
                rawTitle = context.GetString(component.LanguageCode + "." + titleKey);
            }
            state.Title = !string.IsNullOrEmpty(rawTitle)
                ? StoryFlowInterpolation.Interpolate(rawTitle, context)
                : "";

            // 3. Resolve text
            var textKey = node.GetData("text");
            string rawText = null;
            if (!string.IsNullOrEmpty(textKey))
            {
                rawText = context.GetString(component.LanguageCode + "." + textKey);
            }
            state.Text = !string.IsNullOrEmpty(rawText)
                ? StoryFlowInterpolation.Interpolate(rawText, context)
                : "";

            // 4. Resolve image
            var imageKey = node.GetData("image");
            bool imageReset = node.GetDataBool("imageReset");

            if (!string.IsNullOrEmpty(imageKey))
            {
                var sprite = component.ResolveAsset<Sprite>(imageKey);
                if (sprite != null)
                {
                    context.PersistentBackgroundImage = sprite;
                    state.Image = sprite;
                }
                else
                {
                    state.Image = context.PersistentBackgroundImage;
                }
            }
            else if (imageReset)
            {
                context.PersistentBackgroundImage = null;
                state.Image = null;
            }
            else
            {
                // Keep persistent background image
                state.Image = context.PersistentBackgroundImage;
            }

            // 5. Resolve audio
            var audioKey = node.GetData("audio");
            if (!string.IsNullOrEmpty(audioKey))
            {
                state.Audio = component.ResolveAsset<AudioClip>(audioKey);
            }
            else
            {
                state.Audio = null;
            }

            state.AudioLoop = node.GetDataBool("audioLoop");
            state.AudioReset = node.GetDataBool("audioReset");
            bool advanceOnEnd = node.GetDataBool("audioAdvanceOnEnd") && !state.AudioLoop;
            state.AudioAdvanceOnEnd = advanceOnEnd;
            state.AudioAllowSkip = advanceOnEnd && node.GetDataBool("audioAllowSkip");

            // 6. Build text blocks
            state.TextBlocks.Clear();
            var runtimeState = context.GetNodeRuntimeState(node.Id);
            var textBlocksJson = node.GetData("textBlocks");
            if (!string.IsNullOrEmpty(textBlocksJson))
            {
                try
                {
                    var blocks = runtimeState.CachedTextBlocks;
                    if (blocks == null)
                    {
                        blocks = JArray.Parse(textBlocksJson);
                        runtimeState.CachedTextBlocks = blocks;
                    }
                    foreach (var block in blocks)
                    {
                        var blockId = block.Value<string>("id") ?? "";
                        var blockTextKey = block.Value<string>("text") ?? "";
                        string blockRawText = null;
                        if (!string.IsNullOrEmpty(blockTextKey))
                        {
                            blockRawText = context.GetString(component.LanguageCode + "." + blockTextKey);
                        }
                        var interpolated = !string.IsNullOrEmpty(blockRawText)
                            ? StoryFlowInterpolation.Interpolate(blockRawText, context)
                            : "";

                        state.TextBlocks.Add(new StoryFlowTextBlock
                        {
                            Id = blockId,
                            Text = interpolated
                        });
                    }
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"[StoryFlow] Failed to parse textBlocks for node {node.Id}: {e.Message}");
                }
            }

            // 7. Build options
            state.Options.Clear();
            var optionsJson = node.GetData("options");
            if (!string.IsNullOrEmpty(optionsJson))
            {
                try
                {
                    var options = runtimeState.CachedOptions;
                    if (options == null)
                    {
                        options = JArray.Parse(optionsJson);
                        runtimeState.CachedOptions = options;
                    }
                    foreach (var opt in options)
                    {
                        var optionId = opt.Value<string>("id") ?? "";
                        var isOnceOnly = opt.Value<bool?>("onceOnly") ?? opt.Value<bool?>("isOnceOnly") ?? false;
                        var inputType = opt.Value<string>("inputType") ?? "";
                        var defaultValue = opt.Value<string>("defaultValue") ?? "";

                        // Skip if once-only and already used
                        if (isOnceOnly)
                        {
                            var onceOnlyKey = node.Id + "-" + optionId;
                            if (context.IsOnceOnlyUsed(onceOnlyKey))
                                continue;
                        }

                        // Check visibility via evaluator
                        if (!StoryFlowEvaluator.EvaluateOptionVisibility(context, node, optionId))
                            continue;

                        // Resolve option text
                        var optTextKey = opt.Value<string>("text") ?? "";
                        string optRawText = null;
                        if (!string.IsNullOrEmpty(optTextKey))
                        {
                            optRawText = context.GetString(component.LanguageCode + "." + optTextKey);
                        }
                        var optText = !string.IsNullOrEmpty(optRawText)
                            ? StoryFlowInterpolation.Interpolate(optRawText, context)
                            : "";

                        state.Options.Add(new StoryFlowOption
                        {
                            Id = optionId,
                            Text = optText,
                            IsOnceOnly = isOnceOnly,
                            InputType = inputType,
                            DefaultValue = defaultValue
                        });
                    }
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"[StoryFlow] Failed to parse options for node {node.Id}: {e.Message}");
                }
            }

            // 8. CanAdvance: true when no options AND a header edge exists to follow
            if (state.Options.Count == 0)
            {
                var headerHandle = StoryFlowHandles.Source(node.Id, "");
                var headerEdge = context.CurrentScript?.FindEdgeBySourceHandle(headerHandle);
                state.CanAdvance = headerEdge != null;
            }
            else
            {
                state.CanAdvance = false;
            }

            // 9. Mark as valid and waiting for input
            state.IsValid = true;
            context.IsWaitingForInput = true;
            context.ShouldPause = true;

            // 10. Handle audio on fresh entry only
            if (isFreshEntry)
            {
                if (state.Audio != null)
                {
                    component.PlayDialogueAudio(state.Audio, state.AudioLoop);

                    // Set advance-on-end state (non-looped audio that actually played)
                    if (state.AudioAdvanceOnEnd && component.IsDialogueAudioPlaying())
                    {
                        component.SetAudioAdvanceState(true, state.AudioAllowSkip);
                    }
                    else if (state.AudioAdvanceOnEnd && !component.IsDialogueAudioPlaying())
                    {
                        // Audio was expected to play but didn't — clear flags
                        component.SetAudioAdvanceState(false, false);
                    }
                }
                else if (state.AudioReset)
                {
                    component.StopDialogueAudio();
                }
            }

            // 11. Reset the edge entry flag
            context.IsEnteringDialogueViaEdge = false;

            // 12. Notify UI
            component.BroadcastDialogueUpdate();
        }
    }
}
