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

        // ----------------------------------------------------------------- read

        /// <summary>
        /// Parses a unified-format blob into a snapshot. Returns null on any malformed,
        /// truncated, empty or null input. Never throws: a blob arriving from a cloud
        /// provider is far less trustworthy than a local file.
        /// </summary>
        public static StoryFlowStateSnapshot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) { return null; }

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
                if (root == null) { return null; }
            }
            catch (JsonException)
            {
                return null;
            }

            var snapshot = new StoryFlowStateSnapshot();

            if (root["globalVariables"] is JObject globals)
            {
                foreach (var property in globals.Properties())
                {
                    if (property.Value is JObject record &&
                        TryVariableValueFromJson(record, out var value))
                    {
                        snapshot.GlobalValues[property.Name] = value;
                    }
                }
            }

            if (root["characters"] is JObject characters)
            {
                foreach (var charProperty in characters.Properties())
                {
                    if (!(charProperty.Value is JObject charObj)) { continue; }

                    // name and image are runtime VALUES: SetCharacterVar mutates both at
                    // story time, so they are read back here and applied on import, matching
                    // Unreal's save game and the HTML runtime. A field absent from the blob
                    // stays out of the snapshot, which the merge reads as "keep current".
                    if (charObj["name"] != null && charObj["name"].Type == JTokenType.String)
                    {
                        snapshot.CharacterNames[charProperty.Name] = (string)charObj["name"];
                    }
                    if (charObj["image"] != null && charObj["image"].Type == JTokenType.String)
                    {
                        snapshot.CharacterImages[charProperty.Name] = (string)charObj["image"];
                    }

                    if (!(charObj["variables"] is JObject vars)) { continue; }

                    var values = new Dictionary<string, StoryFlowVariant>();
                    foreach (var varProperty in vars.Properties())
                    {
                        if (varProperty.Value is JObject record &&
                            TryVariableValueFromJson(record, out var value))
                        {
                            values[varProperty.Name] = value;
                        }
                    }
                    snapshot.CharacterValues[charProperty.Name] = values;
                }
            }

            if (root["usedOnceOnlyOptions"] is JArray onceOnly)
            {
                foreach (var entry in onceOnly)
                {
                    var key = entry?.ToString();
                    if (!string.IsNullOrEmpty(key)) { snapshot.UsedOnceOnlyOptions.Add(key); }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Rehydrates one variable record's value. Returns false when the record carries a
        /// type this engine does not know, which includes the "None" the other engines can
        /// emit. Unknown records are skipped rather than failing the whole blob.
        /// </summary>
        private static bool TryVariableValueFromJson(JObject record, out StoryFlowVariant value)
        {
            value = null;

            var typeName = (string)record["type"];
            if (!TryParseType(typeName, out var type)) { return false; }

            var isArray = record["isArray"] != null && (bool)record["isArray"];
            var token = record["value"];

            if (type == StoryFlowVariableType.Map)
            {
                if (!TryParseType((string)record["keyType"], out var keyType))
                {
                    keyType = StoryFlowVariableType.String;
                }
                if (!TryParseType((string)record["valueType"], out var valueType))
                {
                    valueType = StoryFlowVariableType.String;
                }

                var entries = new List<StoryFlowMapEntry>();
                if (token is JArray entryArray)
                {
                    foreach (var entryToken in entryArray)
                    {
                        if (!(entryToken is JObject entryObj) || entryObj["key"] == null) { continue; }
                        entries.Add(new StoryFlowMapEntry
                        {
                            Key = VariantFromJson(entryObj["key"], keyType),
                            Value = VariantFromJson(entryObj["value"], valueType)
                        });
                    }
                }

                value = new StoryFlowVariant();
                value.SetMap(entries);
                return true;
            }

            if (isArray)
            {
                var list = new List<StoryFlowVariant>();
                if (token is JArray array)
                {
                    foreach (var element in array)
                    {
                        list.Add(VariantFromJson(element, type));
                    }
                }
                value = new StoryFlowVariant { Type = type, ArrayValue = list };
                return true;
            }

            value = VariantFromJson(token, type);
            return true;
        }

        private static bool TryParseType(string name, out StoryFlowVariableType type)
        {
            // "None" is emitted by Unreal and Godot, which declare it, and has no Unity
            // counterpart. Enum.TryParse rejects it, which is the behaviour we want.
            // Reject numeric strings too: TryParse would happily accept "3" and reintroduce
            // exactly the integer-code coupling this format exists to remove.
            type = default;
            if (string.IsNullOrEmpty(name)) { return false; }
            if (char.IsDigit(name[0]) || name[0] == '-') { return false; }
            return Enum.TryParse(name, ignoreCase: false, out type);
        }

        private static StoryFlowVariant VariantFromJson(JToken token, StoryFlowVariableType type)
        {
            var variant = new StoryFlowVariant();
            if (token == null || token.Type == JTokenType.Null) { variant.Type = type; return variant; }

            switch (type)
            {
                case StoryFlowVariableType.Boolean:
                    variant.SetBool(token.Type == JTokenType.Boolean && (bool)token);
                    break;
                case StoryFlowVariableType.Integer:
                    variant.SetInt(token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? (int)token : 0);
                    break;
                case StoryFlowVariableType.Float:
                    variant.SetFloat(token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? (float)token : 0f);
                    break;
                case StoryFlowVariableType.Enum:
                    variant.SetEnum(token.ToString());
                    break;
                default:
                    variant.SetString(token.ToString());
                    variant.Type = type;
                    break;
            }
            return variant;
        }
    }
}
