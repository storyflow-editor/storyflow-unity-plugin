using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StoryFlow.Data
{
    /// <summary>
    /// One map entry. Entry ORDER is observable through mapKeys/mapValues/forEachMap and is
    /// contractual (must match the editor's serialized order), so map storage is an ordered
    /// entry list, NOT a Dictionary — C# Dictionary iteration order is not guaranteed.
    /// Reference semantics on Key/Value give natural aliasing within a chain; boundaries
    /// that need isolation deep-copy via the StoryFlowVariant copy constructor.
    /// </summary>
    [Serializable]
    public class StoryFlowMapEntry
    {
        public StoryFlowVariant Key;
        public StoryFlowVariant Value;
    }

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
        [NonSerialized] public List<StoryFlowMapEntry> MapValue;

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
            if (other.MapValue != null)
            {
                MapValue = new List<StoryFlowMapEntry>(other.MapValue.Count);
                foreach (var entry in other.MapValue)
                    MapValue.Add(new StoryFlowMapEntry
                    {
                        Key = new StoryFlowVariant(entry.Key),
                        Value = new StoryFlowVariant(entry.Value)
                    });
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
            // Image, Audio, and Character types also store their values in StringValue
            return (Type == StoryFlowVariableType.String ||
                    Type == StoryFlowVariableType.Image ||
                    Type == StoryFlowVariableType.Audio ||
                    Type == StoryFlowVariableType.Character)
                ? StringValue : defaultValue;
        }

        public string GetEnum(string defaultValue = "")
        {
            return Type == StoryFlowVariableType.Enum ? EnumValue : defaultValue;
        }

        public List<StoryFlowVariant> GetArray()
        {
            return ArrayValue ??= new List<StoryFlowVariant>();
        }

        /// <summary>
        /// Assigns map entries. A variant holds either array or map data, never both,
        /// so the array storage is cleared (mutual exclusivity).
        /// </summary>
        public void SetMap(List<StoryFlowMapEntry> entries)
        {
            Type = StoryFlowVariableType.Map;
            MapValue = entries ?? new List<StoryFlowMapEntry>();
            ArrayValue = null;
        }

        public List<StoryFlowMapEntry> GetMap()
        {
            return MapValue ??= new List<StoryFlowMapEntry>();
        }

        /// <summary>
        /// DELIBERATELY aliases this variant's map storage to the source variant's LIVE
        /// entry list (no copy). Used by setMap on script/global variable chains: the HTML
        /// runtime assigns the _runtimeMap REFERENCE, so after setMap(b ← getMap(a)) a later
        /// clearMap(a) also empties b. Clears the array storage (array/map mutual exclusivity,
        /// same rule as <see cref="SetMap"/>).
        /// </summary>
        public void SetMapAlias(StoryFlowVariant source)
        {
            Type = StoryFlowVariableType.Map;
            MapValue = source?.GetMap() ?? new List<StoryFlowMapEntry>();
            ArrayValue = null;
        }

        public void Reset()
        {
            BoolValue = false;
            IntValue = 0;
            FloatValue = 0f;
            StringValue = "";
            EnumValue = "";
            ArrayValue = null;
            MapValue = null;
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
                // Map display formatting is deliberately deferred — interpolation of maps
                // is out of contract; maps render as an empty string
                StoryFlowVariableType.Map => "",
                _ => ""
            };
        }

        public static StoryFlowVariant Bool(bool value) => new() { Type = StoryFlowVariableType.Boolean, BoolValue = value };
        public static StoryFlowVariant Int(int value) => new() { Type = StoryFlowVariableType.Integer, IntValue = value };
        public static StoryFlowVariant Float(float value) => new() { Type = StoryFlowVariableType.Float, FloatValue = value };
        public static StoryFlowVariant String(string value) => new() { Type = StoryFlowVariableType.String, StringValue = value ?? "" };
        public static StoryFlowVariant Enum(string value) => new() { Type = StoryFlowVariableType.Enum, EnumValue = value ?? "" };

        /// <summary>
        /// Serializes an array StoryFlowVariant to a JSON array string (e.g. ["a","b"] or [1,2]),
        /// the same dialect the editor importer writes to DefaultValueJson and
        /// <see cref="DeserializeArrayFromJson"/> consumes. Returns "[]" when the array is empty or unset.
        /// </summary>
        public string SerializeArrayToJson()
        {
            var array = new JArray();
            if (ArrayValue != null)
            {
                foreach (var item in ArrayValue)
                {
                    if (item == null) continue;
                    switch (item.Type)
                    {
                        case StoryFlowVariableType.Boolean:
                            array.Add(item.BoolValue);
                            break;
                        case StoryFlowVariableType.Integer:
                            array.Add(item.IntValue);
                            break;
                        case StoryFlowVariableType.Float:
                            array.Add(item.FloatValue);
                            break;
                        default:
                            array.Add(item.ToString());
                            break;
                    }
                }
            }
            return array.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Deserializes an array StoryFlowVariant from a JSON array string (e.g. ["a","b"]).
        /// Each element is deserialized as the given element type.
        /// </summary>
        public static StoryFlowVariant DeserializeArrayFromJson(StoryFlowVariableType elementType, string json)
        {
            var variant = new StoryFlowVariant { Type = elementType };
            variant.ArrayValue = new List<StoryFlowVariant>();

            if (string.IsNullOrEmpty(json)) return variant;

            try
            {
                var array = JArray.Parse(json);
                foreach (var item in array)
                {
                    // Numeric elements must be read with invariant culture so arrays
                    // round-trip regardless of the process culture.
                    string itemText = item is JValue jValue && jValue.Value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : item.ToString();
                    variant.ArrayValue.Add(DeserializeFromJson(elementType, itemText));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryFlow] Failed to parse array value of type {elementType} " +
                                 $"from JSON '{json}': {e.Message}. Treating it as an empty array.");
                variant.ArrayValue.Clear();
            }

            return variant;
        }

        /// <summary>
        /// Serializes a map StoryFlowVariant to the JSON entry-array dialect
        /// (e.g. [{"key":"a","value":1},...]) that <see cref="DeserializeMapFromJson"/>
        /// consumes — the same shape the editor exports. Entry order is preserved,
        /// keys/values write as native JSON types (numbers/bools unquoted), keyless
        /// entries are skipped (unaddressable). Returns "[]" when the map is empty/unset.
        /// </summary>
        public string SerializeMapToJson()
        {
            var array = new JArray();
            if (MapValue != null)
            {
                foreach (var entry in MapValue)
                {
                    if (entry?.Key == null) continue;
                    array.Add(new JObject
                    {
                        ["key"] = VariantToJsonToken(entry.Key),
                        ["value"] = VariantToJsonToken(entry.Value)
                    });
                }
            }
            return array.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Renders a scalar variant as a native JSON token (number/bool kept as JSON
        /// types so DeserializeMapFromJson's invariant-culture parse round-trips them;
        /// string-family and enum values write their stored text verbatim).
        /// </summary>
        private static JToken VariantToJsonToken(StoryFlowVariant variant)
        {
            if (variant == null) return "";
            return variant.Type switch
            {
                StoryFlowVariableType.Boolean => variant.BoolValue,
                StoryFlowVariableType.Integer => variant.IntValue,
                StoryFlowVariableType.Float => variant.FloatValue,
                _ => variant.ToString()
            };
        }

        /// <summary>
        /// Deserializes a map StoryFlowVariant from a JSON entry-array string
        /// (e.g. [{"key":"a","value":1},...]). Entry order is contractual and preserved.
        /// Keys are raw identifiers — never strings-table keys — typed per the declared
        /// keyType (integer keys parse numerically). String-family values store the
        /// exported strings-table key / asset id VERBATIM; resolution happens at read time.
        /// Tolerant: malformed JSON degrades to an empty map, entries without a key are skipped.
        /// </summary>
        public static StoryFlowVariant DeserializeMapFromJson(
            StoryFlowVariableType keyType, StoryFlowVariableType valueType, string json)
        {
            var variant = new StoryFlowVariant { Type = StoryFlowVariableType.Map };
            variant.MapValue = new List<StoryFlowMapEntry>();

            if (string.IsNullOrEmpty(json)) return variant;

            try
            {
                var array = JArray.Parse(json);
                foreach (var item in array)
                {
                    var entryObj = item as JObject;
                    if (entryObj == null) continue;

                    // An entry without a key is unaddressable — skip it
                    JToken keyToken = entryObj["key"];
                    if (keyToken == null || keyToken.Type == JTokenType.Null)
                    {
                        Debug.LogWarning("[StoryFlow] Skipping map entry with missing key " +
                                         $"in map JSON '{json}'.");
                        continue;
                    }

                    variant.MapValue.Add(new StoryFlowMapEntry
                    {
                        Key = DeserializeFromJson(keyType, TokenToInvariantString(keyToken)),
                        Value = DeserializeFromJson(valueType, TokenToInvariantString(entryObj["value"]))
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryFlow] Failed to parse map value ({keyType} → {valueType}) " +
                                 $"from JSON '{json}': {e.Message}. Treating it as an empty map.");
                variant.MapValue.Clear();
            }

            return variant;
        }

        /// <summary>
        /// Renders a JSON token as plain text with invariant culture for numerics, so
        /// values round-trip regardless of the process culture (Newtonsoft preserves
        /// number types). Null tokens render as "" (callers get the type default).
        /// </summary>
        private static string TokenToInvariantString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token is JValue jValue && jValue.Value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : token.ToString();
        }

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
