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
    /// A rig's viseme mapping. OPTIONAL: a component with none uses the built-in table, which is tuned for
    /// Synty Sidekick and is what most projects want. Make one only to retarget onto a different face.
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

        /// <summary>The table in the driver's shape. An empty asset answers the built-in table, not an empty mouth.</summary>
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
            return table;
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
