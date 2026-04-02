using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    public class StoryFlowCharacterAsset : ScriptableObject
    {
        public string CharacterName;
        public string CharacterPath;
        public string ImageAssetKey;
        public Sprite ResolvedImage;
        public List<StoryFlowVariable> Variables = new();

        // Resolved asset references (asset key → Unity object)
        [SerializeField] public List<ResolvedAssetEntry> ResolvedAssetEntries = new();
        [NonSerialized] private Dictionary<string, UnityEngine.Object> _resolvedAssets;

        [Serializable]
        public class ResolvedAssetEntry
        {
            public string Key;
            public UnityEngine.Object Asset;
        }

        public Dictionary<string, UnityEngine.Object> ResolvedAssets
        {
            get
            {
                if (_resolvedAssets == null) RebuildResolvedAssets();
                return _resolvedAssets;
            }
        }

        private void OnEnable()
        {
            _resolvedAssets = null;
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
        /// Creates a deep copy of the character variables for runtime mutation.
        /// </summary>
        public Dictionary<string, StoryFlowVariant> CreateRuntimeVariables()
        {
            var runtimeVars = new Dictionary<string, StoryFlowVariant>();
            foreach (var v in Variables)
            {
                runtimeVars[v.Name] = new StoryFlowVariant(v.Value);
            }
            return runtimeVars;
        }

        /// <summary>
        /// Creates runtime character data from this asset definition.
        /// Deep-copies variables so runtime mutations do not affect the source asset.
        /// </summary>
        public StoryFlowCharacterData CreateRuntimeData()
        {
            var data = new StoryFlowCharacterData
            {
                Name = CharacterName,
                Image = ResolvedImage,
                ImageAssetKey = ImageAssetKey,
                Variables = new Dictionary<string, StoryFlowVariant>(),
                VariablesList = new List<StoryFlowVariable>()
            };

            foreach (var v in Variables)
            {
                var copy = new StoryFlowVariable(v);
                // Deserialize array from JSON if ArrayValue was lost during Unity serialization
                if (copy.IsArray && copy.Value.ArrayValue == null && !string.IsNullOrEmpty(copy.DefaultValueJson))
                {
                    copy.Value = StoryFlowVariant.DeserializeArrayFromJson(copy.Type, copy.DefaultValueJson);
                }
                data.VariablesList.Add(copy);
                data.Variables[copy.Name] = copy.Value;
            }

            return data;
        }
    }
}
