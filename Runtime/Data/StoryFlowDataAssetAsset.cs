using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    /// <summary>
    /// One imported .sfd Data Asset — a level of the seed table the runtime store resolves
    /// through (engine contract §2.1). Written by StoryFlowImporter from data-assets.json,
    /// one asset per .sfd file at Assets/StoryFlow/DataAssets/{assetId}.asset.
    ///
    /// Named by ASSET ID rather than by display name: ids are unique by construction and
    /// survive a rename, so a re-import lands on the same .asset instead of orphaning the
    /// old one and churning its GUID. The readable name travels in
    /// <see cref="DisplayName"/> — deliberately not a field called Name, which would
    /// collide with UnityEngine.Object.name.
    /// </summary>
    public class StoryFlowDataAssetAsset : ScriptableObject
    {
        /// <summary>The .sfd's asset id ("da_" + 32 hex). Also this asset's file name.</summary>
        public string Id;

        /// <summary>The .sfd's display name (its filename base). Never read by the resolver.</summary>
        public string DisplayName;

        /// <summary>The parent asset's id, or empty for a root asset.</summary>
        public string ParentId;

        /// <summary>
        /// This level's own declarations, in the exported order. DECLARATION ORDER IS
        /// CONTRACTUAL (§2.1), which is why this is a List and never a Dictionary.
        /// </summary>
        public List<StoryFlowVariable> Variables = new List<StoryFlowVariable>();

        /// <summary>
        /// This level's overrides of variables declared further up the chain, stored as RAW
        /// exported JSON. See <see cref="OverrideEntry"/> for why they are not typed here.
        /// </summary>
        public List<OverrideEntry> Overrides = new List<OverrideEntry>();

        /// <summary>
        /// SHA-256 of this data asset's condensed source JSON. Written only after a
        /// successful save, so an unchanged sync touches nothing on disk.
        /// </summary>
        [HideInInspector] public string ImportedSourceHash;

        /// <summary>
        /// One override: a variable id declared somewhere on this asset's ancestor chain,
        /// and the value this level substitutes for it.
        ///
        /// The value stays RAW JSON here and is typed at seed build (StoryFlowDataAssetStore
        /// .BuildSeed pass 2) rather than at import. Typing it needs the DECLARATION, which
        /// may live on an ancestor this importer has not built yet — the assets arrive in
        /// export order, not chain order — so the question is a chain question and can only
        /// be answered once the whole seed exists. This is a deliberate divergence from the
        /// Unreal port, which types at import because its importer resolves the chain there;
        /// the contract outcome is identical, and a JSON string is exactly what survives
        /// Unity serialization anyway (the same DefaultValueJson dance the declarations use).
        /// </summary>
        [Serializable]
        public class OverrideEntry
        {
            public string VariableId;
            public string ValueJson;
        }
    }
}
