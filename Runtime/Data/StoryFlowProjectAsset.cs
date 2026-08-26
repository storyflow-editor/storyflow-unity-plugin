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

        /// <summary>
        /// SHA-256 of project.json, global-variables.json and characters.json together with
        /// the membership this asset actually ended up holding — startup script, script
        /// references, character references and the resolved-asset pool. Certifying the
        /// inputs alone would let an import whose media failed record "up to date" and skip
        /// the repair on every later sync. Written only after a successful save.
        /// </summary>
        [HideInInspector] public string ImportedSourceHash;

        [Header("Startup")]
        public StoryFlowScriptAsset StartupScript;

        [Header("Assets")]
        public List<ScriptReference> ScriptReferences = new();
        public List<CharacterReference> CharacterReferences = new();

        /// <summary>
        /// The project's imported .sfd Data Assets. A flat list rather than path-keyed
        /// references like scripts and characters: .sfd files are addressed by ASSET ID
        /// everywhere downstream — the pills, the resolver, the save key — so the id each
        /// asset already carries is the key, and <see cref="DataAssets"/> indexes by it.
        /// </summary>
        public List<StoryFlowDataAssetAsset> DataAssetReferences = new();

        /// <summary>
        /// The character id bridge from character-index.json (characters engine contract §3):
        /// character FILE id (da_ prefixed) → the character's record key, i.e. exactly a key
        /// of <see cref="Characters"/>. A serialized list rebuilt into a runtime dictionary,
        /// like <see cref="CharacterReferences"/>, because Unity cannot serialize a
        /// Dictionary. Values are normalized at IMPORT (the wire ships the exporter's
        /// lowercase-backslash record keys; this store's keys are lowercase-forward-slash),
        /// so lookups use them verbatim. Empty on a pre-P4 export, which is the whole
        /// fall-back-to-paths posture.
        /// </summary>
        public List<CharacterIdEntry> CharacterIdEntries = new();

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
        [NonSerialized] private Dictionary<string, StoryFlowDataAssetAsset> _dataAssets;
        [NonSerialized] private Dictionary<string, string> _characterIdBridge;

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
        public class CharacterIdEntry
        {
            public string Id;
            public string Path;
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

            // Map variables only (Type == StoryFlowVariableType.Map)
            public StoryFlowVariableType KeyType;
            public StoryFlowVariableType ValueType;
            public List<string> KeyEnumValues = new();
            public List<string> ValueEnumValues = new();
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
            _dataAssets = null;
            _characterIdBridge = null;
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
                    KeyType = entry.KeyType,
                    ValueType = entry.ValueType,
                    KeyEnumValues = entry.KeyEnumValues != null ? new List<string>(entry.KeyEnumValues) : new List<string>(),
                    ValueEnumValues = entry.ValueEnumValues != null ? new List<string>(entry.ValueEnumValues) : new List<string>(),
                    Value = entry.Type == StoryFlowVariableType.Map
                        ? StoryFlowVariant.DeserializeMapFromJson(entry.KeyType, entry.ValueType, entry.DefaultValueJson)
                        : entry.IsArray
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

        private void RebuildDataAssets()
        {
            _dataAssets = new Dictionary<string, StoryFlowDataAssetAsset>(DataAssetReferences.Count);
            foreach (var asset in DataAssetReferences)
            {
                if (asset != null && !string.IsNullOrEmpty(asset.Id))
                    _dataAssets[asset.Id] = asset;
            }
        }

        private void RebuildCharacterIdBridge()
        {
            _characterIdBridge = new Dictionary<string, string>(CharacterIdEntries.Count);
            foreach (var entry in CharacterIdEntries)
            {
                if (!string.IsNullOrEmpty(entry.Id) && !string.IsNullOrEmpty(entry.Path))
                    _characterIdBridge[entry.Id] = entry.Path;
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

        /// <summary>The project's Data Assets, keyed by asset id (the seed's key too).</summary>
        public Dictionary<string, StoryFlowDataAssetAsset> DataAssets
        {
            get
            {
                if (_dataAssets == null) RebuildDataAssets();
                return _dataAssets;
            }
        }

        /// <summary>
        /// The character id bridge: character file id → <see cref="Characters"/> key.
        /// Empty when the import saw no character-index.json (a pre-P4 export).
        /// </summary>
        public Dictionary<string, string> CharacterIdBridge
        {
            get
            {
                if (_characterIdBridge == null) RebuildCharacterIdBridge();
                return _characterIdBridge;
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

        public StoryFlowDataAssetAsset GetDataAsset(string assetId)
        {
            return assetId != null && DataAssets.TryGetValue(assetId, out var asset) ? asset : null;
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

        /// <summary>
        /// Replaces the character id entries and invalidates the runtime bridge. The importer
        /// assigns through this rather than the field because a re-import inside one editor
        /// session gets no OnEnable — a directly assigned list would leave the [NonSerialized]
        /// bridge serving the previous import's mapping.
        /// </summary>
        public void SetCharacterIdEntries(List<CharacterIdEntry> entries)
        {
            CharacterIdEntries = entries ?? new List<CharacterIdEntry>();
            _characterIdBridge = null;
        }

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

        /// <summary>
        /// Empties the resolved-asset pool and invalidates the runtime cache. Used by the
        /// importer to rebuild the pool from scratch each import. Clearing the serialized list
        /// alone would leave the [NonSerialized] cache stale until the next OnEnable.
        /// </summary>
        public void ClearResolvedAssets()
        {
            ResolvedAssetEntries.Clear();
            _resolvedAssets = null;
        }

        private static StoryFlowVariant DeserializeVariant(StoryFlowVariableType type, string json)
        {
            return StoryFlowVariant.DeserializeFromJson(type, json);
        }

        #endregion
    }
}
