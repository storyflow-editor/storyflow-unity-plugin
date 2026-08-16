using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;

namespace StoryFlow.Utilities
{
    /// <summary>
    /// Reads and writes the unified StoryFlow state format shared by the Unreal, Unity and
    /// Godot plugins.
    ///
    /// Type codes are written as NAMES, never integer codes. Unity's StoryFlowVariableType
    /// has no None member, so Boolean is 0 here while Unreal and Godot declare None = 0 and
    /// Boolean = 1. A format keyed on integers would silently mistype every value moved
    /// between engines. Unity's enum member names match the other engines' type names
    /// exactly, so ToString and TryParse are the entire mapping.
    ///
    /// Globals key by variable id; character variables key by variable name. Every record
    /// carries both id and name so any engine can match on its native key.
    /// </summary>
    public static class StoryFlowStateSerializer
    {
        public const string FormatVersion = "1";

        // ---------------------------------------------------------------- write

        public static string Serialize(
            Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions)
        {
            var root = new JObject { ["version"] = FormatVersion };

            var globals = new JObject();
            foreach (var kvp in globalVariables)
            {
                globals[kvp.Key] = VariableToJson(kvp.Value);
            }
            root["globalVariables"] = globals;

            var characters = new JObject();
            foreach (var kvp in runtimeCharacters)
            {
                var charObj = new JObject
                {
                    ["name"] = kvp.Value.Name ?? "",
                    ["image"] = kvp.Value.ImageAssetKey ?? ""
                };

                var vars = new JObject();
                if (kvp.Value.VariablesList != null)
                {
                    foreach (var v in kvp.Value.VariablesList)
                    {
                        // Character variables key by NAME in this format. Globals key by id.
                        vars[v.Name] = VariableToJson(v);
                    }
                }
                charObj["variables"] = vars;
                characters[kvp.Key] = charObj;
            }
            root["characters"] = characters;

            var onceOnly = new JArray();
            foreach (var key in usedOnceOnlyOptions)
            {
                onceOnly.Add(key);
            }
            root["usedOnceOnlyOptions"] = onceOnly;

            return root.ToString(Formatting.Indented);
        }

        private static JObject VariableToJson(StoryFlowVariable variable)
        {
            var obj = new JObject
            {
                ["id"] = variable.Id ?? "",
                ["name"] = variable.Name ?? "",
                ["type"] = variable.Type.ToString(),
                ["isArray"] = variable.IsArray
            };

            if (variable.Type == StoryFlowVariableType.Map)
            {
                obj["keyType"] = variable.KeyType.ToString();
                obj["valueType"] = variable.ValueType.ToString();

                var entries = new JArray();
                var map = variable.Value?.GetMap();
                if (map != null)
                {
                    foreach (var entry in map)
                    {
                        entries.Add(new JObject
                        {
                            ["key"] = VariantToJson(entry.Key),
                            ["value"] = VariantToJson(entry.Value)
                        });
                    }
                }
                obj["value"] = entries;
            }
            else if (variable.IsArray)
            {
                var arr = new JArray();
                var list = variable.Value?.GetArray();
                if (list != null)
                {
                    foreach (var element in list)
                    {
                        arr.Add(VariantToJson(element));
                    }
                }
                obj["value"] = arr;
            }
            else
            {
                obj["value"] = VariantToJson(variable.Value);
            }

            return obj;
        }

        private static JToken VariantToJson(StoryFlowVariant variant)
        {
            if (variant == null) { return JValue.CreateNull(); }

            switch (variant.Type)
            {
                case StoryFlowVariableType.Boolean:
                    return new JValue(variant.GetBool());
                case StoryFlowVariableType.Integer:
                    return new JValue(variant.GetInt());
                case StoryFlowVariableType.Float:
                    return new JValue(variant.GetFloat());
                case StoryFlowVariableType.Enum:
                    return new JValue(variant.GetEnum());
                case StoryFlowVariableType.String:
                case StoryFlowVariableType.Image:
                case StoryFlowVariableType.Audio:
                case StoryFlowVariableType.Character:
                    // Values persist EXACTLY as held in memory. This runtime resolves the
                    // strings table at READ time, so string-family values are normally the
                    // raw table key, matching the other engines.
                    return new JValue(variant.GetString());
                default:
                    return JValue.CreateNull();
            }
        }
    }
}
