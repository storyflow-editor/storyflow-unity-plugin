using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Lipsync
{
    /// <summary>How a pose's morph names have to be spelled to match the rig's blendshapes.</summary>
    public enum StoryFlowMorphNameStyle
    {
        /// <summary>Exactly as written. Unreal's Sidekick parts, and most hand-authored rigs.</summary>
        Exact = 0,

        /// <summary>
        /// Unity keeps the FBX blendshape-group prefix, so a Sidekick character baked by the Character Creator
        /// spells `jawOpen` as `MESHBlends.jawOpen`. This is the default because it is what Synty ship.
        /// </summary>
        UnityMeshBlendsPrefix = 1,
    }

    [System.Serializable]
    public class StoryFlowVisemeMorph
    {
        public string Name;
        [Range(0f, 1f)] public float Weight = 1f;
    }

    [System.Serializable]
    public class StoryFlowVisemePose
    {
        /// <summary>One of StoryFlowVisemeTable.PoseNames.</summary>
        public string Pose;
        public List<StoryFlowVisemeMorph> Morphs = new List<StoryFlowVisemeMorph>();
    }

    /// <summary>
    /// A rig's viseme mapping. OPTIONAL: a component with none uses the built-in table, which is ARKit-named
    /// and tuned on Synty Sidekick, and is what most projects want. ARKit's 52 face blendshape names are the
    /// de facto interchange format — MetaHumans (via Live Link Face), VRM, Ready Player Me and Character
    /// Creator all use them — so the built-in table already fits far more rigs than Synty's. Make an asset
    /// only to retarget onto a face that names its shapes differently.
    ///
    /// The schema is deliberately flat — one morph and one weight per entry, no per-shape attack/decay curves.
    /// The driver's global smoothing is what sells the motion; per-shape curves would be four more numbers per
    /// pose for a user to get wrong. See the mapping-asset schema in LIPSYNC_DESIGN.md.
    /// </summary>
    [CreateAssetMenu(fileName = "VisemeMap", menuName = "StoryFlow/Viseme Map", order = 300)]
    public class StoryFlowVisemeMap : ScriptableObject
    {
        [Tooltip("How these names are spelled on the rig's blendshapes.")]
        public StoryFlowMorphNameStyle NameStyle = StoryFlowMorphNameStyle.UnityMeshBlendsPrefix;

        public List<StoryFlowVisemePose> Poses = new List<StoryFlowVisemePose>();

        /// <summary>Warn about an authored table once, not once per component that reads it.</summary>
        [System.NonSerialized] private bool _validated;

        /// <summary>
        /// The table in the driver's shape. An empty asset answers the built-in table, not an empty mouth.
        ///
        /// NameStyle is not applied here and does not need to be: it says how the rig SPELLS these names, and
        /// that is a resolve-time question the component answers against the mesh. It reads NameStyle off this
        /// asset whether or not Poses is empty, so an asset that exists only to say "this rig spells them
        /// exactly" works and pairs that with the built-in table.
        /// </summary>
        public Dictionary<string, Dictionary<string, float>> ToTable()
        {
            if (Poses == null || Poses.Count == 0) return StoryFlowVisemeTable.Default();

            var table = new Dictionary<string, Dictionary<string, float>>();
            foreach (var pose in Poses)
            {
                if (pose == null || string.IsNullOrEmpty(pose.Pose)) continue;
                var morphs = new Dictionary<string, float>();
                if (pose.Morphs != null)
                {
                    foreach (var morph in pose.Morphs)
                    {
                        if (morph == null || string.IsNullOrEmpty(morph.Name)) continue;
                        morphs[morph.Name] = morph.Weight;
                    }
                }
                table[pose.Pose] = morphs;
            }

            // `rest` is the pose the mouth returns to. An asset that forgets it would leave the driver with
            // nothing to ease toward on a gap.
            if (!table.ContainsKey("rest")) table["rest"] = new Dictionary<string, float>();

            Validate(table);
            return table;
        }

        /// <summary>
        /// Say what an authored table is missing, ONCE per asset.
        ///
        /// Nothing here is fatal and nothing is repaired: an unknown pose name is simply a pose the driver
        /// never asks for, and a missing axis pose leaves a gap in the vowel slide. Both look like a mouth
        /// that half works, which is the hardest kind of wrong to attribute — a typo in a name field is not
        /// something anyone re-reads.
        /// </summary>
        private void Validate(Dictionary<string, Dictionary<string, float>> table)
        {
            if (_validated) return;
            _validated = true;

            var unknown = new List<string>();
            foreach (var pose in table.Keys)
            {
                var known = false;
                foreach (var candidate in StoryFlowVisemeTable.PoseNames)
                {
                    if (candidate != pose) continue;
                    known = true;
                    break;
                }
                if (!known) unknown.Add(pose);
            }
            if (unknown.Count > 0)
            {
                Debug.LogWarning($"[StoryFlow] Viseme map '{name}' has poses speech never reaches: " +
                                 $"{string.Join(", ", unknown)}. Only the idle mouth uses them. The pose names are " +
                                 $"{string.Join(", ", StoryFlowVisemeTable.PoseNames)}.", this);
            }

            var missing = new List<string>();
            foreach (var pose in StoryFlowVisemeTable.Axis)
            {
                if (!table.ContainsKey(pose)) missing.Add(pose);
            }
            if (missing.Count > 0)
            {
                Debug.LogWarning($"[StoryFlow] Viseme map '{name}' has no {string.Join(", ", missing)}. Those are " +
                                 "the vowel axis the mouth slides along, so that part of the slide does nothing.", this);
            }
        }

        /// <summary>Fill the asset with the built-in table, so editing starts from the tuned numbers.</summary>
        [ContextMenu("Reset to built-in Sidekick table")]
        public void ResetToDefault()
        {
            Poses = new List<StoryFlowVisemePose>();
            foreach (var kvp in StoryFlowVisemeTable.Default())
            {
                var pose = new StoryFlowVisemePose { Pose = kvp.Key };
                foreach (var morph in kvp.Value)
                {
                    pose.Morphs.Add(new StoryFlowVisemeMorph { Name = morph.Key, Weight = morph.Value });
                }
                Poses.Add(pose);
            }
        }
    }
}
