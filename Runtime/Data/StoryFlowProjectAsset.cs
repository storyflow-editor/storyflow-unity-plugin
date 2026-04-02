using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    public class StoryFlowProjectAsset : ScriptableObject
    {
        [Header("Metadata")]
        public string Version;
        public string ApiVersion;
        public string Title;
        public string Description;

        [Header("Startup")]
        public StoryFlowScriptAsset StartupScript;

        [Header("Assets")]
        public List<ScriptReference> ScriptReferences = new();
        public List<CharacterReference> CharacterReferences = new();
        public List<GlobalVariableEntry> GlobalVariableEntries = new();
        public List<GlobalStringEntry> GlobalStringEntries = new();

        // Resolved asset references (asset key → Unity object)
        [SerializeField] public List<ResolvedAssetEntry> ResolvedAssetEntries = new();
        [NonSerialized] private Dictionary<string, UnityEngine.Object> _resolvedAssets;

        // Runtime dictionaries (built from serialized lists)
        [NonSerialized] private Dictionary<string, StoryFlowScriptAsset> _scripts;
        [NonSerialized] private Dictionary<string, StoryFlowVariable> _globalVariables;
        [NonSerialized] private Dictionary<string, StoryFlowCharacterAsset> _characters;
        [NonSerialized] private Dictionary<string, string> _globalStrings;

        [Serializable]
        public class ResolvedAssetEntry
        {
            public string Key;
            public UnityEngine.Object Asset;
        }

        [Serializable]
        public class ScriptReference
        {
            public string Path;
            public StoryFlowScriptAsset Asset;
        }

        [Serializable]
        public class CharacterReference
        {
            public string Path;
            public StoryFlowCharacterAsset Asset;
        }

        [Serializable]
        public class GlobalVariableEntry
        {
            public string Id;
            public string Name;
            public StoryFlowVariableType Type;
            public string DefaultValueJson;
            public bool IsArray;
            public List<string> EnumValues = new();
        }

        [Serializable]
        public class GlobalStringEntry
        {
            public string Key;
            public string Value;
        }

        #region Initialization

        private void OnEnable()
        {
            _scripts = null;
            _globalVariables = null;
            _characters = null;
            _globalStrings = null;
            _resolvedAssets = null;
        }

        private void RebuildScripts()
        {
            _scripts = new Dictionary<string, StoryFlowScriptAsset>(ScriptReferences.Count);
            foreach (var sr in ScriptReferences)
            {
                if (sr.Asset != null)
                    _scripts[sr.Path] = sr.Asset;
            }
        }

        private void RebuildGlobalVariables()
        {
            _globalVariables = new Dictionary<string, StoryFlowVariable>(GlobalVariableEntries.Count);
            foreach (var entry in GlobalVariableEntries)
            {
                var variable = new StoryFlowVariable
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Type = entry.Type,
                    IsArray = entry.IsArray,
                    EnumValues = entry.EnumValues != null ? new List<string>(entry.EnumValues) : new List<string>(),
                    Value = entry.IsArray
                        ? StoryFlowVariant.DeserializeArrayFromJson(entry.Type, entry.DefaultValueJson)
                        : DeserializeVariant(entry.Type, entry.DefaultValueJson)
                };
                _globalVariables[entry.Id] = variable;
            }
        }

        private void RebuildCharacters()
        {
            _characters = new Dictionary<string, StoryFlowCharacterAsset>(CharacterReferences.Count);
            foreach (var cr in CharacterReferences)
            {
                if (cr.Asset != null)
                    _characters[cr.Path] = cr.Asset;
            }
        }

        private void RebuildGlobalStrings()
        {
            _globalStrings = new Dictionary<string, string>(GlobalStringEntries.Count);
            foreach (var entry in GlobalStringEntries)
                _globalStrings[entry.Key] = entry.Value;
        }

        private void RebuildResolvedAssets()
        {
            _resolvedAssets = new Dictionary<string, UnityEngine.Object>(ResolvedAssetEntries.Count);
            foreach (var entry in ResolvedAssetEntries)
            {
                if (entry.Asset != null)
                    _resolvedAssets[entry.Key] = entry.Asset;
            }
        }

        #endregion

        #region Public API

        public Dictionary<string, StoryFlowScriptAsset> Scripts
        {
            get
            {
                if (_scripts == null) RebuildScripts();
                return _scripts;
            }
        }

        public Dictionary<string, StoryFlowVariable> GlobalVariables
        {
            get
            {
                if (_globalVariables == null) RebuildGlobalVariables();
                return _globalVariables;
            }
        }

        public Dictionary<string, StoryFlowCharacterAsset> Characters
        {
            get
            {
                if (_characters == null) RebuildCharacters();
                return _characters;
            }
        }

        public Dictionary<string, string> GlobalStrings
        {
            get
            {
                if (_globalStrings == null) RebuildGlobalStrings();
                return _globalStrings;
            }
        }

        public Dictionary<string, UnityEngine.Object> ResolvedAssets
        {
            get
            {
                if (_resolvedAssets == null) RebuildResolvedAssets();
                return _resolvedAssets;
            }
        }

        public StoryFlowScriptAsset GetStartupScriptAsset()
        {
            return StartupScript;
        }

        public StoryFlowScriptAsset GetScriptByPath(string path)
        {
            return Scripts.TryGetValue(path, out var asset) ? asset : null;
        }

        public StoryFlowVariable GetGlobalVariable(string id)
        {
            return GlobalVariables.TryGetValue(id, out var variable) ? variable : null;
        }

        public StoryFlowCharacterAsset GetCharacterAsset(string normalizedPath)
        {
            return Characters.TryGetValue(normalizedPath, out var asset) ? asset : null;
        }

        public string GetGlobalString(string key)
        {
            return GlobalStrings.TryGetValue(key, out var value) ? value : null;
        }

        public List<string> GetAllScriptPaths()
        {
            return new List<string>(Scripts.Keys);
        }

        #endregion

        #region Helpers

        public void SetResolvedAsset(string key, UnityEngine.Object asset)
        {
            for (int i = 0; i < ResolvedAssetEntries.Count; i++)
            {
                if (ResolvedAssetEntries[i].Key == key)
                {
                    ResolvedAssetEntries[i].Asset = asset;
                    _resolvedAssets = null;
                    return;
                }
            }
            ResolvedAssetEntries.Add(new ResolvedAssetEntry { Key = key, Asset = asset });
            _resolvedAssets = null;
        }

        private static StoryFlowVariant DeserializeVariant(StoryFlowVariableType type, string json)
        {
            return StoryFlowVariant.DeserializeFromJson(type, json);
        }

        #endregion
    }
}
