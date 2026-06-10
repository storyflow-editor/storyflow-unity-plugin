using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Utilities
{
    public static class StoryFlowSaveHelpers
    {
        /// <summary>Current save format version.</summary>
        public const string CurrentSaveVersion = "1.0.0";

        private static string GetSavePath(string slotName)
        {
            var dir = Path.Combine(Application.persistentDataPath, "StoryFlow", "Saves");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{slotName}.json");
        }

        public static void Save(string slotName, Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions)
        {
            var saveData = new StoryFlowSaveData();
            saveData.Version = CurrentSaveVersion;

            // Serialize global variables
            foreach (var kvp in globalVariables)
            {
                saveData.GlobalVariables.Add(ToSavedVariable(kvp.Value));
            }

            // Serialize runtime characters
            foreach (var kvp in runtimeCharacters)
            {
                var savedChar = new SavedCharacter { Path = kvp.Key };
                foreach (var v in kvp.Value.VariablesList)
                {
                    savedChar.Variables.Add(ToSavedVariable(v));
                }
                saveData.RuntimeCharacters.Add(savedChar);
            }

            // Serialize once-only options
            saveData.UsedOnceOnlyOptions.AddRange(usedOnceOnlyOptions);

            var json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
            File.WriteAllText(GetSavePath(slotName), json);
        }

        /// <summary>
        /// Converts a runtime variable to its saved form. Array values are serialized
        /// as JSON arrays (StoryFlowVariant.ToString cannot represent them), scalars
        /// keep their plain-text form for compatibility with existing saves.
        /// </summary>
        private static SavedVariable ToSavedVariable(StoryFlowVariable variable)
        {
            return new SavedVariable
            {
                Id = variable.Id,
                Name = variable.Name,
                Type = variable.Type,
                ValueJson = variable.IsArray
                    ? variable.Value.SerializeArrayToJson()
                    : variable.Value.ToString(),
                IsArray = variable.IsArray
            };
        }

        /// <summary>
        /// Rehydrates a saved variable's value, honoring its IsArray flag.
        /// Counterpart of <see cref="ToSavedVariable"/>.
        /// </summary>
        public static StoryFlowVariant DeserializeSavedVariable(SavedVariable savedVariable)
        {
            return savedVariable.IsArray
                ? StoryFlowVariant.DeserializeArrayFromJson(savedVariable.Type, savedVariable.ValueJson)
                : StoryFlowVariant.DeserializeFromJson(savedVariable.Type, savedVariable.ValueJson);
        }

        public static StoryFlowSaveData Load(string slotName)
        {
            var path = GetSavePath(slotName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var saveData = JsonConvert.DeserializeObject<StoryFlowSaveData>(json);
            if (saveData == null) return null;

            // Version compatibility check
            if (!string.IsNullOrEmpty(saveData.Version) && saveData.Version != CurrentSaveVersion)
            {
                Debug.LogWarning($"[StoryFlow] Save data version mismatch: file is v{saveData.Version}, current is v{CurrentSaveVersion}. " +
                                 "Data will be loaded but may not be fully compatible.");
                saveData = MigrateSaveData(saveData);
            }

            return saveData;
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
        /// </summary>
        public static async Task<bool> SaveAsync(string slotName,
            Dictionary<string, StoryFlowVariable> globalVariables,
            Dictionary<string, StoryFlowCharacterData> runtimeCharacters,
            HashSet<string> usedOnceOnlyOptions)
        {
            try
            {
                var saveData = new StoryFlowSaveData();
                saveData.Version = CurrentSaveVersion;

                // Serialize global variables
                foreach (var kvp in globalVariables)
                {
                    saveData.GlobalVariables.Add(ToSavedVariable(kvp.Value));
                }

                // Serialize runtime characters
                foreach (var kvp in runtimeCharacters)
                {
                    var savedChar = new SavedCharacter { Path = kvp.Key };
                    foreach (var v in kvp.Value.VariablesList)
                    {
                        savedChar.Variables.Add(ToSavedVariable(v));
                    }
                    saveData.RuntimeCharacters.Add(savedChar);
                }

                // Serialize once-only options
                saveData.UsedOnceOnlyOptions.AddRange(usedOnceOnlyOptions);

                var json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
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
        /// Asynchronous variant of <see cref="Load"/> that uses non-blocking file I/O.
        /// Returns the deserialized save data, or null if the file does not exist or an error occurs.
        /// </summary>
        public static async Task<StoryFlowSaveData> LoadAsync(string slotName)
        {
            try
            {
                var path = GetSavePath(slotName);
                if (!File.Exists(path)) return null;
                var json = await File.ReadAllTextAsync(path);
                var saveData = JsonConvert.DeserializeObject<StoryFlowSaveData>(json);
                if (saveData == null) return null;

                // Version compatibility check
                if (!string.IsNullOrEmpty(saveData.Version) && saveData.Version != CurrentSaveVersion)
                {
                    Debug.LogWarning($"[StoryFlow] Save data version mismatch: file is v{saveData.Version}, current is v{CurrentSaveVersion}. " +
                                     "Data will be loaded but may not be fully compatible.");
                    saveData = MigrateSaveData(saveData);
                }

                return saveData;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryFlow] LoadAsync failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Migrates save data from an older version to the current version.
        /// Add migration logic here as the save format evolves.
        /// </summary>
        private static StoryFlowSaveData MigrateSaveData(StoryFlowSaveData data)
        {
            // Future migration logic goes here.
            // Example:
            // if (data.Version == "1.0.0") { /* migrate to 1.1.0 */ data.Version = "1.1.0"; }
            // if (data.Version == "1.1.0") { /* migrate to 1.2.0 */ data.Version = "1.2.0"; }

            data.Version = CurrentSaveVersion;
            return data;
        }
    }
}
