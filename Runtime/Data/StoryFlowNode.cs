using System;
using System.Collections.Generic;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowNode
    {
        public string Id;
        public StoryFlowNodeType Type;

        /// <summary>
        /// The original type string from the JSON data, preserved for custom node type dispatch.
        /// For built-in types this matches the JSON key (e.g. "dialogue", "setBool").
        /// For custom/plugin types this holds the unrecognized type name.
        /// May be null if not set during import.
        /// </summary>
        public string RawType;

        public Dictionary<string, string> Data;

        public StoryFlowNode()
        {
            Data = new Dictionary<string, string>();
        }

        public string GetData(string key, string defaultValue = "")
        {
            return Data != null && Data.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public bool GetDataBool(string key, bool defaultValue = false)
        {
            if (Data == null || !Data.TryGetValue(key, out var value)) return defaultValue;
            return value == "true" || value == "1";
        }

        public int GetDataInt(string key, int defaultValue = 0)
        {
            if (Data == null || !Data.TryGetValue(key, out var value)) return defaultValue;
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        public float GetDataFloat(string key, float defaultValue = 0f)
        {
            if (Data == null || !Data.TryGetValue(key, out var value)) return defaultValue;
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
        }
    }
}
