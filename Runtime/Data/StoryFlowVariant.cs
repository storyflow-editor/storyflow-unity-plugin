using System;
using System.Collections.Generic;
using System.Globalization;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowVariant
    {
        public StoryFlowVariableType Type;
        public bool BoolValue;
        public int IntValue;
        public float FloatValue;
        public string StringValue = "";
        public string EnumValue = "";
        [NonSerialized] public List<StoryFlowVariant> ArrayValue;

        public StoryFlowVariant()
        {
            Type = StoryFlowVariableType.Boolean;
        }

        public StoryFlowVariant(StoryFlowVariant other)
        {
            Type = other.Type;
            BoolValue = other.BoolValue;
            IntValue = other.IntValue;
            FloatValue = other.FloatValue;
            StringValue = other.StringValue ?? "";
            EnumValue = other.EnumValue ?? "";
            if (other.ArrayValue != null)
            {
                ArrayValue = new List<StoryFlowVariant>(other.ArrayValue.Count);
                foreach (var item in other.ArrayValue)
                    ArrayValue.Add(new StoryFlowVariant(item));
            }
        }

        public void SetBool(bool value)
        {
            Type = StoryFlowVariableType.Boolean;
            BoolValue = value;
        }

        public void SetInt(int value)
        {
            Type = StoryFlowVariableType.Integer;
            IntValue = value;
        }

        public void SetFloat(float value)
        {
            Type = StoryFlowVariableType.Float;
            FloatValue = value;
        }

        public void SetString(string value)
        {
            Type = StoryFlowVariableType.String;
            StringValue = value ?? "";
        }

        public void SetEnum(string value)
        {
            Type = StoryFlowVariableType.Enum;
            EnumValue = value ?? "";
        }

        public bool GetBool(bool defaultValue = false)
        {
            return Type == StoryFlowVariableType.Boolean ? BoolValue : defaultValue;
        }

        public int GetInt(int defaultValue = 0)
        {
            return Type == StoryFlowVariableType.Integer ? IntValue : defaultValue;
        }

        public float GetFloat(float defaultValue = 0f)
        {
            return Type == StoryFlowVariableType.Float ? FloatValue : defaultValue;
        }

        public string GetString(string defaultValue = "")
        {
            return Type == StoryFlowVariableType.String ? StringValue : defaultValue;
        }

        public string GetEnum(string defaultValue = "")
        {
            return Type == StoryFlowVariableType.Enum ? EnumValue : defaultValue;
        }

        public List<StoryFlowVariant> GetArray()
        {
            return ArrayValue ??= new List<StoryFlowVariant>();
        }

        public void Reset()
        {
            BoolValue = false;
            IntValue = 0;
            FloatValue = 0f;
            StringValue = "";
            EnumValue = "";
            ArrayValue = null;
        }

        public override string ToString()
        {
            return Type switch
            {
                StoryFlowVariableType.Boolean => BoolValue.ToString(),
                StoryFlowVariableType.Integer => IntValue.ToString(),
                StoryFlowVariableType.Float => FloatValue.ToString(CultureInfo.InvariantCulture),
                StoryFlowVariableType.String => StringValue,
                StoryFlowVariableType.Enum => EnumValue,
                StoryFlowVariableType.Image => StringValue,
                StoryFlowVariableType.Audio => StringValue,
                StoryFlowVariableType.Character => StringValue,
                _ => ""
            };
        }

        public static StoryFlowVariant Bool(bool value) => new() { Type = StoryFlowVariableType.Boolean, BoolValue = value };
        public static StoryFlowVariant Int(int value) => new() { Type = StoryFlowVariableType.Integer, IntValue = value };
        public static StoryFlowVariant Float(float value) => new() { Type = StoryFlowVariableType.Float, FloatValue = value };
        public static StoryFlowVariant String(string value) => new() { Type = StoryFlowVariableType.String, StringValue = value ?? "" };
        public static StoryFlowVariant Enum(string value) => new() { Type = StoryFlowVariableType.Enum, EnumValue = value ?? "" };

        /// <summary>
        /// Deserializes a StoryFlowVariant from a type and a plain-text JSON value string.
        /// Used by project/script asset importers and save/load helpers.
        /// </summary>
        public static StoryFlowVariant DeserializeFromJson(StoryFlowVariableType type, string json)
        {
            var variant = new StoryFlowVariant { Type = type };
            if (string.IsNullOrEmpty(json)) return variant;

            switch (type)
            {
                case StoryFlowVariableType.Boolean:
                    variant.BoolValue = json == "True" || json == "true" || json == "1";
                    break;
                case StoryFlowVariableType.Integer:
                    int.TryParse(json, out variant.IntValue);
                    break;
                case StoryFlowVariableType.Float:
                    float.TryParse(json, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out variant.FloatValue);
                    break;
                case StoryFlowVariableType.String:
                    variant.StringValue = json;
                    break;
                case StoryFlowVariableType.Enum:
                    variant.EnumValue = json;
                    break;
                default:
                    variant.StringValue = json;
                    break;
            }

            return variant;
        }
    }
}
