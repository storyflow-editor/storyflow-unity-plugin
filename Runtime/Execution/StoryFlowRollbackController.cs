using System;
using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    internal sealed class StoryFlowRollbackController
    {
        internal const long PayloadLimit = 32L * 1024 * 1024;
        private readonly StoryFlowComponent component;
        private readonly StoryFlowManager manager;
        private readonly int historyLimit;
        private readonly List<StoryFlowExecutionSnapshot> history = new List<StoryFlowExecutionSnapshot>();
        private long bytes;
        private ulong blockedEntry;
        private bool hasBlockedEntry, contentChanged, restoring, notifyingRestore, invalidTarget;
        private int executionDepth;
        private string reason = "empty";
        private StoryFlowRollbackAvailability published;
        internal long Captures { get; private set; }
        internal long EstimatedBytes => bytes;
        internal double LastCaptureMilliseconds { get; private set; }
        internal double LastPrepareMilliseconds { get; private set; }
        internal double LastCommitMilliseconds { get; private set; }
        private static double Milliseconds(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        internal bool IsBusy => restoring || executionDepth > 0;
        internal StoryFlowRollbackController(StoryFlowComponent component, StoryFlowManager manager, int limit)
        { this.component = component; this.manager = manager; historyLimit = limit; }

        internal StoryFlowRollbackAvailability Availability()
        {
            string unavailable = contentChanged ? "contentChanged" : !manager.OwnsRollback(this) ? "multipleSessions" :
                manager.RollbackMutationActive ? "barrier" : IsBusy ? "busy" : invalidTarget ? reason : history.Count < 2 ? reason : null;
            return new StoryFlowRollbackAvailability(unavailable == null, unavailable == null ? history.Count - 1 : 0, unavailable);
        }
        internal void EnterExecution() { executionDepth++; }
        internal void ExitExecution()
        {
            executionDepth--;
            if (executionDepth == 0) Capture();
        }
        internal void Capture()
        {
            if (contentChanged || invalidTarget || restoring || manager.RollbackMutationActive || executionDepth > 0 || !manager.OwnsRollback(this) || !component.IsDialogueActive()) return;
            var context = component.GetContext();
            if (!context.IsWaitingForInput || context.CurrentDialogueState?.IsValid != true) return;
            ulong entry = component.DialogueEntrySerial;
            if ((hasBlockedEntry && blockedEntry == entry) || (history.Count > 0 && history[history.Count - 1].Entry == entry)) return;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var snapshot = context.CaptureRollback(entry, manager, PayloadLimit);
                component.CaptureRollbackMedia(snapshot);
                history.Add(snapshot); bytes += snapshot.Bytes; Captures++;
                while (history.Count > historyLimit + 1 || bytes > PayloadLimit)
                { bytes -= history[0].Bytes; history.RemoveAt(0); }
                reason = "empty";
            }
            catch (Exception error)
            {
                Clear(error is RollbackException known ? known.Reason : "unsupportedState");
                Debug.LogWarning("[StoryFlow] Rollback capture failed: " + reason);
            }
            LastCaptureMilliseconds = Milliseconds(start);
            Publish();
        }
        internal void BeforeLeave()
        {
            // Leaving a reentrant or otherwise uncaptured interaction must never skip it.
            if (history.Count > 0 && history[history.Count - 1].Entry != component.DialogueEntrySerial) Clear("busy");
        }
        internal void Clear(string why, bool permanent = false)
        {
            history.Clear(); bytes = 0; reason = why; blockedEntry = component.DialogueEntrySerial; hasBlockedEntry = true;
            contentChanged |= permanent; invalidTarget = false;
            Publish();
        }
        internal StoryFlowRollbackResult GoBack()
        {
            if (notifyingRestore) return new StoryFlowRollbackResult(false, "busy");
            var available = Availability();
            if (!available.CanGoBack) return new StoryFlowRollbackResult(false, available.Reason);
            restoring = true;
            StoryFlowRollbackRecovery recovery = null;
            var context = component.GetContext();
            try
            {
                long prepareStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var prepared = context.PrepareRollback(history[history.Count - 2], manager, PayloadLimit);
                recovery = new StoryFlowRollbackRecovery(context.CaptureRollback(component.DialogueEntrySerial, manager, PayloadLimit), context, PayloadLimit);
                component.CaptureRollbackRecovery(recovery);
                if (!manager.OwnsRollback(this) || contentChanged) throw new RollbackException("contentChanged");
                LastPrepareMilliseconds = Milliseconds(prepareStart);
                long commitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                component.CancelRollbackMedia();
                manager.CommitRollback(prepared, context);
                component.RestoreRollbackMedia(prepared);
                LastCommitMilliseconds = Milliseconds(commitStart);
                bytes -= history[history.Count - 1].Bytes; history.RemoveAt(history.Count - 1);
                // Keep component identity monotonic; future same-node entries remain distinct.
                component.DialogueEntrySerial++;
                history[history.Count - 1].Entry = component.DialogueEntrySerial;
                component.MarkDialogueRestored();
            }
            catch (Exception error)
            {
                if (recovery != null)
                {
                    try
                    {
                        manager.CommitRollback(recovery.Snapshot, context);
                        recovery.RestoreControl(context);
                        component.RestoreRollbackRecovery(recovery);
                    }
                    catch (Exception recoveryError) { Debug.LogException(recoveryError); component.StopDialogue(); }
                }
                restoring = false;
                reason = error is RollbackException known ? known.Reason : "restoreFailed";
                invalidTarget = true; Publish();
                Debug.LogWarning("[StoryFlow] Rollback restore failed: " + reason);
                return new StoryFlowRollbackResult(false, reason);
            }
            restoring = false;
            notifyingRestore = true;
            var restoredState = context.CurrentDialogueState;
            var restoredEntry = component.DialogueEntrySerial;
            var restoredSession = component.DialogueLifecycleGeneration;
            try { Publish(); component.PublishDialogueRestored(this, restoredSession, restoredEntry, restoredState); }
            finally { notifyingRestore = false; }
            return new StoryFlowRollbackResult(true);
        }
        internal void Publish()
        {
            var current = Availability();
            if (published.CanGoBack == current.CanGoBack && published.Steps == current.Steps && published.Reason == current.Reason) return;
            published = current; component.PublishRollbackAvailability(current);
        }
    }
}

namespace StoryFlow
{
    public partial class StoryFlowManager
    {
        // Null throughout disabled playback. Public setters never carry this token, including
        // when invoked synchronously by an event emitted from an executing story node.
        private HashSet<Execution.StoryFlowRollbackController> rollbackOwners;
        private int rollbackMutationDepth, rollbackContentUpdateDepth;
        internal bool RollbackMutationActive => rollbackMutationDepth > 0;
        internal void RegisterRollback(Execution.StoryFlowRollbackController owner)
        {
            (rollbackOwners ??= new HashSet<Execution.StoryFlowRollbackController>()).Add(owner);
            if (rollbackContentUpdateDepth > 0) owner.Clear("contentChanged", true);
            else if (RollbackMutationActive) owner.Clear("barrier");
        }
        internal void UnregisterRollback(Execution.StoryFlowRollbackController owner) { rollbackOwners?.Remove(owner); }
        internal bool OwnsRollback(Execution.StoryFlowRollbackController owner) => _activeDialogueCount == 1 && rollbackOwners?.Contains(owner) == true;
        internal void InvalidateRollback(string reason = "barrier", bool permanent = false)
        {
            if (rollbackOwners == null) return;
            foreach (var owner in new List<Execution.StoryFlowRollbackController>(rollbackOwners)) owner.Clear(reason, permanent);
        }
        internal IDisposable BeginHostMutation()
        {
            if (rollbackOwners == null || rollbackOwners.Count == 0) return null;
            rollbackMutationDepth++;
            InvalidateRollback();
            return new RollbackMutationScope(this, false);
        }
        private sealed class RollbackMutationScope : IDisposable
        {
            private StoryFlowManager manager;
            private readonly bool content;
            internal RollbackMutationScope(StoryFlowManager manager, bool content) { this.manager = manager; this.content = content; }
            public void Dispose()
            {
                if (manager == null) return;
                var current = manager; manager = null;
                if (content) current.rollbackContentUpdateDepth--;
                else
                {
                    // Publication can itself replace an owner. Keep manager exclusion active
                    // until all notifications finish, including registrations made by them.
                    try { current.InvalidateRollback(); }
                    finally { current.rollbackMutationDepth--; }
                }
            }
        }
        /// <summary>Excludes every session started until the imported replacement finishes.</summary>
        public IDisposable BeginContentUpdate(StoryFlowProjectAsset project)
        {
            if (project == null || project != Project) return null;
            rollbackContentUpdateDepth++;
            InvalidateRollback("contentChanged", true);
            return new RollbackMutationScope(this, true);
        }
        /// <summary>Called before replacing imported graph objects during live content updates.</summary>
        public void NotifyContentChanging(StoryFlowProjectAsset project)
        { if (project == Project) InvalidateRollback("contentChanged", true); }

#if STORYFLOW_ROLLBACK_TESTS
        internal static Action CommitFault;
#endif
        internal void CommitRollback(Execution.StoryFlowExecutionSnapshot state, Execution.StoryFlowExecutionContext context)
        {
            GlobalVariables.Clear(); foreach (var item in state.Globals) GlobalVariables.Add(item.Key, item.Value);
#if STORYFLOW_ROLLBACK_TESTS
            CommitFault?.Invoke();
#endif
            // Character records are public references held by native portraits and host code.
            // Keep each surviving record and its collection roots alive while applying values.
            foreach (var key in new List<string>(RuntimeCharacters.Keys))
                if (!state.Characters.ContainsKey(key)) RuntimeCharacters.Remove(key);
            foreach (var item in state.Characters)
            {
                if (!RuntimeCharacters.TryGetValue(item.Key, out var current))
                { RuntimeCharacters.Add(item.Key, item.Value); continue; }
                var saved = item.Value;
                current.Name = saved.Name; current.NameKey = saved.NameKey;
                current.Image = saved.Image; current.ImageAssetKey = saved.ImageAssetKey;
                current.Variables.Clear(); foreach (var variable in saved.Variables) current.Variables.Add(variable.Key, variable.Value);
                current.VariablesList.Clear(); current.VariablesList.AddRange(saved.VariablesList);
                if (ReferenceEquals(state.Dialogue.Character, saved)) state.Dialogue.Character = current;
            }
            UsedOnceOnlyOptions.Clear(); foreach (var item in state.Once) UsedOnceOnlyOptions.Add(item);
            DataAssetOverlay.Clear(); foreach (var item in state.Overlay) DataAssetOverlay.Add(item.Key, item.Value);
            context.ApplyExecutionRollback(state);
        }
    }

    public partial class StoryFlowComponent
    {
        private Execution.StoryFlowRollbackController rollback;
        private StoryFlowManager rollbackManager;
        private StoryFlowManager dialogueSessionManager;
        private ulong dialogueLifecycleGeneration;
        private ulong mediaGeneration;
        // Presentation consumers can bind after the restored event or receive a redraw of this entry.
        // Keep this identity outside snapshots; a fresh traversal increments the component serial.
        private ulong? restoredDialogueEntrySerial;
        internal bool IsCurrentDialogueRestored => restoredDialogueEntrySerial == DialogueEntrySerial;
        internal void MarkDialogueRestored() { restoredDialogueEntrySerial = DialogueEntrySerial; }
        private bool publishingRollbackAvailability, pendingRollbackAvailability;
        public event Action<StoryFlowRollbackAvailability> OnRollbackAvailabilityChanged;
        public event Action<StoryFlowDialogueState> OnDialogueRestored;
        public bool CanGoBack() => rollback?.Availability().CanGoBack ?? false;
        public StoryFlowRollbackAvailability GetRollbackAvailability() => rollback?.Availability()
            ?? new StoryFlowRollbackAvailability(false, 0, "disabled");
        public StoryFlowRollbackResult GoBack() => rollback?.GoBack() ?? new StoryFlowRollbackResult(false, "disabled");
        public void BlockRollback(string reason = null) { rollback?.Clear("barrier"); }
        internal ulong DialogueLifecycleGeneration => dialogueLifecycleGeneration;
        internal void HandleRollbackBarrier(StoryFlowNode node)
        {
            var generation = dialogueLifecycleGeneration;
            var entry = DialogueEntrySerial;
            var context = _context;
            var script = context.CurrentScript;
            var nodeId = node.Id;
            var reason = node.GetData("reason");
            BlockRollback(reason);
            if (!IsCurrentDialogueSession(generation) || DialogueEntrySerial != entry ||
                !ReferenceEquals(_context, context) || !ReferenceEquals(context.CurrentScript, script) || context.CurrentNodeId != nodeId) return;
            ProcessNextNodeFromSource(nodeId, StoryFlowHandles.Out_Flow);
        }
        internal void BeginRollback(StoryFlowManager manager)
        {
            var settings = _context.Project.DialogueRollback;
            if (settings == null || settings.Version != 1 || !settings.Enabled) return;
            rollbackManager = manager;
            rollback = new Execution.StoryFlowRollbackController(this, manager, settings.Normalize().HistoryLimit);
            _context.RollbackRandomState = unchecked((uint)Guid.NewGuid().GetHashCode()) | 1;
            manager.RegisterRollback(rollback);
        }
        internal void EndRollback()
        {
            var outgoing = rollback;
            var manager = dialogueSessionManager;
            dialogueSessionManager = null;
            rollback = null; rollbackManager = null;
            manager?.NotifyDialogueEnded();
            if (outgoing == null) return;
            manager?.UnregisterRollback(outgoing);
            outgoing.Clear("empty");
        }
        internal void CancelRollbackMedia() { mediaGeneration++; StopDialogueAudio(); }
        internal void CaptureRollbackRecovery(Execution.StoryFlowRollbackRecovery recovery)
        {
            recovery.Audio = CurrentDialogueAudioClip; recovery.AudioEntry = _dialogueAudioEntrySerial;
            recovery.MediaGeneration = mediaGeneration; recovery.AudioWaiting = _waitingForAudioAdvance;
            recovery.AudioAllowSkip = _audioAdvanceAllowSkip;
            if (_dialogueAudioSource == null) return;
            recovery.SourceClip = _dialogueAudioSource.clip; recovery.AudioLoop = _dialogueAudioSource.loop;
            recovery.AudioTime = _dialogueAudioSource.time; recovery.AudioPlaying = _dialogueAudioSource.isPlaying;
        }
        internal void RestoreRollbackRecovery(Execution.StoryFlowRollbackRecovery recovery)
        {
            if (_dialogueAudioSource != null)
            {
                _dialogueAudioSource.clip = recovery.SourceClip; _dialogueAudioSource.loop = recovery.AudioLoop;
                if (recovery.SourceClip != null)
                {
                    _dialogueAudioSource.Play(); _dialogueAudioSource.time = recovery.AudioTime;
                    if (!recovery.AudioPlaying) _dialogueAudioSource.Pause();
                }
            }
            CurrentDialogueAudioClip = recovery.Audio; _dialogueAudioEntrySerial = recovery.AudioEntry;
            mediaGeneration = recovery.MediaGeneration; _waitingForAudioAdvance = recovery.AudioWaiting;
            _audioAdvanceAllowSkip = recovery.AudioAllowSkip;
        }
        internal void CaptureRollbackMedia(Execution.StoryFlowExecutionSnapshot snapshot)
        {
            if (CurrentDialogueAudioClip != null && _dialogueAudioSource != null && _dialogueAudioSource.loop)
            { snapshot.LoopAudio = CurrentDialogueAudioClip; snapshot.LoopAudioTime = _dialogueAudioSource.time; }
        }
        internal void RestoreRollbackMedia(Execution.StoryFlowExecutionSnapshot snapshot)
        {
            if (snapshot.LoopAudio == null) return;
            PlayDialogueAudio(snapshot.LoopAudio, true);
            _dialogueAudioSource.time = snapshot.LoopAudioTime;
        }
        internal void PublishRollbackAvailability(StoryFlowRollbackAvailability value)
        {
            if (OnRollbackAvailabilityChanged == null) return;
            pendingRollbackAvailability = true;
            if (publishingRollbackAvailability) return;
            publishingRollbackAvailability = true;
            try
            {
                // Keep delivery on the component: observers can replace the private controller.
                while (pendingRollbackAvailability)
                {
                    pendingRollbackAvailability = false;
                    value = GetRollbackAvailability();
                    var observers = OnRollbackAvailabilityChanged;
                    if (observers == null) continue;
                    foreach (Action<StoryFlowRollbackAvailability> observer in observers.GetInvocationList())
                        try { observer(value); } catch (Exception error) { Debug.LogException(error); }
                }
            }
            finally { publishingRollbackAvailability = false; }
        }
        internal void PublishDialogueRestored(Execution.StoryFlowRollbackController owner, ulong generation, ulong entry, StoryFlowDialogueState state)
        {
            if (OnDialogueRestored == null) return;
            foreach (Action<StoryFlowDialogueState> observer in OnDialogueRestored.GetInvocationList())
            {
                if (!ReferenceEquals(rollback, owner) || !IsCurrentDialogueSession(generation) ||
                    DialogueEntrySerial != entry || !IsCurrentDialogueRestored ||
                    !ReferenceEquals(_context?.CurrentDialogueState, state) || rollbackManager == null || !rollbackManager.OwnsRollback(owner)) return;
                try { observer(state); } catch (Exception error) { Debug.LogException(error); }
            }
        }
        private IDisposable BeforeHostMutation() => StoryFlowManager.Instance?.BeginHostMutation();
    }
}
