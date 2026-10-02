using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    public class StoryFlowProjectAsset : ScriptableObject
    {
        public const int DefaultMaxScriptNesting = 20;

        [Header("Metadata")]
        public string Version;
        public string ApiVersion;
        public string Title;
        public string Description;

        /// <summary>Maximum nested RunScript calls. Legacy projects default to 20.</summary>
        public int MaxScriptNesting = DefaultMaxScriptNesting;

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
        /// data-assets.json's localization version. Version 2 keys authored overrides as
        /// well as declarations; older exports localize declarations only.
        /// </summary>
        public int DataAssetLocalizationVersion = 1;

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

        /// <summary>
        /// THE FILE-PRESENCE MARKER (localization spec §9): true when this project was
        /// imported from a build that carried a localization.json beside its artifacts.
        ///
        /// A bool and not an "are there any tables" test ON PURPOSE. An absent sidecar and a
        /// sidecar carrying no rows are the same empty list once they are in C#, and only one
        /// of them is a pre-localization export. The contract branches on the FILE EXISTING,
        /// never on a key count: an author who registered a language and translated nothing
        /// still ships full tables of source text, and that is a localized project. False here
        /// means source-only and ZERO behavior change — every lookup falls straight through to
        /// the artifact tables it always used.
        /// </summary>
        public bool HasLocalization;

        /// <summary>
        /// The language the documents are AUTHORED in, and therefore the language the
        /// artifacts' own strings blocks are keyed by — the second tier of the lookup. "en"
        /// without a sidecar, which is exactly what every pre-localization export's strings
        /// block carries.
        /// </summary>
        public string SourceLanguage = "en";

        /// <summary>
        /// The project's TARGET languages, in the author's registry order (the order a picker
        /// draws). Never includes the source language, which has no table of its own.
        /// </summary>
        public List<LanguageEntry> LanguageEntries = new();

        /// <summary>
        /// The sidecar's per-language tables, flattened to one entry per (language, id) because
        /// Unity cannot serialize a nested dictionary — rebuilt into <see cref="LanguageStrings"/>
        /// exactly like <see cref="CharacterIdEntries"/> is rebuilt into the id bridge.
        ///
        /// FULL AND PRE-RESOLVED is the whole engine contract (§9): the export already applied
        /// every fallback rule — an outdated row ships the OLD translation, an untranslated or
        /// cleared one ships the source text, an orphan has no row at all — so this plugin
        /// computes NO status, compares NO hash, and holds no rule beyond the lookup ladder in
        /// StoryFlowExecutionContext.LookUpLocalizedIn.
        ///
        /// These ids are the ids that KEYED an engine artifact. .sfui widget and dropdown
        /// strings have no rows here and never will: .sfui documents do not reach a plugin at
        /// all and their text localizes in the HTML lane. Their absence is the contract, not a
        /// missing feature.
        /// </summary>
        public List<LanguageStringEntry> LanguageStringEntries = new();

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
        [NonSerialized] private Dictionary<string, Dictionary<string, string>> _languageStrings;

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

        /// <summary>
        /// One TARGET language of the localization sidecar (§9): the code its table is keyed
        /// by, and the display label the author registered for it.
        ///
        /// The SOURCE language is not one of these. It is a code with no table at all — the
        /// artifacts themselves carry the source text — so it appears in
        /// StoryFlowManager.GetLanguages as a row whose Name is its Code, exactly as the HTML
        /// runtime's getLanguages builds it.
        /// </summary>
        [Serializable]
        public class LanguageEntry
        {
            /// <summary>The language code, and the key of this language's table ("fr", "es").</summary>
            public string Code;

            /// <summary>The display label the author registered ("French"), for a game's own picker.</summary>
            public string Name;
        }

        /// <summary>One row of one language's table: the id, its text, and which language it is in.</summary>
        [Serializable]
        public class LanguageStringEntry
        {
            public string Language;
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
            _languageStrings = null;
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
                if (cr.Asset != null && !string.IsNullOrEmpty(cr.Path))
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

        private void RebuildLanguageStrings()
        {
            _languageStrings = new Dictionary<string, Dictionary<string, string>>();
            foreach (var entry in LanguageStringEntries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Language) || string.IsNullOrEmpty(entry.Key))
                    continue;
                if (!_languageStrings.TryGetValue(entry.Language, out var table))
                {
                    table = new Dictionary<string, string>();
                    _languageStrings[entry.Language] = table;
                }
                table[entry.Key] = entry.Value;
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

        /// <summary>
        /// `language code` → that language's FULL, PRE-RESOLVED table, straight from the
        /// sidecar. Empty for a project with no localization.json.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> LanguageStrings
        {
            get
            {
                if (_languageStrings == null) RebuildLanguageStrings();
                return _languageStrings;
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

        /// <summary>
        /// THE OVERLAY TIER (localization spec §9), and the first step of every string lookup
        /// in this plugin: the sidecar's row for <paramref name="key"/> in
        /// <paramref name="languageCode"/>, or null when there is none.
        ///
        /// Null covers all four ways a row can be absent — no sidecar, an unknown code, the
        /// SOURCE language (which has no table by construction), and an id this table does not
        /// carry — and every one of them means the same thing to a caller: fall through to the
        /// artifact's own source table. THE EMPTY-TEXT GUARD LIVES HERE AND NOWHERE ELSE: an
        /// empty row reads as absent, because the export already turned a cleared translation
        /// back into source text and this is the second net for a hand-edited sidecar.
        /// </summary>
        public string FindLocalizedString(string key, string languageCode)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(languageCode)) return null;
            if (!LanguageStrings.TryGetValue(languageCode, out var table)) return null;
            if (!table.TryGetValue(key, out var text)) return null;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// The code this project actually carries that matches <paramref name="code"/>, or
        /// EMPTY when it carries none.
        ///
        /// Case-INSENSITIVE with the REGISTERED casing winning, matching the HTML runtime's
        /// resolveLanguage and the store's own rule that a code IS a file name: "ES" and "es"
        /// are one language, and the canonical form is the one the tables are keyed by. The
        /// SOURCE language matches too — running in the authored language is a legitimate
        /// choice, it simply has no table. A project with no sidecar therefore matches its
        /// source language and nothing else.
        /// </summary>
        public string ResolveLanguageCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            if (!string.IsNullOrEmpty(SourceLanguage) &&
                string.Equals(SourceLanguage, code, StringComparison.OrdinalIgnoreCase))
                return SourceLanguage;
            foreach (var language in LanguageEntries)
            {
                if (language != null && !string.IsNullOrEmpty(language.Code) &&
                    string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
                    return language.Code;
            }
            return "";
        }

        public List<string> GetAllScriptPaths()
        {
            return new List<string>(Scripts.Keys);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Replaces the script references and invalidates the runtime dictionary. Same rule as
        /// <see cref="SetCharacterReferences"/>: a re-import inside one editor session gets no
        /// OnEnable, so a directly assigned list would leave the [NonSerialized] dictionary
        /// serving the previous import's script assets.
        /// </summary>
        public void SetScriptReferences(List<ScriptReference> references)
        {
            ScriptReferences = references ?? new List<ScriptReference>();
            _scripts = null;
        }

        /// <summary>
        /// Replaces the global variable declarations and invalidates the runtime dictionary.
        /// Same rule as <see cref="SetCharacterReferences"/> — a same-session re-import would
        /// otherwise keep answering with the previous import's globals.
        /// </summary>
        public void SetGlobalVariableEntries(List<GlobalVariableEntry> entries)
        {
            GlobalVariableEntries = entries ?? new List<GlobalVariableEntry>();
            _globalVariables = null;
        }

        /// <summary>
        /// Replaces the project's global string table and invalidates the runtime dictionary.
        /// Same rule as <see cref="SetCharacterReferences"/>, and the one with the most visible
        /// failure: this table is the SOURCE text every un-translated lookup falls through to, so
        /// a stale copy makes a same-session re-import keep rendering the previous import's lines
        /// while the editor shows the new ones.
        /// </summary>
        public void SetGlobalStringEntries(List<GlobalStringEntry> entries)
        {
            GlobalStringEntries = entries ?? new List<GlobalStringEntry>();
            _globalStrings = null;
        }

        /// <summary>
        /// Replaces the character references and invalidates the runtime dictionary. The
        /// importer assigns through this rather than the field because a re-import inside one
        /// editor session gets no OnEnable — a directly assigned list would leave the
        /// [NonSerialized] dictionary serving the previous import's characters.
        /// </summary>
        public void SetCharacterReferences(List<CharacterReference> references)
        {
            CharacterReferences = references ?? new List<CharacterReference>();
            _characters = null;
        }

        /// <summary>
        /// Replaces the data asset references and invalidates the runtime dictionary. The
        /// importer assigns through this rather than the field because a re-import inside one
        /// editor session gets no OnEnable — a directly assigned list would leave the
        /// [NonSerialized] dictionary serving the previous import's data assets.
        /// </summary>
        public void SetDataAssetReferences(List<StoryFlowDataAssetAsset> references)
        {
            DataAssetReferences = references ?? new List<StoryFlowDataAssetAsset>();
            _dataAssets = null;
        }

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

        /// <summary>
        /// Replaces the whole localization block and invalidates the runtime tables. The
        /// importer assigns through this rather than the fields because a re-import inside one
        /// editor session gets no OnEnable — a directly assigned list would leave the
        /// [NonSerialized] table serving the previous import's translations, which is the same
        /// stale-bridge hazard <see cref="SetCharacterIdEntries"/> exists to prevent.
        ///
        /// The four move TOGETHER and are REPLACED, never appended to: a re-import of a build
        /// that dropped its sidecar must leave a source-only project, not a stale claim to be
        /// localized, and a second import in one session must not stack a second copy of every
        /// row on top of the first.
        /// </summary>
        public void SetLocalization(bool hasLocalization, string sourceLanguage,
            List<LanguageEntry> languages, List<LanguageStringEntry> strings)
        {
            HasLocalization = hasLocalization;
            SourceLanguage = string.IsNullOrEmpty(sourceLanguage) ? "en" : sourceLanguage;
            LanguageEntries = languages ?? new List<LanguageEntry>();
            LanguageStringEntries = strings ?? new List<LanguageStringEntry>();
            _languageStrings = null;
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
