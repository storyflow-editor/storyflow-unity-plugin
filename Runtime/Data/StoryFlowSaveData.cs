using System;
using System.Collections.Generic;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowSaveData
    {
        public string Version = "1.0.0";
        public List<SavedVariable> GlobalVariables = new();
        public List<SavedCharacter> RuntimeCharacters = new();
        public List<string> UsedOnceOnlyOptions = new();
    }

    [Serializable]
    public class SavedVariable
    {
        public string Id;
        public string Name;
        public StoryFlowVariableType Type;
        public string ValueJson;
        public bool IsArray;

        // Map variables only (Type == StoryFlowVariableType.Map): the declared key/value
        // types the load side parses ValueJson entries with. Loads are tolerant — saves
        // that predate maps simply lack these fields and rehydrate as empty maps.
        public StoryFlowVariableType KeyType;
        public StoryFlowVariableType ValueType;
    }

    [Serializable]
    public class SavedCharacter
    {
        public string Path;
        public List<SavedVariable> Variables = new();
    }
}
