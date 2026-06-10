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

        // Map variables only (Type == StoryFlowVariableType.Map): the declared key/value
        // types and, when a side is enum-typed, its allowed values.
        public StoryFlowVariableType KeyType;
        public StoryFlowVariableType ValueType;
        public List<string> KeyEnumValues;
        public List<string> ValueEnumValues;

        /// <summary>
        /// JSON-serialized default value for array and map variables.
        /// ArrayValue/MapValue on StoryFlowVariant are [NonSerialized], so containers survive
        /// Unity serialization through this field and are deserialized at runtime.
        /// </summary>
        public string DefaultValueJson;

        public StoryFlowVariable()
        {
            Value = new StoryFlowVariant();
            EnumValues = new List<string>();
            KeyEnumValues = new List<string>();
            ValueEnumValues = new List<string>();
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
            KeyType = other.KeyType;
            ValueType = other.ValueType;
            KeyEnumValues = other.KeyEnumValues != null ? new List<string>(other.KeyEnumValues) : new List<string>();
            ValueEnumValues = other.ValueEnumValues != null ? new List<string>(other.ValueEnumValues) : new List<string>();
            DefaultValueJson = other.DefaultValueJson;
        }
    }
}
