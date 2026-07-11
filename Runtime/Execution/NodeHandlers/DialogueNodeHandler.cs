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

            // Check if this is a fresh entry or returning from a Set* node
            bool isFreshEntry = context.EnteringDialogueViaEdge;
            context.EnteringDialogueViaEdge = false;

            // Dialogue nodes cannot be used inside forEach loops
            var loopCtx = context.PeekLoop();
            if (loopCtx != null)
            {
                component.BroadcastError("Dialogue nodes cannot be used inside ForEach loops.");
                return;
            }

            // Track the last dialogue node so Set* nodes with no outgoing edge can return here
            context.LastDialogueNodeId = node.Id;

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

                    // Re-resolve character image from ImageAssetKey each time.
                    // SetCharacterVar Image updates ImageAssetKey but not the Sprite field,
                    // so we must resolve the current key to keep the UI in sync.
                    if (!string.IsNullOrEmpty(characterData.ImageAssetKey))
                    {
                        var charSprite = component.ResolveAsset<Sprite>(characterData.ImageAssetKey);
                        if (charSprite != null)
                            characterData.Image = charSprite;
                        component.Trace($"CHAR IMAGE \"{characterData.ImageAssetKey}\"");
                    }
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
                component.Trace($"IMAGE \"{imageKey}\"");
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
                component.Trace($"AUDIO \"{audioKey}\"");
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

                        // Check visibility condition (same mechanism as options)
                        if (!StoryFlowEvaluator.EvaluateOptionVisibility(context, node, blockId))
                            continue;

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

            // 7b. Parse tags (optional, additive field). Rebuilt on every render so the
            // read API (state.Tags) reflects the current line; the per-tag event fires only
            // on fresh entry below so a re-render of the same line never re-fires.
            state.Tags.Clear();
            var tagsJson = node.GetData("tags");
            if (!string.IsNullOrEmpty(tagsJson))
            {
                try
                {
                    foreach (var tag in JArray.Parse(tagsJson))
                    {
                        // Only scalar tokens become tags; object/array elements are skipped
                        // (Value<string>() on a JObject/JArray throws InvalidCastException,
                        // which would escape the JsonException catch below).
                        if (tag.Type == JTokenType.Object || tag.Type == JTokenType.Array)
                            continue;
                        // Raw string passed through untouched.
                        state.Tags.Add(tag.Value<string>() ?? "");
                    }
                }
                catch (JsonException e)
                {
                    Debug.LogWarning($"[StoryFlow] Failed to parse tags for node {node.Id}: {e.Message}");
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

            // 10. Handle audio — only on fresh entry (not on re-render from Set* fallthrough).
            // Matches Godot's is_fresh_entry approach: prevents dialogue audio from
            // overwriting PlayAudio clips or restarting on variable-change re-renders.
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

            // 11+12. Notify UI, then fire dialogue tags.
            //
            // Re-entrancy contract: a handler on either broadcast may synchronously
            // advance/select/stop/restart the dialogue. The entered node's tag list is
            // snapshot HERE, before any event fires, so neither an OnDialogueUpdated handler
            // nor an OnDialogueTagReached handler that clears state.Tags (via a re-render) can
            // touch the list being iterated. dialogue-updated is broadcast FIRST (state fully
            // applied) and the whole snapshot then fires even if a handler transitions
            // mid-loop (this may interleave with the next node's events, which is accepted).
            // Iterating a local copy — like HandleForEachMap's entry snapshot — means a
            // mid-loop StopDialogue() can't invalidate the iterator. Tags fire only on fresh
            // entry; re-renders (isFreshEntry == false) never re-fire, revisiting the node
            // later (a new edge entry) fires again, and untagged nodes fire nothing.
            var tagsSnapshot = isFreshEntry ? state.Tags.ToArray() : System.Array.Empty<string>();

            component.BroadcastDialogueUpdate();

            foreach (var tag in tagsSnapshot)
            {
                component.Trace($"TAG \"{tag}\"");
                component.BroadcastDialogueTag(tag);
            }
        }
    }
}
