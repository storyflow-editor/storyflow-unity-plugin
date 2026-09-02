using System.Collections.Generic;

namespace StoryFlow.Lipsync
{
    /// <summary>
    /// The ten mouth poses, as ARKit morph weights in 0..1. Pure data and pure math — no Unity types — so
    /// the numbers that decide whether a mouth reads as speech are testable without an engine.
    ///
    /// These weights are NOT guesses. They are the tuned table from the three.js build that already runs
    /// against these same Synty rigs, and they are pinned by the normative driver spec in the plugin's
    /// LIPSYNC_DESIGN.md. The Unreal arm implements the same table. Changing a number here without changing
    /// it there is exactly the engine-arm drift the sequencing is meant to prevent: change the spec first.
    /// </summary>
    public static class StoryFlowVisemeTable
    {
        /// <summary>The pose the axis interpolates between, low centroid to high. Order is load-bearing.</summary>
        public static readonly string[] Axis = { "OO", "OH", "AA", "EE" };

        /// <summary>Every pose name, `rest` included. `rest` is empty by design: it is the absence of a pose.</summary>
        public static readonly string[] PoseNames = { "rest", "AA", "EE", "IH", "OH", "OO", "MM", "FF", "TH", "L" };

        /// <summary>
        /// The default table. A rig can override it with a mapping asset, but this is what ships, and it is
        /// what an arm falls back to when no asset is assigned — a component with nothing configured still
        /// moves a mouth rather than doing nothing and looking broken.
        /// </summary>
        public static Dictionary<string, Dictionary<string, float>> Default()
        {
            return new Dictionary<string, Dictionary<string, float>>
            {
                ["rest"] = new Dictionary<string, float>(),
                ["AA"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.85f, ["mouthLowerDownLeft"] = 0.32f, ["mouthLowerDownRight"] = 0.32f,
                },
                ["EE"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.28f,
                    ["mouthStretchLeft"] = 0.78f, ["mouthStretchRight"] = 0.78f,
                    ["mouthSmileLeft"] = 0.32f, ["mouthSmileRight"] = 0.32f,
                },
                ["IH"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.36f, ["mouthStretchLeft"] = 0.44f, ["mouthStretchRight"] = 0.44f,
                },
                ["OH"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.62f, ["mouthFunnel"] = 0.72f, ["mouthPucker"] = 0.32f,
                },
                ["OO"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.20f, ["mouthPucker"] = 0.72f, ["mouthFunnel"] = 0.38f,
                },
                ["MM"] = new Dictionary<string, float>
                {
                    ["mouthClose"] = 0.68f, ["mouthPressLeft"] = 0.52f, ["mouthPressRight"] = 0.52f,
                },
                ["FF"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.20f, ["mouthRollLower"] = 0.72f,
                    ["mouthUpperUpLeft"] = 0.38f, ["mouthUpperUpRight"] = 0.38f,
                },
                ["TH"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.44f, ["tongueOut"] = 0.66f, ["tongueUp"] = 0.28f,
                },
                ["L"] = new Dictionary<string, float>
                {
                    ["jawOpen"] = 0.52f, ["tongueUp"] = 0.82f, ["tongueRaise"] = 0.58f,
                },
            };
        }

        /// <summary>
        /// Every morph name any pose can touch. This is the "owned set": the driver writes these and NOTHING
        /// else, so identity morphs (defaultBuff, defaultSkinny, defaultHeavy, masculineFeminine) survive.
        /// Unity's baked characters do not carry those, but Unreal's parts do, and a driver that zeroes every
        /// weight it finds would silently flatten a character back to the base body.
        /// </summary>
        public static HashSet<string> OwnedMorphs(Dictionary<string, Dictionary<string, float>> table)
        {
            var owned = new HashSet<string>();
            foreach (var pose in table.Values)
            {
                foreach (var morph in pose.Keys) owned.Add(morph);
            }
            return owned;
        }
    }
}
