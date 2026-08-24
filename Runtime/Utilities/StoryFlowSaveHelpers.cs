using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Utilities
{
    /// <summary>
    /// Slot file I/O for StoryFlow state, plus reading of the pre-unification Unity save
    /// dialect.
    ///
    /// Serialization itself lives in <see cref="StoryFlowStateSerializer"/>. This type only
    /// moves bytes and picks a dialect, so the slot API and the string API cannot drift.
    /// </summary>
    public static class StoryFlowSaveHelpers
    {
        /// <summary>Current save format version.</summary>
        public const string CurrentSaveVersion = StoryFlowStateSerializer.FormatVersion;

        private static string GetSavePath(string slotName)
        {
            var dir = Path.Combine(Application.persistentDataPath, "StoryFlow", "Saves");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{slotName}.json");
        }

        /// <summary>
        /// Writes state to a slot in the unified cross-engine format.
        ///
        /// Single funnel: slot files carry exactly the payload ExportState returns. Note
        /// this is NOT readable by plugin versions before the unification, which expected
        /// the legacy "1.0.0" envelope. Reading old saves still works, so upgrades are safe;
        /// downgrades are not.
        ///
        /// THE DATA ASSET PAIR IS REQUIRED HERE, and optional on
        /// <see cref="StoryFlowStateSerializer.Serialize"/> — deliberately, and the difference is
        /// the audience. This type is PUBLIC SURFACE a host calls; a defaulted pair would let a
        /// host write a save that silently drops the whole .sfd session and only notice a
        /// playthrough later, and the compiler naming every caller is the only thing that
        /// catches that. The serializer is internal plumbing with exactly three callers, all in
        /// this package, and its defaults exist so a caller with genuinely no store (the state
        /// tests, and any host that predates .sfd) can still produce a valid document. Pass
        /// nulls here to mean "no store", but pass them ON PURPOSE.
        /// </summary>
        public static void Save(string slotName, Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions,
            Dictionary<string, StoryFlowDataAssetDef> dataAssetSeed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> dataAssetOverlay)
        {
            var json = StoryFlowStateSerializer.Serialize(
                globalVariables, runtimeCharacters, usedOnceOnlyOptions, dataAssetSeed, dataAssetOverlay);
            File.WriteAllText(GetSavePath(slotName), json);
        }

        /// <summary>
        /// Reads a slot file and returns a snapshot, or null if the slot is missing,
        /// unreadable or malformed. Never throws: this path also serves ImportState, whose
        /// input arrives from outside the game.
        /// </summary>
        public static StoryFlowStateSnapshot Load(string slotName)
        {
            try
            {
                var path = GetSavePath(slotName);
                if (!File.Exists(path)) { return null; }
                return ReadSnapshot(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoryFlow] Load failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Parses either dialect into a snapshot. Returns null on unparseable input.
        ///
        /// Detection is STRUCTURAL: the legacy envelope has GlobalVariables as an ARRAY,
        /// where the unified format has globalVariables as an OBJECT. Do not sniff on the
        /// version field. The legacy key is "Version" and the unified key is "version",
        /// differing only in case, and Newtonsoft matches property names case-insensitively
        /// by default, so a version-based check would misfire.
        /// </summary>
        public static StoryFlowStateSnapshot ReadSnapshot(string json)
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

            if (root["GlobalVariables"] is JArray)
            {
                return ReadLegacySnapshot(root);
            }

            return StoryFlowStateSerializer.Deserialize(json);
        }

        /// <summary>
        /// Reads Unity's pre-unification dialect: a StoryFlowSaveData whose variables carry
        /// ValueJson strings and INTEGER type codes. Kept so existing local saves keep
        /// loading; deletable after a deprecation window.
        /// </summary>
        private static StoryFlowStateSnapshot ReadLegacySnapshot(JObject root)
        {
            StoryFlowSaveData legacy;
            try
            {
                legacy = root.ToObject<StoryFlowSaveData>();
            }
            catch (JsonException)
            {
                return null;
            }
            if (legacy == null) { return null; }

            var snapshot = new StoryFlowStateSnapshot();

            foreach (var saved in legacy.GlobalVariables)
            {
                if (saved?.Id == null) { continue; }
                snapshot.GlobalValues[saved.Id] = DeserializeSavedVariable(saved);
            }

            foreach (var savedChar in legacy.RuntimeCharacters)
            {
                if (savedChar?.Path == null) { continue; }
                var values = new Dictionary<string, StoryFlowVariant>();
                foreach (var saved in savedChar.Variables)
                {
                    // The snapshot keys character variables by NAME, matching the unified
                    // format. Legacy records carry both Id and Name.
                    if (string.IsNullOrEmpty(saved?.Name)) { continue; }
                    values[saved.Name] = DeserializeSavedVariable(saved);
                }
                snapshot.CharacterValues[savedChar.Path] = values;
            }

            snapshot.UsedOnceOnlyOptions.AddRange(legacy.UsedOnceOnlyOptions);
            return snapshot;
        }

        /// <summary>
        /// Rehydrates a legacy saved variable's value, honoring its Map type / IsArray flag.
        /// Only the legacy read path needs this; the unified format carries native JSON
        /// values and rehydrates in <see cref="StoryFlowStateSerializer"/>.
        /// </summary>
        public static StoryFlowVariant DeserializeSavedVariable(SavedVariable savedVariable)
        {
            // Tolerant map branch: a map record with absent/malformed ValueJson entry
            // data rehydrates as an empty map (never throws); absent KeyType/ValueType
            // fall back to the enum default, harmless for an empty entry list.
            if (savedVariable.Type == StoryFlowVariableType.Map)
            {
                return StoryFlowVariant.DeserializeMapFromJson(
                    savedVariable.KeyType, savedVariable.ValueType, savedVariable.ValueJson);
            }

            return savedVariable.IsArray
                ? StoryFlowVariant.DeserializeArrayFromJson(savedVariable.Type, savedVariable.ValueJson)
                : StoryFlowVariant.DeserializeFromJson(savedVariable.Type, savedVariable.ValueJson);
        }

        public static bool Exists(string slotName)
        {
            return File.Exists(GetSavePath(slotName));
        }

        public static void Delete(string slotName)
        {
            var path = GetSavePath(slotName);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Asynchronous variant of <see cref="Save"/> that uses non-blocking file I/O.
        /// Returns true if the save succeeded, false otherwise.
        ///
        /// The Data Asset pair is required for the same reason it is on <see cref="Save"/>, and
        /// more so: nothing in this package calls this one, so a host is its ONLY caller and
        /// there is no in-package call site whose correctness could stand in for theirs.
        /// </summary>
        public static async Task<bool> SaveAsync(string slotName,
            Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions,
            Dictionary<string, StoryFlowDataAssetDef> dataAssetSeed,
            Dictionary<string, Dictionary<string, StoryFlowVariant>> dataAssetOverlay)
        {
            try
            {
                var json = StoryFlowStateSerializer.Serialize(
                    globalVariables, runtimeCharacters, usedOnceOnlyOptions, dataAssetSeed, dataAssetOverlay);
                await File.WriteAllTextAsync(GetSavePath(slotName), json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryFlow] SaveAsync failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Asynchronous variant of <see cref="Load"/>. Returns null if the slot is missing,
        /// unreadable or malformed. Accepts both dialects, same as the synchronous path.
        /// </summary>
        public static async Task<StoryFlowStateSnapshot> LoadAsync(string slotName)
        {
            try
            {
                var path = GetSavePath(slotName);
                if (!File.Exists(path)) { return null; }
                return ReadSnapshot(await File.ReadAllTextAsync(path));
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryFlow] LoadAsync failed: " + e.Message);
                return null;
            }
        }
    }
}
