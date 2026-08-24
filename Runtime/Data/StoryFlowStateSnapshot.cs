using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// The .sfd Data Asset session overlay: assetId -> variableId -> BARE value, exactly
        /// as the blob carried it (contract §7). Values stay raw <see cref="JToken"/>s here
        /// because typing them needs the seed's declaration, which the snapshot has no access
        /// to — the manager does that on the way in. They are DETACHED copies: a token still
        /// attached to the parsed blob would keep that whole document alive for as long as the
        /// snapshot is held, which for a host that caches snapshots is one full save blob each.
        ///
        /// ABSENCE MEANS CLEAR HERE, not "unchanged" — the one section of this class that
        /// inverts the rule the others follow, and deliberately. The overlay is a complete
        /// picture of the session's writes rather than a set of independent entries, so
        /// merging a save into a live overlay would let the pre-load session's writes survive
        /// into the loaded game. An absent or malformed key restores seed state, which is
        /// exactly the state a save without this key was made in (an older plugin build, or a
        /// game that never wrote a .sfd variable). Once-only options are the same shape and
        /// the precedent for it.
        ///
        /// NULL is therefore meaningful and is the default: it is what an absent key parses
        /// to, and it clears just like an empty table does.
        /// </summary>
        public Dictionary<string, Dictionary<string, JToken>> DataAssetValues;
    }
}
