using System.Collections.Generic;

namespace StoryFlow.Data
{
    /// <summary>
    /// A fully parsed but not yet applied state blob. Import parses into one of these
    /// first so a malformed or truncated blob can be rejected without having touched
    /// live story state.
    ///
    /// Only VALUES are carried. Schema (Type, KeyType, ValueType, enum value lists) stays
    /// owned by the project asset, which remains authoritative across story updates.
    /// </summary>
    public class StoryFlowStateSnapshot
    {
        /// <summary>Global variable values, keyed by variable id.</summary>
        public Dictionary<string, StoryFlowVariant> GlobalValues = new();

        /// <summary>Character variable values: normalized character path, then variable name.</summary>
        public Dictionary<string, Dictionary<string, StoryFlowVariant>> CharacterValues = new();

        /// <summary>Once-only option keys recorded as used.</summary>
        public List<string> UsedOnceOnlyOptions = new();
    }
}
