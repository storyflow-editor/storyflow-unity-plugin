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
    ///
    /// Character name and image count as values, not schema: SetCharacterVar mutates both
    /// at story time, so they are staged and applied like any other value.
    /// </summary>
    public class StoryFlowStateSnapshot
    {
        /// <summary>Global variable values, keyed by variable id.</summary>
        public Dictionary<string, StoryFlowVariant> GlobalValues = new();

        /// <summary>Character variable values: normalized character path, then variable name.</summary>
        public Dictionary<string, Dictionary<string, StoryFlowVariant>> CharacterValues = new();

        /// <summary>
        /// Character display names, keyed by normalized character path. A character missing
        /// from this map keeps its current name: absence is how the blob says "unchanged".
        /// </summary>
        public Dictionary<string, string> CharacterNames = new();

        /// <summary>
        /// Character portrait asset keys, keyed by normalized character path. Absence keeps
        /// the current key, same as <see cref="CharacterNames"/>.
        /// </summary>
        public Dictionary<string, string> CharacterImages = new();

        /// <summary>Once-only option keys recorded as used.</summary>
        public List<string> UsedOnceOnlyOptions = new();
    }
}
