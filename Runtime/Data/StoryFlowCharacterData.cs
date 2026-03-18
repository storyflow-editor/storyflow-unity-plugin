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
