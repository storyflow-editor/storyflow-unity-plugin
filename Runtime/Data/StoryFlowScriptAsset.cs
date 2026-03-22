using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryFlow.Data
{
    [CreateAssetMenu(menuName = "StoryFlow/Script Asset", order = 2)]
    public class StoryFlowScriptAsset : ScriptableObject
    {
        public string ScriptPath;
        public string StartNodeId = "0";

        [SerializeField] private List<SerializedNode> serializedNodes = new();
        [SerializeField] private List<StoryFlowConnection> connections = new();
        [SerializeField] private List<SerializedVariable> serializedVariables = new();
        [SerializeField] private List<SerializedString> serializedStrings = new();
        [SerializeField] private List<SerializedAsset> serializedAssets = new();
        [SerializeField] private List<StoryFlowFlowDef> flows = new();

        // Runtime dictionaries (built from serialized lists)
        [NonSerialized] private Dictionary<string, StoryFlowNode> _nodes;
        [NonSerialized] private Dictionary<string, StoryFlowVariable> _variables;
        [NonSerialized] private Dictionary<string, string> _strings;
        [NonSerialized] private Dictionary<string, StoryFlowAsset> _assets;

        // Connection indices
        [NonSerialized] private Dictionary<string, StoryFlowConnection> _sourceHandleIndex;
        [NonSerialized] private Dictionary<string, List<StoryFlowConnection>> _sourceNodeIndex;
        [NonSerialized] private Dictionary<string, List<StoryFlowConnection>> _targetNodeIndex;
        [NonSerialized] private bool _indicesBuilt;

        // Resolved asset references (asset key → Unity object)
        [SerializeField] public List<ResolvedAssetEntry> ResolvedAssetEntries = new();
        [NonSerialized] private Dictionary<string, UnityEngine.Object> _resolvedAssets;

        #region Serialization Helpers

        [Serializable]
        public class ResolvedAssetEntry
        {
            public string Key;
            public UnityEngine.Object Asset;
        }

        [Serializable]
        public class SerializedNode
        {
            public string Id;
            public StoryFlowNodeType Type;
            public List<SerializedKV> Data = new();
        }

        [Serializable]
        public class SerializedKV
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        public class SerializedVariable
        {
            public string Id;
            public string Name;
            public StoryFlowVariableType Type;
            public string DefaultValueJson;
            public bool IsArray;
            public List<string> EnumValues = new();
            public bool IsInput;
            public bool IsOutput;
        }

        [Serializable]
        public class SerializedString
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        public class SerializedAsset
        {
            public string Id;
            public string Path;
            public string Type;
        }

        #endregion

        #region Public Properties

        public List<StoryFlowConnection> Connections => connections;
        public List<StoryFlowFlowDef> Flows => flows;

        public Dictionary<string, StoryFlowNode> Nodes
        {
            get
            {
                if (_nodes == null) RebuildRuntimeData();
                return _nodes;
            }
        }

        public Dictionary<string, StoryFlowVariable> Variables
        {
            get
            {
                if (_variables == null) RebuildRuntimeData();
                return _variables;
            }
        }

        public Dictionary<string, string> Strings
        {
            get
            {
                if (_strings == null) RebuildRuntimeData();
                return _strings;
            }
        }

        public Dictionary<string, StoryFlowAsset> Assets
        {
            get
            {
                if (_assets == null) RebuildRuntimeData();
                return _assets;
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

        #endregion

        #region Initialization

        private void OnEnable()
        {
            _indicesBuilt = false;
            _nodes = null;
            _resolvedAssets = null;
        }

        private void RebuildRuntimeData()
        {
            _nodes = new Dictionary<string, StoryFlowNode>(serializedNodes.Count);
            foreach (var sn in serializedNodes)
            {
                var node = new StoryFlowNode { Id = sn.Id, Type = sn.Type };
                foreach (var kv in sn.Data)
                    node.Data[kv.Key] = kv.Value;
                _nodes[sn.Id] = node;
            }

            _variables = new Dictionary<string, StoryFlowVariable>(serializedVariables.Count);
            foreach (var sv in serializedVariables)
            {
                var variable = new StoryFlowVariable
                {
                    Id = sv.Id,
                    Name = sv.Name,
                    Type = sv.Type,
                    IsArray = sv.IsArray,
                    EnumValues = sv.EnumValues != null ? new List<string>(sv.EnumValues) : new List<string>(),
                    IsInput = sv.IsInput,
                    IsOutput = sv.IsOutput,
                    Value = sv.IsArray
                        ? StoryFlowVariant.DeserializeArrayFromJson(sv.Type, sv.DefaultValueJson)
                        : DeserializeVariant(sv.Type, sv.DefaultValueJson)
                };
                _variables[sv.Id] = variable;
            }

            _strings = new Dictionary<string, string>(serializedStrings.Count);
            foreach (var ss in serializedStrings)
                _strings[ss.Key] = ss.Value;

            _assets = new Dictionary<string, StoryFlowAsset>(serializedAssets.Count);
            foreach (var sa in serializedAssets)
                _assets[sa.Id] = new StoryFlowAsset { Id = sa.Id, Path = sa.Path, Type = sa.Type };
        }

        public void BuildIndices()
        {
            if (_indicesBuilt) return;

            _sourceHandleIndex = new Dictionary<string, StoryFlowConnection>(connections.Count);
            _sourceNodeIndex = new Dictionary<string, List<StoryFlowConnection>>();
            _targetNodeIndex = new Dictionary<string, List<StoryFlowConnection>>();

            foreach (var conn in connections)
            {
                if (!string.IsNullOrEmpty(conn.SourceHandle))
                    _sourceHandleIndex[conn.SourceHandle] = conn;

                if (!_sourceNodeIndex.TryGetValue(conn.Source, out var sourceList))
                {
                    sourceList = new List<StoryFlowConnection>();
                    _sourceNodeIndex[conn.Source] = sourceList;
                }
                sourceList.Add(conn);

                if (!_targetNodeIndex.TryGetValue(conn.Target, out var targetList))
                {
                    targetList = new List<StoryFlowConnection>();
                    _targetNodeIndex[conn.Target] = targetList;
                }
                targetList.Add(conn);
            }

            _indicesBuilt = true;
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

        #region Lookup Methods

        public StoryFlowNode GetNode(string id)
        {
            return Nodes.TryGetValue(id, out var node) ? node : null;
        }

        public string GetString(string key)
        {
            return Strings.TryGetValue(key, out var value) ? value : null;
        }

        public StoryFlowVariable GetVariable(string id)
        {
            return Variables.TryGetValue(id, out var variable) ? variable : null;
        }

        public StoryFlowConnection FindEdgeBySourceHandle(string sourceHandle)
        {
            BuildIndices();
            return _sourceHandleIndex.TryGetValue(sourceHandle, out var conn) ? conn : null;
        }

        public List<StoryFlowConnection> GetEdgesFromSource(string nodeId)
        {
            BuildIndices();
            return _sourceNodeIndex.TryGetValue(nodeId, out var list) ? list : new List<StoryFlowConnection>();
        }

        public List<StoryFlowConnection> GetEdgesByTarget(string nodeId)
        {
            BuildIndices();
            return _targetNodeIndex.TryGetValue(nodeId, out var list) ? list : new List<StoryFlowConnection>();
        }

        public StoryFlowConnection FindInputEdge(string nodeId, string targetHandleSuffix)
        {
            BuildIndices();
            var targetHandle = StoryFlowHandles.Target(nodeId, targetHandleSuffix);
            if (_targetNodeIndex.TryGetValue(nodeId, out var edges))
            {
                // Try exact match first
                foreach (var edge in edges)
                {
                    if (edge.TargetHandle == targetHandle)
                        return edge;
                }

                // Fallback: prefix match for handles with trailing option ID.
                // The editor appends a numbered suffix to handles (e.g., "string-2", "string-array-1")
                // while the runtime constants omit it (e.g., "string", "string-array").
                var prefix = targetHandle + "-";
                foreach (var edge in edges)
                {
                    if (edge.TargetHandle != null && edge.TargetHandle.StartsWith(prefix))
                        return edge;
                }
            }
            return null;
        }

        #endregion

        #region Setters (Used by Importer)

        public void SetNodes(List<SerializedNode> nodes) => serializedNodes = nodes;
        public void SetConnections(List<StoryFlowConnection> conns) => connections = conns;
        public void SetVariables(List<SerializedVariable> vars) => serializedVariables = vars;
        public void SetStrings(List<SerializedString> strings) => serializedStrings = strings;
        public void SetAssets(List<SerializedAsset> assets) => serializedAssets = assets;
        public void SetFlows(List<StoryFlowFlowDef> flowDefs) => flows = flowDefs;

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

        #endregion

        #region Helpers

        private static StoryFlowVariant DeserializeVariant(StoryFlowVariableType type, string json)
        {
            return StoryFlowVariant.DeserializeFromJson(type, json);
        }

        #endregion
    }
}
