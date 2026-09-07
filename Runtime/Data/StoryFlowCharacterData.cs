using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    /// <summary>
    /// Resolved runtime character data for display in dialogue.
    /// </summary>
    [Serializable]
    public class StoryFlowCharacterData
    {
        public string Name;

        /// <summary>
        /// The string-table KEY <see cref="Name"/> was resolved from, or empty once a script has
        /// written a literal name over it.
        ///
        /// Unity cannot follow the sibling engines here. Unreal and Godot keep the key on the
        /// runtime record and resolve into a per-line dialogue-state SNAPSHOT; this plugin hands
        /// the LIVE record out as StoryFlowDialogueState.Character — pinned by reference in four
        /// tests, and what makes a mid-dialogue SetCharacterVar visible without rebuilding a
        /// state. So the record carries BOTH: the resolved text games read, and the key to
        /// re-resolve it from when the language moves.
        ///
        /// EMPTY MEANS LITERAL. A name a script wrote is live data, not content, so clearing the
        /// key is what stops a later SetLanguage overwriting it — the same provenance rule the
        /// .sfd read door applies to session writes.
        /// </summary>
        public string NameKey;

        public Sprite Image;
        public string ImageAssetKey;
        public Dictionary<string, StoryFlowVariant> Variables;

        /// <summary>
        /// Deep-copied list of character variables for mutation by Set handlers and save/load.
        /// </summary>
        public List<StoryFlowVariable> VariablesList;

        public StoryFlowCharacterData()
        {
            Variables = new Dictionary<string, StoryFlowVariant>();
            VariablesList = new List<StoryFlowVariable>();
        }

        public StoryFlowCharacterData(StoryFlowCharacterData other)
        {
            Name = other.Name;
            NameKey = other.NameKey;
            Image = other.Image;
            ImageAssetKey = other.ImageAssetKey;
            Variables = new Dictionary<string, StoryFlowVariant>();
            VariablesList = new List<StoryFlowVariable>();
            if (other.VariablesList != null)
            {
                foreach (var v in other.VariablesList)
                {
                    var copy = new StoryFlowVariable(v);
                    VariablesList.Add(copy);
                    Variables[copy.Name] = copy.Value;
                }
            }
            else if (other.Variables != null)
            {
                foreach (var kvp in other.Variables)
                    Variables[kvp.Key] = new StoryFlowVariant(kvp.Value);
            }
        }

        /// <summary>
        /// Finds a variable in the VariablesList by its display name.
        /// </summary>
        public StoryFlowVariable FindVariableByName(string name)
        {
            if (string.IsNullOrEmpty(name) || VariablesList == null) return null;
            foreach (var v in VariablesList)
            {
                if (v.Name == name)
                    return v;
            }
            return null;
        }
    }

    /// <summary>
    /// Character definition as stored in characters.json.
    /// </summary>
    [Serializable]
    public class StoryFlowCharacterDef
    {
        public string Name;
        public string ImageAssetKey;
        public List<StoryFlowVariable> Variables;

        public StoryFlowCharacterDef()
        {
            Variables = new List<StoryFlowVariable>();
        }
    }
}
