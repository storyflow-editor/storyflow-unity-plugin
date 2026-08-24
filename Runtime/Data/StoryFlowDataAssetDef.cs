using System.Collections.Generic;

namespace StoryFlow.Data
{
    /// <summary>
    /// One level of the runtime SEED table: a <see cref="StoryFlowDataAssetAsset"/> with its
    /// containers rehydrated and its overrides typed, ready for the chain walk.
    ///
    /// A plain class rather than a UnityEngine.Object because nothing here is serialized —
    /// it is built once per session from the imported assets (StoryFlowDataAssetStore
    /// .BuildSeed) and then never written to again. Contract §3: "the seed is never mutated
    /// by anything, ever"; every read hands out copies, every write lands in the overlay.
    /// </summary>
    public class StoryFlowDataAssetDef
    {
        /// <summary>The asset id this level is keyed by in the seed.</summary>
        public string Id;

        /// <summary>The display name, carried for diagnostics only — no resolver reads it.</summary>
        public string Name;

        /// <summary>The parent's asset id, or empty at the root (which ends the chain walk).</summary>
        public string ParentId;

        /// <summary>
        /// This level's declarations, in the exported order (§2.1). Deep copies, with array
        /// and map storage rehydrated from DefaultValueJson — those containers are
        /// [NonSerialized] on StoryFlowVariant and do not survive Unity serialization.
        /// </summary>
        public List<StoryFlowVariable> Variables = new List<StoryFlowVariable>();

        /// <summary>
        /// This level's overrides, keyed by variable id and typed against the declaration
        /// that owns each id somewhere on the chain. Overrides whose declaration is gone
        /// (orphans) never reach here — see BuildSeed.
        /// </summary>
        public Dictionary<string, StoryFlowVariant> Overrides = new Dictionary<string, StoryFlowVariant>();
    }
}
