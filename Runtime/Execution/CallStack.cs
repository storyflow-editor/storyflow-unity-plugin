using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Represents a saved execution frame when crossing script boundaries via RunScript.
    /// Stores the previous script's state so execution can resume after the called script ends.
    /// </summary>
    public class CallFrame
    {
        /// <summary>Path of the script that was executing before the call.</summary>
        public string ScriptPath;

        /// <summary>Node ID to return to when the called script finishes.</summary>
        public string ReturnNodeId;

        /// <summary>Weak reference to the script asset (avoids preventing GC).</summary>
        public System.WeakReference<StoryFlowScriptAsset> Script;

        /// <summary>Deep copy of the calling script's local variables at time of call.</summary>
        public Dictionary<string, StoryFlowVariable> SavedLocalVariables;

        /// <summary>Saved flow call stack from the calling script context.</summary>
        public List<FlowFrame> SavedFlowStack;

        public CallFrame()
        {
            SavedLocalVariables = new Dictionary<string, StoryFlowVariable>();
            SavedFlowStack = new List<FlowFrame>();
        }
    }

    /// <summary>
    /// Tracks flow (in-script macro) invocation depth.
    /// Flows are JUMPs, not CALLs — there is no return, only depth tracking
    /// for recursion protection.
    /// </summary>
    public class FlowFrame
    {
        /// <summary>The flow definition ID being executed.</summary>
        public string FlowId;

        public FlowFrame() { }

        public FlowFrame(string flowId)
        {
            FlowId = flowId;
        }
    }

    /// <summary>
    /// Tracks the state of an active forEach loop iteration.
    /// </summary>
    public class LoopContext
    {
        /// <summary>The forEach node ID driving this loop.</summary>
        public string NodeId;

        /// <summary>Current iteration index within the loop.</summary>
        public int CurrentIndex;

        /// <summary>The array element type being iterated (e.g. "boolean", "integer").</summary>
        public string LoopType;

        public LoopContext() { }

        public LoopContext(string nodeId, int currentIndex, string loopType)
        {
            NodeId = nodeId;
            CurrentIndex = currentIndex;
            LoopType = loopType;
        }
    }

    /// <summary>
    /// Per-node cached runtime state. Holds evaluation results and loop iteration data
    /// so that pure expression nodes are not re-evaluated within the same processing pass.
    /// </summary>
    public class NodeRuntimeState
    {
        /// <summary>Cached output value from the most recent evaluation.</summary>
        public StoryFlowVariant CachedOutput;

        /// <summary>Current loop index for forEach nodes.</summary>
        public int LoopIndex;

        /// <summary>The array being iterated for forEach nodes.</summary>
        public List<StoryFlowVariant> LoopArray;

        /// <summary>Named output values (used by RunScript nodes for typed outputs).</summary>
        public Dictionary<string, StoryFlowVariant> OutputValues;

        /// <summary>Cached parsed text blocks JSON for dialogue nodes (avoids re-parsing on revisits).</summary>
        public JArray CachedTextBlocks;

        /// <summary>Cached parsed options JSON for dialogue nodes (avoids re-parsing on revisits).</summary>
        public JArray CachedOptions;

        /// <summary>When true, cached values should be invalidated before next read.</summary>
        public bool Dirty;

        public NodeRuntimeState()
        {
            OutputValues = new Dictionary<string, StoryFlowVariant>();
        }

        /// <summary>
        /// Clears cached evaluation output so the node will be re-evaluated.
        /// </summary>
        public void ClearCache()
        {
            CachedOutput = null;
            CachedTextBlocks = null;
            CachedOptions = null;
            Dirty = false;
        }
    }
}
