namespace StoryFlow.Data
{
    /// <summary>
    /// Standardized handle string builders matching StoryFlow Editor format.
    /// Source handles: "source-{nodeId}-{suffix}"
    /// Target handles: "target-{nodeId}-{suffix}"
    /// </summary>
    public static class StoryFlowHandles
    {
        // Source output suffixes
        public const string Out_Default = "";
        public const string Out_True = "true";
        public const string Out_False = "false";
        public const string Out_Flow = "1";
        public const string Out_Output = "output";
        public const string Out_Boolean = "boolean-";
        public const string Out_Integer = "integer-";
        public const string Out_Float = "float-";
        public const string Out_String = "string-";
        public const string Out_Enum = "enum-";
        public const string Out_LoopBody = "loopBody";
        public const string Out_LoopCompleted = "completed";

        // Target input suffixes — single typed inputs
        public const string In_Default = "";
        public const string In_Boolean = "boolean";
        public const string In_Integer = "integer";
        public const string In_Float = "float";
        public const string In_String = "string";
        public const string In_Enum = "enum";
        public const string In_Image = "image";
        public const string In_Audio = "audio";
        public const string In_Character = "character";

        // Numbered inputs (binary operations: and, or, equal, arithmetic, etc.)
        public const string In_Boolean1 = "boolean-1";
        public const string In_Boolean2 = "boolean-2";
        public const string In_BooleanCondition = "boolean-condition";
        public const string In_Integer1 = "integer-1";
        public const string In_Integer2 = "integer-2";
        public const string In_IntegerIndex = "integer-index";
        public const string In_IntegerValue = "integer-value";
        public const string In_Float1 = "float-1";
        public const string In_Float2 = "float-2";
        public const string In_String1 = "string-1";
        public const string In_String2 = "string-2";
        public const string In_Enum1 = "enum-1";
        public const string In_Enum2 = "enum-2";

        // Array inputs
        public const string In_BoolArray = "boolean-array";
        public const string In_IntArray = "integer-array";
        public const string In_FloatArray = "float-array";
        public const string In_StringArray = "string-array";
        public const string In_ImageArray = "image-array";
        public const string In_CharacterArray = "character-array";
        public const string In_AudioArray = "audio-array";

        // Media node inputs
        public const string In_ImageInput = "image-image-input";
        public const string In_AudioInput = "audio-audio-input";
        public const string In_CharacterInput = "character-character-input";

        // Map handles — the key/value types are baked into the handle ID itself:
        //   Source: "source-{nodeId}-map-{keyType}-{valueType}"             (no optionId)
        //   Target: "target-{nodeId}-map-{keyType}-{valueType}-{optionId}"
        // Target optionIds: "1" (pure reads: getMapValue/hasMapKey/mapSize/mapKeys/
        // mapValues), "2" (setMap + mutators: setMapValue/removeMapKey/clearMap),
        // "map" (forEachMap), "input" (setCharacterVar's map input) — optionIds are
        // NOT always digits, so map handles must be resolved by EXACT match only.
        // FindInputEdge's prefix fallback already rejects multi-hyphen rests, so a
        // short suffix like "map" can never accidentally match these handles.

        /// <summary>Builds a map source handle suffix: "map-{keyType}-{valueType}".</summary>
        public static string OutMap(string keyType, string valueType)
        {
            return string.Concat("map-", keyType, "-", valueType);
        }

        /// <summary>Builds a map target handle suffix: "map-{keyType}-{valueType}-{optionId}".</summary>
        public static string InMap(string keyType, string valueType, string optionId)
        {
            return string.Concat("map-", keyType, "-", valueType, "-", optionId);
        }

        // Data Asset (.sfd) handles.
        //
        // The accessor's asset pin: "target-{nodeId}-dataAsset-asset". The wire IS the
        // binding (contract §2.2) — the accessor carries no assetId of its own, so this
        // edge, followed a SINGLE hop to a getDataAsset pill, is the whole lookup.
        public const string In_DataAsset = "dataAsset";
        public const string In_DataAssetArray = "dataAsset-array";
        public const string In_DataAssetRef = "dataAsset-asset";

        // The Set node's VALUE input is the editor's pin "2" (SetDataAssetVariableNode.tsx
        // is the source of truth; the "3" beside it is the pass-through output, never read
        // as an input). The three shapes below mirror the reference's readDataAssetSetInput
        // exactly: "{type}-2" scalar, "{type}-array-2" array, InMap(k, v, "2") map.
        public const string DataAssetValueOptionId = "2";

        /// <summary>Builds a Set Data Asset Variable scalar value input suffix: "{type}-2".</summary>
        public static string InDataAssetValue(string variableType)
        {
            return string.Concat(variableType, "-", DataAssetValueOptionId);
        }

        /// <summary>Builds a Set Data Asset Variable array value input suffix: "{type}-array-2".</summary>
        public static string InDataAssetArrayValue(string variableType)
        {
            return string.Concat(variableType, "-array-", DataAssetValueOptionId);
        }

        public static string Source(string nodeId, string suffix = "")
        {
            return string.Concat("source-", nodeId, "-", suffix);
        }

        public static string Target(string nodeId, string suffix = "")
        {
            return string.Concat("target-", nodeId, "-", suffix);
        }

        public static string SourceOption(string nodeId, string optionId)
        {
            return string.Concat("source-", nodeId, "-", optionId);
        }

        public static string SourceExit(string nodeId, string flowId)
        {
            return string.Concat("source-", nodeId, "-exit-", flowId);
        }

        public static string SourceTypedValue(string nodeId, string type, string optionId)
        {
            return string.Concat("source-", nodeId, "-", type, "-value-", optionId);
        }
    }
}
