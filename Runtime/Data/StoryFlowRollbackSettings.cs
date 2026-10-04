using System;
using Newtonsoft.Json.Linq;

namespace StoryFlow.Data
{
    [Serializable]
    public sealed class StoryFlowRollbackSettings
    {
        public int Version = 1;
        public bool Enabled;
        public int HistoryLimit = 100;

        public static StoryFlowRollbackSettings Normalize(JToken value)
        {
            var result = new StoryFlowRollbackSettings();
            if (!(value is JObject block) || !Integer(block["version"], 1, 1, out _)) return result;
            result.Enabled = block["enabled"]?.Type == JTokenType.Boolean && (bool)block["enabled"];
            if (Integer(block["historyLimit"], 1, 1000, out int limit)) result.HistoryLimit = limit;
            return result;
        }

        internal StoryFlowRollbackSettings Normalize()
        {
            if (Version != 1) return new StoryFlowRollbackSettings();
            return new StoryFlowRollbackSettings { Enabled = Enabled,
                HistoryLimit = HistoryLimit >= 1 && HistoryLimit <= 1000 ? HistoryLimit : 100 };
        }

        private static bool Integer(JToken value, int min, int max, out int result)
        {
            result = 0;
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return false;
            if (!double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double number) ||
                double.IsNaN(number) || number < min || number > max || Math.Truncate(number) != number) return false;
            result = (int)number;
            return true;
        }
    }
}
