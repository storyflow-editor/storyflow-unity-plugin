using System;
using System.Collections.Generic;
using System.Reflection;
using StoryFlow;
using StoryFlow.Data;
using StoryFlow.Execution;
using StoryFlow.Lipsync;
using UnityEngine;
using Object = UnityEngine.Object;

// Runs the real component, dialogue handler and graph executor. Only Unity's engine boundary
// is substituted: lifecycle callbacks are invoked explicitly and the scene/audio/mesh state is fake.
internal static class LipsyncComponentTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run(nameof(StoppedLocalSourceDoesNotHideDialoguePlayback), StoppedLocalSourceDoesNotHideDialoguePlayback);
        Run(nameof(StoppedSceneHolderDoesNotCancelDelayedAcquisition), StoppedSceneHolderDoesNotCancelDelayedAcquisition);
        Run(nameof(AudioAcquisitionExpires), AudioAcquisitionExpires);
        Run(nameof(PausedAudioRetainsItsSource), PausedAudioRetainsItsSource);
        Run(nameof(EnableDuringAudioPauseResumesWithTheLine), EnableDuringAudioPauseResumesWithTheLine);
        Run(nameof(RebindDuringAudioPauseResumesWithTheLine), RebindDuringAudioPauseResumesWithTheLine);
        Run(nameof(PlayingCustomSourceWinsOverPausedOwnedSource), PlayingCustomSourceWinsOverPausedOwnedSource);
        Run(nameof(StoppedOwnedAudioDoesNotBlockDelayedPlayback), StoppedOwnedAudioDoesNotBlockDelayedPlayback);
        Run(nameof(PreviousEntryAudioDoesNotBlockDelayedPlayback), PreviousEntryAudioDoesNotBlockDelayedPlayback);
        Run(nameof(EndedAudioDoesNotAcquireAnotherSource), EndedAudioDoesNotAcquireAnotherSource);
        Run(nameof(DestroyedAudioDoesNotAcquireAnotherSource), DestroyedAudioDoesNotAcquireAnotherSource);
        Run(nameof(SameNodeIdInCalledScriptFollowsTheNewAudio), SameNodeIdInCalledScriptFollowsTheNewAudio);
        Run(nameof(FreshSameNodeLoopResetsTheAnalysis), FreshSameNodeLoopResetsTheAnalysis);
        Run(nameof(RedrawPreservesTheAnalysis), RedrawPreservesTheAnalysis);
        Run(nameof(EntrySerialSurvivesStopAndRestart), EntrySerialSurvivesStopAndRestart);
        Run(nameof(ManualAudioSurvivesDialogueEnd), ManualAudioSurvivesDialogueEnd);
        Run(nameof(TextOnlyLineFollowsTheAudioTail), TextOnlyLineFollowsTheAudioTail);
        Run(nameof(DialogueEndClosesAfterTheAudioTail), DialogueEndClosesAfterTheAudioTail);
        Run(nameof(RetainedTailReportsActivity), RetainedTailReportsActivity);
        Run(nameof(AddedFacePartReceivesThePose), AddedFacePartReceivesThePose);
        Run(nameof(LateMeshReceivesThePose), LateMeshReceivesThePose);
        Run(nameof(RemovedSpeakingPartIsReleased), RemovedSpeakingPartIsReleased);
        Run(nameof(PeriodicRefreshPreservesRestingExpressions), PeriodicRefreshPreservesRestingExpressions);
        Run(nameof(RemovedRestingPartKeepsItsExpression), RemovedRestingPartKeepsItsExpression);
        Run(nameof(MeshSwapResolvesNewIndicesImmediately), MeshSwapResolvesNewIndicesImmediately);
        Run(nameof(ReassignedSourceCatchesUpToItsActiveLine), ReassignedSourceCatchesUpToItsActiveLine);
        Run(nameof(DisableAfterReassignmentDetachesTheOriginalSource), DisableAfterReassignmentDetachesTheOriginalSource);
        Run(nameof(RebindingAnInactiveSourcePreservesManualAudio), RebindingAnInactiveSourcePreservesManualAudio);
        Run(nameof(DestroyedDialogueSourceIsRediscovered), DestroyedDialogueSourceIsRediscovered);
        Run(nameof(DisableClosesTheOwnedMouth), DisableClosesTheOwnedMouth);
        Run(nameof(BackReleasesAutomaticMouthUntilFreshSameNodeEntry), BackReleasesAutomaticMouthUntilFreshSameNodeEntry);
        Run(nameof(RestoredLineStaysReleasedAfterEnableAndLateBinding), RestoredLineStaysReleasedAfterEnableAndLateBinding);
        Run(nameof(BackReleasesAudioTailAndPendingAcquisition), BackReleasesAudioTailAndPendingAcquisition);
        Run(nameof(BackPreservesManualAudio), BackPreservesManualAudio);
        Run(nameof(FailedBackPreservesAutomaticOwnership), FailedBackPreservesAutomaticOwnership);
        Run(nameof(AvailabilityCallbackStartsFreshEntry), AvailabilityCallbackStartsFreshEntry);
        Run(nameof(EarlierRestoredListenerStartsFreshEntry), EarlierRestoredListenerStartsFreshEntry);
        Run(nameof(AvailabilityCallbackRestartsSession), AvailabilityCallbackRestartsSession);
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        Object.TestScene.Clear();
        Debug.ClearHandlers();
        SetManager(null);
        Time.unscaledDeltaTime = 1f / 60f;
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { _failed++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e).Message); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    private static void Tick(StoryFlowLipsync lip, int frames = 1)
    {
        for (var i = 0; i < frames; i++) Call(lip, "LateUpdate");
    }

    private sealed class Face : IDisposable
    {
        public readonly StoryFlowComponent Source = NewSource();
        public readonly AudioSource Audio = new AudioSource();
        public readonly Transform Root = new Transform { name = "face" };
        public readonly SkinnedMeshRenderer Head = Part();
        public readonly StoryFlowLipsync Lip;

        public Face()
        {
            Root.TestChildren.Add(Head);
            Source.TestComponents.Add(Audio);
            Set(Source, "_dialogueAudioSource", Audio);
            Object.TestScene.Add(Audio);
            Lip = new StoryFlowLipsync { Source = Source, FaceRoot = Root };
            Call(Lip, "OnEnable");
        }

        public AudioClip Speak()
        {
            var clip = new AudioClip { name = "voice" };
            Source.PlayDialogueAudio(clip, false);
            Broadcast(Source, "line", clip);
            Tick(Lip, 30);
            return clip;
        }

        public void Dispose() => Call(Lip, "OnDisable");
    }

    private static SkinnedMeshRenderer Part() => new SkinnedMeshRenderer { sharedMesh = new Mesh { TestNames = new[] { "jawOpen" } } };
    private static StoryFlowComponent NewSource()
    {
        var source = new StoryFlowComponent { TraceEnabled = false, UIStyle = BuiltInUIStyle.None };
        Set(source, "_context", new StoryFlowExecutionContext { CurrentDialogueState = new StoryFlowDialogueState() });
        return source;
    }

    private static void Broadcast(StoryFlowComponent source, string node, AudioClip clip)
    {
        source.GetContext().CurrentDialogueState = new StoryFlowDialogueState { NodeId = node, Audio = clip, IsValid = true };
        source.BroadcastDialogueUpdate();
    }

    private static void StoppedLocalSourceDoesNotHideDialoguePlayback()
    {
        using var f = new Face();
        var clip = new AudioClip();
        f.Source.TestComponents.Insert(0, new AudioSource { clip = clip });
        f.Source.PlayDialogueAudio(clip, false);
        Broadcast(f.Source, "line", clip);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0 && f.Head.GetBlendShapeWeight(0) > 0, "playing default dialogue source must open the mouth");
    }

    private static void StoppedSceneHolderDoesNotCancelDelayedAcquisition()
    {
        using var f = new Face();
        var clip = new AudioClip();
        Object.TestScene.Insert(0, new AudioSource { clip = clip });
        Broadcast(f.Source, "line", clip);
        Tick(f.Lip, 5);
        f.Source.PlayDialogueAudio(clip, false);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "audio started after the dialogue event must be acquired despite a stopped holder");
    }

    private static void AudioAcquisitionExpires()
    {
        using var f = new Face();
        var clip = new AudioClip();
        var warnings = 0;
        Debug.OnWarning += _ => warnings++;
        Broadcast(f.Source, "line", clip);
        Tick(f.Lip, 120);
        f.Source.PlayDialogueAudio(clip, false);
        Tick(f.Lip, 120);
        Require(f.Lip.Level == 0 && warnings == 1, "late unrelated playback must stay silent after one bounded warning");
    }

    private static AudioSource OtherPlaying(AudioClip clip)
    {
        var source = new AudioSource { clip = clip };
        source.Play();
        Object.TestScene.Add(source);
        return source;
    }

    private static void PausedAudioRetainsItsSource()
    {
        using var f = new Face();
        var clip = f.Speak();
        f.Audio.Pause();
        OtherPlaying(clip);
        Tick(f.Lip, 90);
        Require(f.Lip.Level == 0, "paused mouth must not follow a different source holding the same clip");
        f.Audio.UnPause();
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "resuming the original source must resume the mouth");
    }

    private static void EndedAudioDoesNotAcquireAnotherSource()
    {
        using var f = new Face();
        var clip = f.Speak();
        f.Audio.Stop();
        OtherPlaying(clip);
        Tick(f.Lip, 90);
        Require(f.Lip.Level == 0, "finished line must not switch to another later playback");
    }

    private static void EnableDuringAudioPauseResumesWithTheLine()
    {
        using var f = new Face();
        var script = Script("line.sfe", new AudioClip());
        InstallScripts(script);
        f.Source.StartDialogue(script);
        f.Audio.Pause();
        Call(f.Lip, "OnDisable");
        Call(f.Lip, "OnEnable");
        Tick(f.Lip, 120);
        Require(f.Lip.Level == 0, "enabling during a pause must leave the mouth still");
        f.Audio.UnPause();
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "the already active line must resume even after initial acquisition would expire");
    }

    private static void RebindDuringAudioPauseResumesWithTheLine()
    {
        using var f = new Face();
        var script = Script("line.sfe", new AudioClip());
        InstallScripts(script);
        var replacement = NewSource();
        var audio = new AudioSource();
        replacement.TestComponents.Add(audio);
        Object.TestScene.Add(audio);
        Set(replacement, "_dialogueAudioSource", audio);
        replacement.StartDialogue(script);
        audio.Pause();
        f.Lip.Source = replacement;
        Tick(f.Lip, 120);
        audio.UnPause();
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "a replacement's paused active line must resume without a new dialogue entry");
    }

    private static void StoppedOwnedAudioDoesNotBlockDelayedPlayback()
    {
        using var f = new Face();
        var clip = new AudioClip();
        var script = Script("line.sfe", clip);
        InstallScripts(script);
        f.Source.StartDialogue(script);
        f.Audio.Pause();
        f.Source.StopDialogueAudio();
        Call(f.Lip, "OnDisable");
        Call(f.Lip, "OnEnable");
        Tick(f.Lip, 5);
        OtherPlaying(clip);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "explicitly stopped built-in playback must not block a later custom source");
    }

    private static void PlayingCustomSourceWinsOverPausedOwnedSource()
    {
        using var f = new Face();
        var clip = new AudioClip();
        var script = Script("line.sfe", clip);
        InstallScripts(script);
        f.Source.StartDialogue(script);
        f.Audio.Pause();
        OtherPlaying(clip);
        Call(f.Lip, "OnDisable");
        Call(f.Lip, "OnEnable");
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "actively playing custom audio must take precedence over a paused built-in source");
    }

    private static void PreviousEntryAudioDoesNotBlockDelayedPlayback()
    {
        using var f = new Face();
        var clip = new AudioClip();
        var script = Script("line.sfe", clip);
        script.SetNodes(new List<StoryFlowScriptAsset.SerializedNode>
        {
            Node("0", StoryFlowNodeType.Start), Node("1", StoryFlowNodeType.Dialogue, ("audio", "voice")),
            Node("2", StoryFlowNodeType.Dialogue)
        });
        script.SetConnections(new List<StoryFlowConnection> { Edge("0", "1"), Edge("1", "2") });
        InstallScripts(script);
        f.Source.StartDialogue(script);
        f.Audio.Pause();
        Call(f.Lip, "OnDisable");
        // A game's earlier listener supplies the next line's voice and starts it asynchronously.
        f.Source.OnDialogueUpdated += state => { if (state.NodeId == "2") state.Audio = clip; };
        Call(f.Lip, "OnEnable");
        f.Source.AdvanceDialogue();
        Tick(f.Lip, 5);
        OtherPlaying(clip);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "the prior entry's paused default source must not claim a new entry's delayed audio");
    }

    private static void DestroyedAudioDoesNotAcquireAnotherSource()
    {
        using var f = new Face();
        var clip = f.Speak();
        Object.Destroy(f.Audio);
        OtherPlaying(clip);
        Tick(f.Lip, 30);
        Require(f.Lip.Level == 0, "destroying an acquired source must not reopen initial acquisition");
    }

    private static StoryFlowScriptAsset.SerializedNode Node(string id, StoryFlowNodeType type, params (string, string)[] data)
    {
        var node = new StoryFlowScriptAsset.SerializedNode { Id = id, Type = type };
        foreach (var pair in data) node.Data.Add(new StoryFlowScriptAsset.SerializedKV { Key = pair.Item1, Value = pair.Item2 });
        return node;
    }

    private static StoryFlowConnection Edge(string from, string to) => new StoryFlowConnection
    {
        Id = from + "-" + to, Source = from, Target = to,
        SourceHandle = StoryFlowHandles.Source(from), TargetHandle = "target-" + to
    };

    private static StoryFlowScriptAsset Script(string path, AudioClip clip, bool childCall = false)
    {
        var script = new StoryFlowScriptAsset { ScriptPath = path, StartNodeId = "0" };
        var nodes = new List<StoryFlowScriptAsset.SerializedNode> { Node("0", StoryFlowNodeType.Start), Node("1", StoryFlowNodeType.Dialogue, ("audio", "voice")) };
        var edges = new List<StoryFlowConnection> { Edge("0", "1") };
        if (childCall)
        {
            nodes.Add(Node("9", StoryFlowNodeType.RunScript, ("script", "child.sfe")));
            edges.Add(Edge("1", "9"));
        }
        else edges.Add(Edge("1", "1"));
        script.SetNodes(nodes);
        script.SetConnections(edges);
        script.ResolvedAssetEntries.Add(new StoryFlowScriptAsset.ResolvedAssetEntry { Key = "voice", Asset = clip });
        return script;
    }

    private static void SetManager(StoryFlowManager manager) => typeof(StoryFlowManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { manager });
    private static void InstallScripts(params StoryFlowScriptAsset[] scripts)
    {
        var project = new StoryFlowProjectAsset();
        foreach (var script in scripts) project.Scripts[script.ScriptPath] = script;
        var manager = new StoryFlowManager();
        SetManager(manager);
        manager.SetProject(project);
    }

    private static void SameNodeIdInCalledScriptFollowsTheNewAudio()
    {
        using var f = new Face();
        var parent = Script("parent.sfe", new AudioClip(), true);
        var child = Script("child.sfe", new AudioClip());
        InstallScripts(parent, child);
        f.Source.StartDialogue(parent);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "parent fixture must produce analysable speech");
        f.Source.AdvanceDialogue();
        Tick(f.Lip, 30);
        Require(f.Source.GetContext().CurrentScript == child && f.Lip.Level > 0, "child line sharing node id 1 must follow its new audio");
    }

    private static void FreshSameNodeLoopResetsTheAnalysis()
    {
        using var f = new Face();
        var script = Script("loop.sfe", new AudioClip());
        InstallScripts(script);
        f.Source.StartDialogue(script);
        Tick(f.Lip, 30);
        Require(f.Lip.RawPeak > 0, "fixture must have measured speech");
        f.Source.AdvanceDialogue();
        Require(f.Lip.RawPeak == 0, "fresh edge entry into the same node must begin a new analysis");
    }

    private static void RedrawPreservesTheAnalysis()
    {
        using var f = new Face();
        var script = Script("line.sfe", new AudioClip());
        InstallScripts(script);
        f.Source.StartDialogue(script);
        Tick(f.Lip, 30);
        var peak = f.Lip.RawPeak;
        StoryFlow.Execution.NodeHandlers.DialogueNodeHandler.Handle(f.Source, script.GetNode("1"));
        Require(peak > 0 && f.Lip.RawPeak == peak, "a redraw of the displayed line must not reset its peak follower");
    }

    private static void EntrySerialSurvivesStopAndRestart()
    {
        using var f = new Face();
        var script = Script("line.sfe", new AudioClip());
        InstallScripts(script);
        var property = typeof(StoryFlowComponent).GetProperty("DialogueEntrySerial");
        Require(property != null, "fresh dialogue entries need component-lifetime identity");
        f.Source.StartDialogue(script);
        var first = (ulong)property.GetValue(f.Source);
        f.Source.StopDialogue();
        f.Source.StartDialogue(script);
        Require(first > 0 && (ulong)property.GetValue(f.Source) > first, "stopping and creating a new context must not reset entry identity");
    }

    private static void ManualAudioSurvivesDialogueEnd()
    {
        using var f = new Face();
        f.Audio.Play();
        f.Lip.StartLipsyncFor(f.Audio);
        Call(f.Lip, "HandleDialogueEnded");
        Tick(f.Lip, 30);
        Require(f.Lip.IsLipsyncActive && f.Lip.Level > 0, "dialogue end must not cancel manual playback");
    }

    private static void TextOnlyLineFollowsTheAudioTail()
    {
        using var f = new Face();
        f.Speak();
        Broadcast(f.Source, "text-only", null);
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "text-only line must keep analysing the previous audible tail");
        f.Audio.Stop();
        Tick(f.Lip, 30);
        Require(f.Lip.IsLipsyncActive && f.Lip.Level == 0, "text-only line may idle once the audio tail ends");
    }

    private static void DialogueEndClosesAfterTheAudioTail()
    {
        using var f = new Face();
        f.Speak();
        Broadcast(f.Source, "text-only", null);
        Call(f.Lip, "HandleDialogueEnded");
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "dialogue end must retain audible tail analysis");
        f.Audio.Stop();
        Tick(f.Lip, 180);
        Require(!f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) == 0, "ended tail must close without entering text-only idle");
    }

    private static void AddedFacePartReceivesThePose()
    {
        using var f = new Face();
        f.Speak();
        var teeth = Part();
        f.Root.TestChildren.Add(teeth);
        Tick(f.Lip, 90);
        Require(teeth.GetBlendShapeWeight(0) > 0, "compatible teeth added to an already resolved face must move");
    }

    private static void RetainedTailReportsActivity()
    {
        using var f = new Face();
        f.Speak();
        Call(f.Lip, "HandleDialogueEnded");
        Require(f.Lip.IsLipsyncActive, "an audible retained tail must report active lipsync");
        f.Audio.Stop();
        Require(!f.Lip.IsLipsyncActive, "the tail must report inactive as soon as its audio stops");
    }

    private static void LateMeshReceivesThePose()
    {
        using var f = new Face();
        var teeth = new SkinnedMeshRenderer();
        f.Root.TestChildren.Add(teeth);
        f.Speak();
        teeth.sharedMesh = new Mesh { TestNames = new[] { "jawOpen" } };
        Tick(f.Lip, 90);
        Require(teeth.GetBlendShapeWeight(0) > 0, "late mesh assignment must join the existing face");
    }

    private static void RemovedSpeakingPartIsReleased()
    {
        using var f = new Face();
        f.Speak();
        Require(f.Head.GetBlendShapeWeight(0) > 0, "fixture jaw must start open");
        f.Root.TestChildren.Clear();
        Tick(f.Lip, 90);
        Require(f.Head.GetBlendShapeWeight(0) == 0, "a live part removed or reparented away must release the owned pose");
    }

    private static void PeriodicRefreshPreservesRestingExpressions()
    {
        using var f = new Face();
        Tick(f.Lip);
        f.Head.SetBlendShapeWeight(0, 42f);
        Tick(f.Lip, 180);
        Require(f.Head.GetBlendShapeWeight(0) == 42f, "periodic discovery must not reclaim shapes surrendered at rest");
    }

    private static void RemovedRestingPartKeepsItsExpression()
    {
        using var f = new Face();
        Tick(f.Lip);
        f.Head.SetBlendShapeWeight(0, 42f);
        f.Root.TestChildren.Clear();
        Tick(f.Lip, 90);
        Require(f.Head.GetBlendShapeWeight(0) == 42f, "removing a surrendered target must not zero another animator's expression");
    }

    private static void MeshSwapResolvesNewIndicesImmediately()
    {
        using var f = new Face();
        f.Speak();
        f.Head.sharedMesh = new Mesh { TestNames = new[] { "expression", "jawOpen" } };
        f.Head.SetBlendShapeWeight(0, 42f);
        Tick(f.Lip);
        Require(f.Head.GetBlendShapeWeight(1) > 0 && f.Head.GetBlendShapeWeight(0) == 42f, "mesh swap must use new jaw index without touching the old index's new shape");
    }

    private static void ReassignedSourceCatchesUpToItsActiveLine()
    {
        using var f = new Face();
        var replacement = NewSource();
        var audio = OtherPlaying(new AudioClip());
        replacement.TestComponents.Add(audio);
        Broadcast(replacement, "replacement", audio.clip);
        Set(replacement, "_isDialogueActive", true);
        f.Lip.Source = replacement;
        Tick(f.Lip, 90);
        Require(f.Lip.Level > 0, "replacement source's already active line must be picked up without another event");
        f.Source.BroadcastDialogueUpdate();
        Tick(f.Lip);
        Require(f.Lip.Level > 0, "old source must no longer control the face");
    }

    private static void DisableAfterReassignmentDetachesTheOriginalSource()
    {
        using var f = new Face();
        f.Lip.Source = NewSource();
        Call(f.Lip, "OnDisable");
        Broadcast(f.Source, "old-source", null);
        Require(!f.Lip.IsLipsyncActive, "disabled face must not retain the original source callback");
    }

    private static void RebindingAnInactiveSourcePreservesManualAudio()
    {
        using var f = new Face();
        f.Audio.Play();
        f.Lip.StartLipsyncFor(f.Audio);
        f.Lip.Source = NewSource();
        Tick(f.Lip, 90);
        Require(f.Lip.IsLipsyncActive && f.Lip.Level > 0, "rebinding dialogue discovery must not cancel explicit manual audio");
    }

    private static void DestroyedDialogueSourceIsRediscovered()
    {
        using var f = new Face();
        Object.Destroy(f.Source);
        var replacement = NewSource();
        var audio = OtherPlaying(new AudioClip());
        replacement.TestComponents.Add(audio);
        Broadcast(replacement, "replacement", audio.clip);
        Set(replacement, "_isDialogueActive", true);
        Object.TestScene.Add(replacement);
        Tick(f.Lip, 90);
        Require(f.Lip.Source == replacement && f.Lip.Level > 0, "destroyed dialogue component must rediscover and catch up to its replacement");
    }

    private static void DisableClosesTheOwnedMouth()
    {
        using var f = new Face();
        f.Speak();
        Call(f.Lip, "OnDisable");
        Require(f.Head.GetBlendShapeWeight(0) == 0, "disabling a speaking component must release its open jaw immediately");
    }

    private static StoryFlowScriptAsset StartRollbackLoop(Face f, AudioClip clip = null)
    {
        var script = Script("rollback.sfe", clip);
        if (clip == null) script.SetNodes(new List<StoryFlowScriptAsset.SerializedNode>
        { Node("0", StoryFlowNodeType.Start), Node("1", StoryFlowNodeType.Dialogue) });
        InstallScripts(script);
        StoryFlowManager.Instance.Project.DialogueRollback = new StoryFlowRollbackSettings { Enabled = true };
        f.Source.StartDialogue(script);
        f.Source.AdvanceDialogue();
        Require(f.Source.CanGoBack(), "fixture must retain two real dialogue entries");
        return script;
    }

    private static void BackReleasesAutomaticMouthUntilFreshSameNodeEntry()
    {
        using var f = new Face();
        var script = StartRollbackLoop(f);
        Tick(f.Lip, 90);
        Require(f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) > 0, "text-only dialogue must animate before Back");
        int normal = 0, restored = 0;
        f.Source.OnDialogueUpdated += _ => normal++;
        f.Source.OnDialogueRestored += _ => restored++;
        Require(f.Source.GoBack().Ok, "real Back must succeed");
        Require(!f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) == 0 && f.Lip.Level == 0,
            "Back must release ownership and close the mouth synchronously");
        Require(normal == 0 && restored == 1, "Back must emit only the dedicated restored event");
        f.Head.SetBlendShapeWeight(0, 42f);
        StoryFlow.Execution.NodeHandlers.DialogueNodeHandler.Handle(f.Source, script.GetNode("1"));
        Tick(f.Lip, 180);
        Require(!f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) == 42f, "restored redraw must surrender writes to host expressions");
        f.Source.AdvanceDialogue(); Tick(f.Lip, 90);
        Require(f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) > 0, "fresh same-node traversal must resume automatic lipsync");
    }

    private static void RestoredLineStaysReleasedAfterEnableAndLateBinding()
    {
        using var f = new Face(); StartRollbackLoop(f);
        Require(f.Source.GoBack().Ok, "Back must succeed");
        Call(f.Lip, "OnDisable"); Call(f.Lip, "OnEnable"); Tick(f.Lip, 90);
        Require(!f.Lip.IsLipsyncActive && f.Head.GetBlendShapeWeight(0) == 0, "re-enable must not adopt a revealed restored line");
        var late = new StoryFlowLipsync { Source = f.Source, FaceRoot = f.Root };
        try
        {
            Call(late, "OnEnable"); Tick(late, 90);
            Require(!late.IsLipsyncActive, "newly spawned consumer must recognize the restored entry");
            var handlers = (Delegate)typeof(StoryFlowComponent).GetField("OnDialogueRestored", Private).GetValue(f.Source);
            Require(Array.FindAll(handlers.GetInvocationList(), h => h.Target == late).Length == 1, "restored handler must bind once");
            Call(late, "OnDisable");
            handlers = (Delegate)typeof(StoryFlowComponent).GetField("OnDialogueRestored", Private).GetValue(f.Source);
            Require(Array.FindAll(handlers.GetInvocationList(), h => h.Target == late).Length == 0, "disable must detach restored handler");
        }
        finally { Call(late, "OnDisable"); }
    }

    private static void BackReleasesAudioTailAndPendingAcquisition()
    {
        using var f = new Face();
        var clip = new AudioClip(); StartRollbackLoop(f, clip);
        var custom = OtherPlaying(clip);
        Call(f.Lip, "OnDisable"); Call(f.Lip, "OnEnable");
        Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "fixture must analyse speech");
        Require(f.Source.GoBack().Ok, "Back must succeed");
        Require(custom.isPlaying && !f.Lip.IsLipsyncActive && f.Lip.Level == 0, "Back must release automatic audio even while custom tail plays");
        Require(typeof(StoryFlowLipsync).GetField("_speaking", Private).GetValue(f.Lip) == null &&
            typeof(StoryFlowLipsync).GetField("_lineClip", Private).GetValue(f.Lip) == null,
            "Back must release source and clip references");
        f.Source.AdvanceDialogue(); f.Source.StopDialogueAudio(); custom.Stop();
        Call(f.Lip, "OnDisable"); Call(f.Lip, "OnEnable");
        Require((bool)typeof(StoryFlowLipsync).GetField("_awaitingAudioSource", Private).GetValue(f.Lip), "fixture must search for delayed voice");
        Require(f.Source.GoBack().Ok, "second Back must succeed");
        custom.Play(); Tick(f.Lip, 90);
        Require(!f.Lip.IsLipsyncActive && !(bool)typeof(StoryFlowLipsync).GetField("_awaitingAudioSource", Private).GetValue(f.Lip), "restored line must cancel pending audio search");
    }

    private static void BackPreservesManualAudio()
    {
        using var f = new Face(); StartRollbackLoop(f);
        var manual = OtherPlaying(new AudioClip()); f.Lip.StartLipsyncFor(manual); Tick(f.Lip, 30);
        Require(f.Source.GoBack().Ok, "Back must succeed"); Tick(f.Lip, 30);
        Require(f.Lip.IsLipsyncActive && f.Lip.Level > 0 && manual.isPlaying, "Back must preserve explicit manual lipsync");
        f.Source.BroadcastDialogueUpdate(); Tick(f.Lip, 30);
        Require(f.Lip.Level > 0, "redraw of restored line must not replace manual playback");
    }

    private static void FailedBackPreservesAutomaticOwnership()
    {
        using var f = new Face(); StartRollbackLoop(f, new AudioClip()); Tick(f.Lip, 30);
        var source = typeof(StoryFlowLipsync).GetField("_speaking", Private).GetValue(f.Lip);
        int restored = 0; f.Source.OnDialogueRestored += _ => restored++;
        StoryFlowManager.CommitFault = () => { StoryFlowManager.CommitFault = null; throw new InvalidOperationException("lipsync recovery probe"); };
        try
        {
            Require(!f.Source.GoBack().Ok, "fault seam must fail commit"); Tick(f.Lip, 30);
            Require(restored == 0 && f.Lip.IsLipsyncActive && f.Lip.Level > 0 &&
                ReferenceEquals(source, typeof(StoryFlowLipsync).GetField("_speaking", Private).GetValue(f.Lip)),
                "failed Back must recover audio without releasing automatic lipsync");
        }
        finally { StoryFlowManager.CommitFault = null; }
    }

    private static void AvailabilityCallbackStartsFreshEntry()
    {
        using var f = new Face(); StartRollbackLoop(f);
        bool advanced = false;
        f.Source.OnRollbackAvailabilityChanged += availability => {
            if (!availability.CanGoBack && !advanced) { advanced = true; f.Source.AdvanceDialogue(); }
        };
        Require(f.Source.GoBack().Ok, "Back succeeds");
        Require(advanced && !f.Source.IsCurrentDialogueRestored, "availability listener entered fresh entry");
        Require(f.Lip.IsLipsyncActive, "fresh entry from availability callback must retain automatic lipsync");
    }

    private static void EarlierRestoredListenerStartsFreshEntry()
    {
        using var f = new Face(); StartRollbackLoop(f);
        Call(f.Lip, "OnDisable");
        f.Source.OnDialogueRestored += _ => f.Source.AdvanceDialogue();
        Call(f.Lip, "OnEnable");
        Require(f.Source.GoBack().Ok, "Back succeeds");
        Require(!f.Source.IsCurrentDialogueRestored, "earlier restored listener entered fresh entry");
        Require(f.Lip.IsLipsyncActive, "fresh entry from earlier restored listener must retain automatic lipsync");
    }

    private static void AvailabilityCallbackRestartsSession()
    {
        using var f = new Face(); var script = StartRollbackLoop(f);
        bool restarted = false;
        f.Source.OnRollbackAvailabilityChanged += availability => {
            if (!availability.CanGoBack && !restarted) { restarted = true; f.Source.StopDialogue(); f.Source.StartDialogue(script); }
        };
        Require(f.Source.GoBack().Ok, "Back succeeds");
        Require(restarted && f.Source.IsDialogueActive() && !f.Source.IsCurrentDialogueRestored, "availability listener started new session");
        Require(f.Lip.IsLipsyncActive, "replacement session must retain automatic lipsync");
    }
}
