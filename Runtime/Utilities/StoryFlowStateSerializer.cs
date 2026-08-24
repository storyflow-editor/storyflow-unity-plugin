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

        /// <summary>
        /// Writes the unified state document.
        ///
        /// The Data Asset pair is OPTIONAL here and REQUIRED on StoryFlowSaveHelpers.Save /
        /// SaveAsync, which is not an inconsistency but the line between plumbing and public
        /// surface. This method has three callers, all inside this package, and its defaults
        /// exist so a caller with genuinely no store still writes a valid document. The save
        /// helpers are what a HOST calls, and a defaulted pair there would let a game ship
        /// saves that silently drop the whole .sfd session — so those name every caller.
        ///
        /// The SEED is needed alongside the overlay because the overlay's values are written
        /// BARE, and only the declaration can say whether a value is an array (contract §7 and
        /// the rule on <see cref="BareValueToJson"/>).
        /// </summary>
        public static string Serialize(
            Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions,
            Dictionary<string, StoryFlowDataAssetDef> dataAssetSeed = null,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> dataAssetOverlay = null)
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

            root["dataAssets"] = DataAssetOverlayToJson(dataAssetSeed, dataAssetOverlay);

            return root.ToString(Formatting.Indented);
        }

        // --- The Data Asset overlay: the sparse `dataAssets` key (contract §7) ---
        //
        // NORMATIVE SOURCE: the HTML runtime's runtime-data-assets.js snapshot()/restore(),
        // whose table this key is byte-shape-identical to — the first envelope section all
        // four runtimes share verbatim even though the documents around it differ.
        //
        // BARE values, not the typed {id,name,type,isArray,value} records VariableToJson
        // writes for globals and characters. The seed is schema-authoritative and always ships
        // with the game, so a save that pinned types would freeze content the author later
        // edited — and TryVariableValueFromJson, which reads those records, is not reusable
        // here for the same reason: there is no type field to read.

        /// <summary>
        /// The sparse overlay table: <c>{ assetId: { variableId: bare value } }</c>. ALWAYS
        /// PRESENT, <c>{}</c> when the session has written nothing — the reference's envelope
        /// convention, and what makes "the key is absent" mean "an older save" rather than
        /// "an untouched session".
        /// </summary>
        private static JObject DataAssetOverlayToJson(
            Dictionary<string, StoryFlowDataAssetDef> seed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> overlay)
        {
            var root = new JObject();
            if (overlay == null) { return root; }

            foreach (var assetEntry in overlay)
            {
                var assetObj = new JObject();
                foreach (var valueEntry in assetEntry.Value)
                {
                    var declaration = seed != null
                        ? StoryFlowDataAssetStore.FindDeclaration(seed, assetEntry.Key, valueEntry.Key)
                        : null;
                    assetObj[valueEntry.Key] = BareValueToJson(valueEntry.Value, declaration);
                }
                root[assetEntry.Key] = assetObj;
            }
            return root;
        }

        /// <summary>
        /// One overlay value as a BARE JSON value: a native scalar, an array of scalars, or a
        /// map's ordered <c>[{key, value}]</c> entry list.
        ///
        /// THE DECLARATION DECIDES array-vs-scalar, and it can say NO as well as yes.
        /// StoryFlowVariant's scalar setters do not clear ArrayValue (only SetMap does), so a
        /// variant that once held an array and was re-set as a scalar still carries the old
        /// elements — trusting "there are elements" over the declaration would persist that
        /// residue as a JSON array under a scalar declaration, and it would reload as a scalar,
        /// silently losing the value. No writer produces that state today; the rule costs
        /// nothing and does not depend on that staying true. The element count only answers for
        /// a value with NO declaration at all (deleted from the .sfd since the write), which is
        /// dropped on the way back in anyway, so the fallback only has to be harmless.
        ///
        /// A map is told by the variant's own TYPE rather than by MapValue being non-null:
        /// MapValue is [NonSerialized], so type is the half that always survives, and a
        /// Map-typed variant with no entries must still write <c>[]</c> rather than fall
        /// through to the scalar writer.
        /// </summary>
        private static JToken BareValueToJson(StoryFlowVariant variant, StoryFlowVariable declaration)
        {
            if (variant == null) { return JValue.CreateNull(); }

            if (variant.Type == StoryFlowVariableType.Map)
            {
                // The ordered entry list of §2.1 — the same shape the typed map path writes,
                // minus the keyType/valueType record around it. Entry ORDER is authored and
                // observable, so this walks the list in order.
                var entries = new JArray();
                foreach (var entry in variant.GetMap())
                {
                    entries.Add(new JObject
                    {
                        ["key"] = VariantToJson(entry.Key),
                        ["value"] = VariantToJson(entry.Value)
                    });
                }
                return entries;
            }

            bool isArray = declaration != null
                ? declaration.IsArray
                : variant.ArrayValue != null && variant.ArrayValue.Count > 0;
            if (isArray)
            {
                var elements = new JArray();
                foreach (var element in variant.GetArray())
                {
                    elements.Add(VariantToJson(element));
                }
                return elements;
            }

            return VariantToJson(variant);
        }

        /// <summary>
        /// One saved bare value back into a variant, TYPED FROM THE DECLARATION.
        ///
        /// The save carries no types, so the declaration is the only authority — the same rule
        /// BuildSeed's second pass applies to file overrides. Getting this wrong is invisible
        /// to a read (an enum and a string both answer GetString) and visible in the NEXT save,
        /// so a save -> load -> save cycle would stop being stable.
        ///
        /// Non-throwing on every shape: a token of the wrong kind produces the declared type's
        /// default rather than an exception, because this input arrives from a file.
        /// </summary>
        internal static StoryFlowVariant BareValueFromJson(JToken token, StoryFlowVariable declaration)
        {
            if (declaration.Type == StoryFlowVariableType.Map)
            {
                var entries = new List<StoryFlowMapEntry>();
                if (token is JArray entryArray)
                {
                    foreach (var entryToken in entryArray)
                    {
                        // An entry without a key is unaddressable — skip it, matching the
                        // importer and the typed reader above.
                        if (!(entryToken is JObject entryObj) || entryObj["key"] == null) { continue; }
                        entries.Add(new StoryFlowMapEntry
                        {
                            Key = VariantFromJson(entryObj["key"], declaration.KeyType),
                            Value = VariantFromJson(entryObj["value"], declaration.ValueType)
                        });
                    }
                }
                var map = new StoryFlowVariant();
                map.SetMap(entries);
                return map;
            }

            if (declaration.IsArray)
            {
                var list = new List<StoryFlowVariant>();
                if (token is JArray array)
                {
                    foreach (var element in array)
                    {
                        list.Add(VariantFromJson(element, declaration.Type));
                    }
                }
                // The ELEMENT TYPE is stated, never inferred: an emptied array carries nothing
                // to infer from, and a variant that reads back typed or untyped depending on
                // the last writer is a variant whose next save has a different shape.
                return new StoryFlowVariant { Type = declaration.Type, ArrayValue = list };
            }

            return VariantFromJson(token, declaration.Type);
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

            // The .sfd overlay comes through as RAW tokens: typing them needs the seed's
            // declarations, which this reader does not have. A key that is absent, or present
            // as anything other than an object, leaves the field NULL — which the manager
            // reads as "clear the overlay", not as "leave it alone" (see the field's doc).
            if (root["dataAssets"] is JObject dataAssets)
            {
                var table = new Dictionary<string, Dictionary<string, JToken>>();
                foreach (var assetProperty in dataAssets.Properties())
                {
                    if (!(assetProperty.Value is JObject assetObj)) { continue; }

                    var values = new Dictionary<string, JToken>();
                    foreach (var valueProperty in assetObj.Properties())
                    {
                        values[valueProperty.Name] = valueProperty.Value;
                    }
                    table[assetProperty.Name] = values;
                }
                snapshot.DataAssetValues = table;
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
