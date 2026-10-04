using System;
using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution
{
    // A bounded, typed copier. One memo spans execution and shared state so deliberate map
    // aliases survive restoration, but no mutable object aliases the live world or history.
    internal sealed class RollbackCopy
    {
        internal long Bytes;
        private readonly long limit;
        private int depth;
        private readonly Dictionary<object, object> copies = new Dictionary<object, object>();
        internal RollbackCopy(long limit) { this.limit = limit; }
        internal void Charge(long bytes)
        {
            Bytes += bytes;
            if (Bytes > limit) throw new RollbackException("budget");
        }
        internal string Text(string value) { Charge(24L + (value?.Length ?? 0) * 2L); return value; }
        private T Object<T>(T source, Func<T> create, Action<T> fill) where T : class
        {
            if (source == null) return null;
            if (copies.TryGetValue(source, out var existing)) return (T)existing;
            if (++depth > 64) throw new RollbackException("unsupportedState");
            Charge(64);
            var target = create();
            copies.Add(source, target);
            fill(target);
            depth--;
            return target;
        }
        internal List<T> List<T>(List<T> value, Func<T,T> copy) => Object(value,
            () => new List<T>(), target => { foreach (var item in value) { Charge(8); target.Add(copy(item)); } });
        internal Dictionary<string,T> Map<T>(Dictionary<string,T> value, Func<T,T> copy) => Object(value,
            () => new Dictionary<string,T>(), target => { foreach (var pair in value) { Charge(32); target.Add(Text(pair.Key), copy(pair.Value)); } });
        internal StoryFlowVariant Variant(StoryFlowVariant value) => Object(value, () => new StoryFlowVariant(), target =>
        {
            target.Type = value.Type; target.BoolValue = value.BoolValue; target.IntValue = value.IntValue;
            target.FloatValue = value.FloatValue; target.StringValue = Text(value.StringValue);
            target.EnumValue = Text(value.EnumValue); target.IsLiteralString = value.IsLiteralString;
            target.ArrayValue = List(value.ArrayValue, Variant); target.MapValue = List(value.MapValue, Entry);
        });
        private StoryFlowMapEntry Entry(StoryFlowMapEntry value) => Object(value, () => new StoryFlowMapEntry(), target =>
        { target.Key = Variant(value.Key); target.Value = Variant(value.Value); });
        internal StoryFlowVariable Variable(StoryFlowVariable value) => Object(value, () => new StoryFlowVariable(), target =>
        {
            target.Id = Text(value.Id); target.Name = Text(value.Name); target.Type = value.Type;
            target.Value = Variant(value.Value); target.IsArray = value.IsArray; target.Localizable = value.Localizable;
            target.IsInput = value.IsInput; target.IsOutput = value.IsOutput; target.KeyType = value.KeyType; target.ValueType = value.ValueType;
            target.EnumValues = List(value.EnumValues, Text); target.KeyEnumValues = List(value.KeyEnumValues, Text);
            target.ValueEnumValues = List(value.ValueEnumValues, Text); target.DefaultValueJson = Text(value.DefaultValueJson);
        });
        internal StoryFlowCharacterData Character(StoryFlowCharacterData value) => Object(value, () => new StoryFlowCharacterData(), target =>
        {
            target.Name = Text(value.Name); target.NameKey = Text(value.NameKey); target.Image = value.Image;
            target.ImageAssetKey = Text(value.ImageAssetKey); target.VariablesList = List(value.VariablesList, Variable);
            target.Variables = Map(value.Variables, Variant);
        });
        internal NodeRuntimeState Node(NodeRuntimeState value) => Object(value, () => new NodeRuntimeState(), target =>
        {
            target.CachedOutput = Variant(value.CachedOutput); target.HasExecutionOutput = value.HasExecutionOutput;
            target.DetachedMapOutput = Variable(value.DetachedMapOutput); target.LoopIndex = value.LoopIndex;
            target.LoopArray = List(value.LoopArray, Variant); target.LoopMapEntries = List(value.LoopMapEntries, Entry);
            target.LoopKey = Variant(value.LoopKey); target.LoopValue = Variant(value.LoopValue);
            target.OutputValues = Map(value.OutputValues, Variant); target.Dirty = value.Dirty;
            // Parsed authored JSON is a read-only cache, not execution state. Reparse lazily.
        });
        internal LoopContext Loop(LoopContext value) => Object(value, () => new LoopContext(), target =>
        { target.NodeId = Text(value.NodeId); target.CurrentIndex = value.CurrentIndex; target.LoopType = Text(value.LoopType); });
        internal FlowFrame Flow(FlowFrame value) => Object(value, () => new FlowFrame(), target => { target.FlowId = Text(value.FlowId); });
        internal CallFrame Frame(CallFrame value) => Object(value, () => new CallFrame(), target =>
        {
            target.ScriptPath = Text(value.ScriptPath); target.ReturnNodeId = Text(value.ReturnNodeId); target.Script = value.Script;
            target.SavedLocalVariables = Map(value.SavedLocalVariables, Variable); target.SavedFlowStack = List(value.SavedFlowStack, Flow);
            target.SavedLoopStack = List(value.SavedLoopStack, Loop); target.SavedNodeStates = Map(value.SavedNodeStates, Node);
            target.SavedLastDialogueNodeId = Text(value.SavedLastDialogueNodeId);
        });
        internal StoryFlowDialogueState Dialogue(StoryFlowDialogueState value) => Object(value, () => new StoryFlowDialogueState(), target =>
        {
            target.NodeId = Text(value.NodeId); target.Title = Text(value.Title); target.Text = Text(value.Text);
            target.Image = value.Image; target.Audio = value.Audio; target.Character = Character(value.Character);
            target.CharacterReference = Text(value.CharacterReference); target.Tags = List(value.Tags, Text);
            target.IsValid = value.IsValid; target.CanAdvance = value.CanAdvance; target.AudioLoop = value.AudioLoop;
            target.AudioReset = value.AudioReset; target.AudioAdvanceOnEnd = value.AudioAdvanceOnEnd; target.AudioAllowSkip = value.AudioAllowSkip;
            target.Options = List(value.Options, option => new StoryFlowOption { Id = Text(option.Id), Text = Text(option.Text),
                IsOnceOnly = option.IsOnceOnly, IsSelected = option.IsSelected, InputType = Text(option.InputType), DefaultValue = Text(option.DefaultValue) });
            target.TextBlocks = List(value.TextBlocks, block => new StoryFlowTextBlock { Id = Text(block.Id), Text = Text(block.Text) });
        });
    }

    internal sealed class RollbackException : Exception
    {
        internal readonly string Reason;
        internal RollbackException(string reason) : base(reason) { Reason = reason; }
    }

    internal sealed class StoryFlowExecutionSnapshot
    {
        internal const int Version = 1;
        internal ulong Entry;
        internal long Bytes;
        internal StoryFlowProjectAsset Project;
        internal StoryFlowScriptAsset Script;
        internal string Node, LastDialogue, LastSource;
        internal uint RandomState;
        internal List<CallFrame> Calls;
        internal List<FlowFrame> Flows;
        internal List<LoopContext> Loops;
        internal Dictionary<string,NodeRuntimeState> Nodes;
        internal Dictionary<string,StoryFlowVariable> Locals, Globals;
        internal Dictionary<string,StoryFlowCharacterData> Characters;
        internal Dictionary<string,Dictionary<string,StoryFlowVariant>> Overlay;
        internal HashSet<string> Once;
        internal StoryFlowDialogueState Dialogue;
        internal Sprite Background;
        internal AudioClip LoopAudio;
        internal float LoopAudioTime;
    }

    // Only retained during one Back attempt. Historical checkpoints deliberately exclude
    // live pause/advance/delay state and transient voice playback.
    internal sealed class StoryFlowRollbackRecovery
    {
        internal readonly StoryFlowExecutionSnapshot Snapshot;
        private readonly StoryFlowNode nextNode;
        private readonly int evaluationDepth;
        private readonly bool waiting, executing, paused, shouldPause, enteringDialogue;
        internal AudioClip Audio, SourceClip;
        internal float AudioTime;
        internal bool AudioLoop, AudioPlaying, AudioWaiting, AudioAllowSkip;
        internal ulong MediaGeneration, AudioEntry;
        internal StoryFlowRollbackRecovery(StoryFlowExecutionSnapshot snapshot, StoryFlowExecutionContext context, long limit)
        {
            if (snapshot.Bytes + 160 > limit) throw new RollbackException("budget");
            Snapshot = snapshot; nextNode = context.NextNode; evaluationDepth = context.EvaluationDepth;
            waiting = context.IsWaitingForInput; executing = context.IsExecuting; paused = context.IsPaused;
            shouldPause = context.ShouldPause; enteringDialogue = context.EnteringDialogueViaEdge;
        }
        internal void RestoreControl(StoryFlowExecutionContext context)
        {
            context.NextNode = nextNode; context.EvaluationDepth = evaluationDepth;
            context.IsWaitingForInput = waiting; context.IsExecuting = executing; context.IsPaused = paused;
            context.ShouldPause = shouldPause; context.EnteringDialogueViaEdge = enteringDialogue;
        }
    }

    public partial class StoryFlowExecutionContext
    {
        internal uint? RollbackRandomState;
        internal uint NextRollbackRandom()
        {
            uint state = RollbackRandomState ?? 1;
            if (state == 0) state = 1;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            RollbackRandomState = state;
            return state;
        }
        internal int RandomRange(int min, int max) => RollbackRandomState.HasValue
            ? (int)(min + (long)Math.Floor((double)NextRollbackRandom() / 4294967296d * ((long)max - min)))
            : UnityEngine.Random.Range(min, max);
        internal float RandomRange(float min, float max) => RollbackRandomState.HasValue
            ? (float)(min + (double)NextRollbackRandom() / 4294967296d * (max - min))
            : UnityEngine.Random.Range(min, max);

        internal StoryFlowExecutionSnapshot CaptureRollback(ulong entry, StoryFlowManager manager, long limit)
        {
            var copy = new RollbackCopy(limit);
            var result = new StoryFlowExecutionSnapshot { Entry = entry, Project = Project, Script = CurrentScript,
                Node = copy.Text(CurrentNodeId), LastDialogue = copy.Text(LastDialogueNodeId), LastSource = copy.Text(LastSourceHandle),
                RandomState = RollbackRandomState ?? 1, Calls = copy.List(callStack, copy.Frame), Flows = copy.List(flowCallStack, copy.Flow),
                Loops = copy.List(loopStack, copy.Loop), Nodes = copy.Map(nodeRuntimeStates, copy.Node),
                Locals = copy.Map(localVariables, copy.Variable), Globals = copy.Map(externalGlobalVariables, copy.Variable),
                Characters = copy.Map(externalCharacters, copy.Character), Overlay = copy.Map(manager.DataAssetOverlay, values => copy.Map(values, copy.Variant)),
                Dialogue = copy.Dialogue(CurrentDialogueState), Background = PersistentBackgroundImage, Once = new HashSet<string>() };
            foreach (var item in externalUsedOnceOnlyOptions) result.Once.Add(copy.Text(item));
            result.Bytes = copy.Bytes;
            return result;
        }

        internal StoryFlowExecutionSnapshot PrepareRollback(StoryFlowExecutionSnapshot source, StoryFlowManager manager, long limit)
        {
            if (source.Project != Project || source.Script == null || source.Script.GetNode(source.Node) == null)
                throw new RollbackException("restoreFailed");
            foreach (var frame in source.Calls)
                if (frame.Script == null || !frame.Script.TryGetTarget(out var script) ||
                    script == null || script.GetNode(frame.ReturnNodeId) == null || Project.GetScriptByPath(frame.ScriptPath) != script)
                    throw new RollbackException("restoreFailed");
            // Clone first; every staging read uses the detached target state.
            var copy = new RollbackCopy(limit);
            var result = new StoryFlowExecutionSnapshot { Entry = source.Entry, Project = Project, Script = source.Script,
                LoopAudio = source.LoopAudio, LoopAudioTime = source.LoopAudioTime,
                Node = copy.Text(source.Node), LastDialogue = copy.Text(source.LastDialogue), LastSource = copy.Text(source.LastSource),
                RandomState = source.RandomState, Calls = copy.List(source.Calls, copy.Frame), Flows = copy.List(source.Flows, copy.Flow),
                Loops = copy.List(source.Loops, copy.Loop), Nodes = copy.Map(source.Nodes, copy.Node),
                Locals = copy.Map(source.Locals, copy.Variable), Globals = copy.Map(source.Globals, copy.Variable),
                Characters = copy.Map(source.Characters, copy.Character), Overlay = copy.Map(source.Overlay, values => copy.Map(values, copy.Variant)),
                Dialogue = copy.Dialogue(source.Dialogue), Background = source.Background, Once = new HashSet<string>() };
            foreach (var item in source.Once) result.Once.Add(copy.Text(item));
            result.Bytes = copy.Bytes;
            // Resolve only authored presentation strings and retained option identities. No evaluators.
            var node = result.Script.GetNode(result.Node);
            var stage = new StoryFlowExecutionContext { Project = Project, LanguageCode = LanguageCode,
                externalGlobalVariables = result.Globals, externalCharacters = result.Characters,
                externalUsedOnceOnlyOptions = result.Once, externalCharacterIdBridge = manager.CharacterIdBridge,
                DataAssetStore = new StoryFlowDataAssetStoreRef { Seed = manager.DataAssetSeed, Overlay = result.Overlay } };
            stage.ApplyExecutionRollback(result);
            foreach (var character in result.Characters.Values)
                if (!string.IsNullOrEmpty(character.NameKey))
                    character.Name = LookUpLocalizedIn(Project, null, character.NameKey, stage.ActiveLanguageCode) ?? character.NameKey;
            string Resolve(string key) => StoryFlowInterpolation.Interpolate(stage.LookUpLocalized(key) ?? "", stage);
            result.Dialogue.Title = Resolve(node.GetData("title")); result.Dialogue.Text = Resolve(node.GetData("text"));
            var options = node.GetData("options");
            if (!string.IsNullOrEmpty(options))
                foreach (var authored in Newtonsoft.Json.Linq.JArray.Parse(options))
                    foreach (var option in result.Dialogue.Options)
                        if (option.Id == (string)authored["id"]) option.Text = Resolve((string)authored["text"]);
            var blocks = node.GetData("textBlocks");
            if (!string.IsNullOrEmpty(blocks))
                foreach (var authored in Newtonsoft.Json.Linq.JArray.Parse(blocks))
                    foreach (var block in result.Dialogue.TextBlocks)
                        if (block.Id == (string)authored["id"]) block.Text = Resolve((string)authored["text"]);
            result.Dialogue.AudioAdvanceOnEnd = false; result.Dialogue.AudioAllowSkip = false;
            return result;
        }

        internal void ApplyExecutionRollback(StoryFlowExecutionSnapshot value)
        {
            CurrentScript = value.Script; CurrentNodeId = value.Node; LastDialogueNodeId = value.LastDialogue;
            LastSourceHandle = value.LastSource; RollbackRandomState = value.RandomState;
            callStack.Clear(); callStack.AddRange(value.Calls); flowCallStack.Clear(); flowCallStack.AddRange(value.Flows);
            loopStack.Clear(); loopStack.AddRange(value.Loops); nodeRuntimeStates.Clear();
            foreach (var pair in value.Nodes) nodeRuntimeStates.Add(pair.Key, pair.Value);
            localVariables = value.Locals; localVariableNameIndex = null; globalVariableNameIndex = null;
            CurrentDialogueState = value.Dialogue; PersistentBackgroundImage = value.Background;
            NextNode = null; EvaluationDepth = 0; EnteringDialogueViaEdge = false;
            IsWaitingForInput = true; IsExecuting = true; ShouldPause = true; IsPaused = false;
        }
    }
}
