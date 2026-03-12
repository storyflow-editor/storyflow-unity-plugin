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
    }

    [Serializable]
    public class SavedCharacter
    {
        public string Path;
        public List<SavedVariable> Variables = new();
    }
}
