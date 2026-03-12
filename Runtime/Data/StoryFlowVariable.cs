using System;
using System.Collections.Generic;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowVariable
    {
        public string Id;
        public string Name;
        public StoryFlowVariableType Type;
        public StoryFlowVariant Value;
        public bool IsArray;
        public List<string> EnumValues;
        public bool IsInput;
        public bool IsOutput;

        public StoryFlowVariable()
        {
            Value = new StoryFlowVariant();
            EnumValues = new List<string>();
        }

        public StoryFlowVariable(StoryFlowVariable other)
        {
            Id = other.Id;
            Name = other.Name;
            Type = other.Type;
            Value = new StoryFlowVariant(other.Value);
            IsArray = other.IsArray;
            EnumValues = other.EnumValues != null ? new List<string>(other.EnumValues) : new List<string>();
            IsInput = other.IsInput;
            IsOutput = other.IsOutput;
        }
    }
}
